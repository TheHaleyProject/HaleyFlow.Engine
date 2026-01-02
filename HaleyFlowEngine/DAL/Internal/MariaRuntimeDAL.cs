using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaRuntimeDAL : MariaDALBase, IRuntimeDAL {
        public MariaRuntimeDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long runtimeId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_RUNTIME.GET_BY_ID, load, (ID, runtimeId));

        public Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_RUNTIME.LIST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstanceAndStateAsync(long instanceId, int stateId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_RUNTIME.LIST_BY_INSTANCE_AND_STATE, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId));

        public Task<DbRows> ListByInstanceStateActivityAsync(long instanceId, int stateId, int activityId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_RUNTIME.LIST_BY_INSTANCE_STATE_ACTIVITY, load,
                (INSTANCE_ID, instanceId),
                (STATE_ID, stateId),
                (ACTIVITY_ID, activityId)
            );
       
        public async Task<long> UpsertByKeyReturnIdAsync(long instanceId, int activityId, int stateId, string actorId, int statusId, DbExecutionLoad load = default) {
            // NOTE: QRY_RUNTIME.UPSERT_BY_KEY_RETURN_ID must end with: "SELECT LAST_INSERT_ID();"
            var id = await Db.ScalarAsync<long>(QRY_RUNTIME.UPSERT_BY_KEY_RETURN_ID, load,
                (INSTANCE_ID, instanceId),
                (ACTIVITY_ID, activityId),
                (STATE_ID, stateId),
                (ACTOR_ID, actorId),
                (STATUS_ID, statusId)
            );

            if (id <= 0) throw new InvalidOperationException("Upsert runtime did not return an id.");
            return id;
        }

        public Task<int> SetStatusAsync(long runtimeId, int statusId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_RUNTIME.SET_STATUS, load, (ID, runtimeId), (STATUS_ID, statusId));

        public Task<int> DeleteByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_RUNTIME.DELETE_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRow?> GetByKeyAsync(long instanceId, long activityId, long stateId, string actorId, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }

        public Task<DbRow?> GetByKeyAsync(long instanceId, int activityId, int stateId, string actorId, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }
    }
}
