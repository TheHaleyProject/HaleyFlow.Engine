using System.Diagnostics;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACTIVITY_STATUS {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM activity_status WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_NAME = $@"SELECT 1 FROM activity_status WHERE name = lower(trim({NAME})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM activity_status WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_NAME = $@"SELECT * FROM activity_status WHERE name = lower(trim({NAME})) LIMIT 1;";

        public const string LIST_ALL = $@"SELECT * FROM activity_status ORDER BY id;";

        // NOTE: activity_status.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO activity_status (display_name) VALUES ({DISPLAY_NAME}) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id); SELECT LAST_INSERT_ID() AS id;";
        public const string UPDATE = $@"UPDATE activity_status SET display_name = {DISPLAY_NAME} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM activity_status WHERE id = {ID};";
    }
}
