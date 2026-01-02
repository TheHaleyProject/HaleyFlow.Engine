using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal sealed class MariaAckDAL : MariaDALBase, IAckDAL {
        public MariaAckDAL(IWorkFlowDALUtil db) : base(db) { }

        public Task<DbRow?> GetByIdAsync(long ackId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK.GET_BY_ID, load, (ID, ackId));

        public Task<DbRow?> GetByGuidAsync(string ackGuid, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK.GET_BY_GUID, load, (GUID, ackGuid));

        public Task<DbRow?> GetByConsumerAndSourceAsync(long consumerId, long sourceId, DbExecutionLoad load = default)
            => Db.RowAsync(QRY_ACK.GET_BY_CONSUMER_AND_SOURCE, load, (CONSUMER_ID, consumerId), (SOURCE_ID, sourceId));

        public async Task<long> UpsertByConsumerAndSourceReturnIdAsync(long consumerId, long sourceId, int ackStatus, DbExecutionLoad load = default) {
            // NOTE: QRY_ACK.UPSERT_BY_CONSUMER_AND_SOURCE_RETURN_ID must end with: "SELECT LAST_INSERT_ID();"
            var id = await Db.ScalarAsync<long>(QRY_ACK.UPSERT_BY_CONSUMER_AND_SOURCE_RETURN_ID, load,
                (CONSUMER_ID, consumerId),
                (SOURCE_ID, sourceId),
                (ACK_STATUS, ackStatus)
            );

            if (id <= 0) throw new InvalidOperationException("Upsert ack did not return an id.");
            return id;
        }

        public Task<int> SetStatusAsync(long ackId, int ackStatus, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK.SET_STATUS, load, (ID, ackId), (ACK_STATUS, ackStatus));

        public Task<int> MarkRetryAsync(long ackId, DbExecutionLoad load = default)
            => Db.ExecAsync(QRY_ACK.MARK_RETRY, load, (ID, ackId));
    }
}
