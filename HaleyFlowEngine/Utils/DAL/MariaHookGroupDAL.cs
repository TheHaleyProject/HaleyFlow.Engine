using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookGroupDAL : MariaDALBase, IHookGroupDAL {
        public MariaHookGroupDAL(IDALUtilBase db) : base(db) { }

        public Task<long?> GetIdByNameAsync(string name, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_HOOK_GROUP.GET_ID_BY_NAME, load, (GROUP_NAME, name));

        public async Task<long> UpsertByNameReturnIdAsync(string name, DbExecutionLoad load = default) {
            var id = await GetIdByNameAsync(name, load);
            if (id.HasValue && id.Value > 0) return id.Value;
            try {
                return await Db.ScalarAsync<long>(QRY_HOOK_GROUP.INSERT, load, (GROUP_NAME, name));
            } catch {
                var id2 = await GetIdByNameAsync(name, load);
                if (id2.HasValue && id2.Value > 0) return id2.Value;
                throw;
            }
        }

        public Task<DbRow?> GetContextByAckGuidAsync(string ackGuid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK_GROUP.GET_CONTEXT_BY_ACK_GUID, load, (GUID, ackGuid));

        public Task<int> CountUnresolvedInGroupAsync(long instanceId, long stateId, long viaEventId, bool onEntry, long groupId, DbExecutionLoad load = default)
            => Db.ScalarAsync<int>(QRY_HOOK_GROUP.COUNT_UNRESOLVED_IN_GROUP, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId), (EVENT_ID, viaEventId), (ON_ENTRY, onEntry ? 1 : 0), (GROUP_ID, groupId));
    }
}
