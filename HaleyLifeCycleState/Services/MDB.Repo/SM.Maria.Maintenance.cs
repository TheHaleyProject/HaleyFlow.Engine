using Haley.Abstractions;
using Haley.Internal;
using Haley.Models;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        // Keep PurgeOldLogs using NonQuery (rows affected). If you add NonQueryCountAsync, swap it in.
        public async Task<IFeedback<int>> PurgeOldLogs(int daysToKeep) {
            var fb = new Feedback<int>();
            var res = await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_MAINTENANCE.PURGE_OLD_LOGS }, (RETENTION_DAYS, daysToKeep));
            if (res is int n) return fb.SetStatus(true).SetResult(n);
            return fb.SetMessage("PurgeOldLogs operation did not return a valid deleted count.");
        }

        public Task<IFeedback<int>> CountInstances(int defVersion, int flagsFilter = 0) =>
            _agw.ScalarAsync<int>(_key, QRY_MAINTENANCE.COUNT_INSTANCES, (DEF_VERSION, defVersion), (FLAGS, flagsFilter));

        public async Task<IFeedback> RebuildIndexes() {
            var fb = new Feedback();
            var res = await _agw.NonQueryAsync(_key, QRY_MAINTENANCE.REBUILD_INDEXES);
            if (res.Status) return fb.SetStatus(true).SetMessage("Database indexes optimized successfully.");
            return fb.SetMessage(res.Message);
        }
    }
}
