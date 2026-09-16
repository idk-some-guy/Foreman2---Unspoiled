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
- Kit.FormRow and Kit.FormGroup, the shared label/control-column form layout for Settings and the
  inspector, land in the gallery ahead of the surfaces that will adopt them. Kit.SearchField gained
  a loading spinner slot and Kit.Chip a focus-fallback contract for the row that hosts it.
- Kit.InspectorGroup and Kit.SplitDivider land in the gallery. A collapsed group renders collapsed
  on its very first layout, never realizing its body until it is first expanded. The divider is a
  real 5pt splitter with a hover-revealed grip, tab-stop arrow-key resizing, a double-click reset,
  and an Escape-to-restore drag, ahead of the fixed 300pt inspector edge it will eventually replace.
- Kit.PickCell and Kit.PickGrid land in the gallery, replacing the module/beacon slot's old shared
  box-shadow: selection is now an inset ring and keyboard focus the shared outer ring, so a slot can
  show both at once. An empty slot carries a real plus glyph instead of an 8pt "+", and a missing
  module shows an alert glyph with a tooltip naming it while Delete still clears the slot.
- Kit.ChooserCell and Kit.ChooserGrid land in the gallery, replacing the add-browser's old cell and
  subhead styles. A cell's label may wrap and truncate to two lines, but its tooltip and VoiceOver
  name always carry the untruncated item; the grid is one tab stop with arrow/Home/End/Page traversal
  across section headings, a sticky heading per section, and a staggered entrance that now honours a
  live Reduce Motion toggle on an already-built grid.
- Kit.Scrim, Kit.ButtonRow, Kit.SheetFrame and Kit.AlertFrame land in the gallery. Sheet widths
  collapse to three named sizes; a button row orders itself Other/Cancel/Default with a destructive
  trailing action never taking the default styling or the Return key, so Cancel keeps it; a sheet
  traps Tab inside itself while open and returns focus to whatever opened it on dismiss.
- Kit.PopoverFrame and Kit.RadialRing land in the gallery, hosted by a new Kit.PopoverHost overlay
  layer that clamps a popover to its own bounds and closes it on an outside press, swallowing that
  click rather than also passing it to whatever sits beneath. The popover
  carries the app's translucent material with an opaque fallback and a tail that tracks its anchor,
  falling back from below to above to trailing to leading before it clamps to the window, and moves
  focus into itself on open. The ring stays fixed at 220pt across at every display scale; a slice
  arms the moment the pointer crosses the hub boundary into its wedge and stays armed once the
  pointer leaves the ring, a cursor dot with an accent halo tracks the pointer, arrow and diagonal
  keys arm the same way, and any other key dismisses the ring and opens the same six items as a
  context menu. Both composites carry real automation roles now. PaletteMaterial and TitlebarMaterial
  gained the opaque-fallback pair every material now shares.
- Kit.StatePanel lands in the gallery, drawing the four A25 shapes (empty, no-results, loading,
  error) that FormGroup, InspectorGroup, PickGrid, ChooserGrid, SheetFrame and PopoverFrame will all
  delegate to. Empty, no-results and error refuse to build without a real button, so a surface can no
  longer ship a hint instead of a way out; loading is the one shape that may carry none, and it waits
  400ms before appearing so a fast solve never flashes it. Below 196x260 the panel switches itself to
  a single compact line rather than clip. ChooserGrid, SheetFrame and a new FormGroup state slot now
  wire a real state panel into their gallery examples in place of a stub.
- Kit.ZoomStepper lands in the gallery: a fixed-width readout that never reflows the toolbar between
  25% and 400%, with the minus and plus buttons staying live through a solve. The readout swaps to a
  small spinner after a 400ms grace so a fast solve never flashes it, and to an alert glyph on solve
  failure that opens a host-supplied error popover; the zoom value returns the moment the error clears.
- Kit.ZoomStepper's solve-error alert now opens a real popover it hosts itself, carrying the error
  content and returning the readout to its number on Escape, an outside click, or the error clearing.
  The solving spinner's fade-out actually renders instead of vanishing in the same frame it starts,
  and the readout, micro buttons, and glyph boxes scale with the interface size setting like the rest
  of the kit.
- Kit.PopoverHost now queues a popover add, bring-to-front, or remove that lands mid-layout and
  applies it right after, so opening one from a control's own TemplateApplied handler no longer
  needs a manual workaround. Dismissing Kit.ZoomStepper's error popover leaves the error flag for the
  host to clear, and the readout stays its true fixed size at 150% instead of stretching a few points
  tall.
- Kit.ToolbarRow lands in the gallery: title, document/add/arrange/share-view groups, a search field
  and Kit.ZoomStepper laid out with a fixed 16pt gap between groups and no drawn rule anywhere. As
  the row narrows it collapses in three measured steps: item labels drop to icon-only with a
  tooltip, the search field shrinks to its minimum width, then the trailing group folds into an
  overflow menu button. The title and the zoom stepper never collapse.
- Kit.ToolbarRow's group gaps are now real fixed-width spacer elements rather than arithmetic, its
  collapse decision stays correct after a resize token, an item-width token, or a scale change, and
  an icon-only item keeps its label instead of losing it; the trailing group's overflow menu now
  exposes each action's label to VoiceOver, and the gallery's toolbar row tile shows the search
  field's loading state.
- Kit.PaletteRow and its panel example land in the gallery, replacing the quick-add palette's old
  row and hint styles: a 26pt row with a 22pt icon, staggered entrance and leaving fades, a panel
  height ease as results change, the top row selected by default so Return commits without an
  arrow press, and a compact Kit.StatePanel for no results. A keystroke that turns up the same
  results replays no animation. The palette and browser row-label type sizes retire onto the
  Callout/Caption ramp.

### Fixed

- Kit.SheetFrame now handles ⌘. and ⌘⌫ itself, so they work no matter which control inside the
  sheet has focus; Return in a destructive alert raises Cancelled instead of Closed; a sheet that
  has never been opened lets clicks pass through to whatever sits behind it; going busy no longer
  force-enables a default button a consumer had already disabled. SheetFrame and AlertFrame report
  themselves to VoiceOver, and the alert's icon is excluded from the automation tree. Kit.Button's
  plain style no longer lightens the label weight, and the gallery's alert tiles show the real app
  icon.
- The search field's placeholder now hides the moment typed text is non-empty instead of
  overlaying it; the stepper's value field fills the remaining width when no unit is set, with the
  arrows flush at the control's trailing edge; text and search fields show an accent-soft
  selection highlight when text is selected or dragged; and the spinner's head stays on its track
  through a full rotation instead of swinging off it.
- Kit.FormRow's error ring now draws outside the control's own box instead of thickening it, so the
  control column and the content-to-unit gap hold steady whether or not a row is in error. Its
  label's first line baseline-aligns to the control's own first line, growing the row instead of
  drawing past it when the label wraps to two lines; the label also focuses the control on click
  without a hand cursor and names it for VoiceOver, with the note or error read out as its help
  text. Note and error rows cross-fade, and a row's own height animates off the same clock as it
  grows or shrinks with the note, error, or content. Kit.Chip falls back to the previous chip before
  the owning control when the last chip in a row is removed, and Kit.Spinner now tracks its own
  visibility for the animating class, so Kit.SearchField's loading spinner animates only while
  actually showing, including after the field is detached and reattached.
- The stepper's unit-label spacing now tracks the kit's shared glyph-gap token instead of a fixed
  value.
- The keyboard focus ring on pill-shaped controls (radio buttons, switches) no longer draws
  looping arcs past the control's own edge, and its radius now matches the ring's true drawn
  extent rather than overshooting it; a pop-up's indicator now sits flush against the control's
  trailing edge instead of crowding the label, with a proper gap between the label and the
  indicator/chevron; and every glyph that scales with the interface grows its whole design box
  uniformly instead of stretching the artwork itself past the size the icon sheet drew it at.
- Kit pop-up buttons and menu buttons open a real menu again: the panel picks up its own theme, so
  it is no longer an invisible zero-size popup, and dismissing it by clicking away leaves the button
  ready to reopen instead of stuck open.
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
- The dev-only gallery window (`--gallery`) now fills from the live window background token instead
  of a fixed white, so it follows light/dark like every other window and its kit tiles read against
  the ground they'd actually sit on.
- The keyboard focus ring no longer clips to the corners of the control it outlines, the checkbox
  check mark now scales with the interface scale setting, and the segmented control now reacts to
  clicks anywhere in a segment instead of missing presses off its label text.
- Closing a pop-up button or menu button's menu, by Escape, Return, or a click away, now returns
  keyboard focus to the button, with the focus ring back only when the close came from the
  keyboard.
- The segmented control's selection highlight now tracks its segment's own measured position and
  width, so it no longer drifts off the label at the mini size or at a larger interface scale.
- Kit.PaletteRow's entering rows now actually fade in instead of starting at full opacity, and a
  requery whose top result is unchanged still selects it. PalettePanel exposes IsKey to its rows,
  cancels its pending rebuild and reflow on detach, and re-arms both on reattach; its search header
  is now a flush, bordered Kit.SearchField variant spanning the panel edge to edge at Body type,
  with no hover swap on its rule, and its row body padding matches the figure exactly, closing the
  wasted space below the footer hints. The gallery's palette tiles open inside a real
  Kit.PopoverFrame, one showing the search field's loading spinner.
- Kit.Slider no longer clips its own thumb at Value 0 and 100, and Kit.FormRow no longer clips its
  own error ring's top edge; both controls were self-clipping to their own bounds. A slider's value
  label now baseline-aligns against a form row's label when the slider sits directly in the row, and
  the gallery's own form-row example uses a real Kit.FormRow to show it. A popover's tail now hides
  and reopens with its panel instead of staying on screen after a click outside closes it. The
  radial ring re-arms as the pointer crosses from wedge to wedge outside the hub, not only on first
  crossing the hub boundary. The zoom stepper's solving spinner carries the `animating` class while
  visible.
- The boxed form group's Link opacity row now uses a real Kit.Slider with ShowValue/Unit instead of
  a hand-paired label, and Kit.FormRow's label baseline search skips a control's own hidden text
  instead of landing on it by coincidence. A popover reopens on its own anchor's click and after an
  outside-press dismiss, across repeated cycles, with no leftover layout churn once it settles. The
  radial ring cancels rather than commits when the pointer returns to the hub before release. The
  zoom stepper's solving spinner had no control template at all, so it never actually rendered
  during a live solve; it now sizes and animates correctly, and the gallery needs no special-cased
  workaround to show it. Closing a window with an open zoom-stepper error popover no longer throws.

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
- A new Kit appearance hub tracks the system accent color and the Increase Contrast, Reduce
  Transparency, and Reduce Motion display preferences, widening the focus ring and promoting Border
  to BorderStrong under Increase Contrast and falling materials back to their opaque value under
  Reduce Transparency; no visual change under default macOS settings.
- Interface scale no longer thickens borders, corner radii and the focus ring; text, controls,
  icons and the canvas scale as before.

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
