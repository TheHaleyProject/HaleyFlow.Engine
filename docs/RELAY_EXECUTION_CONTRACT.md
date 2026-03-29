# WorkflowRelay — Execution Contract

Rules governing how `WorkflowRelay.NextAsync` executes transition handlers and hooks.

---

## Hook Types

| Type | JSON | Meaning |
|------|------|---------|
| **Gate** | `"type": "gate"` | Must succeed before state machine can advance. Failure terminates immediately. |
| **Effect** | `"type": "effect"` | Side-effect hook. Always runs. Result is completely ignored. |

Backward compat: `"blocking": true` → Gate, `"blocking": false` → Effect.

---

## Execution Order

1. Transition handler runs (in `FromState` context)
2. `ctx.CurrentState` advances to `ToState`
3. Hooks run in `OrderSeq` order (same order = parallel; lower fires first)
4. After all hooks complete with no gate termination — fire pending success event (if any)

---

## Transition Handler Rules

| Outcome | Action |
|---------|--------|
| Handler returns `false` (failure) | Skip ALL hooks. Fire failure event immediately if `CompleteFailureCode` is set. |
| Handler returns `true` (success) | Run all hooks in order. Fire success event AFTER all hooks complete. |

---

## Hook Execution Contract

| Hook | Outcome | Has success code | Has failure code | Action |
|------|---------|-----------------|-----------------|--------|
| **Gate** | succeeds | yes | — | Remember success code. Skip remaining gate hooks. Run all remaining **effect** hooks in order. Then fire success code. |
| **Gate** | succeeds | no  | — | Continue to next hook (gate or effect). |
| **Gate** | fails    | —   | yes | Fire failure code immediately. Skip ALL remaining hooks (gate and effect). |
| **Gate** | fails    | —   | no  | Roll back `ctx.CurrentState` to `FromState`. Return `Blocked`. |
| **Effect** | any | — | — | Run handler, ignore result, always continue. Complete codes on effect hooks are ignored entirely. |

---

## Key Invariants

- **Gate failure terminates immediately** — any gate hook failure skips everything remaining.
- **Gate success with code drains effects** — effects after the gate still run; success code fires only after they complete.
- **Gate success without code = pass-through** — chain continues normally to the next hook.
- **Effect hooks never terminate the chain** — result and complete codes are ignored.
- **Transition success waits for all hooks** — the transition handler's success code fires after ALL hooks complete with no gate termination. If a gate terminates first, the transition code is discarded.
- **Transition failure skips all hooks** — if the transition handler returns failure, hooks never run.
- **Params do not cascade from rule to hooks automatically** — resolved at parse time: hook-own params first, parent rule params as fallback if hook defines none.
