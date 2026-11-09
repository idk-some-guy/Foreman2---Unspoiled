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
- Kit.RecentRow lands in the gallery: a 46pt welcome-window library entry with a preset/node-count
  meta line, a trailing relative time pinned to the row's edge whose tooltip carries the absolute
  date, and a right-click/Shift-F10 context menu (Open, Open in New Window, Duplicate, Export…,
  Show in Finder, Remove from Recents). An unavailable row dims but stays selectable and focusable,
  with the reason folded into its meta line and only Open disabled.
- Kit.SkiaBridge now carries the canvas painters' tab, chip and error border colors as theme tokens
  (formerly the #696969/#B8860B/#8B0000 literals) alongside its existing metrics, re-resolved on every
  theme, scale or contrast change and republished to the painters only when a value moved.
- The titlebar toolbar now composes Kit.ToolbarRow: the document title sits leading, right after the
  traffic lights, with unsaved state shown as the close button's dot instead of trailing "- Edited"
  text; a "Find node" search field sits between the toolbar and the zoom stepper (⌘F focuses it, ⌘G
  and ⇧⌘G step through matches on a node's drawn name or key title); and Settings is gone from the
  toolbar row entirely, reachable only from the app menu's ⌘,.
- The View Options popover now composes Kit.PopoverFrame anchored to its toolbar item, replacing the
  old floating panel and its two group boxes: Kit.FormGroup and Kit.FormRow lay out the Gridlines and
  Graph groups at a new 86pt popover label column, Minor Gridlines, Major Gridlines and Rate Unit are
  pop-up buttons listing every value instead of a scrolling row, and Show Gridlines, Icon View and
  Pause All Calculations stay checkboxes.
- The inspector's shell now composes Kit.InspectorGroup, Kit.SplitDivider and Kit.StatePanel: every
  group carries the same disclosure header, is separated from the next by a single 1pt rule, and
  renders collapsed on first layout with no flash; the sidebar's leading edge is a real divider (drag
  toward the canvas to widen, arrow keys, Home/End, Return reset) clamped 260-380pt and scaling with
  the display factor like the rest of the sidebar; no selection shows a proper empty state with an
  Add Recipe button instead of a line of plain text. The identity row's sub-line now names the node
  type before the building, and quality lives only in its own mini-select.
- The inspector's Recipe group now shows its own header, and when the node's recipe is missing from
  the current preset it swaps its card for Kit.StatePanel.error with Open Presets and Pick Another
  Recipe buttons, while the Rate, Assembler, Fuel, Modules and Beacon groups stay live.
- The inspector's form body now composes Kit.FormRow and Kit.FormGroup at the 108pt inspector label
  column: Low-priority recipe, Key node and Draw simplified are checkboxes instead of switches;
  Quality is a pop-up button carrying its badge icon beside the word; the Auto/Fixed rate toggle is
  a real segmented control; and the module and beacon slots are Kit.PickGrid, with empty slots drawn
  as a plus glyph, a click on an option equipping it into the selected slot, Delete clearing a
  filled one, and Return moving focus to the option grid below it. A building with no module slots
  no longer shows an empty Modules group. Solving no longer rebuilds every pick grid from scratch -
  only the slots whose content actually changed update, so a focused grid keeps its focus and
  selection through a solve. The old Recipe/Flow segmented control stays gone (its type moved to
  the identity row in an earlier pass).
- Building, Beacon and Fuel are now Kit.PickGrid in a new choice mode - a radio group of icon cells
  with one candidate shown as a plain icon-and-name line reading "The only building that crafts this
  recipe.", none enabled shown dimmed with a warning,
  and more than nine candidates falling back to a pop-up; Beacon's set carries a leading "None" cell
  and its old Clear button is gone. Quality's pop-up now carries its badge as a real leading-icon
  slot instead of a separate image beside it. The identity row's building line now appends the
  quality in parentheses when it is not the base tier. The Rate group on both the Recipe and Flow
  segments gained a Rate unit pop-up reading and writing the graph's own rate unit.
- Sheets now open in Kit.SheetFrame and alerts in Kit.AlertFrame. Every sheet shows its title as a
  headline, slides 12pt down from under the toolbar (opacity only under Reduce Motion), and leaves the
  toolbar usable while it is open. Alerts are 300pt wide with the app icon beside a full-sentence
  headline. The save prompt reads Don't Save · Cancel · Save with Save on Return and Don't Save on ⌘D
  or ⌘⌫; discarding an unsaved graph and deleting a preset are destructive alerts where Return cancels
  and ⌘⌫ confirms. A sheet can report work in progress (default disabled, Cancel live, a spinner or a
  full-width progress bar under a sentence of up to three lines, whose fill eases to each new value and
  reads its percent to VoiceOver) or a failure (the default becomes Try Again) in a strip above its
  buttons, keeping its fields on screen.
- The sheet bodies moved onto the kit. Export, Save-file Load, Science Packs and the two annotation
  property sheets are 360pt wide, Preset Import and Preset Selection 460pt, and every title is
  sentence case. Each sheet ends in a trailing Cancel · default row. Path fields fill their form row
  up to the Browse… button, long notes wrap inside the row, and the inspector's Auto/Fixed switch
  keeps its natural width. A preset name under five characters turns its rule note to the warning
  colour. Preset Selection reads "Select a preset" with Cancel · Load Preset, ranks presets by mods,
  then items, then recipes, opens with the best match selected, lines its column headers up with the
  rows, selects by typing a name, and names the full preset and its mods in each row's tooltip. A
  problem with one field now shows under that field and moves focus to it instead of raising an alert.
  A failed export, import, save read or preset load keeps the sheet open with one plain reason above
  the buttons and Try Again as the default; Import and Save-file Load add Show Log…. Export renders on
  a background thread, so its progress bar moves and Cancel works while it runs. The overwrite,
  missing-icon and save-mod-mismatch confirms name their action, and the mismatch lists what is
  missing on each side. VoiceOver can press a science pack. When focus leaves an open sheet it returns
  to the control that last had it, or to the first visible control if that one is hidden.
- Shape and Text properties pick colours with a colour well instead of red, green and blue steppers.
  The well opens a popover with 32 fixed swatches and a Hex field that takes 3 or 6 digits; arrow keys
  preview each swatch on the canvas, Return or a click keeps it, and Escape or ⌘. puts the old colour back.
  "No fill" and "Transparent background" are the None swatch, fill alpha is an Opacity slider in
  percent, the shape type and text alignment are segmented controls, Bold and Italic are spelled out,
  and ⌘Return in the text field presses OK. Export shows the chosen path as a label that keeps the
  file name visible, with "Not chosen" before one is picked, and sizes read like "2140 × 1360 px". The
  import strip counts its four stages instead of repeating Factorio's own progress text. The Font menu
  draws each family in its own face, VoiceOver reads sliders, steppers and segmented controls with
  their values ("Opacity, 40 percent"), and the export path and size labels read their text as the
  value. Every popover now opens with a small settle from 97 % scale, and Tab no longer lands in a
  popover that has closed.
- The Quick Add palette is rebuilt on the kit. It opens where you double-clicked, where a link drag
  was released, or under the Item and Recipe toolbar buttons, and flips to stay on screen. It shows
  at most six rows, and past that ⌘Return opens the browser with the same search. Before you type, it
  explains how to add a node, and a search with no matches offers Browse All…. When it opens from a
  link drag, it lists only recipes that make or take the dragged item, says which in its placeholder,
  and shows a "Compatible with Iron Plate" chip. A long item name is cut short with an ellipsis and
  shown in full in a tooltip, and VoiceOver can press the chip's ✕ as Remove Constraint. Removing the
  chip widens the search and places the next node unlinked at the drop point. Return places the node
  and keeps the palette open. A node placed from the palette or the browser flashes once with an
  accent ring so you can see where it landed. Reduce Motion keeps the flash and only softens its fade.
- The Add to Graph browser is rebuilt on the kit. The Items/Recipes switch moved out of the titlebar
  into the toolbar row, next to the search field, the Quality pop-up and a new Filter menu. The Filter
  menu holds Show unavailable, Show hidden and Ignore assembler plus Reset to defaults, and shows how
  many filters are on. The seven role buttons are now one "Add as" pop-up beside the selection and the
  Close and Add buttons. It always lists Source, Passthrough, Output, Spoil, Unspoil, Plant and Unplant
  in that order, dims the roles the selected item can't take, and remembers your last pick; ⌘1 to ⌘7
  pick the role at that position. Add is the default and double-clicking a cell does the same. The
  Quality pop-up shows each quality's badge. Cells are a uniform 88 pt with the full name in a tooltip,
  and unavailable items are dimmed but can still be added. Search results sit under "Group · Subgroup"
  headings that stay pinned while you scroll. The category sidebar grows to fit its names, trims past
  its maximum width with the full name as a tooltip and as its VoiceOver name, and moves with ↑↓. When
  the Filter menu hides every match, the grid says so and offers Show All; a search that matches
  nothing offers Clear Search. If the preset can't be read or searched, the grid says which and offers
  Try Again. Try Again keeps the message in place while it retries, and a repeat failure is read out
  again to VoiceOver. An icon that can't be decoded shows a picture placeholder instead of an empty
  well. When typing empties the results, focus moves to the panel's button. Opened from a link, the
  browser explains what the link hides and offers Remove Constraint, Show All or Show Item (add the
  linked item itself) where one of them helps. Icons that are still loading show a spinner in the grid while the sidebar stays usable. The window's minimum size is 760 × 520, and it
  remembers its size and last category. ⌘F focuses the search field and Escape closes the window.
- The link-drag radial is rebuilt on the kit ring. It is 220 pt across at every display size and
  always has six wedges in fixed places: Add… at the top, Output or Source next, then Passthrough,
  Spoil or Unspoil at the bottom, Plant or Unplant, and Cancel at the upper left. An outcome the item
  can't take leaves its wedge empty instead of moving the others. A wedge arms as soon as you point
  toward it, and pointing at an empty wedge or back into the hub disarms it. Clicking commits, also
  just past the ring as long as the click is within 140 pt of the center; clicking in the hub or an
  empty wedge cancels, and so does moving more than 140 pt from the center. The arrow keys arm a
  wedge by direction and Return commits it. Escape cancels the radial and the link. Any other
  printable key swaps the radial for a menu with the same outcomes at the release point, while Tab,
  bare modifier keys and ⌘/⌃ shortcuts do nothing. The labels now read Unspoil and Unplant, and VoiceOver hears a
  menu called "Link options" with one item per outcome. Once a radial closes and fades out, it
  leaves the Tab order and VoiceOver no longer finds it.
- The Settings window chrome is rebuilt on the kit. Its six panes (Presets, Enabled Objects,
  General, Advanced, Annotations, Appearance) sit in a labelled tab strip that never shrinks to
  icons. ⌘1 to ⌘6 switch panes, each tab's tooltip names its shortcut, the window title follows
  the pane, and panes swap without a cross-fade. Presets, General, Annotations and Appearance stay
  at 520 × 480. Enabled Objects and Advanced resize between 520 × 480 and 900 × 760, and each
  remembers its own size. The footer keeps the version and gains Restore Defaults on General,
  Advanced, Annotations and Appearance. The button is dimmed while the pane is already at its
  defaults and asks for confirmation before resetting only that pane. VoiceOver reads the strip as
  a "Settings panes" tab group, ⌘1 to ⌘6 do nothing while a sheet is open, and a sheet on a
  window whose root is a dock or stack panel now covers the whole window instead of collapsing to
  zero width.
- The Presets, Enabled Objects, General, Advanced and Annotations panes use the kit form layout:
  titled group boxes, one label column, switches for settings, and a one-sentence note saying what
  each control does. Presets and Enabled Objects fit their window, and only their lists scroll.
  General, Advanced and Annotations scroll as one page behind an auto-hiding scroller, open at the
  top whenever you switch to them, and bring a row and its note fully into view when Tab reaches
  it. Restore Defaults leaves the scroll position where it was. Presets shows Other presets and Mods in preset side by side in one box. The mods list shows a loading line while it
  reads a preset, and a preset file that can't be read gets an inline warning with Retry in the
  list's place instead of an alert. Enabled Objects drops its group boxes: a Category pop-up with
  counts, a filter field, a Show filter (All, Enabled, Disabled) and a Show unavailable box sit
  above the list with their help in tooltips, and the list takes all the extra height when the
  window grows. Unavailable objects are dimmed, and a search that matches nothing says so, with a
  Show All Objects button. Compare Presets… is dimmed until a second preset exists. The preset menu
  reads Use Preset and Delete Preset. The alerts for switching to an imported preset and for an
  unreadable save file use the new wording. Long kit lists now scroll without building every row,
  a kit button row can hold several ordinary buttons, and a stretched kit group box gives its rows
  the extra height.
- The Appearance pane uses the same form layout in three groups. Theme is a System, Light and Dark
  segmented control, Interface scale is a pop-up with the five scale steps, and a new Default
  routing control (Rounded or Orthogonal) is saved with the other settings. Width by throughput is
  a switch and stays off by default. Level of detail, Simplify above, Icon size, Link arrows and
  Science packs sit under Links and Nodes. The pane scrolls as one page like General. The unused
  Recipe tooltips setting and the recipe name filter setting are gone, and the General pane loses
  its Canvas group. The settings database moves to version 4: it drops those two columns and
  stores the inspector width, the add browser's size and last categories, and the two resizable
  panes' sizes, so they now survive a restart.
- The menu bar gains Select All (⌘A) and Deselect All (⇧⌘A) in Edit, with ⌘A still selecting a
  focused text field's text, and an Edit ▸ Find submenu whose Find Node…, Find Next and Find
  Previous drive the toolbar search. File gains Open Recent and Window gains Welcome to Foreman
  (⇧⌘W), both disabled until the graph library and welcome window arrive. View gains a Link
  Routing submenu, disabled until links can be selected, Show Toolbar (⌥⌘T), a disabled Customize
  Toolbar… and Enter Full Screen (⌃⌘F). With the toolbar hidden the window keeps a plain 28 pt
  titlebar with the document title centred, the graph stays put on screen, and ⌘F shows the toolbar
  until the search field loses focus; the choice lasts for the session. Window gains Preset
  Comparator, which needs two presets, the same rule as Settings' Compare Presets…. Help has four
  items that open the project's pages in the browser (Foreman 2 Help ⌘?, Keyboard Shortcuts,
  Foreman 2 on GitHub, Report an Issue…), with an alert offering Copy Link when no browser opens.
  Menu titles follow the macOS forms (New Graph, Open…, Save As…, Import Graph…, Export Image…,
  Settings…), and the disabled Level of Detail item is gone. ⌘. is now cancel-in-flight and belongs
  to no menu item: it stops a running solve first, then closes the frontmost popover or canvas
  panel, then dismisses the frontmost sheet, and Ctrl+period does the same on Linux. ⌘-chords on W,
  A, S and D no longer pan the canvas. With no document open, the save, export and close items, all
  of Edit and the canvas half of View disable.
- The About window is a plain 280 pt window with a 64 pt icon, the name, the version and the
  tagline on the standard type ramp, with no card and no buttons; Esc and ⌘W close it. The preset
  comparator follows the Settings layout: eight category tabs with their own glyphs in the toolbar
  (⌘1 to ⌘8, opening on Items), a "Compare … with …" row, a filter row, and three groups (only in
  the left preset, in both, only in the right preset). The "In both" group pairs each shared object
  on one row with a Match column that reads Similar or Different, and it tints the row too.
  Unavailable objects show dimmed and italic. VoiceOver reads each group's heading with its shown
  count ("In both, 2 recipes") and the "In both" list as a table. The data-load window, titled Loading Preset, names the
  preset and shows one of three stage sentences above the progress bar. After launch, Cancel, Esc,
  ⌘., ⌘W and the close button all stop the load, and it really stops, because the load now takes a
  cancellation token. At launch there is no Cancel, and closing shows a short alert while loading
  carries on. A failed load keeps the window open with Try Again and Choose Another Preset…, or, at
  launch, Use Default Preset and Quit Foreman. That replaces the two startup alerts about corrupt
  presets.
- Keyboard navigation on the canvas: Tab and Shift-Tab step through nodes in reading order, Option-arrow
  moves to the nearest node in that direction, Option-Tab steps through a node's links, and Return opens
  the inspector. Nodes, links and the empty-state and paused panels are exposed to VoiceOver, and wrap
  and placement announcements are spoken.
- Find now rings every matching node on the canvas with a dashed accent outline just outside the
  card, 2 pt wide at any zoom, so matches show on a zoomed-out graph and in image export.
- A solve-status indicator beside the zoom readout shows a solve in progress and a failed solve with
  a warning triangle. With the toolbar hidden it sits at the right end of the titlebar. The triangle
  opens a popover headed "Couldn't solve this graph" with Show Node, which selects the failed group
  and pans to it, and Try Again. View > Show Solve Error opens it from the keyboard, Cmd-period
  cancels a solve, and VoiceOver announces the failure.

### Fixed

- Opening a graph from the library, the Welcome window, Open Recent or a dropped file after closing the window that had it open now shows the graph, instead of an empty window.
- Opening a graph from Open…, Open Recent or the library no longer shows an empty window first: the graph window appears once its contents have loaded, and if the graph can't be opened, nothing is left open but the reason. Cancelling Open… no longer leaves an empty window behind.
- Choosing New Graph, Open or the Welcome window when the default preset has been removed now shows
  the same warning as at launch once, and nothing opens. Closing the Welcome window that one of those actions opened while the preset
  loads now cancels the action, and reopening the Welcome after closing it mid-load shows the preset
  you were loading rather than the previous one.
- The Building/Fuel/Beacon choice grids' value line now reads in the right ink: a selected name in
  secondary text, a "not researched" warning in amber rather than red, and the Building row's stats
  readout back beside the name after a middle dot. A selection the current preset no longer enables
  still shows one selected (missing) cell instead of none. The identity row speaks the building on
  VoiceOver focus, and committing a choice announces the new value; a lone candidate never auto-picks
  itself on a refresh. A pop-up menu's leading item icon now sits after the checkmark column rather
  than displacing it. A rate unit picked on any of the three surfaces - the inspector's own row, View
  Options' pop-up and the View menu's radios - now mirrors into the other two and is saved to disk on
  the pick; View Options and the menu used to write the setting in memory and lose it unless the
  window closed cleanly.
- Kit.ToolbarItem's hover, pressed and on fill now covers the whole item instead of stopping short at
  the padded content; the gallery's 150% chooser, state-panel and sheet tiles scope every scaled type
  key they resolve, so cell labels, sentence line boxes and the alert's icon and its column track the
  tile's scale instead of sitting at 100%.
- Kit.SkiaBridge repaints the canvas once per appearance change. A system light/dark flip reaches the
  appearance hub through PlatformAppearance alone, so the painters no longer walk twice for one OS
  event; ThemeService dropped its own dormant colour-source subscription, since the single-refresh
  guarantee was already AppearanceHub and TokenBridge's alone.
- Tabbing into a module or beacon-module grid no longer arms a slot for replacement: arriving on a
  cell by keyboard only moves the focus ring, and a click, Space or Return is what arms it. Return
  also hands focus to the grid of options below, Delete empties the focused slot and arms the hole it
  left, and Escape backs out one step at a time - first clearing the arm, then leaving the grid. An
  option's Space or Return now equips exactly like a click would. Deleting a filled slot no longer arms
  the wrong module for replacement once the list closes the gap, whichever slot was armed at the time;
  the arm lands on the freed slot at the end. The row under each equipped grid reads the arm state in
  words instead of the old static slot count, and speaks it on VoiceOver as the grid's own value,
  announcing every change without moving focus. A full, unarmed options grid dims to show it has
  nowhere left to put a click, and names why on its tooltip and VoiceOver help text; otherwise each
  option names which slot it would fill or replace. The beacon's own module row gets the same
  treatment as the assembler's.
- Kit.SheetFrame now handles ⌘. and ⌘⌫ itself, so they work no matter which control inside the
  sheet has focus; Return in a destructive alert raises Cancelled instead of Closed; a sheet that
  has never been opened lets clicks pass through to whatever sits behind it; going busy no longer
  force-enables a default button a consumer had already disabled. SheetFrame and AlertFrame report
  themselves to VoiceOver, and the alert's icon is excluded from the automation tree. Kit.Button's
  plain style no longer lightens the label weight, and the gallery's alert tiles show the real app
  icon.
- Kit.ToolbarRow sits on the titlebar's own material with no drawn rule at either end, reports
  itself to VoiceOver as an AXToolBar, and its items, overflow button and zoom stepper share one
  tab stop that arrow keys rove across, re-homing to the overflow button when the trailing group
  folds into it; the search field keeps its own stop for ⌘F. Item hover now fills over a brush
  transition and the icon-only label swap cross-fades instead of cutting, and the zoom stepper's
  Zoom Out, readout and Zoom In report as named children to VoiceOver.
- The titlebar's own material now covers the traffic-light strip instead of showing the plain
  window background there; Kit.ToolbarRow applies Kit.ToolbarItem to every group item itself, so a
  consumer can no longer forget it and end up with a zero-size button; New Graph, Add Item, Add
  Recipe, Align Selected, Export Image and Graph Summary read their full command name to VoiceOver
  instead of a truncated label, and the Save item's on state, every item's shortcut, and
  Return/Space activation all reach the accessibility tree. Pause All Calculations moves to ⌥⌘P so
  it no longer shares ⌘. with the toolbar's own cancel-in-flight shortcut. Find node now matches a
  node's drawn name (not only its key title), trims its query, orders results by position, anchors
  the current match the moment typing produces one, wraps ⌘G/Return and ⇧⌘G/⇧Return silently, pans
  the canvas into view without zooming (never inside an 80pt margin of the viewport edge, over a
  200ms ease, and as a single jump under Reduce Motion), and shows a "1 of 3"/"No matches" readout with
  VoiceOver announcements for the result set and each step. The field reports as "Find node" to
  VoiceOver, the run recomputes when nodes are added, deleted or renamed mid-find, and the count
  slot hides below a floor that tracks the display scale.
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
- The search field's loading spinner now reveals itself on the animation clock instead of a
  wall-clock timer, and toolbar item hover crossfades an overlay instead of animating the control's
  own background. The zoom stepper disposes its spinner fade subscription on detach and rebuilds its
  error state on reattach, and its solve-failed readout now reads "Solve failed" before the zoom
  percentage, matching the solving state's own wording. PalettePanel now paints its own background,
  and PaletteRow/RecentRow follow the control's own Background property instead of a hardcoded
  transparent fill. Several 150% gallery tiles now scope the tokens their hosted primitives read, not
  just the ones their own composite touches, so a search field or icon inside them scales along with
  everything else. A handful of literal radii and sizes in the composite templates now resolve real
  tokens, and the kit's dead Space6 and Thickness1/4/5/6 tokens are gone.
- Graph library: graphs now live in Foreman's own database and save themselves two seconds after the last change, and on close, opening another graph, quit and when the app is switched away from. Save records a version, versions are also recorded when a changed graph is opened and every 30 minutes of editing (thinned hourly for a day and daily for a month), and Revert To ▸ Browse Versions… restores any of them while keeping the current graph as a version. Rename… and Duplicate sit in the File menu, Open Recent lists the ten latest graphs with Clear Menu, and each graph keeps a 60 by 60 thumbnail. Show Toolbar, Show Welcome at launch and the Graph Summary window's size, position and segment now persist across launches.
- Welcome window: Window ▸ Welcome to Foreman (⇧⌘1, enabled with or without a graph) opens a window, resizable down to 660 by 420 and remembering its size for the session with the extra room going to the graph list, with New Graph and Open… on the left above a list of presets for new graphs and a Show when Foreman opens checkbox, and every graph in the library on the right, newest first, with a Filter field above eight graphs. Choosing another preset loads it in a strip under the list with a progress bar, a stage sentence and Cancel, New Graph, Open… and opening a row stay off until it is ready, and a failed load shows Couldn’t load with Try Again instead of an alert. Each row's menu reads Open, Duplicate, Rename…, Export as .fjson…, Choose Preset… when its preset is no longer installed, and Delete Graph… behind a destructive alert; opening a graph whose preset is missing offers Choose Preset…. Choose Preset… opens the graph with the preset you pick in one step and writes it to the row only once the graph has loaded, and a preset picked in the welcome window is the one New Graph uses in the main window. Closing the main window brings the welcome window back when Show when Foreman opens is on, and otherwise the app keeps running with no window, with New Graph, Open…, Open Recent and Welcome to Foreman in the application menu and a Dock click showing the welcome window, or a new graph when it is off; those application-menu items show only while no window is open, and the welcome window carries its own menu bar with File (New Graph, Open… and Open Recent) and Window (Welcome to Foreman). An unavailable row shows its name in tertiary italic, its thumbnail dimmed and the missing preset as a warning sentence in place of the usual details, and VoiceOver reads it as preset not installed. Kit.RecentRow now takes its context menu from the host instead of a fixed seven-item list, and Return or ⌘↓ on a row raises OpenRequested; preset names too long for the sidebar shorten in the middle; Foreman.Size.WelcomeSidebar and Foreman.Size.WelcomePresetRow join the scale tokens.

### Changed

- The solve error now says why the graph could not be solved: its fixed rates and limits cannot all be
  met at once, or nothing limits its rates. A graph that has never solved says rates will show once a
  solve works, "Show Node" reads "Show Nodes" for a group, and while calculations are paused the button
  reads "Resume Calculations" and resumes them.
- Graph Summary shows the building and power totals under the Buildings tables, writes zeros as "0" and
  rates with their unit such as "45/s", lists all-zero rows while every kind is shown, counts spoil and
  plant key nodes under Recipe nodes, and reads unlinked inputs, unlinked outputs and overproduction
  aloud in VoiceOver.
- Adding from the quick-add palette now closes it. Hold Option while clicking a result or pressing Return
  to place it and keep the palette open for adding several.
- Exported images are clean of selection and search marks: no selection border, find ring, focus ring,
  link highlight or waypoint handles appear in the file.
- VoiceOver reads each node's item tabs, with the item, whether it is an input or an output, its rate and
  whether it is short of supply or overproducing.
- Links that run the wrong way, or leave two flipped nodes on the same side, route around the nodes with
  a clear margin. Moving both ends of a link together carries its waypoints along. The paused capsule's
  padding and pause glyph match the design, the unavailable node text is slightly more opaque, and the
  tab outline is easier to see in dark mode.
- Tooltips, the recipe card, annotation selection handles and the off-screen arrows follow light and
  dark. The arrows now use the error and warning colours only.
- Saving a large graph no longer pauses editing: the thumbnail and the database write happen in the
  background.
- Solving no longer freezes the graph: the numbers are worked out in the background and applied when
  they are ready. Cmd-period stops a long solve and leaves the last rates in place.
- Launch now follows Show Welcome at Launch: with it on, only the welcome window opens and the preset
  loads in its own loading strip, with Try Again and the preset list as the answer to a failed load;
  with it off, the most recent graph reopens (or a new Untitled Graph opens on an empty library)
  behind the launch loading window. Dropping a `.fjson` file on the Dock icon opens it like File ▸ Open…,
  queued until the preset has loaded when it arrives during launch. Opening a graph from the library switches to its
  own preset without announcing the switch.
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
- Internal: the kit's temporary token-alias file is gone. No visible change; four unused forwarding
  keys were deleted, and the handful still read by legacy screens moved into the main token files
  with the same values.
- Internal: every window now registers its minimum-size scaling with the same appearance hub it
  already used for theme, instead of a separate tracking list; the main window gains one shared
  popover overlay layer for future popovers to anchor against. No visible change.
- Canvas colours now come from the app's theme tokens in both light and dark: item tab outlines
  use a new tab border colour that gets stronger with Increase Contrast, and overproduced and
  undersupplied node borders use the standard warning and error colours.
- Every node on the canvas is now one card: the theme's control background with a thin border,
  a coloured strip on its leading edge for its type, the node's icon and name, and a detail row.
  Recipes show the building, its count and a status row with a beacon badge, productivity pips
  and bonus productivity; suppliers, consumers and passthroughs read "Source", "Output" and
  "Passthrough", or a "Fixed 90/s" or "Limit 240/s" chip once their rate is set, and a recipe
  with a set building count shows "Fixed ×4". Item tabs are 22 pt tall with a 16 pt icon and a
  10 pt rate, and they grow to fit it. Every rate unit has its own suffix (/s, /min, /5 min,
  /10 min, /30 min, /h), and rates use one number format with k, M and G from 10 000 instead of
  scientific notation. Recipe, supplier, consumer and spoil footprints are unchanged. A passthrough
  shown as a card is now 144 wide; it was 72 in both modes and stays 72 while it draws as a bar.
  A card with a long chip grows to fit it. Width changes keep each node's centre, so saved graphs
  open where they were saved. A node placed from a link drag moves along the flow away from its
  source until it clears the source by 24 pt. Counts and rates cross-fade when they change, and
  Align Selected glides nodes into place; both happen instantly under Reduce Motion.
- Node states on the canvas now differ in shape as well as colour. A selected node gets a 2 pt
  orange border instead of the blue wash. An overproducing node gets a dashed warning border,
  with dashed outlines on its overproduced output tabs. An undersupplied node gets a double red
  border, and its short input tabs turn red. A node with an error gets a red border instead of the
  coral fill. Keyboard focus draws a ring 6 pt outside the node, and the placement flash sits
  between the two. Each node shows at most one badge inside its card: Unavailable, Error, Short,
  Warning or Over, in that order of priority. The Error and Warning badges keep the old error
  icon's click to fix and right-click menu. Turning off Flag supply problems hides the Short and
  Over badges but keeps the borders. A node whose recipe or item is disabled or unavailable draws
  at 60 % opacity with an Unavailable badge. A hovered item tab lightens, and during a link drag
  the tab the link would connect to is outlined in the accent colour. Borders run under the item
  tabs, and an unlinked tab keeps its normal outline. The canvas tooltip waits 800 ms before it
  appears. In icon view the selection and focus rings wrap the icon. A passthrough drawn as a bar
  switches back to the card whenever it would show a badge, and a badge takes clicks up to 2 pt
  beyond its edge. Spoil and plant counts go grey and italic when the graph fails to solve. The
  system accent colour and Increase Contrast now apply from the first window.
- Canvas text never draws below 10 pt at 100 % and multiplies with the interface scale. Zooming out
  hides a string once it would show smaller than 7 pt on screen: item tabs drop their rate and
  shrink to the icon, badges shrink to their glyph, and node names and counts go last. The simple
  view, used when zoomed far out or with over 200 nodes, now draws just the card, type strip, icon
  and border.
- Links can be selected and carry their own routing, Rounded or Orthogonal, with waypoints you add
  by double-clicking the link or from its context menu, drag onto the grid, and remove by
  double-click or Delete. New links take the default routing from Settings, and View > Link Routing
  now applies to the selected links. A selected link gets a casing, a hovered one a halo, a link
  short of supply turns red and dashed, and its hit band stays at least 12 pt wide whatever the
  stroke width. Saved files move to format version 9; older files open with the current default
  routing and no waypoints.
- The canvas ground and grid follow the design tokens. Grid lines become dots at the Minor and Major
  Gridlines units, one point across for minor and two for major, and they drop out once they would
  sit closer than 8 pt on screen; Show Gridlines off leaves the plain ground. The origin axes are
  one point wide, the Shift axis-lock guide is a dashed accent line, and the lasso is an accent
  outline over a soft accent fill. Pausing calculations draws a 2 pt warning frame and a
  "Calculations paused" capsule at the top of the canvas that resumes them when clicked.
- A graph the solver cannot solve keeps the rates and link throughputs from its last good solve,
  shown in secondary italic as out of date, instead of dropping to zero. Nodes that were never
  solved still read as not yet solved.
- Graph Summary is now a window of its own instead of a sheet over the graph. It opens beside the
  graph window, stays open while you edit, and a second Summary click brings it forward. Buildings,
  Items and fluids and Key nodes sit in one segmented row, with a Show menu in place of the old
  checkboxes. The rows follow every successful solve and keep their sort, filter, scroll position
  and selection; while the solver fails they stay as they were, in secondary italic. A row with an
  unlinked input is tinted red and one with an unlinked output or surplus orange, and
  double-clicking a row selects its nodes in the graph and pans to them. An empty graph, a filter
  that matches nothing and a Show menu with nothing ticked each say so and offer the way out.
  The window remembers its size, position and last segment until the app quits.

### Removed

- Factorio 1.x support: pre-2.0 save import (`*.json`) and the legacy `OldImport` save-file field.
- The Recipe/Flow segmented toggle in the inspector sidebar: it was display-only, since the segment
  is already chosen by the selected node's type. The identity header now sits at the top instead.
- The on-canvas item/recipe chooser panels, replaced by the Quick Add palette, the "Add to Graph"
  browser, and the radial menu. The Recipe Only filter setting no longer gates anything.
- The app's last legacy theme file and the one leftover alias token: every window now takes its
  styles and tokens from the component kit alone.
- File menu follows the autosaving document model: ⇧⌘S now duplicates the graph, Save As… is gone, Open… adds the file to the library (opening the same file twice opens the same graph), Export as .fjson… writes a file, and closing a graph, a window or the app never asks to save.

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
