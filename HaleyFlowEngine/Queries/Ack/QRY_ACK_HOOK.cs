using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK_HOOK {
        public const string EXISTS_BY_ACK_ID = $@"SELECT 1 FROM hook_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string EXISTS_BY_HOOK_ID = $@"SELECT 1 FROM hook_ack WHERE hook_id = {HOOK_ID} LIMIT 1;";

        public const string GET_BY_HOOK_ID = $@"SELECT * FROM hook_ack WHERE hook_id = {HOOK_ID} LIMIT 1;";
        public const string GET_BY_ACK_ID = $@"SELECT * FROM hook_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string GET_ACK_ID_BY_HOOK_ID = $@"SELECT ack_id AS id FROM hook_ack WHERE hook_id = {HOOK_ID} LIMIT 1;";

        // idempotent attach (hook_id is PK)
        public const string ATTACH = $@"INSERT INTO hook_ack (ack_id, hook_id) VALUES ({ACK_ID}, {HOOK_ID}) ON DUPLICATE KEY UPDATE ack_id = ack_id;";
        public const string DETACH = $@"DELETE FROM hook_ack WHERE hook_id = {HOOK_ID};";

        public const string DELETE_BY_ACK_ID = $@"DELETE FROM hook_ack WHERE ack_id = {ACK_ID};";
        public const string DELETE_BY_HOOK_ID = $@"DELETE FROM hook_ack WHERE hook_id = {HOOK_ID};";

        // Pending hook dispatch is now per consumer/status in ack_consumer
        public const string PENDING_ACKS = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, ac.consumer, ac.status, ac.last_trigger, ac.trigger_count, h.* FROM hook_ack ha JOIN ack a ON a.id = ha.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN hook h ON h.id = ha.hook_id WHERE ac.status = {ACK_STATUS} ORDER BY ac.last_trigger ASC, ac.id ASC;";
    }
}
