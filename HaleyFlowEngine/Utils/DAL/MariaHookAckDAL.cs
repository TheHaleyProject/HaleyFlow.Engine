using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookAckDAL : MariaDALBase, IHookAckDAL {
        public MariaHookAckDAL(IDALUtilBase db) : base(db) { }

        // hook_ack.hook_id references hook_lc.id — all params named hookLcId for clarity.
        public Task<long?> GetAckIdByHookLcIdAsync(long hookLcId, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_ACK_HOOK.GET_ACK_ID_BY_HOOK_LC_ID, load, (HOOK_LC_ID, hookLcId));

        public Task<long?> GetStateIdByAckGuidAsync(string ackGuid, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_ACK_HOOK.GET_STATE_ID_BY_ACK_GUID, load, (GUID, ackGuid));

        public Task<int> AttachAsync(long ackId, long hookLcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_HOOK.ATTACH, load, (ACK_ID, ackId), (HOOK_LC_ID, hookLcId));

        public Task<int> DeleteByHookLcIdAsync(long hookLcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_HOOK.DELETE_BY_HOOK_LC_ID, load, (HOOK_LC_ID, hookLcId));
    }
}
