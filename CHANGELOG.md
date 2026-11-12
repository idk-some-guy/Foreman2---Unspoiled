# Changelog

All notable changes to Foreman2 - Unspoiled are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Sync research from a Factorio save (⌘R). Recipe and mining productivity follow the save's research, every bonus can be edited by hand in the new Research window, and the graph keeps what it synced, so it solves without the save.

## [2.0.0] - 2026-11-10

Foreman now supports Factorio 2.1. Import presets from Factorio 2.1.23 or newer, or start from the 2.1
presets that come with the app, and quality, modules and machine limits behave the way the game does
on a 2.1 save. Your 2.0 presets and graphs work as before.

### Added

- Import presets from Factorio 2.1.23 or a later 2.1. Foreman picks the right exporter from the game
  version it finds.
- Factorio 2.1 Vanilla and Factorio 2.1 Space Age presets come with the app, beside the 2.0 ones.
  New graphs start on 2.1 Vanilla. A preset you already chose stays as it is, and none of the four
  built-in presets can be replaced by an import or deleted from Settings.
- On 2.1 presets, quality yields, module bonuses and machine effect limits follow the 2.1 rules: each
  higher quality tier chains from the one before it, modules give the game's own per-quality values,
  and a module stack can no longer push a machine past its limits. 2.0 presets keep their numbers.
- A spoil node's result quality follows the item's own quality rules in 2.1.
- A recipe that cannot be set to a quality shows a warning and offers the lowest tier that keeps its
  settings and links.
- Plant nodes can run in an agricultural tower with productivity modules, for towers that a mod
  gives module slots.
- Each preset in Settings, the Welcome window and the Preset Comparator shows the Factorio version
  it came from, and presets made by an older version of Foreman say so. After such a preset loads,
  Foreman tells you to import it again from Factorio to get the latest fixes.
- A graph library. Graphs live in Foreman's own database and save themselves two seconds after your
  last change, and again when you close the graph, open another one, quit or switch to another app.
  File ▸ Rename… and File ▸ Duplicate (⇧⌘S) work on the open graph, Open Recent lists the ten latest
  graphs with Clear Menu, and every graph keeps a thumbnail.
- Version history. Saving records a version, and so do opening a changed graph and every 30 minutes
  of editing; versions are kept hourly for a day and daily for a month. File ▸ Revert To ▸ Browse
  Versions… restores any of them and keeps the graph you had as a version too.
- A Welcome window (Window ▸ Welcome to Foreman, ⇧⌘1). New Graph and Open… sit above the presets for
  new graphs, and every graph in the library is listed on the right, newest first, with a filter
  field. Each graph's menu opens, duplicates, renames, exports or deletes it. A graph whose preset
  is no longer installed is dimmed and offers Choose Preset…, which opens it with the preset you
  pick. Choosing a preset loads it in place with a progress bar and Cancel, and New Graph then uses
  it.
- Show when Foreman opens decides how the app starts: with it on, only the Welcome window opens;
  with it off, your most recent graph reopens, or a new Untitled Graph if the library is empty.
  Closing the last graph window brings the Welcome window back when the setting is on. When it is
  off, Foreman keeps running with no window, the application menu offers New Graph, Open…, Open
  Recent and Welcome to Foreman, and a click on the Dock icon opens a new graph.
- Drop a `.fjson` file on the Dock icon to open it, even while Foreman is still launching.
- An inspector sidebar on the right edge of the canvas edits the selected node in place: rate (Auto
  or Fixed), rate unit, key node, low priority, building, fuel, modules, beacon and beacon modules,
  and quality. Selecting several nodes shows how many are selected, and with nothing selected it
  offers Add Recipe. View ▸ Show Inspector (⌥⌘I) and Pin Inspector (⇧⌥⌘I) toggle it, and dragging
  its edge resizes it between 260 and 380 pt. The pin and the width survive a restart.
- Buildings, beacons and fuel are picked from icon grids, with the building's speed, productivity
  and pollution beside its name. A recipe that only one building can craft says so, and a long list
  of candidates becomes a pop-up menu. Click a module to equip it into the selected slot and press
  Delete to empty a slot. When a node's recipe is missing from the current preset, the inspector
  offers Open Presets and Pick Another Recipe and keeps the rest of the node editable.
- The Quick Add palette. Double-click empty canvas, choose Add Item or Add Recipe, or use the canvas
  context menu, then type to search items and recipes. Return adds the selected result and closes
  the palette, ⌥Return or an ⌥-click adds it and keeps the palette open, and ⌘Return opens the full
  browser with the same search. Opened from a link drag, it lists only recipes that make or use the
  dragged item. A new node flashes once so you can see where it landed.
- The Add to Graph browser window: a category sidebar, a grid of items or recipes grouped under
  pinned subgroup headings, a Quality pop-up, a Filter menu (Show unavailable, Show hidden, Ignore
  assembler) and an Add as pop-up for the node's role (Source, Passthrough, Output, Spoil, Unspoil,
  Plant, Unplant, also ⌘1 to ⌘7). Unavailable items are dimmed but can still be added. The window
  remembers its size and last category; ⌘F focuses its search and Escape closes it.
- Releasing a link drag over empty canvas opens a radial menu with Add…, Output or Source,
  Passthrough, Spoil or Unspoil, Plant or Unplant, and Cancel, each always in the same place. Point
  at a wedge or use the arrow keys, then click or press Return; Escape cancels the link. Any other
  key swaps the radial for an ordinary menu with the same choices.
- Links can be selected and routed one by one, Rounded or Orthogonal, from View ▸ Link Routing, and
  Settings ▸ Appearance sets the routing for new links. Double-click a link or use its context menu
  to add a waypoint, drag waypoints to move them, and remove them with a double-click or Delete.
  Moving both ends of a link carries its waypoints along. Graphs from earlier versions open with the
  default routing and no waypoints.
- Find Node in the toolbar and in Edit ▸ Find (⌘F, then ⌘G and ⇧⌘G to step). It matches a node's
  name or key title, shows a "1 of 3" count, pans each match into view without changing the zoom and
  outlines every match on the canvas.
- A solve status indicator beside the zoom readout shows a spinner while a long solve runs and a
  warning triangle when one fails. The triangle, or View ▸ Show Solve Error, says in plain words why
  the graph can't be solved, for example that its fixed rates and limits can't all be met, and
  offers Show Node and Try Again. While calculations are paused, the same button resumes them.
- Dark Mode. Foreman follows the system appearance, or you can choose Light or Dark in Settings ▸
  Appearance. The canvas, tooltips, recipe card, annotation handles and off-screen arrows follow it,
  and the system accent colour is used throughout.
- Settings ▸ Appearance sets the interface scale from 100 to 200 % in five steps, independently of
  the canvas zoom.
- Shape and text annotations pick colours with a colour well: 32 swatches and a Hex field that takes
  3 or 6 digits. The arrow keys preview each swatch on the canvas, Return or a click keeps it, and
  Escape or ⌘. restores the old colour. Fill transparency is an Opacity slider in percent.
- Edit ▸ Select All (⌘A) and Deselect All (⇧⌘A). In a text field, ⌘A still selects the text.
- Keyboard control of the canvas. ⇥ and ⇧⇥ step through nodes in reading order, ⌥ with an arrow key
  moves to the nearest node in that direction (add ⇧ to extend the selection), ⌥⇥ steps through a
  node's links, Return opens the inspector on the focused node, and Escape returns from the
  inspector to the same node.
- VoiceOver support across the app. It reads nodes and their item tabs (the item, input or output,
  its rate, and whether it is short of supply or overproducing), links, Graph Summary rows, find
  results and solve failures. Sliders, steppers and segmented controls are read with their values,
  and text cut short on screen is read once in full.
- Increase Contrast strengthens item tab outlines and widens the focus ring, Reduce Transparency
  gives popovers and the toolbar an opaque background, and Reduce Motion turns slides, glides and
  cross-fades into instant changes or plain fades. All three apply from the first window and follow
  changes while Foreman runs.
- Text cut short with an ellipsis, in recent graphs, palette results, pop-up buttons, chips, sidebar
  rows and empty-state messages, shows in full in a tooltip.

### Changed

- The menu bar follows macOS conventions. Items carry the standard names (New Graph, Open…, Export
  Image…, Settings…). View holds the zoom commands (⌘+, ⌘−, Actual Size ⌘0, Fit to Graph ⇧⌘0), Show
  Gridlines (⌥⌘G), Pause All Calculations (⌥⌘P), the gridline steps, the rate unit, Show Toolbar
  (⌥⌘T) and Enter Full Screen (⌃⌘F). Window has Minimize (⌘M), Zoom, Bring All to Front and Preset
  Comparator, and Help links to the online help (⌘?), the keyboard shortcuts, the GitHub project and
  the issue tracker. With no graph open, the items that need one are dimmed.
- ⌘. cancels whatever is in progress: a running solve first, then an open popover or panel, then the
  frontmost sheet. Holding ⌘ with W, A, S or D no longer pans the canvas.
- The toolbar sits in the titlebar, with icon-and-label buttons and the graph's name beside the
  window controls. As the window narrows, the labels drop to icons and the last group folds into an
  overflow menu. A zoom stepper shows the current zoom, and the gridline and graph options sit in a
  View Options popover. Import Graph and Help moved to the menu bar, and Settings opens from the
  application menu (⌘,). View ▸ Show Toolbar hides it, and the choice survives a restart.
- Dialogs open as sheets attached to their window instead of separate windows, and an alert raised
  from a sheet stacks on top of it. Tab stays inside an open sheet, and focus returns to where it
  was when the sheet closes. In a destructive alert Return cancels and ⌘⌫ confirms.
- Sheets report problems where they happen. A field with a problem shows it underneath and takes
  focus, and a failed export, import or preset load keeps the sheet open with the reason and Try
  Again. Image export runs in the background with a progress bar and a working Cancel.
- Settings is a window of its own with six panes (Presets, Enabled Objects, General, Advanced,
  Annotations, Appearance), switched with ⌘1 to ⌘6. Changes apply as you make them, and General,
  Advanced, Annotations and Appearance each have Restore Defaults. Enabled Objects filters by
  category, by enabled state and by name, and the two larger panes remember their size.
- Preset Selection opens with the best-matching preset selected, ranks presets by mods, then items,
  then recipes, and lets you jump to a preset by typing its name.
- The preset loading window names the preset and shows its progress. After launch, Cancel, Escape,
  ⌘. and ⌘W stop the load. A failed load offers Try Again and Choose Another Preset…, or at launch
  Use Default Preset and Quit Foreman, in place of the old startup alerts about corrupt presets.
- The Preset Comparator has a tab for each of its eight categories (⌘1 to ⌘8) and lists what is only
  in the left preset, in both, and only in the right preset. Objects in both presets share one row
  marked Similar or Different.
- Graph Summary is a window beside the graph instead of a sheet over it, and stays open while you
  edit. Buildings, Items and fluids and Key nodes sit in one segmented control with a Show menu.
  Rows follow every solve and keep their sort, filter and scroll position; unlinked inputs are
  tinted red and unlinked outputs or surplus orange; double-clicking a row selects its nodes and
  pans to them. Building and power totals appear under the Buildings tables.
- Nodes are drawn as cards with a coloured strip for their type, the icon and name, and a detail
  row: building count, beacon and productivity for recipes, or "Source", "Output" and "Passthrough",
  replaced by a "Fixed 90/s" or "Limit 240/s" chip once a rate is set. Saved graphs open where they
  were saved.
- Node states differ in shape as well as colour. A selected node gets an orange border, an
  overproducing one a dashed border, an undersupplied one a double red border with its short inputs
  in red, and a node with an error a red border. Each node shows at most one badge: Unavailable,
  Error, Short, Warning or Over. The Error and Warning badges keep the old click-to-fix and
  right-click menu, and a disabled or unavailable node draws faded.
- Rates carry a suffix for every unit (/s, /min, /5 min, /10 min, /30 min, /h) and use k, M and G
  from 10 000 instead of scientific notation. Changing counts and rates cross-fade, and Align
  Selected glides nodes into place.
- Canvas text never draws below 10 pt at 100 % and grows with the interface scale. Zooming out hides
  labels once they would be too small to read, rates first and names last.
- Gridlines are dots at the minor and major steps and drop out when they would crowd together.
  Pausing calculations frames the canvas in the warning colour and shows a "Calculations paused"
  label that resumes them when clicked. The origin lines show only while a graph is empty.
- A graph that can't be solved keeps the rates and link throughputs of its last good solve, shown in
  grey italic as out of date, instead of dropping to zero.
- Links that run backwards, or join two flipped nodes on the same side, route around the nodes with
  a clear margin.
- Exported images leave out selection borders, find outlines, focus rings, link highlights and
  waypoint handles.
- The About window shows the app icon, name, version and tagline; Escape and ⌘W close it.
- Solving runs in the background, so editing, panning and the menus stay responsive during a long
  solve, and ⌘. stops it with the last rates left in place. Saving a large graph no longer pauses
  editing either.
- Foreman uses a new solver that gives identical results. The bundled solver shrinks from about 55
  MB across 100 files to 4.4 MB in one file, and the first solve after launch is much faster.
- Icons load as they are needed, so the bundled Space Age preset loads about four times faster.
- Settings, presets and preset icons are stored in a single local database. Your existing settings
  and imported presets move over automatically the first time they are needed.

### Fixed

- Graphs with a low-priority recipe failed to solve. Settings carrying the bad priority value are
  repaired automatically.
- About Foreman 2 opens Foreman's own About window.
- A rate unit picked from the View menu was lost unless the app closed cleanly. It is saved straight
  away and stays in step with the inspector and View Options.
- After opening a file or cancelling a save dialog, the menu bar no longer looks stuck, and a ⇧ held
  during a drag no longer stays locked to one axis once a file picker takes focus.
- Closing the shape or text properties without pressing OK discards the colour and text changes you
  previewed.

### Removed

- Import of saves from before Factorio 2.0.
- Save As… and the prompts to save before closing. Graphs save themselves; use File ▸ Duplicate
  (⇧⌘S) for a copy and File ▸ Export as .fjson… to write a file. Open… adds the file to the library,
  and opening the same file again opens the same graph.
- The floating recipe and flow editors and the canvas's hover recipe tooltip. The inspector sidebar
  replaces both, and shows the recipe card.
- The on-canvas item and recipe choosers, replaced by the Quick Add palette, the Add to Graph
  browser and the link-drag radial menu. The Recipe Only filter setting no longer has any effect.
- The Recipe tooltips and recipe name filter settings, the General pane's Canvas group and the View
  menu's Level of Detail item. Level of detail, Simplify above, icon size, link arrows and science
  packs are set in Settings ▸ Appearance.

## [1.0.2] - 2026-11-10

### Fixed

- Right-clicking a node or pressing Ctrl+V on Linux no longer freezes Foreman.

## [1.0.1] - 2026-09-03

### Fixed

- The macOS menu bar named the app "Avalonia Application" instead of Foreman 2.

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

[Unreleased]: https://github.com/idk-some-guy/Foreman2---Unspoiled/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/idk-some-guy/Foreman2---Unspoiled/releases/tag/v2.0.0
[1.0.2]: https://github.com/idk-some-guy/Foreman2---Unspoiled/releases/tag/v1.0.2
[1.0.1]: https://github.com/idk-some-guy/Foreman2---Unspoiled/releases/tag/v1.0.1
[1.0.0]: https://github.com/idk-some-guy/Foreman2---Unspoiled/releases/tag/v1.0.0
