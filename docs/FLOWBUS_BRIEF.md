# IFlowBus — Design Brief for HaleyFlow

## Business Problem

Application business methods should be **sequence-agnostic**. A method that completes a vendor review should not know that "after review, create company" or "after company creation, send credentials" — that is responsibility pollution. Methods do their work and return a result. Something else decides what comes next.

Currently, this "something else" is scattered: EventStore publishes, Utils orchestrate, MDL classes chain calls. There is no single seam.

**IFlowBus is that seam.**

---

## What IFlowBus Is

A single interface that a **service layer** calls after completing a unit of work. The service says: "this step is done, here is the result." IFlowBus decides what to do next — without the service knowing how.

```
[SubmissionService.CompleteReview()]
    → result: true
    → _flowBus.Next("review_completed", context)
                        │
              ┌─────────┴──────────┐
              ▼                    ▼
      RelayFlowBus          ExecutorFlowBus
   (EventStore path)     (WorkflowEngine path)
```

Which implementation is active is controlled by appsettings. Business logic never changes.

---

## Two Modes

### Mode 1 — Relay (offline / local / debug)

- **Backed by**: EventStore (HaleyEvents pub-sub, already in the codebase)
- **Sequence knowledge**: None — fires an event and lets existing subscribers handle it
- **Persistence**: None — if the process crashes, the sequence is lost
- **External dependency**: None — works with zero infrastructure
- **Best for**: Local development, debugging, demos, scenarios where crash-tolerance is not required

The Relay mode is essentially **the current EventStore wiring**, wrapped behind IFlowBus. No behaviour change — just a clean abstraction over what already exists.

### Mode 2 — Executor (production / crash-tolerant)

- **Backed by**: HaleyFlow WorkflowEngine (existing `IWorkFlowConsumerService`)
- **Sequence knowledge**: Defined in workflow definition JSON (states + transitions)
- **Persistence**: Full — MariaDB tracks every transition, every hook, every ACK
- **External dependency**: WorkflowEngine process + its database
- **Best for**: Production, long-running processes, anything that must survive a crash and resume

The Executor mode calls `TriggerAsync` on the engine with the appropriate event code. The engine drives progression through the policy-defined sequence.

---

## IFlowBus Interface (proposed)

```csharp
public interface IFlowBus {

    /// <summary>
    /// Called by a service after completing a unit of work.
    /// stepKey identifies the completed step (e.g. "review_completed", "account_created").
    /// context carries entity identity and any payload the next step needs.
    /// </summary>
    Task Next(string stepKey, FlowContext context, CancellationToken ct = default);

    /// <summary>
    /// Backfill mode — reads existing application state and registers it with the
    /// workflow engine without executing any transitions. Used to sync when switching
    /// from Relay to Executor mode after the fact.
    /// </summary>
    Task Sync(string workflowName, string entityRef, string currentStep,
              object state, CancellationToken ct = default);

    /// <summary>
    /// Returns the current status of an entity's flow.
    /// Only meaningful in Executor mode; returns null in Relay mode.
    /// </summary>
    Task<FlowStatus?> GetStatus(string workflowName, string entityRef,
                                CancellationToken ct = default);

    /// <summary>
    /// Current mode this instance is running in.
    /// </summary>
    FlowBusMode Mode { get; }
}

public enum FlowBusMode {
    Relay,     // EventStore, no persistence, offline-capable
    Executor,  // WorkflowEngine, crash-tolerant, full history
    Sync       // Backfill-only, records state without executing
}

public class FlowContext {
    public string EntityRef    { get; set; }  // e.g. submission GUID
    public string WorkflowName { get; set; }  // e.g. "prelim-review", "tech-review"
    public string Actor        { get; set; }  // who triggered this step
    public string RequestId    { get; set; }  // stable idempotency key
    public object Payload      { get; set; }  // step-specific data
}

public class FlowStatus {
    public string CurrentState  { get; set; }
    public bool   IsCompleted   { get; set; }
    public bool   IsSuspended   { get; set; }
    public string InstanceGuid  { get; set; }
}
```

---

## Service Layer Usage (how application code calls it)

```csharp
// SubmissionService — does NOT know about sequences
public class SubmissionService : ISubmissionService {
    private readonly IFlowBus _flowBus;

    public async Task<IFeedback> CompleteReview(string guid, long resourceId, ...) {
        // 1. Do the work — pure business logic
        var result = await _dal.Submission.Reviewer.SetFlagsAsync(...);
        await GenesisUtils.LogAsync(...);

        // 2. Advance the sequence — that's all
        if (result.Status) {
            await _flowBus.Next("review_completed", new FlowContext {
                EntityRef    = guid,
                WorkflowName = formName,
                Actor        = resourceId.ToString(),
                RequestId    = $"{guid}-review-{resourceId}"
            });
        }

        return result;
    }
}
```

`SubmissionService` does not know whether EventStore or WorkflowEngine handles "review_completed". It does not know what comes next. It only says: "this step is done."

---

## Relay Implementation — What It Does

When `FlowBusMode.Relay`, `Next("review_completed", ctx)`:

1. Maps `stepKey` to an EventStore event type via a local step map (a `Dictionary<string, Type>` registered at startup)
2. Calls `EventStore.Get<TEvent>().Publish(payload)` — exactly as the current code does
3. Existing subscribers in `Initializer.cs` handle the rest

The Relay mode **does not change existing EventStore behaviour**. It is a wrapper. Current `Initializer.cs` subscriptions remain unchanged.

---

## Executor Implementation — What It Does

When `FlowBusMode.Executor`, `Next("review_completed", ctx)`:

1. Maps `stepKey` to a workflow event name (e.g. `"review_completed"` → event code `30`)
2. Calls `engine.TriggerAsync(new LifeCycleTriggerRequest { ... })`
3. The WorkflowEngine handles progression: transitions, hook emission, ACK tracking, monitor retries

The `LifeCycleWrapper` (per workflow definition) handles hooks — it calls existing business methods that were previously called inline.

---

## Sync Mode — Backfilling

Used when switching from Relay → Executor on a live system that already has data.

`Sync("prelim-review", submissionGuid, "under_review", state)`:
1. Calls engine with a special "sync" flag in the trigger request
2. Engine records the instance and its current state **without firing hooks or dispatching events**
3. Existing in-progress entities are now tracked in the engine's DB
4. Future transitions proceed normally via Executor mode

**Engine-side requirement:** `TriggerAsync` needs to support a `SyncMode = true` flag that creates/updates the instance row and lifecycle row but skips hook emission and ACK creation. This is new engine functionality to be implemented.

---

## Configuration

```json
// appsettings.json
{
  "FlowBus": {
    "mode": "Relay"
  }
}
```

| Value | Behaviour |
|-------|-----------|
| `Relay` | EventStore path. No external dependency. |
| `Executor` | WorkflowEngine path. Requires engine + consumer configured. |
| `Sync` | Backfill-only. Executor must also be reachable. |

One flag change + restart = full mode switch. No code changes.

---

## Suggested Project Placement

| Component | Location |
|-----------|----------|
| `IFlowBus`, `FlowBusMode`, `FlowContext`, `FlowStatus` | `HaleyAbstractions` (alongside `IWorkFlowConsumerService`) |
| `RelayFlowBus` (EventStore impl) | `HaleyFlow.Consumer` project (has EventStore + consumer access) |
| `ExecutorFlowBus` (WF engine impl) | `HaleyFlow.Consumer` project |
| `AddFlowBus(config)` DI extension | `HaleyFlow.Consumer` project |
| Step-key → event mapping convention | Each consuming application registers its own map at startup |

---

## What Needs to Be Built (ordered)

1. **`IFlowBus` + supporting types** in `HaleyAbstractions`
2. **`RelayFlowBus`** in `HaleyFlow.Consumer` — wraps EventStore, reads step map from DI
3. **`ExecutorFlowBus`** in `HaleyFlow.Consumer` — wraps `IWorkFlowEngineAccessor.TriggerAsync`
4. **`AddFlowBus(IConfiguration)`** DI extension — reads `FlowBus:mode`, registers correct impl as `IFlowBus`
5. **Sync mode flag** on `LifeCycleTriggerRequest` — `bool SyncMode` — engine skips hooks/ACKs when true
6. **Engine-side Sync handling** in `TriggerAsync` — create instance + lifecycle row only, no hooks

Items 1–4 are self-contained HaleyFlow changes. Items 5–6 touch the engine's `TriggerAsync` method.

---

## What Does NOT Change

- `EventStore` and `HEvent<T>` — untouched
- Existing `Initializer.cs` event subscriptions in consuming apps — untouched (Relay mode calls them as-is)
- `LifeCycleWrapper` handlers — unchanged (Executor mode calls them via existing hook dispatch)
- `IWorkFlowConsumerService` / `IWorkFlowEngineAccessor` interfaces — unchanged (ExecutorFlowBus uses them as-is)
- Business logic methods — they gain one `_flowBus.Next(...)` call at the end; everything else stays

---

## Relation to EventStore Receipt Mechanism

`IFlowBus` and `EventStore.Expect<T>()` / `EventStore.TryComplete()` solve **different problems**:

| | IFlowBus | Receipt |
|--|---------|---------|
| **Purpose** | Advance a sequence to the next step | Get a result back from an async handler in the same request |
| **Direction** | Fire-and-advance (one-way) | Request/response (bidirectional) |
| **Who uses it** | Service layer, after completing a step | Service layer, when it needs the handler's output synchronously |
| **When** | Every multi-step flow | Only when the HTTP response depends on an async result |

Both are needed. They complement each other.
