using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System.Collections.Concurrent;
using static Haley.Internal.KeyConstants;

namespace Haley.Services.Orchestrators {
    // Executes monitor duties each tick, in order:
    //
    // A) STALE SCAN  (RaiseOverDueDefaultStateStaleNoticesAsync)
    //    Fires STATE_STALE notices for instances stuck in a state with no open ACKs and NO policy
    //    timeout rule. States that DO have a timeout rule are excluded — they are owned by job C.
    //    Read-only: no DB mutations. In-memory throttle prevents flooding on every tick.
    //
    // B) ACK RE-DISPATCH  (ResendDispatchKindAsync, per consumer)
    //    Resends overdue Pending and Delivered ACK rows. Handles down-consumer backoff (pushes
    //    next_due forward without burning trigger_count). Suspends instances when a blocking hook
    //    exhausts MaxRetryCount.
    //
    // C) POLICY TIMEOUT PROCESSING  ← PENDING IMPLEMENTATION
    //    States with a timeouts policy rule are handled exclusively here; the stale scan skips them.
    //    The infrastructure is in place: QRY_LC_TIMEOUT.LIST_DUE_PAGED, MariaLifeCycleTimeoutDAL,
    //    and the lc_timeout table all exist. What is missing is the wiring in this orchestrator
    //    and the IWorkFlowDAL.LcTimeout property.
    //
    //    Two distinct cases based on whether timeout_event is set in the policy rule:
    //
    //    Case A — timeout_event IS set (e.g. "P7D" → event 4010):
    //      The engine auto-moves the instance without human intervention.
    //      Flow:
    //        1. INSERT INTO lc_timeout(lc_id) FIRST — idempotency marker before TriggerAsync.
    //           If the process crashes after insert but before trigger, the next monitor tick
    //           finds the marker and skips, preventing a double-fire.
    //        2. TriggerAsync(entityId, event = timeout_event_code, actor = "engine.monitor",
    //           AckRequired = false) — goes through the full transactional pipeline.
    //        3. Fire TIMEOUT_FIRED notice (Info) for observability.
    //      Resolution: instance moves to a new state → the old lc_id no longer satisfies
    //        (l.to_state = i.current_state) in LIST_DUE_PAGED → query naturally stops returning it.
    //
    //    Case B — no timeout_event (advisory / escalation timeout):
    //      The engine raises STATE_TIMEOUT_EXCEEDED notices repeatedly; it does NOT auto-move.
    //      Someone must act on the notice to advance the workflow.
    //      lc_timeout schema (mirrors ack_consumer field naming for consistency):
    //        lc_id          BIGINT PK  — lifecycle entry that entered the timed state
    //        created        DATETIME   — when the row was first created (first timeout processed)
    //        trigger_count  INT        — how many STATE_TIMEOUT_EXCEEDED notices have been sent
    //        last_trigger   DATETIME   — when the last notice was fired (for audit)
    //        next_due       DATETIME   — when the next notice should fire (scheduled after each send)
    //      Flow:
    //        1. First time: INSERT lc_timeout(lc_id) with trigger_count=1, last_trigger=now,
    //           next_due = now + DefaultStateStaleDuration  →  fire STATE_TIMEOUT_EXCEEDED notice.
    //        2. Subsequent ticks: if next_due <= UTC_NOW → increment trigger_count, update
    //           last_trigger and next_due, fire notice again (trigger_count in Data for escalation).
    //        3. If max_notices is set on the timeout rule and trigger_count >= max_notices:
    //           FailWithMessageAsync(instance)  →  fire STATE_TIMEOUT_FAILED notice.
    //      Repeat interval: uses DefaultStateStaleDuration (engine config), NOT the policy duration.
    //        policy duration = initial grace period (how long before first notice).
    //        DefaultStateStaleDuration = repeat cadence after that (e.g. every 12h).
    //      Resolution: instance transitions out naturally → l.to_state != i.current_state
    //        → query stops returning it → notices stop. No "mark resolved" step needed.
    //
    //    mode field on the timeouts policy rule:
    //      0 = Once   → Case A: fire event once (standard). Case B: send one notice then stop.
    //      1 = Repeat → Case A: not meaningful (instance moves state regardless).
    //                   Case B: keep notifying at DefaultStateStaleDuration intervals (next_due scheduling).
    //
    //    Notice codes to be emitted by job C (reserved, not yet wired):
    //      TIMEOUT_FIRED           — Info    — Case A fired: auto-trigger was sent
    //      STATE_TIMEOUT_EXCEEDED  — OverDue — Case B: instance exceeded its policy-defined deadline
    //      STATE_TIMEOUT_FAILED    — Warn    — Case B: notice limit reached, instance marked Failed
    internal sealed class MonitorOrchestrator {
        private readonly IWorkFlowDAL _dal;
        private readonly WorkFlowEngineOptions _opt;
        private readonly IAckManager _ackManager;
        private readonly Action<ILifeCycleEvent> _fireEvent;
        private readonly Action<LifeCycleNotice> _fireNotice;

        // In-memory throttle so stale-state notices do not flood every monitor tick.
        private readonly ConcurrentDictionary<string, DateTimeOffset> _overDueLastAt = new(StringComparer.Ordinal);

        // fireEvent and fireNotice delegate to WorkFlowEngine's subscriber pipeline.
        public MonitorOrchestrator(IWorkFlowDAL dal, WorkFlowEngineOptions opt, IAckManager ackManager, Action<ILifeCycleEvent> fireEvent, Action<LifeCycleNotice> fireNotice) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _opt = opt ?? throw new ArgumentNullException(nameof(opt));
            _ackManager = ackManager ?? throw new ArgumentNullException(nameof(ackManager));
            _fireEvent = fireEvent ?? throw new ArgumentNullException(nameof(fireEvent));
            _fireNotice = fireNotice ?? throw new ArgumentNullException(nameof(fireNotice));
        }

        // Single-consumer resend pass. Handles both:
        // - Pending (never delivered)
        // - Delivered (delivered but not ACKed yet)
        public async Task RunMonitorOnceAsync(long consumerId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            await ResendDispatchKindAsync(consumerId, (int)AckStatus.Pending, ct);
            await ResendDispatchKindAsync(consumerId, (int)AckStatus.Delivered, ct);
        }

        // Full monitor tick runs three jobs in order (see class-level comment for full design):
        //   A) Stale scan — STATE_STALE notices for states with no timeout rule
        //   B) Per-consumer ACK re-dispatch — resend overdue Pending/Delivered ACK rows
        //   C) Policy timeout processing — PENDING IMPLEMENTATION
        //      (QRY_LC_TIMEOUT.LIST_DUE_PAGED + MariaLifeCycleTimeoutDAL are ready; wiring is not)
        public async Task RunMonitorOnceInternalAsync(Func<LifeCycleConsumerType, CancellationToken, Task<IReadOnlyList<long>>> consumersProvider, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            // A) Stale scan — read-only, fires STATE_STALE notices
            await RaiseOverDueDefaultStateStaleNoticesAsync(ct);

            // B) ACK re-dispatch — per consumer
            var consumerList = await consumersProvider(LifeCycleConsumerType.Monitor, ct);
            for (var i = 0; i < consumerList.Count; i++) {
                var consumerId = consumerList[i];
                await RunMonitorOnceAsync(consumerId, ct);
            }

                // C) Policy timeout processing — TODO: implement
            // Call _dal.LcTimeout.ListDuePagedAsync(...) and for each result:
            //   Case A (event_code != null):
            //     INSERT lc_timeout(lc_id, trigger_count=1, last_trigger=now) — BEFORE TriggerAsync
            //     TriggerAsync(entityId, event = timeout_event_code, actor = "engine.monitor")
            //     Fire TIMEOUT_FIRED notice
            //   Case B (event_code == null):
            //     UPSERT lc_timeout: increment trigger_count, set last_trigger=now,
            //       next_due = now + DefaultStateStaleDuration  (NOT the policy duration —
            //       policy duration is the initial grace period; stale duration is the repeat cadence)
            //     If trigger_count >= max_notices (policy rule): FailWithMessageAsync + STATE_TIMEOUT_FAILED
            //     Else: fire STATE_TIMEOUT_EXCEEDED notice with trigger_count in Data
        }

        // Resend for one status class (Pending or Delivered) for one consumer.
        // Includes down-consumer postponement and max-retry handling.
        private async Task ResendDispatchKindAsync(long consumerId, int ackStatus, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var load = new DbExecutionLoad(ct);
            var nowUtc = DateTime.UtcNow;
            var nextDueUtc = ackStatus == (int)AckStatus.Pending ? nowUtc.Add(_opt.AckPendingResendAfter) : nowUtc.Add(_opt.AckDeliveredResendAfter);
            var ttlSeconds = _opt.ConsumerTtlSeconds > 0 ? _opt.ConsumerTtlSeconds : 30;
            var recheckSeconds = _opt.ConsumerDownRecheckSeconds;

            // If consumer heartbeat is stale, push due rows forward instead of repeatedly sending now.
            await _dal.AckConsumer.PushNextDueForDownAsync(consumerId, ackStatus, ttlSeconds, recheckSeconds, load);

            // Lifecycle dispatch retries.
            var lc = await _ackManager.ListDueLifecycleDispatchAsync(consumerId, ackStatus, ttlSeconds, 0, _opt.MonitorPageSize, load);
            for (var i = 0; i < lc.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = lc[i];

                if (item.TriggerCount >= _opt.MaxRetryCount) {
                    // Retry budget exhausted: mark failed and suspend instance.
                    await _dal.AckConsumer.SetStatusAndDueAsync(item.AckId, item.ConsumerId, (int)AckStatus.Failed, null, load);

                    var instanceId = await _dal.Instance.GetIdByGuidAsync(item.Event.InstanceGuid, load) ?? 0;
                    if (instanceId > 0) {
                        var msg = $"Suspended: ack max retries exceeded (max={_opt.MaxRetryCount}) kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}";
                        await _dal.Instance.SuspendWithMessageAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended, msg, load);
                        _fireNotice(LifeCycleNotice.Warn("ACK_SUSPEND", "ACK_SUSPEND", msg));
                    } else {
                        _fireNotice(LifeCycleNotice.Warn("ACK_FAIL", "ACK_FAIL", $"Ack marked failed (max retries) but instance not found. kind=lifecycle ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                    }

                    continue;
                }

                // Still retryable: bump trigger counter/next_due and re-dispatch.
                await _dal.AckConsumer.MarkTriggerAsync(item.AckId, item.ConsumerId, nextDueUtc, load);
                _fireNotice(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=lifecycle status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                _fireEvent(item.Event);
            }

            // Hook dispatch retries. Blocking and non-blocking failure handling differs.
            var hk = await _ackManager.ListDueHookDispatchAsync(consumerId, ackStatus, ttlSeconds, 0, _opt.MonitorPageSize, load);
            for (var i = 0; i < hk.Count; i++) {
                ct.ThrowIfCancellationRequested();
                var item = hk[i];

                if (item.TriggerCount >= _opt.MaxRetryCount) {
                    await _dal.AckConsumer.SetStatusAndDueAsync(item.AckId, item.ConsumerId, (int)AckStatus.Failed, null, load);

                    var isBlocking = item.Event is ILifeCycleHookEvent hev && hev.IsBlocking;
                    if (isBlocking) {
                        // Blocking hook failure is terminal for workflow progression; suspend instance.
                        var instanceId = await _dal.Instance.GetIdByGuidAsync(item.Event.InstanceGuid, load) ?? 0;
                        if (instanceId > 0) {
                            var msg = $"Suspended: ack max retries exceeded (max={_opt.MaxRetryCount}) kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}";
                            await _dal.Instance.SuspendWithMessageAsync(instanceId, (uint)LifeCycleInstanceFlag.Suspended, msg, load);
                            _fireNotice(LifeCycleNotice.Warn("ACK_SUSPEND", "ACK_SUSPEND", msg));
                        } else {
                            _fireNotice(LifeCycleNotice.Warn("ACK_FAIL", "ACK_FAIL", $"Ack marked failed (max retries) but instance not found. kind=hook ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                        }
                    } else {
                        // Non-blocking hooks are observed but do not block lifecycle movement.
                        var hookRoute = item.Event is ILifeCycleHookEvent h ? h.Route : string.Empty;
                        _fireNotice(LifeCycleNotice.Warn("ACK_FAIL", "ACK_FAIL", $"Non-blocking hook failed after max retries. kind=hook ack={item.AckGuid} consumer={item.ConsumerId} route={hookRoute} instance={item.Event.InstanceGuid}"));
                    }

                    continue;
                }

                await _dal.AckConsumer.MarkTriggerAsync(item.AckId, item.ConsumerId, nextDueUtc, load);
                _fireNotice(LifeCycleNotice.Warn("ACK_RETRY", "ACK_RETRY", $"kind=hook status={ackStatus} ack={item.AckGuid} consumer={item.ConsumerId} instance={item.Event.InstanceGuid}"));
                _fireEvent(item.Event);
            }
        }

        // Detects instances staying in default state longer than allowed duration and emits notices.
        // This is read-only monitoring; no instance state mutation happens here.
        private async Task RaiseOverDueDefaultStateStaleNoticesAsync(CancellationToken ct) {
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

            // Safety valve for long-running processes.
            if (_overDueLastAt.Count > 200_000) _overDueLastAt.Clear();

            while (!ct.IsCancellationRequested) {
                var rows = await _dal.Instance.ListStaleByDefaultStateDurationPagedAsync(staleSeconds, processed, excluded, skip, take, new DbExecutionLoad(ct));
                if (rows.Count == 0) break;

                for (var i = 0; i < rows.Count; i++) {
                    ct.ThrowIfCancellationRequested();

                    var r = rows[i];
                    var instanceId = Convert.ToInt64(r[KEY_INSTANCE_ID]);
                    var instanceGuid = (string)r[KEY_INSTANCE_GUID];
                    var entityId = r[KEY_ENTITY_ID] as string ?? string.Empty;
                    var defVersionId = Convert.ToInt64(r[KEY_DEF_VERSION_ID]);
                    var stateId = Convert.ToInt64(r[KEY_CURRENT_STATE_ID]);
                    var stateName = r[KEY_STATE_NAME] as string ?? string.Empty;
                    var lcId = Convert.ToInt64(r[KEY_LC_ID]);
                    var staleSec = Convert.ToInt64(r[KEY_STALE_SECONDS]);

                    // Resolve transition consumers once per defVersionId per pass.
                    IReadOnlyList<long> consumers;
                    if (resolver == null) {
                        consumers = Array.Empty<long>();
                    } else if (!consumerCache.TryGetValue(defVersionId, out consumers!)) {
                        consumers = InternalUtils.NormalizeConsumers(await resolver(LifeCycleConsumerType.Transition, defVersionId, ct));
                        consumerCache[defVersionId] = consumers;
                    }

                    if (consumers.Count == 0) {
                        // No consumers mapped: emit one generic notice (consumerId=0) with throttling.
                        var k0 = $"0:{instanceId}:{stateId}";
                        if (_overDueLastAt.TryGetValue(k0, out var last0) && (now - last0) < dur) continue;
                        _overDueLastAt[k0] = now;

                        _fireNotice(new LifeCycleNotice(LifeCycleNoticeKind.OverDue, "STATE_STALE", "DEFAULT_STATE_STALE", $"Instance stale (no consumers resolved). instance={instanceGuid} entity={entityId} state={stateName} stale={TimeSpan.FromSeconds(staleSec)}", null, new Dictionary<string, object?> {
                            ["consumerId"] = 0L,
                            ["instanceId"] = instanceId,
                            ["instanceGuid"] = instanceGuid,
                            ["entityId"] = entityId,
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
                        // Throttle per consumer-instance-state triple.
                        if (_overDueLastAt.TryGetValue(key, out var last) && (now - last) < dur) continue;
                        _overDueLastAt[key] = now;

                        _fireNotice(new LifeCycleNotice(LifeCycleNoticeKind.OverDue, "STATE_STALE", "DEFAULT_STATE_STALE", $"Instance stale. consumer={consumerId} instance={instanceGuid} entity={entityId} state={stateName} stale={TimeSpan.FromSeconds(staleSec)}", null, new Dictionary<string, object?> {
                            ["consumerId"] = consumerId,
                            ["instanceId"] = instanceId,
                            ["instanceGuid"] = instanceGuid,
                            ["entityId"] = entityId,
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
