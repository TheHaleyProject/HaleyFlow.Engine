using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    // lc_timeout table design:
    //
    // Current schema: lc_timeout (lc_id PK)
    //   A single row here means "the lifecycle entry at lc_id has been processed by the timeout system."
    //   INSERT_IGNORE is idempotent — safe to call before TriggerAsync (Case A) to guard against
    //   double-firing if the process crashes between the insert and the trigger call.
    //
    // Full schema (field names mirror ack_consumer for consistency):
    //   lc_timeout (
    //     lc_id          BIGINT PK   — lifecycle entry that entered the timed state (FK → lifecycle.id)
    //     created        DATETIME    — UTC timestamp when the row was first created
    //     trigger_count  INT         — how many notices/triggers have been sent (Case A: always 1)
    //     last_trigger   DATETIME    — when the last notice/trigger was fired
    //     next_due       DATETIME    — when the next notice should fire (Case B scheduling only)
    //   )
    //
    // The lc_timeout row connects the timeline to the timeout event:
    //   - For Case A: row inserted before TriggerAsync; the resulting new lifecycle entry is the
    //     auto-transition. Timeline shows: "entered state at T, timeout fired at T+duration."
    //   - For Case B: row tracks the repeated notice schedule. Timeline shows entries stayed in state.
    //
    // Resolution in both cases is natural: once the instance transitions out of the timed state,
    //   (l.to_state = i.current_state) no longer holds, so LIST_DUE_PAGED stops returning it.
    //   No "mark resolved" step is needed — the transition itself is the resolution.
    internal class QRY_LC_TIMEOUT {

        public const string EXISTS_BY_LC_ID = $@"SELECT 1 FROM lc_timeout WHERE lc_id = {LC_ID} LIMIT 1;";

        // Case A (timeout_event set): INSERT before calling TriggerAsync — idempotency guard.
        // If process crashes after insert but before trigger, next tick finds the row and skips.
        // max_retry is copied from the timeout rule at insert time so the monitor never needs to re-join timeouts.
        public const string INSERT_IGNORE = $@"INSERT INTO lc_timeout (lc_id, max_retry) VALUES ({LC_ID}, {MAX_RETRY}) ON DUPLICATE KEY UPDATE lc_id = lc_id;";

        public const string DELETE_BY_LC_ID = $@"DELETE FROM lc_timeout WHERE lc_id = {LC_ID};";

        // Finds instances whose policy-defined timeout has elapsed and have not yet been processed.
        // Conditions:
        //   - Instance is active (not Suspended, Completed, Failed, Archived)
        //   - The latest lifecycle entry is the one that entered the current state (l.to_state = i.current_state)
        //   - A timeout rule exists for this state on the instance's locked policy
        //   - The timeout duration has elapsed since the lifecycle entry was created
        //   - No lc_timeout marker exists yet for this lifecycle entry (lt.lc_id IS NULL)
        //
        // NOTE: currently filters event_code IS NOT NULL (Case A only).
        // When Case B support is added, remove that filter and handle both cases in the orchestrator
        // based on whether timeout_event_code is null in the result row.
        //
        // NOTE: LIST_DUE_PAGED is wired in MariaLifeCycleTimeoutDAL but that DAL is not yet
        // exposed on IWorkFlowDAL (LcTimeout property missing) and not called by MonitorOrchestrator.
        public const string LIST_DUE_PAGED = $@"SELECT i.id AS instance_id, i.guid AS instance_guid, i.entity_id AS entity_id, i.def_version AS def_version_id, i.current_state AS state_id, l.id AS entry_lc_id, l.created AS entered_at, tm.duration AS timeout_duration, tm.mode AS timeout_mode, tm.event_code AS timeout_event_code, DATE_ADD(l.created, INTERVAL tm.duration MINUTE) AS due_at FROM instance i JOIN lifecycle l ON l.id = (SELECT MAX(l2.id) FROM lifecycle l2 WHERE l2.instance_id = i.id) JOIN state s ON s.id = i.current_state AND s.def_version = i.def_version JOIN timeouts tm ON tm.policy_id = i.policy_id AND tm.state_name = s.name LEFT JOIN lc_timeout lt ON lt.lc_id = l.id WHERE (i.flags & {FLAGS}) = 0 AND l.to_state = i.current_state AND tm.duration > 0 AND tm.event_code IS NOT NULL AND lt.lc_id IS NULL AND DATE_ADD(l.created, INTERVAL tm.duration MINUTE) <= UTC_TIMESTAMP() ORDER BY due_at ASC, i.id ASC LIMIT {TAKE} OFFSET {SKIP};";

    }
}
