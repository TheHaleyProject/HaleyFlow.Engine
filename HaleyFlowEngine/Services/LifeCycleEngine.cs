using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    internal sealed class LifeCycleEngine : ILifeCycleEngine {
        private readonly IWorkFlowDAL _dal;
        private readonly IStateMachine _sm;
        private readonly IBlueprintManager _bp;
        private readonly IPolicyEnforcer _policy;
        private readonly IAckManager _ack;

        // monitor knobs (simple + safe defaults)
        private readonly IReadOnlyList<long> _monitorConsumers;
        private readonly int _monitorTake;
        private readonly TimeSpan _olderThan;

        public event Func<ILifeCycleEvent, Task>? EventRaised;

        public LifeCycleEngine(IWorkFlowDAL dal, IStateMachine sm, IBlueprintManager bp, IPolicyEnforcer policy, IAckManager ack, IReadOnlyList<long>? monitorConsumers = null, int monitorTake = 100, TimeSpan? olderThan = null) {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _sm = sm ?? throw new ArgumentNullException(nameof(sm));
            _bp = bp ?? throw new ArgumentNullException(nameof(bp));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _ack = ack ?? throw new ArgumentNullException(nameof(ack));
            _monitorConsumers = monitorConsumers ?? new long[] { 0 };
            _monitorTake = monitorTake;
            _olderThan = olderThan ?? TimeSpan.FromMinutes(1);
        }

        public async Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            var bp = await _bp.GetBlueprintLatestAsync(req.EnvCode, req.DefName, ct);
            var instance = await _sm.EnsureInstanceAsync(bp.DefVersionId, req.ExternalRef, default);

            var applied = await _sm.ApplyTransitionAsync(bp, instance, req.Event, req.RequestId, req.Actor, req.Payload, default);

            var result = new LifeCycleTriggerResult {
                Applied = applied.Applied,
                InstanceGuid = instance.GetString("guid"),
                InstanceId = instance.GetLong("id"),
                LifeCycleId = applied.LifeCycleId,
                FromState = bp.StatesById.TryGetValue(applied.FromStateId, out var fs) ? fs.Name : null,
                ToState = bp.StatesById.TryGetValue(applied.ToStateId, out var ts) ? ts.Name : null,
                Reason = applied.Reason,
                LifecycleAckGuids = Array.Empty<string>(),
                HookAckGuids = Array.Empty<string>()
            };

            if (!applied.Applied) return result;

            var instanceId = result.InstanceId;

            // Policy resolution (optional attach to instance)
            var pr = await _policy.ResolvePolicyAsync(bp, instance, applied, default);
            if (pr != null && pr.PolicyId.HasValue) await _dal.Instance.SetPolicyAsync(instanceId, pr.PolicyId.Value, default);

            // Consumers for transition
            var transitionConsumers = await _ack.GetTransitionConsumersAsync(bp.DefVersionId, instanceId, ct);
            if (transitionConsumers == null || transitionConsumers.Count == 0) transitionConsumers = new long[] { 0 };

            var lcAckGuids = new List<string>(transitionConsumers.Count);

            foreach (var consumerId in transitionConsumers) {
                var ackRef = await _ack.CreateLifecycleAckAsync(applied.LifeCycleId!.Value, new[] { consumerId }, (int)AckStatus.Pending, default);
                lcAckGuids.Add(ackRef.AckGuid);

                // You already have your own derived event types; replace these instantiations with your types if needed.
                var evt = new LifeCycleTransitionEvent {
                    ConsumerId = consumerId,
                    InstanceId = instanceId,
                    DefinitionVersionId = bp.DefVersionId,
                    ExternalRef = req.ExternalRef,
                    RequestId = req.RequestId,
                    OccurredAt = DateTimeOffset.UtcNow,
                    AckGuid = ackRef.AckGuid,
                    AckRequired = true,
                    Payload = req.Payload,
                    LifeCycleId = applied.LifeCycleId.Value,
                    FromStateId = applied.FromStateId,
                    ToStateId = applied.ToStateId,
                    EventId = applied.EventId,
                    EventCode = applied.EventCode,
                    EventName = applied.EventName ?? string.Empty,
                    PrevStateMeta = null,
                    PolicyId = pr?.PolicyId,
                    PolicyHash = pr?.PolicyHash,
                    PolicyJson = pr?.PolicyJson
                };

                var handler = EventRaised;
                if (handler != null) await handler.Invoke(evt);
            }

            // Hooks (create hook rows + then ack per consumer + raise hook event)
            var hookEmissions = await _policy.EmitHooksAsync(bp, instance, applied, default);
            var hookAckGuids = new List<string>();

            foreach (var he in hookEmissions) {
                var hookConsumers = await _ack.GetHookConsumersAsync(bp.DefVersionId, instanceId, he.HookCode, ct);
                if (hookConsumers == null || hookConsumers.Count == 0) hookConsumers = new long[] { 0 };

                foreach (var consumerId in hookConsumers) {
                    var ackRef = await _ack.CreateHookAckAsync(he.HookId, new[] { consumerId }, (int)AckStatus.Pending, default);
                    hookAckGuids.Add(ackRef.AckGuid);

                    var hk = new LifeCycleHookEvent {
                        ConsumerId = consumerId,
                        InstanceId = instanceId,
                        DefinitionVersionId = bp.DefVersionId,
                        ExternalRef = req.ExternalRef,
                        RequestId = req.RequestId,
                        OccurredAt = DateTimeOffset.UtcNow,
                        AckGuid = ackRef.AckGuid,
                        AckRequired = true,
                        Payload = he.Payload,
                        HookId = he.HookId,
                        StateId = he.StateId,
                        OnEntry = he.OnEntry,
                        HookCode = he.HookCode,
                        OnSuccessEvent = he.OnSuccessEvent,
                        OnFailureEvent = he.OnFailureEvent,
                        NotBefore = he.NotBefore,
                        Deadline = he.Deadline
                    };

                    var handler = EventRaised;
                    if (handler != null) await handler.Invoke(hk);
                }
            }

            result.LifecycleAckGuids = lcAckGuids;
            result.HookAckGuids = hookAckGuids;
            return result;
        }

        public Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _ack.AckAsync(consumerId, ackGuid, outcome, message, retryAt, default); }

        public Task ClearCacheAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _bp.Clear(); return Task.CompletedTask; }

        public Task InvalidateAsync(int envCode, string defName, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _bp.Invalidate(envCode, defName); return Task.CompletedTask; }

        public Task InvalidateAsync(long defVersionId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _bp.Invalidate(defVersionId); return Task.CompletedTask; }

        public async Task RunMonitorOnceAsync(CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            // This monitor focuses on ACK re-dispatch (re-raise events) using your ack_dispatch queries.
            // For timeout-trigger monitor, you can extend here later.

            var handler = EventRaised;
            if (handler == null) return;

            var olderThanUtc = DateTime.UtcNow.Subtract(_olderThan);

            foreach (var consumerId in _monitorConsumers) {
                ct.ThrowIfCancellationRequested();

                var lc = await _ack.ListPendingLifecycleDispatchAsync(consumerId, (int)AckStatus.Pending, olderThanUtc, 0, _monitorTake, default);
                foreach (var item in lc) {
                    ct.ThrowIfCancellationRequested();
                    await handler.Invoke(item.Event);
                    await _ack.MarkRetryAsync(item.AckId, item.ConsumerId, null, default);
                }

                var hk = await _ack.ListPendingHookDispatchAsync(consumerId, (int)AckStatus.Pending, olderThanUtc, 0, _monitorTake, default);
                foreach (var item in hk) {
                    ct.ThrowIfCancellationRequested();
                    await handler.Invoke(item.Event);
                    await _ack.MarkRetryAsync(item.AckId, item.ConsumerId, null, default);
                }
            }
        }
    }
}