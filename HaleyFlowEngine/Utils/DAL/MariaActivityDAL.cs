using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaActivityDAL : MariaDALBase, IActivityDAL {
        public MariaActivityDAL(IDALUtilBase db) : base(db) { }

        public Task<DbRows> ListAllAsync(DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACTIVITY.LIST_ALL, load);

        public Task<DbRow?> GetByIdAsync(long id, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACTIVITY.GET_BY_ID, load, (ID, id));

        public Task<DbRow?> GetByNameAsync(string name, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACTIVITY.GET_BY_NAME, load, (NAME, name));

        public async Task<long> InsertAsync(string displayName, DbExecutionLoad load = default) {
            var exists = await Db.ScalarAsync<int?>(QRY_ACTIVITY.EXISTS_BY_NAME, load, (NAME, displayName));
            if (exists.HasValue) {
                var row = await Db.RowAsync(QRY_ACTIVITY.GET_BY_NAME, load, (NAME, displayName));
                if (row == null) throw new InvalidOperationException($"activity not found after EXISTS. name={displayName}");
                return row.GetLong("id");
            }
            try {
                return await Db.ScalarAsync<long>(QRY_ACTIVITY.INSERT, load, (DISPLAY_NAME, displayName));
            } catch {
                var row = await Db.RowAsync(QRY_ACTIVITY.GET_BY_NAME, load, (NAME, displayName));
                if (row == null) throw;
                return row.GetLong("id");
            }
        }
    }
}
