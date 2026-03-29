# Definition & Policy Contract

Rules governing how workflow definition JSON and policy JSON are parsed, validated, and executed.
Applies equally to the relay (`WorkflowRelay`) and the engine (`WorkFlowEngine`).

---

## 1. Definition JSON Structure

### States
- Every state has a `name`.
- Exactly **one** state must be marked `is_initial: true` — this is the entry point.
- One or more states may be marked `is_final: true` — these are terminal states.
- A state not marked `is_final` but with no outgoing transitions is a structural error.

### Transitions
- Each transition has: `from`, `to`, `event` (numeric code), optional `eventName`.
- Event codes must be **unique per FromState** — duplicate codes from the same state are ambiguous.
- `FromState` and `ToState` must both exist in the states list — mismatches indicate spelling mistakes.

### Self-loops
- A self-loop (`from == to`) with **no other exit transitions** from that state is a critical error — guaranteed infinite loop.
- A self-loop with other exit transitions is a warning — likely intentional (reminder, timeout ping).

---

## 2. Policy JSON Structure

### Top-level params catalog
```json
"params": [
  { "code": "PARAMS.LOAN.MANAGER.APPROVAL", "data": { ... } }
]
```
- Defines named parameter entries available to rules and hooks.
- `code` is the unique key. `data` is a free-form object (approval rules, role selectors, thresholds, etc.).

### Rules
```json
"rules": [
  {
    "state": "ManagerApproval",
    "via": 2005,
    "params": [ "PARAMS.LOAN.MANAGER.APPROVAL" ],
    "complete": { "success": 2007, "failure": 2008 },
    "emit": [ ... ]
  }
]
```

- `state` — the state being **arrived at** (ToState). The rule fires when this state is entered.
- `via` — optional. If present, the rule only applies when the state is entered via this specific event code. If absent, the rule applies to all transitions that arrive at this state.
- `params` — optional list of param codes from the top-level catalog. Applied to the transition and inherited by hooks unless overridden.
- `complete` — optional. Defines auto-advance event codes for the transition handler (`success`, `failure`).
- `emit` — list of hooks to fire upon arrival at this state.

### Hooks (emit entries)
```json
{
  "route": "APP.LOAN.MANAGER.DECISION",
  "label": "Manager Approval Decision",
  "blocking": true,
  "order": 1,
  "complete": { "success": 2007, "failure": 2008 },
  "params": [ "PARAMS.LOAN.MANAGER.APPROVAL" ]
}
```

- `route` — unique identifier for the hook handler.
- `blocking` — if `true`, failure stops the chain. If `false`, result is ignored entirely.
- `order` — execution sequence. Hooks without an order run **last** (treated as `int.MaxValue`). Once any hook defines an order, unordered hooks are pushed to the end.
- `complete` — optional. Hook-own success/failure event codes. Only respected on **blocking** hooks.
- `params` — optional. Hook-own param codes. If absent, inherits parent rule's param codes.

---

## 3. Parameter Resolution

| Context | Resolution |
|---|---|
| Transition handler | `transition.ParamCodes` (from the rule-level `params`) |
| Hook handler | Hook-own `params` if defined; falls back to parent rule `params` if not |

Parameters are resolved into `ctx.Parameters` before each handler call so business logic can read approval rules, roles, thresholds, etc. directly.

---

## 4. Execution Contract (NextAsync)

### Transition handler
| Outcome | Action |
|---|---|
| Handler returns `false` (failure) | Skip ALL hooks. Fire `CompleteFailureCode` immediately if set. If not set, return `Blocked`. |
| Handler returns `true` (success) | Hold `CompleteSuccessCode`. Run all hooks first. Fire success code AFTER all hooks complete. |

### Hooks (in `OrderSeq` order)
| Outcome | Action |
|---|---|
| Non-blocking hook (any result) | Continue to next hook. Complete codes on non-blocking hooks are **ignored entirely**. |
| Blocking hook fails + has `CompleteFailureCode` | Skip remaining hooks. Fire failure event immediately. |
| Blocking hook fails + no `CompleteFailureCode` + transition has `CompleteFailureCode` | Skip remaining hooks. Fire transition's failure event immediately. |
| Blocking hook fails + no failure path anywhere | Roll back `ctx.CurrentState` to `FromState`. Return `Blocked`. |
| Blocking hook succeeds + has `CompleteSuccessCode` | Capture success code. Continue remaining hooks. Fire after all hooks complete. |

### After all hooks
- Fire `pendingHookSuccessCode` if any blocking hook captured one — **hook success takes priority over transition handler success**.
- Otherwise fire `transitionNextCode` from the transition handler if set.
- Otherwise return `RelayResult.Ok(currentState)` — no auto-advance.

### Key invariants
- **Failure always fires immediately** — no further hooks run after a failure event is resolved.
- **Success always waits** — fired only after all hooks complete.
- **Non-blocking hooks cannot drive auto-advance** — complete codes on non-blocking hooks are silently ignored.
- **Rule-level complete codes do NOT cascade to hooks** — each hook's complete codes come only from its own `complete` block.
- **State advances before hooks run** — `ctx.CurrentState` is set to `ToState` before hook handlers are called. On blocking hook failure with no path, it is rolled back to `FromState`.

---

## 5. Validation Rules

### Structural errors (reject registration)

| Code | Condition |
|---|---|
| `NO_INITIAL_STATE` | No state marked `is_initial` |
| `MULTIPLE_INITIAL_STATES` | More than one state marked `is_initial` |
| `UNKNOWN_FROM_STATE` | Transition `from` references a state not in the states list |
| `UNKNOWN_TO_STATE` | Transition `to` references a state not in the states list |
| `DUPLICATE_EVENT_CODE` | Same event code used twice from the same state |
| `TERMINAL_HAS_TRANSITIONS` | A terminal state has outgoing transitions |
| `STUCK_STATE` | Non-terminal state has no outgoing transitions |
| `CIRCULAR_ONLY` | A state's only outgoing transition is a self-loop — infinite loop guaranteed |
| `AMBIGUOUS_ORDER` | Multiple blocking hooks at the same order define complete codes |

### Structural warnings (allowed, informational)

| Code | Condition |
|---|---|
| `UNREACHABLE_STATE` | State has no incoming transitions and is not the initial state |
| `SELF_LOOP_WITH_EXIT` | State has a self-loop but also has exit transitions — likely intentional |

### Policy warnings (allowed, informational)

| Code | Condition |
|---|---|
| `POLICY_UNKNOWN_STATE` | Policy rule targets a state not in the definition — dead rule, never fires |
| `NON_BLOCKING_WITH_COMPLETE` | Non-blocking hook defines complete codes — ignored at runtime |
| `UNREACHABLE_HOOK` | Hook after a full-terminator (blocking hook with both success + failure codes) |
| `NO_FAILURE_PATH` | Blocking hook has no failure code and transition has no failure code — code-side Tier 2/3 must handle |

---

## 6. Pending Engine Audit

Once relay semantics are confirmed stable, audit `InstanceOrchestrator` and `MonitorOrchestrator` to verify the engine honours the same rules:

- `via` matching — hooks only applied when state entered via the specified event code
- Non-blocking hooks ignore complete codes
- Failure fires immediately, success waits for all hooks
- Param inheritance (hook-own first, rule fallback)

Reference: `RELAY_EXECUTION_CONTRACT.md`
