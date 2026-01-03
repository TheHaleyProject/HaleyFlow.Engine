using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaActivityStatusDAL : MariaDALBase, IActivityStatusDAL {
        public MariaActivityStatusDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRows> ListAllAsync(DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACTIVITY_STATUS.LIST_ALL, load);

        public Task<DbRow?> GetByIdAsync(long id, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACTIVITY_STATUS.GET_BY_ID, load, (ID, id));

        public Task<DbRow?> GetByNameAsync(string name, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACTIVITY_STATUS.GET_BY_NAME, load, (NAME, name));

        public Task<long> InsertAsync(string displayName, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ACTIVITY_STATUS.INSERT, load, (DISPLAY_NAME, displayName));
    }
}
