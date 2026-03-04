using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK_LC {
        public const string EXISTS_BY_ACK_ID = $@"SELECT 1 FROM lc_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string EXISTS_BY_LC_ID = $@"SELECT 1 FROM lc_ack WHERE lc_id = {LC_ID} LIMIT 1;";

        public const string GET_BY_LC_ID = $@"SELECT * FROM lc_ack WHERE lc_id = {LC_ID} LIMIT 1;";
        public const string GET_BY_ACK_ID = $@"SELECT * FROM lc_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string GET_ACK_ID_BY_LC_ID = $@"SELECT ack_id AS id FROM lc_ack WHERE lc_id = {LC_ID} LIMIT 1;";

        // idempotent attach (lc_id is PK)
        public const string ATTACH = $@"INSERT INTO lc_ack (ack_id, lc_id) VALUES ({ACK_ID}, {LC_ID}) ON DUPLICATE KEY UPDATE ack_id = ack_id;";
        public const string DETACH = $@"DELETE FROM lc_ack WHERE lc_id = {LC_ID};";

        public const string DELETE_BY_ACK_ID = $@"DELETE FROM lc_ack WHERE ack_id = {ACK_ID};";
        public const string DELETE_BY_LC_ID = $@"DELETE FROM lc_ack WHERE lc_id = {LC_ID};";

        public const string PENDING_ACKS = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, ac.consumer, ac.status, ac.last_trigger, ac.trigger_count, l.* FROM lc_ack la JOIN ack a ON a.id = la.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id WHERE ac.status = {ACK_STATUS} ORDER BY ac.last_trigger ASC, ac.id ASC;";
    }
}
