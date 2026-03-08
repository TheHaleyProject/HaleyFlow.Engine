using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaEngineCareDAL : MariaDALBase, IEngineCareDAL {
        public MariaEngineCareDAL(IDALUtilBase db) : base(db) { }

        public Task<long> CountConsumersTotalAsync(DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ENGINE_HEALTH.COUNT_CONSUMERS_TOTAL, load);

        public Task<long> CountConsumersAliveAsync(int ttlSeconds, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ENGINE_HEALTH.COUNT_CONSUMERS_ALIVE, load, (TTL_SECONDS, ttlSeconds));

        public Task<long> CountConsumersDownAsync(int ttlSeconds, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ENGINE_HEALTH.COUNT_CONSUMERS_DOWN, load, (TTL_SECONDS, ttlSeconds));

        public Task<long> CountStaleDefaultStateAsync(uint excludedFlags, int ackStatus, int staleSeconds, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ENGINE_HEALTH.COUNT_STALE_DEFAULT_STATE, load,
                (FLAGS, excludedFlags),
                (ACK_STATUS, ackStatus),
                (STALE_SECONDS, staleSeconds));

        public Task<long> CountTotalInstancesByEnvAsync(int envCode, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ENGINE_HEALTH.COUNT_TOTAL_INSTANCES_BY_ENV, load, (CODE, envCode));

        public Task<long> CountRunningInstancesByEnvAsync(int envCode, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ENGINE_HEALTH.COUNT_RUNNING_INSTANCES_BY_ENV, load, (CODE, envCode));

        public Task<long> CountPendingAcksAsync(DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ENGINE_HEALTH.COUNT_PENDING_ACKS, load);
    }
}
