using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaInstanceDAL : MariaDALBase, IInstanceDAL {
        public MariaInstanceDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long id, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_INSTANCE.GET_BY_ID, load, (ID, id));

        public Task<DbRow?> GetByGuidAsync(string guid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_INSTANCE.GET_BY_GUID, load, (GUID, guid));

        public Task<DbRow?> GetByParentAndExternalRefAsync(long defVersionId, string externalRef, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_INSTANCE.GET_BY_PARENT_AND_EXTERNAL_REF, load, (PARENT_ID, defVersionId), (EXTERNAL_REF, externalRef));

        public Task<DbRows> ListByParentAsync(long defVersionId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_INSTANCE.LIST_BY_PARENT, load, (PARENT_ID, defVersionId));

        public Task<DbRows> ListByExternalRefAsync(string externalRef, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_INSTANCE.LIST_BY_EXTERNAL_REF, load, (EXTERNAL_REF, externalRef));

        public Task<DbRows> ListByCurrentStateAsync(int stateId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_INSTANCE.LIST_BY_CURRENT_STATE, load, (STATE_ID, stateId));

        public Task<DbRows> ListWhereFlagsAnyAsync(uint flags, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_INSTANCE.LIST_WHERE_FLAGS_ANY, load, (FLAGS, flags));

        public Task<DbRows> ListWhereFlagsNoneAsync(uint flags, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_INSTANCE.LIST_WHERE_FLAGS_NONE, load, (FLAGS, flags));

        public async Task<long> UpsertByParentAndExternalRefReturnIdAsync(long defVersionId, string externalRef, int currentStateId, int lastEventId, int policyId, uint flags, DbExecutionLoad load = default) {
            // NOTE: QRY_INSTANCE.UPSERT_BY_PARENT_AND_EXTERNAL_REF_RETURN_ID must end with: "SELECT LAST_INSERT_ID();"
            var id = await Db.ScalarAsync<long>(QRY_INSTANCE.UPSERT_BY_PARENT_AND_EXTERNAL_REF_RETURN_ID, load,
                (PARENT_ID, defVersionId),
                (EXTERNAL_REF, externalRef),
                (STATE_ID, currentStateId),
                (EVENT_ID, lastEventId),
                (POLICY_ID, policyId),
                (FLAGS, flags)
            );

            if (id <= 0) throw new InvalidOperationException("Upsert instance did not return an id.");
            return id;
        }

        public Task<int> UpdateCurrentStateAsync(long instanceId, int newStateId, int lastEventId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.UPDATE_CURRENT_STATE, load, (ID, instanceId), (STATE_ID, newStateId), (EVENT_ID, lastEventId));

        public Task<int> UpdateCurrentStateCasAsync(long instanceId, int expectedFromStateId, int newToStateId, int lastEventId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.UPDATE_CURRENT_STATE_CAS, load, (ID, instanceId), (FROM_ID, expectedFromStateId), (TO_ID, newToStateId), (EVENT_ID, lastEventId));

        public Task<int> SetFlagsAsync(long instanceId, uint flags, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.SET_FLAGS, load, (ID, instanceId), (FLAGS, flags));

        public Task<int> AddFlagsAsync(long instanceId, uint flags, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.ADD_FLAGS, load, (ID, instanceId), (FLAGS, flags));

        public Task<int> RemoveFlagsAsync(long instanceId, uint flags, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.REMOVE_FLAGS, load, (ID, instanceId), (FLAGS, flags));

        public Task<int> SetPolicyAsync(long instanceId, int policyId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.SET_POLICY, load, (ID, instanceId), (POLICY_ID, policyId));

        public Task<int> SetExternalRefAsync(long instanceId, string externalRef, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.SET_EXTERNAL_REF, load, (ID, instanceId), (EXTERNAL_REF, externalRef));

        public Task<DbRow?> GetByKeyAsync(long defVersionId, string externalRef, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }

        public Task<long> UpsertAsync(long defVersionId, string externalRef, long currentStateId, long? lastEventId, long policyId, uint flags, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }

        public Task<int> UpdateCurrentStateCasAsync(long instanceId, long expectedFromStateId, long newToStateId, long? lastEventId, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }

        public Task<int> SetPolicyAsync(long instanceId, long policyId, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }
    }

}
