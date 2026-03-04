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

        public const string INSERT = $@"INSERT INTO hook (instance_id, state_id, via_event, on_entry, route_id, blocking) VALUES ({INSTANCE_ID}, {STATE_ID}, {EVENT_ID}, {ON_ENTRY}, {ROUTE_ID}, {BLOCKING}); SELECT LAST_INSERT_ID() AS id;";

        public const string UPSERT_BY_KEY_RETURN_ID = $@"INSERT INTO hook (instance_id, state_id, via_event, on_entry, route_id, blocking) VALUES ({INSTANCE_ID}, {STATE_ID}, {EVENT_ID}, {ON_ENTRY}, {ROUTE_ID}, {BLOCKING}) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id), blocking = VALUES(blocking); SELECT LAST_INSERT_ID() AS id;";

        public const string DELETE = $@"DELETE FROM hook WHERE id = {ID};";
        public const string DELETE_BY_INSTANCE = $@"DELETE FROM hook WHERE instance_id = {INSTANCE_ID};";
    }
}
