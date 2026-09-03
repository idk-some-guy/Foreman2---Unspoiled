# Canvas reference — ProductionGraphView port (phase 3 prep)

Source of truth: `upstream/Foreman/ProductionGraphView/` (30 files, ~6075 LOC) +
its host `upstream/Foreman/Forms/MainForm.cs`. Every file was read in full.
Phase 1-2 already ported `Foreman.Core` (Models/DataCaching/Serialization,
icons as `SKBitmap`) and an Avalonia shell (`Foreman.Mac`) with a
`GraphViewerHost` `ContentControl` placeholder in `MainWindow.axaml` (line 90)
awaiting the canvas built here.

Tags: **P3** = read-only rendering/viewport/hit-test/tooltip (this phase).
**P4** = editing (drag, add/delete, right-click menus, link-drag). **P5-panel**
= floating edit/chooser panels (separate WinForms controls hosted via
`FloatingTooltipControl`, out of scope for both P3 and P4's canvas work).

## 1. Class inventory

### Elements/ (14 files, GraphElement hierarchy)

`GraphElement` (abstract, 133 LOC) — root of all drawable/hit-testable canvas
objects. Owns `Bounds`/`Location`/`Visible`, parent-relative coordinate
conversion (`GraphToLocal`/`LocalToGraph`), the `Paint`/`Draw` split, mouse
event virtuals, `SubElements` tree, and a static `RightClickMenu`
(`ContextMenuStrip`, P4). **P3** (rendering/hit-test parts only; menu is P4).

- `BaseNodeElement` (abstract, 481 LOC) — shared node chrome: rounded-rect
  background + status border, input/output `ItemTabElement` layout, error
  badge, highlight overlay, drag/selection, right-click menu (huge, P4-only).
  **P3** for `Draw`/`DetailsDraw`/`UpdateState`/`UpdateValues`/tooltips;
  **P4** for `MouseDown`/`Dragged`/`AddRClickMenuOptions`/copy-paste.
  - `RecipeNodeElement` (334) — assembler/beacon recipe node. Owns child
    `AssemblerElement` + `BeaconElement`. Huge right-click menu (paste
    options) is P4. **P3+P4 split**.
  - `PassthroughNodeElement` (107) — simple-draw line-only mode when
    auto-rate/no-key-node/fully-connected; else falls back to base node draw.
    **P3** draw; **P4** simple-draw toggle menu.
  - `SupplierNodeElement` (33) — "Infinite Source:" / "Exact Input:" node.
    **P3**.
  - `ConsumerNodeElement` (33) — "Infinite Sink:" / "Required Output:" node.
    **P3**.
  - `SpoilNodeElement` (58) — spoilage conversion node, single dynamic output
    tab. **P3**.
  - `PlantNodeElement` (67) — planting/growth node, multiple dynamic output
    tabs. **P3**.
- `ItemTabElement` (172) — per-item input/output badge (icon + flow text +
  direction chevron + status border color). **P3** draw/tooltip; **P4**
  right-click delete-connection.
- `AssemblerElement` (159) — assembler icon + module icons/dots/letter-tally
  (3 density tiers) + stat readout at LOD High. **P3**.
- `BeaconElement` (142, `internal`) — beacon icon + module display (same 3
  tiers) + beacon count text. **P3**.
- `ErrorNoticeElement` (114) — orange warning-triangle badge, top-left of
  node; tooltip lists errors/warnings text. **P3** draw/tooltip; **P4**
  click-to-autoresolve.
- `BaseLinkElement` (abstract, 261 LOC) — bezier path construction for the
  three link shapes (Simple/UShape/NShape), visibility bounds, arrow-cap pen.
  **P3**.
  - `LinkElement` (44) — link bound to a live `INodeLinkViewModel`, resolves
    endpoints from `ItemTabElement.GetConnectionPoint()`. **P3**.
  - `DraggedLinkElement` (216) — in-progress link-drag ghost, multi-endpoint
    fan-out for group passthrough dragging. **P4 only** (no read-only use).
- `AnnotationElement` (abstract, 492 LOC) — text/shape annotation base:
  8-handle resize, selection highlight, lasso-edge hit test, drag. **P3**
  Draw/bounds/`ContainsPoint`/`ForceVisible`/`ToSaveData` (annotations must
  render read-only since they're part of saved graphs); **P4** all
  drag/resize/right-click/properties-dialog machinery.
  - `TextAnnotationElement` (211) — freehand text label, GDI `Font` +
    `StringFormat`, auto-fit-box-to-text. **P3** draw; **P4** editing.
  - `ShapeAnnotationElement` (148) — rectangle/ellipse with fill+border.
    **P3** draw; **P4** editing.

### Root ProductionGraphView/ (16 files)

- `ProductionGraphViewer` (1557 LOC, `partial class : UserControl`) — the
  canvas control itself. See §2-3. **P3+P4 split** (see below).
- `ProductionGraphViewer.Annotations.cs` (448) — partial-class extension:
  annotation CRUD, lasso selection, clipboard import, cursor-per-handle,
  context menu items. **P3**: `LoadAnnotationsFromSave`, `GetAnnotationAtPoint`
  (needed for tooltips even read-only), `GetExportBounds`. **P4**: everything
  else (add/select/drag/delete/lasso).
- `ProductionGraphViewer.Designer.cs` (46) — WinForms designer boilerplate
  (event wiring, `DoubleBuffered`, `BorderStyle`). **N/A** — Avalonia control
  templates/styles replace this outright.
- `GridManager` (89) — background dot/line grid: minor/major spacing, zero
  axis, locked-drag-axis indicator. **P3** (`Paint`, `ShowGrid`,
  `AlignToGrid` used for read-only display); drag-axis fields are P4 but
  harmless to port together (small class).
- `PointingArrowRenderer` (99) — screen-edge chevrons pointing at off-screen
  error/warning/disconnected/over-under-supplied nodes. **P3**.
- `FloatingTooltipRenderer` (140) — draws the tooltip bubble (triangle +
  rounded rect + text or custom-draw callback) for both floating WinForms
  controls (P5-panel, e.g. edit panels) and plain hover tooltips (P3, e.g.
  `GetToolTips()` results). **P3** for the hover-tooltip path only.
- `FloatingTooltipControl` (39) + `TooltipInfo` record struct (in same file)
  — wraps an actual WinForms `Control` (edit panel, recipe panel) as a
  positioned floating tooltip. **P5-panel** (the `Control` wrapper itself);
  `TooltipInfo` struct is **P3** (used by hover tooltips too).
- `EditPanelScreenLayout` (54) — clamps/shifts a floating panel rectangle to
  stay inside the viewer bounds. **P5-panel**.
- `EditPanelViewportLayout` (75) — scroll-host wrapper + auto-size math for
  floating edit panels that exceed viewport height. **P5-panel**.
- `NodeCopyOptions` (157) — assembler/module/beacon clipboard copy-paste
  payload for recipe nodes. **P4**.
- `Annotations/AnnotationClipboardCodec.cs` (35) — merges annotation JSON
  into graph clipboard fragments. **P4** (clipboard only).
- `Annotations/AnnotationSelectionModifiers.cs` (14) — static Alt/Ctrl
  modifier-key selection-mode helpers. **P4**.
- `Annotations/GraphExportBounds.cs` (69) — computes combined node+annotation
  bounding rect for full-graph PNG export. **P3** (export/full-graph render
  needs it; also useful for a "fit to view" P3 command).
- `Annotations/TextAnnotationLayout.cs` (48) — font-size/box-size math for
  text annotations (measure, resize-to-font-scale). **P3** (needed to render
  saved text annotations at correct box size) + **P4** (resize interaction).

**Total: 30 classes/static-helper files inventoried** (29 `.cs` files; one
file — `FloatingTooltipControl.cs` — declares two types: the class and the
`TooltipInfo` record struct).

## 2. Viewer core — state that matters for read-only rendering

`ProductionGraphViewer : UserControl` fields (all in root `.cs`, lines
25-146):

- **Viewport model**: `Point ViewOffset` (graph-space translation, applied
  *after* scale) + `float ViewScale` (0.01-2.0, clamped in
  `MouseWheel` handler) + `Rectangle VisibleGraphBounds` (recomputed by
  `UpdateGraphBounds()`, used to cull element visibility every paint).
- **Coordinate transforms** (`ScreenToGraph`/`GraphToScreen`, lines
  1266-1272): screen origin is control center (`Width/2, Height/2`); graph
  point = `(screenPoint - center) / ViewScale - ViewOffset`. Paint applies
  the inverse via GDI+ matrix ops (see §3) rather than per-point math — hit
  testing uses the explicit formula instead since GDI+ has no live inverse
  transform query mid-frame in this codebase.
- **Element collections**: `Dictionary<NodeId, BaseNodeElement>
  nodeElementDictionary` + parallel `List<BaseNodeElement> nodeElements`
  (dictionary for O(1) lookup by id, list for paint-order iteration);
  identical dual dictionary+list pattern for `LinkElement`/`LinkId`. Exposed
  read-only via `NodeElementDictionary`/`LinkElementDictionary` properties.
  `annotationElements` (`List<AnnotationElement>`, in the `.Annotations.cs`
  partial) is a flat list, no dictionary (annotations have no server-side id
  to key by).
- **LOD**: `enum LOD { Low, Medium, High }` — Low = names/text only (no
  assembler/beacon sub-elements, they're hidden via `SetVisibility(false)`);
  Medium = assembler+beacon icons, no percentage stat readout; High = adds
  the Speed/Prod/Power/Quality percentage text block. Read from
  `AssemblerElement.Draw`/`RecipeNodeElement.UpdateState` checking
  `graphViewer.LevelOfDetail`.
- **Session wiring**: `ProductionGraphSession Session` (from `Foreman.Core`,
  already ported) fires `NodeViewModelAdded/Removed`,
  `LinkViewModelAdded/Removed`, `NodeValuesUpdated`, `GraphCleared` — the
  viewer's constructor subscribes each to create/destroy the matching
  `BaseNodeElement`/`LinkElement` and calls `Invalidate()`. This event-driven
  element-lifecycle pattern is model-agnostic and ports as-is.
- **Redraw triggers**: nothing auto-invalidates on a timer — every mutation
  path (`SetLocation`, `ViewModel_NodeStateChanged`, resize, zoom, drag,
  session events) explicitly calls `Invalidate()`. `UpdateNodeVisuals()`
  walks all nodes calling `RequestStateUpdate()` then invalidates — this is
  the main "external state changed, redraw" entry point `MainForm` calls
  after settings changes.
- **Paint order** (`GetPaintingOrder()`, lines 642-651): annotations →
  dragged-link-ghost (P4) → links → nodes. Bottom-to-top: annotations sit
  under everything (background decorations), links under nodes (so node
  boxes occlude the bezier endpoints cleanly), nodes last (topmost,
  clickable). This order is a `yield return` generator re-evaluated every
  paint and every hit-test-adjacent call (visibility update, drag-diff
  count) — cheap because it's lazy but called ~4x/frame.

## 3. Paint pipeline — `OnPaint` step by step

`OnPaint(PaintEventArgs e)` (lines 663-673) sets up the transform, then
delegates to `Paint(Graphics, bool FullGraph)` (675-759) which is also called
directly (with `FullGraph=true`) for full-resolution PNG export, bypassing
the screen transform:

1. **Transform setup** (`OnPaint` only): `ResetTransform()` →
   `SmoothingMode = HighQuality` → `Clear(BackColor)` →
   `TranslateTransform(Width/2, Height/2)` (screen origin → center) →
   `ScaleTransform(ViewScale, ViewScale)` → `TranslateTransform(ViewOffset)`.
   This 3-matrix composition is exactly what `GraphToScreen`/`ScreenToGraph`
   compute by hand for hit-testing — **parity point**: any Avalonia port must
   replicate both the `DrawingContext` transform *and* the manual point-math
   formula identically, or hit-testing and rendering will drift.
2. **Visibility pass**: `element.UpdateVisibility(bounds)` for every element
   in paint order — `FullGraph` uses `Graph.Bounds` (whole graph, no
   viewport culling) and force-shows all annotations
   (`ann.ForceVisible()`); normal frame uses `VisibleGraphBounds`. This is a
   simple AABB-vs-AABB test per element, no spatial index — fine at Foreman's
   node counts (hundreds, not thousands).
3. **Selection pen width fixup**: `selectionPen.Width = 2 / ViewScale` — pens
   are **module-level `static readonly`** (cached across frames) but their
   *width* is mutated in place every frame to stay visually constant in
   screen pixels despite the scale transform. Same pattern in `GridManager`
   and `AnnotationElement` (`penWidth = X / graphViewer.ViewScale`). **This
   is the one per-frame-mutable-static pattern to preserve**: SKPaint/Pen
   objects should be either recreated per frame with corrected width, or a
   long-lived paint's `StrokeWidth` set each frame — never left at a stale
   scale.
4. **Grid paint** (skipped when `FullGraph`): `Grid.Paint(...)`, draws under
   everything else already (called before links/nodes).
5. **Link width computation** (`DynamicLinkWidth` on/off): scans all
   `LinkElement`s to find max per-item/fluid throughput, then linearly maps
   each link's `Throughput/max` ratio into `[minLinkWidth=3,
   maxLinkWidth=35]` pixels; static width `3` otherwise. This must happen
   *before* the draw loop since `LinkElement.Draw` reads `LinkWidth`.
6. **`PrePaint()`** on every element in paint order — this is where
   `BaseNodeElement.PrePaint` lazily calls `UpdateState()`/`UpdateValues()`
   if a dirty flag was set by a session event, i.e. **state recomputation is
   deferred from "on change" to "on next paint,"** batching multiple rapid
   model updates into one layout pass.
7. **Main draw loop**: `element.Paint(graphics, style)` for every element —
   `style` is computed once per frame from: `FullGraph ? PrintStyle :
   IconsOnly ? IconsOnly : (visibleCount > NodeCountForSimpleView ||
   ViewScale < 0.2) ? Simple : Regular`. So the *same* element tree renders
   in up to 4 different fidelity modes depending purely on current zoom/count
   — see §4 for what each mode skips.
8. **Draw-shape rubber-band** (P4) and **selection rubber-band + stats
   tooltip** (P4) — both skipped when `FullGraph`.
9. **`graphics.ResetTransform()`** — everything from here draws in raw
   screen space, no scale/offset.
10. **Arrow overlay**: `ArrowRenderer.Paint(graphics, Graph)` — screen-space,
    reads `Graph.Nodes` directly (not element list) plus `GraphToScreen` per
    node.
11. **Tooltip overlay**: `ToolTipRenderer.Paint(graphics, showCondition)` —
    `showCondition` is `TooltipsEnabled && !SubwindowOpen &&
    currentDragOperation == None && !viewBeingDragged` (P3-relevant: hover
    tooltips should only show when idle, not mid-pan/mid-drag).
    `ClearExtraToolTips()` runs immediately after — extra tooltips (like the
    selection-stats box) are frame-scoped, rebuilt every paint, not
    persistent state.
12. **Paused border**: red 5px rect around the whole viewer if
    `Graph.PauseUpdates` — cosmetic global-state indicator, trivial P3 port.

GDI+ constructs used throughout: `Pen`/`SolidBrush` (mostly `static
readonly`, i.e. process-lifetime-cached — map to cached `SKPaint`/Avalonia
`Pen`/`Brush` instances, not per-frame allocations), `GraphicsPath` (only in
`GraphicsStuff.FillRoundRect`/`DrawRoundRect`/`FillRoundRectTLFlag` — 4-arc
rounded-rect construction, trivially expressed as `SKPath.AddRoundRect` or
Avalonia `RoundedRect`), `Matrix`-via-`Translate/Scale` calls (not an
explicit `Matrix` object — chained transform calls), `Graphics.DrawBeziers`
(cubic bezier polylines for links — `SKPath.CubicTo` chain or Avalonia
`PathGeometry` with `BezierSegment`s), and `Graphics.DrawString` /
`Graphics.MeasureString` (see §7 for the text-metrics parity concern — this
codebase does **not** use `TextRenderer.DrawText`, so there's no GDI
vs GDI+ text-rendering-engine mismatch to worry about, only the
GDI+-vs-Skia/Avalonia metrics gap).

Clipping: none observed anywhere in the pipeline — visibility culling is
done by skipping `Draw()` calls (via the `Visible` flag), not by GDI+ clip
regions. Quality: `SmoothingMode.HighQuality` is the only quality knob, set
once per `OnPaint` call (not per-element).

## 4. Node rendering specifics

### Geometry constants (`BaseNodeElement`, lines 54-62)

```
BaseSimpleHeight   = 96   // supplier/consumer/passthrough/spoil/plant height
BaseRecipeHeight   = 144  // recipe node height (LOD Medium/High); 96 at LOD Low
TabPadding         = 7
WidthD             = 24   // widths rounded up to a multiple of this
PassthroughNodeWidth = 72 // WidthD * 3
SpoilNodeWidth     = 144  // WidthD * 6 (declared but MinWidth is what's used)
MinWidth           = 144  // WidthD * 6 — floor for supplier/consumer/spoil/plant width
BorderSpacing      = 1    // gap between adjacent node borders
```

Recipe node width is dynamic: `Max(MinWidth, Max(inputTabWidths,
outputTabWidths) + 10)`, then rounded up to the next `WidthD` multiple.
Corner radius is a plain `int radius` param to `GraphicsStuff.FillRoundRect`
— **10px** for the outer flow-status border, **7px** for the inner
background fill, **8px** for the highlight overlay. `ItemTabElement` uses
`border=3` as both padding and its own corner radius.

### Base fill colors (resting/"clean" state, exact ARGB from source)

| Node type | `CleanBgBrush` ARGB | Notes |
|---|---|---|
| `SupplierNodeElement` | `(255,231,214,224)` pale pink/lavender | "Infinite Source:" |
| `ConsumerNodeElement` | `(255,249,237,195)` pale khaki/tan | "Infinite Sink:" / "Required Output:" |
| `RecipeNodeElement` | `(255,190,217,212)` pale green | matches README "recipe = green" |
| `PassthroughNodeElement` | `(255,200,200,200)` neutral gray | overridden entirely when simple-draw active (draws a colored line instead, see below) |
| `SpoilNodeElement` | `(255,190,217,212)` same pale green as recipe | |
| `PlantNodeElement` | `(255,190,217,212)` same pale green as recipe | |

### Status colors (border + fill overrides, `BaseNodeElement` lines 35-42)

- `errorBgBrush = Brushes.Coral` — full-fill replacement for `CleanBgBrush`
  when `ViewModel.State == NodeState.Error` (this is the README's "full
  orange-salmon fill" missing-recipe state).
- `equalFlowBorderBrush = Brushes.DarkGreen` — default/balanced border.
- `overproducingFlowBorderBrush = Brushes.DarkGoldenrod` — overproduction
  (README's "golden border").
- `undersuppliedFlowBorderBrush = Brushes.DarkRed` — insufficient inputs
  (README's "red border"), suppressed for `SupplierNodeElement` (a supplier
  can't be undersupplied by definition).
- `ManualRateBGFilterBrush = (50,0,0,0)` — semi-transparent black overlay
  when `RateType == Manual` (darkens the fixed-rate nodes slightly).
- `selectionOverlayBrush = (100,100,100,200)` — highlight tint when
  `Highlighted`.
- Corner "supply flag" (`FillRoundRectTLFlag`, top-left diagonal-cut
  triangle) drawn in the border-status color when `FlagOUSuppliedNodes` is
  on and status isn't equal-flow — this is the README's "orange diagonal
  corner-flag."
- Warning flag: same triangle shape but always `errorBgBrush` (Coral) when
  `State == Warning`, independent of the `FlagOUSuppliedNodes` setting.
- `ItemTabElement` border pens: `regularBorderPen = DimGray`,
  `overproducedBorderPen = DarkGoldenrod`, `disconnectedBorderPen = DarkRed`.
- Passthrough simple-draw line/dot color: `passthroughItem.AverageColor` —
  **per-item average color from `Foreman.Core` icon data**, not a fixed
  palette; this is the "link color tied to item identity" the UI reference
  doc calls out.

### Badge/icon layout

- Error/warning badge: 24x24 `ErrorNoticeElement`, anchored at node
  top-left corner (`Location = (-Width/2, -Height/2)`), drawn from a shared
  static `Graphics/ErrorIcon.png` bitmap (via `IconCache.GetIcon`, 64px
  source scaled to 24px).
- `ItemTabElement`: 32px icon + up to 2 lines of flow-rate text (regular +
  overproduced-secondary-line), tab width = `iconSize(32) + border(3)*3 =
  41px`, height = `32 + textHeight + border + 3` (grows by 10px when a
  second overproduced line is shown). Direction chevron (triangle,
  `directionBrush = (40,0,0,0)` translucent black) drawn behind the tab only
  when `DynamicLinkWidth || !ArrowsOnLinks` (i.e. suppressed when arrow caps
  on links already convey direction).
- `AssemblerElement`: 54px assembler icon; modules shown as actual 13px icon
  images if count ≤ 6, colored 3px dot markers in a 4x7 grid if count ≤ 28
  (dot color = speed/blue, prod/red, eff/green, quality/gold, unknown/black
  by module bonus type), else a text tally (`S:n E:n P:n Q:n U:n`) — **3-tier
  degradation by module count**, independent of LOD.
- `BeaconElement`: same 3-tier pattern at smaller scale (28px icon, 12px
  module icons, 6-icon / 32-dot / tally thresholds, 8x4 dot grid).
- Assembler stat readout (LOD High only): Speed/Prod/Power(+Quality if >0)
  as percentage-formatted text (`{0:+0%; -0%; 0%}`), positioned right of the
  assembler icon.

### Text layout at the three LOD settings

- **Low**: node shows only a centered name string (via
  `GraphicsStuff.DrawText`, auto-shrinking font — see §7) + a small 32px
  category icon (assembler/spoilage/planting icon) + productivity-bonus tick
  marks (small colored ellipses down the left edge, max 6 shown then a "+"
  glyph). `AssemblerElement`/`BeaconElement` sub-elements are hidden
  entirely (`SetVisibility(false)`).
- **Medium**: full recipe node height (144 vs 96), assembler/beacon
  sub-elements visible with icon+module display, no percentage stat text, no
  productivity ticks (that's a Low-only compact indicator — at Medium+ the
  extra-productivity ellipse is drawn directly on the node instead, single
  dot at fixed position).
- **High**: same as Medium plus the Speed/Prod/Power/Quality percentage
  block next to the assembler, and beacon count includes a `Σ` (sigma) total
  when `LevelOfDetail == High` vs a bare multiplier at Medium.

### Draw-mode matrix (`NodeDrawingStyle`)

- `IconsOnly` — every node/link draws as a single centered bitmap icon at
  `graphViewer.IconsDrawSize` (`ItemTabElement`/`AssemblerElement`/
  `BeaconElement`/`ErrorNoticeElement` all skip drawing entirely); used when
  the `IconsOnly` viewer flag is set (a distinct display mode, not LOD- or
  zoom-triggered).
- `Simple` — background rounded-rect + border only, `DetailsDraw` (name
  text, icons, module display) is *not* called at all; triggered
  automatically when `visibleElements > NodeCountForSimpleView` (default
  200) or `ViewScale < 0.2`, i.e. **performance-driven auto-downgrade**, not
  user-selected.
- `Regular` — full detail per current LOD.
- `PrintStyle` — same detail level as `Regular` (`DetailsDraw` called for
  both), used only for `FullGraph=true` (image export); the only difference
  from `Regular` observed in the codebase is that grid/selection/rubber-band
  overlays are suppressed by the `!FullGraph` guards in `Paint()`, not by
  anything style-specific in the elements themselves.

### Passthrough simple-draw mode (distinct from `NodeDrawingStyle.Simple`)

When `PassthroughViewModel.SimpleDraw && RateType==Auto && !KeyNode &&
!IsOverproducing && !ManualRateNotMet && has both links`, the node skips its
box entirely and draws as a colored line (width = max of its two link
widths) directly between its input and output connection points, with two
filled circles as end-caps and a translucent thick overlay line for
highlight — this is the README's "unlabeled stub with one item tab" visual.
Falls back to normal `BaseNodeElement.Draw` otherwise.

## 5. Link rendering

Path type: **cubic Bezier**, always (`Graphics.DrawBeziers`), never a plain
polyline. Three shapes selected by relative node direction
(`BaseLinkElement.UpdateCurve`, lines 58-171):

- **Simple** (same direction, correctly ordered): single 4-point bezier,
  control points pulled straight out from each endpoint by
  `max((Δy)/2, 20)` px along the node's facing direction.
- **UShape** (opposite node directions): a 5-segment bezier chain forming a
  rounded U detour, `circlePull = 100` px "loop" constant, horizontal offset
  capped at `min(200, |Δx|)/2` and signed toward the target.
- **NShape** (same direction but wrong order, e.g. consumer above a
  down-facing supplier): 7-segment bezier chain routing out, across, and
  back — the most complex path, computed `midX` splits the horizontal span
  or projects `1.5×circlePull` past whichever node when they're horizontally
  close.

All three recompute lazily — `UpdateCurve()` only rebuilds points when
supplier/consumer origin or direction actually changed since last call
(cheap dirty-check via field comparison), called both from
`UpdateVisibility` (every frame) and `Draw` (also every frame) — effectively
memoized per-frame.

**Dynamic width**: mapped linearly from `element.ViewModel.Throughput /
maxThroughputForCategory` into `[3, 35]` px, computed once per frame in
`ProductionGraphViewer.Paint` (not in the link element itself) — separate
max tracked for fluids vs items (`§§`-prefixed special items like heat are
excluded from the max calculation). Static width `3` px when
`DynamicLinkWidth` is off.

**Direction arrows**: `pen.CustomEndCap = arrowCap` where `arrowCap = new
AdjustableArrowCap(4, 3)` — only applied when `ArrowsOnLinks &&
!DynamicLinkWidth && !iconOnlyDraw` (arrows and dynamic width are mutually
exclusive display choices; icons-only mode never shows them either).

**Color**: `Item.Item.AverageColor` — same per-item average-color source as
the passthrough simple-draw line, i.e. **link color is entirely
item/fluid-identity-driven**, confirming the UI reference doc's "high
cardinality visual channel" observation. No color depends on link state or
flow health.

**Endpoints**: `LinkElement.GetCurveEndpoints()` reads
`SupplierTab.GetConnectionPoint()` / `ConsumerTab.GetConnectionPoint()`
(the specific `ItemTabElement`, not just the node position) — except when
`iconOnlyDraw`, where it falls back to the raw node `.Location` since tabs
aren't drawn/positioned meaningfully at that zoom level.

## 6. Hit testing + tooltips

**Hit-test routing** (`ProductionGraphViewer.GetNodeAtPoint`, lines
158-170): reverse-order (`topmost first`, matching paint order) linear scan
over `nodeElements`, two-stage — cheap ±50px-expanded rough `Rectangle`
check first, then the real `element.ContainsPoint(graphPoint)` only if the
rough check passes. `GetAnnotationAtPoint` (in `.Annotations.cs`) is the
equivalent for annotations, using `PickAtPoint` (handle-aware when
selected). Mouse handlers (`ProductionGraphViewer_MouseDown/Up/Move`) probe
in a fixed priority: dragged-link-ghost → node-at-point →
annotation-at-point, i.e. **nodes always win over annotations on overlap**.

**`GraphElement.ContainsPoint`** default is a simple `Bounds.Contains(local
point)`; `BaseNodeElement` overrides to additionally test each
`ItemTabElement` and the error badge (so a click on a protruding tab still
resolves to the node); `BaseLinkElement.ContainsPoint` always returns
`false` (links are never directly clickable — no link-specific interaction
exists anywhere in this codebase, P3 or P4).

**Hover mechanics**: no dedicated "hover" event/state machine exists.
Tooltips are recomputed from scratch every paint frame by
`FloatingTooltipRenderer.Paint` calling `GetNodeAtPoint` +
`element.GetToolTips(mouseGraphPoint)` on the current mouse position (via
`Control.MousePosition`/`PointToClient`) — i.e. **tooltips are a pure
function of current mouse position, recomputed per-frame, not
cached/debounced**. This is simple to port but means the Avalonia canvas
needs a `PointerMoved`-driven `Invalidate()` (WinForms gets this for free
via `MouseMove` already calling `Invalidate()` at line 1062) or tooltip text
will lag one frame behind actual hover position.

**`GetToolTips` cascade** (`GraphElement.GetToolTips`, default returns `[]`;
`BaseNodeElement` override, lines 234-248): finds the first sub-element
containing the point, recurses into it, and *prepends* the sub-element's
tooltips before appending the node's own "exclusive help" tooltip only if
the sub-element didn't already produce something. This produces the layered
behavior where hovering an `ItemTabElement` shows the item name (from the
tab) instead of the generic node help text.

**`RecipeToolTip` content**: not a class of its own — `RecipeNodeElement.
GetMyToolTips` builds a `TooltipInfo` with `CustomDraw = (g, offset) =>
RecipePainter.Paint(recipes, g, offset)` and `ScreenSize =
RecipePainter.GetSize(recipes)`, gated by the `ShowRecipeToolTip` viewer
setting. `RecipePainter` (in `Foreman.Controls`, outside
`ProductionGraphView/` but a direct P3 dependency) renders recipe
name/ingredients/products as its own mini GDI+ layout — **this is a
separate small class to also port for P3** (not inventoried above since it
lives in `Controls/`, but flagged here since `RecipeNodeElement` depends on
it directly for the read-only hover tooltip).

**`FloatingTooltipRenderer.Paint`**: draws a filled triangle "arrow" from
the anchor screen point plus a rounded-rect bubble (`getTooltipScreenBounds`
computes bubble position offset by `arrowSize=10` px in the direction away
from the anchor) — either plain text (`DrawString`) or via the `CustomDraw`
callback (used by the recipe tooltip and the selection-stats box). Two call
modes: `paintAll=true` (used by `FullGraph` export — draws every registered
floating-control tooltip regardless of override flag, plus live hover) vs
`paintAll=false` (normal frame — only draws floating controls whose
`showOverride` flag is true, i.e. those meant to always render like pinned
edit panels).

## 7. WinForms/GDI+ dependencies to replace

| Upstream construct | Avalonia/Skia equivalent | Notes |
|---|---|---|
| `UserControl` + `OnPaint(PaintEventArgs)` | Custom `Control` subclass overriding `Render(DrawingContext)` (or a `Panel`-hosted `ICustomDrawOperation` for raw Skia access) | Avalonia repaints reactively; `Invalidate()` → `InvalidateVisual()`. |
| `System.Drawing.Point`/`Rectangle`/`Size` | **Keep `System.Drawing.Primitives`** per the port's existing convention (Core layer already uses these value types for graph coordinates) — do not migrate to `Avalonia.Point`/`PixelRect` for the *model-space* coordinates that flow through `INodeViewModel.Location` etc. | Only the *screen-space* transform/paint call sites need Avalonia types; keep the graph-space math untouched to avoid touching already-ported Core code. |
| `Bitmap` (GDI+) | **Already done** — Core's icon pipeline produces `SKBitmap` (phase 1). Draw calls become `context.DrawImage`/`SKCanvas.DrawBitmap` instead of `Graphics.DrawImage`. |
| `Pen`/`SolidBrush`/`Brushes.X` | `SKPaint` (Style=Stroke/Fill) if rendering via raw Skia, or Avalonia `IPen`/`IBrush` if via `DrawingContext` | Preserve the "cached static, mutate `.Width` per frame for scale-invariant strokes" pattern (§3 point 3) — recreate or reassign width each frame, don't bake scale into a shared cached object incorrectly. |
| `GraphicsPath` (rounded rects via 4 arcs) | `SKPath.AddRoundRect` / Avalonia `RoundedRect` geometry — direct 1:1 simplification, no need to hand-build 4 arcs. |
| `Graphics.DrawBeziers` (cubic chain) | `SKPath.CubicTo` chain, or Avalonia `PathGeometry` with `BezierSegment`s — same point semantics (start, ctrl1, ctrl2, end repeating). |
| `Graphics.TranslateTransform`/`ScaleTransform` (implicit matrix) | `SKCanvas.Translate/Scale` or Avalonia `DrawingContext.PushTransform(Matrix)` — must exactly mirror `ScreenToGraph`/`GraphToScreen`'s manual math (§3 point 1) since hit-testing bypasses the render pipeline. |
| `Graphics.DrawString` + `Graphics.MeasureString(text, font, maxWidth)` word-wrap-aware overload, used by `GraphicsStuff.DrawText`'s auto-shrink-to-fit loop | `SKFont`/`SKPaint.MeasureText` + manual wrap, or Avalonia `FormattedText` | **Metrics parity matters most here.** `DrawText` repeatedly shrinks font size by 0.5pt until the measured text fits the box — this iterative shrink-to-fit is a real algorithm to port faithfully (not just "measure text"), since node name legibility depends on it converging to similar sizes as upstream. Skia/Avalonia font metrics differ slightly from GDI+ (different hinting/kerning tables), so exact pixel parity isn't guaranteed — visually validate against the README screenshots rather than assuming numeric equality. |
| `StringFormat` (`Alignment`/`LineAlignment`) | `SKTextAlign` (horizontal only — Skia has no built-in vertical text-box centering, must compute baseline manually) or Avalonia `FormattedText.TextAlignment` + manual vertical centering | Every node/tab uses `Alignment=Center` — vertical centering logic will need to be written explicitly since it's implicit in GDI+'s `StringFormat.LineAlignment`. |
| `ContextMenuStrip` (WinForms right-click menus) | Avalonia `ContextMenu`/`MenuFlyout` | **P4-only** — none of this is needed for P3's read-only canvas. |
| `Control` hosting for floating panels (`FloatingTooltipControl`) | Avalonia `Popup` or an overlay `Canvas` with absolutely-positioned controls | **P5-panel** — out of scope for P3; the plain-text/custom-draw hover-tooltip path (`FloatingTooltipRenderer` sans `FloatingTooltipControl`) is the only piece P3 needs. |
| `Cursor`/`Cursors.X` | Avalonia `Cursor`/`StandardCursorType` | P4/annotation-resize only; P3 doesn't change cursor. |
| `MouseEventArgs`/`MouseButtons`/`Control.ModifierKeys` | Avalonia `PointerPressedEventArgs`/`PointerEventArgs` + `KeyModifiers` | P3 needs pointer-move (for hover/tooltip) and pointer-press+drag (for pan/zoom) at minimum; full click/drag semantics for node interaction are P4. |
| `AdjustableArrowCap` (line-end arrow shape) | `SKPath`-based custom arrowhead (draw a small triangle at the endpoint oriented along the tangent) — Skia/Avalonia have no built-in adjustable arrow cap. | Needed for `ArrowsOnLinks`, a P3-visible feature. |
| `Properties.Settings.Default.*` (WinForms user-scoped settings) | Whatever settings/persistence mechanism `Foreman.Mac` already uses (check `Services/` in `src/Foreman.Mac`) | Several element static fields read settings directly at class-init time (e.g. `TextAnnotationElement`'s default font/color statics) — these need a working settings source before annotation defaults render correctly, even read-only. |

## 8. Porting sequence for P3

Dependency-ordered; sizes are rough LOC-to-port estimates (source LOC as a
proxy, not 1:1 output LOC — Avalonia/Skia equivalents are often more
verbose for text layout, less verbose for path/transform code).

1. **Coordinate + viewport core** (~150 LOC): `ViewOffset`/`ViewScale`
   fields, `ScreenToGraph`/`GraphToScreen`, `UpdateGraphBounds`,
   mouse-wheel zoom, pan (view-drag subset only, no selection/item-drag).
   Nothing renders without this; do it first and validate with a plain
   filled rectangle before touching any node code.
2. **`GraphElement` base + coordinate conversion** (~100 LOC of the 133,
   excluding `RightClickMenu`/mouse-drag virtuals): `Bounds`, `Location`,
   `GraphToLocal`/`LocalToGraph`, `UpdateVisibility`, the `Paint`/`Draw`
   split, `SubElements` tree.
3. **`GridManager`** (89 LOC, ~full file): standalone, no node dependency,
   good early visual-parity checkpoint (dot/line pattern from the UI
   reference screenshots).
4. **`GraphicsStuff` port** (from `Controls/`, not inventoried above but a
   hard P3 dependency): rounded-rect fill/stroke, the shrink-to-fit
   `DrawText`, number-formatting helpers (`DoubleToString`,
   `DoubleToEnergy`, `BuildingQuantityToText`). Everything downstream calls
   into this.
5. **`BaseNodeElement` read path** (~300 of 481 LOC: `Draw`, `DetailsDraw`
   dispatch, `UpdateState`/`UpdateValues`, `PrePaint`, `ContainsPoint`,
   `GetToolTips` cascade, `UpdateTabOrder`; excluding `Dragged`/
   `MouseUpAction`/`AddRClickMenuOptions`/right-click menu body) +
   `ItemTabElement` (full file, 172 LOC, minus the right-click delete-menu)
   + `ErrorNoticeElement` (full file minus `MouseUp` resolution logic).
6. **Five node subclasses** (~330 LOC total):
   `Supplier`/`Consumer`/`Spoil`/`Plant`/`Passthrough` — each is small and
   self-contained once `BaseNodeElement` works; do these before
   `RecipeNodeElement` since they're simpler validation cases.
7. **`RecipeNodeElement` + `AssemblerElement` + `BeaconElement`** (~635 LOC
   combined, minus `RecipeNodeElement`'s ~280-line paste-options right-click
   menu): the most visually complex node type, save for after the pattern is
   proven on simpler nodes.
8. **`BaseLinkElement` + `LinkElement`** (~305 LOC): bezier path math is
   pure geometry, portable independent of node work once `ItemTabElement`
   connection points exist.
9. **`PointingArrowRenderer`** (99 LOC, full file): screen-space overlay,
   only needs `GraphToScreen` + `Graph.Nodes` — can be done any time after
   step 1.
10. **Annotations read path**: `AnnotationElement` (~150 of 492 LOC: `Draw`
    helpers `GetGraphRect`/`DrawSelectionHighlight`(can no-op, selection is
    P4)/bounds/`ContainsPointFull`/`ForceVisible`, minus all
    drag/resize/handle logic) + `TextAnnotationElement` +
    `ShapeAnnotationElement` (full `Draw`/`ToSaveData`/`FromSaveData` pairs,
    ~360 LOC combined) + `TextAnnotationLayout` (48 LOC, full file) +
    `GraphExportBounds` (69 LOC, full file) + the load-only slice of
    `ProductionGraphViewer.Annotations.cs` (`LoadAnnotationsFromSave`,
    `TryCreateAnnotationFromSave`, `GetAnnotationAtPoint`, `GetExportBounds`
    — ~80 of 448 LOC). Annotations must render because saved graphs contain
    them, even though creating/editing one is P4.
11. **Hover tooltips**: `FloatingTooltipRenderer.Paint`'s plain-text/
    custom-draw path (~60 of 140 LOC, skip the `FloatingTooltipControl`
    floating-panel half) + `RecipePainter` (from `Controls/`, needed for the
    recipe hover tooltip) + pointer-move-driven invalidate wiring.
12. **`ProductionGraphViewer` paint orchestration**: `GetPaintingOrder`,
    `Paint(Graphics, FullGraph)` main method (~120 of the relevant lines),
    `OnPaint` transform setup, `LOD`/`NodeCountForSimpleView`/`IconsOnly`
    style-selection logic, `DynamicLinkWidth` pre-pass. This is the
    integration point — do it last, after every element type it orchestrates
    already renders correctly in isolation (e.g. via a test harness that
    paints one element type directly).

Explicitly **not** in this sequence (P4/P5-panel, deferred):
`DraggedLinkElement`, all `MouseDown`/`MouseUp`/`Dragged` selection and
item-drag logic in `ProductionGraphViewer`, every `RightClickMenu`/
`ContextMenuStrip` body, `NodeCopyOptions`, `AnnotationClipboardCodec`,
`AnnotationSelectionModifiers`, `FloatingTooltipControl`,
`EditPanelScreenLayout`, `EditPanelViewportLayout`, and the annotation
drag/resize/properties-dialog machinery in `AnnotationElement`.
