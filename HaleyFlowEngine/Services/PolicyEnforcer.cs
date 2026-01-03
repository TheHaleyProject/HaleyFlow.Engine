using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    public sealed class PolicyEnforcer : IPolicyEnforcer {
        private readonly IWorkFlowDAL _dal;

        public PolicyEnforcer(IWorkFlowDAL dal) => _dal = dal;

        public async Task<PolicyResolution> ResolvePolicyAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default) {
            // expected: your DAL will implement this (even if internally it parses def_version.data)
            var row = await _dal.Blueprint.GetPolicyForStateAsync(bp.DefinitionId, applied.ToStateId, load).ConfigureAwait(false);
            if (row == null) return new PolicyResolution();

            return new PolicyResolution {
                PolicyId = Convert.ToInt64(row["id"]),
                PolicyHash = Convert.ToString(row["hash"]),
                PolicyJson = Convert.ToString(row["content"])
            };
        }

        public async Task<IReadOnlyList<ILifeCycleHookEvent>> EmitHooksAsync(LifeCycleBlueprint bp, DbRow instance, ApplyTransitionResult applied, DbExecutionLoad load = default) {
            var pol = await ResolvePolicyAsync(bp, instance, applied, load).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pol.PolicyJson)) return Array.Empty<ILifeCycleHookEvent>();

            // get state name for matching routes
            var st = await _dal.RowAsync(QRY_STATE.GET_BY_ID, load, ("ID", applied.ToStateId)).ConfigureAwait(false);
            var stateName = st == null ? "" : (Convert.ToString(st["display_name"]) ?? Convert.ToString(st["name"]) ?? "");
            var stateKey = stateName.Trim().ToLowerInvariant();

            var payloadJson = (string?)null; // keep reserved; you said you might want it later

            using var doc = System.Text.Json.JsonDocument.Parse(pol.PolicyJson);
            if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.ValueKind != System.Text.Json.JsonValueKind.Array)
                return Array.Empty<ILifeCycleHookEvent>();

            var emitted = new List<ILifeCycleHookEvent>();
            var instanceId = instance.GetLong("id");
            var externalRef = instance.GetString("external_ref");

            foreach (var route in routes.EnumerateArray()) {
                if (!route.TryGetProperty("state", out var rs)) continue;
                var rState = (rs.GetString() ?? "").Trim().ToLowerInvariant();
                if (!string.Equals(rState, stateKey, StringComparison.OrdinalIgnoreCase)) continue;

                // optional via (policy uses EVENT CODE in your samples)
                if (route.TryGetProperty("via", out var via) && via.ValueKind == System.Text.Json.JsonValueKind.Number) {
                    var viaCode = via.GetInt32();
                    if (viaCode != applied.EventCode) continue;
                }

                if (!route.TryGetProperty("emit", out var emitArr) || emitArr.ValueKind != System.Text.Json.JsonValueKind.Array)
                    continue;

                foreach (var emit in emitArr.EnumerateArray()) {
                    var hookCode = emit.TryGetProperty("event", out var ev) ? (ev.GetString() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(hookCode)) continue;

                    string? onSuccess = null;
                    string? onFailure = null;

                    if (emit.TryGetProperty("complete", out var comp) && comp.ValueKind == System.Text.Json.JsonValueKind.Object) {
                        if (comp.TryGetProperty("success", out var scc)) onSuccess = scc.ToString();
                        if (comp.TryGetProperty("failure", out var flr)) onFailure = flr.ToString();
                    }

                    // persist hook row (replay after crash)
                    var hookId = await _dal.Hook.UpsertByKeyReturnIdAsync(
                        instanceId: instanceId,
                        stateId: applied.ToStateId,
                        viaEventId: applied.EventId, // keep non-null for now
                        onEntry: true,
                        hookCode: hookCode,
                        payloadJson: payloadJson,
                        load: load).ConfigureAwait(false);

                    // create ack (source = hookId)
                    var ackId = await _dal.Ack.UpsertByConsumerAndSourceReturnIdAsync(
                        consumer: 0,
                        source: hookId,
                        ackStatus: (int)LifeCycleAckStatus.Pending,
                        load: load).ConfigureAwait(false);

                    await _dal.HookAck.AttachAsync(ackId, hookId, load).ConfigureAwait(false);

                    var ackRow = await _dal.Ack.GetByIdAsync(ackId, load).ConfigureAwait(false);
                    var ackGuid = ackRow == null ? Guid.Empty : Guid.Parse(Convert.ToString(ackRow["guid"]) ?? Guid.Empty.ToString());

                    emitted.Add(new LifeCycleHookEvent(instanceId, bp.DefinitionVersionId, externalRef, DateTimeOffset.UtcNow, ackGuid) {
                        HookId = hookId,
                        StateId = applied.ToStateId,
                        OnEntry = true,
                        HookCode = hookCode,
                        OnSuccessEvent = onSuccess,
                        OnFailureEvent = onFailure
                    });
                }
            }

            return emitted;
        }
    }

}