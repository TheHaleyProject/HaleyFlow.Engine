using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_DEFINITION {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM definition WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM definition WHERE guid = {GUID} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_NAME = $@"SELECT 1 FROM definition WHERE env = {PARENT_ID} AND name = {NAME} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, guid, env, display_name, name, description, created FROM definition WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT id, guid, env, display_name, name, description, created FROM definition WHERE guid = {GUID} LIMIT 1;";
        public const string GET_BY_PARENT_AND_NAME = $@"SELECT id, guid, env, display_name, name, description, created FROM definition WHERE env = {PARENT_ID} AND name = {NAME} LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT id, guid, env, display_name, name, description, created FROM definition WHERE env = {PARENT_ID} ORDER BY id;";

        // NOTE: definition.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO definition (env, display_name, description) VALUES ({PARENT_ID}, {DISPLAY_NAME}, {DESCRIPTION});";
        public const string UPDATE = $@"UPDATE definition SET env = {PARENT_ID}, display_name = {DISPLAY_NAME}, description = {DESCRIPTION} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM definition WHERE id = {ID};";
    }
}
