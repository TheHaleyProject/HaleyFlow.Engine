using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaLifeCycleDAL : MariaDALBase, ILifeCycleDAL {
        public MariaLifeCycleDAL(IDALUtilBase db) : base(db) { }

        public Task<long> InsertAsync(long instanceId, long fromStateId, long toStateId, long eventId, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_LIFECYCLE.INSERT, load, (INSTANCE_ID, instanceId), (FROM_ID, fromStateId), (TO_ID, toStateId), (EVENT_ID, eventId));

        public Task<DbRow?> GetLastByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_LIFECYCLE.GET_LAST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstanceAsync(long instanceId, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_LIFECYCLE.LIST_BY_INSTANCE, load, (INSTANCE_ID, instanceId));

        public Task<DbRows> ListByInstancePagedAsync(long instanceId, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_LIFECYCLE.LIST_BY_INSTANCE_PAGED, load, (INSTANCE_ID, instanceId), (SKIP, skip), (TAKE, take));

        public Task<string?> GetTimelineJsonByInstanceIdAsync(long instanceId, DbExecutionLoad load = default)
            => Db.ScalarAsync<string?>(QRY_INSTANCE.GET_TIMELINE_JSON_BY_INSTANCE_ID, load, (INSTANCE_ID, instanceId));
    }
}
