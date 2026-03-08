using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_HOOK {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM hook WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_KEY = $@"SELECT 1 FROM hook WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND via_event = {EVENT_ID} AND on_entry = {ON_ENTRY} AND route_id = {ROUTE_ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM hook WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_KEY =
        $@"SELECT * FROM hook WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND via_event = {EVENT_ID} AND on_entry = {ON_ENTRY} AND route_id = {ROUTE_ID} LIMIT 1;";
        public const string LIST_BY_INSTANCE = $@"SELECT * FROM hook WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC, id DESC;";
        public const string LIST_BY_INSTANCE_AND_STATE = $@"SELECT * FROM hook WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} ORDER BY created DESC, id DESC;";
        public const string LIST_BY_INSTANCE_STATE_ENTRY = $@"SELECT * FROM hook WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND on_entry = {ON_ENTRY} ORDER BY created DESC, id DESC;";

        // dispatched column removed from hook — it now lives on hook_lc.
        public const string INSERT = $@"INSERT INTO hook (instance_id, state_id, via_event, on_entry, route_id, blocking, group_id, order_seq, ack_mode) VALUES ({INSTANCE_ID}, {STATE_ID}, {EVENT_ID}, {ON_ENTRY}, {ROUTE_ID}, {BLOCKING}, {GROUP_ID}, {ORDER_SEQ}, {ACK_MODE}); SELECT LAST_INSERT_ID() AS id;";

        // group_id, blocking, order_seq and ack_mode are updated on re-emit so that policy changes are reflected.
        public const string UPDATE_BLOCKING_AND_GROUP = $@"UPDATE hook SET blocking = {BLOCKING}, group_id = {GROUP_ID}, order_seq = {ORDER_SEQ}, ack_mode = {ACK_MODE} WHERE id = {ID};";

        // Context query for post-ACK logic (any hook, grouped or not).
        // hook_ack.hook_id now references hook_lc.id — join chain: ack → hook_ack → hook_lc → hook.
        public const string GET_CONTEXT_BY_ACK_GUID =
            $@"SELECT h.id, h.instance_id, h.state_id, h.via_event, h.on_entry,
                      h.blocking, h.ack_mode, h.order_seq, h.group_id, ha.ack_id,
                      hl.id AS hook_lc_id, hl.lc_id,
                      i.guid AS instance_guid, i.def_version AS def_version_id,
                      i.metadata AS metadata,
                      i.entity_id AS entity_id, i.policy_id,
                      dv.parent AS definition_id
               FROM ack a
               JOIN hook_ack ha ON ha.ack_id = a.id
               JOIN hook_lc hl ON hl.id = ha.hook_id
               JOIN hook h ON h.id = hl.hook_id
               JOIN instance i ON i.id = h.instance_id
               JOIN def_version dv ON dv.id = i.def_version
               WHERE a.guid = lower(trim({GUID}))
               LIMIT 1;";

        // Count blocking+dispatched hooks in a given order for the current lifecycle entry
        // where at least one ack_consumer is non-terminal.
        public const string COUNT_INCOMPLETE_BLOCKING_IN_ORDER =
            $@"SELECT COUNT(DISTINCT h.id) AS cnt
               FROM hook h
               JOIN hook_lc hl ON hl.hook_id = h.id AND hl.lc_id = {LC_ID}
               JOIN hook_ack ha ON ha.hook_id = hl.id
               JOIN ack_consumer ac ON ac.ack_id = ha.ack_id
               WHERE h.instance_id = {INSTANCE_ID}
                 AND h.state_id = {STATE_ID}
                 AND h.via_event = {EVENT_ID}
                 AND h.on_entry = {ON_ENTRY}
                 AND h.order_seq = {ORDER_SEQ}
                 AND h.blocking = 1
                 AND hl.dispatched = 1
                 AND ac.status NOT IN (3, 4);";

        // Find the next order_seq that has undispatched hook_lc rows for this lifecycle entry.
        public const string GET_MIN_UNDISPATCHED_ORDER =
            $@"SELECT MIN(h.order_seq) AS next_order
               FROM hook h
               JOIN hook_lc hl ON hl.hook_id = h.id AND hl.lc_id = {LC_ID}
               WHERE h.instance_id = {INSTANCE_ID}
                 AND h.state_id = {STATE_ID}
                 AND h.via_event = {EVENT_ID}
                 AND h.on_entry = {ON_ENTRY}
                 AND hl.dispatched = 0;";

        // List all undispatched hooks for a specific order_seq scope for this lifecycle entry.
        // Returns hook_lc_id so callers can use it for CreateHookAckAsync + MarkDispatchedAsync.
        public const string LIST_UNDISPATCHED_BY_ORDER =
            $@"SELECT h.id, h.blocking, h.ack_mode, h.group_id, h.on_entry,
                      hl.id AS hook_lc_id,
                      hr.name AS route, hg.name AS group_name
               FROM hook h
               JOIN hook_lc hl ON hl.hook_id = h.id AND hl.lc_id = {LC_ID}
               JOIN hook_route hr ON hr.id = h.route_id
               LEFT JOIN hook_group hg ON hg.id = h.group_id
               WHERE h.instance_id = {INSTANCE_ID}
                 AND h.state_id = {STATE_ID}
                 AND h.via_event = {EVENT_ID}
                 AND h.on_entry = {ON_ENTRY}
                 AND h.order_seq = {ORDER_SEQ}
                 AND hl.dispatched = 0;";

        public const string DELETE = $@"DELETE FROM hook WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM hook WHERE instance_id = {INSTANCE_ID};";
    }
}
