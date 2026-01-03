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
    public sealed class StateMachine : IStateMachine {
        private readonly IWorkFlowDAL _dal;

        private const uint INSTANCE_FLAG_ACTIVE = 1u;     // from schema comment
        private const uint STATE_FLAG_IS_INITIAL = 1u;    // from schema comment

        public StateMachine(IWorkFlowDAL dal) => _dal = dal;

        public async Task<DbRow> EnsureInstanceAsync(long defVersionId, string externalRef, DbExecutionLoad load = default) {
            var existing = await _dal.Instance.GetByKeyAsync(defVersionId, externalRef, load).ConfigureAwait(false);
            if (existing != null) return existing;

            var states = await _dal.Blueprint.ListStatesAsync(defVersionId, load).ConfigureAwait(false);
            long initialStateId = 0;

            foreach (var s in states) {
                var flags = Convert.ToUInt32(s["flags"]);
                if ((flags & STATE_FLAG_IS_INITIAL) != 0) {
                    initialStateId = Convert.ToInt64(s["id"]);
                    break;
                }
            }

            if (initialStateId <= 0) throw new InvalidOperationException("No initial state found for definition version.");

            await _dal.Instance.UpsertAsync(defVersionId, externalRef, initialStateId, lastEventId: null, policyId: 0, flags: INSTANCE_FLAG_ACTIVE, load).ConfigureAwait(false);

            var created = await _dal.Instance.GetByKeyAsync(defVersionId, externalRef, load).ConfigureAwait(false);
            return created ?? throw new InvalidOperationException("Instance upsert failed.");
        }

        public async Task<ApplyTransitionResult> ApplyTransitionAsync(
            LifeCycleBlueprint bp,
            DbRow instance,
            string eventName,
            string? requestId,
            string? actor,
            IReadOnlyDictionary<string, object?>? payload,
            DbExecutionLoad load = default) {

            var instanceId = instance.GetLong("id");
            var fromStateId = instance.GetLong("current_state");

            // resolve event by NAME under this def_version (no inline SQL; use QRY_*)
            var ev = await _dal.RowAsync(QRY_EVENTS.GET_BY_PARENT_AND_NAME, load,
                ("PARENT_ID", bp.DefinitionVersionId),
                ("NAME", eventName.Trim().ToLowerInvariant())
            ).ConfigureAwait(false);

            if (ev == null) {
                return new ApplyTransitionResult { Applied = false, FromStateId = fromStateId };
            }

            var eventId = ev.GetLong("id");
            var eventCode = ev.GetInt("code");
            var eventDisplayName = ev.GetString("display_name");

            // resolve transition
            var tr = await _dal.RowAsync(QRY_TRANSITION.GET_BY_FROM_AND_EVENT, load,
                ("PARENT_ID", bp.DefinitionVersionId),
                ("FROM_ID", fromStateId),
                ("EVENT_ID", eventId)
            ).ConfigureAwait(false);

            if (tr == null) {
                return new ApplyTransitionResult { Applied = false, FromStateId = fromStateId, EventId = eventId, EventCode = eventCode, EventName = eventDisplayName };
            }

            var toStateId = tr.GetLong("to_state");

            // CAS update instance state
            var changed = await _dal.Instance.UpdateCurrentStateCasAsync(instanceId, fromStateId, toStateId, eventId, load).ConfigureAwait(false);
            if (changed <= 0) {
                return new ApplyTransitionResult { Applied = false, FromStateId = fromStateId, ToStateId = toStateId, EventId = eventId, EventCode = eventCode, EventName = eventDisplayName };
            }

            // insert lifecycle record
            var lcId = await _dal.LifeCycle.InsertAsync(instanceId, fromStateId, toStateId, eventId, load).ConfigureAwait(false);

            // lifecycle data (actor + payload json)
            string? payloadJson = null;
            if (payload != null) {
                try { payloadJson = System.Text.Json.JsonSerializer.Serialize(payload); } catch { payloadJson = null; }
            }

            await _dal.LifeCycleData.UpsertAsync(lcId, actor, payloadJson, load).ConfigureAwait(false);

            return new ApplyTransitionResult {
                Applied = true,
                LifeCycleId = lcId,
                FromStateId = fromStateId,
                ToStateId = toStateId,
                EventId = eventId,
                EventCode = eventCode,
                EventName = eventDisplayName
            };
        }
    }

}