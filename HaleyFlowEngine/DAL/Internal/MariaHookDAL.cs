using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookDAL : MariaDALBase, IHookDAL {
        public MariaHookDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long hookId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_HOOK.GET_BY_ID, load, (ID, hookId));

        public Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_HOOK.LIST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstanceAndStateAsync(long instanceId, int stateId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_HOOK.LIST_BY_INSTANCE_AND_STATE, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId));

        public Task<DbRows> ListByInstanceStateEntryAsync(long instanceId, int stateId, bool onEntry, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_HOOK.LIST_BY_INSTANCE_STATE_ENTRY, load, (INSTANCE_ID, instanceId), (STATE_ID, stateId), (ON_ENTRY, onEntry));

        public async Task<long> UpsertByKeyReturnIdAsync(long instanceId, int stateId, long? viaEventId, bool onEntry, string hookCode, string? payloadJson, DbExecutionLoad load = default) {
            // Schema has no hook.payload column currently => ignore for now (as per your instruction).
            _ = payloadJson;

            // via_event is NOT NULL in schema, so normalize null -> 0
            var via = viaEventId ?? 0;

            // NOTE: QRY_HOOK.UPSERT_BY_KEY_RETURN_ID must end with: "SELECT LAST_INSERT_ID();"
            var id = await Db.ScalarAsync<long>(QRY_HOOK.UPSERT_BY_KEY_RETURN_ID, load,
                (INSTANCE_ID, instanceId),
                (STATE_ID, stateId),
                (EVENT_ID, via),
                (ON_ENTRY, onEntry),
                (ROUTE, hookCode)
            );

            if (id <= 0) throw new InvalidOperationException("Upsert hook did not return an id.");
            return id;
        }

        public Task<DbRow?> GetByKeyAsync(long instanceId, long stateId, long? viaEventId, bool onEntry, string hookCode, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }

        public Task<long> UpsertByKeyReturnIdAsync(long instanceId, long stateId, long? viaEventId, bool onEntry, string hookCode, string? payloadJson, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }

        public Task<int> DeleteByInstanceAsync(long instanceId, DbExecutionLoad load = default) {
            throw new NotImplementedException();
        }
    }
}
