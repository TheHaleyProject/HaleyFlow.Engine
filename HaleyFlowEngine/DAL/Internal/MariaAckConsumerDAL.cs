using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaAckConsumerDAL : MariaDALBase, IAckConsumerDAL {
        public MariaAckConsumerDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByKeyAsync(long ackId, long consumer, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK_CONSUMER.GET_BY_KEY, load, (ACK_ID, ackId), (CONSUMER, consumer));

        public Task<DbRow?> GetByAckGuidAndConsumerAsync(string ackGuid, long consumer, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK_CONSUMER.GET_BY_ACK_GUID_AND_CONSUMER, load, (GUID, ackGuid), (CONSUMER_ID, consumer));

        public Task<long> UpsertByAckIdAndConsumerReturnIdAsync(long ackId, long consumer, int status, DbExecutionLoad load = default)
            => Db.ScalarAsync<long>(QRY_ACK_CONSUMER.UPSERT_RETURN_ID, load, (ACK_ID, ackId), (CONSUMER_ID, consumer), (ACK_STATUS, status));

        public Task<int> SetStatusAsync(long ackId, long consumer, int status, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_CONSUMER.SET_STATUS, load, (ACK_ID, ackId), (CONSUMER_ID, consumer), (ACK_STATUS, status));

        public Task<int> SetStatusByGuidAsync(string ackGuid, long consumer, int status, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_CONSUMER.SET_STATUS_BY_GUID, load, (GUID, ackGuid), (CONSUMER_ID, consumer), (ACK_STATUS, status));

        public Task<int> MarkRetryAsync(long ackId, long consumer, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK_CONSUMER.MARK_RETRY, load, (ACK_ID, ackId), (CONSUMER_ID, consumer));

        public Task<DbRows> ListByStatusPagedAsync(int status, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_CONSUMER.LIST_BY_STATUS_PAGED, load, (ACK_STATUS, status), (SKIP, skip), (TAKE, take));

        public Task<DbRows> ListByConsumerAndStatusPagedAsync(long consumer, int status, int skip, int take, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_CONSUMER.LIST_BY_CONSUMER_AND_STATUS_PAGED, load, (CONSUMER_ID, consumer), (ACK_STATUS, status), (SKIP, skip), (TAKE, take));

        public Task<DbRows> ListReadyForRetryAsync(int status, DateTime utcOlderThan, DbExecutionLoad load = default)
            => Db.RowsAsync(QRY_ACK_CONSUMER.LIST_READY_FOR_RETRY, load, (ACK_STATUS, status), (OLDER_THAN, utcOlderThan));
    }
}
