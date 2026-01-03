using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaRuntimeDAL : MariaDALBase, IRuntimeDAL {
        public MariaRuntimeDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long runtimeId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_RUNTIME.GET_BY_ID, load, (ID, runtimeId));

        public Task<DbRow?> GetByKeyAsync(long instanceId, long activityId, long stateId, string actorId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_RUNTIME.GET_BY_KEY, load, (INSTANCE_ID, instanceId), (ACTIVITY_ID, activityId), (STATE_ID, stateId), (ACTOR_ID, actorId));

        public Task<long> UpsertByKeyReturnIdAsync(long instanceId, long activityId, long stateId, string actorId, long statusId, long lcId, bool frozen, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_RUNTIME.UPSERT_BY_KEY_RETURN_ID, load, (INSTANCE_ID, instanceId), (ACTIVITY_ID, activityId), (STATE_ID, stateId), (ACTOR_ID, actorId), (STATUS_ID, statusId), (LC_ID, lcId), (FROZEN, frozen ? 1 : 0));

        // IMPORTANT: your SQL should enforce "AND frozen = 0" (so SetStatus becomes a safe no-op when frozen)
        public Task<int> SetStatusAsync(long runtimeId, long statusId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_RUNTIME.SET_STATUS, load, (ID, runtimeId), (STATUS_ID, statusId));

        public Task<int> SetFrozenAsync(long runtimeId, bool frozen, DbExecutionLoad load = default) {
            if (frozen) {
                return Db.ExecAsync(QRY_RUNTIME.FREEZE, load, (ID, runtimeId));
            } else {
                return Db.ExecAsync(QRY_RUNTIME.UNFREEZE, load, (ID, runtimeId));
            }
        }

        public Task<int> SetLcIdAsync(long runtimeId, long lcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_RUNTIME.SET_LC_ID, load, (ID, runtimeId), (LC_ID, lcId));

        public Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_RUNTIME.LIST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstanceAndStateAsync(long instanceId, long stateId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_RUNTIME.LIST_BY_INSTANCE_AND_STATE, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId));

        public Task<DbRows> ListByInstanceStateActivityAsync(long instanceId, long stateId, long activityId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_RUNTIME.LIST_BY_INSTANCE_STATE_ACTIVITY, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId), (ACTIVITY_ID, activityId));

        public Task<DbRows> ListByLifeCycleAsync(long lcId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_RUNTIME.LIST_BY_LIFECYCLE, load, (LC_ID, lcId));
    }
}
