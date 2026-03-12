using System.Diagnostics.Tracing;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal static class QRY_TIMEOUTS {

        public const string DELETE_BY_POLICY_ID = $@"DELETE FROM timeouts WHERE policy_id = {POLICY_ID};";

        public const string INSERT = $@"INSERT INTO timeouts (policy_id, state_name, duration, mode, event_code, max_retry) VALUES ({POLICY_ID}, {STATE_NAME}, {DURATION}, {MODE}, {EVENT_CODE}, {MAX_RETRY});";

        public const string LIST_BY_POLICY_ID = $@"SELECT policy_id, state_name, duration, mode, event_code, max_retry FROM timeouts WHERE policy_id = {POLICY_ID} ORDER BY state_name ASC;";
    }
}
