using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaAckDispatchDAL : MariaDALBase, IAckDispatchDAL {
        public MariaAckDispatchDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRows> ListDueLifecyclePagedAsync(long consumer, int status, int ttlSeconds, int skip, int take, DbExecutionLoad load = default)
     => Db.RowsAsync(QRY_ACK_DISPATCH.LIST_DUE_LC_PAGED, load, (CONSUMER_ID, consumer), (ACK_STATUS, status), (TTL_SECONDS, ttlSeconds), (SKIP, skip), (TAKE, take));

        public Task<DbRows> ListDueHookPagedAsync(long consumer, int status, int ttlSeconds, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_DISPATCH.LIST_DUE_HOOK_PAGED, load, (CONSUMER_ID, consumer), (ACK_STATUS, status), (TTL_SECONDS, ttlSeconds), (SKIP, skip), (TAKE, take));

        public Task<int?> CountDueLifecycleAsync(int status, DbExecutionLoad load = default)
            => Db.ScalarAsync<int?>(QRY_ACK_DISPATCH.COUNT_DUE_LC, load,(ACK_STATUS, status));

        public Task<int?> CountDueHookAsync(int status, DbExecutionLoad load = default)
            => Db.ScalarAsync<int?>(QRY_ACK_DISPATCH.COUNT_DUE_HOOK, load,(ACK_STATUS, status));
    }
}
