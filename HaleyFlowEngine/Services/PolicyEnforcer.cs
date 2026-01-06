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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    internal sealed class PolicyEnforcer : IPolicyEnforcer {
        private readonly IWorkFlowDAL _dal;

        public PolicyEnforcer(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }

        public async Task<PolicyResolution> ResolvePolicyAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            var pr = new PolicyResolution();
            if (!applied.Applied) return pr; //Applied is nothing but was policy already applied for this transition or not.. 
            return await ResolvePolicyAsync(bp.DefinitionId, load);
        }
        public async Task<PolicyResolution> ResolvePolicyByIdAsync(long policyId, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            return PreparePolicyResolution(await _dal.Blueprint.GetPolicyByIdAsync(policyId, load));
        }

        public async Task<PolicyResolution> ResolvePolicyAsync(long definitionId, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            return PreparePolicyResolution(await _dal.Blueprint.GetPolicyForDefinition(definitionId, load));
        }

        PolicyResolution PreparePolicyResolution(DbRow? pol) {
            var pr = new PolicyResolution();
            if (pol == null) return pr;

            pr.PolicyId = pol.GetNullableLong("id");
            pr.PolicyHash = pol.GetString("hash");
            pr.PolicyJson = pol.GetString("content");
            return pr;
        }

        public async Task<IReadOnlyList<ILifeCycleHookEmission>> EmitHooksAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (!applied.Applied) return Array.Empty<ILifeCycleHookEmission>();

            var pol = await _dal.Blueprint.GetPolicyForDefinition(bp.DefinitionId ,load);
            var policyJson = pol?.GetString("content");
            if (string.IsNullOrWhiteSpace(policyJson)) return Array.Empty<ILifeCycleHookEmission>();

            if (!bp.StatesById.TryGetValue(applied.ToStateId, out var toState)) return Array.Empty<ILifeCycleHookEmission>();
            bp.EventsById.TryGetValue(applied.EventId, out var viaEvent);

            var instanceId = instance.GetLong("id");
            var emissions = new List<ILifeCycleHookEmission>();

            using var doc = JsonDocument.Parse(policyJson);
            if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array) return Array.Empty<ILifeCycleHookEmission>();

            foreach (var route in routes.EnumerateArray()) {
                load.Ct.ThrowIfCancellationRequested();

                var routeState = TryGetString(route, "state");
                if (string.IsNullOrWhiteSpace(routeState)) continue;

                if (!IsStateMatch(routeState!, toState)) continue;

                if (route.TryGetProperty("via", out var viaEl) && viaEl.ValueKind != JsonValueKind.Null && viaEl.ValueKind != JsonValueKind.Undefined) {
                    if (viaEvent == null) continue;
                    var viaCode = TryGetInt(viaEl);
                    if (!viaCode.HasValue || viaCode.Value != viaEvent.Code) continue;
                }

                if (!route.TryGetProperty("emit", out var emitEl) || emitEl.ValueKind != JsonValueKind.Array) continue;

                foreach (var e in emitEl.EnumerateArray()) {
                    load.Ct.ThrowIfCancellationRequested();

                    var hookCode = TryGetString(e, "event");
                    if (string.IsNullOrWhiteSpace(hookCode)) continue;

                    var hookId = await _dal.Hook.UpsertByKeyReturnIdAsync(instanceId, applied.ToStateId, applied.EventId, true, hookCode!, load);

                    var (onSuccess, onFailure) = ReadCompletionEvents(e);
                    var notBefore = ReadDateTimeOffset(e, "notBefore");
                    var deadline = ReadDateTimeOffset(e, "deadline");
                    var payload = ReadPayload(e);

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

        private static bool IsStateMatch(string routeState, StateDef toState) {
            if (string.Equals(routeState, toState.Name, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(toState.DisplayName) && string.Equals(routeState, toState.DisplayName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static (string? onSuccess, string? onFailure) ReadCompletionEvents(JsonElement emitObj) {
            if (!emitObj.TryGetProperty("complete", out var compEl) || compEl.ValueKind != JsonValueKind.Object) return (null, null);
            string? onSuccess = null;
            string? onFailure = null;
            if (compEl.TryGetProperty("success", out var sEl)) onSuccess = sEl.ValueKind == JsonValueKind.String ? sEl.GetString() : sEl.ToString();
            if (compEl.TryGetProperty("failure", out var fEl)) onFailure = fEl.ValueKind == JsonValueKind.String ? fEl.GetString() : fEl.ToString();
            return (onSuccess, onFailure);
        }

        private static DateTimeOffset? ReadDateTimeOffset(JsonElement obj, string prop) {
            if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.String) return null;
            return DateTimeOffset.TryParse(el.GetString(), out var dt) ? dt : null;
        }

        private static IReadOnlyDictionary<string, object?>? ReadPayload(JsonElement emitObj) {
            if (!emitObj.TryGetProperty("payload", out var pEl) || pEl.ValueKind == JsonValueKind.Null || pEl.ValueKind == JsonValueKind.Undefined) return null;
            return JsonToDictionary(pEl);
        }

        private static string? TryGetString(JsonElement obj, string prop) {
            if (!obj.TryGetProperty(prop, out var el)) return null;
            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        }

        private static int? TryGetInt(JsonElement el) {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var j)) return j;
            return null;
        }

        private static IReadOnlyDictionary<string, object?> JsonToDictionary(JsonElement el) {
            if (el.ValueKind == JsonValueKind.Object) {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject()) dict[p.Name] = JsonToObject(p.Value);
                return dict;
            }
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["value"] = JsonToObject(el) };
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