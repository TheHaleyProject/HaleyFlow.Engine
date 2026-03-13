using System.Diagnostics;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_RUNTIME {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM runtime WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_KEY = $@"SELECT 1 FROM runtime WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND activity = {ACTIVITY_ID} AND actor_id = trim({ACTOR_ID}) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM runtime WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_KEY = $@"SELECT * FROM runtime WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND activity = {ACTIVITY_ID} AND actor_id = trim({ACTOR_ID}) LIMIT 1;";

        // list newest activity updates first
        public const string LIST_BY_INSTANCE = $@"SELECT * FROM runtime WHERE instance_id = {INSTANCE_ID} ORDER BY modified DESC, id DESC;";
        public const string LIST_BY_INSTANCE_AND_STATE = $@"SELECT * FROM runtime WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} ORDER BY modified DESC, id DESC;";
        public const string LIST_BY_INSTANCE_STATE_ACTIVITY = $@"SELECT * FROM runtime WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND activity = {ACTIVITY_ID} ORDER BY modified DESC, id DESC;";

        // (new) list by lifecycle
        public const string LIST_BY_LIFECYCLE = $@"SELECT * FROM runtime WHERE lc_id = {LC_ID} ORDER BY modified DESC, id DESC;";

        public const string INSERT = $@"INSERT INTO runtime (instance_id, activity, state_id, actor_id, status, frozen, lc_id) VALUES ({INSTANCE_ID}, {ACTIVITY_ID}, {STATE_ID}, trim({ACTOR_ID}), {STATUS_ID}, {FROZEN}, {LC_ID}); SELECT LAST_INSERT_ID() AS id;";

        // If frozen=1 => do not change status/lc_id/modified; still return id
        public const string UPSERT_BY_KEY_RETURN_ID = $@"INSERT INTO runtime (instance_id, activity, state_id, actor_id, status, frozen, lc_id) VALUES ({INSTANCE_ID}, {ACTIVITY_ID}, {STATE_ID}, trim({ACTOR_ID}), {STATUS_ID}, {FROZEN}, {LC_ID}) ON DUPLICATE KEY UPDATE status = IF(frozen = 0, VALUES(status), status), lc_id = IF(frozen = 0 AND VALUES(lc_id) <> 0, VALUES(lc_id), lc_id), frozen = IF(frozen = 0, VALUES(frozen), frozen), modified = IF(frozen = 0, CURRENT_TIMESTAMP(), modified), id = LAST_INSERT_ID(id); SELECT LAST_INSERT_ID() AS id;";

        public const string SET_STATUS = $@"UPDATE runtime SET status = {STATUS_ID}, modified = CURRENT_TIMESTAMP() WHERE id = {ID} AND frozen = 0;";
        public const string SET_LC_ID = $@"UPDATE runtime SET lc_id = {LC_ID}, modified = CURRENT_TIMESTAMP() WHERE id = {ID} AND frozen = 0;";
        // After a transition succeeds, stamp lc_id on all runtime rows for the state that was just closed.
        // Only updates rows where lc_id = 0 so already-linked rows are never overwritten.
        public const string STAMP_LC_ID_BY_INSTANCE_AND_STATE = $@"UPDATE runtime SET lc_id = {LC_ID} WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND lc_id = 0;";

        public const string FREEZE = $@"UPDATE runtime SET frozen = 1, modified = CURRENT_TIMESTAMP() WHERE id = {ID};";
        public const string UNFREEZE = $@"UPDATE runtime SET frozen = 0, modified = CURRENT_TIMESTAMP() WHERE id = {ID};";

        // Flat list for TimelineBuilder — activities with joined names + label, ordered by lc_id then id.
        public const string LIST_FOR_TIMELINE =
            $@"SELECT r.id AS runtime_id, r.lc_id,
                      act.display_name AS activity, COALESCE(hr.label, '') AS label,
                      r.actor_id, ast.display_name AS status,
                      r.created, r.modified, r.frozen
               FROM runtime r
               JOIN activity act ON act.id = r.activity
               JOIN activity_status ast ON ast.id = r.status
               LEFT JOIN hook_route hr ON hr.name = act.display_name
               WHERE r.instance_id = {INSTANCE_ID}
               ORDER BY r.lc_id ASC, r.id ASC;";

        public const string DELETE = $@"DELETE FROM runtime WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM runtime WHERE instance_id = {INSTANCE_ID};";
    }
}
