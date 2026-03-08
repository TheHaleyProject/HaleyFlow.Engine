using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.QueryFields;
using static Haley.Internal.KeyConstants;

namespace Haley.Internal {
    internal sealed class MariaHookLcDAL : MariaDALBase, IHookLcDAL {
        public MariaHookLcDAL(IDALUtilBase db) : base(db) { }

        public Task<long> InsertReturnIdAsync(long hookId, long lcId, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_HOOK_LC.INSERT, load, (HOOK_ID, hookId), (LC_ID, lcId));

        public Task MarkDispatchedAsync(long hookLcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_HOOK_LC.MARK_DISPATCHED, load, (HOOK_LC_ID, hookLcId));

        public async Task<int> CountDispatchedByHookIdAsync(long hookId, DbExecutionLoad load = default) {
            var row = await Db.RowAsync(QRY_HOOK_LC.COUNT_DISPATCHED_BY_HOOK_ID, load, (HOOK_ID, hookId));
            return row?.GetInt(KEY_CNT) ?? 0;
        }
    }
}
