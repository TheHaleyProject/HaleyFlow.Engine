using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal static class QRY_ENVIRONMENT {

        public const string INSERT =$@"INSERT IGNORE INTO environment (display_name, code) VALUES ({DISPLAY_NAME}, {CODE});
               SELECT id, display_name, name, code FROM environment WHERE code = {CODE} LIMIT 1;";

        public const string UPSERT = $@"INSERT INTO environment (display_name, code) VALUES ({DISPLAY_NAME}, {CODE})
               ON DUPLICATE KEY UPDATE display_name = VALUES(display_name);
               SELECT id, display_name, name, code FROM environment WHERE code = {CODE} LIMIT 1;";

        public const string GET_ALL =$@"SELECT id, display_name, name, code FROM environment ORDER BY display_name;"; // todo: Add pagination later if needed.
        public const string GET_BY_ID = $@"SELECT id, display_name, name, code FROM environment WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_CODE =$@"SELECT id, display_name, name, code FROM environment WHERE code = {CODE} LIMIT 1;";
        public const string GET_BY_NAME =$@"SELECT id, display_name, name, code FROM environment WHERE name = lower(trim({NAME})) LIMIT 1;";

        public const string EXISTS_BY_ID =$@"SELECT 1 FROM environment WHERE id = {ID};";
        public const string EXISTS_BY_CODE =$@"SELECT 1 FROM environment WHERE code = {CODE};";
        public const string EXISTS_BY_NAME =$@"SELECT 1 FROM environment WHERE name = lower(trim({NAME}));";

        public const string UPDATE_DISPLAY_NAME = $@"UPDATE environment SET display_name = {DISPLAY_NAME} WHERE id = {ID};";
        public const string UPDATE = $@"UPDATE environment SET display_name = {DISPLAY_NAME}, code = {CODE} WHERE id = {ID};";
        public const string DELETE_BY_ID = $@"DELETE FROM environment WHERE id = {ID};";
        public const string DELETE_BY_CODE =$@"DELETE FROM environment WHERE code = {CODE};";
    }
}
