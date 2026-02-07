using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaAckDAL : MariaDALBase, IAckDAL {
        public MariaAckDAL(IDALUtilBase db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long ackId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK.GET_BY_ID, load, (ID, ackId));

        public Task<DbRow?> GetByGuidAsync(string guid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK.GET_BY_GUID, load, (GUID, guid));

        public Task<long?> GetIdByGuidAsync(string guid, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_ACK.GET_ID_BY_GUID, load, (GUID, guid));

        public Task<DbRow?> InsertReturnRowAsync(DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK.INSERT, load);

        public Task<DbRow?> InsertWithGuidReturnRowAsync(string guid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK.INSERT_WITH_GUID, load, (GUID, guid));

        public Task<int> DeleteAsync(long ackId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK.DELETE, load, (ID, ackId));
    }
}
