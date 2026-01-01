using System.Diagnostics;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_RUNTIME {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM runtime WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_KEY = $@"SELECT 1 FROM runtime WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND activity = {ACTIVITY_ID} AND actor_id = {ACTOR_ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT instance_id, activity, state_id, actor_id, status, created, modified, id FROM runtime WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_KEY = $@"SELECT instance_id, activity, state_id, actor_id, status, created, modified, id FROM runtime WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND activity = {ACTIVITY_ID} AND actor_id = {ACTOR_ID} LIMIT 1;";

        public const string LIST_BY_INSTANCE = $@"SELECT instance_id, activity, state_id, actor_id, status, created, modified, id FROM runtime WHERE instance_id = {INSTANCE_ID} ORDER BY id DESC;";
        public const string LIST_BY_INSTANCE_AND_STATE = $@"SELECT instance_id, activity, state_id, actor_id, status, created, modified, id FROM runtime WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} ORDER BY id DESC;";
        public const string LIST_BY_INSTANCE_STATE_ACTIVITY = $@"SELECT instance_id, activity, state_id, actor_id, status, created, modified, id FROM runtime WHERE instance_id = {INSTANCE_ID} AND state_id = {STATE_ID} AND activity = {ACTIVITY_ID} ORDER BY id DESC;";

        public const string INSERT = $@"INSERT INTO runtime (instance_id, activity, state_id, actor_id, status) VALUES ({INSTANCE_ID}, {ACTIVITY_ID}, {STATE_ID}, {ACTOR_ID}, {STATUS_ID});";
        public const string UPSERT_BY_KEY_RETURN_ID = $@"INSERT INTO runtime (instance_id, activity, state_id, actor_id, status) VALUES ({INSTANCE_ID}, {ACTIVITY_ID}, {STATE_ID}, {ACTOR_ID}, {STATUS_ID}) ON DUPLICATE KEY UPDATE status = VALUES(status), modified = CURRENT_TIMESTAMP(), id = LAST_INSERT_ID(id);";

        public const string SET_STATUS = $@"UPDATE runtime SET status = {STATUS_ID}, modified = CURRENT_TIMESTAMP() WHERE id = {ID};";

        public const string DELETE = $@"DELETE FROM runtime WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM runtime WHERE instance_id = {INSTANCE_ID};";
    }
}
