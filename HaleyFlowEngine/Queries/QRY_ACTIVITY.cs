using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACTIVITY {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM activity WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_NAME = $@"SELECT 1 FROM activity WHERE name = {NAME} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, display_name, name FROM activity WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_NAME = $@"SELECT id, display_name, name FROM activity WHERE name = {NAME} LIMIT 1;";

        public const string LIST_ALL = $@"SELECT id, display_name, name FROM activity ORDER BY id;";

        public const string INSERT = $@"INSERT INTO activity (display_name) VALUES ({DISPLAY_NAME});";
        public const string UPDATE = $@"UPDATE activity SET display_name = {DISPLAY_NAME} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM activity WHERE id = {ID};";
    }
}
