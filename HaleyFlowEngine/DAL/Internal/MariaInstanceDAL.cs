using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaInstanceDAL : MariaDALBase, IInstanceDAL {
        public MariaInstanceDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByGuidAsync(string guid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_INSTANCE.GET_BY_GUID, load, (GUID, guid));

        public Task<long?> GetIdByGuidAsync(string guid, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_INSTANCE.GET_ID_BY_GUID, load, (GUID, guid));

        public Task<long?> GetIdByKeyAsync(long defVersionId, string externalRef, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_INSTANCE.GET_ID_BY_PARENT_AND_EXTERNAL_REF, load, (PARENT_ID, defVersionId), (EXTERNAL_REF, externalRef));

        public Task<string?> UpsertByKeyReturnGuidAsync(long defVersionId, string externalRef, long currentStateId, long? lastEventId, long policyId, uint flags, DbExecutionLoad load = default)
            => Db.ScalarAsync<string?>(QRY_INSTANCE.UPSERT_BY_PARENT_AND_EXTERNAL_REF_RETURN_GUID, load, (PARENT_ID, defVersionId), (EXTERNAL_REF, externalRef), (STATE_ID, currentStateId), (EVENT_ID, lastEventId), (POLICY_ID, policyId), (FLAGS, flags));

        public Task<int> UpdateCurrentStateCasAsync(long instanceId, long expectedFromStateId, long newToStateId, long? lastEventId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.UPDATE_CURRENT_STATE_CAS, load, (ID, instanceId), (FROM_ID, expectedFromStateId), (TO_ID, newToStateId), (EVENT_ID, lastEventId));

        public Task<int> SetPolicyAsync(long instanceId, long policyId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.SET_POLICY, load, (ID, instanceId), (POLICY_ID, policyId));

        public Task<int> AddFlagsAsync(long instanceId, uint flags, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.ADD_FLAGS, load, (ID, instanceId), (FLAGS, flags));

        public Task<int> RemoveFlagsAsync(long instanceId, uint flags, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_INSTANCE.REMOVE_FLAGS, load, (ID, instanceId), (FLAGS, flags));
    }
}
