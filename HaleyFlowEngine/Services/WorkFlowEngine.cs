using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    public sealed class WorkFlowEngine : IWorkFlowEngine {
        private readonly IWorkFlowDAL _dal;
        private readonly WorkFlowEngineOptions _opt;
        private readonly IReadOnlyList<long> _monitorConsumers;
        public IStateMachine StateMachine { get; }
        public IBlueprintManager BlueprintManager { get; }
        public IBlueprintImporter BlueprintImporter { get; }
        public IPolicyEnforcer PolicyEnforcer { get; }
        public IAckManager AckManager { get; }
        public IRuntimeEngine Runtime { get; }
        public ILifeCycleMonitor Monitor { get; }
        public IWorkFlowDAL Dal { get { return _dal; } }

        public event Func<ILifeCycleEvent, Task>? EventRaised;
        public event Func<LifeCycleNotice, Task>? NoticeRaised;

        public WorkFlowEngine(IWorkFlowDAL dal, WorkFlowEngineOptions? options = null) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _opt = options ?? new WorkFlowEngineOptions();

            BlueprintManager = _opt.BlueprintManager ?? new BlueprintManager(_dal);

            // If you don't want engine to auto-create importer, pass it via options.
            BlueprintImporter = _opt.BlueprintImporter ?? new BlueprintImporter(_dal);

            StateMachine = _opt.StateMachine ?? new StateMachine(_dal, BlueprintManager);
            PolicyEnforcer = _opt.PolicyEnforcer ?? new PolicyEnforcer(_dal);

            var tc = _opt.ResolveTransitionConsumers;
            var hc = _opt.ResolveHookConsumers;
            Func<long, long, CancellationToken, Task<IReadOnlyList<long>>> transitionConsumers = (dv, iid, ct) => Task.FromResult<IReadOnlyList<long>>(tc?.Invoke(dv, iid) ?? Array.Empty<long>());
            Func<long, long, string, CancellationToken, Task<IReadOnlyList<long>>> hookConsumers = (dv, iid, code, ct) => Task.FromResult<IReadOnlyList<long>>(hc?.Invoke(dv, iid, code) ?? Array.Empty<long>());


            AckManager = _opt.AckManager ?? new AckManager(_dal, transitionConsumers, hookConsumers,_opt.AckPendingResendAfter,_opt.AckDeliveredResendAfter);

            Runtime = _opt.RuntimeEngine ?? new RuntimeEngine(_dal);

            _monitorConsumers = _opt.MonitorConsumers;
            Monitor = new LifeCycleMonitor(_opt.MonitorInterval, ct => RunMonitorOnceAsync(ct), (ex) => FireNotice(LifeCycleNotice.Error("MONITOR_ERROR", "MONITOR_ERROR", ex.Message, ex)));
        }

        public Task StartMonitorAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Monitor.StartAsync(ct); }

        public Task StopMonitorAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Monitor.StopAsync(ct); }

        public async ValueTask DisposeAsync() { try { await StopMonitorAsync(CancellationToken.None); } catch { } await Monitor.DisposeAsync(); await _dal.DisposeAsync(); }

        public async Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (string.IsNullOrWhiteSpace(req.DefName)) throw new ArgumentNullException(nameof(req.DefName));
            if (string.IsNullOrWhiteSpace(req.ExternalRef)) throw new ArgumentNullException(nameof(req.ExternalRef));
            if (string.IsNullOrWhiteSpace(req.Event)) throw new ArgumentNullException(nameof(req.Event));

            // Blueprint read can be outside txn (pure read + cached).
            var bp = await BlueprintManager.GetBlueprintLatestAsync(req.EnvCode, req.DefName, ct);

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;

            var toDispatch = new List<ILifeCycleEvent>(8);
            var lcAckGuids = new List<string>(4);
            var hookAckGuids = new List<string>(8);

            DbRow instance = null!;
            ApplyTransitionResult transition = null!;
            PolicyResolution pr = null!;

            try {
                //Before generating the instance, check if policy is needed to be attached. This can be found out from definition version and it's latest policy.. (we always focus on the latest policy for the definition)
                //But once the policy is attached to the instance, it won't change for that instance. That is why it is very important to get the policy id at this stage.. If we are creating instance for first time, we attach whatever is latest at this moment. later on, even if policy changes, that won't affect existing instances.
                var policy = await PolicyEnforcer.ResolvePolicyAsync(bp.DefinitionId, load); 
                instance = await StateMachine.EnsureInstanceAsync(bp.DefVersionId, req.ExternalRef, policy.PolicyId ?? 0, load);
                transition = await StateMachine.ApplyTransitionAsync(bp, instance, req.Event, req.RequestId, req.Actor, req.Payload, load);

                var result = new LifeCycleTriggerResult {
                    Applied = transition.Applied,
                    InstanceGuid = instance.GetString("guid") ?? string.Empty,
                    InstanceId = instance.GetLong("id"),
                    LifeCycleId = transition.LifeCycleId,
                    FromState = bp.StatesById.TryGetValue(transition.FromStateId, out var fs) ? (fs.Name ?? string.Empty) : string.Empty,
                    ToState = bp.StatesById.TryGetValue(transition.ToStateId, out var ts) ? (ts.Name ?? string.Empty) : string.Empty,
                    Reason = transition.Reason ?? string.Empty,
                    LifecycleAckGuids = Array.Empty<string>(),
                    HookAckGuids = Array.Empty<string>()
                };

                // Even if transition not applied, instance may have been created/ensured -> commit that.
                //Meaning, our goal was only to create an instance or just to ensure this exists.. not to make any transition at all. Like this is the first step.
                if (!transition.Applied) {
                    transaction.Commit(); 
                    committed = true;
                    return result;
                }

                var instanceId = result.InstanceId;
                // See.. We have the instance created or ensured above. Now, we need to make sure that the policy is also resolved and sent back to the caller. Because, caller might need to know which policy is attached to this instance.
                // We should never take the latest policy here.. Because the instance might have been created several days back and at that time, the latest policy was something else. So, we need to get the policy that is attached to this instance.
                
                var pid = instance.GetLong("policy_id");
                if (pid > 0) pr = await PolicyEnforcer.ResolvePolicyByIdAsync(pid, load);

                // Transition consumers
                var transitionConsumers = await AckManager.GetTransitionConsumersAsync(bp.DefVersionId, instanceId, ct);
                var envDefaultConsumerId = await BlueprintManager.EnsureDefaultConsumerIdAsync(req.EnvCode, ct);
                var normTransitionConsumers = NormalizeConsumers(transitionConsumers, envDefaultConsumerId);

                // Create lifecycle ACK (one ack guid, multiple consumers) if required
                var lcAckGuid = string.Empty;
                if (req.AckRequired) {
                    var ackRef = await AckManager.CreateLifecycleAckAsync(transition.LifeCycleId!.Value, normTransitionConsumers, (int)AckStatus.Pending, load);
                    lcAckGuid = ackRef.AckGuid ?? string.Empty;
                    lcAckGuids.Add(lcAckGuid);
                }

                var lcEvent = new LifeCycleEvent() {
                    InstanceGuid = result.InstanceGuid,
                    DefinitionVersionId = bp.DefVersionId,
                    ExternalRef = req.ExternalRef,
                    RequestId = req.RequestId,
                    OccurredAt = DateTimeOffset.UtcNow,
                    AckGuid = lcAckGuid,
                    AckRequired = req.AckRequired,
                    Payload = req.Payload,
                };

                // Build transition events (dispatch after commit)
                for (var i = 0; i < normTransitionConsumers.Count; i++) {
                    var consumerId = normTransitionConsumers[i];
                    var transitionEvent = new LifeCycleTransitionEvent(lcEvent) {
                        ConsumerId = consumerId,
                        LifeCycleId = transition.LifeCycleId.Value,
                        FromStateId = transition.FromStateId,
                        ToStateId = transition.ToStateId,
                        EventCode = transition.EventCode,
                        EventName = transition.EventName ?? string.Empty,
                        PrevStateMeta = new Dictionary<string, object>(),
                        PolicyJson = pr.PolicyJson ?? string.Empty
                    };
                    toDispatch.Add(transitionEvent);
                }

                // Hooks (create hook rows in txn; dispatch after commit)
                var hookEmissions = await PolicyEnforcer.EmitHooksAsync(bp, instance, transition, load); //CHECK ONCE MORE
                for (var h = 0; h < hookEmissions.Count; h++) {
                    var he = hookEmissions[h];
                    var hookConsumers = await AckManager.GetHookConsumersAsync(bp.DefVersionId, instanceId, he.HookCode, ct);
                    var normHookConsumers = NormalizeConsumers(hookConsumers, envDefaultConsumerId);

                    var hookAckGuid = string.Empty;
                    if (req.AckRequired) {
                        var hookAck = await AckManager.CreateHookAckAsync(he.HookId, normHookConsumers, (int)AckStatus.Pending, load);
                        hookAckGuid = hookAck.AckGuid ?? string.Empty;
                        hookAckGuids.Add(hookAckGuid);
                    }

                    for (var i = 0; i < normHookConsumers.Count; i++) {
                        var consumerId = normHookConsumers[i];
                        var hookEvent = new LifeCycleHookEvent(lcEvent) {
                            ConsumerId = consumerId,
                            HookId = he.HookId,
                            OnEntry = he.OnEntry,
                            HookCode = he.HookCode ?? string.Empty,
                            OnSuccessEvent = he.OnSuccessEvent ?? string.Empty,
                            OnFailureEvent = he.OnFailureEvent ?? string.Empty,
                            NotBefore = he.NotBefore,
                            Deadline = he.Deadline
                        };
                        toDispatch.Add(hookEvent);
                    }
                }

                transaction.Commit();
                committed = true;

                // Dispatch AFTER commit (failures become notices; monitor will resend due to pending ACK rows).
                await DispatchEventsSafeAsync(toDispatch, ct);

                result.LifecycleAckGuids = lcAckGuids;
                result.HookAckGuids = hookAckGuids;
                return result;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                throw;
            } catch (Exception ex) {
                if (!committed) { try { transaction.Rollback(); } catch { } }
                FireNotice(LifeCycleNotice.Error("TRIGGER_ERROR", "TRIGGER_ERROR", ex.Message, ex));
                throw;
            }
        }

        public async Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (consumerId <= 0) throw new ArgumentOutOfRangeException(nameof(consumerId));
            if (string.IsNullOrWhiteSpace(ackGuid)) throw new ArgumentNullException(nameof(ackGuid));
            await AckManager.AckAsync(consumerId, ackGuid, outcome, message, retryAt, new DbExecutionLoad(ct));
        }

        public async Task<string?> GetTimelineJsonAsync(long instanceId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            return await _dal.LifeCycle.GetTimelineJsonByInstanceIdAsync(instanceId, new DbExecutionLoad(ct));
        }

        public Task ClearCacheAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Clear(); return Task.CompletedTask; }

        public Task InvalidateAsync(int envCode, string defName, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Invalidate(envCode, defName); return Task.CompletedTask; }

        public Task InvalidateAsync(long defVersionId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); BlueprintManager.Invalidate(defVersionId); return Task.CompletedTask; }

        public async Task RunMonitorOnceAsync(CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < _monitorConsumers.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var consumerId = _monitorConsumers[i];
                await ResendDispatchKindAsync(consumerId, (int)AckStatus.Pending, ct);
                await ResendDispatchKindAsync(consumerId, (int)AckStatus.Delivered, ct);
            }
        }

        public Task<long> RegisterConsumerAsync(int envCode, string consumerGuid, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(consumerGuid)) throw new ArgumentNullException(nameof(consumerGuid));
            return BlueprintManager.EnsureConsumerIdAsync(envCode, consumerGuid, ct); // or BlueprintManager.EnsureConsumerIdAsync if interface exposes it
        }

        public Task BeatConsumerAsync(int envCode, string consumerGuid, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(consumerGuid)) throw new ArgumentNullException(nameof(consumerGuid));
            return BlueprintManager.BeatConsumerAsync(envCode, consumerGuid, ct);
        }

        // client-friendly ACK (guid)
        public async Task AckAsync(int envCode, string consumerGuid, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(consumerGuid)) throw new ArgumentNullException(nameof(consumerGuid));
            var consumerId = await BlueprintManager.EnsureConsumerIdAsync(envCode, consumerGuid, ct);
            await AckAsync(consumerId, ackGuid, outcome, message, retryAt, ct);
        }



        private async Task ResendDispatchKindAsync(long consumerId, int ackStatus, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var nowUtc = DateTime.UtcNow;
            var nextDueUtc = ackStatus == (int)AckStatus.Pending
                ? nowUtc.Add(_opt.AckPendingResendAfter)
                : nowUtc.Add(_opt.AckDeliveredResendAfter);

            // Lifecycle
            var lc = await AckManager.ListDueLifecycleDispatchAsync(consumerId, ackStatus, 0, _opt.MonitorPageSize, new DbExecutionLoad(ct));
            for (var i = 0; i < lc.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = lc[i];

                // IMPORTANT: update trigger_count/last_trigger and schedule next_due (or NULL for terminal statuses)
                await _dal.AckConsumer.MarkTriggerAsync(item.AckId, item.ConsumerId, nextDueUtc, new DbExecutionLoad(ct));

                FireNotice(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                FireEvent(item.Event);
            }

            // Hook
            var hk = await AckManager.ListDueHookDispatchAsync(consumerId, ackStatus, 0, _opt.MonitorPageSize, new DbExecutionLoad(ct));
            for (var i = 0; i < hk.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = hk[i];

                await _dal.AckConsumer.MarkTriggerAsync(item.AckId, item.ConsumerId, nextDueUtc, new DbExecutionLoad(ct));

                FireNotice(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                FireEvent(item.Event);
            }
        }


        private async Task DispatchEventsSafeAsync(IReadOnlyList<ILifeCycleEvent> events, CancellationToken ct) {
            for (var i = 0; i < events.Count; i++) { ct.ThrowIfCancellationRequested(); FireEvent(events[i]); }
        }
        private void FireEvent(ILifeCycleEvent e) {
            var h = EventRaised;
            if (h == null) return;

            foreach (Func<ILifeCycleEvent, Task> sub in h.GetInvocationList()) {
                _ = RunHandlerSafeAsync(() => sub(e)); //Dont await.. we are deliberately running this task in synchornous mode , so that it runs in background.
            }
        }
        private void FireNotice(LifeCycleNotice n) {
            var h = NoticeRaised;
            if (h == null) return;

            foreach (Func<LifeCycleNotice, Task> sub in h.GetInvocationList()) {
                _ = RunHandlerSafeAsync(() => sub(n), swallow: true); //Error should not be propagated , else it will end up in infinite loop.
            }
        }
        private async Task RunHandlerSafeAsync(Func<Task> work, bool swallow = false) {
            try {
                await work().ConfigureAwait(false);
            } catch (Exception ex) {
                if (swallow) return;
                try {
                    FireNotice(LifeCycleNotice.Error("EVENT_HANDLER_ERROR", "EVENT_HANDLER_ERROR", ex.Message, ex));
                } catch { }
            }
        }
        private static IReadOnlyList<long> NormalizeConsumers(IReadOnlyList<long>? consumers, long defaultConsumerId) {
            if (consumers == null || consumers.Count == 0) return new long[] { defaultConsumerId };
            var list = new List<long>(consumers.Count);
            for (var i = 0; i < consumers.Count; i++) { var c = consumers[i]; if (c > 0 && !list.Contains(c)) list.Add(c); }
            return list.Count == 0 ? new long[] { defaultConsumerId } : list;
        }
    }
}