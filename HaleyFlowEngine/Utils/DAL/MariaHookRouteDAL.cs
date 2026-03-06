using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaHookRouteDAL : MariaDALBase, IHookRouteDAL {
        public MariaHookRouteDAL(IDALUtilBase db) : base(db) { }

        public Task<long?> GetIdByNameAsync(string name, DbExecutionLoad load = default)
            => Db.ScalarAsync<long?>(QRY_HOOK_ROUTE.GET_ID_BY_NAME, load, (ROUTE, name));

        public async Task<long> UpsertByNameReturnIdAsync(string name, DbExecutionLoad load = default) {
            var id = await GetIdByNameAsync(name, load);
            if (id.HasValue && id.Value > 0) return id.Value;
            try {
                return await Db.ScalarAsync<long>(QRY_HOOK_ROUTE.INSERT, load, (ROUTE, name));
            } catch {
                var id2 = await GetIdByNameAsync(name, load);
                if (id2.HasValue && id2.Value > 0) return id2.Value;
                throw;
            }
        }
    }
}
