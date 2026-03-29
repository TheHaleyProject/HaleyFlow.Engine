# WorkflowRelay — Execution Contract

Rules governing how `WorkflowRelay.NextAsync` executes transition handlers and hooks.

---

## Execution Order

1. Transition handler runs (in `FromState` context)
2. `ctx.CurrentState` advances to `ToState`
3. Hooks run in `OrderSeq` order
4. After all hooks complete — fire pending success event (if any)

---

## Transition Handler Rules

| Outcome | Action |
|---|---|
| Handler returns `false` (failure) | Skip ALL hooks. Fire failure event immediately if `CompleteFailureCode` is set. |
| Handler returns `true` (success) | Run all hooks in order. Fire success event AFTER all hooks complete. |

---

## Hook Rules

| Outcome | Has success code | Has failure code | Action |
|---|---|---|---|
| Blocking hook **succeeds** | yes | — | **Terminate immediately** — skip remaining hooks, fire success code now. |
| Blocking hook **succeeds** | no  | — | Continue to next hook. |
| Blocking hook **fails**    | —   | yes | **Terminate immediately** — skip remaining hooks, fire failure code now. |
| Blocking hook **fails**    | —   | no  | Roll back `ctx.CurrentState` to `FromState`. Return `Blocked`. |
| Non-blocking hook (any result) | — | — | Continue to next hook. Complete codes on non-blocking hooks are ignored entirely. |

---

## Key Invariants

- **Both success and failure terminate immediately** — any complete code on a blocking hook (success or failure) fires immediately and skips all remaining hooks.
- **No complete code = continue** — a blocking hook with no complete codes only stops the chain on failure (rolls back + blocked). On success it passes through to the next hook.
- **Transition success waits for hooks** — the transition handler's success code is only fired after ALL hooks complete with no termination. If any hook terminates first, the transition code is discarded.
- **Transition failure skips all hooks** — if the transition handler returns failure, hooks never run.
- **Non-blocking hooks cannot drive auto-advance** — `CompleteSuccessCode`/`CompleteFailureCode` on a non-blocking hook are ignored at runtime even if present in JSON.
- **Rule-level complete codes do NOT cascade to hooks** — each hook's codes come only from its own `complete` block in the policy JSON.
- **Params do NOT cascade from rule to hooks automatically in code** — resolved at parse time: hook-own params first, parent rule params as fallback if hook defines none.
