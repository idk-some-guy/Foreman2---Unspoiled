# Changelog

All notable changes to Foreman2 - Unspoiled are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Low-priority recipes now work: the default priority weight overflowed the solver and failed any
  graph using one. Existing settings carrying the broken value are healed automatically.

### Changed

- Switched the production solver from Google OR-Tools to HiGHS: a dramatically smaller download
  (the bundled solver drops from roughly 55 MB across 100 files to 4.4 MB in one file) and a much
  faster first solve after launch, with identical solve results.
- Internal modernization of the settings, factory-summary, and preset-comparison windows onto
  CommunityToolkit.Mvvm with compiled bindings; no user-visible behavior change.
- Settings now live in a local SQLite database instead of settings.json; an existing settings.json
  is imported into the database automatically on first launch and is not written to again.
- Preset data and icons now cached in the local database; first load of each preset migrates
  automatically.

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
