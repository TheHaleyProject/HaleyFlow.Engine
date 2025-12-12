using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_INSTANCE {
        public const string INSERT = $@"INSERT IGNORE INTO instance (last_event, current_state, external_ref, flags, def_version) VALUES ({EVENT}, {CURRENT_STATE}, lower({EXTERNAL_REF}), {FLAGS}, {DEF_VERSION}); SELECT * FROM instance WHERE def_version = {DEF_VERSION} AND external_ref = lower({EXTERNAL_REF}) LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM instance WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT * FROM instance WHERE guid = {GUID} LIMIT 1;";
        public const string GET_BY_REF = $@"SELECT * FROM instance WHERE def_version = {DEF_VERSION} AND external_ref = lower({EXTERNAL_REF}) LIMIT 1;";
        public const string GET_BY_REF_ANY_VERSION = $@"SELECT * FROM instance WHERE external_ref = lower({EXTERNAL_REF}) ORDER BY id DESC;";
        public const string GET_BY_STATE_IN_VERSION = $@"SELECT * FROM instance WHERE def_version = {DEF_VERSION} AND current_state = {CURRENT_STATE};";
        public const string GET_BY_FLAGS_IN_VERSION = $@"SELECT * FROM instance WHERE def_version = {DEF_VERSION} AND ((flags & {FLAGS}) = {FLAGS});";
        public const string UPDATE_STATE = $@"UPDATE instance SET current_state = {CURRENT_STATE}, last_event = {EVENT}, flags = {FLAGS}, modified = utc_timestamp() WHERE id = {ID};";
        public const string UPDATE_STATE_BY_GUID = $@"UPDATE instance SET current_state = {CURRENT_STATE}, last_event = {EVENT}, flags = {FLAGS}, modified = utc_timestamp() WHERE guid = {GUID};";
        public const string MARK_COMPLETED = $@"UPDATE instance SET flags = (flags | 4), modified = utc_timestamp() WHERE id = {ID};";
        public const string MARK_COMPLETED_BY_GUID = $@"UPDATE instance SET flags = (flags | 4), modified = utc_timestamp() WHERE guid = {GUID};";
        public const string DELETE = $@"DELETE FROM instance WHERE id = {ID};";
        public const string DELETE_BY_GUID = $@"DELETE FROM instance WHERE guid = {GUID};";
    }
}
