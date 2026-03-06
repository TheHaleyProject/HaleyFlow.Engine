using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_HOOK_GROUP {
        // Global lookup: one row per unique group name (e.g. "eval_group").
        // hook.group_id FK points here. Group name is stamped at hook creation time;
        // policy changes to group names only affect new instances.

        public const string GET_ID_BY_NAME = $@"SELECT id FROM hook_group WHERE name = {GROUP_NAME} LIMIT 1;";

        public const string INSERT = $@"INSERT INTO hook_group (name) VALUES ({GROUP_NAME}); SELECT LAST_INSERT_ID() AS id;";

        // Returns hook + group context for a given ack guid.
        // Used in AckAsync (post-Processed) to determine whether a group completion notice should fire.
        // Returns NULL if the ack is not a hook ack, or the hook has no group_id (no INNER JOIN to hook_group means ungrouped hooks return no row).
        public const string GET_CONTEXT_BY_ACK_GUID = $@"SELECT h.id AS hook_id, h.group_id, hg.name AS group_name, h.instance_id, h.state_id, h.via_event, h.on_entry, i.guid AS instance_guid FROM ack a JOIN hook_ack ha ON ha.ack_id = a.id JOIN hook h ON h.id = ha.hook_id JOIN hook_group hg ON hg.id = h.group_id JOIN instance i ON i.id = h.instance_id WHERE a.guid = {GUID} LIMIT 1;";

        // Count ack_consumer rows for all hooks in the same group+context that are NOT yet terminal (Processed=3, Failed=4).
        // If this returns 0, the entire group is done.
        public const string COUNT_UNRESOLVED_IN_GROUP = $@"SELECT COUNT(*) AS cnt FROM hook h JOIN hook_ack ha ON ha.hook_id = h.id JOIN ack_consumer ac ON ac.ack_id = ha.ack_id WHERE h.instance_id = {INSTANCE_ID} AND h.state_id = {STATE_ID} AND h.via_event = {EVENT_ID} AND h.on_entry = {ON_ENTRY} AND h.group_id = {GROUP_ID} AND ac.status NOT IN (3, 4);";
    }
}
