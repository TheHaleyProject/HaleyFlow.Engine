using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK_HOOK {
        public const string EXISTS = $@"SELECT 1 FROM hook_ack WHERE ack_id = {ACK_ID} AND hook_id = {HOOK_ID} LIMIT 1;";
        public const string EXISTS_BY_ACK_ID = $@"SELECT 1 FROM hook_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string EXISTS_BY_HOOK_ID = $@"SELECT 1 FROM hook_ack WHERE hook_id = {HOOK_ID} LIMIT 1;";

        public const string LIST_BY_ACK_ID = $@"SELECT ack_id, hook_id FROM hook_ack WHERE ack_id = {ACK_ID} ORDER BY hook_id;";
        public const string LIST_BY_HOOK_ID = $@"SELECT ack_id, hook_id FROM hook_ack WHERE hook_id = {HOOK_ID} ORDER BY ack_id;";

        public const string ATTACH = $@"INSERT INTO hook_ack (ack_id, hook_id) VALUES ({ACK_ID}, {HOOK_ID});";
        public const string DETACH = $@"DELETE FROM hook_ack WHERE ack_id = {ACK_ID} AND hook_id = {HOOK_ID};";

        public const string DELETE_BY_ACK_ID = $@"DELETE FROM hook_ack WHERE ack_id = {ACK_ID};";
        public const string DELETE_BY_HOOK_ID = $@"DELETE FROM hook_ack WHERE hook_id = {HOOK_ID};";

        public const string PENDING_ACKS = $@"SELECT a.id AS ack_id, a.consumer, a.ack_status, a.last_retry, a.retry_count, h.id AS hook_id, h.instance_id, h.state_id, h.via_event, h.on_entry, h.route, h.created FROM ack a JOIN hook_ack ha ON ha.ack_id = a.id JOIN hook h ON h.id = ha.hook_id WHERE a.ack_status = {ACK_STATUS} ORDER BY a.last_retry;";
    }
}
