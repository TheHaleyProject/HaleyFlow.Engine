using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACTIVITY_STATUS {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM activity_status WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_NAME = $@"SELECT 1 FROM activity_status WHERE name = {NAME} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, display_name, name FROM activity_status WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_NAME = $@"SELECT id, display_name, name FROM activity_status WHERE name = {NAME} LIMIT 1;";

        public const string LIST_ALL = $@"SELECT id, display_name, name FROM activity_status ORDER BY id;";

        public const string INSERT = $@"INSERT INTO activity_status (display_name) VALUES ({DISPLAY_NAME});";
        public const string UPDATE = $@"UPDATE activity_status SET display_name = {DISPLAY_NAME} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM activity_status WHERE id = {ID};";
    }
}
