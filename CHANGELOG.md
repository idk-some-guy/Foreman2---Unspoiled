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
  popover controls. Undo/Redo and Show/Pin Inspector appear as disabled placeholders for work not
  yet built.
- A zoom stepper on the toolbar, showing the canvas's current scale with quick in/out buttons next
  to the menu bar's own zoom commands.
- The Save button tints when the graph has unsaved changes, and Align Selected disables itself when
  nothing is selected.

### Fixed

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

### Changed

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
