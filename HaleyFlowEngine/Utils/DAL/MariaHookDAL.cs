using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.QueryFields;
using static Haley.Internal.KeyConstants;

namespace Haley.Internal {
    internal sealed class MariaHookDAL : MariaDALBase, IHookDAL {
        public MariaHookDAL(IDALUtilBase db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long hookId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK.GET_BY_ID, load, (ID, hookId));

        public async Task<DbRow?> GetByKeyAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, DbExecutionLoad load = default) {
            // Resolve route_id first; if no hook_route row exists the hook can't exist either.
            var routeId = await Db.ScalarAsync<long?>(QRY_HOOK_ROUTE.GET_ID_BY_NAME, load, (ROUTE, route));
            if (routeId == null) return null;
            return await Db.RowAsync(QRY_HOOK.GET_BY_KEY, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId), (ON_ENTRY, onEntry ? 1 : 0), (ROUTE_ID, routeId.Value));
        }

        public async Task<long> UpsertByKeyReturnIdAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, HookType hookType, string? groupName = null, int orderSeq = 1, int ackMode = 0, DbExecutionLoad load = default) {
            // Ensure hook_route (check-then-insert)
            var routeId = await Db.ScalarAsync<long?>(QRY_HOOK_ROUTE.GET_ID_BY_NAME, load, (ROUTE, route));
            if (!routeId.HasValue || routeId.Value <= 0) {
                try {
                    routeId = await Db.ScalarAsync<long>(QRY_HOOK_ROUTE.INSERT, load, (ROUTE, route));
                } catch {
                    routeId = await Db.ScalarAsync<long?>(QRY_HOOK_ROUTE.GET_ID_BY_NAME, load, (ROUTE, route));
                    if (!routeId.HasValue) throw;
                }
            }

            // Ensure hook_group (check-then-insert)
            long? groupId = null;
            if (!string.IsNullOrWhiteSpace(groupName)) {
                groupId = await Db.ScalarAsync<long?>(QRY_HOOK_GROUP.GET_ID_BY_NAME, load, (GROUP_NAME, groupName));
                if (!groupId.HasValue || groupId.Value <= 0) {
                    try {
                        groupId = await Db.ScalarAsync<long>(QRY_HOOK_GROUP.INSERT, load, (GROUP_NAME, groupName));
                    } catch {
                        groupId = await Db.ScalarAsync<long?>(QRY_HOOK_GROUP.GET_ID_BY_NAME, load, (GROUP_NAME, groupName));
                        if (!groupId.HasValue) throw;
                    }
                }
            }

            // Check if hook exists — if so, update blocking/group_id/order_seq/ack_mode and return.
            var existing = await Db.RowAsync(QRY_HOOK.GET_BY_KEY, load,
                (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId),
                (ON_ENTRY, onEntry ? 1 : 0), (ROUTE_ID, routeId.Value));
            if (existing != null) {
                var existingId = existing.GetLong(KEY_ID);
                await Db.ExecAsync(QRY_HOOK.UPDATE_TYPE_AND_GROUP, load,
                    (ID, existingId), (HOOK_TYPE, (int)hookType), (GROUP_ID, (object?)groupId ?? DBNull.Value),
                    (ORDER_SEQ, orderSeq), (ACK_MODE, ackMode));
                return existingId;
            }

            // Try plain insert
            try {
                return await Db.ScalarAsync<long>(QRY_HOOK.INSERT, load,
                    (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId),
                    (ON_ENTRY, onEntry ? 1 : 0), (ROUTE_ID, routeId.Value),
                    (HOOK_TYPE, (int)hookType), (GROUP_ID, (object?)groupId ?? DBNull.Value),
                    (ORDER_SEQ, orderSeq), (ACK_MODE, ackMode));
            } catch {
                // Race condition: re-read and update
                var row = await Db.RowAsync(QRY_HOOK.GET_BY_KEY, load,
                    (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId),
                    (ON_ENTRY, onEntry ? 1 : 0), (ROUTE_ID, routeId.Value));
                if (row == null) throw;
                var rowId = row.GetLong(KEY_ID);
                await Db.ExecAsync(QRY_HOOK.UPDATE_TYPE_AND_GROUP, load,
                    (ID, rowId), (HOOK_TYPE, (int)hookType), (GROUP_ID, (object?)groupId ?? DBNull.Value),
                    (ORDER_SEQ, orderSeq), (ACK_MODE, ackMode));
                return rowId;
            }
        }

        public Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_HOOK.LIST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstanceAndStateAsync(long instanceId, long stateId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_HOOK.LIST_BY_INSTANCE_AND_STATE, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId));

        public Task<int> DeleteAsync(long hookId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_HOOK.DELETE, load, (ID, hookId));

        public Task<int> DeleteByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_HOOK.DELETE_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        // ── Ordered emission support ──────────────────────────────────────

        public Task<DbRow?> GetContextByAckGuidAsync(string ackGuid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK.GET_CONTEXT_BY_ACK_GUID, load, (GUID, ackGuid));

        public Task<DbRow?> GetContextByLcIdAsync(long lcId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK.GET_CONTEXT_BY_LC_ID, load, (LC_ID, lcId));

        public async Task<int> CountIncompleteBlockingInOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, int orderSeq, DbExecutionLoad load = default) {
            var row = await Db.RowAsync(QRY_HOOK.COUNT_INCOMPLETE_BLOCKING_IN_ORDER, load,
                (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId),
                (ON_ENTRY, onEntry ? 1 : 0), (LC_ID, lcId), (ORDER_SEQ, orderSeq));
            return row?.GetInt(KEY_CNT) ?? 0;
        }

        public async Task<int?> GetMinUndispatchedOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, DbExecutionLoad load = default) {
            var row = await Db.RowAsync(QRY_HOOK.GET_MIN_UNDISPATCHED_ORDER, load,
                (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId),
                (ON_ENTRY, onEntry ? 1 : 0), (LC_ID, lcId));
            if (row == null) return null;
            var val = row.GetInt(KEY_NEXT_ORDER);
            return val > 0 ? val : (int?)null;
        }

        public Task<DbRows> ListUndispatchedByOrderAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, int orderSeq, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_HOOK.LIST_UNDISPATCHED_BY_ORDER, load,
                (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId),
                (ON_ENTRY, onEntry ? 1 : 0), (LC_ID, lcId), (ORDER_SEQ, orderSeq));

        public async Task<int> CountPendingBlockingHookAcksAsync(long instanceId, long lcId, DbExecutionLoad load = default) {
            var row = await Db.RowAsync(QRY_HOOK.COUNT_PENDING_BLOCKING_HOOK_ACKS, load,
                (INSTANCE_ID, instanceId), (LC_ID, lcId));
            return row?.GetInt(KEY_CNT) ?? 0;
        }

        public async Task<int> CountUndispatchedBlockingHooksAsync(long instanceId, long lcId, DbExecutionLoad load = default) {
            var row = await Db.RowAsync(QRY_HOOK.COUNT_UNDISPATCHED_BLOCKING_HOOKS, load,
                (INSTANCE_ID, instanceId), (LC_ID, lcId));
            return row?.GetInt(KEY_CNT) ?? 0;
        }

        public Task<int> CancelPendingBlockingHookAckConsumersAsync(long instanceId, long lcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_HOOK.CANCEL_PENDING_BLOCKING_HOOK_ACK_CONSUMERS, load,
                (INSTANCE_ID, instanceId), (LC_ID, lcId), (ACK_STATUS, (int)AckStatus.Cancelled));

        public Task<int> SkipUndispatchedGateHooksAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long lcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_HOOK.SKIP_UNDISPATCHED_GATE_HOOKS, load,
                (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId),
                (ON_ENTRY, onEntry ? 1 : 0), (LC_ID, lcId));

        public Task<DbRow?> GetFirstSkippedGateRouteAsync(long instanceId, long lcId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK.GET_FIRST_SKIPPED_GATE_ROUTE, load, (INSTANCE_ID, instanceId), (LC_ID, lcId));
    }
}
