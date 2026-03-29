using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaLcNextDAL : MariaDALBase, ILcNextDAL {
        public MariaLcNextDAL(IDALUtilBase db) : base(db) { }

        public Task<int> InsertAsync(long lcId, int nextEvent, long? sourceAckId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LC_NEXT.INSERT, load,
                (LC_ID, lcId),
                (NEXT_EVENT, nextEvent),
                (ACK_ID, (object?)sourceAckId ?? DBNull.Value));

        public Task<int?> GetNextEventByLcIdAsync(long lcId, DbExecutionLoad load = default)
            => Db.ScalarAsync<int?>(QRY_LC_NEXT.GET_NEXT_EVENT_BY_LC_ID, load, (LC_ID, lcId));

        public Task<long?> GetDispatchedAckIdByLcIdAsync(long lcId, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_LC_NEXT.GET_DISPATCHED_ACK_ID_BY_LC_ID, load, (LC_ID, lcId));

        public async Task<int> MarkDispatchedAsync(long lcId, long ackId, DbExecutionLoad load = default) {
            var attached = await Db.ExecAsync(QRY_LC_NEXT.ATTACH_ACK, load, (ACK_ID, ackId), (LC_ID, lcId));
            var marked = await Db.ExecAsync(QRY_LC_NEXT.MARK_DISPATCHED, load, (LC_ID, lcId));
            return attached + marked;
        }

        public Task<DbRows> ListPendingAsync(int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_LC_NEXT.LIST_PENDING, load, (TAKE, take));
    }
}
