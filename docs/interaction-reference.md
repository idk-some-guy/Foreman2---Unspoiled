# Interaction reference — ProductionGraphView editing port (phase 4 prep)

Source of truth: `upstream/Foreman/ProductionGraphView/` (same 30 files as
`canvas-reference.md`, this time read for their **P4** halves) plus
`upstream/Foreman/Forms/MainForm.cs`, `ShapePropertiesForm.cs`/`.Designer.cs`,
`TextPropertiesForm.cs`/`.Designer.cs`, and `upstream/Foreman/Graph/GraphAutoconnect.cs`.
Every file below was read in full. Phases 1-3 ported `Foreman.Core` and the
read-only canvas (`src/Foreman.Mac/Canvas/`); `GraphViewer`, `Viewport`, and
hit-testing already exist there, but every mouse-drag, key handler, and
right-click menu inventoried here is still unported — a grep for
`MouseDown|MouseUp|Dragged|RightClick|ContextMenu` across
`src/Foreman.Mac/Canvas/` turns up nothing except a two-line comment in the
ported `BaseNodeElement.cs` saying those members are deferred, and the ported
`AnnotationElement.cs` is 46 lines against upstream's 492.

## 1. `DragOperation` state machine

`ProductionGraphViewer` has a private `enum DragOperation { None, Item,
Selection, DrawShape }` plus a same-scope `viewBeingDragged` bool that is
**not** part of the enum — panning can happen mid-drag regardless of
`currentDragOperation`, tracked separately so a middle-mouse pan doesn't
interrupt whatever item/selection drag is already running.

### Entry conditions

Threshold: `dragDiff² > minDragDiff` (30, squared **screen** pixels off
`mouseDownStartScreenPoint`, captured via `Control.MousePosition` at
`MouseDown` — screen space, so DPI/zoom-independent and unaffected by pan
during the hold). Once tripped, from `None`: Middle/Right button down →
`viewBeingDragged = true`; `MouseDownElement != null && !inDrawShapeMode` →
`Item`; else Left button down → `DrawShape` if `inDrawShapeMode` else
`Selection`.

### `ProductionGraphViewer_MouseDown`

Clears floating tooltips, closes any chooser panel clicked outside of.
Resolves `clickedElement` with fixed priority: dragged-link-ghost first (if
a link-drag is in progress, everything else is ignored), then
`GetNodeAtPoint`, then `GetAnnotationAtPoint` — nodes win over annotations
on overlap. `Annotation_OnMouseDownDoubleClick` runs first and can
short-circuit entirely: a double left-click (`e.Clicks==2`) on an
`AnnotationElement` opens its properties dialog immediately, bypassing
normal routing. `Annotation_OnMouseDown` runs next (re-resolves an
annotation click if nothing else claimed it, forwards `MouseDown`, never
blocks). Then `clickedElement?.MouseDown(...)`. Button-specific tail:
Middle/Right records `ViewDragOriginPoint` (becomes an active pan only once
the threshold trips); Left on a non-annotation seeds
`SelectionZoneOriginPoint`/`SelectionZone`, and if neither Ctrl nor Alt is
held, clears the existing selection **unless** the click landed on a node
already in `selectedNodes` (`keepGroupSelection`) — this is what lets a
group-drag start from a click on any already-selected node without
dropping the rest of the group.

### `ProductionGraphViewer_MouseMove`

Outside `Selection`/`DrawShape`, forwards `MouseMoved` to the dragged-link
ghost (if any) or `MouseDownElement`. `Item`: if the target is a
`BaseNodeElement` in `selectedNodes`, drags only that node through its own
grid/axis-lock logic, diffs its location, then applies the **raw,
unaligned** delta to every other selected node (`SetLocation`) and
annotation (`X`/`Y +=`) — followers preserve relative offset but never
re-snap to grid themselves. An `AnnotationElement` target routes through
`Annotation_OnItemDrag` instead: the mirror image, where nodes in a mixed
selection *do* re-snap via `SetLocation` even when an annotation leads.
`DrawShape`/`Selection` recompute `SelectionZone` from the origin every
frame; `Selection` additionally recomputes `currentSelectionNodes` via
`IntersectsWithZone(zone, -20, -20)` and calls `UpdateSelection()` +
`UpdateAnnotationLassoPreview()` (preview only, not committed). Panning
applies unconditionally after the switch, independent of
`currentDragOperation`.

### `ProductionGraphViewer_MouseUp`

**Right**: stop panning if panning; else on empty space with
`DragOperation.None`, opens the background menu (§4a); else, if not
mid-`Selection`, forwards `MouseUp(wasDragged: state==Item)`. **Middle**:
stop panning. **Left**: `Annotation_FinishDrawShape()` runs first and can
short-circuit (commits the drawn shape, resets to `None`). Then:
`Selection` commits the lasso (Alt = `ExceptWith`, Ctrl = `UnionWith`,
neither = `Clear()`+`UnionWith`), clears `currentSelectionNodes`, and
commits the annotation lasso the same way via
`CommitAnnotationLassoSelection`. `None` with `MouseDownElement` a node (a
plain click, no drag): Alt removes it, Ctrl toggles it, neither forwards a
plain `MouseUp(wasDragged:false)` (opens the edit panel/node menu, §10).
Anything else (`Item`, or annotation click): `Annotation_OnMouseUpLeft`
handles toggle/replace, then forwards `MouseUp(wasDragged:true)` if not
panning. `currentDragOperation` and `MouseDownElement` always reset at the
end of the `Left` case.

### Selection model

Storage: `selectedNodes` (`HashSet<BaseNodeElement>`),
`currentSelectionNodes` (transient lasso preview, cleared every commit),
`selectedAnnotations` (`HashSet<AnnotationElement>`, in the
`.Annotations.cs` partial) — two parallel selection systems, no shared base
collection. Node highlight during lasso drag (`UpdateSelection`): Alt =
remove-zone preview, Ctrl = add-zone preview, neither = replace preview;
actual `selectedNodes` mutation happens only at commit. Annotation lasso is
the identical three-way split via `AnnotationSelectionModifiers` (§5) and
`ApplyAnnotationZoneSelection(zoneAnnotations, commit:bool)` — same
preview/commit split, separate code path. Highlight drawing:
`BaseNodeElement.Highlighted` draws a translucent overlay
(`selectionOverlayBrush`, ARGB `100,100,100,200`) — already P3-ported as
read-only rendering. `AnnotationElement.IsSelected` drives
`DrawSelectionHighlight` (blue rectangle, ARGB `220,80,160,255`) plus the 8
resize handles (§6) — both unported (the P3 `AnnotationElement.cs` has
neither). Left-click on empty space with no modifiers clears both
selections (`ClearAnnotationSelection`).

## 2. Node dragging

- **Drag threshold**: shared with §1's `minDragDiff` — no node-specific
  threshold.
- **Tab-vs-node disambiguation** (`BaseNodeElement.Dragged`): the first
  `Dragged` call after threshold checks whether `MouseDownLocation` landed
  inside one of this node's `ItemTabElement`s. If so, calls
  `graphViewer.StartLinkDrag(...)` instead of moving the node (that's how a
  tab-drag becomes a link drag). Otherwise sets `DragStarted=true` and
  returns without moving — this first call only "arms" the drag, avoiding a
  jump on the threshold frame.
- **Move math** (second+ calls): `offset = graphPoint - MouseDownLocation`;
  `newLocation = Grid.AlignToGrid(MouseDownNodeLocation + offset)` — grid
  snap is unconditional whenever `Grid.ShowGrid` is on, no modifier gate.
- **Shift = axis lock, not grid snap.** `Grid.LockDragToAxis` toggles on
  Shift state on every `KeyDown`/`KeyUp` (via `Control.ModifierKeys`, so it
  also reacts while some other key fires). Toggling re-anchors
  `Grid.DragOrigin` to `AlignToGrid(MouseDownElement.Location)` **at the
  moment of toggling**, not at drag start — tapping Shift mid-drag
  re-anchors the lock to the node's current position. The locked axis is
  decided dynamically per frame by comparing `|dx|` vs `|dy|` from that
  anchor (larger delta wins the free axis, the other snaps to the anchor)
  — overshooting one axis flips which one is locked, mid-drag.
- **Multi-selection group drag**: per §1's `MouseMove` `Item` case, only
  the directly-dragged node gets grid-snap/axis-lock; every other selected
  node/annotation receives the identical raw pixel delta
  (`SetLocation`/`X`/`Y +=`), preserving relative offset even off-grid.
- **Arrow-key movement** (`ProcessCmdKey`, works in any `DragOperation`
  state): `moveUnit = Grid.CurrentGridUnit>0 ? CurrentGridUnit : 6`; Shift
  = large move, `moveUnit = CurrentMajorGridUnit>CurrentGridUnit ?
  CurrentMajorGridUnit : moveUnit*4`. Applies to both `selectedNodes`
  (`SetLocation`, raw — already grid-sized) and `selectedAnnotations`
  (`Annotation_MoveSelection`, same raw dx/dy; annotations are never
  grid-snapped anywhere). **W/A/S/D** live in the same override, panning by
  `panUnit = 10/ViewScale` (Shift = 5x), suppressed while `SubwindowOpen`.

## 3. Link dragging

`DraggedLinkElement : BaseLinkElement` (216 LOC, P4-only).

- **Start**: the tab-hit branch of `BaseNodeElement.Dragged` calls
  `graphViewer.StartLinkDrag(startNode, tab.LinkType, tab.Item)`, disposing
  any prior in-flight drag and constructing a new `DraggedLinkElement`
  bound at the origin node's `SupplierElement`/`ConsumerElement` slot
  (whichever matches the started tab type), and redirects
  `MouseDownElement` to the ghost (all further mouse routing goes through
  it via §1's ghost-first priority).
- **Ghost rendering**: reuses `BaseLinkElement`'s bezier `Draw` (P3-ported).
  `GetCurveEndpoints` returns `null` once `dragEnded` (freezes the last
  curve for one frame before disposal); each bound end reads
  `ItemTabElement.GetConnectionPoint()`, the free end tracks
  `EndpointLocation`, recomputed every frame by `UpdateEndpoint()` from the
  live cursor position, grid-aligned if `Grid.ShowGrid`.
  `UpdateVisibility` is overridden to stay always-visible.
- **Valid-target resolution** (`MouseMoved`, every move frame, not
  debounced): `GetNodeAtPoint` under the cursor; if the hovered node's
  `Outputs`/`Inputs` contain the dragged item (matching the free-end
  direction), tentatively bind the free slot. If both ends end up bound,
  `LinkChecker.IsPossibleConnection` (temperature range etc.) is checked,
  unbinding the just-set end on failure. No node under the cursor unbinds
  the free end entirely.
- **Completion**: both `MouseDown(Left)` and `MouseUp(Left)` call the same
  `EndDrag` (covers both release-to-drop and click-to-drop styles), which
  sets `dragEnded=true` then: both ends bound →
  `Session.Editor.CreateLink(...)` and disposes the drag; no full bind but
  slave sub-links exist (multi-passthrough, below) →
  `graphViewer.AddPassthroughNodesFromSelection(...)`; exactly one end
  bound, dropped on empty space → `graphViewer.AddNewNode(...)`, opening
  the P5-panel chooser (§10) pre-wired to the bound node.
- **Cancel**: `MouseDown(Right)` on the ghost calls
  `graphViewer.DisposeLinkDrag()` — no node created, no confirmation.
- **Ctrl+drag multi-passthrough bus** (`UpdateSlaveLinks`, every `PrePaint`
  while unresolved): fires only when Ctrl is held, no slave links exist
  yet, the origin is a `PassthroughNodeElement`, more than one node is
  selected including the origin, and **every** selected node is a
  `PassthroughNodeElement` — i.e. only when dragging from a passthrough tab
  inside an all-passthrough multi-selection. Spawns one child
  `DraggedLinkElement` per *other* selected passthrough as a slave link, so
  the drag fans out from every selected passthrough at once (the README's
  multi-passthrough "bus" build). Releasing Ctrl before drop disposes all
  slaves immediately; each slave's free end tracks the master rigidly:
  `anchor.Location + (masterEndpoint - masterOrigin.Location)`.
- **Bus drop**: with slaves present, `EndDrag` routes to
  `AddPassthroughNodesFromSelection(StartConnectionType, offset)`, creating
  one new passthrough node per selected passthrough (mirroring the
  master's placement offset), each auto-linked to its own source
  passthrough, then replaces the selection with the new nodes.

## 4. Right-click menu inventory

`GraphElement.RightClickMenu` is a shared base `ContextMenuStrip`
(`ShowItemToolTips=false`, `ShowImageMargin=false`) whose `Closing` handler
cancels the close on `ItemClicked` (items call `.Close()` themselves inside
their handler) and otherwise clears `Items` + resets `ShowCheckMargin`. Every
menu below is rebuilt from scratch on each right-click, not cached.

### a) Viewer background (right-click empty space, `DragOperation.None`, no element under cursor)

| Caption | Enabled | Action |
|---|---|---|
| Add Item | always | `AddItem(screenPoint, graphPoint)` — opens item chooser (P5) |
| Add Recipe | always | `AddNewNode(..., NewNodeType.Disconnected)` — opens recipe chooser (P5) |
| *(separator)* | | |
| Add Text | always | `AddTextAnnotation(graphPoint)` (§6) |
| Add Shape | always | `AddShapeAnnotation(graphPoint)` (§6) |

### b) `BaseNodeElement.MouseUpAction` (right click, non-dragged)

Built in this exact order:

| Caption | Enabled / shown when | Action |
|---|---|---|
| Delete node | always | `Session.Editor.DeleteNode(this)` |
| Delete selected nodes | `SelectedNodes.Count>1 && contains(this)` | `TryDeleteSelectedNodes()` (confirms if >10) |
| *(separator)* | | |
| Flip node | always | toggle `NodeDirection` |
| Flip selected nodes | `SelectedNodes.Count>1 && contains(this)` | `FlipSelectedNodes()` |
| *(separator)* + Clear selection | `SelectedNodes.Count>0` | `ClearSelection()` |
| *(separator)* + Auto-connect disconnected inputs | selection has an open-input/available-output item match | ad hoc greedy nearest-supplier-by-Manhattan-distance, scoped to selection only |
| Auto-connect disconnected outputs | selection has an open-output/available-input item match | mirror image, scoped to selection only |
| *(`AddRClickMenuOptions`, virtual — base no-op, `RecipeNodeElement` overrides, see §4c)* | | |
| *(separator)* + Copy key node status | always | `Clipboard.SetText(WriteKeyNodeClipboardToString(...))` |
| Paste key node status | selection empty or contains this, **and** clipboard parses as key-node data | applies `KeyNode`/`KeyNodeTitle` to this node (empty selection) or every selected node |

The two inline auto-connect menu items reimplement a near-duplicate of
`GraphAutoconnect.ConnectDisconnectedInputs` (§8) scoped to the current
selection instead of the whole graph — a consolidation candidate for the
port rather than a verbatim re-port.

### c) `RecipeNodeElement.AddRClickMenuOptions(nodeInSelection)` — the ~280-line body

If `nodeInSelection` (target set = selection ∪ this, else just a lone
separator is added and the block below is skipped):

1. `Apply default assembler(s)` — `AutoSetAssembler()` per target node.
2. `Apply default modules` — `AutoSetAssemblerModules()`.
3. `Remove modules` — shown only if any target has `AssemblerModules.Count>0`.
4. `Remove beacons` — shown only if any target has a `SelectedBeacon`.
5. Separator, then a **paste-options block**, built only if `DCache` is
   available and the clipboard text parses as a `NodeCopyOptions` with a
   valid `Assembler`. Up to 7 checkable items (`Tag="CheckBox"`,
   `CheckOnClick=true`), each gated by a per-field "can paste" predicate
   evaluated against every target node (recipe/assembler compatibility,
   `EntityType` checks): the pasted assembler's type name, `Bonus
   Productivity (Miners)`, `Bonus Productivity (non-Miners)` (only if
   `Graph.EnableExtraProductivityForNonMiners`), `Fuel`, `Modules`,
   `Beacon`, `Beacon Modules`. Each checkbox's initial `Checked` state
   comes from a **static, class-level bool** (`OptionsCopyAssemblerDefault`
   etc.) that remembers the last confirmed choice for the whole session —
   these are process-lifetime UI state, not per-node state. Items only
   appear if their predicate is true; if none are true, no paste block (and
   no "Paste selected options" item) appears at all.
6. `Paste selected options` (only if step 5 added at least one checkbox) —
   on click, writes the checkboxes' final `Checked` values back into the
   static defaults, then applies each checked field to every target node
   through `RecipeNodeController` (`SetAssembler` + `SetNeighbourCount` for
   `Reactor`, `SetExtraProductivityBonus`, `SetFuel`,
   `SetAssemblerModules`, `SetBeacon`+`SetBeaconCount`+`SetBeaconsCont`+
   `SetBeaconsPerAssembler`, `SetBeaconModules`), each re-guarded by the
   same compatibility checks used to decide "can paste."

Always present regardless of selection: `Copy this assembler's options` —
`Clipboard.SetText(WriteNodeCopyOptionsToString(new NodeCopyOptions(this)))`.

### d) `ItemTabElement.MouseUp` (right click on an input/output tab)

Single item: `Delete connections` — enabled only if `connections.Count>0`
(every link on this node for this item+direction); deletes all of them.

### e) `ErrorNoticeElement.MouseUp` (right click on the warning/error badge)

Entirely data-driven, no static captions: one menu item per entry in
`BaseNodeController.GetErrorResolutions()`/`GetWarningResolutions()` (a
`Dictionary<string, Action>` keyed by description), shown only if that
dictionary is non-empty — otherwise no menu opens at all. Left-click on the
same badge instead runs **every** resolution at once (auto-resolve) with no
menu.

### f) `AnnotationElement.MouseUp` (right click, non-dragged)

| Caption | Condition | Action |
|---|---|---|
| Properties | always | `ShowPropertiesDialog()` (§6) |
| *(separator)* | | |
| Delete selection | this annotation is selected **and** (other nodes or other annotations are also selected) | `graphViewer.TryDeleteSelection()` |
| Delete | otherwise | `graphViewer.RemoveAnnotationElement(this)` |

## 5. Clipboard

- **`NodeCopyOptions`** (157 LOC): plain data holder for one recipe node's
  build config — `Assembler`(+quality), `AssemblerModules`, `Fuel`,
  `NeighbourCount` (reactor), `ExtraProductivityBonus`, `Beacon`(+quality),
  `BeaconModules`, `BeaconCount`, `BeaconsPerAssembler`, `BeaconsConst`.
  Constructible from an `IRecipeNodeViewModel` or a `RecipeNode`, or resolved
  back from a `NodeCopyOptionsSaveDocument` via `FromSaveDocument` (falls
  back to `defaultQuality` for unresolvable quality names, silently drops
  modules that don't resolve). `GetNodeCopyOptions(string, DataCache)` wraps
  parse+resolve and swallows exceptions to `null` — used directly by the
  paste-options menu gate in §4c.
- **Node copy/cut/paste is keyboard-only** — no menu items exist for it.
  `ProductionGraphViewer_KeyDown` handles Ctrl+C/Ctrl+X together: sets
  `Graph.SerializeNodeIdSet` to the selected node ids, serializes the
  **whole graph** through `GraphSaveCodec.WriteProductionGraphToString`
  filtered by that set (the clipboard fragment reuses the full-graph save
  format scoped down, not a bespoke node-list format), clears
  `SerializeNodeIdSet`, then if any annotations are selected splices them in
  via `AnnotationClipboardCodec.MergeAnnotationsIntoFragment`. Ctrl+X
  additionally deletes the selection after copying. Ctrl+V calls
  `ImportNodesFromFragment(Clipboard.GetText(), ScreenToGraph(cursor),
  applySolverSettings:false)` inside a try/catch that silently swallows
  non-Foreman or malformed JSON (logs only, no user-facing error).
- **Fragment paste pipeline**: `GraphSaveCodec.ReadGraphPayload(json)`
  (already in `Foreman.Core`, confirmed present) →
  `ImportNodesFromDocument` → `Graph.InsertNodesFromDocument` → computes
  the imported nodes' centroid, offsets every new node by the grid-aligned
  delta to `origin` (cursor position for Ctrl+V; explicit viewport center
  for `MainForm`'s Import Graph menu action), replaces the current selection
  with exactly the newly pasted nodes. Independently,
  `AnnotationClipboardCodec.ReadAnnotations(json)` extracts any
  `"Annotations"` array and `ImportAnnotationsAtOrigin` centers+selects
  those the same way — a fragment with only nodes, only annotations, or
  both all paste correctly since the two importers don't depend on each
  other.
- **`AnnotationClipboardCodec`** (35 LOC): `ReadAnnotations(json)` parses
  and looks for `"Annotations"`, returning `null` on any JSON error (a safe
  no-op, no exception surfaces to the caller).
  `MergeAnnotationsIntoFragment(baseJson, annotations)` re-parses the base
  fragment as a `JsonObject` and sets its `"Annotations"` property,
  returning the input **unchanged** if the base isn't a valid JSON object
  (defensive, never throws).
- **`AnnotationSelectionModifiers`** (14 LOC): three static bools —
  `IsRemoveFromSelection` (Alt), `IsAddToSelection` (Ctrl),
  `IsReplaceSelection` (neither) — the canonical modifier vocabulary
  annotation code uses everywhere instead of re-testing
  `Control.ModifierKeys` inline the way the node-selection code still does.
  Worth standardizing the node side onto this same helper during the port.
- **Porting gap found**: `GraphSaveCodec.WriteNodeCopyOptionsToString`
  (called from §4c's "Copy this assembler's options") does **not** exist
  yet in `src/Foreman.Core/Serialization/GraphSaveCodec.cs` — only the read
  path (`ReadNodeCopyOptions`) and the lower-level
  `GraphSaveJson.SerializeNodeCopyOptions` are present. A thin
  `WriteNodeCopyOptionsToString` wrapper needs adding to Core before the
  paste-options menu can be finished.

## 6. Annotation editing

- **Creation is only reachable from the background right-click menu** (§4a,
  "Add Text"/"Add Shape") — no toolbar button, no keyboard shortcut.
  `AddTextAnnotation(point)` constructs a `TextAnnotationElement` at
  `point`, selected, then immediately opens `TextPropertiesForm` modally:
  OK clears the rest of the selection but keeps this annotation selected;
  **Cancel deletes the just-created element outright** (no artifact left
  behind). `AddShapeAnnotation(point)` does not create an element yet —
  arms `inDrawShapeMode=true`, `Cursor=Cross`, seeds
  `SelectionZoneOriginPoint`; the next left-drag becomes
  `DragOperation.DrawShape` (§1), and `Annotation_FinishDrawShape()` on
  `MouseUp` builds the `ShapeAnnotationElement` from the drawn rectangle
  (30px minimum per axis) or a default 200x150 shape centered at the click
  if the drag was too small. Escape while `inDrawShapeMode` cancels without
  creating anything.
- **8-handle resize**, implemented once in `AnnotationElement`, shared by
  both subclasses. `HandleType`: the 8 compass positions, hit-testable only
  when `IsSelected`. Two independently scaled sizes: draw half-size (~5
  screen px, clamped 3..min(W,H)/5) and a larger hit half-size (~10 screen
  px, clamped 5..min(W,H)/4) so the clickable zone exceeds the visible
  square. `ApplyResize` recomputes the 4 edges from a drag-start snapshot
  plus the active handle's delta (corners move 2 edges, edge-centers move
  1), clamps to a 30-graph-unit minimum per axis, derives new
  `X`/`Y`/`Width`/`Height`, then calls virtual `OnResized()` — only
  `TextAnnotationElement` overrides it, rescaling font size via
  `TextAnnotationLayout.ComputeResizeFontSize`; `ShapeAnnotationElement`
  just stretches.
- **Drag (move)**: `AnnotationElement.Dragged` swallows its first
  post-threshold call (same arm-without-jump pattern as
  `BaseNodeElement.Dragged`'s tab check), then moves by raw offset from the
  drag-start snapshot — annotations are never grid-snapped. Group behavior
  lives in the viewer's `Annotation_OnItemDrag`: dragging a selected
  annotation applies the resulting delta to every other selected annotation
  (raw) and to every selected node (`SetLocation`, so nodes in a mixed
  selection *do* re-snap to grid even when an annotation leads — the
  asymmetric counterpart to §2's node-leads case).
- **Properties dialogs** — both modal `Form`s, `ShowDialog(graphViewer.
  FindForm())`. Every field change applies live (`RebuildGdiObjects()` +
  `Invalidate()`); Cancel reverts from a snapshotted original-value set; OK
  calls `SaveDefaults(element)`, persisting current values as the new
  class-level statics and to `Properties.Settings.Default` so the next new
  annotation starts from the last-confirmed values.
  - **`ShapePropertiesForm`** fields: `ShapeTypeCombo` (Rectangle/Ellipse),
    `NoFillCheckBox`, `FillColorButton`(+`ColorDialog`),
    `FillAlphaTrack`(0-255)+`FillAlphaLabel`,
    `BorderColorButton`(+`ColorDialog`), `BorderWidthInput`
    (`NumericUpDown`), `OKButton`, `CancelButton`.
  - **`TextPropertiesForm`** fields: `TextInput` (live-rebuilds text + refits
    box per keystroke), `FontButton`(+`FontDialog`, `ShowColor=false`),
    `FontPreviewLabel`, `AlignLeftRadio`/`AlignCenterRadio`/`AlignRightRadio`
    (Near/Center/Far), `TextColorButton`(+`ColorDialog`),
    `TransparentCheckBox` (forces `Color.Transparent`, disables
    `BackColorButton`), `BackColorButton`(+`ColorDialog`), `OKButton`,
    `CancelButton`; text field auto-focuses and select-alls on `Shown`.
  - Neither dialog is `FloatingTooltipControl`-hosted (unlike P5 edit
    panels) — plain modal `Form`s needing a genuine Avalonia window, not
    the floating-panel machinery in §10.
- **`AnnotationElement` P4 members** not yet in the 46-line ported version
  (per `canvas-reference.md` §1's tagging): `IsSelected` and its
  selection-driven branching in `ContainsPoint`; `_dragStart*`/
  `_activeHandle` resize tracking; the `HandleType` enum and
  `GetHandleAtPoint`/`GetHandleRect`/`GetHandleDrawHalfSize`/
  `GetHandleHitHalfSize`; `ApplyResize` + `OnResized`; `MouseDown`/
  `MouseUp`/`Dragged`/`CancelMouseCapture` overrides;
  `DrawSelectionHighlight`/`DrawResizeHandles` (currently no-op'd);
  `LassoIntersectsEdge`; `PickAtPoint`; `GetCursorForPoint`.
  `ShowPropertiesDialog` (abstract) and `FromSaveData` already exist for P3
  load, but the `RightClickMenu`-building half of `MouseUp` is P4-only.

## 7. Keyboard map

| Key | Modifier | Gated by | Behavior |
|---|---|---|---|
| C | Ctrl | `DragOperation.None` | Copy selection to clipboard (§5) |
| X | Ctrl | `DragOperation.None` | Copy + delete selection |
| V | Ctrl | `DragOperation.None` | Paste clipboard fragment at cursor |
| Delete | | `DragOperation.None` (`KeyUp`) | Delete annotations if any selected, else delete selected nodes (both confirm if >10) |
| ← → ↑ ↓ | (+ Shift = large step) | any state (`ProcessCmdKey`) | Move selected nodes + annotations by grid unit (or major-grid/4x with Shift) |
| W A S D | (+ Shift = 5x speed) | `!SubwindowOpen` (`ProcessCmdKey`) | Pan the view |
| Shift | held | any state | Live-toggles `Grid.LockDragToAxis`, re-anchoring `DragOrigin` at the node's current position on every toggle (§2) |
| Escape | | `inDrawShapeMode` only | Cancels the pending shape draw; no other Escape handling exists anywhere (no cancel-link-drag Escape — that's right-click only; no deselect-all Escape) |
| Space | | `MainForm.GraphViewer_KeyDown`, not the canvas control | Toggles `Grid.ShowGrid` and syncs the toolbar checkbox |
| S | Ctrl | `MainForm_KeyDown`, not the canvas control | Save (or Save As if untitled) |

Two handlers live outside `ProductionGraphViewer` entirely (Space, Ctrl+S) —
both are on `MainForm`, so wherever the Mac port's menu/command wiring ends
up living needs to own them too, not just the canvas control.

**Cmd-mapping**: `canvas-reference.md` doesn't record an explicit
Mac-adapted decision for this, so treat the following as the natural
default rather than a settled call: Ctrl → Cmd is the obvious mapping for
copy/cut/paste/save. Ctrl and Alt must **not** globally remap, though —
Ctrl is also "add to selection" and Alt is "remove from selection" across
the entire lasso/click-toggle system (§1, §5's
`AnnotationSelectionModifiers`), which are spatial-selection idioms tied to
the platform's Ctrl/Option keys, not command-key idioms. Only the
clipboard/save shortcuts should remap; a blanket Ctrl→Cmd sweep would break
selection modifiers.

This paragraph, and the table above it, predate implementation and are
superseded by the actual decision the port shipped: Cmd replaces Ctrl
everywhere this section describes (selection add/toggle, Cmd+C/X/V), with
Alt staying Alt, exactly as anticipated above. See
`docs/upstream-divergences.md`'s "Phase 4 lasso and drag modifiers" entry
for the settled statement; Cmd+S stays unwired pending `ShellCommands.Save`
(see that same file's Task 7 keyboard-map entry).

## 8. Node auto-placement

- **`SmartNodeDirection`** (viewer bool, set from
  `Properties.Settings.Default.SmartNodeDirection` in `MainForm_Load`):
  in `AddNewNode`, when an `originElement` exists, direction resolves as:
  if the setting is off or there's no origin → `Graph.DefaultNodeDirection`
  (the global fallback); else if a link drag is actively in progress →
  mirrors or flips the origin's direction depending on the dragged link's
  shape (`UShape` flips Up↔Down, `Simple`/`NShape` keep the same
  direction) — the same branch is duplicated verbatim in
  `AddPassthroughNodesFromSelection` for the bus-creation case (§3). The
  intent is that a newly connected node visually continues whatever flow
  direction it's being linked from.
- **`Graph.DefaultNodeDirection`**: the plain global default (Up/Down),
  read once from settings at load, used whenever `SmartNodeDirection` is
  off or there's no origin to infer from (e.g. a disconnected Add Recipe).
- **Autoconnect button** (`MainForm.AutoconnectButton_Click` →
  `GraphViewer.AutoconnectDisconnectedInputs()` →
  `GraphAutoconnect.ConnectDisconnectedInputs(Session)`): operates on the
  **whole graph**, every node, not just the selection — one greedy pass,
  nearest supplier by Manhattan distance per open input, no fixpoint
  iteration beyond that single pass. **Already ported**: `diff` between
  `upstream/Foreman/Graph/GraphAutoconnect.cs` and
  `src/Foreman.Core/Graph/GraphAutoconnect.cs` shows **zero differences** —
  byte-identical. P4 only needs to wire the button/command, not port the
  algorithm.
- The two inline right-click "Auto-connect disconnected inputs/outputs"
  items on `BaseNodeElement` (§4b) reimplement a near-identical greedy
  algorithm ad hoc, scoped to the current node selection instead of the
  whole graph — a consolidation candidate onto `GraphAutoconnect` (or a
  selection-scoped overload of it) rather than a second hand-rolled version.

## 9. Undo

**Finding: there is no undo/redo system anywhere in upstream Foreman.** A
search across `ProductionGraphView/` and `MainForm.cs` for
`undo|redo|history` (case-insensitive) turns up nothing but incidental
substring noise in unrelated identifiers — no undo stack, no command
pattern, no Ctrl+Z handling. Every edit (drag, delete, paste, right-click
action) mutates the graph directly and irreversibly through
`Session.Editor`. The closest things to "undo" that exist are: modal
dialogs' Cancel buttons, which revert from a snapshotted original-value set
(§6) rather than any general mechanism, and the clipboard's
copy-before-delete pattern (Ctrl+X). P4 should not introduce an undo system
as part of this port unless the Mac port explicitly adopts it as new scope
beyond parity.

## 10. What P4 does not include (P5-panel touchpoints to stub)

Every one of these is a floating WinForms-hosted panel out of scope for
both P3 and P4 per `canvas-reference.md`'s tagging, but P4 needs a
placeholder at each exact call site so the interaction it's attached to is
demoable rather than a dead end:

- **`BaseNodeElement.MouseUpAction`** (left click, non-dragged) →
  `graphViewer.EditNode(this)` → `EditFlowPanel` for simple nodes, or
  `EditRecipeNode` → `EditRecipePanel` + `RecipePanel` (side-by-side or
  stacked depending on `LockedRecipeEditPanelPosition`) for recipe nodes.
  **Stub point**: left-click-to-select must still work; the panel-open call
  needs a no-op or placeholder until P5.
- **`ProductionGraphViewer.AddItem`/`AddNewNode`** → `ItemChooserPanel` /
  `RecipeChooserPanel`. Reached from: background right-click Add
  Item/Add Recipe (§4a), `DraggedLinkElement.EndDrag`'s new-node outcome
  (§3), `MainForm`'s toolbar Add Item/Add Recipe buttons, and the
  Spoil/Plant multi-origin item-selection sub-flows nested inside
  `AddNewNode`. **Stub point**: every one of these needs at least a minimal
  synchronous picker, or the "drag a tab into empty space" and "right-click
  add" flows have nothing to attach to.
- **Annotation properties dialogs** (`ShapePropertiesForm`/
  `TextPropertiesForm`, §6) are plain modal `Form`s, not
  `FloatingTooltipControl`-hosted, but still need a genuine Avalonia window
  to replace them. Not tagged either P4 or P5 by `canvas-reference.md`;
  flagging here since annotation creation is otherwise pure P4 and is
  incomplete without them — worth an explicit phase-4 planning call rather
  than letting it silently slip.
- **Not P5-gated, fully P4**: `ErrorNoticeElement`'s resolutions (§4e) are
  inline `Action` delegates from `BaseNodeController`, no panel involved.
- `FloatingTooltipControl`/`EditPanelScreenLayout`/`EditPanelViewportLayout`
  are the shared plumbing every panel above rides on — none of it exists in
  the Mac port yet, and none of it should be built for P4 (real P5 scope).

## 11. Dependency-ordered porting sequence for P4

Mirrors `canvas-reference.md` §8's style — sizes are rough source-LOC
estimates, not 1:1 output LOC.

1. **Selection model + `DragOperation` core** (~250 LOC): the enum, the
   `MouseDownElement`/`downButtons`/threshold logic across
   `MouseDown`/`Move`/`Up`, `selectedNodes`/`currentSelectionNodes`/
   `SetSelection`/`UpdateSelection`/`ClearSelection`, lasso rectangle
   draw+intersect. No node-specific behavior yet — validate with
   click-to-select and lasso-select against already-rendered P3 nodes
   before touching anything else.
2. **Node dragging** (~150 LOC): `BaseNodeElement.MouseDown`/`Dragged`
   (tab-vs-node disambiguation, `DragStarted`), `SetLocation`,
   `Grid.LockDragToAxis`/`DragOrigin` wiring, group-drag delta application,
   arrow-key movement via `ProcessCmdKey`. Depends on step 1's
   `DragOperation` plumbing.
3. **Right-click menu infrastructure** (~100 LOC): `GraphElement.
   RightClickMenu`'s construction/closing pattern ported to Avalonia
   `ContextMenu`/`MenuFlyout`, plus `BaseNodeElement.MouseUpAction`'s
   always-present items (Delete/Flip/Clear selection/Copy+Paste key node
   status). Prove the pattern small before step 6's giant menu.
4. **`ItemTabElement` + `ErrorNoticeElement` P4 halves** (~60 LOC): tab
   right-click delete-connection, badge left-click-autoresolve + right-click
   resolution menu. Small, self-contained, a good second menu-pattern
   validation alongside step 3.
5. **Link dragging** (~250 LOC): `DraggedLinkElement` in full
   (`StartLinkDrag`/`DisposeLinkDrag`, `MouseDown`/`MouseUp`/`MouseMoved`,
   `EndDrag`'s three outcomes — two of them need §10's chooser-panel
   placeholder), `UpdateSlaveLinks`'s Ctrl+multi-passthrough fan-out,
   `UpdateEndpoint`. Depends on steps 1-2 (needs `MouseDownElement`
   redirection + grid alignment) and a placeholder for the new-node
   chooser.
6. **`RecipeNodeElement`'s paste-options menu** (~280 LOC): the big
   `AddRClickMenuOptions` body — last among menu work since it depends on
   `NodeCopyOptions` (step 7) and is the single most construction-heavy
   piece.
7. **Clipboard** (~250 LOC): `NodeCopyOptions` + `FromSaveDocument`
   resolution, Ctrl+C/X/V handlers, `ImportNodesFromFragment`/
   `ImportNodesFromDocument` paste-centering math,
   `AnnotationClipboardCodec` (trivial once annotations exist, step 9).
   **Before starting**: add the missing `GraphSaveCodec.
   WriteNodeCopyOptionsToString` wrapper to `Foreman.Core` (§5 finding) —
   the read side and the lower-level serializer already exist, only the
   public write wrapper is missing.
8. **Annotation creation + drag + resize** (~450 of 492 LOC, since P3
   already ported 46): `HandleType` + resize math, `Dragged` move logic,
   `MouseDown`/`MouseUp` incl. right-click menu,
   `AnnotationSelectionModifiers`-based selection (mirrors step 1's node
   selection but keep them as two parallel systems matching upstream — do
   not prematurely unify), the `Annotation_*` viewer-side glue in
   `ProductionGraphViewer.Annotations.cs` (~370 of 448 LOC not already P3),
   `AddShapeAnnotation`'s `DrawShape` rubber-band. Depends on step 1
   (shares `DragOperation`/lasso machinery) and wants step 10's dialogs at
   least stubbed for creation to feel complete.
9. **Annotation clipboard integration** (~50 LOC): wires steps 7 and 8
   together (`AnnotationClipboardCodec` merge/read,
   `ImportAnnotationsAtOrigin`/`ImportAnnotationsWithOffset`) — trivial once
   both sides exist.
10. **Properties dialogs** (~250 LOC combined, a real scope decision):
    `ShapePropertiesForm`/`TextPropertiesForm` as genuine Avalonia windows,
    every field from §6, live-preview-on-change + Cancel-reverts-snapshot +
    OK-saves-as-default. The one piece of "P4 that's really P5-shaped" per
    §10 — schedule it explicitly rather than letting it silently slip.
11. **Auto-placement polish** (~40 LOC): `SmartNodeDirection` branch in
    `AddNewNode`/`AddPassthroughNodesFromSelection`, Autoconnect toolbar
    wiring (algorithm already ported per §8 — just the button/command and,
    ideally, consolidating §4b's two inline menu items onto it instead of
    re-porting the duplicate).
12. **Add Item/Add Recipe P5-stub wiring** (~100 LOC, placeholder scope):
    background right-click Add Item/Add Recipe, `DraggedLinkElement`'s
    new-node `EndDrag` path, `MainForm` toolbar buttons — wire to a minimal
    placeholder chooser (even a simple list dialog) so P4 is demoable
    end-to-end without waiting on P5's real chooser panels. Last, since
    it's throwaway scaffolding rather than real product surface.
