using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal abstract class MariaDALBase {
        protected readonly IWorkFlowDALUtil Db;
        protected MariaDALBase(IWorkFlowDALUtil db) => Db = db;
        protected async Task<bool> ExistsAsync(string existsSql, DbExecutionLoad load, params DbArg[] args)
            => (await Db.ScalarAsync<int>(existsSql, load, args)) == 1;
    }
}
