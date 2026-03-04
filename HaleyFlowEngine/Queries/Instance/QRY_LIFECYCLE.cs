using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_LIFECYCLE {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM lifecycle WHERE id = {ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM lifecycle WHERE id = {ID} LIMIT 1;";
        public const string GET_LAST_BY_INSTANCE = $@"SELECT * FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC, id DESC LIMIT 1;";

        public const string LIST_BY_INSTANCE = $@"SELECT * FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC, id DESC;";
        public const string LIST_BY_INSTANCE_PAGED = $@"SELECT * FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC, id DESC LIMIT {TAKE} OFFSET {SKIP};";

        // occurred is NULL for normal flow; set only for replay/late-join scenarios
        public const string INSERT = $@"INSERT INTO lifecycle (from_state, to_state, event, instance_id, occurred) VALUES ({FROM_ID}, {TO_ID}, {EVENT_ID}, {INSTANCE_ID}, {OCCURRED}); SELECT LAST_INSERT_ID() AS id;";

        public const string DELETE = $@"DELETE FROM lifecycle WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM lifecycle WHERE instance_id = {INSTANCE_ID};";
    }
}
