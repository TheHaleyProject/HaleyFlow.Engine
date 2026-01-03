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

        public PolicyEnforcer(IWorkFlowDAL dal) { _dal = dal; }

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
            if (pol == null) return Array.Empty<ILifeCycleHookEmission>();

            var policyJson = pol.GetString("content");
            if (string.IsNullOrWhiteSpace(policyJson)) return Array.Empty<ILifeCycleHookEmission>();

            var toState = bp.StatesById.ContainsKey(applied.ToStateId) ? bp.StatesById[applied.ToStateId] : null;
            var viaEv = bp.EventsById.ContainsKey(applied.EventId) ? bp.EventsById[applied.EventId] : null;
            if (toState == null) return Array.Empty<ILifeCycleHookEmission>();

            var emissions = new List<ILifeCycleHookEmission>();
            var instanceId = instance.GetLong("id");

            using (var doc = JsonDocument.Parse(policyJson)) {
                JsonElement routes;
                if (!doc.RootElement.TryGetProperty("routes", out routes) || routes.ValueKind != JsonValueKind.Array) return Array.Empty<ILifeCycleHookEmission>();

                foreach (var route in routes.EnumerateArray()) {
                    JsonElement st;
                    if (!route.TryGetProperty("state", out st) || st.ValueKind != JsonValueKind.String) continue;
                    if (!string.Equals(st.GetString(), toState.Name, StringComparison.OrdinalIgnoreCase)) continue;

                    // via: if present, must match current transition event code
                    JsonElement via;
                    if (route.TryGetProperty("via", out via) && via.ValueKind == JsonValueKind.Number) {
                        if (viaEv == null) continue;
                        if (via.GetInt32() != viaEv.Code) continue;
                    }

                    JsonElement emit;
                    if (!route.TryGetProperty("emit", out emit) || emit.ValueKind != JsonValueKind.Array) continue;

                    foreach (var e in emit.EnumerateArray()) {
                        JsonElement ev;
                        if (!e.TryGetProperty("event", out ev) || ev.ValueKind != JsonValueKind.String) continue;

                        var hookCode = ev.GetString();
                        if (string.IsNullOrWhiteSpace(hookCode)) continue;

                        // create hook row (via_event NOT NULL; payload NOT stored)
                        var hookId = await _dal.Hook.UpsertByKeyReturnIdAsync(instanceId, applied.ToStateId, applied.EventId, true, hookCode, load);

                        string onSuccess = null;
                        string onFailure = null;

                        JsonElement complete;
                        if (e.TryGetProperty("complete", out complete) && complete.ValueKind == JsonValueKind.Object) {
                            JsonElement sEl;
                            if (complete.TryGetProperty("success", out sEl) && sEl.ValueKind == JsonValueKind.Number) onSuccess = sEl.GetInt32().ToString();
                            JsonElement fEl;
                            if (complete.TryGetProperty("failure", out fEl) && fEl.ValueKind == JsonValueKind.Number) onFailure = fEl.GetInt32().ToString();
                        }

                        emissions.Add(new LifeCycleHookEmission {
                            HookId = hookId,
                            StateId = applied.ToStateId,
                            OnEntry = true,
                            HookCode = hookCode,
                            OnSuccessEvent = onSuccess,
                            OnFailureEvent = onFailure,
                            NotBefore = null,
                            Deadline = null,
                            Payload = null
                        });
                    }
                }
            }

            return emissions;
        }
    }
}