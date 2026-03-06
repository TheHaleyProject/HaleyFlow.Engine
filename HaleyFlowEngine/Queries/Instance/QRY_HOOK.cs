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

        public const string INSERT = $@"INSERT INTO hook (instance_id, state_id, via_event, on_entry, route_id, blocking, group_id, order_seq, dispatched, ack_mode) VALUES ({INSTANCE_ID}, {STATE_ID}, {EVENT_ID}, {ON_ENTRY}, {ROUTE_ID}, {BLOCKING}, {GROUP_ID}, {ORDER_SEQ}, {DISPATCHED}, {ACK_MODE}); SELECT LAST_INSERT_ID() AS id;";

        // group_id, blocking, order_seq and ack_mode are updated on re-emit so that policy changes are reflected.
        // dispatched is NOT updated — it is managed internally by the engine.
        public const string UPDATE_BLOCKING_AND_GROUP = $@"UPDATE hook SET blocking = {BLOCKING}, group_id = {GROUP_ID}, order_seq = {ORDER_SEQ}, ack_mode = {ACK_MODE} WHERE id = {ID};";

        // Mark a hook as dispatched (ACK rows created + events fired for it).
        public const string MARK_DISPATCHED = $@"UPDATE hook SET dispatched = 1 WHERE id = {ID};";

        // Context query for post-ACK logic (any hook, grouped or not).
        public const string GET_CONTEXT_BY_ACK_GUID =
            $@"SELECT h.id, h.instance_id, h.state_id, h.via_event, h.on_entry,
                      h.blocking, h.ack_mode, h.order_seq, h.group_id, ha.ack_id,
                      i.guid AS instance_guid, i.def_version AS def_version_id,
                      i.entity_id AS entity_id, i.policy_id,
                      dv.parent AS definition_id
               FROM ack a
               JOIN hook_ack ha ON ha.ack_id = a.id
               JOIN hook h ON h.id = ha.hook_id
               JOIN instance i ON i.id = h.instance_id
               JOIN def_version dv ON dv.id = i.def_version
               WHERE a.guid = lower(trim({GUID}))
               LIMIT 1;";

        // Count blocking+dispatched hooks in a given order where at least one ack_consumer is non-terminal.
        public const string COUNT_INCOMPLETE_BLOCKING_IN_ORDER =
            $@"SELECT COUNT(DISTINCT h.id) AS cnt
               FROM hook h
               JOIN hook_ack ha ON ha.hook_id = h.id
               JOIN ack_consumer ac ON ac.ack_id = ha.ack_id
               WHERE h.instance_id = {INSTANCE_ID}
                 AND h.state_id = {STATE_ID}
                 AND h.via_event = {EVENT_ID}
                 AND h.on_entry = {ON_ENTRY}
                 AND h.order_seq = {ORDER_SEQ}
                 AND h.blocking = 1
                 AND h.dispatched = 1
                 AND ac.status NOT IN (3, 4);";

        // Find the next order_seq that has undispatched hooks.
        public const string GET_MIN_UNDISPATCHED_ORDER =
            $@"SELECT MIN(order_seq) AS next_order
               FROM hook
               WHERE instance_id = {INSTANCE_ID}
                 AND state_id = {STATE_ID}
                 AND via_event = {EVENT_ID}
                 AND on_entry = {ON_ENTRY}
                 AND dispatched = 0;";

        // List all undispatched hooks for a specific order_seq scope, with route and group names.
        public const string LIST_UNDISPATCHED_BY_ORDER =
            $@"SELECT h.id, h.blocking, h.ack_mode, h.group_id, h.on_entry,
                      hr.name AS route, hg.name AS group_name
               FROM hook h
               JOIN hook_route hr ON hr.id = h.route_id
               LEFT JOIN hook_group hg ON hg.id = h.group_id
               WHERE h.instance_id = {INSTANCE_ID}
                 AND h.state_id = {STATE_ID}
                 AND h.via_event = {EVENT_ID}
                 AND h.on_entry = {ON_ENTRY}
                 AND h.order_seq = {ORDER_SEQ}
                 AND h.dispatched = 0;";

        public const string DELETE = $@"DELETE FROM hook WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM hook WHERE instance_id = {INSTANCE_ID};";
    }
}
