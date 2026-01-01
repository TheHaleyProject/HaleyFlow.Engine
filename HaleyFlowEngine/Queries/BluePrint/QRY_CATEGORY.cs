using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_CATEGORY {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM category WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_NAME = $@"SELECT 1 FROM category WHERE name = {NAME} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, display_name, name FROM category WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_NAME = $@"SELECT id, display_name, name FROM category WHERE name = {NAME} LIMIT 1;";

        public const string LIST_ALL = $@"SELECT id, display_name, name FROM category ORDER BY id;";

        // NOTE: category.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO category (display_name) VALUES ({DISPLAY_NAME});";
        public const string UPDATE = $@"UPDATE category SET display_name = {DISPLAY_NAME} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM category WHERE id = {ID};";
    }
}
