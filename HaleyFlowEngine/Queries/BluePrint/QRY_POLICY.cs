using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_POLICY {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM policy WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_HASH = $@"SELECT 1 FROM policy WHERE hash = {HASH} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, hash, content, created FROM policy WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_HASH = $@"SELECT id, hash, content, created FROM policy WHERE hash = {HASH} LIMIT 1;";

        public const string INSERT = $@"INSERT INTO policy (hash, content) VALUES ({HASH}, {CONTENT});";
        public const string UPSERT_BY_HASH_RETURN_ID = $@"INSERT INTO policy (hash, content) VALUES ({HASH}, {CONTENT}) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id); SELECT LAST_INSERT_ID() AS policy_id;";
        public const string UPDATE_CONTENT = $@"UPDATE policy SET content = {CONTENT} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM policy WHERE id = {ID};";

        // def_policies: PRIMARY KEY(definition, policy)
        public const string EXISTS_ATTACHMENT = $@"SELECT 1 FROM def_policies WHERE definition = {PARENT_ID} AND policy = {ID} LIMIT 1;";
        public const string ATTACH_TO_DEFINITION = $@"INSERT INTO def_policies (definition, policy) VALUES ({PARENT_ID}, {ID});";
        public const string DETACH_FROM_DEFINITION = $@"DELETE FROM def_policies WHERE definition = {PARENT_ID} AND policy = {ID};";
        public const string LIST_BY_DEFINITION = $@"SELECT p.id, p.hash, p.content, p.created, dp.created AS attached_created FROM def_policies dp JOIN policy p ON p.id = dp.policy WHERE dp.definition = {PARENT_ID} ORDER BY dp.created DESC, p.id ASC;";
    }
}
