# Haley Flow Engine — Overview

A MariaDB-backed macro workflow/state-machine engine. It tracks **what state a business entity is in**, drives it through **policy-defined transitions**, emits **hook events** for downstream work, and retries delivery until acknowledged.

---

## Core Concepts (Quick Reference)

| Concept | One-liner |
|---------|-----------|
| **Definition** | The state machine schema (states + events + transitions). Versioned. |
| **Policy** | Rules and hook emit config attached to a definition. Drives hook emission. |
| **Blueprint** | In-memory compiled form of a def_version (states, events, transition map). |
| **Instance** | A single running entity (e.g. one application, one request). Identified by `external_ref`. |
| **Lifecycle** | One immutable row per state transition. The audit log of the instance. |
| **Event** | Named + coded signal that causes a transition (`approved`, `rejected`, `timeout`). |
| **Hook** | A side-effect emit (e.g. `send_email`, `notify_reviewer`). Stored and ACK-tracked. |
| **ACK** | Acknowledgement receipt — consumer must confirm delivery/processing of each event. |
| **Consumer** | An application instance that registers, heartbeats, and handles events. |
| **Monitor** | Background loop that retries unacknowledged events and scans for stale instances. |

---

## 1. Definition & Versioning

A **definition** is the named state machine schema for a specific environment. It is versioned: each import of a different schema creates a new `def_version`. Versions are identified by a SHA-256 hash of the definition JSON, so the same schema imported twice produces one version (idempotent).

**Definition JSON shape:**
```json
{
  "defName": "loan-approval",
  "displayName": "Loan Approval",
  "states": [
    { "name": "Draft",       "flags": 1 },
    { "name": "UnderReview", "flags": 0 },
    { "name": "Approved",    "flags": 2 },
    { "name": "Rejected",    "flags": 2 }
  ],
  "events": [
    { "name": "submit",   "code": 10 },
    { "name": "approve",  "code": 20 },
    { "name": "reject",   "code": 30 }
  ],
  "transitions": [
    { "from": "Draft",       "event": "submit",  "to": "UnderReview" },
    { "from": "UnderReview", "event": "approve", "to": "Approved" },
    { "from": "UnderReview", "event": "reject",  "to": "Rejected" }
  ]
}
```

**State flags:**
- `1` = IsInitial — exactly one per definition, the starting state for new instances
- `2` = IsFinal

---

## 2. Policy & Rules

A **policy** is a separate JSON document linked to a definition. It defines what happens *when* a state is entered. Policies are also versioned by hash — same content = same policy row.

**Policy JSON shape:**
```json
{
  "defName": "loan-approval",
  "rules": [
    {
      "state": "UnderReview",
      "via": 10,
      "blocking": true,
      "params": ["review-params"],
      "complete": { "success": "approve", "failure": "reject" },
      "emit": [
        { "event": "notify_reviewer", "blocking": true },
        { "event": "log_submission",  "blocking": false }
      ]
    }
  ],
  "params": [
    {
      "code": "review-params",
      "data": { "sla_hours": 48, "escalation_email": "ops@example.com" }
    }
  ]
}
```

**Rule matching:**
- Matched by `state` name + optional `via` event code.
- If both a generic rule (no `via`) and a specific one (with `via`) match, the specific rule wins.

**Inheritance chain for emit fields:**
- `emit.blocking` → `rule.blocking` → default `true`
- `emit.params` → `rule.params` (emit wins; no merge)
- `emit.complete` → `rule.complete` (emit wins per field)

---

## 3. Blueprint (In-Memory Cache)

When a trigger fires for `(envCode, defName)`, the engine loads and caches a **LifeCycleBlueprint**:

- `StatesById` — `id → StateDef`
- `EventsById` / `EventsByCode` / `EventsByName` — lookup maps
- `Transitions` — `(fromStateId, eventId) → TransitionDef`
- `InitialStateId` — used when creating a new instance

Blueprints are cached per `def_version_id`. Call `InvalidateAsync()` after re-importing to force reload.

---

## 4. Instance

Each business entity has one **instance** row, identified by `(def_version, external_ref)`.

**Instance flags:**
- `Active = 1`
- `Suspended = 2` — engine paused it (e.g. ACK max retries on a blocking hook)
- `Completed = 4`
- `Failed = 8`
- `Archived = 16`

**Policy locking:** The policy active at instance creation time is permanently locked to that instance (`policy_id` column). Later policy changes do not affect existing instances.

---

## 5. TriggerAsync — The Main Flow

```
TriggerAsync(req)
  ├─ Load blueprint (cached)
  ├─ Resolve latest policy (for new instance creation)
  ├─ EnsureInstance — create or load by (def_version, external_ref)
  ├─ ApplyTransition — CAS state update, idempotent by RequestId
  │    └─ No valid transition → returns Applied=false, commits, returns early
  ├─ Resolve instance's locked policy
  ├─ Create lifecycle ACK row (if AckRequired)
  ├─ EmitHooksAsync → upsert hook rows + create hook ACK rows
  ├─ Commit transaction
  └─ Dispatch events fire-and-forget (after commit)
```

**Key guarantee:** DB state is fully committed before any event fires. If dispatch fails, the ACK rows remain pending and the monitor retries.

---

## 6. Hook Emission

`PolicyEnforcer.EmitHooksAsync` scans matched rules and creates a **hook row** per emit entry.

Each hook stores: `instance_id`, `state_id`, `via_event`, `on_entry`, `route` (hook code), `blocking`.

Hook upsert is idempotent: `INSERT ... ON DUPLICATE KEY UPDATE` ensures retrying the same transition does not duplicate hooks — it updates the `blocking` flag in place.

**Blocking vs Non-blocking on max retry:**

| `blocking` | Outcome when ACK max retries exceeded |
|------------|--------------------------------------|
| `true` (default) | Instance suspended with message |
| `false` | ACK marked Failed, notice fired, instance continues |

---

## 7. ACK System

Every emitted event (lifecycle transition + each hook per consumer) creates an **ACK** + `ack_consumer` row.

**Status flow:**
```
Pending → Delivered → Processed   (happy path)
        → Retry     → Pending     (consumer requests reschedule)
        → Failed                  (terminal — no more retries)
```

Consumers call `AckAsync(consumerGuid, ackGuid, outcome)`. The engine updates `status` and `next_due` on the matching `ack_consumer` row.

**Monitor re-dispatch fires when:**
`status IN (Pending, Delivered)` AND `next_due <= UTC_NOW` AND consumer heartbeat within TTL.

---

## 8. Consumer Model

A **consumer** is any application instance that processes events. Identified by `(envCode, consumerGuid)`.

**Lifecycle:**
1. `RegisterConsumerAsync(envCode, guid)` — upserts consumer row, returns `consumerId`.
2. `BeatConsumerAsync(envCode, guid)` — updates `last_beat`. Call every ~10 seconds.
3. Monitor skips consumers where `TIMESTAMPDIFF(SECOND, last_beat, UTC_NOW) > ttlSeconds`.

**Consumer types** (resolved via the `ResolveConsumers` delegate you provide):
- `Transition` — receives lifecycle transition events, resolved per `def_version_id`
- `Hook` — receives hook events, resolved per `def_version_id`
- `Monitor` — used by the internal monitor scan loop

---

## 9. Monitor Loop

Runs at a configurable interval. Each tick:

1. **Stale scan** — finds instances in non-final states past a configured duration with no active timeout; fires `STATE_STALE` notice per consumer. Read-only, no DB writes.
2. **ACK retry** — for each active consumer, fetches due `ack_consumer` rows and re-fires events, incrementing `trigger_count`.
3. **Consumer-down handling** — if consumer heartbeat is stale, `next_due` is pushed forward by `recheckSeconds` to stop hammering a dead queue.
4. **Max retry** — if `trigger_count >= MaxRetryCount`, marks ACK `Failed`. Suspends instance if hook was `blocking`.

---

## 10. Timeout System

Timeouts are defined in the **policy** and stored in the `timeouts` table (`policy_id + state_name` key):

- `duration` — minutes before the timeout fires
- `mode` — `0` Once, `1` Repeat
- `event_code` — the event auto-triggered when the timeout fires

The monitor scans `lc_timeout` (one cursor row per lifecycle entry) and auto-fires the configured event when the duration is exceeded. If no timeout is defined for a state, the stale-scan falls back to the engine's `DefaultStateStaleDuration` option and fires a notice instead.

---

## 11. Runtime Tracking (Optional)

The engine optionally tracks micro-level activity progress via the **runtime** table. The engine has no state-machine awareness of these — it is application-managed bookkeeping.

Use `UpsertRuntimeAsync(RuntimeLogByNameRequest)` to log:
- Which `activity` is being performed on `(instance, state, actor)`
- Activity `status` (application-defined: Pending, Completed, Approved, Rejected, etc.)
- Optional freeze (`FreezeRuntimeAsync`) to lock a row from further status changes

Runtime rows are keyed by `(instance_id, state_id, activity, actor_id)` — each actor's work on each state is tracked independently.

---

## 12. Timeline & `occurred`

`GetTimelineJsonAsync(instanceId)` returns a JSON array of lifecycle entries ordered oldest-first, including state names, event names, actor, payload, and timestamps.

**`occurred` vs `created`:**
- `lifecycle.created` — when the engine captured the transition (always set by the engine).
- `lifecycle.occurred` — the *real-world time* the event actually happened (optional; set by caller for replay/late-join scenarios).

When displaying a timeline, prefer `occurred` if present, fall back to `created`. Never override `created` — it is engine-internal metadata.

---

## 13. Key Notice Codes

Subscribe via `engine.NoticeRaised += async (n) => { ... }` to route these to your logging/alerting.

| Code | Severity | Meaning |
|------|----------|---------|
| `TRIGGER_ERROR` | Error | Unhandled exception in `TriggerAsync` |
| `MONITOR_ERROR` | Error | Unhandled exception in monitor loop tick |
| `EVENT_HANDLER_ERROR` | Error | Exception thrown by a consumer's event handler |
| `ACK_RETRY` | Warn | Monitor re-dispatching a due event |
| `ACK_SUSPEND` | Warn | Instance suspended after max retries (blocking hook or lifecycle) |
| `ACK_FAIL` | Warn | Non-blocking hook failed after max retries (no suspend) |
| `STATE_STALE` | OverDue | Instance overdue in current state past configured duration |

---

## 14. Data Flow

```
Application
    │
    ▼ TriggerAsync(req)
WorkFlowEngine
    ├─ BlueprintManager ──► def_version / state / events / transition  (cached)
    ├─ StateMachine      ──► instance (upsert) + lifecycle (CAS insert)
    ├─ PolicyEnforcer    ──► policy (rule match) + hook (upsert per emit)
    ├─ AckManager        ──► ack + ack_consumer (per consumer)
    └─ EventRaised ──────────────────────────────► Consumer handlers
                                                        │
                                                        ▼ AckAsync(guid, outcome)
                                                   AckManager ──► ack_consumer.status

Monitor loop (background)
    ├─ Stale scan   ──► instance query ──► NoticeRaised (STATE_STALE)
    └─ ACK retry    ──► ack_consumer (due)
                            ├─► EventRaised (re-dispatch)
                            └─► SuspendInstance (if max retries + blocking hook)
```

---

## 15. Quick-Start Checklist

```csharp
// 1. Build engine
var engine = new WorkFlowEngine(dal, options);

// 2. Subscribe to events and notices
engine.EventRaised  += HandleEventAsync;
engine.NoticeRaised += HandleNoticeAsync;

// 3. Import schema and policy (idempotent — safe to call on every startup)
await engine.BlueprintImporter.ImportDefinitionJsonAsync(defJson);
await engine.BlueprintImporter.ImportPolicyJsonAsync(policyJson);

// 4. Register consumer and start heartbeat
var consumerId = await engine.RegisterConsumerAsync(envCode, myGuid);
_ = Task.Run(() => BeatLoop(engine, envCode, myGuid, cts.Token));  // beat every ~10s

// 5. Start monitor
await engine.StartMonitorAsync(cts.Token);

// 6. Trigger a transition
var result = await engine.TriggerAsync(new LifeCycleTriggerRequest {
    EnvCode     = envCode,
    DefName     = "loan-approval",
    ExternalRef = loanId.ToString(),
    Event       = "submit",
    RequestId   = requestId,   // stable for retries
    Actor       = userId,
    AckRequired = true
});

// 7. In event handler — ACK after processing
await engine.AckAsync(envCode, myGuid, ackGuid, AckOutcome.Processed);
```
