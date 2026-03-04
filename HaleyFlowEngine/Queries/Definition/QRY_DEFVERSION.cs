using System.Diagnostics.Tracing;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_DEFVERSION {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM def_version WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM def_version WHERE guid = lower(trim({GUID})) LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_VERSION = $@"SELECT 1 FROM def_version WHERE parent = {PARENT_ID} AND version = {VERSION} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT dv.*, e.code AS env_code, d.name AS def_name FROM def_version dv JOIN definition d ON d.id = dv.parent JOIN environment e ON e.id = d.env WHERE dv.id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT dv.*, e.code AS env_code, d.name AS def_name FROM def_version dv JOIN definition d ON d.id = dv.parent JOIN environment e ON e.id = d.env WHERE dv.guid = lower(trim({GUID})) LIMIT 1;";
        public const string GET_LATEST_BY_PARENT = $@"SELECT dv.*, e.code AS env_code, d.name AS def_name FROM def_version dv JOIN definition d ON d.id = dv.parent JOIN environment e ON e.id = d.env WHERE dv.parent = {PARENT_ID} ORDER BY dv.version DESC LIMIT 1;";
        public const string GET_BY_PARENT_AND_HASH = $@"SELECT dv.*, e.code AS env_code, d.name AS def_name FROM def_version dv JOIN definition d ON d.id = dv.parent JOIN environment e ON e.id = d.env WHERE dv.parent = {PARENT_ID} AND dv.hash = lower(trim({HASH})) LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT * FROM def_version WHERE parent = {PARENT_ID} ORDER BY version DESC;";

        public const string INSERT = $@"INSERT INTO def_version (parent, version, data,hash) VALUES ({PARENT_ID}, {VERSION}, {DATA},lower(trim({HASH}))); SELECT LAST_INSERT_ID() AS id;";
        public const string UPDATE_DATA = $@"UPDATE def_version SET data = {DATA} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM def_version WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM def_version WHERE parent = {PARENT_ID};";

        // 1) env (CODE) + definition NAME => latest def_version
        public const string GET_LATEST_BY_ENV_CODE_AND_DEF_NAME = $@"SELECT dv.*, e.code AS env_code, d.name AS def_name FROM def_version dv JOIN definition d ON d.id = dv.parent JOIN environment e ON e.id = d.env WHERE e.code = {CODE} AND d.name = lower(trim({NAME})) ORDER BY dv.version DESC LIMIT 1;";

        // 1b) env (NAME) + definition NAME => latest def_version
        // NOTE: using DISPLAY_NAME placeholder to pass env-name token (since env.name is generated from display_name)
        public const string GET_LATEST_BY_ENV_NAME_AND_DEF_NAME = $@"SELECT dv.*,e.code as env_code, d.name as def_name FROM def_version dv JOIN definition d ON d.id = dv.parent JOIN environment e ON e.id = d.env WHERE e.name = lower(trim({ENV_NAME})) AND d.name = lower(trim({DEF_NAME})) ORDER BY dv.version DESC LIMIT 1;";

        // 2) definition GUID => latest def_version
        public const string GET_LATEST_BY_DEFINITION_GUID = $@"SELECT dv.* FROM def_version dv JOIN definition d ON d.id = dv.parent WHERE d.guid = lower(trim({GUID})) ORDER BY dv.version DESC LIMIT 1;";

        // 3) def_version ID => latest def_version under the same parent
        public const string GET_LATEST_BY_LINE_FROM_DEFVERSION_ID = $@"SELECT dv2.* FROM def_version dv1 JOIN def_version dv2 ON dv2.parent = dv1.parent WHERE dv1.id = {ID} ORDER BY dv2.version DESC LIMIT 1;";

        // 3b) def_version GUID => latest def_version under the same parent
        public const string GET_LATEST_BY_LINE_FROM_DEFVERSION_GUID = $@"SELECT dv2.* FROM def_version dv1 JOIN def_version dv2 ON dv2.parent = dv1.parent WHERE dv1.guid = lower(trim({GUID})) ORDER BY dv2.version DESC LIMIT 1;";

        // 4) env CODE + definition NAME => next version number (max+1)
        public const string GET_NEXT_VERSION_BY_ENV_CODE_AND_DEF_NAME = $@"SELECT IFNULL(MAX(dv.version), 0) + 1 AS next_version FROM def_version dv JOIN definition d ON d.id = dv.parent JOIN environment e ON e.id = d.env WHERE e.code = {CODE} AND d.name = lower(trim({NAME})) LIMIT 1;";
    }
}
