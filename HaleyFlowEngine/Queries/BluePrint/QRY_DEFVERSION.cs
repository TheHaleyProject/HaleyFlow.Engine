using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_DEFVERSION {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM def_version WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM def_version WHERE guid = {GUID} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_VERSION = $@"SELECT 1 FROM def_version WHERE parent = {PARENT_ID} AND version = {VERSION} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, guid, parent, version, data, created, modified FROM def_version WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT id, guid, parent, version, data, created, modified FROM def_version WHERE guid = {GUID} LIMIT 1;";
        public const string GET_LATEST_BY_PARENT = $@"SELECT id, guid, parent, version, data, created, modified FROM def_version WHERE parent = {PARENT_ID} ORDER BY version DESC LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT id, guid, parent, version, created, modified FROM def_version WHERE parent = {PARENT_ID} ORDER BY version DESC;";

        public const string INSERT = $@"INSERT INTO def_version (parent, version, data) VALUES ({PARENT_ID}, {VERSION}, {DATA});";
        public const string UPDATE_DATA = $@"UPDATE def_version SET data = {DATA} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM def_version WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM def_version WHERE parent = {PARENT_ID};";
    }
}
