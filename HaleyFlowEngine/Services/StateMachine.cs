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

        public async Task<DbRow> EnsureInstanceAsync(long defVersionId, string externalRef, DbExecutionLoad load = default) {
            var bp = await _bp.GetBlueprintByVersionIdAsync(defVersionId, CancellationToken.None);
            var initStateId = bp.InitialStateId;

            // NOTE: this matches your earlier finalized query: UPSERT_BY_PARENT_AND_EXTERNAL_REF_RETURN_GUID
            var guid = await _dal.Instance.UpsertByKeyReturnGuidAsync(defVersionId, externalRef, initStateId, null, 0, 1u, load);
            if (string.IsNullOrWhiteSpace(guid)) throw new InvalidOperationException("Instance upsert failed (guid null).");

            var row = await _dal.Instance.GetByGuidAsync(guid, load);
            if (row == null) throw new InvalidOperationException("Instance row missing after upsert.");
            return row;
        }

        public async Task<ApplyTransitionResult> ApplyTransitionAsync(LifeCycleBlueprint bp, DbRow instance, string eventName, string? requestId, string? actor, IReadOnlyDictionary<string, object?>? payload, DbExecutionLoad load = default) {
            if (bp == null) throw new ArgumentNullException(nameof(bp));
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            var res = new ApplyTransitionResult();

            var instanceId = instance.GetLong("id");
            var fromStateId = instance.GetLong("current_state");
            var ev = ResolveEvent(bp, eventName);

            res.FromStateId = fromStateId;
            res.EventId = ev != null ? ev.Id : 0;
            res.EventCode = ev != null ? ev.Code : 0;
            res.EventName = ev != null ? ev.Name : null;

            if (ev == null) { res.Applied = false; res.Reason = "UnknownEvent"; return res; }

            if (!bp.Transitions.TryGetValue(Tuple.Create(fromStateId, ev.Id), out var t)) {
                res.Applied = false;
                res.Reason = "InvalidTransition";
                res.ToStateId = fromStateId;
                return res;
            }

            res.ToStateId = t.ToStateId;

            if (res.ToStateId == res.FromStateId) {
                res.Applied = false;
                res.Reason = "NoOpAlreadyInState";
                return res;
            }

            var cas = await _dal.Instance.UpdateCurrentStateCasAsync(instanceId, fromStateId, res.ToStateId, ev.Id, load);
            if (cas != 1) { res.Applied = false; res.Reason = "ConcurrencyConflict"; return res; }

            var lcId = await _dal.LifeCycle.InsertAsync(instanceId, fromStateId, res.ToStateId, ev.Id, load);
            res.Applied = true;
            res.LifeCycleId = lcId;

            var store = new Dictionary<string, object?>();
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
            if (bp.EventsByName.TryGetValue(s.ToLowerInvariant(), out var byName)) return byName;
            return null;
        }
    }

}