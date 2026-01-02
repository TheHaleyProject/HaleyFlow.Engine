using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaLifeCycleDAL : MariaDALBase, ILifeCycleDAL {
        public MariaLifeCycleDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long id, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_LIFECYCLE.GET_BY_ID, load, (ID, id));

        public Task<DbRow?> GetLastByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_LIFECYCLE.GET_LAST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_LIFECYCLE.LIST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstancePagedAsync(long instanceId, int take, int skip, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_LIFECYCLE.LIST_BY_INSTANCE_PAGED, load, (INSTANCE_ID, instanceId), (TAKE, take), (SKIP, skip));
        public async Task<long> InsertAsync(long instanceId, int fromStateId, int toStateId, int eventId,  DbExecutionLoad load = default) {
            // NOTE: QRY_LIFECYCLE.INSERT must be an INSERT that returns id (append: "SELECT LAST_INSERT_ID();")
            var id = await Db.ScalarAsync<long>(QRY_LIFECYCLE.INSERT, load,
                (FROM_ID, fromStateId),
                (TO_ID, toStateId),
                (EVENT_ID, eventId),
                (INSTANCE_ID, instanceId)
            );

            if (id <= 0) throw new InvalidOperationException("Insert lifecycle did not return an id.");
            return id;
        }

        public Task<int> DeleteAsync(long id, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LIFECYCLE.DELETE, load, (ID, id));

        public Task<int> DeleteByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LIFECYCLE.DELETE_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

       
    }
}
