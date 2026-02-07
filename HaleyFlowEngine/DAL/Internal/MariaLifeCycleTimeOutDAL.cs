using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaLifeCycleTimeoutDAL : MariaDALBase, ILifeCycleTimeoutDAL {
        public MariaLifeCycleTimeoutDAL(IDALUtilBase db) : base(db) { }

        public Task<DbRows> ListDuePagedAsync(uint excludedInstanceFlagsMask, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_LC_TIMEOUT.LIST_DUE_PAGED, load, (FLAGS, excludedInstanceFlagsMask),(SKIP, skip),(TAKE, take));

        public Task<int> InsertIgnoreAsync(long entryLcId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LC_TIMEOUT.INSERT_IGNORE, load, (LC_ID, entryLcId));
    }

}
