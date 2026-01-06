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
        private readonly TimeSpan _ackPendingResendAfter = TimeSpan.FromSeconds(30);  // TODO: move to options
        private readonly TimeSpan _ackDeliveredResendAfter = TimeSpan.FromMinutes(5); // TODO: move to options

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
            Func<long, long, CancellationToken, Task<IReadOnlyList<long>>> transitionConsumers = (dv, iid, ct) => Task.FromResult<IReadOnlyList<long>>(tc?.Invoke(dv, iid) ?? new long[] { _opt.DefaultConsumerId });
            Func<long, long, string, CancellationToken, Task<IReadOnlyList<long>>> hookConsumers = (dv, iid, code, ct) => Task.FromResult<IReadOnlyList<long>>(hc?.Invoke(dv, iid, code) ?? new long[] { _opt.DefaultConsumerId });

            AckManager = _opt.AckManager ?? new AckManager(_dal, transitionConsumers, hookConsumers);
            Runtime = _opt.RuntimeEngine ?? new RuntimeEngine(_dal);

            _monitorConsumers = _opt.MonitorConsumers ?? new long[] { _opt.DefaultConsumerId };
            Monitor = new LifeCycleMonitor(_opt.MonitorInterval, ct => RunMonitorOnceAsync(ct), (ex, ct) => RaiseNoticeSafeAsync(LifeCycleNotice.Error("MONITOR_ERROR", "MONITOR_ERROR", ex.Message, ex), ct));
        }

        public Task StartAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Monitor.StartAsync(ct); }

        public Task StopAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Monitor.StopAsync(ct); }

        public async ValueTask DisposeAsync() { try { await StopAsync(CancellationToken.None); } catch { } await Monitor.DisposeAsync(); await _dal.DisposeAsync(); }

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
            ApplyTransitionResult applied = null!;
            PolicyResolution pr = null!;

            try {
                //Before generating the instance, check if policy is needed to be attached. This can be found out from definition version and it's latest policy.. (we always focus on the latest policy for the definition)
                //But once the policy is attached to the instance, it won't change for that instance. That is why it is very important to get the policy id at this stage.. If we are creating instance for first time, we attach whatever is latest at this moment. later on, even if policy changes, that won't affect existing instances.
                var policy = await PolicyEnforcer.ResolvePolicyAsync(bp.DefinitionId, load); 
                instance = await StateMachine.EnsureInstanceAsync(bp.DefVersionId, req.ExternalRef, policy.PolicyId ?? 0, load);
                applied = await StateMachine.ApplyTransitionAsync(bp, instance, req.Event, req.RequestId, req.Actor, req.Payload, load);

                var result = new LifeCycleTriggerResult {
                    Applied = applied.Applied,
                    InstanceGuid = instance.GetString("guid") ?? string.Empty,
                    InstanceId = instance.GetLong("id"),
                    LifeCycleId = applied.LifeCycleId,
                    FromState = bp.StatesById.TryGetValue(applied.FromStateId, out var fs) ? (fs.Name ?? string.Empty) : string.Empty,
                    ToState = bp.StatesById.TryGetValue(applied.ToStateId, out var ts) ? (ts.Name ?? string.Empty) : string.Empty,
                    Reason = applied.Reason ?? string.Empty,
                    LifecycleAckGuids = Array.Empty<string>(),
                    HookAckGuids = Array.Empty<string>()
                };

                // Even if transition not applied, instance may have been created/ensured -> commit that.
                //Meaning, our goal was only to create an instance or just to ensure this exists.. not to make any transition at all. Like this is the first step.
                if (!applied.Applied) {
                    transaction.Commit(); 
                    committed = true;
                    return result;
                }

                var instanceId = result.InstanceId;
                // See.. We have the instance created or ensured above. Now, we need to make sure that the policy is also resolved and sent back to the caller. Because, caller might need to know which policy is attached to this instance.
                // We should never take the latest policy here.. Because the instance might have been created several days back and at that time, the latest policy was something else. So, we need to get the policy that is attached to this instance.
                
                var pid = instance.GetLong("policy_id");
                if (pid > 0) {
                    pr = await PolicyEnforcer.ResolvePolicyByIdAsync(pid, load);
                }
               
                // Transition consumers
                var transitionConsumers = await AckManager.GetTransitionConsumersAsync(bp.DefVersionId, instanceId, ct);
                var normTransitionConsumers = NormalizeConsumers(transitionConsumers, _opt.DefaultConsumerId);

                // Create lifecycle ACK (one ack guid, multiple consumers) if required
                var lcAckGuid = string.Empty;
                if (req.AckRequired) {
                    var ackRef = await AckManager.CreateLifecycleAckAsync(applied.LifeCycleId!.Value, normTransitionConsumers, (int)AckStatus.Pending, load);
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
                    var transitionEvent = LifeCycleTransitionEvent.Make(lcEvent); 
                    transitionEvent.ConsumerId = consumerId;
                    transitionEvent.LifeCycleId = applied.LifeCycleId.Value;
                    transitionEvent.FromStateId = applied.FromStateId;
                    transitionEvent.ToStateId = applied.ToStateId;
                    transitionEvent.EventCode = applied.EventCode;
                    transitionEvent.EventName = applied.EventName ?? string.Empty;
                    transitionEvent.PrevStateMeta = new Dictionary<string, object>();
                    transitionEvent.PolicyJson = pr.PolicyJson ?? string.Empty;
                    toDispatch.Add(transitionEvent);
                }

                // Hooks (create hook rows in txn; dispatch after commit)
                var hookEmissions = await PolicyEnforcer.EmitHooksAsync(bp, instance, applied, load);
                for (var h = 0; h < hookEmissions.Count; h++) {
                    var he = hookEmissions[h];
                    var hookConsumers = await AckManager.GetHookConsumersAsync(bp.DefVersionId, instanceId, he.HookCode, ct);
                    var normHookConsumers = NormalizeConsumers(hookConsumers, _opt.DefaultConsumerId);

                    var hookAckGuid = string.Empty;
                    if (req.AckRequired) {
                        var hookAck = await AckManager.CreateHookAckAsync(he.HookId, normHookConsumers, (int)AckStatus.Pending, load);
                        hookAckGuid = hookAck.AckGuid ?? string.Empty;
                        hookAckGuids.Add(hookAckGuid);
                    }

                    for (var i = 0; i < normHookConsumers.Count; i++) {
                        var consumerId = normHookConsumers[i];
                        toDispatch.Add(new LifeCycleHookEvent {
                            ConsumerId = consumerId,
                            InstanceGuid = result.InstanceGuid,
                            DefinitionVersionId = bp.DefVersionId,
                            ExternalRef = req.ExternalRef,
                            RequestId = req.RequestId,
                            OccurredAt = DateTimeOffset.UtcNow,
                            AckGuid = hookAckGuid,
                            AckRequired = req.AckRequired,
                            Payload = he.Payload as IReadOnlyDictionary<string, object?>,
                            HookId = he.HookId,
                            StateId = he.StateId,
                            OnEntry = he.OnEntry,
                            HookCode = he.HookCode ?? string.Empty,
                            OnSuccessEvent = he.OnSuccessEvent ?? string.Empty,
                            OnFailureEvent = he.OnFailureEvent ?? string.Empty,
                            NotBefore = he.NotBefore,
                            Deadline = he.Deadline
                        });
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
                await RaiseNoticeSafeAsync(LifeCycleNotice.Error("TRIGGER_ERROR", "TRIGGER_ERROR", ex.Message, ex), ct);
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

            // NOTE: stale-state / timeout-trigger monitor needs dedicated DAL queries; this run focuses on ACK resend.
            var nowUtc = DateTime.UtcNow;
            var pendingOlderThan = nowUtc.Subtract(_ackPendingResendAfter);
            var deliveredOlderThan = nowUtc.Subtract(_ackDeliveredResendAfter);

            for (var i = 0; i < _monitorConsumers.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var consumerId = _monitorConsumers[i];

                await ResendDispatchKindAsync(consumerId, (int)AckStatus.Pending, pendingOlderThan, ct);
                await ResendDispatchKindAsync(consumerId, (int)AckStatus.Delivered, deliveredOlderThan, ct);
            }
        }

        private async Task ResendDispatchKindAsync(long consumerId, int ackStatus, DateTime olderThanUtc, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            // Lifecycle
            var lc = await AckManager.ListPendingLifecycleDispatchAsync(consumerId, ackStatus, olderThanUtc, 0, _opt.MonitorPageSize, new DbExecutionLoad(ct));
            for (var i = 0; i < lc.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = lc[i];

                await RaiseNoticeSafeAsync(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceId}"), ct);
                await RaiseEventSafeAsync(item.Event, ct);
                await AckManager.MarkRetryAsync(item.AckId, item.ConsumerId, null, new DbExecutionLoad(ct));
            }

            // Hook
            var hk = await AckManager.ListPendingHookDispatchAsync(consumerId, ackStatus, olderThanUtc, 0, _opt.MonitorPageSize, new DbExecutionLoad(ct));
            for (var i = 0; i < hk.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = hk[i];

                await RaiseNoticeSafeAsync(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceId}"), ct);
                await RaiseEventSafeAsync(item.Event, ct);
                await AckManager.MarkRetryAsync(item.AckId, item.ConsumerId, null, new DbExecutionLoad(ct));
            }
        }

        private async Task DispatchEventsSafeAsync(IReadOnlyList<ILifeCycleEvent> events, CancellationToken ct) {
            for (var i = 0; i < events.Count; i++) { ct.ThrowIfCancellationRequested(); await RaiseEventSafeAsync(events[i], ct); }
        }

        private async Task RaiseEventSafeAsync(ILifeCycleEvent e, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            var h = EventRaised;
            if (h == null) return;
            try { await h.Invoke(e); } catch (Exception ex) { await RaiseNoticeSafeAsync(LifeCycleNotice.Error("EVENT_HANDLER_ERROR", "EVENT_HANDLER_ERROR", ex.Message, ex), ct); }
        }

        private async Task RaiseNoticeSafeAsync(LifeCycleNotice n, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            var h = NoticeRaised;
            if (h == null) return;
            try { await h.Invoke(n); } catch { }
        }

        private static IReadOnlyList<long> NormalizeConsumers(IReadOnlyList<long>? consumers, long defaultConsumerId) {
            if (consumers == null || consumers.Count == 0) return new long[] { defaultConsumerId };
            var list = new List<long>(consumers.Count);
            for (var i = 0; i < consumers.Count; i++) { var c = consumers[i]; if (c > 0 && !list.Contains(c)) list.Add(c); }
            return list.Count == 0 ? new long[] { defaultConsumerId } : list;
        }
    }
}