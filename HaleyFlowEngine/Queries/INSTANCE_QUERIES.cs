using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_INSTANCE {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM instance WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM instance WHERE guid = lower(trim({GUID})) LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_EXTERNAL_REF = $@"SELECT 1 FROM instance WHERE def_version = {PARENT_ID} AND external_ref = trim({EXTERNAL_REF}) LIMIT 1;";

        // GUID-first lookups
        public const string GET_BY_GUID = $@"SELECT * FROM instance WHERE guid = lower(trim({GUID})) LIMIT 1;";
        public const string GET_BY_PARENT_AND_EXTERNAL_REF = $@"SELECT * FROM instance WHERE def_version = {PARENT_ID} AND external_ref = lower(trim({EXTERNAL_REF})) LIMIT 1;";

        public const string GET_ID_BY_GUID = $@"SELECT id FROM instance WHERE guid = lower(trim({GUID})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM instance WHERE id = {ID} LIMIT 1;";
        public const string GET_GUID_BY_ID = $@"SELECT guid FROM instance WHERE id = {ID} LIMIT 1;";
        public const string GET_ID_BY_PARENT_AND_EXTERNAL_REF = $@"SELECT id FROM instance WHERE def_version = {PARENT_ID} AND external_ref = lower(trim({EXTERNAL_REF})) LIMIT 1;";
        public const string GET_GUID_BY_PARENT_AND_EXTERNAL_REF = $@"SELECT guid FROM instance WHERE def_version = {PARENT_ID} AND external_ref = lower(trim({EXTERNAL_REF})) LIMIT 1;";

        // lists should be chronological newest-first
        public const string LIST_BY_PARENT = $@"SELECT * FROM instance WHERE def_version = {PARENT_ID} ORDER BY created DESC, id DESC;";
        public const string LIST_BY_EXTERNAL_REF = $@"SELECT * FROM instance WHERE external_ref = lower(trim({EXTERNAL_REF})) ORDER BY created DESC, id DESC;";
        public const string LIST_BY_CURRENT_STATE = $@"SELECT * FROM instance WHERE current_state = {STATE_ID} ORDER BY created DESC, id DESC;";

        public const string LIST_WHERE_FLAGS_ANY = $@"SELECT * FROM instance WHERE (flags & {FLAGS}) <> 0 ORDER BY created DESC, id DESC;";
        public const string LIST_WHERE_FLAGS_NONE = $@"SELECT * FROM instance WHERE (flags & {FLAGS}) = 0 ORDER BY created DESC, id DESC;";

        // INSERT returns guid (first-class for external apps)
        public const string INSERT = $@"INSERT INTO instance (def_version, external_ref, current_state, last_event, policy_id, flags) VALUES ({PARENT_ID}, lower(trim({EXTERNAL_REF})), {STATE_ID}, {EVENT_ID}, {POLICY_ID}, {FLAGS}); SELECT guid FROM instance WHERE id = LAST_INSERT_ID() LIMIT 1;";

        // Note: constant name says RETURN_ID, but we now return GUID
        public const string UPSERT_BY_PARENT_AND_EXTERNAL_REF_RETURN_ID = $@"INSERT INTO instance (def_version, external_ref, current_state, last_event, policy_id, flags) VALUES ({PARENT_ID}, lower(trim({EXTERNAL_REF})), {STATE_ID}, {EVENT_ID}, {POLICY_ID}, {FLAGS}) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id); SELECT guid FROM instance WHERE id = LAST_INSERT_ID() LIMIT 1;";
        public const string UPSERT_BY_PARENT_AND_EXTERNAL_REF_RETURN_GUID = $@"INSERT INTO instance (def_version, external_ref, current_state, last_event, policy_id, flags) VALUES ({PARENT_ID}, lower(trim({EXTERNAL_REF})), {STATE_ID}, {EVENT_ID}, {POLICY_ID}, {FLAGS}) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id); SELECT guid AS guid FROM instance WHERE id = LAST_INSERT_ID() LIMIT 1;";

        public const string UPDATE_CURRENT_STATE = $@"UPDATE instance SET current_state = {STATE_ID}, last_event = {EVENT_ID} WHERE id = {ID};";
        public const string UPDATE_CURRENT_STATE_CAS = $@"UPDATE instance SET current_state = {TO_ID}, last_event = {EVENT_ID} WHERE id = {ID} AND current_state = {FROM_ID};";

        public const string SET_FLAGS = $@"UPDATE instance SET flags = {FLAGS} WHERE id = {ID};";
        public const string ADD_FLAGS = $@"UPDATE instance SET flags = (flags | {FLAGS}) WHERE id = {ID};";
        public const string REMOVE_FLAGS = $@"UPDATE instance SET flags = (flags & ~{FLAGS}) WHERE id = {ID};";

        public const string SET_POLICY = $@"UPDATE instance SET policy_id = {POLICY_ID} WHERE id = {ID};";
        public const string SET_EXTERNAL_REF = $@"UPDATE instance SET external_ref = lower(trim({EXTERNAL_REF})) WHERE id = {ID};";

        public const string DELETE = $@"DELETE FROM instance WHERE id = {ID};";

        public const string SET_MESSAGE_BY_ID = @$"UPDATE instance SET message={MESSAGE} WHERE id={INSTANCE_ID};";
        public const string SUSPEND_WITH_MESSAGE_BY_ID = @$"UPDATE instance SET flags=(flags | {FLAGS}), message={MESSAGE} WHERE id={INSTANCE_ID};";
        public const string FAIL_WITH_MESSAGE_BY_ID = @$"UPDATE instance SET flags=(flags | {FLAGS}), message={MESSAGE} WHERE id={INSTANCE_ID};";
        public const string CLEAR_MESSAGE_BY_ID = @$"UPDATE instance SET message=NULL WHERE id={INSTANCE_ID};";
        public const string UNSUSPEND_BY_ID = @$"UPDATE instance SET flags=(flags & ~{FLAGS}) WHERE id={INSTANCE_ID};";
        public const string CLEAR_FAILED_BY_ID = @$"UPDATE instance SET flags=(flags & ~{FLAGS}) WHERE id={INSTANCE_ID};";
        public const string COMPLETE_WITH_MESSAGE_BY_ID = @$"UPDATE instance SET flags=(flags | {FLAGS}), message={MESSAGE} WHERE id={INSTANCE_ID};";
        public const string CLEAR_COMPLETED_BY_ID = @$"UPDATE instance SET flags=(flags & ~{FLAGS}) WHERE id={INSTANCE_ID};";
        public const string ARCHIVE_WITH_MESSAGE_BY_ID = @$"UPDATE instance SET flags=(flags | {FLAGS}), message={MESSAGE} WHERE id={INSTANCE_ID};";
        public const string CLEAR_ARCHIVED_BY_ID = @$"UPDATE instance SET flags=(flags & ~{FLAGS}) WHERE id={INSTANCE_ID};";

        public const string GET_TIMELINE_JSON_BY_INSTANCE_ID = $@"SELECT JSON_OBJECT('instance', JSON_OBJECT('id', i.id, 'guid', i.guid, 'external_ref', i.external_ref, 'def_version', i.def_version, 'current_state', i.current_state, 'last_event', i.last_event, 'created', i.created, 'modified', i.modified), 'timeline', COALESCE((SELECT JSON_ARRAYAGG(t.j) FROM (SELECT JSON_OBJECT('lifecycle_id', l.id, 'created', l.created, 'from_state', sf.display_name, 'to_state', st.display_name, 'event', ev.display_name, 'event_code', ev.code, 'actor', ld.actor, 'activities', COALESCE((SELECT JSON_ARRAYAGG(a.j) FROM (SELECT JSON_OBJECT('runtime_id', r.id, 'lc_id', r.lc_id, 'state', rs.display_name, 'actor_id', r.actor_id, 'activity', act.display_name, 'status', ast.display_name, 'created', r.created, 'modified', r.modified, 'frozen', IF(r.frozen = 1, JSON_EXTRACT('true','$'), JSON_EXTRACT('false','$'))) AS j FROM runtime r JOIN state rs ON rs.id = r.state_id JOIN activity act ON act.id = r.activity JOIN activity_status ast ON ast.id = r.status WHERE r.instance_id = i.id AND r.lc_id = l.id ORDER BY r.modified DESC, r.id DESC) a), JSON_ARRAY())) AS j FROM lifecycle l JOIN state sf ON sf.id = l.from_state JOIN state st ON st.id = l.to_state JOIN events ev ON ev.id = l.event LEFT JOIN lc_data ld ON ld.lc_id = l.id WHERE l.instance_id = i.id ORDER BY l.created DESC, l.id DESC) t), JSON_ARRAY()), 'Other Activities', COALESCE((SELECT JSON_ARRAYAGG(o.j) FROM (SELECT JSON_OBJECT('runtime_id', r.id, 'lc_id', r.lc_id, 'state', rs.display_name, 'actor_id', r.actor_id, 'activity', act.display_name, 'status', ast.display_name, 'created', r.created, 'modified', r.modified, 'frozen', IF(r.frozen = 1, JSON_EXTRACT('true','$'), JSON_EXTRACT('false','$'))) AS j FROM runtime r JOIN state rs ON rs.id = r.state_id JOIN activity act ON act.id = r.activity JOIN activity_status ast ON ast.id = r.status WHERE r.instance_id = i.id AND (r.lc_id = 0 OR NOT EXISTS (SELECT 1 FROM lifecycle l2 WHERE l2.id = r.lc_id AND l2.instance_id = r.instance_id)) ORDER BY r.modified DESC, r.id DESC) o), JSON_ARRAY())) AS json FROM instance i WHERE i.id = {INSTANCE_ID} LIMIT 1;";


        public const string GET_TIMELINE_ROWSET_BY_INSTANCE_ID = $@"SELECT * FROM (SELECT 'timeline' AS block, l.id AS lifecycle_id, l.created AS lifecycle_created, sf.display_name AS from_state, st.display_name AS to_state, ev.display_name AS event, lcd.actor AS actor, r.id AS runtime_id, act.display_name AS activity, r.actor_id AS activity_actor_id, ast.display_name AS status, r.created AS activity_created, r.modified AS activity_modified, (r.frozen = 1) AS frozen, r.lc_id AS lc_id, NULL AS orphan_state FROM lifecycle l JOIN state sf ON sf.id = l.from_state JOIN state st ON st.id = l.to_state JOIN events ev ON ev.id = l.event LEFT JOIN lc_data lcd ON lcd.lc_id = l.id LEFT JOIN runtime r ON r.instance_id = l.instance_id AND r.lc_id = l.id LEFT JOIN activity act ON act.id = r.activity LEFT JOIN activity_status ast ON ast.id = r.status WHERE l.instance_id = {INSTANCE_ID} UNION ALL SELECT 'other_activities' AS block, NULL AS lifecycle_id, NULL AS lifecycle_created, NULL AS from_state, NULL AS to_state, NULL AS event, NULL AS actor, r.id AS runtime_id, act.display_name AS activity, r.actor_id AS activity_actor_id, ast.display_name AS status, r.created AS activity_created, r.modified AS activity_modified, (r.frozen = 1) AS frozen, r.lc_id AS lc_id, s.display_name AS orphan_state FROM runtime r JOIN activity act ON act.id = r.activity JOIN activity_status ast ON ast.id = r.status LEFT JOIN state s ON s.id = r.state_id WHERE r.instance_id = {INSTANCE_ID} AND (r.lc_id = 0 OR NOT EXISTS (SELECT 1 FROM lifecycle l2 WHERE l2.id = r.lc_id AND l2.instance_id = r.instance_id))) x ORDER BY CASE WHEN x.block = 'timeline' THEN 0 ELSE 1 END, x.lifecycle_created DESC, x.activity_modified DESC;";

    }
    internal class QRY_HOOK {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM hook WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_KEY = $@"SELECT 1 FROM hook WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND via_event = {EVENT_ID} AND on_entry = {ON_ENTRY} AND route = {ROUTE} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM hook WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_KEY =
        $@"SELECT * FROM hook WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND via_event = {EVENT_ID} AND on_entry = {ON_ENTRY} AND route = {ROUTE}  LIMIT 1;";
        public const string LIST_BY_INSTANCE = $@"SELECT * FROM hook WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC, id DESC;";
        public const string LIST_BY_INSTANCE_AND_STATE = $@"SELECT * FROM hook WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} ORDER BY created DESC, id DESC;";
        public const string LIST_BY_INSTANCE_STATE_ENTRY = $@"SELECT * FROM hook WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND on_entry = {ON_ENTRY} ORDER BY created DESC, id DESC;";

        public const string INSERT = $@"INSERT INTO hook (instance_id, state_id, via_event, on_entry, route) VALUES ({INSTANCE_ID}, {STATE_ID}, {EVENT_ID}, {ON_ENTRY}, {ROUTE}); SELECT LAST_INSERT_ID() AS id;";

        public const string UPSERT_BY_KEY_RETURN_ID = $@"INSERT INTO hook (instance_id, state_id, via_event, on_entry, route) VALUES ({INSTANCE_ID}, {STATE_ID}, {EVENT_ID}, {ON_ENTRY}, {ROUTE}) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id); SELECT LAST_INSERT_ID() AS id;";

        public const string DELETE = $@"DELETE FROM hook WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM hook WHERE instance_id = {INSTANCE_ID};";
    }
    internal class QRY_LC_DATA {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM lc_data WHERE lc_id = {ID} LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM lc_data WHERE lc_id = {ID} LIMIT 1;";

        // lc_id is the PK; return it as id for consistency
        public const string UPSERT = $@"INSERT INTO lc_data (lc_id, actor, payload) VALUES ({ID}, {ACTOR}, {PAYLOAD}) ON DUPLICATE KEY UPDATE actor = VALUES(actor), payload = VALUES(payload); SELECT {ID} AS id;";

        public const string DELETE = $@"DELETE FROM lc_data WHERE lc_id = {ID};";
    }
    internal class QRY_LIFECYCLE {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM lifecycle WHERE id = {ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM lifecycle WHERE id = {ID} LIMIT 1;";
        public const string GET_LAST_BY_INSTANCE = $@"SELECT * FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC, id DESC LIMIT 1;";

        public const string LIST_BY_INSTANCE = $@"SELECT * FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC, id DESC;";
        public const string LIST_BY_INSTANCE_PAGED = $@"SELECT * FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC, id DESC LIMIT {TAKE} OFFSET {SKIP};";

        public const string INSERT = $@"INSERT INTO lifecycle (from_state, to_state, event, instance_id) VALUES ({FROM_ID}, {TO_ID}, {EVENT_ID}, {INSTANCE_ID}); SELECT LAST_INSERT_ID() AS id;";

        public const string DELETE = $@"DELETE FROM lifecycle WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM lifecycle WHERE instance_id = {INSTANCE_ID};";
    }
    internal class QRY_LC_TIMEOUT {

        public const string EXISTS_BY_LC_ID = $@"SELECT 1 FROM lc_timeout WHERE lc_id = {LC_ID} LIMIT 1;";
        public const string INSERT_IGNORE = $@"INSERT IGNORE INTO lc_timeout (lc_id) VALUES ({LC_ID});";
        public const string DELETE_BY_LC_ID = $@"DELETE FROM lc_timeout WHERE lc_id = {LC_ID};";

        // Due timeouts (no record yet) based on latest lifecycle entry == current state
        // NOTE: requires each instance to have at least one lifecycle row for current_state (recommended: insert creation lifecycle row).
        public const string LIST_DUE_PAGED = $@"SELECT i.id AS instance_id, i.guid AS instance_guid, i.external_ref AS external_ref, i.def_version AS def_version_id, i.current_state AS state_id, l.id AS entry_lc_id, l.created AS entered_at, s.timeout_minutes AS timeout_minutes, s.timeout_mode AS timeout_mode, s.timeout_event AS timeout_event_id, DATE_ADD(l.created, INTERVAL s.timeout_minutes MINUTE) AS due_at FROM instance i JOIN lifecycle l ON l.id = (SELECT MAX(l2.id) FROM lifecycle l2 WHERE l2.instance_id = i.id) JOIN state s ON s.id = i.current_state AND s.def_version = i.def_version LEFT JOIN lc_timeout t ON t.lc_id = l.id WHERE (i.flags & {FLAGS}) = 0 AND l.to_state = i.current_state AND s.timeout_minutes IS NOT NULL AND s.timeout_minutes > 0 AND s.timeout_event IS NOT NULL AND t.lc_id IS NULL AND DATE_ADD(l.created, INTERVAL s.timeout_minutes MINUTE) <= UTC_TIMESTAMP() ORDER BY due_at ASC, i.id ASC LIMIT {TAKE} OFFSET {SKIP};";
    }
}
