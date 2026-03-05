using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookGroupDAL : MariaDALBase, IHookGroupDAL {
        public MariaHookGroupDAL(IDALUtilBase db) : base(db) { }

        public Task<long?> GetIdByNameAsync(string name, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_HOOK_GROUP.GET_ID_BY_NAME, load, (GROUP_NAME, name));

        public Task<long> UpsertByNameReturnIdAsync(string name, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_HOOK_GROUP.UPSERT_BY_NAME_RETURN_ID, load, (GROUP_NAME, name));

        public Task<DbRow?> GetContextByAckGuidAsync(string ackGuid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK_GROUP.GET_CONTEXT_BY_ACK_GUID, load, (GUID, ackGuid));

        public Task<int> CountUnresolvedInGroupAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long groupId, DbExecutionLoad load = default)
            => Db.ScalarAsync<int>(QRY_HOOK_GROUP.COUNT_UNRESOLVED_IN_GROUP, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId), (ON_ENTRY, onEntry ? 1 : 0), (GROUP_ID, groupId));
    }
}
