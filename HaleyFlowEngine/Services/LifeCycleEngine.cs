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
    internal sealed class DefaultLifeCycleEngine : ILifeCycleEngine {
        private readonly IWorkFlowDAL _dal;
        private readonly IStateMachine _sm;
        private readonly IBlueprintManager _bp;
        private readonly IPolicyEnforcer _policy;
        private readonly IAckManager _ack;
        private readonly IConsumerRegistry _consumers;

        public event Func<ILifeCycleEvent, Task> EventRaised;

        public DefaultLifeCycleEngine(IWorkFlowDAL dal, IStateMachine sm, IBlueprintManager bp, IPolicyEnforcer policy, IAckManager ack, IConsumerRegistry consumers = null) {
            _dal = dal; _sm = sm; _bp = bp; _policy = policy; _ack = ack; _consumers = consumers;
        }

        public async Task<LifeCycleTriggerResult> TriggerAsync(LifeCycleTriggerRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            var result = new LifeCycleTriggerResult { LifecycleAckGuids = Array.Empty<string>(), HookAckGuids = Array.Empty<string>() };

            var bp = await _bp.GetBlueprintLatestAsync(req.EnvCode, req.DefName, ct);
            var instance = await _sm.EnsureInstanceAsync(bp.DefVersionId, req.ExternalRef, default);

            var applied = await _sm.ApplyTransitionAsync(bp, instance, req.Event, req.RequestId, req.Actor, req.Payload, default);
            if (!applied.Applied) {
                result.Applied = false;
                result.InstanceGuid = instance.GetString("guid");
                result.InstanceId = instance.GetLong("id");
                result.FromState = bp.StatesById.ContainsKey(applied.FromStateId) ? bp.StatesById[applied.FromStateId].Name : null;
                result.ToState = bp.StatesById.ContainsKey(applied.ToStateId) ? bp.StatesById[applied.ToStateId].Name : null;
                result.Reason = applied.Reason;
                return result;
            }

            // Resolve consumers (fallback to consumer=0)
            var consumerIds = await GetTransitionConsumersAsync(bp.DefVersionId, instance.GetLong("id"), ct);

            // Resolve policy (for transition event)
            var pol = await _policy.ResolvePolicyAsync(bp, instance, applied, default);
            if (pol != null && pol.PolicyId.HasValue) await _dal.Instance.SetPolicyAsync(instance.GetLong("id"), pol.PolicyId.Value, default);

            // Create + publish transition events (1 per consumer)
            var lcAckGuids = new List<string>();
            foreach (var c in consumerIds) {
                var ackRef = await _ack.CreateLifecycleAckAsync(applied.LifeCycleId.Value, new[] { c }, AckStatus.Pending, default);
                lcAckGuids.Add(ackRef.AckGuid);

                var evt = new LifeCycleTransitionEvent {
                    ConsumerId = c,
                    InstanceId = instance.GetLong("id"),
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
                    EventName = applied.EventName,
                    PrevStateMeta = null,
                    PolicyId = pol != null ? pol.PolicyId : null,
                    PolicyHash = pol != null ? pol.PolicyHash : null,
                    PolicyJson = pol != null ? pol.PolicyJson : null
                };

                await RaiseAsync(evt);
            }

            // Hooks (create hook rows + publish hook events, with acks)
            var hookEmissions = await _policy.EmitHooksAsync(bp, instance, applied, default);
            var hookAckGuids = new List<string>();

            foreach (var he in hookEmissions) {
                var hookConsumerIds = await GetHookConsumersAsync(bp.DefVersionId, instance.GetLong("id"), he.HookCode, ct);
                foreach (var c in hookConsumerIds) {
                    var ackRef = await _ack.CreateHookAckAsync(he.HookId, new[] { c }, AckStatus.Pending, default);
                    hookAckGuids.Add(ackRef.AckGuid);

                    var hk = new LifeCycleHookEvent {
                        ConsumerId = c,
                        InstanceId = instance.GetLong("id"),
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

                    await RaiseAsync(hk);
                }
            }

            // Basic AUTO support (from policy json): if route has auto events, trigger them sequentially (depth-limited)
            await TriggerAutoEventsIfAnyAsync(bp, instance, applied, pol != null ? pol.PolicyJson : null, req, ct);

            result.Applied = true;
            result.InstanceGuid = instance.GetString("guid");
            result.InstanceId = instance.GetLong("id");
            result.LifeCycleId = applied.LifeCycleId;
            result.FromState = bp.StatesById.ContainsKey(applied.FromStateId) ? bp.StatesById[applied.FromStateId].Name : null;
            result.ToState = bp.StatesById.ContainsKey(applied.ToStateId) ? bp.StatesById[applied.ToStateId].Name : null;
            result.Reason = null;
            result.LifecycleAckGuids = lcAckGuids;
            result.HookAckGuids = hookAckGuids;
            return result;
        }

        public Task AckAsync(long consumerId, string ackGuid, AckOutcome outcome, string message = null, DateTimeOffset? retryAt = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            return _ack.AckAsync(consumerId, ackGuid, outcome, message, retryAt, default);
        }

        public Task ClearCacheAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _bp.Clear(); return Task.CompletedTask; }
        public Task InvalidateAsync(int envCode, string defName, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _bp.Invalidate(envCode, defName); return Task.CompletedTask; }
        public Task InvalidateAsync(long defVersionId, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _bp.Invalidate(defVersionId); return Task.CompletedTask; }

        private async Task RaiseAsync(ILifeCycleEvent evt) { var h = EventRaised; if (h != null) await h.Invoke(evt); }

        private async Task<IReadOnlyList<long>> GetTransitionConsumersAsync(long defVersionId, long instanceId, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            if (_consumers == null) return new long[] { 0 };
            var list = await _consumers.GetTransitionConsumersAsync(defVersionId, instanceId, ct);
            return (list == null || list.Count == 0) ? new long[] { 0 } : list;
        }

        private async Task<IReadOnlyList<long>> GetHookConsumersAsync(long defVersionId, long instanceId, string hookCode, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            if (_consumers == null) return new long[] { 0 };
            var list = await _consumers.GetHookConsumersAsync(defVersionId, instanceId, hookCode, ct);
            return (list == null || list.Count == 0) ? new long[] { 0 } : list;
        }

        private async Task TriggerAutoEventsIfAnyAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, string policyJson, LifeCycleTriggerRequest req, CancellationToken ct) {
            if (string.IsNullOrWhiteSpace(policyJson)) return;

            var toState = bp.StatesById.ContainsKey(applied.ToStateId) ? bp.StatesById[applied.ToStateId] : null;
            var viaEv = bp.EventsById.ContainsKey(applied.EventId) ? bp.EventsById[applied.EventId] : null;
            if (toState == null) return;

            var autoEvents = new List<int>();

            using (var doc = JsonDocument.Parse(policyJson)) {
                JsonElement routes;
                if (!doc.RootElement.TryGetProperty("routes", out routes) || routes.ValueKind != JsonValueKind.Array) return;

                foreach (var route in routes.EnumerateArray()) {
                    JsonElement st;
                    if (!route.TryGetProperty("state", out st) || st.ValueKind != JsonValueKind.String) continue;
                    if (!string.Equals(st.GetString(), toState.Name, StringComparison.OrdinalIgnoreCase)) continue;

                    JsonElement via;
                    if (route.TryGetProperty("via", out via) && via.ValueKind == JsonValueKind.Number) {
                        if (viaEv == null) continue;
                        if (via.GetInt32() != viaEv.Code) continue;
                    }

                    JsonElement auto;
                    if (!route.TryGetProperty("auto", out auto) || auto.ValueKind != JsonValueKind.Array) continue;

                    foreach (var a in auto.EnumerateArray()) {
                        JsonElement ev;
                        if (a.TryGetProperty("event", out ev) && ev.ValueKind == JsonValueKind.Number) autoEvents.Add(ev.GetInt32());
                    }
                }
            }

            // depth limit
            for (int i = 0; i < autoEvents.Count && i < 8; i++) {
                ct.ThrowIfCancellationRequested();
                var autoReq = new LifeCycleTriggerRequest {
                    EnvCode = req.EnvCode,
                    DefName = req.DefName,
                    ExternalRef = req.ExternalRef,
                    Event = autoEvents[i].ToString(),
                    Actor = req.Actor,
                    RequestId = req.RequestId,
                    Payload = req.Payload
                };
                await TriggerAsync(autoReq, ct);
            }
        }
    }

}