using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public async Task<IFeedback<bool>> Exists(LifeCycleEntity entity, LifeCycleKey key) {
            var result = new Feedback<bool>().SetResult(false);
            var (sql, args) = BuildExists(entity, key);
            var fb = await _agw.ReadSingleAsync(_key, sql, args).ConfigureAwait(false);
            if (!fb.Status) return result.SetStatus(false);
            return result.SetStatus(true).SetResult(fb.Result != null && fb.Result.Count > 0);
        }

        public Task<IFeedback<bool>> Delete(LifeCycleEntity entity, LifeCycleKey key) {
            var (sql, args) = BuildDelete(entity, key);
            return _agw.NonQueryAsync(_key, sql, args);
        }

        public Task<IFeedback<Dictionary<string, object>>> Get(LifeCycleEntity entity, LifeCycleKey key) {
            var (sql, args) = BuildGet(entity, key);
            return _agw.ReadSingleAsync(_key, sql, args);
        }

        public Task<IFeedback<List<Dictionary<string, object>>>> List(LifeCycleEntity entity, LifeCycleKey? scope = null, int skip = 0, int limit = 200) {
            var (sql0, args0) = BuildList(entity, scope);
            var sql = ApplyPaginationIfMissing(sql0);

            var args = new List<(string, object)>(args0 ?? Array.Empty<(string, object)>());
            if (sql.Contains(SKIP, StringComparison.Ordinal) && !HasArg(args, SKIP)) args.Add((SKIP, skip));
            if (sql.Contains(LIMIT, StringComparison.Ordinal) && !HasArg(args, LIMIT)) args.Add((LIMIT, limit));

            return _agw.ReadAsync(_key, sql, args.ToArray());
        }
    }
}
