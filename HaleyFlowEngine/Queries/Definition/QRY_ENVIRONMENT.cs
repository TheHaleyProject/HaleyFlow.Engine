using System.Diagnostics.Tracing;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ENVIRONMENT {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM environment WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_NAME = $@"SELECT 1 FROM environment WHERE name = lower(trim({NAME})) LIMIT 1;";
        public const string EXISTS_BY_CODE = $@"SELECT 1 FROM environment WHERE code = {CODE} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM environment WHERE guid = lower(trim({GUID})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM environment WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_NAME = $@"SELECT * FROM environment WHERE name = lower(trim({NAME})) LIMIT 1;";
        public const string GET_BY_CODE = $@"SELECT * FROM environment WHERE code = {CODE} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT * FROM environment WHERE guid = lower(trim({GUID})) LIMIT 1;";

        public const string LIST_ALL = $@"SELECT * FROM environment ORDER BY id;";

        public const string INSERT = $@"INSERT INTO environment (display_name, code) VALUES ({DISPLAY_NAME}, {CODE}); SELECT LAST_INSERT_ID() AS id;";
        public const string UPDATE = $@"UPDATE environment SET display_name = {DISPLAY_NAME}, code = {CODE} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM environment WHERE id = {ID};";
    }
}
