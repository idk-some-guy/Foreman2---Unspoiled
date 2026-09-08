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

### Fixed

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
