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
    internal sealed class StateMachine : IStateMachine {
        private readonly IWorkFlowDAL _dal;
        private readonly IBlueprintManager _bp;

        public StateMachine(IWorkFlowDAL dal, IBlueprintManager bp) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); _bp = bp ?? throw new ArgumentNullException(nameof(bp)); }

        public async Task<DbRow> EnsureInstanceAsync(long defVersionId, string externalRef, long policyId, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(externalRef)) throw new ArgumentNullException(nameof(externalRef));

            var bp = await _bp.GetBlueprintByVersionIdAsync(defVersionId, load.Ct);
            var initStateId = bp.InitialStateId;

            var guid = await _dal.Instance.UpsertByKeyReturnGuidAsync(defVersionId, externalRef, initStateId, null, policyId, (uint)LifeCycleInstanceFlag.Active, load);
            if (string.IsNullOrWhiteSpace(guid)) throw new InvalidOperationException("Instance upsert failed (guid null).");

            var row = await _dal.Instance.GetByGuidAsync(guid, load);
            if (row == null) throw new InvalidOperationException("Instance row missing after upsert.");
            return row;
        }

        public async Task<ApplyTransitionResult> ApplyTransitionAsync(LifeCycleBlueprint bp, DbRow instance, string eventName, string? requestId, string? actor, IReadOnlyDictionary<string, object?>? payload, DbExecutionLoad load = default) {
            load.Ct.ThrowIfCancellationRequested();
            if (bp == null) throw new ArgumentNullException(nameof(bp));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (string.IsNullOrWhiteSpace(eventName)) throw new ArgumentNullException(nameof(eventName));

            var res = new ApplyTransitionResult { Applied = false, EventName = string.Empty, Reason = string.Empty };

            var instanceId = instance.GetLong("id");
            var fromStateId = instance.GetLong("current_state");
            var ev = ResolveEvent(bp, eventName);

            res.FromStateId = fromStateId;
            res.EventId = ev?.Id ?? 0;
            res.EventCode = ev?.Code ?? 0;
            res.EventName = ev?.Name ?? string.Empty;

            if (ev == null) { res.Reason = "UnknownEvent"; res.ToStateId = fromStateId; return res; }

            if (!bp.Transitions.TryGetValue(Tuple.Create(fromStateId, ev.Id), out var t)) {
                res.Reason = "InvalidTransition";
                res.ToStateId = fromStateId;
                return res;
            }

            res.ToStateId = t.ToStateId;

            //Sometimes, we will have transitions that point to the same state (like re-trying, etc.), todo: handle it.
            if (res.ToStateId == res.FromStateId) { res.Reason = "NoOpAlreadyInState"; return res; }

            var cas = await _dal.Instance.UpdateCurrentStateCasAsync(instanceId, fromStateId, res.ToStateId, ev.Id, load);
            if (cas != 1) { res.Reason = "ConcurrencyConflict"; return res; }

            var lcId = await _dal.LifeCycle.InsertAsync(instanceId, fromStateId, res.ToStateId, ev.Id, load);
            res.Applied = true;
            res.LifeCycleId = lcId;

            var store = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(requestId)) store["requestId"] = requestId;
            if (payload != null && payload.Count > 0) store["payload"] = payload;

            var payloadJson = store.Count == 0 ? null : JsonSerializer.Serialize(store);
            await _dal.LifeCycleData.UpsertAsync(lcId, actor, payloadJson, load);

            return res;
        }

        private static EventDef? ResolveEvent(LifeCycleBlueprint bp, string eventNameOrCode) {
            if (string.IsNullOrWhiteSpace(eventNameOrCode)) return null;
            var s = eventNameOrCode.Trim();
            if (int.TryParse(s, out var code) && bp.EventsByCode.TryGetValue(code, out var byCode)) return byCode;
            var key = s.ToLowerInvariant();
            if (bp.EventsByName.TryGetValue(key, out var byName)) return byName;
            return null;
        }
    }

}