# Haley Flow Consumer — Integration Guide

The consumer library (`HaleyFlowConsumer`) is the application-side counterpart to the engine. It runs in your worker/API process, receives lifecycle events, dispatches them to your handler code, and ACKs results back to the engine.

---

## Architecture Overview

```
WorkFlowEngine (host A)
    │  EventRaised / ACK table rows
    ▼
WorkFlowConsumerManager (host B — your app)
    ├─ HeartbeatLoop  — keeps consumer row alive
    ├─ PollLoop       — polls engine DB for due events, dispatches
    └─ OutboxLoop     — retries ACK calls that failed to reach engine

    Per-event dispatch:
    ├─ WrapperRegistry  — maps def_id → LifeCycleWrapper subclass
    ├─ LifeCycleWrapper — your handler code (per definition)
    └─ Outbox           — persists ACK outcomes, retries until confirmed
```

**Deployment modes:**
- **In-process** — engine and consumer in the same process. Use `DeferredInProcessEngineProxy` (registered automatically by `AddWorkFlowConsumerService` when `IWorkFlowEngineAccessor` is present).
- **Remote** — consumer in a separate process/microservice. Implement `ILifeCycleEngineProxy` pointing at the engine's HTTP/gRPC transport and register it manually before calling `AddWorkFlowConsumerService`.

---

## 1. DI Registration

```csharp
// Program.cs

// Option A — from appsettings.json (reads "WorkFlowConsumer" section)
builder.Services.AddWorkFlowConsumerService(builder.Configuration);

// Option B — inline configuration
builder.Services.AddWorkFlowConsumerService(options => {
    options.ConsumerGuid    = "89c52807-5054-47fc-9dee-dbb8b42218cb"; // stable per process
    options.EnvCode         = 1;
    options.ConsumerAdapterKey = "consumer-db";
    options.MaxConcurrency  = 5;
    options.BatchSize       = 20;
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.PollInterval    = TimeSpan.FromSeconds(5);
});
```

**`addDeferredInProcessProxy` parameter (default `true`):**
When true, `ILifeCycleEngineProxy` is auto-registered using `DeferredInProcessEngineProxy`, which wraps `IWorkFlowEngineAccessor`. This means both engine and consumer services can share the same DI container without circular dependencies — the proxy resolves the accessor lazily on first use.

If consumer and engine are in separate processes, set `addDeferredInProcessProxy: false` and register your own `ILifeCycleEngineProxy` implementation.

---

## 2. `ConsumerServiceOptions` — appsettings.json

```json
{
  "WorkFlowConsumer": {
    "adapter_key":       "consumer-db",
    "env_code":          1,
    "consumer_guid":     "89c52807-5054-47fc-9dee-dbb8b42218cb",
    "env_name":          "production",
    "max_concurrency":   5,
    "batch_size":        20,
    "ttl_seconds":       120,
    "heartbeat_interval": "00:00:30",
    "poll_interval":     "00:00:05",
    "outbox_interval":   "00:00:15",
    "outbox_retry_delay": "00:02:00",
    "wrapper_assemblies": ["MyApp.Handlers"],
    "default_handler_upgrade": "Pinned"
  }
}
```

| Key | Description |
|-----|-------------|
| `adapter_key` | DB adapter key for the consumer's `lc_consumer` database |
| `consumer_guid` | Stable UUID for this process. Must be unique per consumer process. Keep it stable across restarts. |
| `env_code` | Environment code that matches the engine's `envCode` |
| `env_name` | Display name for this environment (informational) |
| `ttl_seconds` | Seconds after which a missing heartbeat marks this consumer as down (default 120) |
| `max_concurrency` | Max simultaneous event processing tasks (default 5) |
| `batch_size` | Events fetched per poll tick (default 20) |
| `poll_interval` | Sleep between poll ticks when nothing is due (default 5s) |
| `heartbeat_interval` | How often to send heartbeat (default 30s) |
| `outbox_retry_delay` | How long to wait before retrying a failed ACK delivery (default 2m) |
| `wrapper_assemblies` | Assembly names to scan for `[LifeCycleDefinition]` wrappers at startup |
| `default_handler_upgrade` | `Pinned` (default) or `Auto` — controls how handler versions progress for new events |

---

## 3. Writing a Handler — `LifeCycleWrapper`

Every workflow definition gets one `LifeCycleWrapper` subclass. The consumer service discovers it at startup via the `[LifeCycleDefinition]` attribute.

```csharp
[LifeCycleDefinition("loan-approval")]
public class LoanApprovalWrapper : LifeCycleWrapper {

    private readonly IEmailService _email;
    private readonly ILoanRepository _loans;

    // Constructor-inject your own services normally. IWorkFlowEngineAccessor is required
    // by the base class for wrapper-to-engine calls (TriggerAsync, UpsertRuntimeAsync).
    public LoanApprovalWrapper(
        IWorkFlowEngineAccessor engineAccessor,
        IEmailService email,
        ILoanRepository loans) : base(engineAccessor) {
        _email = email;
        _loans = loans;
    }

    // ── Transition handlers ───────────────────────────────────────────────────
    // Decorated with [TransitionHandler(eventCode)] where eventCode matches your
    // policy definition's event codes.

    [TransitionHandler(10)]  // event code 10 = "submit"
    public async Task<AckOutcome> OnSubmit(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
        // idempotent check via step tracking
        if (!await IsStepCompletedAsync(ctx, Steps.SendAcknowledgement)) {
            await StartStepAsync(ctx, Steps.SendAcknowledgement);
            await _email.SendSubmissionAcknowledgementAsync(evt.EntityId);
            await CompleteStepAsync(ctx, Steps.SendAcknowledgement);
        }
        return AckOutcome.Processed;
    }

    [TransitionHandler(20)]  // event code 20 = "approve"
    public async Task<AckOutcome> OnApprove(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
        var result = await ExecuteBusinessActionAsync(ctx, evt.DefinitionId, evt.EntityId,
            ActionCodes.DisburseAmount,
            async ct => {
                var amount = await _loans.GetApprovedAmountAsync(evt.EntityId, ct);
                await _loans.DisburseAsync(evt.EntityId, amount, ct);
                return new { disbursed = amount };
            });

        if (!result.Executed && result.AlreadyCompleted) {
            // already disbursed on a previous attempt — safe to ack processed
        }
        return AckOutcome.Processed;
    }

    // ── Hook handlers ─────────────────────────────────────────────────────────
    // Decorated with [HookHandler("route-name")] matching policy emit.event values.

    [HookHandler("notify_reviewer")]
    public async Task<AckOutcome> OnNotifyReviewer(ILifeCycleHookEvent evt, ConsumerContext ctx) {
        await _email.SendReviewRequestAsync(evt.EntityId, evt.Params);
        return AckOutcome.Processed;
    }

    [HookHandler("run_credit_check")]
    public async Task<AckOutcome> OnCreditCheck(ILifeCycleHookEvent evt, ConsumerContext ctx) {
        var passed = await ExternalCreditApi.CheckAsync(evt.EntityId, ctx.CancellationToken);
        if (!passed) return AckOutcome.Retry;  // will retry after back-off
        return AckOutcome.Processed;
    }

    // ── Required fallbacks ────────────────────────────────────────────────────
    // Must be implemented. Called when no decorated handler matches. Return
    // Processed to silently acknowledge, or Failed to flag as a bug.

    protected override Task<AckOutcome> OnUnhandledTransitionAsync(
        ILifeCycleTransitionEvent evt, ConsumerContext ctx)
        => Task.FromResult(AckOutcome.Processed);

    protected override Task<AckOutcome> OnUnhandledHookAsync(
        ILifeCycleHookEvent evt, ConsumerContext ctx)
        => Task.FromResult(AckOutcome.Processed);

    private static class Steps {
        public const int SendAcknowledgement = 1;
    }

    private static class ActionCodes {
        public const int DisburseAmount = 100;
    }
}
```

---

## 4. Handler Attributes

### `[LifeCycleDefinition("def-name")]`
Placed on the wrapper class. The consumer scans all loaded assemblies for this attribute at startup, resolves each name to its engine `def_id`, and registers the wrapper in `WrapperRegistry`.

### `[TransitionHandler(eventCode, minVersion = 1)]`
Maps the method to a lifecycle transition event by integer event code. The `minVersion` parameter enables handler versioning (see section 7).

### `[HookHandler("route-name", minVersion = 1)]`
Maps the method to a hook event by route name (matches `emit.event` in the policy JSON). An empty or omitted route matches hooks emitted without a route qualifier.

---

## 5. Step Tracking — Idempotency Checkpoints

Steps let a handler record fine-grained progress within a single event delivery. If the process crashes after doing the work but before ACKing, the monitor re-delivers the event. On retry, the handler checks the step table to skip already-completed work.

```csharp
if (!await IsStepCompletedAsync(ctx, Steps.SendEmail)) {
    await StartStepAsync(ctx, Steps.SendEmail);
    await emailService.SendAsync(evt.EntityId);
    await CompleteStepAsync(ctx, Steps.SendEmail);
}
```

- `StartStepAsync(ctx, stepCode)` — marks step Running
- `CompleteStepAsync(ctx, stepCode, result?)` — marks step Completed, optional result string
- `FailStepAsync(ctx, stepCode, error?)` — marks step Failed
- `IsStepCompletedAsync(ctx, stepCode)` — returns true if step is Completed

Steps are scoped by `wf_id` (one per event delivery). They reset on re-delivery to a new `wf_id`.

**Design for idempotency:** if your external operation (HTTP call, email, DB write) can be retried safely, use step tracking. If it cannot, use a business action (section 6) which has a stronger completed-skip guarantee.

---

## 6. Business Actions — Persistent Idempotent Operations

Business actions are keyed by `(consumer_id, def_id, entity_id, action_code)` across all events for the same entity. Unlike steps (which reset per delivery), a completed business action is never re-executed — even on a different event delivery or after a consumer restart.

```csharp
var result = await ExecuteBusinessActionAsync(
    ctx,
    evt.DefinitionId,
    evt.EntityId,
    ActionCodes.SendContractEmail,
    async ct => {
        await emailService.SendContractAsync(evt.EntityId, ct);
        return new { sentAt = DateTime.UtcNow };
    },
    mode: BusinessActionExecutionMode.SkipIfCompleted  // default
);

if (result.AlreadyCompleted) {
    // email was sent on a previous attempt — skip
}
```

**`BusinessActionExecutionMode`:**
- `SkipIfCompleted` (default) — if a Completed row exists, return its result without re-executing.
- Other modes — execute regardless.

**`BusinessActionExecutionResult`:**
- `Executed` — true if the action ran this call
- `AlreadyCompleted` — true if the action was skipped due to a prior completion
- `ResultJson` — serialized result from the action delegate (or prior run)

**Reading decisions from result JSON:**
```csharp
var approved = ReadDecisionFromResultJson(result.ResultJson, defaultValue: true);
// Reads the "decision" boolean field from the result JSON. Useful when
// the action stored an approval/rejection decision for later steps.
```

---

## 7. Handler Versioning

Handler versioning lets you evolve handler logic for new instances without breaking in-flight ones. Each `[TransitionHandler]` / `[HookHandler]` method declares a `minVersion` (default 1). At dispatch time the consumer picks the highest `minVersion` that is still ≤ the instance's pinned handler version.

**Example:**
```csharp
[TransitionHandler(10, minVersion: 1)]
public Task<AckOutcome> OnSubmitV1(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
    // Original logic
}

[TransitionHandler(10, minVersion: 3)]
public Task<AckOutcome> OnSubmitV3(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
    // Improved logic for instances that started on or after version 3
}
```

- An instance first seen at def_version 2 is pinned to handler version 2 → uses `V1` handler.
- An instance first seen at def_version 4 is pinned to version 4 → uses `V3` handler.
- Pinning is per-entity and stable — old instances always use the old handler.

**`default_handler_upgrade`:**
- `Pinned` — version is set once at first event delivery and never changes.
- `Auto` — version advances with each new def_version (instances effectively follow the latest handler).

---

## 8. Triggering the Next State from a Handler

Wrappers can trigger the next engine transition directly using `EngineAccessor`:

```csharp
[TransitionHandler(20)]  // approve
public async Task<AckOutcome> OnApprove(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
    var passed = await RunChecksAsync(evt.EntityId, ctx.CancellationToken);

    var eng = await EngineAccessor.GetEngineAsync(ctx.CancellationToken);
    await eng.TriggerAsync(new LifeCycleTriggerRequest {
        EnvCode     = evt.EnvCode,
        DefName     = evt.DefinitionName,
        ExternalRef = evt.EntityId,
        Event       = passed ? "approve" : "reject",
        RequestId   = $"{evt.EntityId}-approve-{evt.InstanceGuid}",
        Actor       = "system",
        AckRequired = true
    });

    return AckOutcome.Processed;
}
```

Use `PickEvent(preferred, fallback)` when the event name comes from the policy's `complete.success` / `complete.failure` fields:
```csharp
var nextEvent = PickEvent(evt.OnSuccessEvent, "approve");
```

---

## 9. Consumer Notice Codes

Subscribe via `consumerService.Consumer.NoticeRaised` (or relay through your logging):

```csharp
var manager = await consumerService.GetConsumerAsync();
manager.NoticeRaised += async n => {
    logger.Log(n.Severity switch {
        "Error" => LogLevel.Error,
        "Warn"  => LogLevel.Warning,
        _       => LogLevel.Information
    }, "[{Code}] {Message}", n.Code, n.Message);
};
```

| Code | Severity | Meaning |
|------|----------|---------|
| `HEARTBEAT_ERROR` | Error | Heartbeat call to engine failed |
| `POLL_ERROR` | Error | Poll loop error (network/DB blip) |
| `OUTBOX_ERROR` | Error | Outbox loop error |
| `OUTBOX_ACK_FAILED` | Error | Outbox retry failed to reach engine |
| `DISPATCH_SCHEDULE_ERROR` | Error | Failed to schedule item for processing (item will be re-sent by monitor) |
| `DISPATCH_ERROR` | Error | Unhandled exception in `ProcessItemAsync` |
| `DISPATCH_INVALID_ACK` | Error | Event arrived with missing ack_guid — skipped |
| `WRAPPER_ERROR` | Error | Wrapper threw during handler execution — outcome set to Retry |
| `REGISTRY_MISS` | Warn | Received event for a def_id not registered in this consumer — ignored |
| `REGISTRY_RESOLVE_FAILED` | Warn | Definition name not found in engine at startup — handler will not receive events |

---

## 10. Admin Reads — Consumer DB

`IWorkFlowConsumerService` exposes paged reads of the consumer DB tables:

```csharp
// List consumer-side instance mirror rows
var instances = await consumerService.ListInstancesAsync(new ConsumerInstanceFilter {
    EntityGuid = "cabd2ed2-ab6c-4986-ab46-3a1ef415ca56",
    DefName = "change-request",
    Skip = 0,
    Take = 50
}, ct);

// List raw inbox event rows
var inbox = await consumerService.ListInboxAsync(new ConsumerInboxFilter {
    Kind = WorkflowKind.Transition,
    DefId = 1998,
    Skip = 0,
    Take = 50
}, ct);

// List inbox processing status rows
var inboxStatus = await consumerService.ListInboxStatusesAsync(new ConsumerInboxStatusFilter {
    Status = InboxStatus.Failed,
    InstanceGuid = "2fdb6730-2018-11f1-8441-8c8caad7d6a5",
    Skip = 0,
    Take = 50
}, ct);

// List outbox rows (ACK outcomes and delivery status)
var outbox = await consumerService.ListOutboxAsync(new ConsumerOutboxFilter {
    Status = OutboxStatus.Pending,
    Kind = WorkflowKind.Hook,
    Skip = 0,
    Take = 50
}, ct);

// Quick counts
var pendingInbox  = await consumerService.CountPendingInboxAsync(ct);
var pendingOutbox = await consumerService.CountPendingOutboxAsync(ct);
```

Use the extension methods on `DbRows` to get enum-friendly dictionaries:
```csharp
var rows = await consumerService.ListInboxStatusesAsync(new ConsumerInboxStatusFilter { Skip = 0, Take = 50 }, ct);
var dicts = rows.ToInboxStatusDictionaries();
```

---

## 11. Registering Additional Assemblies at Runtime

If handler assemblies are loaded dynamically after startup:
```csharp
var svc = app.Services.GetRequiredService<IWorkFlowConsumerService>();
svc.RegisterAssembly("MyApp.NewHandlers");         // by name
svc.RegisterAssembly(typeof(SomeWrapper).Assembly); // by Assembly reference
```

Assemblies registered before `EnsureHostInitializedAsync` are merged with any `wrapper_assemblies` from config. Assemblies registered after startup are forwarded directly to the live `WorkFlowConsumerManager`.

---

## 12. Three Background Loops

| Loop | Interval | Role |
|------|----------|------|
| **HeartbeatLoop** | `heartbeat_interval` (default 30s) | Calls `BeatConsumerAsync` to update `last_beat`. If this stops, the engine monitor stops delivering events to this consumer after `ttl_seconds`. |
| **PollLoop** | `poll_interval` (default 5s, backs off when queue is empty) | Fetches due lifecycle + hook `ack_consumer` rows via `GetDueTransitionsAsync` / `GetDueHooksAsync` and dispatches them. This is the **pull** path — it catches anything the engine's push (`EventRaised`) missed. |
| **OutboxLoop** | `outbox_interval` (default 15s) | Scans `outbox` rows in `Pending` status where `next_retry_at <= now` and retries `AckAsync` calls that failed on the first attempt. Guarantees at-least-once ACK delivery even when the engine is temporarily unreachable. |

---

## 13. Quick-Start Checklist

```csharp
// 1. Register services (in-process with engine in same host)
builder.Services.AddWorkFlowEngineService(builder.Configuration, resolveConsumerGuids: ...);
builder.Services.AddWorkFlowConsumerService(builder.Configuration);

// appsettings.json
{
  "WorkFlowConsumer": {
    "adapter_key":    "consumer-db",
    "env_code":       1,
    "consumer_guid":  "89c52807-5054-47fc-9dee-dbb8b42218cb",
    "max_concurrency": 5
  }
}

// 2. Write your wrapper
[LifeCycleDefinition("loan-approval")]
public class LoanApprovalWrapper : LifeCycleWrapper {
    public LoanApprovalWrapper(IWorkFlowEngineAccessor acc, IEmailService email)
        : base(acc) { _email = email; }

    [TransitionHandler(10)]
    public async Task<AckOutcome> OnSubmit(ILifeCycleTransitionEvent evt, ConsumerContext ctx) {
        await _email.SendAckAsync(evt.EntityId);
        return AckOutcome.Processed;
    }

    protected override Task<AckOutcome> OnUnhandledTransitionAsync(
        ILifeCycleTransitionEvent evt, ConsumerContext ctx)
        => Task.FromResult(AckOutcome.Processed);

    protected override Task<AckOutcome> OnUnhandledHookAsync(
        ILifeCycleHookEvent evt, ConsumerContext ctx)
        => Task.FromResult(AckOutcome.Processed);
}

// 3. The consumer bootstraps automatically via IHostedService (autoStart: true).
//    On first trigger (or on host startup), it will:
//    - Scan assemblies for [LifeCycleDefinition] wrappers
//    - Resolve def names → def_ids from the engine
//    - Register consumer identity with the engine
//    - Start heartbeat, poll, and outbox loops

// 4. Subscribe to notices for observability
var manager = await app.Services
    .GetRequiredService<IWorkFlowConsumerService>()
    .GetConsumerAsync();
manager.NoticeRaised += async n => logger.LogWarning("[{Code}] {Msg}", n.Code, n.Message);
```
