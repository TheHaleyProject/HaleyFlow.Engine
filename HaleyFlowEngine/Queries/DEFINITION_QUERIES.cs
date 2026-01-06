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
    internal class QRY_DEFINITION {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM definition WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM definition WHERE guid = lower(trim({GUID})) LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_NAME = $@"SELECT 1 FROM definition WHERE env = {PARENT_ID} AND name = lower(trim({NAME})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM definition WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT * FROM definition WHERE guid = lower(trim({GUID})) LIMIT 1;";
        public const string GET_BY_PARENT_AND_NAME = $@"SELECT * FROM definition WHERE env = {PARENT_ID} AND name = lower(trim({NAME})) LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT * FROM definition WHERE env = {PARENT_ID} ORDER BY id;";

        // NOTE: definition.name is GENERATED from display_name (do not set name)
        public const string INSERT = $@"INSERT INTO definition (env, display_name, description) VALUES ({PARENT_ID}, {DISPLAY_NAME}, {DESCRIPTION}); SELECT LAST_INSERT_ID() AS id;";
        public const string UPDATE = $@"UPDATE definition SET env = {PARENT_ID}, display_name = {DISPLAY_NAME}, description = {DESCRIPTION} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM definition WHERE id = {ID};";
    }
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
    internal class QRY_POLICY {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM policy WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_HASH = $@"SELECT 1 FROM policy WHERE hash = lower(trim({HASH})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM policy WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_HASH = $@"SELECT * FROM policy WHERE hash = lower(trim({HASH})) LIMIT 1;";

        public const string INSERT = $@"INSERT INTO policy (hash, content) VALUES (lower(trim({HASH})), {CONTENT}); SELECT LAST_INSERT_ID() AS id;";

        // upsert: if exists -> update content, and always return the id
        public const string UPSERT_BY_HASH_RETURN_ID = $@"INSERT INTO policy (hash, content) VALUES (lower(trim({HASH})), {CONTENT}) ON DUPLICATE KEY UPDATE content = VALUES(content), id = LAST_INSERT_ID(id); SELECT LAST_INSERT_ID() AS id;";

        public const string UPDATE_CONTENT = $@"UPDATE policy SET content = {CONTENT} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM policy WHERE id = {ID};";

        // def_policies: PRIMARY KEY(definition, policy) + column is "modified"
        public const string EXISTS_ATTACHMENT = $@"SELECT 1 FROM def_policies WHERE definition = {PARENT_ID} AND policy = {ID} LIMIT 1;";

        // If already attached, bump modified => tells you “this is latest attach”
        public const string ATTACH_TO_DEFINITION = $@"INSERT INTO def_policies (definition, policy) VALUES ({PARENT_ID}, {ID}) ON DUPLICATE KEY UPDATE modified = CURRENT_TIMESTAMP;";

        public const string DETACH_FROM_DEFINITION = $@"DELETE FROM def_policies WHERE definition = {PARENT_ID} AND policy = {ID};";

        public const string LIST_BY_DEFINITION = $@"SELECT p.*, dp.modified AS attached_modified FROM def_policies dp JOIN policy p ON p.id = dp.policy WHERE dp.definition = {PARENT_ID} ORDER BY dp.modified DESC, p.id ASC;";

        public const string GET_POLICY_FOR_DEFINITION = $@"SELECT p.*, dp.modified AS attached_modified FROM def_policies dp 
JOIN policy p ON p.id = dp.policy JOIN def_version dv ON dv.parent = dp.definition WHERE dp.definition = {PARENT_ID} ORDER BY dp.modified DESC, p.id DESC LIMIT 1;";

        public const string GET_POLICY_FOR_DEFVERSION = $@"SELECT p.*, dp.modified AS attached_modified FROM def_policies dp 
JOIN policy p ON p.id = dp.policy JOIN def_version dv ON dv.parent = dp.definition WHERE dv.id = {ID} ORDER BY dp.modified DESC, p.id DESC LIMIT 1;";

        // Latest policy lookups

        // A) definition id -> latest attached policy
        public const string GET_LATEST_BY_DEFINITION = $@"SELECT p.*, dp.modified AS attached_modified FROM def_policies dp JOIN policy p ON p.id = dp.policy WHERE dp.definition = {PARENT_ID} ORDER BY dp.modified DESC, p.id DESC LIMIT 1;";

        // B) definition guid -> latest attached policy
        public const string GET_LATEST_BY_DEFINITION_GUID = $@"SELECT p.*, dp.modified AS attached_modified FROM definition d JOIN def_policies dp ON dp.definition = d.id JOIN policy p ON p.id = dp.policy WHERE d.guid = lower(trim({GUID})) ORDER BY dp.modified DESC, p.id DESC LIMIT 1;";

        // C) env code + def name -> latest attached policy
        public const string GET_LATEST_BY_ENV_CODE_AND_DEF_NAME = $@"SELECT p.*, dp.modified AS attached_modified FROM environment e JOIN definition d ON d.env = e.id JOIN def_policies dp ON dp.definition = d.id JOIN policy p ON p.id = dp.policy WHERE e.code = {CODE} AND d.name = lower(trim({DEF_NAME})) ORDER BY dp.modified DESC, p.id DESC LIMIT 1;";

        // (optional) env name + def name -> latest attached policy
        // NOTE: using DISPLAY_NAME placeholder to pass env-name token (because env.name is generated)
        public const string GET_LATEST_BY_ENV_NAME_AND_DEF_NAME = $@"SELECT p.*, dp.modified AS attached_modified FROM environment e JOIN definition d ON d.env = e.id JOIN def_policies dp ON dp.definition = d.id JOIN policy p ON p.id = dp.policy WHERE e.name = lower(trim({ENV_NAME})) AND d.name = lower(trim({DEF_NAME})) ORDER BY dp.modified DESC, p.id DESC LIMIT 1;";

        // Optional: Attach by definition identifiers (no need to pre-fetch def.id)
        // Attach policy to definition by def GUID (idempotent: updates modified if exists)
        public const string ATTACH_TO_DEFINITION_BY_DEF_GUID = $@"INSERT INTO def_policies (definition, policy) SELECT d.id, {ID} FROM definition d WHERE d.guid = lower(trim({GUID})) LIMIT 1 ON DUPLICATE KEY UPDATE modified = CURRENT_TIMESTAMP;";

        // Attach policy to definition by env code + def name (idempotent)
        public const string ATTACH_TO_DEFINITION_BY_ENV_CODE_AND_DEF_NAME = $@"INSERT INTO def_policies (definition, policy) SELECT d.id, {ID} FROM definition d JOIN environment e ON e.id = d.env WHERE e.code = {CODE} AND d.name = lower(trim({DEF_NAME})) LIMIT 1 ON DUPLICATE KEY UPDATE modified = CURRENT_TIMESTAMP;";
    }
}
