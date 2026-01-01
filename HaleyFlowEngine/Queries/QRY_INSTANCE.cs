using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_INSTANCE {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM instance WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM instance WHERE guid = {GUID} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_EXTERNAL_REF = $@"SELECT 1 FROM instance WHERE def_version = {PARENT_ID} AND external_ref = {EXTERNAL_REF} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, guid, external_ref, def_version, current_state, last_event, policy_id, flags, created, modified FROM instance WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT id, guid, external_ref, def_version, current_state, last_event, policy_id, flags, created, modified FROM instance WHERE guid = {GUID} LIMIT 1;";
        public const string GET_BY_PARENT_AND_EXTERNAL_REF = $@"SELECT id, guid, external_ref, def_version, current_state, last_event, policy_id, flags, created, modified FROM instance WHERE def_version = {PARENT_ID} AND external_ref = {EXTERNAL_REF} LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT id, guid, external_ref, def_version, current_state, last_event, policy_id, flags, created, modified FROM instance WHERE def_version = {PARENT_ID} ORDER BY id;";
        public const string LIST_BY_EXTERNAL_REF = $@"SELECT id, guid, external_ref, def_version, current_state, last_event, policy_id, flags, created, modified FROM instance WHERE external_ref = {EXTERNAL_REF} ORDER BY id;";
        public const string LIST_BY_CURRENT_STATE = $@"SELECT id, guid, external_ref, def_version, current_state, last_event, policy_id, flags, created, modified FROM instance WHERE current_state = {STATE_ID} ORDER BY id;";

        public const string LIST_WHERE_FLAGS_ANY = $@"SELECT id, guid, external_ref, def_version, current_state, last_event, policy_id, flags, created, modified FROM instance WHERE (flags & {FLAGS}) <> 0 ORDER BY id;";
        public const string LIST_WHERE_FLAGS_NONE = $@"SELECT id, guid, external_ref, def_version, current_state, last_event, policy_id, flags, created, modified FROM instance WHERE (flags & {FLAGS}) = 0 ORDER BY id;";

        public const string INSERT = $@"INSERT INTO instance (def_version, external_ref, current_state, last_event, policy_id, flags) VALUES ({PARENT_ID}, {EXTERNAL_REF}, {STATE_ID}, {EVENT_ID}, {POLICY_ID}, {FLAGS});";
        public const string UPSERT_BY_PARENT_AND_EXTERNAL_REF_RETURN_ID = $@"INSERT INTO instance (def_version, external_ref, current_state, last_event, policy_id, flags) VALUES ({PARENT_ID}, {EXTERNAL_REF}, {STATE_ID}, {EVENT_ID}, {POLICY_ID}, {FLAGS}) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id);";

        public const string UPDATE_CURRENT_STATE = $@"UPDATE instance SET current_state = {STATE_ID}, last_event = {EVENT_ID} WHERE id = {ID};";
        public const string UPDATE_CURRENT_STATE_CAS = $@"UPDATE instance SET current_state = {TO_ID}, last_event = {EVENT_ID} WHERE id = {ID} AND current_state = {FROM_ID};";

        public const string SET_FLAGS = $@"UPDATE instance SET flags = {FLAGS} WHERE id = {ID};";
        public const string ADD_FLAGS = $@"UPDATE instance SET flags = (flags | {FLAGS}) WHERE id = {ID};";
        public const string REMOVE_FLAGS = $@"UPDATE instance SET flags = (flags & ~{FLAGS}) WHERE id = {ID};";

        public const string SET_POLICY = $@"UPDATE instance SET policy_id = {POLICY_ID} WHERE id = {ID};";
        public const string SET_EXTERNAL_REF = $@"UPDATE instance SET external_ref = {EXTERNAL_REF} WHERE id = {ID};";

        public const string DELETE = $@"DELETE FROM instance WHERE id = {ID};";
    }
}
