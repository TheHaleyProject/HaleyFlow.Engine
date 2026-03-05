using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

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

        public async Task<long> UpsertByKeyReturnIdAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, bool blocking, string? groupName = null, DbExecutionLoad load = default) {
            // Ensure hook_route row exists (idempotent) and get its id.
            var routeId = await Db.ScalarAsync<long>(QRY_HOOK_ROUTE.UPSERT_BY_NAME_RETURN_ID, load, (ROUTE, route));
            // Resolve group_id: upsert group row if a name is provided, else pass SQL NULL.
            long? groupId = null;
            if (!string.IsNullOrWhiteSpace(groupName))
                groupId = await Db.ScalarAsync<long>(QRY_HOOK_GROUP.UPSERT_BY_NAME_RETURN_ID, load, (GROUP_NAME, groupName));
            return await Db.ScalarAsync<long>(QRY_HOOK.UPSERT_BY_KEY_RETURN_ID, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId), (ON_ENTRY, onEntry ? 1 : 0), (ROUTE_ID, routeId), (BLOCKING, blocking ? 1 : 0), (GROUP_ID, (object?)groupId ?? DBNull.Value));
        }

        public Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_HOOK.LIST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstanceAndStateAsync(long instanceId, long stateId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_HOOK.LIST_BY_INSTANCE_AND_STATE, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId));

        public Task<int> DeleteAsync(long hookId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_HOOK.DELETE, load, (ID, hookId));

        public Task<int> DeleteByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_HOOK.DELETE_BY_INSTANCE, load, (INSTANCE_ID, instanceId));
    }
}
