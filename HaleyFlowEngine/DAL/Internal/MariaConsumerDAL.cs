using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaConsumerDAL : MariaDALBase, IConsumerDAL {
        public MariaConsumerDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<int?> GetIdByEnvIdAndGuidAsync(int envId, string consumerGuid, DbExecutionLoad load = default)
            => Db.ScalarAsync<int?>(QRY_CONSUMER.GET_ID_BY_ENV_ID_AND_GUID, load, (ENV_ID, envId), (CONSUMER_GUID, consumerGuid));

        public Task<int?> GetIdAliveByEnvIdAndGuidAsync(int envId, string consumerGuid, int ttlSeconds, DbExecutionLoad load = default)
            => Db.ScalarAsync<int?>(QRY_CONSUMER.GET_ID_ALIVE_BY_ENV_ID_AND_GUID, load, (ENV_ID, envId), (CONSUMER_GUID, consumerGuid), (TTL_SECONDS, ttlSeconds));

        public Task<int> UpsertBeatByEnvIdAndGuidAsync(int envId, string consumerGuid, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_CONSUMER.UPSERT_BEAT_BY_ENV_ID_AND_GUID, load, (ENV_ID, envId), (CONSUMER_GUID, consumerGuid));

        public Task<int> UpdateBeatByIdAsync(int consumerId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_CONSUMER.UPDATE_BEAT_BY_ID, load, (CONSUMER_ID, consumerId));

        public async Task<IReadOnlyList<int>> ListAliveIdsByEnvIdAsync(int envId, int ttlSeconds, DbExecutionLoad load = default) {
            var rows = await Db.RowsAsync(QRY_CONSUMER.LIST_ALIVE_IDS_BY_ENV_ID, load, (ENV_ID, envId), (TTL_SECONDS, ttlSeconds));
            return rows.Select(r => (int)r["id"]).ToArray();
        }

        public Task<int> IsAliveByIdAsync(int consumerId, int ttlSeconds, DbExecutionLoad load = default)
            => Db.ScalarAsync<int>(QRY_CONSUMER.IS_ALIVE_BY_ID, load, (CONSUMER_ID, consumerId), (TTL_SECONDS, ttlSeconds));

        public Task<int> IsAliveByEnvIdAndGuidAsync(int envId, string consumerGuid, int ttlSeconds, DbExecutionLoad load = default)
            => Db.ScalarAsync<int>(QRY_CONSUMER.IS_ALIVE_BY_ENV_ID_AND_GUID, load, (ENV_ID, envId), (CONSUMER_GUID, consumerGuid), (TTL_SECONDS, ttlSeconds));

        public Task<DateTime?> GetLastBeatByEnvIdAndGuidAsync(int envId, string consumerGuid, DbExecutionLoad load = default)
            => Db.ScalarAsync<DateTime?>(QRY_CONSUMER.GET_LAST_BEAT_BY_ENV_ID_AND_GUID, load, (ENV_ID, envId), (CONSUMER_GUID, consumerGuid));
        public async Task<int> EnsureByEnvIdAndGuidReturnIdAsync(int envId, string consumerGuid, DbExecutionLoad load = default) {
            var id = await GetIdByEnvIdAndGuidAsync(envId, consumerGuid, load);
            if (id.HasValue && id.Value > 0) return id.Value;

            // Insert OR upsert; choose what you have
            if (!string.IsNullOrWhiteSpace(QRY_CONSUMER.UPSERT_BEAT_BY_ENV_ID_AND_GUID)) {
                await UpsertBeatByEnvIdAndGuidAsync(envId, consumerGuid, load);
            } else {
                await Db.ExecAsync(QRY_CONSUMER.INSERT, load, (ENV_ID, envId), (CONSUMER_GUID, consumerGuid));
            }

            id = await GetIdByEnvIdAndGuidAsync(envId, consumerGuid, load);
            if (!id.HasValue || id.Value <= 0) throw new InvalidOperationException($"consumer ensure failed. env={envId}, guid={consumerGuid}");
            return id.Value;
        }
        public Task<int> PushNextDueForDownAsync(long consumerId, int ackStatus, int ttlSeconds, int recheckSeconds, DbExecutionLoad load = default)
    => Db.ExecAsync(QRY_ACK_CONSUMER.PUSH_NEXT_DUE_FOR_DOWN_BY_CONSUMER_AND_STATUS, load, (CONSUMER_ID, consumerId), (ACK_STATUS, ackStatus), (TTL_SECONDS, ttlSeconds), (RECHECK_SECONDS, recheckSeconds));


    }
}
