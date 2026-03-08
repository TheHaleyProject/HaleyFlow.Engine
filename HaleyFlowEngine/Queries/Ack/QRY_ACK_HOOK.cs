using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK_HOOK {
        // hook_ack.hook_id FK now references hook_lc.id (not hook.id).
        // Use HOOK_LC_ID param everywhere to be explicit about what the column holds.

        public const string EXISTS_BY_ACK_ID = $@"SELECT 1 FROM hook_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string EXISTS_BY_HOOK_LC_ID = $@"SELECT 1 FROM hook_ack WHERE hook_id = {HOOK_LC_ID} LIMIT 1;";

        public const string GET_BY_HOOK_LC_ID = $@"SELECT * FROM hook_ack WHERE hook_id = {HOOK_LC_ID} LIMIT 1;";
        public const string GET_BY_ACK_ID = $@"SELECT * FROM hook_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string GET_ACK_ID_BY_HOOK_LC_ID = $@"SELECT ack_id AS id FROM hook_ack WHERE hook_id = {HOOK_LC_ID} LIMIT 1;";

        // idempotent attach (hook_id / hook_lc.id is PK in hook_ack)
        public const string ATTACH = $@"INSERT INTO hook_ack (ack_id, hook_id) VALUES ({ACK_ID}, {HOOK_LC_ID}) ON DUPLICATE KEY UPDATE ack_id = ack_id;";
        public const string DETACH = $@"DELETE FROM hook_ack WHERE hook_id = {HOOK_LC_ID};";

        public const string DELETE_BY_ACK_ID = $@"DELETE FROM hook_ack WHERE ack_id = {ACK_ID};";
        public const string DELETE_BY_HOOK_LC_ID = $@"DELETE FROM hook_ack WHERE hook_id = {HOOK_LC_ID};";

        // Pending hook dispatch joined through hook_lc to reach hook definition.
        public const string PENDING_ACKS =
            $@"SELECT a.id AS ack_id, a.guid AS ack_guid, ac.consumer, ac.status, ac.last_trigger, ac.trigger_count,
                      h.*, hl.id AS hook_lc_id, hl.lc_id
               FROM hook_ack ha
               JOIN ack a ON a.id = ha.ack_id
               JOIN ack_consumer ac ON ac.ack_id = a.id
               JOIN hook_lc hl ON hl.id = ha.hook_id
               JOIN hook h ON h.id = hl.hook_id
               WHERE ac.status = {ACK_STATUS}
               ORDER BY ac.last_trigger ASC, ac.id ASC;";

        // Resolves the state_id for a hook-type ack by guid.
        // Join chain: ack → hook_ack → hook_lc → hook.
        public const string GET_STATE_ID_BY_ACK_GUID =
            $@"SELECT h.state_id
               FROM ack a
               JOIN hook_ack ha ON ha.ack_id = a.id
               JOIN hook_lc hl ON hl.id = ha.hook_id
               JOIN hook h ON h.id = hl.hook_id
               WHERE a.guid = lower(trim({GUID}))
               LIMIT 1;";
    }
}
