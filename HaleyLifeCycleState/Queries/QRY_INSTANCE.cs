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

        public const string UPDATE_STATE = $@"UPDATE instance SET current_state = {CURRENT_STATE}, last_event = {EVENT}, flags = {FLAGS} WHERE id = {ID};";
        public const string UPDATE_STATE_BY_GUID = $@"UPDATE instance SET current_state = {CURRENT_STATE}, last_event = {EVENT}, flags = {FLAGS} WHERE guid = {GUID};";
        public const string MARK_COMPLETED = $@"UPDATE instance SET flags = (flags | 4) WHERE id = {ID};";
        public const string MARK_COMPLETED_BY_GUID = $@"UPDATE instance SET flags = (flags | 4) WHERE guid = {GUID};";

        public const string DELETE = $@"DELETE FROM instance WHERE id = {ID};";
        public const string DELETE_BY_GUID = $@"DELETE FROM instance WHERE guid = {GUID};";

        //We are innerjoining state's timeout events with events, because timeout_event in state can be NULL. In that case, we don't want to select those instances which doesn't have any timeout event assigned.
        public const string GET_INSTANCES_WITH_EXPIRED_TIMEOUTS = $@"SELECT i.def_version, i.external_ref, IF(e.code IS NULL OR e.code = 0, e.id, e.code) AS event_code FROM instance i INNER JOIN state s ON s.id = i.current_state INNER JOIN events e ON e.id = s.timeout_event WHERE s.timeout_minutes IS NOT NULL AND s.timeout_minutes > 0 AND (i.flags & 4) = 0 AND (i.flags & 8) = 0 AND TIMESTAMPADD(MINUTE, s.timeout_minutes, i.modified) <= current_timestamp() LIMIT {MAX_BATCH};";

        public const string EXISTS_BY_ID = $@"SELECT 1 FROM instance WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM instance WHERE guid = {GUID} LIMIT 1;";
        public const string EXISTS_BY_VERSION_AND_REF =$@"SELECT 1 FROM instance WHERE def_version = {DEF_VERSION} AND external_ref = lower(trim({EXTERNAL_REF})) LIMIT 1;";

    }
}
