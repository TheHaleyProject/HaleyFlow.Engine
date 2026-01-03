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
    public sealed class AckManager : IAckManager {
        private readonly IWorkFlowDAL _dal;

        public AckManager(IWorkFlowDAL dal) => _dal = dal;

        public async Task AckAsync(Guid ackGuid, LifeCycleAckOutcome outcome, string? message = null, DateTimeOffset? retryAt = null, DbExecutionLoad load = default) {
            var row = await _dal.RowAsync(QRY_ACK.GET_BY_GUID, load, ("GUID", ackGuid.ToString())).ConfigureAwait(false);
            if (row == null) return;

            var ackId = row.GetLong("id");

            var status = outcome switch {
                LifeCycleAckOutcome.Success => (int)LifeCycleAckStatus.Processed,
                LifeCycleAckOutcome.Failure => (int)LifeCycleAckStatus.Failed,
                LifeCycleAckOutcome.Retry => (int)LifeCycleAckStatus.Pending,
                _ => (int)LifeCycleAckStatus.Pending
            };

            await _dal.ExecAsync(QRY_ACK.SET_STATUS, load, ("ID", ackId), ("ACK_STATUS", status)).ConfigureAwait(false);

            // NOTE: schema is master; if you later add "not_before" support, wire retryAt there.
        }

        public async Task<IReadOnlyList<ILifeCycleEvent>> GetPendingDispatchAsync(DateTime utcNow, int take, DbExecutionLoad load = default) {
            // backoff window (simple)
            var olderThan = utcNow.AddSeconds(-30);

            var lcRows = await _dal.AckDispatch.ListPendingLifecycleReadyPagedAsync(
                ackStatus: (int)LifeCycleAckStatus.Pending,
                utcOlderThan: olderThan,
                skip: 0,
                take: take,
                load: load).ConfigureAwait(false);

            var hookRows = await _dal.AckDispatch.ListPendingHookReadyPagedAsync(
                ackStatus: (int)LifeCycleAckStatus.Pending,
                utcOlderThan: olderThan,
                skip: 0,
                take: take,
                load: load).ConfigureAwait(false);

            var list = new List<ILifeCycleEvent>(lcRows.Count + hookRows.Count);

            foreach (var r in lcRows) {
                var ackId = Convert.ToInt64(r["ack_id"]);
                await _dal.Ack.MarkRetryAsync(ackId, load).ConfigureAwait(false);

                var e = new LifeCycleTransitionEvent(
                    instanceId: Convert.ToInt64(r["instance_id"]),
                    defVersionId: 0, // not in dispatch query; optional (you can extend query if you want)
                    externalRef: Convert.ToString(r["external_ref"]) ?? "",
                    occurredAt: new DateTimeOffset(DateTime.SpecifyKind((DateTime)r["lc_created"], DateTimeKind.Utc)),
                    ackGuid: Guid.Parse(Convert.ToString(r["ack_guid"]) ?? Guid.Empty.ToString())
                ) {
                    AckRequired = true,
                    LifeCycleId = Convert.ToInt64(r["lc_id"]),
                    FromStateId = Convert.ToInt64(r["from_state"]),
                    ToStateId = Convert.ToInt64(r["to_state"]),
                    EventId = Convert.ToInt64(r["event_id"]),
                    EventCode = Convert.ToInt32(r["event_code"]),
                    EventName = Convert.ToString(r["event_name"]) ?? ""
                };

                list.Add(e);
            }

            foreach (var r in hookRows) {
                var ackId = Convert.ToInt64(r["ack_id"]);
                await _dal.Ack.MarkRetryAsync(ackId, load).ConfigureAwait(false);

                var e = new LifeCycleHookEvent(
                    instanceId: Convert.ToInt64(r["instance_id"]),
                    defVersionId: 0, // not in dispatch query; optional
                    externalRef: Convert.ToString(r["external_ref"]) ?? "",
                    occurredAt: new DateTimeOffset(DateTime.SpecifyKind((DateTime)r["hook_created"], DateTimeKind.Utc)),
                    ackGuid: Guid.Parse(Convert.ToString(r["ack_guid"]) ?? Guid.Empty.ToString())
                ) {
                    HookId = Convert.ToInt64(r["hook_id"]),
                    StateId = Convert.ToInt64(r["state_id"]),
                    OnEntry = Convert.ToBoolean(r["on_entry"]),
                    HookCode = Convert.ToString(r["route"]) ?? ""
                };

                list.Add(e);
            }

            return list;
        }
    }

}