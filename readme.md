# Haley.LifeCycle – Embedded Workflow Runtime

This document describes the **runtime workflow** when Haley.LifeCycle is used as an **embedded library** inside an application.

Actors:
- **Application**
- **LCEngine** (embedded)
- **LCMonitor** (embedded)

Key rule:
- The **engine owns macro workflow state** (states/transitions/timeouts).
- The **application owns business actions + micro logic** (approvals/quorum/work-items), while the engine stores only **dumb snapshots** for visibility.

---

## 1) Concepts and Contracts

### Engine Events (macro)
- Numeric codes (preferred) that drive **state transitions**.
- Raised by:
  - Application (major business outcomes)
  - LCEngine itself (AutoStart)
  - LCMonitor (timeouts / retries)

API contract:
- `Trigger(instanceId, engineEventCode, requestId, metadata)`

### App Events (micro actions)
- String codes like `APP.EVAL.SETUP`, `APP.WF.SELECTION.REQUIRED`
- These are mapped to application methods via attribute scanning:
  - `[LC_APP_EVENT("APP.XYZ")]`

Invocation contract (embedded/local today):
- `IAppActionInvoker.Invoke(appEventCode, context)`

### Macro acknowledgement (transition awareness)
- Tracks only: **“did application become aware of this transition?”**
- One macro transition → one macro acknowledgement (sent/acked).
- This is not micro-action completion.

### Micro snapshots (state work items)
- Engine persists dumb “mini-state” snapshots in a table like `instance_state_items`.
- Identity approach:
  - `item_key` = stable string (e.g., `approver:<userId>`)
  - optional `item_hash` = hash(payload_json) for dedupe
- Engine does not interpret these items; it just stores them.

---

## 2) Startup Sequence

On application startup:
1. Application calls `LCEngineInitializer.Initialize()`.
2. Initializer:
   - Reads DB config from `appsettings.json`
   - Creates/validates LC DB/schema if needed
   - Imports **workflow definitions** from `./lifecycle_config/` into `lcstate` (states/events/transitions/timeouts)
   - Loads **policy JSON** into **LCEngine memory cache** (keyed by `definition_name + definition_version`)
   - Scans assemblies for `[LC_APP_EVENT]` methods and registers them in `IAppActionInvoker`
3. Application now holds:
   - `LCEngine` (ready)
   - `LCMonitor` (running/timer-based)
   - No workflow-state knowledge required beyond choosing definition version per entity type

---

## 3) Runtime Workflow (End-to-End)

### A) Create instance (submission starts)
1. A submission is created in the application.
2. App calls:
   - `instanceId = CreateInstance(defName, defVersion, externalRef=submissionRef, metadata)`
3. Engine persists instance and returns `instanceId`.

### B) AutoStart (first move)
4. Engine performs initial workflow move internally (recommended: internal AutoStart event).
5. Engine calls its own:
   - `Trigger(instanceId, autoStartEventCode, requestId, metadata)`

### C) Macro transition handling
6. Whenever an engine event occurs, caller invokes:
   - `Trigger(instanceId, engineEventCode, requestId, metadata)`
7. Engine performs atomic work:
   - validate transition
   - update current state
   - insert transition log (includes requestId)

### D) Post-transition behavior
8. Engine checks policy cache for `(defName, defVersion, stateName, hook=enter)`.

If **no policy route exists**:
- Engine emits generic `TransitionOccurred` notice.
- Application may log/UI, and macro-ack “I’m aware”.

If **policy route exists**:
- Engine invokes one or more application actions via `IAppActionInvoker` (short-running).
- Application actions may:
  - create notifications/tasks in app DB (full audit lives here)
  - persist micro snapshots in engine (`instance_state_items`)
- After transition notice delivery, application sends macro acknowledgement (awareness).

### E) Micro logic drives next macro event
9. As micro work progresses (e.g., approvals):
- Application updates:
  - app DB (full audit)
  - engine snapshot (`instance_state_items` status updates)
- When the application decides a macro step is complete (e.g., quorum reached):
  - it raises the next engine event via:
    - `Trigger(instanceId, nextEngineEventCode, requestId, metadata)`

Engine remains dumb about micro rules; application decides when to move forward.

---

## 4) LCMonitor Responsibilities

LCMonitor runs on a timer and only calls the engine:
- **State timeouts**:
  - reads due instances for current state timeout
  - triggers timeout event via `Trigger(...)` with deterministic requestId
- **Macro acknowledgement monitoring**:
  - detects transitions where acknowledgement isn’t received
  - asks engine to re-notify / re-emit transition notice (crash resume)

LCMonitor never invokes app methods directly.

---

## 5) Idempotency Rules (Mandatory)

### Engine Trigger idempotency
- Every `Trigger()` must include a `requestId`.
- Engine enforces uniqueness:
  - `UNIQUE(instance_id, requestId)`
- Retried calls with the same requestId do not duplicate transitions.

Guidance:
- App major actions: generate once and reuse on retry.
- Monitor timeouts: deterministic requestId per due tick.

### Micro snapshot idempotency (optional but recommended)
- Use stable `item_key` per state.
- Optionally compute `item_hash` over canonical payload_json to detect duplicates.

---

## 6) What the Application Must Implement

- Entity → workflow mapping in `appsettings.json`:
  - which `definition_name + definition_version` to use per entity type
- Wrapper methods for workflow actions:
  - `[LC_APP_EVENT("APP.*")]` methods
  - must be short-running (setup/notify/create records)
- A listener for generic `TransitionOccurred` (if needed)
- Micro progression logic:
  - update micro items + decide when to call next macro engine event

---

## 7) Summary

- LCEngine owns macro state transitions and timeouts.
- Policies are loaded into engine memory cache and drive app action invocation.
- Application owns micro logic and decides when macro progression should happen.
- Engine stores only dumb micro snapshots (`instance_state_items`) for visibility/audit-summary.
- Idempotency is guaranteed using requestId on macro triggers and stable keys on micro items.
