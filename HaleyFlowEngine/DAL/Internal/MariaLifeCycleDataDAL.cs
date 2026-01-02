using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaLifeCycleDataDAL : MariaDALBase, ILifeCycleDataDAL {
        public MariaLifeCycleDataDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long lifeCycleId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_LC_DATA.GET_BY_ID, load, (ID, lifeCycleId));

        public Task<int> UpsertAsync(long lifeCycleId, string? actor, string? payloadJson, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LC_DATA.UPSERT, load, (ID, lifeCycleId), (ACTOR, actor), (PAYLOAD, payloadJson));

        public Task<int> DeleteAsync(long lifeCycleId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_LC_DATA.DELETE, load, (ID, lifeCycleId));
    }

}
