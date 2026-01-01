using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK_LC {
        public const string EXISTS = $@"SELECT 1 FROM lc_ack WHERE ack_id = {ACK_ID} AND lc_id = {LC_ID} LIMIT 1;";
        public const string EXISTS_BY_ACK_ID = $@"SELECT 1 FROM lc_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string EXISTS_BY_LC_ID = $@"SELECT 1 FROM lc_ack WHERE lc_id = {LC_ID} LIMIT 1;";

        public const string LIST_BY_ACK_ID = $@"SELECT ack_id, lc_id FROM lc_ack WHERE ack_id = {ACK_ID} ORDER BY lc_id;";
        public const string LIST_BY_LC_ID = $@"SELECT ack_id, lc_id FROM lc_ack WHERE lc_id = {LC_ID} ORDER BY ack_id;";

        public const string ATTACH = $@"INSERT INTO lc_ack (ack_id, lc_id) VALUES ({ACK_ID}, {LC_ID});";
        public const string DETACH = $@"DELETE FROM lc_ack WHERE ack_id = {ACK_ID} AND lc_id = {LC_ID};";

        public const string DELETE_BY_ACK_ID = $@"DELETE FROM lc_ack WHERE ack_id = {ACK_ID};";
        public const string DELETE_BY_LC_ID = $@"DELETE FROM lc_ack WHERE lc_id = {LC_ID};";

        public const string PENDING_ACKS = $@"SELECT a.id AS ack_id, a.consumer, a.ack_status, a.last_retry, a.retry_count, l.id AS lc_id, l.instance_id, l.from_state, l.to_state, l.event, l.created FROM ack a JOIN lc_ack la ON la.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id WHERE a.ack_status = {ACK_STATUS} ORDER BY a.last_retry;";
    }
}
