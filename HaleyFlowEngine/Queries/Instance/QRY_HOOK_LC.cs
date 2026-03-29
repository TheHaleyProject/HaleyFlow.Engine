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

        // Count queued hook_lc rows that still need real dispatch.
        // Skipped rows remain status=2, dispatched=0 and must not be counted here.
        public const string COUNT_UNDISPATCHED_BY_LC_ID =
            $@"SELECT COUNT(*) AS cnt
               FROM hook_lc
               WHERE lc_id = {LC_ID}
                 AND dispatched = 0
                 AND status <> 2;";

        public const string SKIP_UNDISPATCHED_BY_LC_ID =
            $@"UPDATE hook_lc
               SET status = 2
               WHERE lc_id = {LC_ID}
                 AND dispatched = 0
                 AND status <> 2;";

        // Count how many times this hook has been fully dispatched across all lifecycle entries.
        // Used to populate RunCount on ILifeCycleHookEvent so consumers can detect reruns.
        public const string COUNT_DISPATCHED_BY_HOOK_ID =
            $@"SELECT COUNT(*) AS cnt FROM hook_lc WHERE hook_id = {HOOK_ID} AND dispatched = 1;";

        // Flat list for TimelineBuilder (Admin detail) — all hook_lc rows for an instance with
        // aggregated ACK stats. ACK status: 3=Processed, 4=Failed, 5=Cancelled.
        public const string LIST_FOR_TIMELINE =
            $@"SELECT hl.id AS hook_lc_id, hl.lc_id, hl.dispatched, hl.status AS hook_status,
                      hr.name AS route, COALESCE(hr.label, '') AS label,
                      h.type AS hook_type, h.on_entry, h.order_seq,
                      COUNT(ac.ack_id) AS total_acks,
                      SUM(IF(ac.status = 3, 1, 0)) AS processed_acks,
                      SUM(IF(ac.status = 4, 1, 0)) AS failed_acks,
                      MAX(COALESCE(ac.trigger_count, 0)) AS max_retries,
                      SUM(COALESCE(ac.trigger_count, 0)) AS total_triggers,
                      MAX(ac.last_trigger) AS last_trigger
               FROM hook_lc hl
               JOIN hook h ON h.id = hl.hook_id
               JOIN hook_route hr ON hr.id = h.route_id
               LEFT JOIN hook_ack ha ON ha.hook_id = hl.id
               LEFT JOIN ack_consumer ac ON ac.ack_id = ha.ack_id
               WHERE h.instance_id = {INSTANCE_ID}
               GROUP BY hl.id, hl.lc_id, hl.dispatched, hl.status, hr.name, hr.label, h.type, h.on_entry, h.order_seq
               ORDER BY hl.lc_id ASC,
                        CASE WHEN h.order_seq > 0 THEN 0 ELSE 1 END ASC,
                        h.order_seq ASC,
                        h.on_entry DESC,
                        hr.name ASC,
                        hl.id ASC;";
    }
}
