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
        public StateMachine(IWorkFlowDAL dal) { _dal = dal; }
        public async Task<DbRow> EnsureInstanceAsync(long defVersionId, string externalRef, DbExecutionLoad load = default) {
            var guid = await _dal.Instance.UpsertByKeyReturnGuidAsync(defVersionId, externalRef, 0, null, 0, 1u, load);
            if (string.IsNullOrWhiteSpace(guid)) throw new InvalidOperationException("Instance upsert failed.");
            var row = await _dal.Instance.GetByGuidAsync(guid, load);
            if (row == null) throw new InvalidOperationException("Instance row missing.");
            return row;
        }

        public async Task<ApplyTransitionResult> ApplyTransitionAsync(LifeCycleBlueprint bp, DbRow instance, string eventName, string requestId, string actor, IReadOnlyDictionary<string, object?>? payload, DbExecutionLoad load = default) {
            var res = new ApplyTransitionResult();
            var instanceId = instance.GetLong("id");
            var fromStateId = instance.GetLong("current_state");
            var ev = ResolveEvent(bp, eventName);

            res.FromStateId = fromStateId;
            res.EventId = ev != null ? ev.Id : 0;
            res.EventCode = ev != null ? ev.Code : 0;
            res.EventName = ev != null ? ev.Name : null;

            if (ev == null) { res.Applied = false; res.Reason = "UnknownEvent"; return res; }

            TransitionDef t;
            if (!bp.Transitions.TryGetValue(Tuple.Create(fromStateId, ev.Id), out t)) { res.Applied = false; res.Reason = "InvalidTransition"; return res; }

            var toStateId = t.ToStateId;
            res.ToStateId = toStateId;

            var cas = await _dal.Instance.UpdateCurrentStateCasAsync(instanceId, fromStateId, toStateId, ev.Id, load);
            if (cas != 1) { res.Applied = false; res.Reason = "ConcurrencyConflict"; return res; }

            var lcId = await _dal.LifeCycle.InsertAsync(instanceId, fromStateId, toStateId, ev.Id, load);
            res.Applied = true;
            res.LifeCycleId = lcId;

            var store = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(requestId)) store["requestId"] = requestId;
            if (payload != null && payload.Count > 0) store["payload"] = payload;

            await _dal.LifeCycleData.UpsertAsync(lcId, actor, store.Count == 0 ? null : JsonSerializer.Serialize(store), load);
            return res;
        }

        private static EventDef? ResolveEvent(LifeCycleBlueprint bp, string s) {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            int code;
            if (int.TryParse(s, out code)) { EventDef e; if (bp.EventsByCode.TryGetValue(code, out e)) return e; }
            EventDef byName;
            if (bp.EventsByName.TryGetValue(s.ToLowerInvariant(), out byName)) return byName;
            return null;
        }
    }
}