using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookDAL : MariaDALBase, IHookDAL {
        public MariaHookDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long hookId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK.GET_BY_ID, load, (ID, hookId));

        public Task<DbRow?> GetByKeyAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK.GET_BY_KEY, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId), (ON_ENTRY, onEntry ? 1 : 0), (ROUTE, route));

        public Task<long> UpsertByKeyReturnIdAsync(long instanceId, long stateId, long viaEventId, bool onEntry, string route, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_HOOK.UPSERT_BY_KEY_RETURN_ID, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId), (ON_ENTRY, onEntry ? 1 : 0), (ROUTE, route));

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
