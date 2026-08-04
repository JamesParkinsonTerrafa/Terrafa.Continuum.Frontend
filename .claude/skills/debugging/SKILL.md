---
name: debugging
description: How to diagnose a bug in this repo — reproduce before claiming a fix, the ground-truth sources to read, and the ordering traps in the singleton event graph. Use when a symptom is reported from a screenshot or a user report rather than a failing test.
---

# Debugging this codebase

## The rule that matters

**A bug reported from a screenshot is not fixed until you have reproduced it and then
un-reproduced it yourself.**

Reading the code until you have a plausible cause is the *start* of the work. This repo has
several layers between a fetch and a pixel — HTTP catalogue → `ReadingStore` → `Workspace` →
`NetworkGraph` → `TableCatalog`/`FigureCatalog` → tile — and a symptom at the end is consistent
with a fault in any of them. Finding *a* real bug in one layer is not finding *the* bug.

The failure mode to avoid: fix the first plausible cause, report it as resolved, watch the same
screenshot come back. That costs a round trip and a great deal of trust. It has happened here more
than once.

Build the observation harness first. It is almost always cheaper than the round trips it saves.

## Reproduce headlessly

`--probe` runs the live signed-in read path with no window, restoring the session from the keychain
so no credential is handled directly:

```bash
dotnet run --project src/Terrafa.Continuum.Frontend -- --probe synthetic_dev.contract_requirements
```

It prints per-leaf cell and history counts, then the restored mounts, links and every derived table
with its note. That last line is what a grid tile draws — if it says `empty`, the tile will too.
Extend it rather than guessing; it is the fastest way to see across the whole pipeline at once.

`--snapshot <dir>` renders every screen to PNG using stub data. Use it to verify UI and layout
without auth. It exercises `SelectEvaluation` and the tile rendering, but **not** the HTTP
catalogue.

Never use `screencapture` or System Events to look at the app — use `--snapshot`.

## Ground truth, in order of usefulness

Client-side reasoning is where the guessing happens. Go to the server.

- **DynamoDB** — what the account actually holds. This is authoritative for "my work disappeared":
  `aws dynamodb get-item --table-name terrafa-user-state --key '{"userSub":{"S":"<sub>"},"kind":{"S":"workspace"}}' --region eu-north-1 --query 'Item.payload.S' --output text`
  Kinds: `settings`, `functions`, `workspace`, `network`, `dashboard`.
- **API Gateway access logs** — whether a call was made, and what it returned:
  log group `/aws/apigateway/continuum-core-datafeed-prod`, filter on the table name.
  A 400 here means the client built a bad query; a 200 means the data reached the browser and the
  fault is downstream.
- **Athena direct** — what the table really contains. Workgroup `primary` has no default output
  location, so always pass
  `--result-configuration OutputLocation=s3://continuum-core-datafeed-prod-athena-results-514421696790/adhoc/`.
  `timestamp` is reserved — quote it. Poll `get-query-execution` in a bare loop; foreground `sleep`
  is blocked.

Counting duplicates in a key column, or checking a join's real row count, settles in one query what
an hour of reading `SelectEvaluation` will not.

## The ordering trap

`Workspace`, `ReadingStore`, `NetworkGraph`, `FigureCatalog`, `TableCatalog` and `Dashboard` are
singletons cross-wired by events. **Several bugs have been this shape.** Much of the old hazard was
removed on 2026-08-04 — read `refactor-plan.md` for what changed and why — but the shape is still
worth knowing.

**What is settled now.** `Session.TransitionAsync` is the single owner of becoming somebody: it
resets the singletons, applies the documents (settings → functions → workspace → network →
dashboard) and reads the values, in one method, under one cancellation token. It is idempotent, so
running it twice from the same identity lands on the same state, and startup order stops mattering —
a token restored before or after the app starts converges either way. Nothing else may reset or load
those singletons.

`AuthSession.Changed` now fires **only when the signed-in identity changes**. A renewal is invisible.
The old trap here — a routine token refresh read as a sign-in, resetting the workspace to the demo
seed and then saving the seed over the operator's real work — cannot happen by construction.

**What still needs care.** When something is stale, empty, or reset, ask:

1. **Does it recompute when its inputs land?** Structure arriving before values is normal here.
   `NetworkGraph` subscribes to `ReadingStore.Changed` for exactly this reason — a committed table
   evaluated against no cells stayed empty forever until it did.
2. **Is the world half-rearranged?** `PruneUnmounted` deletes measure cards no value answers for,
   and during a load the store fills one dataset at a time. Anything that resets the singletons and
   then refills them must hold `NetworkGraph.Instance.Suspend()` for the *whole* operation, or it
   deletes the cards belonging to whatever has not been read yet. This is a real bug that shipped.
3. **Did the value come through the one door?** `ReadingLoader.ReadAsync` is the only place a value
   enters the app — fetch, `ReadingStore.Write`, `Workspace.SetAxis`. If something has a value the
   store does not, it did not come from there.

Before adding a handler that mutates shared state, write down which events reach it and what else is
subscribed. Before assuming a value is wrong, check whether anything recomputes it.

`UserStateSync` guards: `applying` suppresses echoes of a load; `saving` is false from the moment a
session begins until its documents are in place, and a successful `LoadAllAsync` is the only thing
that turns it back on.

## Running things

- Browser head is pinned to port 8791 (`runtimeconfig.template.json`) so the origin — and therefore
  the localStorage token — is stable across restarts. Restart it and wait for a real 200 with an
  `until curl` loop before concluding anything. Testing against a stale build wastes whole cycles.
- Build and restart after **every** change set. Debugging a binary that does not contain your edits
  is the most expensive mistake available here.
- The test suite runs sequentially (`TestParallelism.cs`). It used to run collections in parallel
  while they all mutated the same singletons, which is what made "which tests fail changes with
  which tests run" true. If that file goes, the flakiness comes back. `PointerHintTests
  .Catalog_TargetsExistInTheTransferFunctionView` still fails in every configuration — its hints
  target controls no view defines yet, and it is unrelated to anything recent.

## Reporting

Say what is verified and what is not, separately and plainly. "The read path is fixed and I have
not confirmed the tile renders" is a useful sentence. "Fixed" when you have only built successfully
is not.

## Notes for James

These are asks, not complaints — each one measurably shortens a bug hunt.

- **Please commit more often.** An uncommitted tree costs real capability: no stash, no bisect, and
  no way to tell a fresh regression from a pre-existing failure. Checkpoint commits turn "did I
  break this?" into a five-second question.
- **Start a fresh session for a bug hunt.** A long session that has already covered three unrelated
  features gets compacted, and reasoning about a five-layer pipeline through a lossy summary is
  much worse than reasoning about it directly. A tight brief in a clean session is sharper.
- **Say when to spend.** Pressure to be efficient is fair, but it reads as "go faster", which in
  practice means "guess more" — exactly wrong when the right move is to stop and build a harness.
  If depth is wanted, say so and it will be taken literally.
- **One thread at a time where you can.** New symptoms arriving mid-fix fragment the work. Not
  always avoidable when something is urgent — just worth knowing the cost.
