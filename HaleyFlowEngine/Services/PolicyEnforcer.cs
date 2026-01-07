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
            //If there are no routes, then nothing to emit..
            if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array) return Array.Empty<ILifeCycleHookEmission>();

            foreach (var route in routes.EnumerateArray()) {
                load.Ct.ThrowIfCancellationRequested();

                var routeState = route.GetString("state");
                if (string.IsNullOrWhiteSpace(routeState)) continue;

                if (!IsStateMatch(routeState!, toState)) continue;

                if (route.TryGetProperty("via", out var viaEl) && viaEl.ValueKind != JsonValueKind.Null && viaEl.ValueKind != JsonValueKind.Undefined) {
                    if (viaEvent == null) continue;
                    var viaCode = viaEl.GetInt();
                    if (!viaCode.HasValue || viaCode.Value != viaEvent.Code) continue;
                }

                if (!route.TryGetProperty("emit", out var emitEl) || emitEl.ValueKind != JsonValueKind.Array) continue;

                foreach (var e in emitEl.EnumerateArray()) {
                    load.Ct.ThrowIfCancellationRequested();

                    var hookCode = e.GetString("event");
                    if (string.IsNullOrWhiteSpace(hookCode)) continue;

                    var hookId = await _dal.Hook.UpsertByKeyReturnIdAsync(instanceId, applied.ToStateId, applied.EventId, true, hookCode!, load);

                    var (onSuccess, onFailure) = ReadCompletionEvents(e);
                    var notBefore = e.GetDatetimeOffset("notBefore");
                    var deadline = e.GetDatetimeOffset("deadline");
                    var payload = e.GetDictionary("payload");

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
    }
}