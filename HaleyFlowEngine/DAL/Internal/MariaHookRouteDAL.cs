using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookRouteDAL : MariaDALBase, IHookRouteDAL {
        public MariaHookRouteDAL(IDALUtilBase db) : base(db) { }

        public Task<long?> GetIdByNameAsync(string name, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_HOOK_ROUTE.GET_ID_BY_NAME, load, (ROUTE, name));

        public Task<long> UpsertByNameReturnIdAsync(string name, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_HOOK_ROUTE.UPSERT_BY_NAME_RETURN_ID, load, (ROUTE, name));
    }
}
