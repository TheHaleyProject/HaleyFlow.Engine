using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal static class QRY_LC_NEXT {
        // One lc_next row per lifecycle id (lc_next.id == lifecycle.id).
        public const string INSERT =
            $@"INSERT INTO lc_next (id, `next`, ack_id, dispatched)
               VALUES ({LC_ID}, {NEXT_EVENT}, {ACK_ID}, b'0')
               ON DUPLICATE KEY UPDATE
                   `next` = VALUES(`next`),
                   ack_id = VALUES(ack_id);";

        public const string MARK_DISPATCHED =
            $@"UPDATE lc_next
               SET dispatched = b'1'
               WHERE id = {LC_ID};";

        // Same pattern as lc_ack / hook_ack: one complete-event ack per lifecycle.
        public const string ATTACH_ACK =
            $@"INSERT INTO lcn_ack (ack_id, lc_id)
               VALUES ({ACK_ID}, {LC_ID})
               ON DUPLICATE KEY UPDATE ack_id = ack_id;";

        public const string LIST_PENDING =
            $@"SELECT id AS lc_id, `next` AS next_event
               FROM lc_next
               WHERE dispatched = b'0'
               ORDER BY id ASC
               LIMIT {TAKE};";
    }
}
