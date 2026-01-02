using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaAckDispatchDAL : MariaDALBase, IAckDispatchDAL {
        public MariaAckDispatchDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRows> ListPendingLifecycleReadyAsync(int ackStatus, DateTime utcOlderThan, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_DISPATCH.LIST_PENDING_LC_READY, load, (ACK_STATUS, ackStatus), (OLDER_THAN, utcOlderThan));

        public Task<DbRows> ListPendingLifecycleReadyPagedAsync(int ackStatus, DateTime utcOlderThan, int take, int skip, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_DISPATCH.LIST_PENDING_LC_READY_PAGED, load,
                (ACK_STATUS, ackStatus),
                (OLDER_THAN, utcOlderThan),
                (TAKE, take),
                (SKIP, skip)
            );

        public Task<DbRows> ListPendingHookReadyAsync(int ackStatus, DateTime utcOlderThan, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_DISPATCH.LIST_PENDING_HOOK_READY, load, (ACK_STATUS, ackStatus), (OLDER_THAN, utcOlderThan));

        public Task<DbRows> ListPendingHookReadyPagedAsync(int ackStatus, DateTime utcOlderThan, int take, int skip, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_DISPATCH.LIST_PENDING_HOOK_READY_PAGED, load,
                (ACK_STATUS, ackStatus),
                (OLDER_THAN, utcOlderThan),
                (TAKE, take),
                (SKIP, skip)
            );
    }

}
