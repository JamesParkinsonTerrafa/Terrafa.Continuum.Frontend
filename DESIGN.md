# Terrafa Continuum — design language

## Premise

Industrial. Every control is a physical object machined into a panel, not a rectangle
drawn on glass. A user should be able to point at anything on screen and say what it is
made of and whether it sticks out or goes in.

Three consequences:

- **One light source, fixed at the top-left.** Every highlight falls on the top-left of a
  raised object and the bottom-right of a recessed one. Shadows are the reverse. Nothing
  in the app may light itself from another direction.
- **State is physical, not decorative.** Selected means pressed in. Available means raised.
  Fixed means engraved. Colour is reserved for meaning (amber = command, red = flagged,
  cyan/green/purple = kind), never for indicating which control is active.
- **Surfaces are one material.** Chrome — top bar, tab strip, draft bar, buttons — shares
  a single surface colour so the shadows read as depth in one continuous plate rather than
  as separate stuck-on tiles.

## Surfaces

`EmbossSurfaceBrush` is the material. Chrome bars use the same value (`BgBarBrush`), so a
raised control on chrome differs from its surroundings only by its shadows. A control that
sets its own fill — an amber command key — keeps the same geometry and shadows, so it reads
as the same object cut from a different metal.

Light: `#EDF3F9`. Dark: `#0A0C10`.

## Corners

Corners are continuous, not circular — the Apple pre-OS-26 curve. `Controls/SquircleBorder.cs`
builds the outline as four straight edges joined by one cubic Bézier per corner (4 control
points: two anchors, two handles).

Given radius `r`, the corner begins `1.5287r` from the vertex along each edge rather than `r`,
and each handle sits at `0.822` of the way from its anchor to the vertex. The curve then hugs
the edge longer and turns harder at the apex, which is what reads as "continuous". Both numbers
fall out of one constraint — that the curve pass the 45° diagonal at the same depth as a
circular corner of radius `r` — so a single expression covers the whole family:

```
extent = r * (1 + 0.5287 * smoothing)
handle = (4 - 2.3431 * r / extent) / 3
```

`smoothing = 0` yields `extent = r` and `handle = 0.5523`, exactly the circular-arc Bézier
approximation. `smoothing = 1` is Apple's curve and the default.

`extent` is clamped to half the short side, so a corner can never exceed the control; when
clamped, `handle` falls out of the same formula and the corner degrades gracefully back toward
circular rather than self-intersecting. This matters at button scale: a 25px-tall control clamps
at any radius above ~8, so control radius is 8 and panel radius is 18.

The shadow silhouette is still drawn from a circular-cornered rect — the shadows are blurred by
5px or more, so the ~1px difference at the corner is not resolvable, and it lets Avalonia's own
`BoxShadow` renderer do the blur. Only the crisp surface edge is a true squircle, and only the
crisp edge is legible as a shape.

## Emboss

Four `SquircleBorder` classes in `Themes/Terminal.axaml`, backed by themed `BoxShadows` in
`Themes/Palette.cs`. Ported from soft-UI CSS `box-shadow`; Avalonia's `BoxShadow` takes the
same `[inset] offsetX offsetY blur spread colour` grammar, so the specs transfer directly.

| Class | Depth | Use |
| --- | --- | --- |
| `emboss` | raised, control scale, strength from settings | neutral buttons and unselected tabs |
| `emboss-key` | raised, control scale, full strength | primary command keys |
| `emboss-press` | recessed, control scale, full strength | the selected state of a button |
| `emboss-card` | raised, panel scale | standalone cards |
| `emboss-inner` | recessed, panel scale | wells that hold content — input fields, readouts |

Depth is not decoration, so it is not spent evenly. A row of five equally raised tabs is five
competing objects and the eye has nowhere to land. Idle chrome sits near-flat and is separated
by rules instead; the depth budget goes to the two things worth reading as physical — the
selected tab, pressed into the panel, and the command keys that carry the amber.

`emboss` and `emboss-press` set geometry and shadow but **not** `Background`. Each usage
supplies its own fill: `EmbossSurfaceBrush` for a neutral button, `AmberBrush` for a command
key. `emboss-card` and `emboss-inner` do set the surface, since they are always material.

Shadow offsets are tuned per scale, not scaled from one spec. Control-scale shadows use
2–3px offsets; the panel-scale ones use the full 7px. A card-scale shadow on a 25px button
swamps it, and a control-scale shadow on a 200px card disappears.

### Buttons

Every button in the app is a `SquircleBorder` with padding and a text child. Tabs carry `emboss`
and swap to `emboss-press` when selected; separator rules between them are hidden on either side
of the selected tab, so the pressed key reads as a break in the run rather than one cell of a
grid.

Two knobs live under Settings › BUTTON UI: **unselected depth**, which scales the alpha of the
raised shadows on the `emboss` class only, and **corner radius**, which drives every emboss
class. Both write application resources that the styles consume through `DynamicResource`, so
they retarget live without rebuilding any view. Buttons need vertical room for their shadow — a control in a fixed-height
bar wants roughly 16px more than its own height, which is why the tab strip is 40px, the panel
header 40px, and the top bar 36px.

## Engraved text

`TextBlock.engraved` — letters stamped into the surface. Mid-tone fill plus a highlight on
the lower-right lip, matching the light model above. Applied to the TERRAFA wordmark.

Needs weight to sit in: use on bold type at 11px or larger. Below that the recess is thinner
than a stroke and reads as a rendering artefact.

The blur is themed rather than fixed (`EngraveBlurRadius`): on the near-white light surface a
hard 1px lip reads as a crisp stamp and any blur smears into the background, while on the
near-black dark surface a hard lip turns letters into hollow outlines and they need a soft
bloom to gain depth.

## Theming

`Themes/Palette.cs` holds every themed value as a dark/light pair and pushes it into
application resources. Four kinds are supported — brushes, colours, doubles, and box shadows —
so an effect that varies by theme is expressed as a themed resource rather than as a branch in
view code. `ThemeManager` owns the current theme and also drives Avalonia's
`RequestedThemeVariant`, so built-in controls (scrollbars, sliders) match the palette.

Light is the default.

### Highlights

The amber family — `AmberBrush` plus its soft, pale, fill and chip-border variants — is registered
as *highlight* rather than plain themed, so it tracks two further knobs under Settings › APPEARANCE:
**highlight sat** and **highlight bright**. Both scale the current theme's own colour in HSL —
saturation against `S`, brightness against `L` — leaving hue and alpha alone, so the five ambers move
as one family and the translucent fills keep their transparency. At 1.00/1.00 the transform is
skipped outright and the brushes are byte-for-byte what `Palette.cs` declares.

The shipped default is 0.20 / 1.55 — amber drained most of the way to grey and lifted a step, so it
separates from body text by tone rather than by shouting. `Palette.cs` still declares the full-strength
colour: it is the origin the knobs scale from, not what lands on screen.

Only amber is registered this way. Cyan, green, purple and red carry kind, and draining those would
erase meaning; amber is the one colour spent on emphasis rather than identity, so it is the one worth
turning down.

## Structure

- **Rules line up.** Panels sitting side by side share `HeaderHeight` and `FooterHeight` so
  their horizontal rules sit at the same y. Auto-sized chrome drifts as soon as content
  differs — a header button, a taller glyph — and the drift is visible.
- **Hints are separable.** Instructional prose (tab-strip hints, status-bar notes, panel
  footnotes) is bound to `HintsVisible` and collapses from Settings. Data, alerts, and
  readouts are not hints and always stay. Where a status bar is entirely hints, the whole bar
  collapses rather than leaving an empty strip.
- **Building is opt-in.** The left rails are editing surfaces, bound to `BuilderPanelsVisible`
  and hidden until BUILDER MODE — the first row in Settings — is switched on. Off is the
  default: the app opens read-only, and the top bar shows a `BuilderHintVisible` note pointing
  at the switch. The readouts themselves never collapse; only the tools for changing them do.
- **Session state outlives the screen it was built on.** The mounted tree (`Workspace`), the
  network canvas (`NetworkGraph`) and the board (`Dashboard`) are models, not view fields.
  A change on any screen rebuilds the others, and a screen that held its own state would throw
  away the operator's work every time they went to fetch the leaf it needed. A screen renders
  its model and writes back to it; it never keeps a second copy.
- **One value has one owner.** A dashboard figure is computed by the network from the leaves
  wired into it and published to `FigureCatalog` — the single list the network draws its cards
  from and the dashboard offers as tile sources. Where a value cannot be computed it is
  *declared*, and says so: the seeded hazard branch is not identifiable from its leaves, so
  nothing downstream of it is handed a number the chain cannot support.
- **The read path is thin; the table owes it a shape.** A dataset names the column its readings
  run along — `timestamp` when it has one, otherwise the operator picks — because Athena has no
  inherent row order and the service caps a read *after* ordering. From there a value's whole
  journey to a chart is: rows come back sorted, each column's non-null cells parse into the
  series, the newest cell is the reading. No windowing, no repair. What earns that thinness is
  a contract on the data: **one row per axis value per table**. A table that carries more —
  one row per sensor or analyte — interleaves several series in every column, and the client
  detects it, says so on the header and every leaf, and declines to draw through it; the fix
  belongs in the table. A text column keeps its text and carries no series.
- **σ travels beside its measure.** A flat table cannot nest a σ under its reading, so the feed
  spells the pairing in the column name — `level__sigma` beside `level` — and the catalogue
  folds it in row-by-row: σ(x) is the carrier's cell on each row the measure read from, or the
  flat newest σ where the carrier is missing one. The carrier leaf stays in the tree (it is
  real data) but is never offered as a quantity to plot. Nested trees keep the `sigma`-child
  spelling, bound by `MeasureNumerics.BindSigmaLeaves`.
- **Overlays are anchored to what they describe.** On the map, pins and zones hold normalized
  image coordinates, never screen ones. Swap the client's photo, or fit a portrait one into a
  landscape frame, and every figure is still over the same piece of ground. The card keeps its
  pixel size and its offset from the anchor, because a readout that scaled with the photo would
  stop being readable.
- **Snapshots are the review surface.** `--snapshot <dir>` renders every view headlessly in
  both themes plus a hints-off set, and drives the interactive surfaces — including a rail drag
  onto the plan and a client upload — into frames of their own. Check visual work there rather
  than by eye on a running window.
