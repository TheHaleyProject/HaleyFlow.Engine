using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_STATE {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM state WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_NAME = $@"SELECT 1 FROM state WHERE def_version = {PARENT_ID} AND name = {NAME} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, def_version, display_name, name, category, flags, timeout_minutes, timeout_mode, timeout_event, created FROM state WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_PARENT_AND_NAME = $@"SELECT id, def_version, display_name, name, category, flags, timeout_minutes, timeout_mode, timeout_event, created FROM state WHERE def_version = {PARENT_ID} AND name = {NAME} LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT id, def_version, display_name, name, category, flags, timeout_minutes, timeout_mode, timeout_event, created FROM state WHERE def_version = {PARENT_ID} ORDER BY id;";
        public const string LIST_BY_PARENT_WITH_CATEGORY = $@"SELECT s.id, s.def_version, s.display_name, s.name, s.category, c.display_name AS category_display_name, c.name AS category_name, s.flags, s.timeout_minutes, s.timeout_mode, s.timeout_event, s.created FROM state s LEFT JOIN category c ON c.id = s.category WHERE s.def_version = {PARENT_ID} ORDER BY s.id;";

        // NOTE: state.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO state (def_version, display_name, category, flags, timeout_minutes, timeout_mode, timeout_event) VALUES ({PARENT_ID}, {DISPLAY_NAME}, {CATEGORY_ID}, {FLAGS}, {TIMEOUT_MINUTES}, {TIMEOUT_MODE}, {TIMEOUT_EVENT});";
        public const string UPDATE = $@"UPDATE state SET display_name = {DISPLAY_NAME}, category = {CATEGORY_ID}, flags = {FLAGS}, timeout_minutes = {TIMEOUT_MINUTES}, timeout_mode = {TIMEOUT_MODE}, timeout_event = {TIMEOUT_EVENT} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM state WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM state WHERE def_version = {PARENT_ID};";
    }
}
