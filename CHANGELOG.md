# Changelog

All notable changes to Foreman2 - Unspoiled are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- True dark mode: follows the system appearance, with manual light/dark override.
- Adjustable interface scale (100-200%) in Settings, independent of the graph canvas's own zoom.
- A full menu bar: canvas zoom (Zoom In/Out, Actual Size, Fit to Graph), a Window menu
  (Minimize/Zoom/Bring All to Front), and a real About panel. Gridlines, icon view, pause, minor/
  major gridline steps, and rate unit are now menu items too, alongside their existing toolbar/
  popover controls. Undo/Redo appears as a disabled placeholder for work not yet built.
- A zoom stepper on the toolbar, showing the canvas's current scale with quick in/out buttons next
  to the menu bar's own zoom commands.
- The Save button tints when the graph has unsaved changes, and Align Selected disables itself when
  nothing is selected.
- An inspector view model tracking the canvas selection: it shows the Recipe or Flow segment for a
  single node, a "N nodes selected" count for multiple, and stays visible when pinned or opened
  from the menu even with nothing selected.
- The inspector sidebar itself: a 300pt overlay on the right edge of the canvas showing the
  selected node's icon, name, and a Recipe/Flow/Supplier/Consumer/Passthrough/Spoil/Plant
  subtitle. Showing or hiding it never resizes the canvas underneath.
- View menu's Show Inspector and Pin Inspector are now live toggles (⌥⌘I and ⇧⌥⌘I). Pinning the
  sidebar persists across restarts.
- The inspector's Flow segment: rate (Auto/Fixed with a live value), key node title, and a
  simplify-throughput toggle for passthrough nodes, for every non-recipe node type. Selecting a
  supplier, consumer, passthrough, spoil, or plant node now edits it right there in the sidebar
  instead of opening a floating panel.
- The inspector's Recipe segment (Rate/Assembler/Fuel groups): assembler count with an Auto/Fixed
  toggle, low priority and key node fields, an assembler picker with live speed/productivity/
  pollution stats, average neighbours and extra productivity, and a fuel picker for burner
  assemblers. Built on a new reusable PickGrid control for icon pickers.
- The inspector's Recipe segment now covers Modules and Beacon too: an equipped-module grid you tap
  to remove, an options grid to add, a beacon picker with its own module grid and the three beacon
  steppers, and a quality mini-select under the identity row's name/subtitle that re-quals the
  node's assembler and updates its own label and the subtitle immediately.
- Quick Add palette: cursor-anchored search over items and recipes, backed by a shared search/
  ranking service; Enter places and keeps it open.
- Add to Graph browser window: categories, a labelled grid, live Show Unavailable/Hidden/Ignore
  Assembler toggles, a quality select, footer role buttons (Source/Pass-Through/Output/Spoil/Plant),
  a Spoil/Plant origins flow, and a Compatible-with chip for link drags.
- A radial menu for releasing a link drag over empty space, with its own slice set following drag
  direction: Add…/Source/Passthrough/UnSpoil/UnPlant/Cancel from an input tab, Add…/Output/
  Passthrough/Spoil/Plant/Cancel from an output tab, with the Spoil/UnSpoil/Plant/UnPlant slices
  appearing only when the dragged item actually has that data. Source/Output/Spoil/Plant place the
  linked node directly; UnSpoil/UnPlant do too with a single origin, otherwise they open the browser
  on its Origins-of chip. The Quick Add palette no longer lists the linked item itself under a link
  context, since the radial now owns that role.
- Dialogs attach to their window as sheets, dropping in flush under the titlebar over a scrim
  instead of opening as separate windows.
- The graph summary, text/shape annotation properties, and confirm/warning/save-before-continuing
  alerts now attach as sheets too.
- Settings is a non-modal window with toolbar tabs; changes apply as you make them.

### Fixed

- An alert raised from inside an open sheet (a mod-mismatch or overwrite confirm, a warning)
  stacks on top of it as a second sheet.
- The inspector sidebar now overlays the canvas instead of pushing it. Its identity header, quality
  mini-select, recipe card (folded in from the canvas's retired hover tooltip), groups, steppers,
  and toggles are restyled to match the design mock: proper padding, a titled group per section
  with a disclosure chevron only where it actually collapses, label-left rows, and pill-style
  switches without "On"/"Off" text.
- Low-priority recipes now work: the default priority weight overflowed the solver and failed any
  graph using one. Existing settings carrying the broken value are healed automatically.
- The toolbar now uses real vector icons matching the design mock instead of placeholder glyphs,
  and every button pairs its icon with a text label. The window title shows the open document's
  name instead of the app name, with an "Edited" marker while the graph has unsaved changes.
- The View Options popover now light-dismisses like a real macOS popover: a click anywhere outside
  it closes it instead of waiting for its own button to be clicked again.
- The application menu shows About Foreman 2 again instead of Avalonia's own About panel. The
  Minor/Major Gridlines and Rate Unit radio items, permanently grayed out, are enabled again.
  Opening a file or cancelling Save As no longer leaves the menu bar looking stuck engaged, and a
  Shift held during a drag no longer gets stuck locking the axis once a file picker steals focus.
- About Foreman 2 now shows the real design-mock panel: the app icon, centered name, version, and
  tagline, instead of a plain left-aligned message box with no icon.
- The Save button's dirty tint no longer lights up on a plain solve with nothing to save. It now
  tracks one genuine graph-mutated signal instead of five, so it lights on real edits, node drags,
  and annotation changes, and stays off when the graph just recalculates its rates.
- Deleting the inspected node, whether with the canvas Delete key, a node's own right-click Delete,
  or the error notice's Delete resolution, no longer leaves the inspector open on a node that's
  gone; the sidebar closes with it.
- Selecting two or more nodes now shows the inspector's own "N nodes selected" state instead of
  hiding the sidebar.
- Picking the assembler or quality that's already selected no longer marks the graph as having
  unsaved changes.
- Manually collapsing the Beacon group in the inspector's Recipe segment now sticks; it no longer
  snaps back open on the next unrelated solve.
- The Quick Add palette now grows to fit its results as you type (up to six rows, then scrolls) and
  shrinks back once the query empties, instead of staying pinned to its size at open. Its hint row
  wraps instead of clipping its last entry, and Cmd+Enter now shows as one combined key cap.
- Typing in the Quick Add search box no longer triggers canvas hotkeys underneath it; Space inserts
  a space instead of toggling gridlines. A click on the link-drag radial menu now cancels anywhere
  past its ring, including inside its square hit area, instead of only outside the whole panel.
  AddBrowserWindow and QuickAddPalette build without a startup XAML-loader warning, and the beacon
  icon binding no longer logs a null-binding warning when no beacon is selected.
- No canvas hotkey (gridlines included) fires while the Quick Add palette or another floating panel
  has focus. The Recipe segment's assembler icon and name binding no longer logs a null-binding
  warning either.
- The "Add to Graph" browser's sidebar now sizes itself to its widest group name (up to 220pt,
  ellipsizing with a tooltip beyond that) instead of clipping long names in a fixed 168pt column.
  Its grid cells all share one fixed height regardless of a one- or two-line label, so rows no
  longer drift out of alignment. Clicking a recipe now places it immediately, since a recipe has no
  options left to choose; a following double-click or Enter on that same cell no longer places a
  second copy.
- The "Add to Graph" browser's toolbar chip is a small pill again instead of a block spanning the
  toolbar height, and the search field's text now sits centred in its well. Grid cells hold a fixed
  width instead of shrinking to their label, so columns line up; a selected cell rings its icon well
  rather than filling the whole cell. The sidebar measures its widest realised row instead of a
  hand-tuned constant, so a real preset's group name no longer clips past its right edge. A
  link-drag's own "Add..." opens the browser on Recipes instead of Items, and a subgroup with no
  localised name shows a readable heading instead of its raw internal key. Opening the browser mid-
  press no longer leaves the canvas thinking a mouse button is still down, which used to drag a
  stray selection rectangle on the next plain mouse move.
- Quick Add rows fade and slide into place as they appear, the browser's segment switch and
  filtered-in cells cross-fade instead of jumping, and the link-drag radial menu opens with a brief
  scale-and-fade and fades out before it's removed on close.
- The extraction/power/rocket-launch group in the "Add to Graph" browser's Recipes segment used to
  show as three lines stacked in its sidebar row ("Resource Extraction / Power Generation / Rocket
  Launches"); it's now a single line, "Extraction, Power & Rockets". Any group or subgroup name
  carrying stray whitespace, newlines included, also collapses to a single space as a defense in
  the browser's own name display.
- Browser search results, and a link drag's compatible-recipe list, group under one heading per
  subgroup instead of a flat list, and every cell's tooltip shows its full name and kind.
- Animations run at a steadier pace and cascade top-down; selecting the group or segment that is
  already active, or retyping an equivalent search, no longer rebuilds the list.
- The UI test suite's ~3x slowdown after wiring the appearance hub is fixed: a scale change stopped
  leaking a stale handler onto every test run's App. The factory summary sheet repaints through its
  host instead of a second, redundant subscription, and the Add to Graph browser's own icons and
  MainWindow's browser icon cache now rebake on a theme or scale flip like the rest of the hub's
  subscribers.

### Changed

- Add Item/Add Recipe, the background context menu, and a double-click on empty canvas now open the
  Quick Add palette instead of the old chooser panels; Escape now cancels a link drag in progress.
- Clicking a recipe node now edits it in the inspector sidebar instead of opening a floating panel
  pair; the paired recipe editor and its standalone recipe card are retired.
- The toolbar and window titlebar are unified into one row, macOS-style, with icon-and-label
  buttons for the most common actions; Save As, Import Graph, and Help move to the menu bar only.
  Gridlines and graph-options controls (rate unit, icon view, pause) now live behind a View
  Options button instead of sitting always-open on the toolbar.
- Switched the production solver from Google OR-Tools to HiGHS: a dramatically smaller download
  (the bundled solver drops from roughly 55 MB across 100 files to 4.4 MB in one file) and a much
  faster first solve after launch, with identical solve results.
- Internal modernization of the settings, factory-summary, and preset-comparison windows onto
  CommunityToolkit.Mvvm with compiled bindings; no user-visible behavior change.
- Settings, preset data, and preset icons now live in a local SQLite database instead of
  scattered files; an existing settings.json or preset import is migrated in automatically the
  first time it's needed and isn't written to again.
- Icons decode on first use instead of all at once at preset load, cutting load time for the
  bundled Space Age preset roughly fourfold.
- Image Export, Load from Save, Science Packs, Import Preset, and the cross-preset picker now open
  as sheets sliding down from their owner window instead of separate windows.
- Theme tokens and shared motion timing move into a new Foreman.Mac.Kit library, the first piece of
  a shared UI kit; no visual or behavioral change.
- Color tokens now bind to semantic roles (focus ring, pressed control, warning, success, popover
  and titlebar materials) with the existing hex values as fallback, and the light accent corrects
  to #007AFF. Canvas selection's token renames from Selection to NodeSelected, with an alias layer
  keeping the old name resolvable. The preset comparator's status colors and the last four ad-hoc
  button/field colors (quality picker, preset import, science packs) now route through these tokens
  instead of literal brushes.
- The Kit's type ramp narrows to five steps (LargeTitle, EmphasizedBody, Body, Callout, Caption) plus
  new metric and size tokens (Radius.SheetBottom 10, Radius.Node, ControlHeightRegular/Large,
  InspectorWidthMin/Max, ChooserCell), with the old per-surface keys either aliased onto the nearest
  step or kept resolvable as-is; no visual or behavioral change.
- A new Kit appearance hub tracks the system accent color and the Increase Contrast, Reduce
  Transparency, and Reduce Motion display preferences, widening the focus ring and promoting Border
  to BorderStrong under Increase Contrast and falling materials back to their opaque value under
  Reduce Transparency; no visual change under default macOS settings.
- Every open window and sheet now repaints live on a theme, accent, contrast, or interface-scale
  change, not just at launch: Settings, the factory summary sheet, the preset comparator, the Add to
  Graph browser, the inspector, and the gallery all follow the appearance hub above. The dark-mode
  service that used to drive this on its own now feeds into the same hub.
- The Kit gains its full v2 motion vocabulary (popover, radial sweep, switch knob, segmented
  selection, inspector reflow, disclosure, align tween, tab-label cross-fade, and spinner timings)
  and a `Motion.Transition` factory that the palette and browser entrance animations now build on;
  under Reduce Motion every transition it returns collapses to a same-duration opacity cross-fade.
  No visual or behavioral change with Reduce Motion off.
- Interface scale no longer thickens borders, corner radii and the focus ring; text, controls,
  icons and the canvas scale as before.
- The Kit gains its glyph catalogue: the 29-glyph set from the ratified glyph sheet (New through
  Beacon, the chevron and search magnifier, the radial chooser's nine outcomes, and the six Settings
  tabs), each at 16/17/18/20 pt, plus a shared stroke style for drawing them. The toolbar's own icons
  are untouched; no visual change.
- The dev gallery window now renders from a declarative `GalleryRegistry` of tiles instead of a
  fixed sequence of inline builder calls, and its last two hardcoded colors route through tokens;
  no visual change.

### Removed

- Factorio 1.x support: pre-2.0 save import (`*.json`) and the legacy `OldImport` save-file field.
- The Recipe/Flow segmented toggle in the inspector sidebar: it was display-only, since the segment
  is already chosen by the selected node's type. The identity header now sits at the top instead.
- The on-canvas item/recipe chooser panels, replaced by the Quick Add palette, the "Add to Graph"
  browser, and the radial menu. The Recipe Only filter setting no longer gates anything.

## [1.0.0] - 2026-09-03

First public release: a native macOS port of
[DanielKote/Foreman2](https://github.com/DanielKote/Foreman2) 2.4.0, feature-complete with
upstream. Runs on Apple Silicon Macs, built on Avalonia instead of WinForms.

Linux is supported in this release's build (self-contained `linux-x64` tarball) but has not yet
been runtime-verified on an actual Linux machine; treat it as best-effort until that verification
lands.

### Added

- macOS `.app` bundle and dmg installer, ad-hoc signed, with a badge-free icon matching upstream.
- Linux `linux-x64` self-contained tarball with a flat launcher layout.
- Factorio/Steam install detection and config paths for both platforms.

### Fixed

Three defects present in upstream, fixed in this port rather than carried forward:

- The graph view froze noticeably on the first node placed after launch, while the production
  solver warmed up. The solver now warms up in the background at startup, so that first placement
  is as responsive as every one after it.
- Exporting a view-limited image (rather than the full graph) at any zoom other than 100% produced
  a mis-centered, cropped bitmap. Exports now match what the view actually shows, at every zoom
  level.
- The "graph has unsaved changes" prompt's Yes button silently discarded the graph instead of
  saving it first. Yes now saves, then continues, as its label promises.
