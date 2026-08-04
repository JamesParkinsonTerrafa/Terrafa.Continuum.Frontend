# Refactor plan

Baseline at the time of writing: solution builds clean; `dotnet test --filter Category!=Live`
gives 166 passed, 1 failed (`PointerHintTests.Catalog_TargetsExistInTheTransferFunctionView`,
a pre-existing failure from in-progress hint work — see "Not in scope").

---

## 1. Diagnosis

### 1.1 The root cause of the auth/fetch races

There is no single owner of "what is loaded". Seven singletons — `AuthSession`, `Workspace`,
`ReadingStore`, `NetworkGraph`, `FigureCatalog`, `TableCatalog`, `Dashboard` — are cross-wired by
events, and correctness depends on an apply order that nothing enforces. The repo's own debugging
notes say two separate bugs in one session were this shape.

The specific mechanism: **`AuthSession.Changed` means five different things**, and five independent
subscribers each re-derive which one it was.

| Subscriber | Subscribes in | What it does |
|---|---|---|
| `UserStateSync` | `Program.Main`, before Avalonia starts | loads all documents, then reads values |
| `SessionDatasetCatalog` | `App.OnFrameworkInitializationCompleted` | drops + disposes the live catalogue |
| `MainView` | `OnAttachedToVisualTree` | resets Workspace / Network / Dashboard to seed |
| `ExportTable` | first touch of `ExportTable.Instance` — i.e. when the CSV screen is first opened | clears the built parquet document |
| `SettingsFlyout` | on attach | redraws the account row |

Subscriber order is therefore a function of *which screens the user has visited*. That is the race.

**Race A — restore vs. reset.** `Program.Main` fires `TryRestoreAsync()` and starts Avalonia
concurrently. If the restore lands after `MainView` attaches, `UserStateSync` (subscribed first)
begins `LoadAllAsync` and awaits the store over HTTP; `MainView.OnSessionChanged` then runs
synchronously and calls `Workspace.Reset` / `NetworkGraph.Reset` / `Dashboard.Reset` *underneath the
in-flight load*. `ApplyWorkspaceAsync`'s `KeepExisting` fallback then captures whichever tree
happens to be standing. Sometimes right, sometimes not, never deterministic.

**Race B — catalogue disposed under in-flight reads.**
`SessionDatasetCatalog.OnSessionChanged` disposes the live `HttpDatasetCatalog`, which disposes its
`HttpClient`. Any request already in flight throws `ObjectDisposedException`, which `ReadingLoader`
catches and silently drops. This is precisely "the frontend rejects data in the guise of
reliability".

**Race C — `HttpDatasetCatalog.LoadCatalogueAsync` clears the route map.**

```csharp
lock (gate) { routes.Clear(); foreach (var (name, route) in routing) routes[name] = route; }
```

A concurrent `RouteAsync` landing between the clear and the fill sees an empty map and throws
"'X' is not in the catalogue" — a real read failing for no reason. `Warnings` is likewise written
from the fetch and read from the UI thread with no lock.

**Race D — per-screen, ad-hoc request guarding.** `DataSourcesView` guards continuations by
comparing `openDataset` / `xAxis` string fields. It is correct for the two cases written, but it is
a hand-rolled re-implementation of cancellation, and `LoadCatalogue` is an `async void` with no
guard at all — a fast sign-out/sign-in pair can let the older call win.

**Race E — silent failure.** Twelve `catch (Exception) { }` blocks on the read and restore paths.
A failure disappears, the screen shows seed or stale state, and nothing says why.

### 1.2 Colocality

- **The read pipeline is spread over six files** with no one place that describes it:
  `DataSourcesView.LoadSeries`, `ReadingLoader.LoadAsync`, `ReadingLoader.LoadDatasetAsync`,
  `UserStateSync.LoadAllAsync`, `HttpDatasetCatalog.GetSeriesAsync`, `ReadingStore.Write`.
  Three of those repeat the identical triple
  `GetSeriesAsync → ReadingStore.Write → Workspace.SetAxis` — over the "twice" threshold.
- **Session handling is spread over five files**, each re-deriving what the event meant.
- **`HttpDatasetCatalog` is 842 lines doing four jobs**: transport, routing, Hive→tree schema
  construction, and σ binding. Roughly 450 of those lines are pure, stateless transformation with
  no HTTP in them, sitting in a class whose name says "Http".

### 1.3 Single source of truth

- The x axis for a dataset is held in four places — `DatasetSchema.XAxis`, `MountedSubtree.XAxis`,
  `DataSourcesView.xAxis`, and the axis argument in flight — and reconciled by hand.
- `Workspace.ReadingAt(path)` is `ReadingStore.Find(path) ?? FindNode(path)?.Reading`, and
  `DataTreeNode.Reading` is *itself* `ReadingStore.Find(Path) ?? declared`. Two overlapping fallback
  chains answering the same question.
- `DataSourcesView` `switch`es on the concrete catalogue type twice (`IsLive`, `Warnings`) because
  the interface does not carry either.

### 1.4 Naming that will actively hurt as the feed arrives

`IDataFeed` / `StaticDataFeed` / `DataSnapshot` have nothing to do with the data feed. They are demo
chrome for the decorative screens (positions, leaderboard, calibration, event log). `SnapshotChanged`
is an event nobody raises. The name needs to be free for the real thing.

---

## 2. Intended changes

Ordered so each lands on a green build.

### C1 — `Measure` becomes a `record`

`Models/Measure.cs`: `sealed class` → `sealed record`. Then rewrite the five hand-written copy sites
as `with` expressions and delete two of them outright:

- delete `MeasureNumerics.With` → `source with { History = h, IsSigmaCarrier = true }`
- delete `HttpDatasetCatalog.AsCarrier` → `source with { IsSigmaCarrier = true }`
- `MeasureNumerics.BindSigmaLeaves`, `MeasureNumerics.Hydrate`,
  `HttpDatasetCatalog.BindSiblingSigma` → `with` expressions

**Why:** this deletes the house rule "any new `Measure` field must be threaded through ALL of
[five sites]" — a rule that exists only because the type is hand-copied. Roughly 90 lines go.

### C2 — Extract `DatasetSchemaBuilder`

New `Services/DatasetSchemaBuilder.cs` (`internal static`) takes the pure functions out of
`HttpDatasetCatalog`: `BuildSchema`, `Append`, `MemberPartition`, `Tail`, `RowsPerPoint`,
`BindSiblingSigma`, `PairedSigma`, `Narrow`, `Format`, `Humanise`, `Coverage`.

`HttpDatasetCatalog` keeps transport, routing and caching, and drops to roughly 350 lines.

**Why:** colocality. Turning an Athena response into a tree is one job; speaking HTTP is another.
The `SeriesTests` are really tests of the former.

### C3 — `IDatasetCatalog` carries what its callers ask of it

```csharp
public sealed record DatasetQuery(
    string Dataset,
    string Axis,
    IReadOnlyCollection<string>? Paths = null);

public interface IDatasetCatalog
{
    bool IsLive { get; }
    IReadOnlyList<string> Warnings { get; }

    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync(CancellationToken ct = default);
    Task<DatasetSchema> GetSchemaAsync(string dataset, CancellationToken ct = default);
    Task<DatasetSchema> GetSeriesAsync(DatasetQuery query, CancellationToken ct = default);
}
```

- `IsLive` / `Warnings` on the interface delete both concrete-type `switch`es from
  `DataSourcesView`. This *narrows* the coupling: a view stops knowing the implementations exist.
- `DatasetQuery` replaces the loose `(dataset, xAxis, wanted)` triple and gives the date filter the
  user has planned (`Since` / `Until`) a home to be added to without touching every call site.
- Cancellation: cached tasks are created with `CancellationToken.None` and awaited per-caller via
  `task.WaitAsync(ct)`, so one caller cancelling cannot poison a shared fetch.

### C4 — One read path

`Services/ReadingLoader.cs` becomes the only place a value enters the app:

```csharp
public static async Task<ReadOutcome> ReadAsync(
    IDatasetCatalog catalog, DatasetQuery query, CancellationToken ct)
{
    var series = await catalog.GetSeriesAsync(query, ct);
    ct.ThrowIfCancellationRequested();
    ReadingStore.Instance.Write(series);
    Workspace.Instance.SetAxis(query.Dataset, series.XAxis);
    return ReadOutcome.Ok(series);
}
```

The three duplicated call sites (`ReadingLoader.LoadAsync` loop body,
`ReadingLoader.LoadDatasetAsync`, `DataSourcesView.LoadSeries`) collapse onto it.
`ReadingStore.Write` stays the single sink — which is exactly the seam a push feed writes to later.

### C5 — `AuthSession.Changed` fires on identity change only

`RaiseChanged` records the last-raised identity and suppresses a repeat. A token renewal becomes
invisible; a renewal failure calls `SignOut`, which is a genuine identity change.

**Why:** trap #3 in the repo's own debugging notes is "`AuthSession.Changed` fires for sign-in,
sign-out **and** token restore and renewal. Treating a renewal as a sign-in reset the workspace to
the demo seed and then saved the seed over the operator's real work." Making the event mean one
thing removes that class of bug by construction, and deletes `MainView.sessionIdentity` and its
comparison.

### C6 — `Session`: the state machine

New `Services/Session.cs`. One owner, one sequence, one cancellation token:

```
SignedOut ──sign in──▶ Loading ──documents + values──▶ Ready
    ▲                     │                              │
    └──────sign out───────┴──────────sign out────────────┘
                          └──▶ Failed (reported, not swallowed)
```

```csharp
public enum SessionPhase { SignedOut, Loading, Ready, Failed }
```

One `TransitionAsync`, run under a `CancellationTokenSource` that a newer transition cancels:

1. cancel any in-flight transition, take a fresh token
2. `Phase = Loading`, announce
3. reset `Workspace` / `NetworkGraph` / `Dashboard` to the seed — **always, exactly once, before
   any document is applied**
4. signed out ⇒ `Phase = SignedOut`, announce, done
5. apply documents (settings → functions → workspace → network → dashboard)
6. read values through C4's single reader, collecting per-dataset failures
7. `Phase = Ready`, announce

Every `await` checks the token; a superseded transition returns without touching shared state.

This is the change the user's "the user path should form a state machine" asks for. It makes the
transition **idempotent**: running it twice from the same auth state produces the same result,
because the reset is inside it rather than racing beside it.

Consequently:
- `MainView` stops resetting anything and follows `Session.Changed` to rebuild screens.
- `UserStateSync` keeps the *save* half (dirty tracking, debounce) and loses the *load trigger*;
  `Session` calls it. Its `loaded` flag becomes "saving is allowed", set by `Session`.
- `DataSourcesView` follows `Session.Changed` instead of being manually poked by its own buttons.
- `Program` / browser `Program` call `Session.Instance.Start()` instead of wiring three things.
- `SnapshotRunner` simply never calls `Start()`, preserving deterministic screenshots.

### C7 — Shared `HttpClient`; catalogue swap stops disposing under load

`HttpDatasetCatalog` no longer owns an injected `HttpClient` and drops `IDisposable`; the app uses
one long-lived client (which is the correct .NET practice regardless — one client per sign-in is a
socket-exhaustion smell). `SessionDatasetCatalog` swaps the reference and drops the old caches.

**Fixes race B outright**: there is no longer a disposed client for an in-flight read to hit.

### C8 — Failures stop vanishing

- `ReadingLoader` returns `IReadOnlyList<ReadFailure>` rather than swallowing per-dataset errors;
  `Session` holds them and exposes them as `Session.Warnings`.
- `DataSourcesView` and the status line show "N dataset(s) could not be read", alongside the
  catalogue warnings already shown.
- `UserStateSync.ReadAsync` distinguishes "no document" (null — genuinely fine) from "store
  unreachable" (recorded).
- Fix race C: build the new route map and swap it atomically; write `Warnings` under the lock.

### C9 — Free the name `DataFeed` for the data feed

- delete `IDataFeed` (one implementation, and `SnapshotChanged` is never raised)
- delete `StaticDataFeed`
- `DataSnapshot` + `DemoData` → `DemoContent` with a `Create()` factory
- the four views that take the blob and never read it — `DashboardView`, `TransferFunctionView`,
  `CsvExportView`, `DataSourcesView` — drop the parameter

---

## 3. Impact assessment, change by change

| # | Files touched | Effect elsewhere | Risk |
|---|---|---|---|
| C1 | `Measure.cs`, `MeasureNumerics.cs`, `HttpDatasetCatalog.cs` | none — no consumer depends on `Measure` being a class | **Low.** `record` adds value equality, but its list members compare by reference and nothing compares `Measure`s. `TableCatalog.Same` / `FigureCatalog.Same` do their own element-wise comparison and do not touch `Measure`. |
| C2 | `HttpDatasetCatalog.cs`, new `DatasetSchemaBuilder.cs` | none — private→internal move inside one assembly | **Low.** The builder must stay `internal` because `DatasetSchemaResponse` / `DatasetColumn` are internal. Tests exercise it through `HttpDatasetCatalog` with a fake handler, as they already do, so no test churn. |
| C3 | `IDatasetCatalog.cs`, `HttpDatasetCatalog`, `StubDatasetCatalog`, `SessionDatasetCatalog`, `DataSourcesView`, `ReadingLoader`, `DataProbe`, `SeriesTests`, `RestoreFallbackTests` | `DataSourcesView` loses two concrete-type `switch`es | **Medium (churn, not logic).** ~14 `GetSeriesAsync` call sites in `SeriesTests` change shape; `RestoreFallbackTests.LiveLikeCatalog` gains two members. Mechanical. The one real design point is the cancellation/caching interaction, handled by `WaitAsync`. |
| C4 | `ReadingLoader.cs`, `DataSourcesView`, `UserStateSync`, `Session` | `ReadingStore.Write` becomes provably the only sink | **Low–medium.** `DataSourcesView` keeps its own preview guard (the screen owns its preview; the reader must not), so the split is: reader owns store+axis, screen owns what it draws. `DataSourcesView.xAxis` is retained but re-described as *the pending pick*; the confirmed axis is read back from the schema. |
| C5 | `AuthSession.cs`, `MainView`, `SessionDatasetCatalog`, `ExportTable`, `SettingsFlyout`, `UserStateSync` | **`ExportTable` improves**: a token renewal used to bin the built parquet document; it no longer does | **Medium.** This changes an event's contract, so every subscriber must be checked — all five are, above. `DurableSessionTests.Restore_SignsBackInFromTheStoredCredential` asserts exactly one `Changed`; a restore is still one identity change, so it stays green. |
| C6 | new `Session.cs`; `MainView`, `UserStateSync`, `DataSourcesView`, both `Program`s, `DataProbe` | the whole reset/load/read ordering moves into one method | **High — the substantive change.** Mitigations: `UserStateSync.LoadAllAsync` stays public so the four `UserStateTests.Sync_*` tests keep working unchanged; `Session` takes its catalogue and store as settable dependencies (the pattern `UserStateSync` already uses) so it is drivable from tests; `SnapshotRunner` is unaffected because it never starts a session. Visible consequence: signing in flashes seed state before restored state — that already happens today, and it is now honest rather than accidental. |
| C7 | `HttpDatasetCatalog`, `SessionDatasetCatalog`, `SeriesTests` | fixes race B | **Low.** `SeriesTests` use `using var catalog = new HttpDatasetCatalog(transport.Client)` where `FakeDataFeed` already disposes the client — the `using` becomes unnecessary and is removed. |
| C8 | `ReadingLoader`, `Session`, `DataSourcesView`, `UserStateSync`, `HttpDatasetCatalog` | new information appears on screen that was previously discarded | **Low.** Additive. The route-map swap (race C) is a two-line change with no caller impact. |
| C9 | ~40 sites, mostly `SnapshotRunner` | none — pure rename plus dropping unused parameters | **Low but noisy.** No behaviour change. Kept last so it cannot obscure a real regression; if it fights, it is the one item safe to drop. |

### Cross-cutting checks

- **`NetworkGraph` subscribes to `ReadingStore.Changed`** so committed tables recompute when values
  land after structure. C4 and C6 keep every write going through `ReadingStore.Write`, so that
  recompute still fires exactly once per read. Verified as a constraint, not changed.
- **Load order settings → functions → workspace → network → dashboard → values** is a real
  dependency (the network's stages name saved functions; its measures name mounted leaves). C6
  moves it but does not reorder it.
- **`ApplyWorkspaceAsync`'s `KeepExisting` fallback** depends on the demo mount being present when
  the workspace document is applied. C6 guarantees this by making the reset unconditionally precede
  the apply — today it is guaranteed only by subscriber order. `RestoreFallbackTests` is the test
  that pins this and must stay green.
- **Snapshot determinism** rests on `Program` skipping auth for `--snapshot`. C6 preserves it.

---

## 4. Not in scope

- **`PointerHintTests.Catalog_TargetsExistInTheTransferFunctionView`.** `HintCatalog` holds seven
  hints — `DataSources`, `DataTree`, `TransferFunction`, `Network`, `DashBoard`, `Map`, `CSVExport` —
  all filed under `TransferFunctionScreen` and all targeting controls that view does not define.
  There is a `//TO POINT TO CORRECT PAGE/ LOCATION` marker beside them. These read as one hint per
  screen awaiting routing, which is a product decision, not a refactor. Left untouched; the test
  stays red and correctly reports it.
- **The push feed itself.** `DatasetQuery` is shaped for the subscribe-with-filter model and
  `ReadingStore.Write` is the sink a push would call, but no fake subscription API is built over an
  HTTP backend that cannot push.
- Chart rendering, theming, bubble physics, the parquet/CSV export path.

---

## 5. Outcome

All of C1–C9 landed. Solution builds clean; `dotnet test --filter Category!=Live` gives
**169 passed, 1 failed** — the same pre-existing `PointerHintTests` failure as the baseline, which
was 159 passed / 1 failed. `--snapshot` renders all 67 screens, exit 0, with every probe reporting
what it did before. Net **−418 lines** across 37 files.

### Two bugs found while implementing, not in the original diagnosis

**A restored network spanning two datasets silently lost the second.** `NetworkGraph.PruneUnmounted`
drops measure cards that no value answers for, and it is subscribed to `Workspace.Changed`. A
restore reads datasets one at a time, and each read records its dataset's axis — which *is* a
workspace change. So the first read pruned every card belonging to a dataset whose read had not
happened yet. It only bit cards pointing at datasets referenced but not mounted on this machine —
the "same account on a second machine" case that `ReadingLoader.Referenced` exists to serve — which
is why it presented as intermittent.

Fixed by `NetworkGraph.Suspend()`, a scope `Session` holds across the whole transition: nothing
prunes, recomputes or announces until the last scope closes. "No value at this path" only means
something once everything that is going to be read has been.

Pinned by `SessionTests.ANetworkNodeOnADatasetReadLast_SurvivesTheLoad`. That test was verified to
*fail* against the unfixed code before being kept — the first version of it passed either way,
because its fixture returned an axis from the structure-only schema, which the real catalogue never
does. Worth recording: a fixture that is wrong in the same direction as the bug hides it.

**The test suite was racing itself.** xUnit runs separate collections in parallel, and both the
`workspace` and `function library` collections mutate the same singletons — one swapping the mounted
subtrees while the other indexed `Subtrees[0]`. This is the mechanism behind the known property that
"which tests fail changes with which tests run". Fixed with
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`; the suite runs in about half a
second, so there is nothing to buy back.

### Deviations from the plan

- **`UserStateSync.ResumeSaving` was dropped.** Saving turns back on at the end of a successful
  `LoadAllAsync` instead. The invariant — "the documents are in place, so an edit is the operator's
  own" — belongs with the thing that establishes it, and it kept `Session` from having to remember
  a second call. A load that is cancelled or throws leaves saving off, which is the safe direction.
- **`Session` owns the catalogue** (`Session.Instance.Catalog`) rather than `App` creating it and
  handing it around. "The catalogue this app reads through" is a fact about the session, and having
  the screens, the restore and the probe each reach for their own is how they came to disagree.
- **`MainView.WarmCatalogue` was deleted rather than moved.** The restore's first `GetSchemaAsync`
  routes through `GetAvailableDatasetsAsync` already, and that task is shared — the prefetch was a
  second name for something that was going to happen anyway.

## 6. The row window — `DatasetQuery.MaxRows`

Added after the main refactor, from a question about whether the 240-row cap is a future failure.
It was: not because of the number, but because nothing said the number existed.

### What was actually happening

- The service emits `ORDER BY … LIMIT MaxRows + 1` with `AthenaQueryOptions.MaxRows = 1000`, and the
  `+ 1` is how it detects truncation. It returns `Truncated` on every response.
- The client received up to 1000 rows and kept the newest 240. **760 rows fetched and discarded on
  every read.**
- `DatasetDataResponse.Truncated` was read by nobody. The service was saying "there is more" and the
  client dropped it.
- Worst case, in `SelectEvaluation`: every row count in the join note is computed from
  `Measure.Cells`, which is already windowed. A join across the newest 240 rows of a 10,000-row
  table reported `240 row(s) · inner join on 2 key(s) · 240/240 base rows matched` — total success,
  over a window, with nothing to say a window existed. `docs/select-join-boolean-plan.md` specified
  `window 240` on that card; it never landed.

### What a row cap is and is not

It bounds **transfer and memory**. It does **not** bound scan, and therefore not cost: Athena bills
on bytes scanned, and a `LIMIT` behind an `ORDER BY` still reads every row to determine which are
the top *n*. The levers that do reduce scan are column projection (already done — Parquet) and
partition pruning (a date filter, which the query has no way to express yet). Recorded here because
"add a row cap so dev/test doesn't run up scan" is a reasonable-sounding assumption that does not
hold.

### Changed

- `DatasetQuery.MaxRows`, defaulting to `DataFeedOptions.SeriesRows` (240) — unchanged behaviour
  until somebody asks for more.
- **It is part of the cache key.** `DatasetQuery.CacheKey` replaces the old
  `(dataset, axis, projection)` tuple. Without it the cache hands a caller that asked for 5,000 rows
  the 240 a previous caller settled for, and reports it as complete — the same silent-wrong-data
  class the refactor exists to remove. Pinned by
  `SeriesTests.AskingForMoreRows_IsADifferentReadRatherThanACacheHit`, verified to fail without it.
- `DatasetSchema.Truncated` / `.WindowRows` — set from the service's own flag *or* from more rows
  arriving than the window kept. Either means the same thing downstream.
- `ReadingStore.WindowOf(dataset)` — the window recorded beside the values, because everything
  downstream works from cells rather than from the schema that produced them.
- The join note now says `windowed: db.table (240 rows) — more rows exist than were read`.
- The DATA SOURCES axis row now distinguishes three states: unordered, `all N rows read`, or
  `newest N rows read — the table holds more` (amber).

### The service half — landed in `Terrafa.Continuum.Core.DataFeed`

`GET /api/datasets/{db}/{table}/data` now takes `maxRows`:

- `DatasetsController.GetData` — `[FromQuery] int? maxRows = null`
- `IDatasetReader.GetDataAsync` carries it
- `AthenaDatasetReader` — `Math.Clamp(asked, 1, _queryOptions.MaxRows)`, so **configuration is the
  ceiling, never the floor**: a caller narrows a read and can never widen one past what the
  deployment allows. Nought or negative reads as one row rather than emitting SQL Athena rejects —
  a caller wanting no rows would not call.
- `BuildSql` and the truncation check use that cap, so the `+ 1` that makes truncation detectable
  travels with the caller's window, and the cap still lands *after* the `ORDER BY`.

Service suite 178 passed (was 172), 6 added.

**Deploy order does not matter.** The service uses plain `AddControllers()` with no strict query
binding, so a deployment predating the parameter ignores it and applies its own cap, which the
client then windows locally exactly as before. The frontend sends `&maxRows=` unconditionally.

## 7. Still recommended to the DataFeed service

**A time filter on the read** — `since` / `until` on `GET /api/datasets/{db}/{table}/data`. If the
tables are partitioned by date this is the *only* change discussed here that reduces bytes scanned,
and therefore the only one that reduces what a query costs. It is also the same parameter a
subscription's initial backfill will need. `DatasetQuery` is shaped to carry it, so adding it
touches the record and the URL builder and no call site.
