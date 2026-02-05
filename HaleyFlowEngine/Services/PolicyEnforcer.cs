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
            //If there are no rules, then nothing to emit..
            if (!doc.RootElement.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
                return Array.Empty<ILifeCycleHookEmission>();

            // params catalog: code -> data dictionary
            var paramCatalog = new Dictionary<string, IReadOnlyDictionary<string, object?>?>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Array) {
                foreach (var p in paramsEl.EnumerateArray()) {
                    var code = p.GetString("code");
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    IReadOnlyDictionary<string, object?>? data = null;
                    if (p.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object) {
                        // Use your existing helper pattern (same as payload)
                        data = p.GetDictionary("data");
                    }
                    paramCatalog[code!] = data;
                }
            }

            foreach (var rule in rules.EnumerateArray()) {
                load.Ct.ThrowIfCancellationRequested();

                var ruleState = rule.GetString("state");
                if (string.IsNullOrWhiteSpace(ruleState)) continue;
                if (!IsStateMatch(ruleState!, toState)) continue;

                // via matching (same logic)
                if (rule.TryGetProperty("via", out var viaEl) && viaEl.ValueKind != JsonValueKind.Null && viaEl.ValueKind != JsonValueKind.Undefined) {
                    if (viaEvent == null) continue;
                    var viaCode = viaEl.GetInt();
                    if (!viaCode.HasValue || viaCode.Value != viaEvent.Code) continue;
                }

                // rule-level param codes (array)
                var ruleParamCodes = ReadParamCodes(rule, "params");

                if (!rule.TryGetProperty("emit", out var emitEl) || emitEl.ValueKind != JsonValueKind.Array) continue;

                var (ruleSuccess, ruleFailure) = ReadCompletionEvents(rule);

                foreach (var e in emitEl.EnumerateArray()) {
                    load.Ct.ThrowIfCancellationRequested();

                    var hookCode = e.GetString("event");
                    if (string.IsNullOrWhiteSpace(hookCode)) continue;

                    var hookId = await _dal.Hook.UpsertByKeyReturnIdAsync(instanceId, applied.ToStateId, applied.EventId, true, hookCode!, load);

                    var (emitSuccess, emitFailure) = ReadCompletionEvents(e);

                    var onSuccess = !string.IsNullOrWhiteSpace(emitSuccess) ? emitSuccess : ruleSuccess;
                    var onFailure = !string.IsNullOrWhiteSpace(emitFailure) ? emitFailure : ruleFailure;

                    var notBefore = e.GetDatetimeOffset("notBefore");
                    var deadline = e.GetDatetimeOffset("deadline");
                    var payload = e.GetDictionary("payload");

                    // emit params override rule params (no merge)
                    var emitParamCodes = ReadParamCodes(e, "params");
                    var effectiveParamCodes = emitParamCodes.Count > 0 ? emitParamCodes : ruleParamCodes;
                    var resolvedParams = ResolveParams(paramCatalog, effectiveParamCodes);

                    emissions.Add(new LifeCycleHookEmission {
                        HookId = hookId,
                        StateId = applied.ToStateId,
                        OnEntry = true,
                        HookCode = hookCode!,
                        OnSuccessEvent = onSuccess,
                        OnFailureEvent = onFailure,
                        NotBefore = notBefore,
                        Deadline = deadline,
                        Payload = payload,
                        Params = resolvedParams
                    });
                }
            }
            
            return emissions;
        }

        private static IReadOnlyList<string> ReadParamCodes(JsonElement obj, string propName) {
            if (!obj.TryGetProperty(propName, out var el) || el.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var x in el.EnumerateArray()) {
                if (x.ValueKind != JsonValueKind.String) continue;
                var s = x.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
            }
            return list;
        }

        private static IReadOnlyList<LifeCycleParamItem>? ResolveParams(
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>?> catalog,
            IReadOnlyList<string> codes) {

            if (codes == null || codes.Count == 0) return null;

            var list = new List<LifeCycleParamItem>(codes.Count);
            foreach (var c in codes) {
                //  todo: If we need “fail fast”, throw here when missing
                catalog.TryGetValue(c, out var data);
                list.Add(new LifeCycleParamItem { Code = c, Data = data ?? new Dictionary<string, object?>() });
            }
            return list;
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

        public RuleContext ResolveRuleContextFromJson(string policyJson, StateDef toState, EventDef? viaEvent, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(policyJson)) return new RuleContext();

            using var doc = JsonDocument.Parse(policyJson);
            if (!TryGetRules(doc.RootElement, out var rules)) return new RuleContext();

            var catalog = BuildParamCatalog(doc.RootElement);

            if (!TrySelectBestRule(rules, toState, viaEvent, ct, out var rule)) return new RuleContext();

            return BuildRuleContext(rule, catalog);
        }

        public HookContext ResolveHookContextFromJson(string policyJson, StateDef toState, EventDef? viaEvent, string hookCode, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(policyJson) || string.IsNullOrWhiteSpace(hookCode)) return new HookContext();

            using var doc = JsonDocument.Parse(policyJson);
            if (!TryGetRules(doc.RootElement, out var rules)) return new HookContext();

            var catalog = BuildParamCatalog(doc.RootElement);

            if (!TrySelectBestRule(rules, toState, viaEvent, ct, out var rule)) return new HookContext();

            // Start from rule context
            var hc = BuildHookContextFromRule(rule, catalog);

            // If emit match exists, override with emit (params + complete + timing)
            if (TryFindEmit(rule, hookCode, ct, out var emit)) ApplyEmitOverrides(hc, rule, emit, catalog);

            return hc;
        }
        private static bool TryGetRules(JsonElement root, out JsonElement rules) {
            rules = default;
            return root.TryGetProperty("rules", out rules) && rules.ValueKind == JsonValueKind.Array;
        }

        private static Dictionary<string, IReadOnlyDictionary<string, object?>?> BuildParamCatalog(JsonElement root) {
            var catalog = new Dictionary<string, IReadOnlyDictionary<string, object?>?>(StringComparer.OrdinalIgnoreCase);

            if (!root.TryGetProperty("params", out var paramsEl) || paramsEl.ValueKind != JsonValueKind.Array) return catalog;

            foreach (var p in paramsEl.EnumerateArray()) {
                var code = p.GetString("code");
                if (string.IsNullOrWhiteSpace(code)) continue;
                catalog[code!] = p.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object ? p.GetDictionary("data") : null;
            }
            return catalog;
        }

        private bool TrySelectBestRule(JsonElement rules, StateDef toState, EventDef? viaEvent, CancellationToken ct, out JsonElement best) {
            best = default;
            var found = false;
            var bestHasVia = false;

            foreach (var rule in rules.EnumerateArray()) {
                ct.ThrowIfCancellationRequested();

                var ruleState = rule.GetString("state");
                if (string.IsNullOrWhiteSpace(ruleState)) continue;
                if (!IsStateMatch(ruleState!, toState)) continue;

                var hasVia = rule.TryGetProperty("via", out var viaEl) && viaEl.ValueKind != JsonValueKind.Null && viaEl.ValueKind != JsonValueKind.Undefined;
                if (hasVia) {
                    if (viaEvent == null) continue;
                    var viaCode = viaEl.GetInt();
                    if (!viaCode.HasValue || viaCode.Value != viaEvent.Code) continue;
                }

                if (!found || (hasVia && !bestHasVia)) {
                    best = rule;
                    found = true;
                    bestHasVia = hasVia;
                }
            }

            return found;
        }

        private RuleContext BuildRuleContext(JsonElement rule, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>?> catalog) {
            var (succ, fail) = ReadCompletionEvents(rule);
            var codes = ReadParamCodes(rule, "params");

            return new RuleContext {
                OnSuccessEvent = succ,
                OnFailureEvent = fail,
                Params = ResolveParams(catalog, codes)
            };
        }

        private HookContext BuildHookContextFromRule(JsonElement rule, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>?> catalog) {
            var rc = BuildRuleContext(rule, catalog);
            return new HookContext { OnSuccessEvent = rc.OnSuccessEvent, OnFailureEvent = rc.OnFailureEvent, Params = rc.Params };
        }

        private static bool TryFindEmit(JsonElement rule, string hookCode, CancellationToken ct, out JsonElement emit) {
            emit = default;
            if (!rule.TryGetProperty("emit", out var emitEl) || emitEl.ValueKind != JsonValueKind.Array) return false;

            foreach (var e in emitEl.EnumerateArray()) {
                ct.ThrowIfCancellationRequested();
                var ev = e.GetString("event");
                if (!string.IsNullOrWhiteSpace(ev) && string.Equals(ev, hookCode, StringComparison.OrdinalIgnoreCase)) { emit = e; return true; }
            }
            return false;
        }

        private void ApplyEmitOverrides(HookContext hc, JsonElement rule, JsonElement emit, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>?> catalog) {
            // completion: emit wins, missing filled from rule
            var (ruleSuccess, ruleFailure) = ReadCompletionEvents(rule);
            var (emitSuccess, emitFailure) = ReadCompletionEvents(emit);
            hc.OnSuccessEvent = !string.IsNullOrWhiteSpace(emitSuccess) ? emitSuccess : ruleSuccess;
            hc.OnFailureEvent = !string.IsNullOrWhiteSpace(emitFailure) ? emitFailure : ruleFailure;

            // params: emit.params wins (no merge), else rule.params
            var ruleCodes = ReadParamCodes(rule, "params");
            var emitCodes = ReadParamCodes(emit, "params");
            var effective = emitCodes.Count > 0 ? emitCodes : ruleCodes;
            hc.Params = ResolveParams(catalog, effective);

            // timing
            hc.NotBefore = emit.GetDatetimeOffset("notBefore");
            hc.Deadline = emit.GetDatetimeOffset("deadline");
        }

    }
}