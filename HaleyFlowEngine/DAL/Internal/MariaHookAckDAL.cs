using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookAckDAL : MariaDALBase, IHookAckDAL {
        public MariaHookAckDAL(IDALUtilBase db) : base(db) { }

        public Task<long?> GetAckIdByHookIdAsync(long hookId, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_ACK_HOOK.GET_ACK_ID_BY_HOOK_ID, load, (HOOK_ID, hookId));

        public Task<int> AttachAsync(long ackId, long hookId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_HOOK.ATTACH, load, (ACK_ID, ackId), (HOOK_ID, hookId));

        public Task<int> DeleteByHookIdAsync(long hookId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_HOOK.DELETE_BY_HOOK_ID, load, (HOOK_ID, hookId));
    }
}
