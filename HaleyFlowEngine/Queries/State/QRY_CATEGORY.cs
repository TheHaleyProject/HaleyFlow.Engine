using static Haley.Internal.QueryFields;

namespace Haley.Internal {
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
