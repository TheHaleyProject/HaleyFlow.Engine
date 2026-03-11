using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System.Collections.Concurrent;
using static Haley.Internal.KeyConstants;

namespace Haley.Services.Orchestrators {
    // Executes monitor duties:
    // - resend overdue lifecycle/hook events
    // - suspend instances when max retry is exceeded for blocking paths
    // - publish stale-default-state notices
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

        // Full monitor tick:
        // 1) stale notices
        // 2) resolve monitor consumer set
        // 3) per-consumer resend pass
        public async Task RunMonitorOnceInternalAsync(Func<LifeCycleConsumerType, CancellationToken, Task<IReadOnlyList<long>>> consumersProvider, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            await RaiseOverDueDefaultStateStaleNoticesAsync(ct);

            var consumerList = await consumersProvider(LifeCycleConsumerType.Monitor, ct);
            for (var i = 0; i < consumerList.Count; i++) {
                var consumerId = consumerList[i];
                await RunMonitorOnceAsync(consumerId, ct);
            }
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
