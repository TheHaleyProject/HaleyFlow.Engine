using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Concurrent;
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
        readonly ConcurrentDictionary<string, DateTimeOffset> _overDueLastAt = new(StringComparer.Ordinal);

        public WorkFlowEngine(IWorkFlowDAL dal, WorkFlowEngineOptions? options = null) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _opt = options ?? new WorkFlowEngineOptions();

            BlueprintManager = _opt.BlueprintManager ?? new BlueprintManager(_dal);
            BlueprintImporter = _opt.BlueprintImporter ?? new BlueprintImporter(_dal);
            StateMachine = _opt.StateMachine ?? new StateMachine(_dal, BlueprintManager);
            PolicyEnforcer = _opt.PolicyEnforcer ?? new PolicyEnforcer(_dal);
            var resolveConsumers = _opt.ResolveConsumers?? ((LifeCycleConsumerType ty, long? id /* DefVersionId */, CancellationToken ct) => Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>()));

            var resolveMonitors = (LifeCycleConsumerType ty,CancellationToken ct)=> resolveConsumers.Invoke(ty,null,ct);

            AckManager = _opt.AckManager ?? new AckManager(_dal, resolveConsumers,_opt.AckPendingResendAfter,_opt.AckDeliveredResendAfter);

            Runtime = _opt.RuntimeEngine ?? new RuntimeEngine(_dal);

            Monitor = new LifeCycleMonitor(_opt.MonitorInterval, ct => RunMonitorOnceInternalAsync(resolveMonitors,ct), (ex) => FireNotice(LifeCycleNotice.Error("MONITOR_ERROR", "MONITOR_ERROR", ex.Message, ex)));
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

            // Transition consumers are needed definetly when a transition happens. Hook consumers are optional.
            var transitionConsumers = await AckManager.GetTransitionConsumersAsync(bp.DefVersionId, ct);
            if (transitionConsumers.Count < 1) throw new ArgumentException("No transition consumers found for this definition version. At least one transition consumer is required to proceed.", nameof(req));

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

                var policy = await PolicyEnforcer.ResolvePolicyAsync(bp.DefinitionId, load); //latest policy
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

                var hookConsumers = await AckManager.GetHookConsumersAsync(bp.DefVersionId, ct);
                var normTransitionConsumers = NormalizeConsumers(transitionConsumers);
                var normHookConsumers = NormalizeConsumers(hookConsumers);

                var instanceId = result.InstanceId;
                // See.. We have the instance created or ensured above. Now, we need to make sure that the policy is also resolved and sent back to the caller. Because, caller might need to know which policy is attached to this instance.
                // We should never take the latest policy here.. Because the instance might have been created several days back and at that time, the latest policy was something else. So, we need to get the policy that is attached to this instance.
                
                var pid = instance.GetLong("policy_id");
                if (pid > 0) pr = await PolicyEnforcer.ResolvePolicyByIdAsync(pid, load);

               
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
                   if (hookConsumers.Count < 1) throw new ArgumentException("No Hook consumers found for this definition version. At least one hook consumer is required to proceed.", nameof(req));

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

        public async Task RunMonitorOnceAsync(long consumerId, CancellationToken ct = default) {
            //This can only check the acknowledgement tables and then send for specific consumers.. It will ignore the consumers which are down at the moment. 
            ct.ThrowIfCancellationRequested();
            await ResendDispatchKindAsync(consumerId, (int)AckStatus.Pending, ct);
            await ResendDispatchKindAsync(consumerId, (int)AckStatus.Delivered, ct);
        }


        internal async Task RunMonitorOnceInternalAsync(Func<LifeCycleConsumerType, CancellationToken, Task<IReadOnlyList<long>>> consumersProvider, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            await RaiseOverDueDefaultStateStaleNoticesAsync(ct);  // stale notice scan (no ACK, no DB writes)

            var consumerList = await consumersProvider(LifeCycleConsumerType.Monitor,ct);
            for (var i = 0; i < consumerList.Count; i++) {
                var consumerId = consumerList[i];
                await RunMonitorOnceAsync(consumerId, ct);
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

        public Task<long> UpsertRuntimeAsync(RuntimeLogByNameRequest req, CancellationToken ct = default) => Runtime.UpsertAsync(req, ct);

        public Task<int> SetRuntimeStatusAsync(long runtimeId, string status, CancellationToken ct = default) => Runtime.SetStatusAsync(runtimeId, status, ct);

        public Task<int> FreezeRuntimeAsync(long runtimeId, CancellationToken ct = default) => Runtime.SetFrozenAsync(runtimeId, true, ct);

        public Task<int> UnfreezeRuntimeAsync(long runtimeId, CancellationToken ct = default) => Runtime.SetFrozenAsync(runtimeId, false, ct);


        async Task ResendDispatchKindAsync(long consumerId, int ackStatus, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var load = new DbExecutionLoad(ct);
            var nowUtc = DateTime.UtcNow;
            var nextDueUtc = ackStatus == (int)AckStatus.Pending ? nowUtc.Add(_opt.AckPendingResendAfter) : nowUtc.Add(_opt.AckDeliveredResendAfter);
            var ttlSeconds = _opt.ConsumerTtlSeconds > 0 ? _opt.ConsumerTtlSeconds : 30; // add this option; fallback keeps it safe
            var recheckSeconds = _opt.ConsumerDownRecheckSeconds; // add option, e.g. 30 or 60

            await _dal.AckConsumer.PushNextDueForDownAsync(consumerId, ackStatus, ttlSeconds, recheckSeconds, load);


            // Lifecycle
            var lc = await AckManager.ListDueLifecycleDispatchAsync(consumerId, ackStatus, ttlSeconds, 0, _opt.MonitorPageSize, load);
            for (var i = 0; i < lc.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = lc[i];

                if (item.TriggerCount >= _opt.MaxRetryCount) {
                    await _dal.AckConsumer.SetStatusAndDueAsync(item.AckId, item.ConsumerId, (int)AckStatus.Failed, null, load);

                    var instanceId = await _dal.Instance.GetIdByGuidAsync(item.Event.InstanceGuid, load) ?? 0;
                    if (instanceId > 0) {
                        var msg = $"Suspended: ack max retries exceeded (max={_opt.MaxRetryCount}) kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}";
                        await _dal.Instance.SuspendWithMessageAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended, msg, load);
                        FireNotice(LifeCycleNotice.Warn("ACK_SUSPEND", "ACK_SUSPEND", msg));
                    } else {
                        FireNotice(LifeCycleNotice.Warn("ACK_FAIL", "ACK_FAIL", $"Ack marked failed (max retries) but instance not found. kind=lifecycle ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                    }

                    continue;
                }

                await _dal.AckConsumer.MarkTriggerAsync(item.AckId, item.ConsumerId, nextDueUtc, load);
                FireNotice(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                FireEvent(item.Event);
            }

            // Hook
            var hk = await AckManager.ListDueHookDispatchAsync(consumerId, ackStatus, ttlSeconds, 0, _opt.MonitorPageSize, load);
            for (var i = 0; i < hk.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = hk[i];

                if (item.TriggerCount >= _opt.MaxRetryCount) {
                    await _dal.AckConsumer.SetStatusAndDueAsync(item.AckId, item.ConsumerId, (int)AckStatus.Failed, null, load);

                    var instanceId = await _dal.Instance.GetIdByGuidAsync(item.Event.InstanceGuid, load) ?? 0;
                    if (instanceId > 0) {
                        var msg = $"Suspended: ack max retries exceeded (max={_opt.MaxRetryCount}) kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}";
                        await _dal.Instance.SuspendWithMessageAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended, msg, load);
                        FireNotice(LifeCycleNotice.Warn("ACK_SUSPEND", "ACK_SUSPEND", msg));
                    } else {
                        FireNotice(LifeCycleNotice.Warn("ACK_FAIL", "ACK_FAIL", $"Ack marked failed (max retries) but instance not found. kind=hook ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                    }

                    continue;
                }

                await _dal.AckConsumer.MarkTriggerAsync(item.AckId, item.ConsumerId, nextDueUtc, load);
                FireNotice(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                FireEvent(item.Event);
            }
        }

        async Task DispatchEventsSafeAsync(IReadOnlyList<ILifeCycleEvent> events, CancellationToken ct) {
            for (var i = 0; i < events.Count; i++) { ct.ThrowIfCancellationRequested(); FireEvent(events[i]); }
        }
        void FireEvent(ILifeCycleEvent e) {
            var h = EventRaised;
            if (h == null) return;

            foreach (Func<ILifeCycleEvent, Task> sub in h.GetInvocationList()) {
                _ = RunHandlerSafeAsync(() => sub(e)); //Dont await.. we are deliberately running this task in synchornous mode , so that it runs in background.
            }
        }
        void FireNotice(LifeCycleNotice n) {
            var h = NoticeRaised;
            if (h == null) return;

            foreach (Func<LifeCycleNotice, Task> sub in h.GetInvocationList()) {
                _ = RunHandlerSafeAsync(() => sub(n), swallow: true); //Error should not be propagated , else it will end up in infinite loop.
            }
        }
        async Task RunHandlerSafeAsync(Func<Task> work, bool swallow = false) {
            try {
                await work().ConfigureAwait(false);
            } catch (Exception ex) {
                if (swallow) return;
                try {
                    FireNotice(LifeCycleNotice.Error("EVENT_HANDLER_ERROR", "EVENT_HANDLER_ERROR", ex.Message, ex));
                } catch { }
            }
        }
        static IReadOnlyList<long> NormalizeConsumers(IReadOnlyList<long>? consumers) {
            if (consumers == null || consumers.Count == 0) return new long[] {};
            var list = new List<long>(consumers.Count);
            for (var i = 0; i < consumers.Count; i++) { var c = consumers[i]; if (c > 0 && !list.Contains(c)) list.Add(c); }
            return list;
        }

        async Task RaiseOverDueDefaultStateStaleNoticesAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var dur = _opt.DefaultStateStaleDuration;
            if (dur <= TimeSpan.Zero) return;

            var staleSeconds = (int)Math.Max(1, dur.TotalSeconds);
            var processed = (int)AckStatus.Processed;
            var excluded = (uint)(LifeCycleInstanceFlag.Suspended | LifeCycleInstanceFlag.Completed | LifeCycleInstanceFlag.Failed | LifeCycleInstanceFlag.Archived);

            var take = _opt.MonitorPageSize > 0 ? _opt.MonitorPageSize : 200;
            var skip = 0;
            var now = DateTimeOffset.UtcNow;

            var resolver = _opt.ResolveConsumers;
            var consumerCache = new Dictionary<long, IReadOnlyList<long>>();

            // crude safety cap (since throttle is in-memory)
            if (_overDueLastAt.Count > 200_000) _overDueLastAt.Clear();

            while (!ct.IsCancellationRequested) {
                var rows = await _dal.Instance.ListStaleByDefaultStateDurationPagedAsync(staleSeconds, processed, excluded, skip, take, new DbExecutionLoad(ct));
                if (rows.Count == 0) break;

                for (var i = 0; i < rows.Count; i++) {
                    ct.ThrowIfCancellationRequested();

                    var r = rows[i];
                    var instanceId = Convert.ToInt64(r["instance_id"]);
                    var instanceGuid = (string)r["instance_guid"];
                    var externalRef = r["external_ref"] as string ?? string.Empty;
                    var defVersionId = Convert.ToInt64(r["def_version_id"]);
                    var stateId = Convert.ToInt64(r["current_state_id"]);
                    var stateName = r["state_name"] as string ?? string.Empty;
                    var lcId = Convert.ToInt64(r["lc_id"]);
                    var staleSec = Convert.ToInt64(r["stale_seconds"]);

                    IReadOnlyList<long> consumers;
                    if (resolver == null) {
                        consumers = Array.Empty<long>();
                    } else if (!consumerCache.TryGetValue(defVersionId, out consumers!)) {
                        consumers = NormalizeConsumers(await resolver(LifeCycleConsumerType.Transition, defVersionId, ct));
                        consumerCache[defVersionId] = consumers;
                    }

                    if (consumers.Count == 0) {
                        var k0 = $"0:{instanceId}:{stateId}";
                        if (_overDueLastAt.TryGetValue(k0, out var last0) && (now - last0) < dur) continue;
                        _overDueLastAt[k0] = now;

                        FireNotice(new LifeCycleNotice(LifeCycleNoticeKind.OverDue, "STATE_STALE", "DEFAULT_STATE_STALE", $"Instance stale (no consumers resolved). instance={instanceGuid} ext={externalRef} state={stateName} stale={TimeSpan.FromSeconds(staleSec)}", null, new Dictionary<string, object?> {
                            ["consumerId"] = 0L,
                            ["instanceId"] = instanceId,
                            ["instanceGuid"] = instanceGuid,
                            ["externalRef"] = externalRef,
                            ["defVersionId"] = defVersionId,
                            ["currentStateId"] = stateId,
                            ["stateName"] = stateName,
                            ["lcId"] = lcId,
                            ["staleSeconds"] = staleSec
                        }));
                        continue;
                    }

                    for (var c = 0; c < consumers.Count; c++) {
                        var consumerId = consumers[c];
                        var key = $"{consumerId}:{instanceId}:{stateId}";
                        if (_overDueLastAt.TryGetValue(key, out var last) && (now - last) < dur) continue;
                        _overDueLastAt[key] = now;

                        FireNotice(new LifeCycleNotice(LifeCycleNoticeKind.OverDue, "STATE_STALE", "DEFAULT_STATE_STALE", $"Instance stale. consumer={consumerId} instance={instanceGuid} ext={externalRef} state={stateName} stale={TimeSpan.FromSeconds(staleSec)}", null, new Dictionary<string, object?> {
                            ["consumerId"] = consumerId,
                            ["instanceId"] = instanceId,
                            ["instanceGuid"] = instanceGuid,
                            ["externalRef"] = externalRef,
                            ["defVersionId"] = defVersionId,
                            ["currentStateId"] = stateId,
                            ["stateName"] = stateName,
                            ["lcId"] = lcId,
                            ["staleSeconds"] = staleSec
                        }));
                    }
                }

                if (rows.Count < take) break;
                skip += take;
            }
        }
    }
}