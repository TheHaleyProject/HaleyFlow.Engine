using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaActivityDAL : MariaDALBase, IActivityDAL {
        public MariaActivityDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(int id, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACTIVITY.GET_BY_ID, load, (ID, id));

        public Task<DbRow?> GetByNameAsync(string name, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACTIVITY.GET_BY_NAME, load, (NAME, name));

        public Task<DbRows> ListAllAsync(DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACTIVITY.LIST_ALL, load);
    }
}
