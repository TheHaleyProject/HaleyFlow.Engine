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
        public const string INSERT = $@"INSERT INTO state (def_version, display_name, category, flags) VALUES ({PARENT_ID}, {DISPLAY_NAME}, {CATEGORY_ID}, {FLAGS}); SELECT LAST_INSERT_ID() AS id;";

        public const string UPDATE = $@"UPDATE state SET display_name = {DISPLAY_NAME}, category = {CATEGORY_ID}, flags = {FLAGS} WHERE id = {ID};";

        public const string DELETE = $@"DELETE FROM state WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM state WHERE def_version = {PARENT_ID};";
    }
}
