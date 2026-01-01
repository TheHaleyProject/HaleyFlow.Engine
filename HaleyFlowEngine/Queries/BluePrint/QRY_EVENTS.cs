using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_EVENTS {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM events WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_CODE = $@"SELECT 1 FROM events WHERE def_version = {PARENT_ID} AND code = {CODE} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_NAME = $@"SELECT 1 FROM events WHERE def_version = {PARENT_ID} AND name = {NAME} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, def_version, display_name, name, code FROM events WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_PARENT_AND_CODE = $@"SELECT id, def_version, display_name, name, code FROM events WHERE def_version = {PARENT_ID} AND code = {CODE} LIMIT 1;";
        public const string GET_BY_PARENT_AND_NAME = $@"SELECT id, def_version, display_name, name, code FROM events WHERE def_version = {PARENT_ID} AND name = {NAME} LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT id, def_version, display_name, name, code FROM events WHERE def_version = {PARENT_ID} ORDER BY id;";

        // NOTE: events.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO events (def_version, display_name, code) VALUES ({PARENT_ID}, {DISPLAY_NAME}, {CODE});";
        public const string UPDATE = $@"UPDATE events SET display_name = {DISPLAY_NAME}, code = {CODE} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM events WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM events WHERE def_version = {PARENT_ID};";
    }
}
