using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_LC_TIMEOUT {

        public const string EXISTS_BY_LC_ID = $@"SELECT 1 FROM lc_timeout WHERE lc_id = {LC_ID} LIMIT 1;";
        public const string INSERT_IGNORE = $@"INSERT INTO lc_timeout (lc_id) VALUES ({LC_ID}) ON DUPLICATE KEY UPDATE lc_id = lc_id;";
        public const string DELETE_BY_LC_ID = $@"DELETE FROM lc_timeout WHERE lc_id = {LC_ID};";

        // Due timeouts (no record yet) based on latest lifecycle entry == current state
        // NOTE: requires each instance to have at least one lifecycle row for current_state 
        public const string LIST_DUE_PAGED = $@"SELECT i.id AS instance_id, i.guid AS instance_guid, i.entity_id AS entity_id, i.def_version AS def_version_id, i.current_state AS state_id, l.id AS entry_lc_id, l.created AS entered_at, tm.duration AS timeout_duration, tm.mode AS timeout_mode, tm.event_code AS timeout_event_code, DATE_ADD(l.created, INTERVAL tm.duration MINUTE) AS due_at FROM instance i JOIN lifecycle l ON l.id = (SELECT MAX(l2.id) FROM lifecycle l2 WHERE l2.instance_id = i.id) JOIN state s ON s.id = i.current_state AND s.def_version = i.def_version JOIN timeouts tm ON tm.policy_id = i.policy_id AND tm.state_name = s.name LEFT JOIN lc_timeout lt ON lt.lc_id = l.id WHERE (i.flags & {FLAGS}) = 0 AND l.to_state = i.current_state AND tm.duration > 0 AND tm.event_code IS NOT NULL AND lt.lc_id IS NULL AND DATE_ADD(l.created, INTERVAL tm.duration MINUTE) <= UTC_TIMESTAMP() ORDER BY due_at ASC, i.id ASC LIMIT {TAKE} OFFSET {SKIP};";

    }
}
