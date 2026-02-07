using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaRuntimeDataDAL : MariaDALBase, IRuntimeDataDAL {
        public MariaRuntimeDataDAL(IDALUtilBase db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long runtimeId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_RUNTIME_DATA.GET_BY_ID, load, (ID, runtimeId));

        public Task<int> UpsertAsync(long runtimeId, string? dataJson, string? payloadJson, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_RUNTIME_DATA.UPSERT, load, (ID, runtimeId), (DATA, dataJson), (PAYLOAD, payloadJson));

        public Task<int> DeleteAsync(long runtimeId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_RUNTIME_DATA.DELETE, load, (ID, runtimeId));
    }
}
