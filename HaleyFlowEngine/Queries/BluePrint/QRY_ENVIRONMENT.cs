using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ENVIRONMENT {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM environment WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_NAME = $@"SELECT 1 FROM environment WHERE name = {NAME} LIMIT 1;";
        public const string EXISTS_BY_CODE = $@"SELECT 1 FROM environment WHERE code = {CODE} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM environment WHERE guid = {GUID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, display_name, name, code, guid FROM environment WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_NAME = $@"SELECT id, display_name, name, code, guid FROM environment WHERE name = {NAME} LIMIT 1;";
        public const string GET_BY_CODE = $@"SELECT id, display_name, name, code, guid FROM environment WHERE code = {CODE} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT id, display_name, name, code, guid FROM environment WHERE guid = {GUID} LIMIT 1;";

        public const string LIST_ALL = $@"SELECT id, display_name, name, code, guid FROM environment ORDER BY id;";

        public const string INSERT = $@"INSERT INTO environment (display_name, code) VALUES ({DISPLAY_NAME}, {CODE});";
        public const string UPDATE = $@"UPDATE environment SET display_name = {DISPLAY_NAME}, code = {CODE} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM environment WHERE id = {ID};";
    }
}
