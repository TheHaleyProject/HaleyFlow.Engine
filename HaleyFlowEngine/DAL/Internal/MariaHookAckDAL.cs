using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookAckDAL : MariaDALBase, IHookAckDAL {
        public MariaHookAckDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<int> AttachAsync(long ackId, long hookId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_HOOK.ATTACH, load, (ACK_ID, ackId), (HOOK_ID, hookId));

        public Task<int> DetachAsync(long ackId, long hookId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_HOOK.DETACH, load, (ACK_ID, ackId), (HOOK_ID, hookId));

        public Task<int> DeleteByAckIdAsync(long ackId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_HOOK.DELETE_BY_ACK_ID, load, (ACK_ID, ackId));

        public Task<int> DeleteByHookIdAsync(long hookId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_HOOK.DELETE_BY_HOOK_ID, load, (HOOK_ID, hookId));
    }
}
