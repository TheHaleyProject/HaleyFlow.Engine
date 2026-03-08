using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_HOOK_LC {
        // Create a hook_lc row for a specific hook + lifecycle entry.
        // UNIQUE key (hook_id, lc_id) makes this idempotent on duplicate.
        public const string INSERT =
            $@"INSERT INTO hook_lc (hook_id, lc_id) VALUES ({HOOK_ID}, {LC_ID})
               ON DUPLICATE KEY UPDATE hook_id = hook_id;
               SELECT id FROM hook_lc WHERE hook_id = {HOOK_ID} AND lc_id = {LC_ID} LIMIT 1;";

        // Mark a specific hook_lc row as dispatched (ACK rows created, event fired).
        public const string MARK_DISPATCHED = $@"UPDATE hook_lc SET dispatched = 1 WHERE id = {HOOK_LC_ID};";

        // Count how many times this hook has been fully dispatched across all lifecycle entries.
        // Used to populate RunCount on ILifeCycleHookEvent so consumers can detect reruns.
        public const string COUNT_DISPATCHED_BY_HOOK_ID =
            $@"SELECT COUNT(*) AS cnt FROM hook_lc WHERE hook_id = {HOOK_ID} AND dispatched = 1;";
    }
}
