# UI parity reference — upstream README screenshots

Source: `upstream/README.md` screenshots, downloaded to
`.superpowers/ui-reference/readme-*.png|jpg`. Mapping below is by upstream puu.sh
image ID (order of appearance in README) to local filename.

## readme-5.jpg — "Foreman 2.0" (README top banner, full app view)

Full main window, largest single reference image.

- Title bar icon + "Foreman 2.0" label, top-left.
- Toolbar row (left column of buttons): Save, Settings, Load, Export Image,
  Import Graph, Add Item, Clear Graph, Add Recipe. Laid out as 2 columns x 4 rows
  of buttons, not a single horizontal strip.
- "Gridlines (2n scaling)" group box: Minor Gridlines dropdown (16), Major
  Gridlines dropdown (16), "Show Gridlines" checkbox (checked), "Align Selected"
  button.
- "Graph Options" group box: Base Time dropdown (1 min), "Show Graph Summary"
  button, "Pause all calculations" checkbox (unchecked).
- Chooser panel open, docked top-left over canvas (floating panel, not modal):
  Filter textbox, "Recipe Only" checkbox, "Ignore Assembler" checkbox, "Show
  Disabled" checkbox, "Fuel" checkbox (checked here). Row of category icon tiles
  below filters (6 icons, one highlighted/selected orange). Below that a
  scrollable icon grid (empty/gray cells when no results). Footer buttons:
  "Pass-Through", "Output" (two-button footer here vs three in readme-8 — third
  "Source" button appears context-dependent, only shown when not pre-seeded from
  an item drag).
- Canvas background: light dot/grid pattern.
- Graph nodes visible (recipe/status coloring): green-bordered recipe nodes
  (Paint dish, Empty Paint dish, Glassware, Rubber stoppers, Latex, Latex slab,
  Agar, Cellulose, Moss, Formic acid, Creamy latex, Sodium alginate, Carbon
  dioxide, Iron plate, Copper plate, Sap, Limestone, "Water boiling to 165°c
  Steam", Seaweed, Coal gas). Two nodes ("Paint dish", "Rubber stoppers") show
  an orange diagonal corner-flag + small warning-triangle badge, top-left of node.
- Orange directional guide-arrow floating on canvas (near top, pointing toward
  an off-screen warning node) — confirms "guide arrow" visual is a large filled
  chevron, not a small icon.
- Link lines: multiple distinct colors crossing the canvas (dark red/maroon,
  gray, olive, teal) — color appears tied to item/fluid identity, not just
  status.
- Item I/O tabs on nodes: small square icon chips with numeric flow value below.

## readme-2.jpg — "Main Menu" (### Menu ###)

Toolbar-only crop (no canvas), same controls as readme-5's toolbar/gridlines/
graph-options group, but toolbar rendered as a single horizontal row here (8
buttons: Save, Settings, Load, Export Image / Import Graph, Add Item, Clear
Graph, Add Recipe) — confirms toolbar is responsive/reflows between 1-row and
2-column layouts depending on window width.

## readme-1.png (tiled) — "Base red science for Pyanodons" example graph

Large real-world graph (Pyanodon mod pack), demonstrates canvas at scale.

- "Required Output" node: distinct olive/khaki header band, label "Required
  Output:" then item name ("Automation science pack") — visually different
  from regular recipe nodes (which have plain green header).
- "Infinite Sink: <item>" nodes: pale yellow/khaki fill, green border — sink
  node type has its own color, distinct from source and recipe nodes.
- Two nodes ("Basic substrate", "Petri dish") show the orange corner-flag +
  warning-triangle badge combo seen in readme-5.
- Dense crossing link lines in at least 6 distinguishable colors (maroon, gray,
  dark olive/green, gold/yellow, teal/dark-teal, brown/orange) — confirms link
  color-by-item-or-fluid-type is a real, high-cardinality visual channel, not
  just 2-3 colors.
- Bottom-row source/extraction nodes: "Infinite Source: Wood" (pink/lavender
  fill, distinct from sink's khaki and recipe's green), "Raw coal Extraction",
  "Quartz ore Extraction", "Copper ore Extraction", "Stone Extraction", "Water
  Extraction" — all styled as normal green recipe nodes (extraction is a
  recipe category, not a distinct node-type color).
- Numeric flow labels on item tabs render to 2-4 significant digits (e.g.
  3.333, 741, 1192.3, 24.66) — confirms flow values are not integer-rounded by
  default (matches "Round building count" being an opt-in display setting).

## readme-7.jpg — "Node examples" (### Nodes ###)

Horizontal strip of isolated node examples, several small ones side by side.

- "Infinite Source: Electronic circuit" — pink/lavender fill, green border.
- "Infinite Sink: Electronic circuit" — khaki/tan fill, green border.
- Small unlabeled green node with just one item tab (bare passthrough-style
  stub, no title text) — likely a "simple draw" passthrough node per README.
- Recipe nodes with 4 different border/fill treatments side by side:
  - Dark red border, fixed value "450" on top tab — manually fixed flow that's
    short of demand (matches README's "red border = insufficient incoming
    ingredients").
  - Plain green border, auto value "450" — normal/automatic node.
  - Dark red border again, values "33.33" — another insufficient case.
  - Gold/tan border, "0" value — overproduction/unconnected-output case
    (matches README's golden-border rule).
- Rightmost pair: one node with a single orange warning-triangle badge (top
  left) labeled "Electronic circuit" with plain item tabs (0 values); paired
  next to it a node with orange-red/salmon full-fill background, label
  "electronic-circuit" (raw dev-name, not translated — i.e. unresolved), a red
  "?" icon replacing the item icon, and a second warning-triangle badge — this
  is the "missing recipe after preset change" visual state: full orange fill +
  "?" icon + dev-name-as-label (not just a corner flag).

## readme-6.jpg — "Recipe node editor" (### Recipe Node Options ###)

Panel crop only (no surrounding chrome).

- "# of Assemblers:" row: Auto/Fixed radio buttons (Fixed selected) + numeric
  stepper input (10).
- "Assembler: Assembling machine 3" section: row of 4 assembler-choice icon
  tiles (4th tile is a red-tinted "hand crafting" character icon — matches
  README's note that unbuildable-in-vanilla options render in red/tinted).
  Stat readout to the right of the icons: Energy 700% 2.64MW, Speed 240% 3
  (360 crafts/1 min), Productivity 140%, Pollution 140% 19.69/min.
  "Modules (4/4):" — 4 filled module slot icons + "Module Options:" palette
  row of selectable module icons to click to add.
- "Beacon: Beacon" section: beacon icon tile + stat readout (Energy 960KW,
  Modules 2, Efficiency 50%, #Beacons 12, Total Energy 691.2MW) + 3 numeric
  inputs on the right: "# Beacons:" (4), "/Assembler:" (1), "Additional:" (2).
  "Modules (2/2):" + "Module Options:" row, same pattern as assembler modules.
- Separate info card, top-right, NOT part of the editor controls — a read-only
  recipe summary: item icon + name ("Electronic circuit"), "Ingredients:" list
  (icon + qty, e.g. "Iron plate 1x", "Copper cable 3x"), "Products:" list
  (icon + qty), "Key required science packs:" label (empty in this example),
  "Crafting Time: 0.5 s". This card is a distinct, always-visible reference
  panel alongside the editable controls.

## readme-8.jpg — "Item & Recipe Selection Window" (### Item / Recipe Selection ###)

Chooser panel crop, this instance shown pre-seeded from a node drag (per
README section 4's "Ingredient/Product/Fuel" filters only appear in that mode).

- Filter textbox, "Recipe Only" checkbox (unchecked).
- "Ignore Assembler" checkbox, "Show Disabled" checkbox (unchecked),
  "Ingredient" checkbox (checked), "Product" checkbox (checked), "Fuel"
  checkbox (checked) — confirms these 3 are independent, all-checked-by-default
  toggles, not radio-exclusive.
- Category icon row: 6 tiles, one visibly selected (red/highlighted border) —
  the extraction/power group icon (pickaxe) is present among them.
- Result grid: 5 icon tiles shown in first row, rest empty/gray placeholder
  cells (fixed-size scrollable grid, not a reflowing list).
- Footer: 3 buttons — "Source", "Pass-Through", "Output" (all three present
  here, vs. only 2 in readme-5's chooser instance).

## readme-3.jpg — "Presets" (### Presets ###)

Settings dialog, Presets tab (tab strip: Presets | Enabled Objects | Graph
Options — same 3-tab dialog reused across readme-3/4/10).

- "Current: Pyanodon" label.
- Left list box of saved presets (7 entries shown: Factorio 1.1 Vanilla,
  Industrial Revolution 2, Krastorio 2, Krastorio 2 SE, Nullius, "Seablock 5.7
  - custom", "Seablock 5.7 - original") — preset naming convention includes
  free-text suffixes for variants of the same base pack.
- Right panel: "Mods (read-only):" — scrollable list of mod-name_version pairs
  (e.g. "base_1.1.39", "pyalienlife_1.12.4", ~25 entries visible, clearly a
  long list requiring scroll).
- Below mods list: "Difficulty (read-only):" with "Recipe: Normal" and
  "Technology: Normal" labels.
- Bottom-left buttons: "Import New Preset From Factorio", "Compare Presets".
- Dialog footer: "Confirm" (full-width primary) / "Cancel".

## readme-4.jpg — "Enabled Objects" (### Enabled Objects ###)

Same Settings dialog, Enabled Objects tab.

- "Enabled Objects:" section header.
- "Load from save" button (full width), "Assign based on science packs"
  button (full width) — stacked, not side by side.
- "Filter:" textbox, "Show Unavailables" checkbox (unchecked).
- Sub-tab strip: Assemblers | Miners | Power | Beacons | Modules | Recipes
  (6 categories) — Assemblers tab active.
- Checked list: icon + checkbox + name per row (Stone furnace checked, Steel
  furnace unchecked, Electric furnace unchecked, Assembling machine 1 checked,
  2/3 unchecked, Chemical plant unchecked, Centrifuge unchecked, Rocket silo
  unchecked, Glassworks MK 01 checked, Advanced foundry/Automated
  factory/Ground borer MK 01 unchecked) — scrollbar present, list is long.
- Same Confirm/Cancel footer as readme-3.

## readme-10.jpg — "Graph Options" (### Graph Options ###)

Same Settings dialog, Graph Options tab. Screenshot appears to show the full
tab content without scrolling (no scrollbar visible), but is shorter than the
README's prose list — several described options are not present in this
capture (see cross-check below).

- "Node Graphics:" header.
- "Level of detail:" — Low/Med/High radio buttons (Low selected).
- "Maximum number of graphical objects:" numeric stepper (300).
- Checkboxes (all unchecked except noted): Dynamic link-width, "Abbreviate
  science packs" (checked), Show recipe tool tip, Round building count, Lock
  recipe editor to top left corner, "Display arrows pointing to any node
  errors" (checked), "Display arrows pointing to any node warnings" (checked)
  — note these last two are TWO separate checkboxes in the actual UI, not one
  "Guide Arrows" toggle as the README prose implies.
- "Defaults" section: "Assemblers:" dropdown (Worst Non-Burner), "Modules:"
  dropdown (Speed).
- "Advanced" section: "Enable extra productivity bonus for all entities
  (instead of only miners)" checkbox, "Show unavailable items (DEV)"
  checkbox, "Load barreling crating recipes (DEV)" checkbox — all unchecked.
- Same Confirm/Cancel footer.

## readme-9.jpg — "Export" (## Exporting the graph ##)

Small standalone dialog, "Export an Image" title.

- Path textbox (empty) + "Browse..." button, same row.
- "Scale" group box: 1x / 2x / 3x radio buttons (1x selected) — README only
  mentions 1x/2x in prose, screenshot confirms a 3x option also exists.
- "Export" button, large, right-aligned, spans two row-heights.
- "Transparent Background" checkbox (unchecked), bottom-left.

---

## Cross-check: screenshot elements not obviously named in the feature-parity matrix

Checked against `docs/superpowers/specs/2026-09-01-foreman-mac-port-design.md`
§ "Feature parity matrix". These are candidate gaps — visible, concrete UI
elements the matrix doesn't call out by name, even if loosely implied by a
broader bullet:

1. **"Clear Graph" toolbar button** as a distinct destructive action (clears
   canvas but explicitly does not touch the saved file) — not named in the
   matrix; only "save/load `.fjson`" and general graph editing are listed.
2. **"Align Selected" button** in the gridlines panel — the matrix says "grid
   lines + snap" but doesn't mention an explicit align/snap-to-grid *action*
   for the current selection, as opposed to drag-time snapping.
3. **Node-type base fill color coding** (source = pink/lavender, sink =
   khaki/tan, recipe/spoil/plant = green, passthrough = gray (200,200,200) in
   full-node draw per upstream source — the "green passthrough" read here was a
   screenshot guess; simple-draw passthroughs render as a line, not a green box
   (corrected 2026-09-01 vs canvas-reference §4), "Required Output" = olive/khaki
   header) — the matrix's "Nodes" bullet only lists *status* colors
   (green/red/golden) and "Special mechanics" node types, not that each of the
   4 base node types also has its own resting fill/header color independent of
   status.
4. **Recipe editor's live stat readout + read-only recipe info card**
   (Energy/Speed/Productivity/Pollution/Total Energy numbers next to the
   assembler/beacon pickers; separate Ingredients/Products/science-pack/
   crafting-time reference card) — the matrix's "Recipe/building config"
   bullet covers the *editable* controls (assembler, modules, beacon, fuel)
   but not these two always-visible read-only displays.
5. **Missing/unresolved-recipe visual state**: full orange-salmon node fill +
   red "?" icon replacing the item icon + raw dev-name-as-label (vs. the
   translated name) — the matrix's "missing recipe/item handling across
   preset versions" bullet (under Import/export) doesn't specify this is a
   distinct, more severe visual treatment than the ordinary orange-fill
   "node has errors" state described elsewhere in the README.
6. **Settings dialog Graph Options tab appears to omit, in the actual
   screenshot**, several options the README prose describes: "Draw arrows to
   show direction on link lines", "Flag over or under supplied nodes",
   "Defaults (node direction, up/down)", "Defaults (smart direction)", and
   "Defaults (simple-draw passthrough)". The matrix does list all of these
   (from the prose), so this is not a matrix gap — but it means no screenshot
   evidence confirms their exact widget layout; worth a follow-up screenshot
   or upstream source check before phase 5/7 implementation.

## Correction (Task 4, phase 2 app shell)

The "Clear Graph" toolbar button named in the readme-2 and readme-5 sections
above, and flagged as a matrix gap in cross-check item 1, does not exist in
current upstream source. Checked directly against
`upstream/Foreman/Forms/MainForm.Designer.cs` and `MainForm.cs`: the only
such button is `NewGraphButton`, captioned "New Graph", wired to
`NewGraph()`. `ClearGraph()` exists only as an internal
`ProductionGraphViewer` method called from `MainForm.cs`, never a toolbar
command. The README screenshots' "Clear Graph" label reflects an older
upstream build than the one checked out here; phase 3-6 implementers should
treat "New Graph" as the only such button and can disregard cross-check
item 1's gap.
