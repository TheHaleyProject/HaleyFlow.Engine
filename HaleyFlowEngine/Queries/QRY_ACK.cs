using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM ack WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM ack WHERE guid = {GUID} LIMIT 1;";
        public const string EXISTS_BY_CONSUMER_AND_SOURCE = $@"SELECT 1 FROM ack WHERE consumer = {CONSUMER_ID} AND source = {SOURCE_ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, guid, consumer, source, ack_status, last_retry, retry_count, created, modified FROM ack WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT id, guid, consumer, source, ack_status, last_retry, retry_count, created, modified FROM ack WHERE guid = {GUID} LIMIT 1;";
        public const string GET_BY_CONSUMER_AND_SOURCE = $@"SELECT id, guid, consumer, source, ack_status, last_retry, retry_count, created, modified FROM ack WHERE consumer = {CONSUMER_ID} AND source = {SOURCE_ID} LIMIT 1;";

        public const string LIST_BY_STATUS_PAGED = $@"SELECT id, guid, consumer, source, ack_status, last_retry, retry_count, created, modified FROM ack WHERE ack_status = {ACK_STATUS} ORDER BY modified LIMIT {TAKE} OFFSET {SKIP};";
        public const string LIST_BY_CONSUMER_AND_STATUS_PAGED = $@"SELECT id, guid, consumer, source, ack_status, last_retry, retry_count, created, modified FROM ack WHERE consumer = {CONSUMER_ID} AND ack_status = {ACK_STATUS} ORDER BY modified LIMIT {TAKE} OFFSET {SKIP};";

        // Retry / monitor helpers
        public const string LIST_READY_FOR_RETRY = $@"SELECT id, guid, consumer, source, ack_status, last_retry, retry_count, created, modified FROM ack WHERE ack_status = {ACK_STATUS} AND last_retry < {OLDER_THAN} ORDER BY last_retry;";
        public const string MARK_RETRY = $@"UPDATE ack SET retry_count = retry_count + 1, last_retry = CURRENT_TIMESTAMP() WHERE id = {ID};";
        public const string SET_STATUS = $@"UPDATE ack SET ack_status = {ACK_STATUS} WHERE id = {ID};";

        public const string INSERT = $@"INSERT INTO ack (consumer, source, ack_status) VALUES ({CONSUMER_ID}, {SOURCE_ID}, {ACK_STATUS});";
        public const string INSERT_WITH_GUID = $@"INSERT INTO ack (guid, consumer, source, ack_status) VALUES ({GUID}, {CONSUMER_ID}, {SOURCE_ID}, {ACK_STATUS});";
        public const string UPSERT_BY_CONSUMER_AND_SOURCE_RETURN_ID = $@"INSERT INTO ack (consumer, source, ack_status) VALUES ({CONSUMER_ID}, {SOURCE_ID}, {ACK_STATUS}) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id);";

        public const string DELETE = $@"DELETE FROM ack WHERE id = {ID};";
        public const string DELETE_BY_CONSUMER_AND_SOURCE = $@"DELETE FROM ack WHERE consumer = {CONSUMER_ID} AND source = {SOURCE_ID};";
    }
}
