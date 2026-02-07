using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaLcAckDAL : MariaDALBase, ILcAckDAL {
        public MariaLcAckDAL(IDALUtilBase db) : base(db) { }

        public Task<long?> GetAckIdByLcIdAsync(long lcId, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_ACK_LC.GET_ACK_ID_BY_LC_ID, load, (LC_ID, lcId));

        public Task<int> AttachAsync(long ackId, long lcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_LC.ATTACH, load, (ACK_ID, ackId), (LC_ID, lcId));

        public Task<int> DeleteByLcIdAsync(long lcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_LC.DELETE_BY_LC_ID, load, (LC_ID, lcId));
    }
}
