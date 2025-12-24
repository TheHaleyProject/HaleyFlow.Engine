using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStoreMaria {

        public Task<IFeedback<List<Dictionary<string, object>>>> GetInstancesWithExpiredTimeouts(int maxBatchSize) =>
      _agw.ReadAsync(_key, QRY_INSTANCE.GET_INSTANCES_WITH_EXPIRED_TIMEOUTS, (MAX_BATCH, maxBatchSize));

        public Task<IFeedback<bool>> PurgeOldLogs(int daysToKeep) =>
            _agw.NonQueryAsync(_key, QRY_MAINTENANCE.PURGE_OLD_LOGS, (RETENTION_DAYS, daysToKeep));

        public Task<IFeedback<bool>> RebuildIndexes() =>
            _agw.NonQueryAsync(_key, QRY_MAINTENANCE.REBUILD_INDEXES);

    }
}
