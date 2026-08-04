# SELECT, joins, and σ-level booleans — implementation plan

Goal: a dashboard table like the parcels screenshot — columns from `synthetic_dev.parcels`
joined to `synthetic_dev.contract_requirements` on `productid` + `contractid`, with a computed
boolean column (`condition_at_lift` vs `required_value`) rendered as green/red pills.

Agreed with James over two design rounds (2026-08-03). This file is the durable record; the
conversation may be compacted away.

## Decided semantics

### σ-level booleans ("absolute determination")

A comparison `a > b` produces:

- **Determination** — sign of the margin `m = a − b` → true/false. What the cell shows.
- **σ level** — `z = m / √(σa² + σb²)`, "the degree greater, in σ units". Rendered
  `true · 2.3σ` when variance mode is on, plain `true` when off. Computed per row where the
  inputs carry per-row σ.
- **Belief** — `Φ(z)` when a probability is wanted.
- **Vacuous regime** — either input's σ is NaN ⇒ NaN means *unknown* variance, not zero
  (house rule, see TransferMath.Combine). The determination still states, but carries no σ
  level: rendered `true · no σ` (reuses the StateNote vocabulary).

Dempster–Shafer grounding: determination-with-unknown-σ is the vacuous mass assignment
(belief 0, plausibility 1); the σ-known case is the Bayesian corner (`m(true) = Φ(z)`).
v1 stores `z`; Dempster's *combination rule* only becomes operative when boolean algebra
(AND/OR over comparisons, dependent evidence) arrives — punt, but note independence caveats
on cards the way TransferMath already does.

Comparator family: `>`, `≥`, `<`, `≤` (strict/non-strict is measure-zero under σ but matters
for exact integer ties). Operator is cycled via node menu, not four node types.

### SELECT as a build-rail concept (no Match node)

- Build rail gains **SELECT** alongside TRANSFER / REGRESSOR / DASHBOARD FIG.
- **Join condition = ALL equality links between the selected datasets, ANDed.** Links are
  created on the TREE screen (existing UI: DbTreeView link dialog → `Workspace.AddLink`;
  already persisted in the workspace document by UserStateMapper). Composite key = two links
  (productid ≡ productid, contractid ≡ contractid). Adjacency links (→) never join.
- Base table = dataset of the first wired column. Inner join. Row order follows the base
  table's axis. Card states join stats honestly (`12 rows · inner join · 2 keys · window 240`).
- More than two datasets allowed if connected under equality links.
- Missing link ⇒ warning glyph + tooltip: "if data from two tables is selected, a matching
  condition must be included — add an equality link between their key columns in TREE."
- A comparator wired from leaves of the selected datasets *into* the select is a **computed
  column**, evaluated per joined row. Standalone cross-dataset comparator ⇒ checker objection.

### Network checker

`NetworkChecker` beside NetworkGraph: pure pass over (graph, workspace) → findings
`(nodeId, port, severity, message)`, rendered through the NodeCard `ExtraContent` amber-line
pattern (see EstimatorExtra in NetworkView) + warning glyph/tooltip on the select. Rules as a
growable list:

- **R1** numeric-only — transfers/estimators/figures/comparators take numeric sources;
  categorical/text leaves valid only as SELECT columns. (Later: equality operator accepts
  categoricals ⇒ filters like `product = 'EN590'`.)
- **R2** unlinked datasets on SELECT — the warning above.
- **R3** cross-dataset pointwise nodes — transfer/comparator spanning datasets is objected to
  unless it feeds a covering SELECT (converts today's silent index-zip into a stated refusal).
- **R4** unit mismatch on comparator (`bbl > h` is not a comparison).

### Multidimensional outputs

Edges carry scalar-series (today) or **table**. SELECT emits a table; **DASHBOARD TABLE**
rail element commits it to a `TableCatalog` mirroring FigureCatalog (`tbl.<key>` naming,
unwire ⇒ withdrawn, NextKey naming). Tile editor lists table artifacts for grid tiles.
CanConnect + checker enforce wire typing. Table wires drawn double-stroked (matches James's
sketch).

### Grid tile

- New tile body: real row grid (reuse TableGridControl, the CSV-export grid), fed by a
  TableCatalog artifact. Today's TileKind.Table (one row per *source*) stays as-is.
- **INDEX** chip row in edit-tile menu — the indexing leaf. Default `timestamp` when present,
  else the base table's mount axis (here `parcel`). Drives leading column + sort. Persisted.
- **HIGHLIGHT BOOLEANS** toggle — green/red pills by determination; variance mode appends the
  σ level; vacuous reads `true · no σ`. Boolean columns are exempt from the variance
  blank-guard (TileView ~:116): a z-carrying boolean IS variance-bearing; a vacuous one is a
  determination, not a measurement.
- `TableColumnKind.Boolean` added to ITableDocument (+ Format, ApproximateBytes, parquet
  DataField mapping in TableExportBuilder, CSV cell path) — CSV export inherits booleans.

## Phases (each independently shippable)

1. **Value kinds on the data path** ✓ landed 2026-08-04 — Measure.IsBoolean + Measure.Cells,
   HiveType.IsBoolean, MeasureNumerics.ParseBoolean, Append reads boolean columns by type and
   retains row-faithful cells on every leaf; 3 SeriesTests added. (Pre-existing red test:
   PointerHintTests DataSources target, from James's uncommitted hint WIP — not this work.)
   - `Measure` gains `IsBoolean` and row-faithful `Cells` (`IReadOnlyList<string?>`, one entry
     per fetched row, nulls preserved).
   - Why Cells: `Append` skips null cells per column, so History indices do NOT correspond
     across columns — fine for charts, fatal for joins. History stays chart-facing (nulls
     dropped); Cells is the row-faithful record SELECT reads. Text/categorical columns get
     per-row values for the first time (join keys are strings!).
   - `HiveType.IsBoolean`; boolean columns (Athena type `boolean`) → 0/1 History + Value,
     `IsBoolean` from the schema type, Display stays the cell text ("true").
   - `MeasureNumerics.ParseBoolean` ("true"/"false"/"1"/"0", else NaN).
   - Thread new fields through EVERY manual Measure copy site (see house rules).
   - Tests in SeriesTests incl. the parcels shape (varchar axis, text key columns).

2. **Boolean library group + comparator + checker skeleton** ✓ landed 2026-08-04
   - BooleanGroup (greater_than/greater_equal/less_than/less_equal) in PrimitiveGroups;
     NetworkNodeKind.Compare with a/b edge roles, CHANGE OPERATOR / SWAP WIRES menu, id prefix
     `compare:c{n}` + own counter; TransferMath.EvaluateComparison + ComparisonObjection +
     ComparisonFormula; IsBoolean/SigmaLevel on TransferInput/TransferResult/DashboardFigure;
     MeasureNumerics.FormatBoolean/FormatSigmaLevel ("exact" for zero spread);
     NetworkChecker (R1 only — **R4 is enforced as an evaluation refusal** in
     ComparisonObjection, stated on the card, not duplicated as a checker warning);
     NetworkNodeState.Operator appended; 10 tests + snapshot probe `1-netw-compare`
     (comparator + boolean figure render verified).
   - Snapshot probe fix: CreateFunctionTab now right-clicks a *named* primitive, not the
     library list's centre — the centre lands on arbitrary rows as the list grows.

3. **Grid tile + TableCatalog + single-table SELECT** ✓ landed 2026-08-04 — DerivedTable /
   TableCatalog / DerivedTableView (Models/TableCatalog.cs), NetworkNodeKind.Select+Table with
   wire typing, EvaluateSelect over Cells, TileKind.Grid + TileSourceKind.Table + IndexLeaf +
   HighlightBooleans (persisted), grid body with boolean pills in TileView, editor table picker +
   INDEX chips + highlight toggle, 5 SelectTests, snapshot probe `3-dash-grid` (parcels grid with
   green/red pills verified). HeaderCell gained ellipsis trimming.
   Scope decisions (deviations from the original sketch, for good reasons):
   - Grid tile body is **hand-rendered in TileView** (TextBlocks in a Grid + ScrollViewer,
     boolean pills inline) — NOT via TableGridControl/ITableDocument. The derived-table window
     is ≤240 rows; the parquet/paging machinery buys nothing here. TableColumnKind.Boolean for
     the CSV-export screen is deferred until derived tables actually export.
   - SELECT inputs are **Measure leaves only** in this phase. A comparator column computed from
     chart-facing History would silently misalign with row-faithful Cells whenever a column has
     nulls — computed columns need per-row evaluation over Cells, which is phase 4's machinery.
   - Multi-dataset selection already shows the R2 message (evaluation refuses; joins land in 4).
   - New kinds: NetworkNodeKind.Select (`select:s{n}`) + NetworkNodeKind.Table (`table:{key}`,
     figure-style commit). Wire typing in CanConnect: Select→Table only, Table accepts Select
     only, Figure refuses Select.
   - DerivedTable/TableCatalog (mirrors FigureCatalog, no declared fallback); column kinds
     Number/Text/Boolean from leaf shape; row count = min Cells length across columns.
   - TileKind.Grid + TileSourceKind.Table; editor lists `derived tables /` for grid tiles;
     INDEX chip row (default: column matching the dataset axis, else first) sorts + leads;
     HIGHLIGHT BOOLEANS toggle; TileState gains IndexLeaf + HighlightBooleans (defaulted).
4. **Equality-link joins + R2/R3 + computed columns** ✓ landed 2026-08-04 — SelectEvaluation.cs:
   link-driven inner join (datasets fold in wiring order; ALL equality links AND together — the
   composite key; null keys never match; base row order preserved; matched/base counted in the
   note). Computed columns evaluate per joined row from Cells, σ levels from "__sigma" carrier
   cells (flat σ fallback, NaN stays vacuous). Standalone cross-table comparator evaluation is
   refused (guard in EvaluateComparatorNode) with checker R3; feeding a SELECT it is legitimate
   and the card says "evaluated per joined row by the SELECT it feeds". Compare→Select wiring
   allowed. Probes `1-netw-select` + `3-dash-grid` render the full parcels ⋈
   contract_requirements story with a computed `<` column (`true · 3σ` green, `false · 1.2σ`
   red pills).

## Deferred polish (post-v1, in rough priority order)

- Per-column display labels for boolean columns (true → "on spec") and aliases for computed
  columns (today a computed column is titled by its formula).
- TableColumnKind.Boolean in ITableDocument/parquet/CSV — when derived tables reach the
  CSV-export screen.
- Left joins, and surfacing row-multiplication (many-to-many keys) on the card.
- Dempster's combination rule — becomes operative when boolean algebra (AND/OR over
  determinations, dependent evidence) arrives.
- `NetworkGraph.DatasetOf`'s unmounted fallback can misjudge member subtrees (sensor level) —
  costs a spurious two-table warning, never a wrong join.
- Suggest/pre-fill equality links when a SELECT spans unlinked tables whose key names match.

## Known red test (not this work)

`PointerHintTests.Catalog_TargetsExistInTheTransferFunctionView` — James's uncommitted hint WIP
targets a "DataSources" control no view defines yet.

## House rules discovered (do not violate)

- **`Reading` vs `DeclaredReading`**: tree-building/binding code must use `DeclaredReading`
  (the store would leak stale values in); everything else reads via `Workspace.ReadingAt`.
- **Measure copy sites** — any new Measure field must be threaded through ALL of:
  MeasureNumerics.With, MeasureNumerics.BindSigmaLeaves, MeasureNumerics.Hydrate,
  HttpDatasetCatalog.BindSiblingSigma, HttpDatasetCatalog.AsCarrier.
- **DTOs**: append-with-defaults only (`string? X = null`) — SchemaVersion is never checked;
  nullable-with-defaults IS the compatibility strategy. New serializable *types* must be added
  to the `[JsonSerializable]` list in UserStateJson (browser head is AOT+trimmed).
- **Persistence chain**: model → UserStateMapper.Capture*/Apply* → UserStateDtos → UserStateSync
  (dirty-mark via Changed/Edited events, 2s debounce). Load order: settings → functions →
  workspace → network → dashboard → ReadingLoader.
- **Node id prefixes** are parsed on Load to resume counters (`transfer:t{n}`); new kinds need
  their own prefix + counter or ids collide.
- **NetworkGraph.CanConnect** hardcodes kind rules; `Reading(...)`'s switch silently yields
  null for unhandled kinds.
- **Verification**: no screencapture — use the app's `--snapshot` PNG mode (SnapshotRunner).
  Snapshot probes must call `window.ShowScreen(...)` explicitly (landing screen is MapScreen).
- **Stale copy**: `.claude/worktrees/nice-mclaren-41337d/` holds an old source tree — never
  grep/edit it.
- Demo values are display-string-first (MeasureNumerics.Hydrate); real-catalog values are
  numeric-first and must never acquire invented history/σ.

## Upstream (DataFeed repo, James's side)

- `contract_requirements` likely violates one-row-per-axis on any single column (products
  repeat across contracts) ⇒ add a unique `requirement_key` column upstream. Until then the
  table's values are nulled by the rows-per-point check.
- Neither new table has `timestamp` ⇒ mount requires a manual axis pick (`parcel` works).
