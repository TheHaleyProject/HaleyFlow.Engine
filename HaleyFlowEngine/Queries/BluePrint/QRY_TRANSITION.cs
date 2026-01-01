using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_TRANSITION {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM transition WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_KEY = $@"SELECT 1 FROM transition WHERE def_version = {PARENT_ID} AND from_state = {FROM_ID} AND to_state = {TO_ID} AND event = {EVENT_ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT id, def_version, from_state, to_state, event, created FROM transition WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_KEY = $@"SELECT id, def_version, from_state, to_state, event, created FROM transition WHERE def_version = {PARENT_ID} AND from_state = {FROM_ID} AND to_state = {TO_ID} AND event = {EVENT_ID} LIMIT 1;";
        public const string GET_BY_FROM_AND_EVENT = $@"SELECT id, def_version, from_state, to_state, event, created FROM transition WHERE def_version = {PARENT_ID} AND from_state = {FROM_ID} AND event = {EVENT_ID} ORDER BY id LIMIT 1;";

        public const string LIST_BY_PARENT = $@"SELECT id, def_version, from_state, to_state, event, created FROM transition WHERE def_version = {PARENT_ID} ORDER BY id;";

        public const string INSERT = $@"INSERT INTO transition (def_version, from_state, to_state, event) VALUES ({PARENT_ID}, {FROM_ID}, {TO_ID}, {EVENT_ID});";
        public const string UPDATE = $@"UPDATE transition SET from_state = {FROM_ID}, to_state = {TO_ID}, event = {EVENT_ID} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM transition WHERE id = {ID};";
        public const string DELETE_BY_PARENT = $@"DELETE FROM transition WHERE def_version = {PARENT_ID};";
    }
}
