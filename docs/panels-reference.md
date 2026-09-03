# Panels reference — chooser/edit panels + dialogs (phase 5 prep)

Source of truth: `upstream/Foreman/Controls/` (20 files, ~5674 LOC), `upstream/Foreman/Forms/{SettingsForm,GraphSummaryForm,PresetComparatorForm}.cs(+.Designer.cs)` (6 files, ~5633 LOC), `upstream/Foreman/ProductionGraphView/{FloatingTooltipControl,FloatingTooltipRenderer,EditPanelScreenLayout,EditPanelViewportLayout}.cs` (4 files, ~308 LOC), and the call sites in `ProductionGraphViewer.cs`/`MainForm.cs`. Every file was read in full. Phases 1-4 ported `Foreman.Core`, the Avalonia shell, the read-only canvas, and all editing interactions; a P5-placeholder chooser (`Views/PlaceholderChooserWindow`) stands in for the real thing. `docs/ui-reference.md` documents panel visuals from README screenshots — its own caveat (§ "Cross-check", item 6) is that the Settings/Graph-Options screenshot shows fewer widgets than the Designer file actually has, so this document treats `SettingsForm.Designer.cs` as authoritative wherever the two disagree.

Tags: **canvas-floating-panel** = hosted as a WinForms child control layered directly over `ProductionGraphViewer`'s canvas (not a separate OS window). **Avalonia-window** = the port's equivalent of a genuine separate top-level window (modal or not). See §9 for which each upstream class should become.

## 1. Class inventory

### Controls/ (20 files, ~5674 LOC)

| Class | File(s) : LOC | Responsibility | Upstream hosting |
|---|---|---|---|
| `MouseHoverDetector` | MouseHoverDetector.cs 116 | Per-control hover-start/hover-end timer (200ms hover delay, 200ms reshow grace, 15px move-tolerance before "ended"), since native `MouseHover` doesn't fire repeatedly | utility, not hosted — used by `SettingsForm`'s Recipes `ListView` to drive `RecipeToolTip` and by the chooser icon grid's hover captions |
| `ControlExtensions` (static) | ControlExtensions.cs 32 | Three `UIThread`/`UIThreadInvoke`/`InvokeOnUiThreadAsync` extension methods for marshaling a callback onto a `Control`'s UI thread | utility, not hosted; **Avalonia has no equivalent need** — `Dispatcher.UIThread` already covers this, so this class has no port target |
| `GraphicsStuff` (static) | GraphicsStuff.cs ~112 of 260 | Generic GDI+ drawing helpers: `DrawText` (auto-shrink font to fit a box), `DrawRoundRect`, and similar primitives | utility; **already superseded** by the SkiaSharp equivalents phase 3 built directly into the canvas element `Draw` methods, not a distinct porting target |
| `MathDecimals` (static) | GraphicsStuff.cs ~6 of 260 | Tiny decimal-rounding helper | utility |
| `IRChooserPanel` (abstract) | IRChooserPanel.cs ~415 of 908 | Base item/recipe chooser: group buttons, filter row, icon-grid population, panel lifecycle (open/close/click-outside) | **canvas-floating-panel** — added directly to `ProductionGraphViewer.Controls`, self-positions via its own `Ui.cs` layout code (not wrapped in `FloatingTooltipControl`) |
| `ItemChooserPanel` | IRChooserPanel.cs ~135 | Item-picking chooser subclass; fires `ItemRequested`, always closes on pick | canvas-floating-panel (same as base) |
| `RecipeChooserPanel` | IRChooserPanel.cs ~278 | Recipe-picking chooser subclass + footer "alt node" buttons (Source/Pass-Through/Output/Spoil/UnSpoil/Plant/UnPlant); fires `RecipeRequested` | canvas-floating-panel (same as base) |
| `NFButton` | IRChooserPanel.cs ~35 | `Button` subclass that renders a true grayscale copy of its icon when `Enabled=false` | leaf control, not independently hosted |
| IRChooserPanel.Ui.cs | 821 | Other half of the `IRChooserPanel` partial class: DPI scaling, dynamic sizing against the viewport, footer-row layout, binary-search height fitting | n/a (layout code, no new type) |
| IRChooserPanel.Designer.cs | 490 | WinForms designer tree: control names/captions/sizes for the whole chooser family | n/a |
| `ChooserIconGrid` | ChooserIconGrid.cs 165 + .Designer.cs 60 = 225 | The fixed 10×8 icon-button subgrid + `VScrollBar` | embedded inside `IRChooserPanel` |
| `ChooserLayout` (static) | ChooserLayout.cs 61 | 96-DPI design-metric constants (cell/group icon sizes, scrollbar width) + scaling helpers | utility, not hosted |
| `EditRecipePanel` | EditRecipePanel.cs 748 + .Designer.cs 1382 = 2130 | Recipe-node editor: assembler/module/beacon/fuel pickers, rate, neighbours, extra productivity | **canvas-floating-panel** — wrapped in `FloatingTooltipControl`, paired with a companion `RecipePanel` |
| EditRecipePanel.Viewport.cs | 33 | Layout-only partial: clamps the panel's size to the viewer's client bounds on resize via `EditPanelViewportLayout.Apply` | n/a |
| `EditFlowPanel` | EditFlowPanel.cs 104 + .Designer.cs 205 = 309 | Non-recipe node editor: rate value, fixed/auto toggle, key-node title, simple-passthrough toggle | **canvas-floating-panel** — wrapped in `FloatingTooltipControl`, standalone (no companion panel) |
| EditFlowPanel.Viewport.cs | 31 | Same resize-clamp pattern as EditRecipePanel.Viewport.cs, applied to `RateOptionsTable` | n/a |
| `CustomProgressBar` | CustomProgressBar.cs 41 | `ProgressBar` subclass that custom-paints percent + `CustomText` over the native chunk | embedded in modal load/solve progress dialogs, not canvas-floating |
| `SyncListView`/`FFListView` | SyncListView.cs 54 | `ListView` subclass that mirrors scroll position and selection with a paired "Buddy" list view | embedded inside `PresetComparatorForm`'s 4-column layout (Avalonia-window scope) |
| `DataObjectCheckedListBox` | DataObjectCheckedListBox.cs 28 | `CheckedListBox` that custom-draws checkbox + `FriendlyName` per row with per-item brush overrides | **dead code** — defined but referenced nowhere else in upstream source (confirmed by grep); `SettingsForm`'s Enabled Objects tab actually uses plain virtual-mode `ListView`s instead (see §5). Do not port; it has no live caller. |
| `RecipePanel` | RecipePanel.cs 23 | Paints one or two `IRecipe`s via `RecipePainter.Paint`/`GetSize` | **canvas-floating-panel** — companion to `EditRecipePanel`, own `FloatingTooltipControl` |
| `CustomToolTip` | RecipeToolTip.cs ~40 of 112 | Owner-drawn generic `ToolTip` with optional "compared" text split by a divider line | transient Win32 tooltip, not a docked panel |
| `RecipeToolTip` | RecipeToolTip.cs ~72 of 112 | Owner-drawn `ToolTip` that paints one or two recipes via `RecipePainter`, for hover comparison | transient Win32 tooltip; used by chooser icon-grid hover and `SettingsForm`'s Recipes sub-tab hover |
| `RecipePainter` (static) | nested in GraphicsStuff.cs:122-260 (~138 LOC) | Icon-drawing primitive for a recipe's ingredient/product rows | **already ported** — `Foreman.Mac.Canvas.RecipePainter` (P3), reused by `RecipeNodeElement`; only the `RecipePanel`/`RecipeToolTip`/`EditRecipePanel` wrappers around it remain unported |

### Forms/ (3 classes, 6 files, ~5633 LOC)

| Class | File(s) : LOC | Responsibility | Upstream hosting |
|---|---|---|---|
| `SettingsForm` | SettingsForm.cs 644 + .Designer.cs 1880 = 2524 | Full app-settings dialog: Presets / Enabled Objects / Graph Options tabs | **Avalonia-window** — modal `Form`, `AcceptButton`/`CancelButton`, opened from a toolbar button |
| `GraphSummaryForm` | GraphSummaryForm.cs 653 + .Designer.cs 1260 = 1913 | Read-only graph statistics report: buildings/power, item/fluid throughput, key nodes | **Avalonia-window** — genuinely modal (`MainForm.GraphSummaryButton_Click` calls `form.ShowDialog()`, `StartPosition=Manual` at owner+50/+50 — not `CenterParent` despite the Designer default), opened from `MainForm`'s "Show Graph Summary" button |
| `PresetComparatorForm` | PresetComparatorForm.cs 521 + .Designer.cs 675 = 1196 | Side-by-side diff of two presets' mods/items/recipes/buildings | **Avalonia-window** — also genuinely modal (`SettingsForm.ComparePresetsButton_Click` calls `form.ShowDialog()`, same manual owner+50/+50 positioning), opened from `SettingsForm`'s "Compare Presets" button |

### Reusable generic controls, in more detail

The Controls/ table above covers `CustomProgressBar`, `SyncListView`, `RecipePanel`, `CustomToolTip`, and `RecipeToolTip` at one line each; they warrant a bit more since none of them is a floating panel in its own right, but every dialog above leans on at least one:

- **`CustomProgressBar`** (41 LOC) overrides `OnPaint` to draw the native chunk fill and then re-draws the percentage plus an optional `CustomText` string centered on top, since stock WinForms `ProgressBar` can't show text. No upstream call site lives in the files this document covers — it's used by long-running load/solve dialogs elsewhere in `Forms/`, out of scope here.
- **`SyncListView`** (54 LOC, subclass named `FFListView` in the task's read list) traps `WM_VSCROLL`/`WM_MOUSEWHEEL`/`WM_MOUSEHWHEEL` to mirror scroll position with a paired "Buddy" list view, and mirrors selection the same way. Its only live caller among the files reviewed is `PresetComparatorForm`'s `LeftListView`/`RightListView` pair (§6) — the mechanism that keeps a "changed recipe" row aligned between the two preset columns as either side scrolls.
- **`CustomToolTip`** and **`RecipeToolTip`** (both in RecipeToolTip.cs, 112 LOC combined) are owner-drawn `ToolTip` subclasses, not docked controls — they appear and vanish with the OS tooltip lifecycle (via `MouseHoverDetector`, above), never via `FloatingTooltipControl`. `CustomToolTip` paints plain text with an optional vertical "compared" divider (used for simple hover captions); `RecipeToolTip` paints one or two recipes through `RecipePainter` (used wherever the UI needs a full recipe preview on hover — the chooser icon grid, §2, and `SettingsForm`'s Recipes sub-tab, §5).
- **`RecipePanel`** (23 LOC) is the only one of these that *is* a floating panel — a thin `UserControl` wrapper that calls `RecipePainter.Paint`/`GetSize` directly, existing solely to be `EditRecipePanel`'s docked companion (§3, §7) rather than a transient tooltip.

### Hosting machinery (ProductionGraphView/, covered fully in §7)

`FloatingTooltipControl` (39 LOC), `FloatingTooltipRenderer` (140 LOC, its tooltip-drawing half already ported per `docs/upstream-divergences.md`), `EditPanelScreenLayout` (54 LOC), `EditPanelViewportLayout` (75 LOC) — the shared plumbing every canvas-floating-panel above rides on.

## 2. IRChooserPanel family: icon grid, search, filters, groups, selection

### Porting note: icon assets and DPI are already solved problems

`ChooserLayout`'s 96-DPI design constants exist because WinForms needs manual DPI-awareness math; Avalonia handles DPI scaling natively through its layout system, so the constants themselves (40px design cell, 64px design group icon, 18px minimum) should port as **fixed logical-pixel sizes** with no rescaling math attached — the DPI-scaling half of `ChooserLayout`'s job disappears, only the size *values* need to survive. The icon bitmaps themselves are not a new asset problem either: `IGroup.Icon`/`IItem.Icon`/`IRecipe.Icon` already resolve to cached `SKBitmap`s from `Foreman.Core`'s `DataCache` (ported in phase 1), the same source `RecipePainter` (already ported, §1) and every node element already draw from — the chooser grid is pure reuse of an existing icon pipeline, not a new one.

### Icon grid mechanics

- `ChooserIconGrid` is a fixed **10 columns × 8 visible rows** (`ColumnCount=10`, `VisibleRowCount=8`). No paging control — a real `VScrollBar` with `LargeChange=8`/`SmallChange=1`, one row per scroll unit, `Enabled` only once `Maximum >= LargeChange` (more than one page of rows). Mouse wheel is wired separately and steps the scrollbar by 1.
- Cell sizing: `ApplyLayout` computes `cell = Min(designCellSize(40px@96dpi), cellByHeight, cellByWidth)`, clamped to an 18px design minimum; `SetBoundsCore` is overridden to pin the control to its last-laid-out size so WinForms parent-layout can't resize it out from under the grid.
- `UpdateIRButtons()` flattens the per-subgroup filtered lists into fixed-width rows of 10, starting a **new row at the start of every subgroup** — subgroups never share a row even if the previous one didn't fill all 10 slots.
- Row/cell background color encodes availability (constants, not per-row highlight overlays):
  - `IRButtonDefaultColor` = RGB(70,70,70) — normal/available.
  - `IRButtonHiddenColor` = RGB(120,0,0) dark red — hidden/disabled.
  - `IRButtonNoAssemblerColor` = RGB(100,100,0) olive — visible but no enabled assembler can use/produce it.
  - `IRButtonUnavailableColor` = RGB(170,10,160) magenta — recipe not `Available`, or no available assembler (recipe panel only).
  - Empty/disabled slots: `BackColor=DimGray`, `ForeColor=Gray`, `BackgroundImage=null`.
- Disabled-but-populated icons (e.g. a right-click-hidden recipe) render **desaturated**, not just recolored: `NFButton.OnEnabledChanged` converts `BackgroundImage` to a true grayscale bitmap (luminance-weighted `ColorMatrix`, alpha ×0.4) on disable, restores the cached color image on re-enable.
- No explicit "selected" highlight on IR buttons — a click closes the panel (or, for `RecipeChooserPanel` with Shift held, stays open for multi-add). Hover shows a `CustomToolTip`/`RecipeToolTip` positioned at `(control.Width, yoffset)`.
- Group buttons: square `NFButton`s, 64px design size, in a wrapping `FlowLayoutPanel`; selected group gets `BackColor = Color.SandyBrown` vs `Color.DimGray` for the rest. Groups with 0 filtered items stay visible but disabled.

### Search

- `FilterTextBox.Text.ToLowerInvariant()`, matched live on every keystroke (`TextChanged`, no debounce) against **both** names:
  ```
  i.LFriendlyName.Contains(filterString) || i.Name.Contains(filterString, StringComparison.OrdinalIgnoreCase)
  ```
  `LFriendlyName` = pre-lowercased translated display name (ordinal `Contains` against the already-lowercased query); `Name` = internal dev-name, matched case-insensitively.
- `RecipeChooserPanel` extends this to the recipe's own name plus (unless "Recipe Only" is checked) every ingredient/product name, same two-field pattern per ingredient/product.

### Filters

| Control | Caption | Bound setting | Effect |
|---|---|---|---|
| `ShowHiddenCheckBox` | "Show Hidden" (item panel) / **"Show Disabled"** (recipe panel, runtime-overridden) | `ShowHidden` | `(visible \|\| showHidden)` — include normally-excluded disabled items/recipes |
| `IgnoreAssemblerCheckBox` | "Ignore Assembler" | `IgnoreAssemblerStatus` | include even without a valid enabled assembler |
| `RecipeNameOnlyFilterCheckBox` | "Recipe Only" (recipe panel only, hidden on item panel) | `RecipeNameOnlyFilter` | when checked, search matches only the recipe's own name, not ingredients/products |
| `AsIngredientCheckBox` | "Ingredient" (recipe panel only) | — | feeds `includeConsumers` |
| `AsProductCheckBox` | "Product" (recipe panel only) | — | feeds `includeSuppliers` |
| `AsFuelCheckBox` | "Fuel" (recipe panel only) | — | feeds `includeFuel`, gated on default quality |
| `ShowUnavailable` (protected property, not a visible checkbox here) | — | `ShowUnavailable` | gates group inclusion (`AvailableGroups` vs all `Groups`) and every `.Available` check |
| `QualitySelector` (ComboBox) | "Quality:" | — | not boolean — restricts which `IQuality` the pick applies; disabled when only one quality exists |

The three role checkboxes (Ingredient/Product/Fuel) are **independent, all-checked-by-default toggles**, confirming `docs/ui-reference.md`'s screenshot read — not radio-exclusive. Combined predicate, `RecipeMatchesKeyItem`:
```
(includeConsumers && recipe.IngredientSet.ContainsKey(keyItem) && tempRangeOk) ||
(includeSuppliers && recipe.ProductSet.ContainsKey(keyItem) && tempRangeOk) ||
(includeConsumers && includeFuel && keyItem.FuelsEntities.Count > 0 && recipe.Assemblers.Any(a => a.Fuels.Contains(keyItem) && (a.Enabled || ignoreAssemblerStatus))) ||
(includeSuppliers && includeFuel && keyItem.FuelOrigin is IItem fo && recipe.Assemblers.Any(a => a.Fuels.Contains(fo) && (a.Enabled || ignoreAssemblerStatus)))
```

### Group/subgroup organization, extraction/power group

No special-cased "extraction"/"power" pseudo-group logic exists in these files — grouping is purely the game data model's `IGroup`/`ISubgroup` hierarchy. `GetSortedGroups()` includes any group with ≥1 qualifying item/recipe in any of its subgroups (respecting `ShowUnavailable`); within a chosen group, one row-batch per `ISubgroup`, in `group.Subgroups` order (not re-sorted). The last-selected group persists across panel instances (`StartingGroup`), defaulting to the group named `"logistics"` on first-ever show; if the current selection ends up empty after a filter change, both subclasses search left-then-right along the sorted group list for the nearest non-empty group.

### Extraction/power group

The extraction (mining/pumping) and power-generation entity groups are ordinary `IGroup`s surfaced the same way as any other — no separate UI section; they appear as ordinary category icons in the group row, filtered/sorted identically.

- The "pickaxe" icon `docs/ui-reference.md`'s screenshot read flagged is simply whichever `IGroup` the data set assigns that icon to — not a hard-coded special case in this control's own logic.
- This is a negative finding worth stating plainly for the port: there is no special-case branch to reproduce. A straight port of `GetSortedGroups()`/`GetSubgroupList()` against `Foreman.Core`'s already-ported `DCache.Groups` (phase 1) reproduces extraction/power grouping for free, with zero extraction-specific code to write.

### Unavailable/disabled highlighting

Covered above under grid mechanics — background-color coding (4-color scheme) plus the `NFButton` grayscale-on-disable behavior is the entire visual vocabulary; there is no separate badge/overlay icon for "unavailable" the way `ErrorNoticeElement` uses a warning triangle on graph nodes.

### Paging/scroll

Real `VScrollBar`, not pagination — see grid mechanics above.

### Selection flows

- **`ItemChooserPanel.IRButtonMouseUp`**: left-click a populated button → builds `ItemQualityPair`, fires `ItemRequested`, **always** `ClosePanel(ChooserPanelCloseReason.ItemSelected)`.
- **`RecipeChooserPanel.IRButtonMouseUp`**: left-click → fires `RecipeRequested`, closes **unless Shift is held** (multi-add without re-opening); right-click on a populated button toggles the recipe's hidden/enabled state in place and re-renders (panel stays open).
- The Shift-to-multi-add gesture has no dedicated visual affordance (no "hold shift to add multiple" hint anywhere in the Designer file) — it's discoverable only by trying it, worth flagging for the Mac port's own UX pass even though faithfully porting the mechanism itself is straightforward.
- **Footer "alt node" buttons** (`RecipeChooserPanel` only, visibility data-driven per key item): `AddSupplyButton`("Source")→`NodeType.Supplier`, `AddConsumerButton`("Output")→`NodeType.Consumer`, `AddPassthroughButton`("Pass-Through")→`NodeType.Passthrough`, `AddSpoilButton`("Spoil")/`AddUnspoilButton`("UnSpoil")→`NodeType.Spoil` up/down, `AddPlantButton`("Plant")/`AddUnplantButton`("UnPlant")→`NodeType.Plant` up/down. The UnSpoil/UnPlant branches set `PanelCloseReason = RequiresItemSelection` instead of closing directly when the key item has ≥2 spoil/plant origins (ambiguous source needs a follow-up picker). All close via `ChooserPanelCloseReason.AltNodeSelected` unless Shift is held.
- Close-reason enum: `RecipeSelected, ItemSelected, AltNodeSelected, RequiresItemSelection, Cancelled`. Panel also closes on click-outside (`CloseIfClickOutside`, §7) and on losing focus entirely (deferred `BeginInvoke` check to avoid false positives when focus moves to a child control).

### Every caption/tooltip string

- `FilterLabel`: "Filter:" · `RecipeNameOnlyFilterCheckBox`: "Recipe Only" · `IgnoreAssemblerCheckBox`: "Ignore Assembler" · `ShowHiddenCheckBox`: "Show Hidden" (item) / "Show Disabled" (recipe, runtime override) · `QualityLabel`: "Quality:" · `AsIngredientCheckBox`: "Ingredient" · `AsProductCheckBox`: "Product" · `AsFuelCheckBox`: "Fuel" · `AddSupplyButton`: "Source" · `AddPassthroughButton`: "Pass-Through" · `AddConsumerButton`: "Output" · `AddUnspoilButton`: "UnSpoil" · `AddUnplantButton`: "UnPlant" · `AddSpoilButton`: "Spoil" · `AddPlantButton`: "Plant".
- Runtime tooltips: group button → `IGroup.FriendlyName` (fallback `"-"`); IR button → `irObject.FriendlyName` (fallback `"-"`).
- Design-mode-only placeholder group labels: "log", "cont", "inter", "prod", "sci".
- None of these strings carry any localization indirection in the Designer file itself — they're plain `Text = "..."` literal assignments, same as every other caption quoted throughout this document (Forms strings included). Whatever translation layer the Mac port eventually adopts, it has no upstream precedent to match; these are all hard-coded English literals as-is.

## 3. EditRecipePanel: fields, viewport, live-update, entry points

### Full field inventory

**Rate/header row**: `AutoAssemblersOption`("Auto")/`FixedAssemblersOption`("Fixed") radios → `nodeController.SetRateType`; `FixedAssemblerInput` (NumericUpDown, max = node's `MaxDesiredSetValue`) → `SetDesiredSetValue`; `LowPriorityCheckBox`("Low Priority Recipe") → `SetPriority`; `KeyNodeCheckBox`("Key Node") + `KeyNodeTitleLabel`("Title:") + `KeyNodeTitleInput` → `SetKeyNode`/`SetKeyNodeTitle`; `QualitySelector` (ComboBox, populated from enabled qualities) → rebuilds assembler/module/beacon option lists and sets `Graph.DefaultAssemblerQuality`.

**Assembler picker**: `AssemblerTitle` + `SelectedAssemblerIcon` (current pick); `AssemblerChoicePanel`/`Table` grid of buttons → `SetAssembler(new AssemblerQualityPair(...))`. Read-only info card (`AssemblerInfoTable`, **the LOD-High percentage displays** — task's phrasing; actually always-present, just conditionally `Visible`, not LOD-gated): `AssemblerRateLabel`("# of Assemblers:"), Energy/Speed/Productivity/Pollution/Quality percent pairs, each from `nodeData.GetConsumptionMultiplier/GetSpeedMultiplier/GetProductivityMultiplier/GetPollutionMultiplier/GetQualityMultiplier` formatted `"P0"`. `NeighboursLabel`("Average # of neighbours:") + `NeighbourInput` (reactor-only, `EntityType.Reactor`) → `SetNeighbourCount`. `ExtraProductivityLabel`("Extra Productivity Bonus (%):") + `ExtraProductivityInput` (NumericUpDown 0–100000, **not a checkbox** — visibility alone gates it, shown when the recipe `HasProductivityResearch` or the entity is a Miner / `EnableExtraProductivityForNonMiners` is set) → `SetExtraProductivityBonus(value/100)`. Generator-only read-only temperature range labels.

**Fuel row**: `FuelTitle` (dynamic, e.g. "Fuel: (Rockets)" or "-none-") + `SelectedFuelIcon` + `FuelOptionsPanel`/`Table` (burner assemblers only) → `SetFuel`.

**Modules**: `AModulesLabel`("Modules ({0}/{1}):"), equipped-module row (left-click removes one via `RemoveAssemblerModule(index)`, right-click removes all of that type) and available-module row (left-click adds one via `AddAssemblerModule`, right-click fills remaining slots via `AddAssemblerModules`).

**Beacon table**: `BeaconTitle` + `SelectedBeaconIcon` + `BeaconChoicePanel`/`Table` → clicking the already-selected beacon calls `ClearBeacon()`, else `SetBeacon`. `BeaconValuesTable`: "# Beacons:" (`BeaconCountInput`), "/Assembler:" (`BeaconsPerAssemblerInput`, ratio), "Additional:" (`ConstantBeaconInput`) — all three route through `SetBeaconValues`. Read-only `BeaconInfoTable`: Energy/Modules/Efficiency/#Beacons/Total Energy. Beacon modules mirror the assembler-module left/right-click pattern via `BModuleButton_Click`/`BModuleOptionButton_Click`.

### Nested viewport (EditRecipePanel.Viewport.cs)

Not a rendering surface — a layout-only partial class. `ApplyViewportBounds()` calls `EditPanelViewportLayout.Apply(this, MainTable, myGraphViewer)` and hooks `myGraphViewer.Resize` (disposal-safe) so the panel's on-screen size stays clamped to the viewer's client bounds. It exists purely to isolate resize-tracking plumbing from control-event logic, not to host drawing.

### Live-update semantics

No explicit "Apply" button — nearly everything re-solves synchronously in its own handler:
- Every `NumericUpDown.ValueChanged` (fixed rate, neighbour bonus, extra productivity, beacon count) calls the controller mutator then `myGraphViewer.Graph.UpdateNodeValues()` in the same call, immediately.
- Every button click (assembler/fuel/beacon/module pick) does the same, unconditionally, on every click.
- **Exception**: `SetBeaconValues` only re-solves when the *changed* control is `BeaconCountInput` specifically — the per-assembler ratio and additional/constant count update the read-only info labels but do **not** trigger a re-solve (comment: "only graph update worthy change is the # of beacons").
- **Exception**: `KeyNodeTitleInput_TextChanged` never calls `UpdateNodeValues()` — title text doesn't affect solve state, only `Invalidate()`.
- Panel close also does one final `Graph.UpdateNodeValues()` as a catch-all.

### Entry points

Sole construction site: `ProductionGraphViewer.EditRecipeNode(RecipeNodeElement)`, reached through `EditNode(BaseNodeElement)`'s type dispatch. Triggered from `BaseNodeElement.MouseUpAction` — **a plain left click** (right click opens the context menu instead). `RecipeNodeElement` does not override `MouseUpAction`, so recipe nodes use the identical base routing as every other node type. `MouseUp` checks `ItemTabElement` (connector tabs) and the error-notice badge *before* falling through to `MouseUpAction` — so clicking a tab or the error badge does **not** open the edit panel (those are intercepted earlier for link-dragging / autoresolve), but clicking anywhere else on the node body or its icon reaches the same `MouseUpAction` and opens the panel identically.

`EditNode`'s dispatch by node type (single entry point, shared by both panels):

| Node's view-model type | Panel opened |
|---|---|
| `RecipeNodeElement` | `EditRecipeNode` → `EditRecipePanel` + companion `RecipePanel` |
| every other `BaseNodeElement` (supplier/consumer/passthrough/spoil/plant) | `EditFlowPanel` directly |

## 4. EditFlowPanel: rate editor, passthrough, fixed/auto

- `RateLabel` — caption set dynamically from `node.SetValueDescription` (Designer default: "Item Flowrate (per 1 hour):").
- `AutoOption`("Auto") / `FixedOption`("Fixed") radios: **Auto** = the solver computes the flow from the rest of the graph — `FixedFlowInput` is disabled/greyed and shows the live computed value. **Fixed** = the user pins the value — `FixedFlowInput` becomes enabled/editable and the pinned value drives the solver as a constraint. Toggling calls `SetRateType(Manual|Auto)` + `Graph.UpdateNodeValues()` only when the resulting `RateType` actually differs from the node's current one.
- `FixedFlowInput` (NumericUpDown, max = `node.MaxDesiredSetValue × RateMultiplier`, up to 1,000,000,000, thousands-separated) → `SetDesiredSetValue` + graph update on every keystroke/spin.
- `SimplePassthroughNodesCheckBox`("Simplify throughput node") — visible only for `IPassthroughNodeViewModel` nodes → `(nodeController as PassthroughNodeController)?.SetSimpleDraw(...)` + `Invalidate()` (drawing-only, no re-solve).
- `KeyNodeCheckBox`("Key Node") + `KeyNodeTitleLabel`("Title:") + `KeyNodeTitleInput` — identical semantics to `EditRecipePanel`'s key-node fields.

## 5. SettingsForm: tabs, widgets, AppSettings cross-reference, preset surface

### Tab inventory

`MainTabControl`, in order: **"Presets"**, **"Enabled Objects"**, **"Graph Options"** (field name `OptionsTab`, caption "Graph Options" not "Options"). Footer: `ConfirmButton`("Confirm", `AcceptButton`), `CancelSettingsButton`("Cancel", `CancelButton`).

### Presets tab

- `label1`"Current:" (bold) + `CurrentPresetLabel` (bold, = `Options.SelectedPreset?.Name`) — clicking it clears the list selection.
- `groupBox4`"Mods (read-only):" → `ModSelectionBox` (ListBox, no selection), from `PresetProcessor.ReadPresetInfo(...).ModList` — display-only, no `Options` field.
- `PresetListBox` (`DisplayMember="Name"`, `Options.Presets` minus the active one at index 0): double-click selects + closes OK; right-click opens `PresetMenuStrip` with two dynamic-caption items — `SelectPresetMenuItem` reads "Select Preset" / "Use This Preset" / "Current Preset" depending on the right-clicked row's state, `DeletePresetMenuItem` reads "Delete Preset" / "Delete This Preset" / "Default Preset" the same way (the active preset's own row can't be deleted, hence "Default Preset" as its disabled-state caption).
- `groupBox1`"Difficulty (read-only)" — **`Visible=false` in the Designer, dead UI**; contains Recipe/Technology difficulty labels the screenshots never show either (matches `docs/ui-reference.md`'s note that this section was hidden from the README capture). Skip entirely in the port.
- `ImportPresetButton`"Import New Preset From Factorio" → opens `PresetImportForm` modally at a manual offset; on success either force-reloads (if it overwrote the active preset, sets `Options.RequireReload=true`) or prompts Yes/No to switch to it. **`PresetImportForm` itself is out of this document's scope** (not among the read-only sources listed for this reference) — treat it as a separate, later porting unit; `SettingsForm`'s job is only to launch it and react to its result.
- `ComparePresetsButton`"Compare Presets" → guards `Options.Presets?.Count < 2` (shows "Can not compare presets!\n...you only have 1 preset :/"), else opens `PresetComparatorForm` modally.

### Enabled Objects tab

- `LoadEnabledFromSaveButton`"Load from save" (full width) → opens `SaveFileLoadForm(Options.DCache, Options.EnabledObjects)`; `DialogResult.OK` refreshes the lists, `DialogResult.Abort` shows an error message about re-saving the Factorio save.
- `SetEnabledFromSciencePacksButton`"Assign based on science packs" (full width, stacked below the above — confirming `docs/ui-reference.md`'s screenshot read) → opens `SciencePacksLoadForm(Options.DCache, Options.EnabledObjects)`, refreshes on OK.
- `EnableAllButton`"Enable All" → clears then re-populates `Options.EnabledObjects` from every available assembler/beacon/module/recipe/quality (plus `DCache.PlayerAssembler` if present).
- `label4`"Filter:" + `FilterTextBox`, `ShowUnavailablesFilterCheckBox`"Show Unavailables" — **session-only list filter, not persisted to `Options`**. An item passes if `(showUnavailables || item.Available)` AND `(filter empty || Name contains filter, ordinal-ignore-case)`.
- `EnabledObjectsTabControl`, **7** sub-tabs (not 6 — the README screenshot in `docs/ui-reference.md` only lists Assemblers/Miners/Power/Beacons/Modules/Recipes): Assemblers, Miners, Power, Beacons, Modules, Recipes, **Qualities**. Each is a virtual-mode `ListView` (checkboxes, hidden header, 24×24 icons, row backcolor White=available/Pink=unavailable), backed by parallel `unfiltered*List`/`filtered*List` fields — **not** `DataObjectCheckedListBox` (that class is dead code, see §1). Recipes sub-tab also drives a `RecipeToolTip` on hover via `MouseHoverDetector`.
- Checking/unchecking a row toggles membership directly in `Options.EnabledObjects` (a `HashSet<IDataObjectBase>`) via `ListView_MouseClick`/`ListView_MouseDoubleClick` — there is no separate "commit" step, every click is immediately live in `Options`.
- Ctrl+A (`ListView_KeyDown`) selects every row in the currently active sub-tab (via `NativeMethods.SelectAllItems`) but does **not** check them — selection and check-state are independent in this virtual-mode `ListView`, same as stock Windows Explorer semantics.

### Graph Options tab

Six group boxes, top to bottom:
1. **`graphOptionsGroupBox`**"Graph Options": "Maximum Quality Steps" + `QualityStepsInput` (1-20, default 1).
2. **`nodeGraphicsGroupBox`**"Node Graphics:" — "Level of detail:" Low/Med/High radios; "Maximum number of graphical objects:" `NodeCountForSimpleViewInput`; "Icon Size in icon view:" `IconsSizeInput` (8-256, default 12); checkboxes: "Draw arrows to show direction on link lines (non-dynamic link-width)", "Dynamic link-width", "Abbreviate science packs", "Show recipe tool tip", "Round building count", "Lock recipe editor to top left corner", "Flag over or under supplied nodes", "Enable Dark Mode".
3. **`guideArrowsGroupBox`**"Guide-Arrows:" — 4 checkboxes: "Display arrows pointing to any node errors", "...node warnings", "...node with missing links", "...under-supplied or over-producing node". Confirms these are genuinely separate toggles, not one combined switch, matching `docs/ui-reference.md`'s screenshot read.
4. **`defaultsGroupBox`**"Defaults" — "Assemblers:" dropdown (`AssemblerSelector.StyleNames`), "Modules:" dropdown (`ModuleSelector.StyleNames`), "Node Direction:" dropdown (items literally "Up (default)"/"Down") + "Smart Direction" checkbox, and (full-width row) "Use simple-draw passthrough nodes as default" checkbox — this row is the "Defaults (simple-draw passthrough)" widget `docs/ui-reference.md` flagged as unconfirmed; it's a checkbox in the same group box as the other Defaults, not a separate section.
5. **`advancedGroupBox`**"Advanced" — "Enable extra productivity bonus for all entities (instead of only miners)", "Show unavailable items (DEV)", "Load barreling & crating recipes (DEV)".
6. **`solverOptionsGroupBox`**"Advanced (Solver options)" — **entirely absent from `docs/ui-reference.md`'s screenshot AND from the port's `AppSettings.cs`**: "Low priority multiplier (10^n, 2 default):" `LowPriorityPowerInput` (1 decimal, 1-6, default 4), "Maximize output nodes: Power (10^n):" checkbox + `PullConsumerNodesPowerInput` (1 decimal, max 5, default 1).

### AppSettings cross-reference (44 properties in `src/Foreman.Mac/Services/AppSettings.cs`)

Widget → property, direct matches: `AssemblerSelectorStyleDropDown`→`DefaultAssemblerOption`, `ModuleSelectorStyleDropDown`→`DefaultModuleOption`, `NodeDirectionDropDown`→`DefaultNodeDirection`, `SmartNodeDirectionCheckBox`→`SmartNodeDirection`, `SimplePassthroughNodesCheckBox`→`SimplePassthroughNodes`, LOD radios→`LevelOfDetail`, `NodeCountForSimpleViewInput`→`NodeCountForSimpleView`, `IconsSizeInput`→`IconsSize`, `ArrowsOnLinksCheckBox`→`ArrowsOnLinks`, `DynamicLWCheckBox`→`DynamicLineWidth`, `AbbreviateSciPackCheckBox`→`AbbreviateSciPacks`, `ShowNodeRecipeCheckBox`→`ShowRecipeToolTip`, `RoundAssemblerCountCheckBox`→`RoundAssemblerCount`, `RecipeEditPanelPositionLockCheckBox`→`LockedRecipeEditorPosition`, `FlagOUSupplyNodesCheckBox`→`FlagOUSuppliedNodes`, `OUSuppliedArrowsCheckBox`→`ShowOUSuppliedArrows`, `WarningArrowsCheckBox`→`ShowWarningArrows`, `ErrorArrowsCheckBox`→`ShowErrorArrows`, `DisconnectedArrowsCheckBox`→`ShowDisconnectedArrows`, `ShowProductivityBonusOnAllCheckBox`→`EnableExtraProductivityForNonMiners`, `LoadBarrelingCheckBox`→`UseRecipeBWfilters` (inverted sense), `FlagDarkModeCheckBox`→`FlagDarkMode` (bool upstream, widened to `ThemeMode` enum on Mac).

**AppSettings fields with no SettingsForm widget** (handled elsewhere or out of this dialog's scope): `MinorGridlines`/`MajorGridlines`/`AltGridlines` (main-window gridline controls, not Settings), `IgnoreAssemblerStatus`/`RecipeNameOnlyFilter` (chooser-panel-local, §2), `DefaultRateUnit` (main toolbar), the 8 `AnnotText*`/`AnnotShape*` annotation-style defaults (P4's annotation dialogs, not Settings).

**SettingsForm fields with no AppSettings counterpart — real porting gaps**: the entire "Advanced (Solver options)" group box (`QualityStepsInput`, `LowPriorityPowerInput`, `PullConsumerNodesCheckBox`+Input — 4 fields, all on `Options`/`ProductionGraph`, none in `AppSettings.cs`); and `ShowUnavailablesCheckBox`(DEV persisted setting) vs `ShowUnavailablesFilterCheckBox`(session-only list filter) both mapping ambiguously onto the single `AppSettings.ShowUnavailable` field — needs an explicit design call when SettingsForm is ported, not a blind 1:1 copy.

## 6. GraphSummaryForm + PresetComparatorForm

### GraphSummaryForm

Modal `Form` (`GraphSummaryButton_Click` in `MainForm.cs:564-569`: `form.StartPosition = FormStartPosition.Manual; form.Left = this.Left+50; form.Top = this.Top+50; form.ShowDialog();` — a true modal blocking dialog, not the `CenterParent` the Designer's default implies), opened from `MainForm`'s `GraphSummaryButton` ("Show Graph Summary"). Two constructor overloads take either the whole `IProductionGraphSession` or an explicit node subset — but `MainForm` always passes the whole session, so it reports over **the entire current graph**, not a selection.

Three tabs:
- **Buildings** — 4 sub-tabs (Assemblers/Miners/Power/Beacons), each a sortable/filterable `ListView`. Columns: Assemblers/Miners `#, Name, Power (Assembler|Extractor), Power (Beacons)`; Power `#, Name, Power Generated, Power Consumed`; Beacons `#, Name, Power (Beacon)`. Header summary: `#Buildings:`, `#Beacons:`, `Power Consumption:`, `Power Production:`, `Net Power:` (shown only when both totals are nonzero).
- **Items/Fluids** (`" (per " + rateString + ")"` suffix) — 2 sub-tabs (Items/Fluids), 7 filter checkboxes (input/input-unlinked/output/output-unlinked/output-overproduced/production/consumption, all default-checked). Columns: `Item|Fluid, In, In (x link), Out, Out (x link), Overprod., Produced, Consumed`.
- **Key Nodes** — one `ListView`, 4 type-filter checkboxes (Supplier/Consumer/Passthrough/Recipe, default-checked). Columns: `Node Type, Node Details, Node Title, Throughput, Factories`. Rows = every node with `KeyNode==true` (user-marked/highlighted).

Computations (all over the full node set):
- Building count = `Σ Ceiling(node.ActualSetValue)` across all recipe nodes; beacon count = `Σ node.GetTotalBeacons()`.
- Power consumption = `Σ (assembler-electrical-consumption + beacon-electrical-consumption)`; production = `Σ generator-electrical-production`; net shown only if both sides are nonzero.
- Per-row power for buildings sums the same per-node getters within each (entity, quality) group; per-row beacon power = `count × (energyConsumption(quality) + energyDrain)`.
- Item/fluid throughput, per node: recipe inputs unlinked (no matching `InputLinks` entry) go to "In (x link)", else "Consumed"; recipe outputs always add to "Produced", unlinked ones also to "Out (x link)", overproduced ones add the overflow amount to "Overprod."; suppliers add to "In", consumers add to "Out".
- Key Nodes: recipe-node rows show `Throughput="-"`, `Factories=ActualSetValue`; every other node type shows the reverse.
- CSV export dumps the **currently filtered** rows per tab, not the unfiltered set. Numeric columns sort descending by default; the Key Nodes "Node Title" column uses a natural-sort regex so numbered titles order correctly.
- **Porting caution**: the header totals (building/beacon/power counts) and the per-row totals are two independent aggregation passes over overlapping-but-different node subsets — the header sums across *every* recipe node regardless of which Buildings sub-tab it belongs to, while each row sums only its own (entity, quality) group. Collapsing these into one merged pass would make the header total silently drift out of sync with what the visible rows actually add up to; keep the two-pass structure upstream uses.

### PresetComparatorForm

Modal `Form` (`ComparePresetsButton_Click` in `SettingsForm.cs:526-531`, same `StartPosition=Manual` + `ShowDialog()` pattern as `GraphSummaryForm` above), opened from `SettingsForm`'s "Compare Presets" button, guarded to require ≥2 presets. Compares **two presets' data caches** (loaded via `DataLoadForm`), not a preset against the live graph.

Layout: `LeftPresetSelectionBox` / "<-- vs -->" / `RightPresetSelectionBox` + `ProcessPresetsButton` (caption swaps between "Read Presets And Compare" / "Select Other Presets" / "Cant Compare Preset To Itself"). `ComparisonTabControl` with **8** tabs — Mods, Items, Recipes, Assemblers, Miners, Power, Beacons, Modules — each swapping the 4 headerless list views below: `LeftOnlyListView` ("Left Preset Exclusive:"), paired `LeftListView`/`RightListView` (scroll+selection-synced via `SyncListView`), `RightOnlyListView` ("Right Preset Exclusive:"). Filter row: `FilterTextBox`, `HideEqualObjectsCheckBox`("Hide Equal", default checked), `HideSimilarObjectsCheckBox`("Hide Similar"), `ShowUnavailableCheckBox`("Show Unavailables").

Diff logic: objects are bucketed by dictionary key (`Name`, or `modName_version` for mods) into left-only / matched-pair / right-only. Matched pairs get a `similarInternals` check whose definition is **per tab**: Mods = name equality only; Items = `Available` flag equality; Recipes = ingredient/product counts + `Available` equal, plus (scaled by `rightRecipe.Time/leftRecipe.Time`) every ingredient/product ratio within 0.1% — so proportionally-identical recipes with different absolute amounts/time still count as "close" (not "different"); Assemblers/Miners/Power = **always true** (a `//QUALITY UPDATE REQUIRED` comment marks this comparison as currently stubbed/disabled upstream); Beacons = `ModuleSlots` equality; Modules = all four bonus getters (productivity/speed/consumption/quality — pollution is not compared here despite appearing in the tooltip) equal on both sides. Row color: white=equal-and-similar, khaki=similar-but-different-name (or ratio-equivalent recipe), pink=different. Unavailable items additionally render in dark-red italic vs black regular.

## 7. Hosting machinery: FloatingTooltipControl's panel half, layout math, close semantics

### What P3 already ported vs what's still missing

`FloatingTooltipRenderer` is not entirely new work for P5 — P3 already ported its **tooltip-drawing half** as `Foreman.Mac.Canvas.FloatingTooltipRenderer`, per `docs/upstream-divergences.md`: "ports `ProductionGraphView/FloatingTooltipRenderer.cs`'s plain-text/custom-draw `Paint` path... skipping `FloatingTooltipControl`'s floating-panel half entirely — P5-panel scope," collapsing upstream's two `DrawTooltip` overloads into one `Draw(SKCanvas, TooltipInfo)` entry point since this port never has a floating-control `Rectangle` to pass in directly. That means the speech-bubble-body-plus-arrow paint code, the hover-tooltip positioning, and `RecipePainter`'s icon-row drawing primitive are all done; what's genuinely new for P5 is exactly the three things below — `FloatingTooltipControl`'s child-control embedding/lifecycle, and the two `EditPanel*Layout` classes — plus the interactive panels themselves (§2-6).

### FloatingTooltipControl (39 LOC) — the part P3 skipped

Same class hosts both the decorative hover tooltip (pure paint, `TooltipInfo`, drawn by `FloatingTooltipRenderer`) and an interactive edit panel (`EditFlowPanel`/`EditRecipePanel`/`RecipePanel`) — the difference is purely which constructor argument is passed, not a mode flag. Panel mode: `new FloatingTooltipControl(editPanel, direction, graphAnchor, viewer, showOverride, useControlLocation)` where `editPanel` is a real `UserControl`.

- **Embedding**: the constructor does `parent.Controls.Add(control)` — the edit panel becomes a genuine child `Control` of `ProductionGraphViewer`, receiving real WinForms mouse/keyboard/focus like any sibling control (not synthetic hit-testing against the canvas).
- **Positioning**: `Rectangle ttRect = FloatingTooltipRenderer.getTooltipScreenBounds(parent.GraphToScreen(graphLocation), control.Size, direction); if (!useControlLocation) control.Location = ttRect.Location;` — callers that already computed a clamped location via `EditPanelScreenLayout` pass `useControlLocation:true` to skip this auto-placement.
- **Showing**: becoming a visible child control is enough; `control.Focus()` at the end of the constructor grabs keyboard focus immediately.
- **Closing**: `Dispose()` → base `Control.Dispose()` (removes it from `Controls` as a WinForms side effect) → `GraphViewer.ToolTipRenderer.RemoveToolTip(this)` → fires `Closing` (callers use this to flip `SubwindowOpen=false` and trigger a final `Graph.UpdateNodeValues()`).
- **Input pass-through**: because the panel is a real child `Control` layered on top, WinForms z-order/hit-testing naturally routes input to it while visible; the canvas underneath only receives input again once the panel is removed. `FloatingTooltipRenderer.Paint` only draws the decorative speech-bubble body+arrow around whichever controls are currently registered — it does not participate in input routing itself.

### EditPanelScreenLayout math (54 LOC)

Screen-space clamping of a panel rectangle inside the visible viewer client area, plus anchor-offset placement for choosers:
```
int maxX = Max(margin, viewerWidth - margin - bounds.Width);
int maxY = Max(margin, viewerHeight - margin - bounds.Height);
x = x < margin ? margin : (x > maxX ? maxX : x);   // same for y
```
`DefaultMargin = 25`. Chooser anchor placement offsets the desired top-left by `(-24, -16)` from the click point before clamping. `ShiftControlsToFit` applies one shared delta to N panels at once (`PlaceFloatingPanels`) so the paired edit+recipe panels move together as a unit instead of clamping independently and drifting apart.

### EditPanelViewportLayout math (75 LOC)

A different axis of the same problem — content **sizing**, not screen position. `EnsureScrollHost` reparents a panel's content root into an `AutoScroll=true, Dock=Fill` host panel. `Apply(editPanel, contentRoot, viewer)` computes `max = viewer.ClientSize - margin*2` (same margin constant as ScreenLayout), measures the content's natural preferred size, reserves scrollbar width if height overflows, and sets the final panel `Size = Min(natural, max)`. **ViewportLayout feeds ScreenLayout, not vice versa**: size is resolved first (`ApplyViewportBounds()`, called at panel construction), then that final size is handed to `EditPanelScreenLayout`'s clamping/anchor functions to compute where the already-sized rectangle sits on screen.

### Click-outside-closes semantics

Two independent mechanisms, by panel family:
- **IRChooserPanel**: explicit bounds hit-test. `CloseIfClickOutside(Point)`: `if (!Bounds.Contains(point)) ClosePanel(Cancelled)`, called from `ProductionGraphViewer_MouseDown` for every live chooser (`Controls.OfType<IRChooserPanel>()`), plus a focus-leave fallback deferred via `BeginInvoke`.
- **EditFlowPanel/EditRecipePanel (tooltip-hosted)**: no bounds test at all — every canvas `MouseDown` unconditionally calls `ToolTipRenderer.ClearFloatingControls()`, disposing **every** registered `FloatingTooltipControl` regardless of click location (a click inside the panel itself never reaches this handler, since the panel swallows its own clicks as a child control first).

### One-panel-at-a-time rules

No single `currentPanel` registry field — enforcement is structural: `ClearFloatingControls()` tears down every open tooltip-hosted panel on each canvas click before a new one can open from that same click; `IRChooserPanel.ClosePanel` always disposes regardless of close reason, so sequential choosers (e.g. the `RequiresItemSelection` follow-up picker) never overlap. `SubwindowOpen` (a private bool, not a panel reference) is the closest thing to global "something is open" state, gating tooltip paint and WASD key handling. **Exception**: `EditRecipeNode` deliberately opens two cooperating panels at once (`EditRecipePanel` + companion `RecipePanel`), sharing one `PlaceFloatingPanels` union-rect placement and closing together — a paired display, not a violation of the rule.

### Call sites

| Panel | Opened from | Trigger | Layout classes used |
|---|---|---|---|
| `RecipeChooserPanel` | `ProductionGraphViewer.cs:243`, inside `AddNewNode`/link-drop handling | dropping a dragged link endpoint on empty canvas | `EditPanelScreenLayout` only (own icon-grid reflow, no `ViewportLayout`) |
| `ItemChooserPanel` | `AddItem` (line 191, toolbar/menu) + 2 inline sites inside `AddNewNode` (~303, ~342) | "add item" action; spoil/plant multi-origin disambiguation | `EditPanelScreenLayout` only |
| `EditFlowPanel` | `EditNode` (517-534) | editing a non-recipe node (double-click/edit action) | `EditPanelViewportLayout` (size) then `EditPanelScreenLayout`/`PlaceFloatingPanels` (position), wrapped in `FloatingTooltipControl` with `useControlLocation:true` |
| `EditRecipePanel` + `RecipePanel` | `EditRecipeNode` (539-586) | editing a `RecipeNodeElement` | same viewport-then-screen pattern; `LockedRecipeEditPanelPosition` variant skips dynamic placement for a fixed `(15,15)` origin |

### Adjacent forms referenced but not covered by this document

Several dialogs get named above only because they're a button's target, not because their own internals were part of this reference's read list — flagging them together here so a later phase doesn't assume they were already researched: `PresetImportForm` (§5, "Import New Preset From Factorio"), `SaveFileLoadForm` (§5, "Load from save"), `SciencePacksLoadForm` (§5, "Assign based on science packs"), and `DataLoadForm` (§6, backs both `PresetComparatorForm`'s left/right preset loads). Each needs its own read-through before it can be ported; none of their internals are documented here.

## 8. P4 touchpoints to replace

Every one of these lives in `src/Foreman.Mac/Canvas/`; each needs to become the real upstream flow, not the current stand-in.

- **`GraphCanvasControl.OnPointerReleased`, left-click-to-edit** (`GraphCanvasControl.cs:417-425`, comment: "EditNode itself stays the P5 stub already on record") — a plain left click with no modifiers already forwards to the clicked node's own `MouseUpLeft` routing (matching upstream `BaseNodeElement.MouseUpAction`), but `EditNode`'s actual panel-opening call is a no-op. **Should become**: `EditNode(BaseNodeElement)` dispatching by node type — `RecipeNodeElement` → `EditRecipeNode` → real `EditRecipePanel` + companion `RecipePanel`; every other node type → real `EditFlowPanel`. Both wrapped by the ported `FloatingTooltipControl` equivalent (§9's canvas-overlay host), sized via `EditPanelViewportLayout`, positioned via `EditPanelScreenLayout`.
- **`GraphCanvasControl.ShowChooserAsync` / `PlaceholderChooserWindow`** (`GraphCanvasControl.cs:561-572`) — currently a modal `Window.ShowDialog`, plain search box over a flat label list, no temperature-range filtering, no spoil/plant sub-choosers, no assembler auto-selection. **Should become**: a real `ItemChooserPanel`/`RecipeChooserPanel` port (§2), hosted as a canvas overlay (not a separate window — see §9's hosting recommendation), with the full group/subgroup/filter/footer-button surface.
- **`GraphCanvasControl.AddItemAsync`/`AddRecipeAsync`** (`GraphCanvasControl.cs:580-608`) — each currently does one chooser call and always creates a disconnected Supplier (item) or Recipe (recipe) node. **Should become**: upstream's two-stage `AddItem` → `AddNewNode(Disconnected)` flow — an item chooser, then a second chooser to pick Consumer/Supplier/Passthrough/Spoil/Plant/Recipe for that item, including the multi-origin spoil/plant sub-pickers and per-item-type assembler/fuel auto-selection `ProcessNodeRequest` does.
- **`GraphCanvasControl.HandleNewNodeRequestedAsync`, `DraggedLinkElement.EndDrag`'s new-node outcome** (`GraphCanvasControl.cs:626-650`) — currently approximates upstream's `RecipeChooserPanel` filtering with a flat `ProductionRecipes`/`ConsumptionRecipes` list (no temperature-range search). **Should become**: a real `RecipeChooserPanel` invocation pre-filtered to the dragged item as key item (`AsIngredient`/`AsProduct` set from the drag direction), matching upstream's `IngredientSet`/`ProductSet`-based search exactly, including the footer alt-node buttons.
- **`GraphCanvasControl.BuildBackgroundMenu`, "Add Item"/"Add Recipe"** (`GraphCanvasControl.cs:553-559`) — already wired to the placeholder chooser via `AddItemAsync`/`AddRecipeAsync` above; no separate stub, just needs those two methods to route through the real chooser once it exists.

## 9. Porting sequence (dependency-ordered)

Sizes are rough source-LOC estimates (upstream), not 1:1 output LOC. **Hosting-model recommendation baked into this sequence: chooser and edit panels are canvas-floating-panels (an Avalonia overlay layer inside/above `GraphCanvasControl`, not separate `Window`s); `SettingsForm`/`GraphSummaryForm`/`PresetComparatorForm` are genuine Avalonia `Window`s, all three modal (`ShowDialog`) — see reasoning below the list.**

1. **Floating-panel host primitive** (~130 LOC of upstream math to re-express in Avalonia terms: `EditPanelScreenLayout` 54 + `EditPanelViewportLayout` 75). Build the Avalonia equivalent of `FloatingTooltipControl`'s panel half: an overlay `ContentControl`/`Canvas`-positioned child hosted inside `GraphCanvasControl`'s visual tree (not a `Popup`, since upstream panels receive normal focus/keyboard input alongside canvas hit-testing), with the screen-clamp and viewport-size-then-position math ported verbatim. Everything below depends on this. Validate with a trivial placeholder panel before touching real content.
2. **IRChooserPanel real port** (~2450 source LOC: 908+821+490+225+61 across IRChooserPanel.cs/.Ui.cs/.Designer.cs/ChooserIconGrid/ChooserLayout) — replaces `PlaceholderChooserWindow`. Build `ChooserLayout` constants, `ChooserIconGrid` (10×8 + scrollbar), then `IRChooserPanel` base (filter row, group buttons, search) with `ItemChooserPanel`/`RecipeChooserPanel` subclasses (§2 in full, including footer alt-node buttons). Depends on step 1's host. This is the single biggest item and unblocks the most already-half-working P4 flows.
3. **Wire chooser into existing P4 call sites** (~small glue, <100 LOC): replace `AddItemAsync`/`AddRecipeAsync`/`HandleNewNodeRequestedAsync`'s placeholder calls (§8) with the real chooser, restoring the two-stage `AddItem`→node-type-picker flow and the spoil/plant multi-origin sub-pickers.
4. **EditFlowPanel** (~340 source LOC: 104+205+31) — the smaller, standalone edit panel. Good second validation of the host primitive against real editing controls (rate, fixed/auto, key-node) before tackling the much larger recipe editor.
5. **EditRecipePanel + RecipePanel + RecipeToolTip** (~2130+23+112 ≈ 2265 source LOC) — the largest single panel: assembler/module/beacon/fuel pickers, the always-visible stat readout card, neighbour/extra-productivity fields (§3 in full). Depends on step 1; benefits from step 4's host-primitive shakeout.
6. **Left-click-to-edit wiring** (~small glue): replace `EditNode`'s no-op stub (§8) with real dispatch to steps 4/5's panels by node type.
7. **SettingsForm** (~2524 source LOC: 644+1880) — self-contained modal window, no dependency on steps 1-6. Presets tab, Enabled Objects tab (7 sub-tab `ListView`s + Load-from-save/Assign-from-science-packs), Graph Options tab (6 group boxes, including the currently-unmapped Solver Options group — needs 4 new `AppSettings` fields, §5). Largest single Forms port; do it as one self-contained unit since its tabs share almost no code with the canvas-floating panels above.
8. **GraphSummaryForm** (~1913 source LOC: 653+1260) — independent read-only report window; can be built in parallel with step 7 if resourcing allows, since it has no shared surface with SettingsForm beyond both being plain Avalonia windows.
9. **PresetComparatorForm** (~1196 source LOC: 521+675, plus porting `SyncListView`'s scroll/selection-sync behavior, ~54 LOC) — its only entry point is SettingsForm's "Compare Presets" button, so build it after step 7 has that button in place, even though the diffing logic itself is independent.

Steps 1-6 (~7500 source LOC) are the canvas-floating-panel half of phase 5 and directly close out the P4 touchpoints in §8; steps 7-9 (~5700 source LOC, ~13,200 total) are the three self-contained Avalonia-window dialogs and can slip to a later milestone independently of the canvas work without blocking any P4 editing flow.

**Hosting-model reasoning**: upstream deliberately keeps the chooser and edit panels attached to the canvas as child controls rather than separate windows specifically so they can track the graph's pan/zoom and the edited node's position (`EditPanelViewportLayout`/`ScreenLayout` exist *only* because these panels live inside the same coordinate space as the canvas), and so a plain click on the canvas cancels them (§7's click-outside/`ClearFloatingControls` semantics) without the user needing to manage a separate OS window. Porting them as genuine Avalonia `Window`s — which is what the current P4 placeholder does — throws away both properties: a real window doesn't reposition when the canvas pans, and dismissing it requires an explicit close action rather than "click elsewhere," which is a real UX regression from upstream, not a neutral Mac-native adaptation. `SettingsForm`/`GraphSummaryForm`/`PresetComparatorForm` have no such coupling to canvas coordinates upstream (they're already separate `Form`s there, both genuinely modal via `ShowDialog`), so porting them as modal Avalonia `Window`s is the direct, uncontroversial equivalent.

### Cross-cutting: the Quality system touches nearly every panel

Factorio's Quality system (2.0-era: items/recipes/buildings can exist at multiple quality tiers) surfaces independently in three of the panels covered here, and a port that treats it as a single shared concern will save real duplicated effort: `IRChooserPanel`'s `QualitySelector` (§2) restricts which `IQuality` a picked item/recipe resolves to; `EditRecipePanel`'s own `QualitySelector` (§3) does the same for an existing node and additionally rebuilds the assembler/module/beacon option lists on change; and `SettingsForm`'s Graph Options tab has a dedicated `QualityStepsInput` (§5, "Maximum Quality Steps") plus a whole "Qualities" sub-tab in Enabled Objects (7th category, §5) for enabling/disabling quality tiers themselves. All three ultimately read the same `DCache.AvailableQualities`/`IQuality.Enabled` surface — worth a shared Avalonia "quality picker" control rather than three independent implementations.

## 10. Biggest porting risks

1. **EditRecipePanel's live-update exceptions are easy to silently drop**: `BeaconCountInput` alone triggers a re-solve while the other two beacon fields don't, and `KeyNodeTitleInput` never re-solves — a naive "call `UpdateNodeValues()` on every field change" port would either over-solve (performance) or under-solve (stale beacon-ratio display) relative to upstream.
2. **`RecipeChooserPanel`'s `RecipeMatchesKeyItem` temperature-range logic** (fluid ingredient/product temperature matching against the key item) wasn't fully traced by this document's research pass — it's referenced in the filter predicate but the `IngredientTemperatureMap`/temperature-range comparison internals live outside the files reviewed here and need their own read before implementation.
3. **`DataObjectCheckedListBox` being dead code is a documentation trap**: the task brief and even upstream's own file layout imply it's what `SettingsForm`'s Enabled Objects tab uses; it actually doesn't (plain virtual-mode `ListView`s do the work) — porting the unused class instead of the real `ListView` pattern would waste effort on something with no live behavior to match.
4. **PresetComparatorForm's per-tab "similar" comparison is inconsistently implemented upstream itself** (Assemblers/Miners/Power always report `similarInternals=true` per a `//QUALITY UPDATE REQUIRED` comment marking it stubbed) — a faithful port must decide whether to replicate this known-incomplete upstream behavior verbatim or fix it, since "port exactly" and "port correctly" diverge here.
5. **Canvas-floating-panel hosting has no direct Avalonia equivalent to lean on**: WinForms' child-`Control`-with-real-focus model doesn't map cleanly onto Avalonia's `Popup`/overlay patterns (a `Popup` steals focus differently and doesn't naturally sit "inside" the same visual tree as the canvas for the coordinate-syncing `EditPanelViewportLayout`/`ScreenLayout` math to work against) — step 1 of §9 carries real design risk that isn't just a mechanical translation, and getting it wrong affects every panel built on top of it.
