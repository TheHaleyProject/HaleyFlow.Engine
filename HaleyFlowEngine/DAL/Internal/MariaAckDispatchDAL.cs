using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaAckDispatchDAL : MariaDALBase, IAckDispatchDAL {
        public MariaAckDispatchDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRows> ListPendingLifecycleReadyPagedAsync(long consumer, int status, DateTime utcOlderThan, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_DISPATCH.LIST_PENDING_LC_READY_PAGED, load, (CONSUMER_ID,consumer), (ACK_STATUS, status), (OLDER_THAN, utcOlderThan), (SKIP, skip), (TAKE, take));

        public Task<DbRows> ListPendingHookReadyPagedAsync(long consumer, int status, DateTime utcOlderThan, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_DISPATCH.LIST_PENDING_HOOK_READY_PAGED, load, (ACK_STATUS, status), (OLDER_THAN, utcOlderThan), (SKIP, skip), (TAKE, take));

        public Task<int?> CountPendingLifecycleReadyAsync(int status, DateTime utcOlderThan, DbExecutionLoad load = default)
            => Db.ScalarAsync<int?>(QRY_ACK_DISPATCH.COUNT_PENDING_LC_READY, load, (ACK_STATUS, status), (OLDER_THAN, utcOlderThan));

        public Task<int?> CountPendingHookReadyAsync(int status, DateTime utcOlderThan, DbExecutionLoad load = default)
            => Db.ScalarAsync<int?>(QRY_ACK_DISPATCH.COUNT_PENDING_HOOK_READY, load, (ACK_STATUS, status), (OLDER_THAN, utcOlderThan));
    }
}
