using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaLcAckDAL : MariaDALBase, ILcAckDAL {
        public MariaLcAckDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<int> AttachAsync(long ackId, long lcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_LC.ATTACH, load, (ACK_ID, ackId), (LC_ID, lcId));

        public Task<int> DetachAsync(long ackId, long lcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_LC.DETACH, load, (ACK_ID, ackId), (LC_ID, lcId));

        public Task<int> DeleteByAckIdAsync(long ackId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_LC.DELETE_BY_ACK_ID, load, (ACK_ID, ackId));

        public Task<int> DeleteByLcIdAsync(long lcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_LC.DELETE_BY_LC_ID, load, (LC_ID, lcId));
    }

}
