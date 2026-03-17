using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaLifeCycleTimeoutDAL : MariaDALBase, ILifeCycleTimeoutDAL {
        public MariaLifeCycleTimeoutDAL(IDALUtilBase db) : base(db) { }

        public Task<int> InsertCaseAAsync(long lcId, int? maxRetry, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LC_TIMEOUT.INSERT_CASE_A, load, (LC_ID, lcId), (MAX_RETRY, maxRetry));

        public Task<int> InsertCaseBFirstAsync(long lcId, int? maxRetry, int staleSeconds, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LC_TIMEOUT.INSERT_CASE_B_FIRST, load, (LC_ID, lcId), (MAX_RETRY, maxRetry), (STALE_SECONDS, staleSeconds));

        public Task<int> UpdateCaseBNextAsync(long lcId, int staleSeconds, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LC_TIMEOUT.UPDATE_CASE_B_NEXT, load, (LC_ID, lcId), (STALE_SECONDS, staleSeconds));

        public Task<DbRows> ListDueCaseAPagedAsync(uint excludedInstanceFlagsMask, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_LC_TIMEOUT.LIST_DUE_CASE_A_PAGED, load, (FLAGS, excludedInstanceFlagsMask), (SKIP, skip), (TAKE, take));

        public Task<DbRows> ListDueCaseBPagedAsync(uint excludedInstanceFlagsMask, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_LC_TIMEOUT.LIST_DUE_CASE_B_PAGED, load, (FLAGS, excludedInstanceFlagsMask), (SKIP, skip), (TAKE, take));
    }
}
