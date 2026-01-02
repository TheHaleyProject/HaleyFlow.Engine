using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_LIFECYCLE {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM lifecycle WHERE id = {ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, from_state, to_state, event, created, instance_id FROM lifecycle WHERE id = {ID} LIMIT 1;";
        public const string GET_LAST_BY_INSTANCE = $@"SELECT id, from_state, to_state, event, created, instance_id FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY id DESC LIMIT 1;";
        public const string LIST_BY_INSTANCE = $@"SELECT id, from_state, to_state, event, created, instance_id FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY id DESC;";
        public const string LIST_BY_INSTANCE_PAGED = $@"SELECT id, from_state, to_state, event, created, instance_id FROM lifecycle WHERE instance_id = {INSTANCE_ID} ORDER BY id DESC LIMIT {TAKE} OFFSET {SKIP};";

        public const string INSERT = $@"INSERT INTO lifecycle (from_state, to_state, event, instance_id) VALUES ({FROM_ID}, {TO_ID}, {EVENT_ID}, {INSTANCE_ID});";

        public const string DELETE = $@"DELETE FROM lifecycle WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM lifecycle WHERE instance_id = {INSTANCE_ID};";
    }
}
