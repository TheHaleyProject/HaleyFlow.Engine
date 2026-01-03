using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_STATE {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM state WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_NAME = $@"SELECT 1 FROM state WHERE def_version = {PARENT_ID} AND name = lower(trim({NAME})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM state WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_PARENT_AND_NAME = $@"SELECT * FROM state WHERE def_version = {PARENT_ID} AND name = lower(trim({NAME})) LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT * FROM state WHERE def_version = {PARENT_ID} ORDER BY id;";

        // keep join safe: s.* plus unique category aliases (avoid duplicate column keys)
        public const string LIST_BY_PARENT_WITH_CATEGORY = $@"SELECT s.*, c.display_name AS category_display_name, c.name AS category_name FROM state s LEFT JOIN category c ON c.id = s.category WHERE s.def_version = {PARENT_ID} ORDER BY s.id;";

        // NOTE: state.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO state (def_version, display_name, category, flags, timeout_minutes, timeout_mode, timeout_event) VALUES ({PARENT_ID}, {DISPLAY_NAME}, {CATEGORY_ID}, {FLAGS}, {TIMEOUT_MINUTES}, {TIMEOUT_MODE}, {TIMEOUT_EVENT}); SELECT LAST_INSERT_ID() AS id;";
        public const string UPDATE = $@"UPDATE state SET display_name = {DISPLAY_NAME}, category = {CATEGORY_ID}, flags = {FLAGS}, timeout_minutes = {TIMEOUT_MINUTES}, timeout_mode = {TIMEOUT_MODE}, timeout_event = {TIMEOUT_EVENT} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM state WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM state WHERE def_version = {PARENT_ID};";
    }

    internal class QRY_TRANSITION {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM transition WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_KEY = $@"SELECT 1 FROM transition WHERE def_version = {PARENT_ID} AND from_state = {FROM_ID} AND to_state = {TO_ID} AND event = {EVENT_ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM transition WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_KEY = $@"SELECT * FROM transition WHERE def_version = {PARENT_ID} AND from_state = {FROM_ID} AND to_state = {TO_ID} AND event = {EVENT_ID} LIMIT 1;";
        public const string GET_BY_FROM_AND_EVENT = $@"SELECT * FROM transition WHERE def_version = {PARENT_ID} AND from_state = {FROM_ID} AND event = {EVENT_ID} ORDER BY id LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT * FROM transition WHERE def_version = {PARENT_ID} ORDER BY id;";

        public const string INSERT = $@"INSERT INTO transition (def_version, from_state, to_state, event) VALUES ({PARENT_ID}, {FROM_ID}, {TO_ID}, {EVENT_ID}); SELECT LAST_INSERT_ID() AS id;";
        public const string UPDATE = $@"UPDATE transition SET from_state = {FROM_ID}, to_state = {TO_ID}, event = {EVENT_ID} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM transition WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM transition WHERE def_version = {PARENT_ID};";
    }

    internal class QRY_EVENTS {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM events WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_CODE = $@"SELECT 1 FROM events WHERE def_version = {PARENT_ID} AND code = {CODE} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_NAME = $@"SELECT 1 FROM events WHERE def_version = {PARENT_ID} AND name = lower(trim({NAME})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM events WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_PARENT_AND_CODE = $@"SELECT * FROM events WHERE def_version = {PARENT_ID} AND code = {CODE} LIMIT 1;";
        public const string GET_BY_PARENT_AND_NAME = $@"SELECT * FROM events WHERE def_version = {PARENT_ID} AND name = lower(trim({NAME})) LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT * FROM events WHERE def_version = {PARENT_ID} ORDER BY id;";

        // NOTE: events.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO events (def_version, display_name, code) VALUES ({PARENT_ID}, {DISPLAY_NAME}, {CODE}); SELECT LAST_INSERT_ID() AS id;";
        public const string UPDATE = $@"UPDATE events SET display_name = {DISPLAY_NAME}, code = {CODE} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM events WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM events WHERE def_version = {PARENT_ID};";
    }

    internal class QRY_CATEGORY {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM category WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_NAME = $@"SELECT 1 FROM category WHERE name = lower(trim({NAME})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM category WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_NAME = $@"SELECT * FROM category WHERE name = lower(trim({NAME})) LIMIT 1;";

        public const string LIST_ALL = $@"SELECT * FROM category ORDER BY id;";

        // NOTE: category.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO category (display_name) VALUES ({DISPLAY_NAME}); SELECT LAST_INSERT_ID() AS id;";
        public const string UPDATE = $@"UPDATE category SET display_name = {DISPLAY_NAME} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM category WHERE id = {ID};";
    }
}
