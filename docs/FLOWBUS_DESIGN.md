# FlowBus Design — Decisions and Rationale

## Business Problem

Service-layer methods know too much. A `SubmissionService.CompleteReview()` method currently knows that "after review, create company" or "after company creation, send credentials." That is sequence knowledge — it belongs in the workflow definition, not the business method. When the sequence changes, business methods have to change. When the sequencing engine changes (EventStore → WorkflowEngine), all the wiring changes.

**IFlowBus is the seam** that separates "doing work" from "deciding what comes next."

After completing a unit of work, the service says: `await _flowBus.NextAsync(ctx)` and that is all it knows. FlowBus decides how to advance the sequence.

---

## Two Modes

### Relay Mode (offline / debug / legacy)

- Backed by EventStore (HaleyEvents pub-sub, already in the codebase)
- The step-key or outcome maps to an EventStore event Type via a registered step map
- No persistence — crash = lost progress
- Zero infrastructure requirements — works for local dev, demos, legacy apps
- The existing `Initializer.cs` subscriptions remain untouched; Relay just wraps them

### Executor Mode (production / crash-tolerant)

- Backed by `IWorkFlowEngineAccessor.TriggerAsync`
- The JSON definition is the sequence contract
- Full persistence via MariaDB — crash-tolerant, resumable
- The LifecycleWrapper handles hook-driven progression internally (not via FlowBus)

---

## Key Decisions

### Decision 1 — No event codes exposed to business code

`NextAsync` accepts an optional `outcome` string (e.g., `"approved"`, `"rejected"`) — not an event code. Event codes are an internal engine detail. The outcome maps to an event name via the step map; the engine resolves the event name to its code.

```csharp
await _flowBus.NextAsync(ctx, outcome: "approved");
// → step map resolves "approved" for this workflow → event name "review_complete"
// → engine resolves "review_complete" → event code 4000
// → TriggerAsync fires
```

### Decision 2 — NextAsync returns FlowBusResult, never void

FlowBus never swallows failures silently. If the engine says `BlockedByPendingBlockingHook`, the caller gets back a `FlowBusResult { Applied=false, Reason="BlockedByPendingBlockingHook" }`. The service can log, alert, or surface this to the user.

### Decision 3 — AutoTransitionAsync inside handlers, not NextAsync

Inside a `LifecycleWrapper` handler (`[TransitionHandler]`, `[HookHandler]`), the correct way to trigger the next step is `AutoTransitionAsync` — not `_flowBus.NextAsync`.

**Why**: `[TransitionHandler]` has a live pending `lc_ack` row. Calling `NextAsync` (which calls `TriggerAsync`) would be blocked by the ACK gate immediately. `AutoTransitionAsync` stores the next event in the outbox and fires after ACK confirmation.

`[HookHandler]` could technically call `TriggerAsync` safely (hook ACKs are in `hook_ack`, invisible to the `lc_ack` gate), but `AutoTransitionAsync` is still preferable for consistency and to respect blocking hook gate rules.

### Decision 4 — JSON definition is the shared sequence contract

Both Relay and Executor modes read the same workflow definition JSON. The JSON defines:
- States and terminal conditions
- Valid transitions (from → to via event)
- Hook routes per transition

This means switching from Relay to Executor requires no code changes — just a config switch. The definition JSON was already the contract; FlowBus formalises this.

### Decision 5 — Step map is registered by the consuming application

Haley provides the `IFlowBusStepMap` interface. Each application registers its own implementation at startup mapping workflow names + outcomes → event names (Executor) or event Types (Relay).

This keeps Haley infrastructure generic and application-specific routing in the application where it belongs.

### Decision 6 — No SyncMode flag on TriggerAsync

The brief suggested a `SyncMode=true` flag on `TriggerAsync`. This was rejected. Backfill is handled by `ImportBackfillAsync` — a completely separate path. `TriggerAsync` stays clean with no special-case flags.

---

## IFlowBus Interface (planned for Phase B/C)

```csharp
public interface IFlowBus {
    FlowBusMode Mode { get; }

    // Called by service layer after completing a unit of work.
    // outcome hint selects the transition path when a state has multiple exits.
    // Returns FlowBusResult — Applied=false with Reason when blocked.
    Task<FlowBusResult> NextAsync(FlowContext ctx, string? outcome = null, CancellationToken ct = default);

    // Returns current status. Meaningful only in Executor mode; returns null in Relay.
    Task<FlowStatus?> GetStatusAsync(FlowContext ctx, CancellationToken ct = default);
}
```

---

## Backfill Integration with FlowBus and LifecycleWrapper

### DefinitionWalker (planned Phase A extension)

The `DefinitionWalker` is a Haley-provided utility that:
1. Fetches the definition snapshot
2. Walks the transition graph from initial state
3. At each transition, calls a consumer-provided `IBackfillDataProvider` callback
4. Assembles a valid `WorkflowBackfillObject` from the responses
5. Validates and returns it, ready for `ImportBackfillAsync`

The consumer implements `IBackfillDataProvider`:
```csharp
interface IBackfillDataProvider {
    // Return null if this entity did not pass through this transition.
    Task<BackfillStateData?> GetTransitionDataAsync(string fromState, string toState, string viaEvent, CancellationToken ct);
    // Return null if this hook was not tracked in the legacy system.
    Task<BackfillHookData?> GetHookDataAsync(string toState, string viaEvent, string route, CancellationToken ct);
}
```

### LifecycleWrapper backfill method

Each domain wrapper (e.g., `RegistrationWrapper`) overrides:
```csharp
protected virtual Task<IBackfillDataProvider?> CreateBackfillDataProviderAsync(string entityRef, CancellationToken ct)
    => Task.FromResult<IBackfillDataProvider?>(null); // default: no backfill
```

The wrapper returns a domain-specific provider that queries the legacy DB. Haley's `DefinitionWalker` does all graph traversal. The wrapper then:
1. Calls `DefinitionWalker.WalkAsync(...)`
2. Gets back a validated `WorkflowBackfillObject`
3. Sends it to engine via `ImportBackfillAsync`

### Triggering backfill on startup

When the system migrates from Relay → Executor, the consumer triggers backfill for existing entities. This can be done via:
- A startup job that loops through legacy entities and calls `wrapper.BackfillAsync(entityRef)` per entity
- Via FlowBus Executor if the infrastructure is already online

The wrapper owns all domain knowledge. Haley owns all graph knowledge. No cross-contamination.

---

## Separation of Concerns

| Layer | Responsibility |
|---|---|
| Service layer | Does business work, calls `_flowBus.NextAsync(ctx)` |
| IFlowBus (Relay) | Maps outcome → EventStore event, publishes |
| IFlowBus (Executor) | Maps outcome → event name, calls `TriggerAsync` |
| Engine | Drives transitions, emits hooks, tracks ACKs |
| LifecycleWrapper | Handles hook dispatch, calls `AutoTransitionAsync` |
| DefinitionWalker | Graph traversal for backfill |
| IBackfillDataProvider | Domain data lookup for backfill (app responsibility) |

---

## What Does NOT Change

- `EventStore` / `HEvent<T>` — untouched
- Existing `Initializer.cs` subscriptions — untouched (Relay wraps them as-is)
- `LifeCycleWrapper` handlers — unchanged
- `IWorkFlowConsumerService` / `IWorkFlowEngineAccessor` interfaces — unchanged
- Business logic methods — gain one `await _flowBus.NextAsync(ctx)` at the end; everything else stays
