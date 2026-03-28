# Backfill Design — Decisions and Rationale

## Business Problem

Legacy applications that operated before HaleyFlow was introduced have workflow history that lives only in application tables. When an organisation wants to switch from **Relay mode** (EventStore pub-sub, no persistence) to **Executor mode** (WorkflowEngine, full history), there is a gap: the engine has no record of what these entities have been through. Future triggers (amendments, renewals, escalations) need a known starting state. Backfill bridges that gap.

**What backfill is NOT**:
- It is not replay (re-executing business logic)
- It is not a migration wizard
- It is not related to the ACK/dispatch pipeline at all

**What backfill IS**:
- Writing read-only history rows to the engine DB
- Recording what happened, not making it happen again

---

## Design Decisions

### Decision 1 — Separate write-only path (not TriggerAsync)

**Rejected**: using `TriggerAsync` with a `SyncMode` flag to skip hook dispatch.

**Reason**: TriggerAsync is designed to drive active workflow execution. Bolting on a "skip everything" mode pollutes the core path with special-case branching and breaks the ACK contract (if no ACKs are created, how does the engine know the state transitioned?).

**Chosen**: A completely separate `ImportBackfillAsync` method that writes rows directly in the correct order, with no ACK creation and no hook dispatch.

### Decision 2 — No ACK rows = backfill marker

We do not add a separate `is_backfilled` flag to the database. Instead:

- `hook_lc.dispatched = 1` (to prevent the blocking hook gate treating it as pending)
- `ack_consumer` count = 0 for that `hook_lc` row

Any query that wants to know "was this imported or live?" checks `ack_consumer` count. Zero = backfill. This is visible naturally in the timeline view.

### Decision 3 — Validator is client-side (not engine-side)

The engine does a defence-in-depth re-check when `ImportBackfillAsync` is called, but the primary validation responsibility lies with `WorkflowBackfillValidator` on the consumer/client side.

**Why**: 1000 backfill entities should not make 1000 validation round-trips to the engine. The validator caches the definition snapshot and validates all objects against the in-memory cache.

The engine checks `obj.Validated == true` as a gate — it refuses unvalidated objects. The `Validated` flag has a `private set` / internal stamper so only `WorkflowBackfillValidator` can set it.

### Decision 4 — Hook validation is forgiving

State transition validation is hard (wrong transition = reject). Hook validation is soft (unknown route = warning, not error). Legacy systems often tracked major states but not every hook-level event.

### Decision 5 — One entity at a time

The import API is designed for one entity per call. Consumer loops through entities and calls `ImportBackfillAsync` sequentially. No concurrent bulk API is exposed — this prevents partial-write ambiguity and simplifies the engine-side write logic.

### Decision 6 — Replay is the consumer's problem, not an engine mode

Replay (re-triggering an instance through normal flow to reproduce history) is handled by the consumer starting a normal workflow instance with a `replay` metadata marker. The engine sees it as any other instance. Whether emails fire, whether side effects run — that is the consumer's responsibility (idempotency guards, feature flags, etc.). The engine has no "replay" concept at all.

---

## DefinitionWalker — Planned Enhancement

### Problem with raw backfill API

The current `WorkflowBackfillObject` approach requires the consumer to:
1. Fetch the definition snapshot
2. Know the valid state names and transition order
3. Build each `BackfillTransition` manually
4. Attach hooks manually

This is too much domain knowledge required from the application developer.

### The DefinitionWalker concept

**Core idea**: Haley owns the graph traversal. The consumer only owns the data.

The `DefinitionWalker` (planned for Phase A extension):
1. Fetches the definition snapshot
2. Walks the transition graph starting from the initial state
3. At each transition node, asks the consumer: "Did this entity go through this transition? If so, when, who, what data?"
4. Consumer returns data (or null = entity didn't reach this state)
5. Walker assembles the `WorkflowBackfillObject` automatically
6. Validates and sends to engine

```
DefinitionWalker.WalkAsync(snapshot, entityRef, IBackfillDataProvider, ct)
  → for each transition in graph order:
      → calls provider.GetTransitionDataAsync(fromState, toState, viaEvent)
      → if null: entity hasn't reached this state, stop walking
      → if data: add to BackfillObject, continue
      → for each hook in that transition:
          → calls provider.GetHookDataAsync(toState, viaEvent, route)
          → if null: hook not tracked, skip (warning logged)
          → if data: add hook to transition
  → validates assembled object
  → returns BackfillObject ready for ImportBackfillAsync
```

The consumer implements `IBackfillDataProvider` — a simple interface with two async callbacks. The walker does all the graph traversal.

### LifecycleWrapper integration

Each domain wrapper (e.g., `RegistrationWrapper`, `SubmissionWrapper`) can expose a `BackfillAsync(entityRef)` method. This method:
1. Creates a domain-specific `IBackfillDataProvider` that knows how to query the legacy DB for registration/submission data
2. Passes it to the `DefinitionWalker`
3. Receives the assembled `WorkflowBackfillObject`
4. Sends it to the engine via `ImportBackfillAsync`

The wrapper owns all domain knowledge. Haley owns all graph knowledge. Clean separation.

### Triggering backfill

When the system comes online (migration window), the consumer can trigger backfill for a set of entities via the FlowBus Executor or directly. The wrapper's `BackfillAsync` is called per entity. This fits naturally into the existing infrastructure without new pipeline concepts.

---

## Backfill Behaviour Table

| Scenario | Behaviour |
|---|---|
| Entity in definition snapshot | Accepted |
| Invalid transition (not in definition) | Hard reject from validator and engine |
| Unknown hook route | Warning only, recorded as-is |
| Partially reached entity (stopped mid-flow) | Supported — walk stops at last known state |
| Entity already exists in engine | Instance row upserted, new lifecycle rows appended |
| Consumer sends late ACK after timeout cancel | Rejected with STALE_ACK_RECEIVED notice |
| Backfill detection | `ack_consumer` count = 0 on `hook_lc` row |

---

## Cancelled ACK on Timeout

When a policy timeout fires (Case A timeout in monitor), the engine:
1. Calls `CancelPendingBlockingHookAckConsumersAsync` — sets all non-terminal blocking hook ACK consumers to `Cancelled` (status=5)
2. Fires `HOOK_ACK_CANCELLED` notice
3. Calls `TriggerAsync` with `SkipAckGate=true`

If a consumer then tries to ACK an already-cancelled (or Processed/Failed) hook:
- `AckManager.AckAsync` checks `currentStatus >= Processed (3)`
- Rejects the ACK (no DB write)
- Fires `STALE_ACK_RECEIVED` notice with ackGuid, consumerId, currentStatus, attemptedOutcome

`Cancelled=5` is a terminal status with the same finality as `Processed=3` and `Failed=4`. All queries using `NOT IN (3,4)` were updated to `NOT IN (3,4,5)`.

---

## Files Modified (Phase A)

| File | Change |
|---|---|
| `HaleyAbstractions.Core/.../Models/WorkflowDefinitionSnapshot.cs` | New — snapshot projection |
| `HaleyAbstractions.Core/.../Models/WorkflowBackfillObject.cs` | New — backfill object + transition + hook |
| `HaleyAbstractions.Core/.../Models/BackfillImportResult.cs` | New — import result |
| `HaleyAbstractions.Core/.../Interfaces/Services/ILifeCycleRuntimeBus.cs` | +2 methods |
| `HaleyFlowEngine/Services/WorkFlowEngine.cs` | Implemented GetDefinitionSnapshotAsync + ImportBackfillAsync |
| `HaleyFlowEngine/Interfaces/IWorkFlowEngineService.cs` | +2 methods |
| `HaleyFlowEngine/Services/Admin/WorkFlowEngineService.cs` | +2 pass-through implementations |
| `HaleyFlowEngine/Controllers/WorkFlowEngineControllerBase.cs` | GET /definition/snapshot + POST /backfill |
| `HaleyFlow.Consumer/.../Services/WorkflowBackfillValidator.cs` | New — client-side validator with cache |
| `QRY_HOOK.cs` | +3 new queries (pending blocking ACKs, undispatched, cancel) |
| `MariaHookDAL.cs` | +3 implementations |
| `InstanceOrchestrator.cs` | Blocking hook gate after ACK gate |
| `AckManager.cs` | Late-ACK terminal check + STALE_ACK_RECEIVED |
| `MonitorOrchestrator.cs` | Cancel hooks before timeout trigger |
| `QRY_ACK_LC.cs`, `QRY_HOOK_GROUP.cs`, `QRY_HOOK.cs`, `QRY_ACK_CONSUMER.cs` | NOT IN (3,4) → NOT IN (3,4,5) |
| `lc_state.sql` | DDL comment updated for Cancelled=5 |
