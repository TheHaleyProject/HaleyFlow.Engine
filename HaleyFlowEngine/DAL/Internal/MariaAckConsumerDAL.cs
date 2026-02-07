using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaAckConsumerDAL : MariaDALBase, IAckConsumerDAL {
        public MariaAckConsumerDAL(IDALUtilBase db) : base(db) { }
        public Task<DbRow?> GetByKeyAsync(long ackId, long consumer, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK_CONSUMER.GET_BY_KEY, load, (ACK_ID, ackId), (CONSUMER_ID, consumer)); // FIXED

        public Task<DbRow?> GetByAckGuidAndConsumerAsync(string ackGuid, long consumer, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK_CONSUMER.GET_BY_ACK_GUID_AND_CONSUMER, load, (GUID, ackGuid), (CONSUMER_ID, consumer));

        public Task<long> UpsertByAckIdAndConsumerReturnIdAsync(long ackId, long consumer, int status, DateTime? utcNextDue, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ACK_CONSUMER.UPSERT_RETURN_ID, load, (ACK_ID, ackId),(CONSUMER_ID, consumer),(ACK_STATUS, status), (NEXT_DUE, utcNextDue));

        public Task<int> SetStatusAndDueAsync(long ackId, long consumer, int status, DateTime? utcNextDue, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_CONSUMER.SET_STATUS_AND_DUE, load, (ACK_ID, ackId), (CONSUMER_ID, consumer),(ACK_STATUS, status), (NEXT_DUE, utcNextDue));

        public Task<int> SetStatusAndDueByGuidAsync(string ackGuid, long consumer, int status, DateTime? utcNextDue, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_CONSUMER.SET_STATUS_AND_DUE_BY_GUID, load, (GUID, ackGuid), (CONSUMER_ID, consumer),(ACK_STATUS, status),(NEXT_DUE, utcNextDue));

        public Task<int> MarkTriggerAsync(long ackId, long consumer, DateTime? utcNextDue, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_CONSUMER.MARK_TRIGGER, load,(ACK_ID, ackId),(CONSUMER_ID, consumer),(NEXT_DUE, utcNextDue));

        public Task<DbRows> ListDueByStatusPagedAsync(int status, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_CONSUMER.LIST_DUE_BY_STATUS_PAGED, load,(ACK_STATUS, status),(SKIP, skip),(TAKE, take));

        public Task<DbRows> ListDueByConsumerAndStatusPagedAsync(long consumer, int status, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_CONSUMER.LIST_DUE_BY_CONSUMER_AND_STATUS_PAGED, load,(CONSUMER_ID, consumer), (ACK_STATUS, status),(SKIP, skip),(TAKE, take));

        // Keep these if you still want “non-due” browsing screens:
        public Task<DbRows> ListByStatusPagedAsync(int status, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_CONSUMER.LIST_BY_STATUS_PAGED, load, (ACK_STATUS, status), (SKIP, skip), (TAKE, take));

        public Task<DbRows> ListByConsumerAndStatusPagedAsync(long consumer, int status, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_CONSUMER.LIST_BY_CONSUMER_AND_STATUS_PAGED, load, (CONSUMER_ID, consumer), (ACK_STATUS, status), (SKIP, skip), (TAKE, take));

        public Task<int> PushNextDueForDownAsync(long consumerId, int ackStatus, int ttlSeconds, int recheckSeconds, DbExecutionLoad load = default) => Db.ExecAsync(QRY_ACK_CONSUMER.PUSH_NEXT_DUE_FOR_DOWN_BY_CONSUMER_AND_STATUS, load, (CONSUMER_ID, consumerId), (ACK_STATUS, ackStatus), (TTL_SECONDS, ttlSeconds), (RECHECK_SECONDS, recheckSeconds));
    }
}
