using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    internal sealed class PolicyEnforcer : IPolicyEnforcer {
        private readonly IWorkFlowDAL _dal;

        public PolicyEnforcer(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }

        public async Task<PolicyResolution> ResolvePolicyAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default) {
            var pr = new PolicyResolution();
            if (!applied.Applied) return pr;

            var pol = await _dal.Blueprint.GetPolicyForStateAsync(bp.DefinitionId, applied.ToStateId, load);
            if (pol == null) return pr;

            pr.PolicyId = pol.GetNullableLong("id");
            pr.PolicyHash = pol.GetString("hash");
            pr.PolicyJson = pol.GetString("content");
            return pr;
        }

        public async Task<IReadOnlyList<ILifeCycleHookEmission>> EmitHooksAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default) {
            if (!applied.Applied) return Array.Empty<ILifeCycleHookEmission>();

            var pol = await _dal.Blueprint.GetPolicyForStateAsync(bp.DefinitionId, applied.ToStateId, load);
            var policyJson = pol?.GetString("content");
            if (string.IsNullOrWhiteSpace(policyJson)) return Array.Empty<ILifeCycleHookEmission>();

            if (!bp.StatesById.TryGetValue(applied.ToStateId, out var toState)) return Array.Empty<ILifeCycleHookEmission>();
            if (!bp.EventsById.TryGetValue(applied.EventId, out var viaEvent)) viaEvent = null;

            var instanceId = instance.GetLong("id");
            var emissions = new List<ILifeCycleHookEmission>();

            using var doc = JsonDocument.Parse(policyJson);
            if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array) return Array.Empty<ILifeCycleHookEmission>();

            foreach (var route in routes.EnumerateArray()) {
                if (!route.TryGetProperty("state", out var stEl) || stEl.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(stEl.GetString(), toState.Name, StringComparison.OrdinalIgnoreCase)) continue;

                if (route.TryGetProperty("via", out var viaEl) && viaEl.ValueKind == JsonValueKind.Number) {
                    if (viaEvent == null) continue;
                    if (viaEl.GetInt32() != viaEvent.Code) continue;
                }

                if (!route.TryGetProperty("emit", out var emitEl) || emitEl.ValueKind != JsonValueKind.Array) continue;

                foreach (var e in emitEl.EnumerateArray()) {
                    if (!e.TryGetProperty("event", out var hookCodeEl) || hookCodeEl.ValueKind != JsonValueKind.String) continue;
                    var hookCode = hookCodeEl.GetString();
                    if (string.IsNullOrWhiteSpace(hookCode)) continue;

                    // Create hook row (via_event NOT NULL; payload NOT stored)
                    var hookId = await _dal.Hook.UpsertByKeyReturnIdAsync(instanceId, applied.ToStateId, applied.EventId, true, hookCode!, load);

                    string? onSuccess = null;
                    string? onFailure = null;

                    if (e.TryGetProperty("complete", out var compEl) && compEl.ValueKind == JsonValueKind.Object) {
                        if (compEl.TryGetProperty("success", out var sEl)) onSuccess = sEl.ValueKind == JsonValueKind.String ? sEl.GetString() : sEl.ToString();
                        if (compEl.TryGetProperty("failure", out var fEl)) onFailure = fEl.ValueKind == JsonValueKind.String ? fEl.GetString() : fEl.ToString();
                    }

                    DateTimeOffset? notBefore = null;
                    DateTimeOffset? deadline = null;

                    if (e.TryGetProperty("notBefore", out var nbEl) && nbEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(nbEl.GetString(), out var nb)) notBefore = nb;
                    if (e.TryGetProperty("deadline", out var dlEl) && dlEl.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(dlEl.GetString(), out var dl)) deadline = dl;

                    IReadOnlyDictionary<string, object?>? payload = null;
                    if (e.TryGetProperty("payload", out var pEl) && pEl.ValueKind != JsonValueKind.Null && pEl.ValueKind != JsonValueKind.Undefined) {
                        payload = JsonToDictionary(pEl);
                    }

                    emissions.Add(new LifeCycleHookEmission {
                        HookId = hookId,
                        StateId = applied.ToStateId,
                        OnEntry = true,
                        HookCode = hookCode!,
                        OnSuccessEvent = onSuccess,
                        OnFailureEvent = onFailure,
                        NotBefore = notBefore,
                        Deadline = deadline,
                        Payload = payload
                    });
                }
            }

            return emissions;
        }

        private static IReadOnlyDictionary<string, object?>? JsonToDictionary(JsonElement el) {
            if (el.ValueKind == JsonValueKind.Object) {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject()) dict[p.Name] = JsonToObject(p.Value);
                return dict;
            }
            // If payload is not an object, wrap it for safety.
            return new Dictionary<string, object?> { ["value"] = JsonToObject(el) };
        }

        private static object? JsonToObject(JsonElement el) {
            switch (el.ValueKind) {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return null;
                case JsonValueKind.String: return el.GetString();
                case JsonValueKind.Number:
                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDouble(out var d)) return d;
                return el.ToString();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var x in el.EnumerateArray()) list.Add(JsonToObject(x));
                return list;
                case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject()) dict[p.Name] = JsonToObject(p.Value);
                return dict;
                default: return el.ToString();
            }
        }
    }

}