using System.Diagnostics.Tracing;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
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
