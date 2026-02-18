# Haley Flow Engine — Application Implementation Contract

This document is the practical contract between an application team and the Haley Flow Engine runtime.

## 1) Responsibilities split

### Engine owns (macro workflow)
- Definition/state/event/transition storage and validation.
- Instance state progression (`current_state`, lifecycle history).
- Policy-driven hook emission.
- Timeout scans and monitor-based retries.
- Acknowledgement tracking and retry/suspension behavior.

### Application owns (micro business logic)
- Business decisions and work-item orchestration.
- Hook handlers and downstream side effects.
- Choosing when to trigger the next macro event.
- Operational ownership of consumer health and acknowledgements.

---

## 2) Integration prerequisites

Before runtime operations, the application must:
1. Initialize lifecycle schema/database.
2. Import (or verify) definition JSON.
3. Import (or verify) policy JSON.
4. Register and heartbeat at least one consumer.
5. Start monitor loop.

If no transition consumer is resolvable, trigger operations are rejected.

---

## 3) Trigger contract

Every macro transition call must provide:
- `EnvCode`
- `DefName`
- `ExternalRef` (stable business key)
- `Event` (code/name)
- `RequestId` (stable for retry attempts)
- `Actor` (optional but recommended)
- `AckRequired` (recommended true for reliability)
- Optional payload metadata

Expected behavior:
- Engine resolves latest blueprint for `(env, definition)`.
- Engine ensures/loads instance by `(def_version, external_ref)`.
- Engine validates transition and applies it atomically.
- Engine persists lifecycle + metadata.
- Engine emits transition event and policy-derived hook events after commit.

---

## 4) Acknowledgement contract

When `AckRequired` is true:
- Transition and hook events may carry an `AckGuid`.
- Consumer must acknowledge with one of: `Delivered`, `Processed`, `Retry`, `Failed`.

Recommended ACK flow:
1. On receipt: ACK `Delivered`.
2. After successful business completion (optional second phase pattern): ACK `Processed`.
3. On transient fault: ACK `Retry` with optional `retryAt`.
4. On terminal inability: ACK `Failed`.

If ACK is missing or delayed, monitor re-dispatches by schedule.
If retries exceed configured max, instance may be suspended.

---

## 5) Consumer/monitor contract

Application must maintain consumer liveness:
- Register consumer identity per environment.
- Send heartbeats regularly.
- Keep monitor running at configured interval.

Monitor responsibilities in engine:
- Retry due lifecycle/hook dispatches.
- Evaluate timeouts and emit timeout-driven transitions (if configured).
- Raise notices for errors/stale states.

---

## 6) Idempotency requirements

Application side (mandatory):
- Reuse the same `RequestId` for retried trigger attempts.
- Keep `ExternalRef` stable for the same business entity.

Application side (recommended):
- Make hook handlers idempotent by business key + ack guid.
- De-duplicate repeated deliveries at consumer boundary.

Engine side:
- Uses CAS-like state update to avoid concurrent state corruption.
- Persists dispatch state and retry counters in ACK tables.

---

## 7) Failure semantics

- Trigger path is transactional for DB state changes.
- Event dispatch occurs after commit.
- Handler failure does not roll back committed lifecycle changes.
- Monitor attempts recovery by re-dispatch using due ACK rows.
- Repeated dispatch failures can mark ACK failed and suspend instance.

---

## 8) Operational checklist

### Required
- [ ] Log and alert on notices (`MONITOR_ERROR`, `TRIGGER_ERROR`, `ACK_RETRY`, `ACK_SUSPEND`).
- [ ] Alert on rising pending/delivered ACK backlog.
- [ ] Track suspended instances and provide operator runbook.

### Recommended
- [ ] Centralize payload schema/versioning for hook params.
- [ ] Redact or encrypt sensitive payload fields.
- [ ] Define retention/archival for lifecycle/runtime tables.

---

## 9) Minimal happy-path sequence

1. App starts engine, imports definition/policy, registers consumer.
2. App triggers first macro event for `ExternalRef`.
3. Engine applies transition and emits transition event/hook events.
4. App ACKs events and runs business logic.
5. App triggers next macro event when business rule is satisfied.
6. Flow proceeds until final state.

