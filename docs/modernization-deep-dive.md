# Phase 9 modernization deep-dive

Source of truth: `src/Foreman.Mac/**`, `src/Foreman.Core/**`, `tests/ForemanTest/**`, `docs/superpowers/specs/2026-09-01-foreman-mac-port-design.md` (Phase 9 section), `docs/perf-packaging-reference.md`. Every `file:line` is this repo unless prefixed `upstream/`. All counts and measurements below were run directly against the tree at `port/phase2`/`unspoiled/phase9` on 2026-09-03, not estimated.

This doc feeds the phase-9 plan. It does not cover UI visual redesign (parallel design round owns that) but does record the structural UI facts that redesign round needs.

## 1. Binding/XAML surface

### 1a. Inventory

16 `.axaml` files total, all under `src/Foreman.Mac/` (`App.axaml`, `MainWindow.axaml`, 14 in `Views/`). **Zero** use `x:DataType`. **Zero** use `{CompiledBinding}`. Every `{Binding ...}` in the tree is reflection-path today.

| File | Lines (axaml / code-behind) | `{Binding}` count | `ItemsSource` in XAML |
|---|---|---|---|
| App.axaml | 8 / 95 | 0 | 0 |
| MainWindow.axaml | 95 / 1010 | 0 | 0 |
| Views/GraphSummaryWindow.axaml | 211 / 814 | 42 | 0 |
| Views/PresetComparatorWindow.axaml | 111 / 491 | 7 | 0 |
| Views/SettingsWindow.axaml | 235 / 837 | 7 | 0 |
| Views/PresetSelectionWindow.axaml | 52 / 108 | 5 | 0 |
| Views/PresetImportWindow.axaml | 44 / 316 | 0 | 0 |
| Views/ImageExportWindow.axaml | 28 / 179 | 0 | 0 |
| Views/SaveFileLoadWindow.axaml | 13 / 193 | 0 | 0 |
| Views/ShapePropertiesWindow.axaml | 44 / 173 | 0 | 0 |
| Views/TextPropertiesWindow.axaml | 50 / 234 | 0 | 0 |
| Views/SciencePacksWindow.axaml | 15 / 159 | 0 | 0 |
| Views/DataLoadWindow.axaml | 14 / 94 | 0 | 0 |
| Views/ConfirmDialog.axaml, MessageDialog.axaml, YesNoCancelDialog.axaml | 16+13+17 / 18+22+19 | 0 | 0 |

61 `{Binding}` expressions total, concentrated in 4 files. The other 12 axaml files bind nothing — every control is populated imperatively from code-behind (the `ItemsSource=0` column confirms this too: not one axaml file sets `ItemsSource` in markup; `QualityPicker.cs:31`, `RecipeChooserPanel.cs:39`, `ItemChooserPanel.cs:33` set it from C#).

The **code-built UI** the mandate asks about is exactly one file: `src/Foreman.Mac/Views/GalleryWindow.cs`, a `Window` subclass with no `.axaml` partner. It is a dev-only diagnostic window ("Dev-only visual check window for every canvas floating panel, launched via `--gallery`", `GalleryWindow.cs:19`) — not reachable from normal app flow, so the redesign and MVVM passes can both defer or skip it without user-facing risk.

Separately, and this matters for where compiled bindings *don't* reach: `EditRecipePanel`, `IRChooserPanel` (+ `ItemChooserPanel`/`RecipeChooserPanel`), `ChooserIconGrid`, and the rest of `src/Foreman.Mac/Canvas/Panels/*.cs` are `Border`/`AvaloniaCanvas` subclasses built entirely in C# — no `.axaml` exists under `Canvas/Panels/` at all (`find src/Foreman.Mac/Canvas/Panels -name '*.axaml'` returns nothing). The compiled-bindings/MVVM binding-surface work is scoped to the 16 Window `.axaml` files; the floating in-canvas panels are a separate, non-XAML, owner-drawn layer untouched by either migration.

### 1b. The chicken-egg question, resolved

The mandate frames compiled bindings vs. MVVM as chicken-and-egg. It isn't, in this codebase, and the evidence is direct: none of the 4 bound windows sets `DataContext` to a ViewModel anywhere (`grep -rn DataContext src/Foreman.Mac/Views` returns one unrelated hit, a `FindAncestorOfType<ListBoxItem>` pattern match). Every existing `{Binding}` targets a small, already-well-formed row DTO used as a `DataTemplate` item, not a shared ViewModel:

| Window | Row types | Declared as |
|---|---|---|
| GraphSummaryWindow | `BuildingRow`, `BeaconRow`, `ItemRow`, `KeyNodeRow` (`GraphSummaryWindow.axaml.cs:28,42,54,79`) | `internal sealed class`, get-only ctor-initialized properties |
| PresetComparatorWindow | `ComparatorRow` (`PresetComparatorWindow.axaml.cs:36`) | `internal sealed class`, same shape |
| PresetSelectionWindow | `PresetRow` (`PresetSelectionWindow.axaml.cs:14`) | `internal sealed class`, same shape |
| SettingsWindow | `EnabledObjectsListItem` (`Views/EnabledObjectsListItem.cs:11`) | `public sealed class`, **already implements `INotifyPropertyChanged`** — it's one of only two `INotifyPropertyChanged` implementers in the whole port (the other is `Foreman.Core/Graph/IGraphContracts.cs`) |

Every one of these types is already at least `internal` (compiled bindings' `x:DataType` needs assembly-visible types — no accessibility bump required) and already immutable-or-notifying (no `INotifyPropertyChanged` retrofit required for one-way bindings; the one `Mode=TwoWay` binding, `SettingsWindow.axaml:22`'s `IsChecked`, already targets a type that raises `PropertyChanged` correctly, `EnabledObjectsListItem.cs:39`).

**Conclusion: compiled bindings can land first, as a small, mechanical, self-contained pass, with zero CommunityToolkit.Mvvm dependency.** Scope: add `x:DataType="vm:BuildingRow"` (etc.) to the 4 files' `DataTemplate` roots — 61 binding expressions across 4 files, no new types needed — then delete `AvaloniaUseCompiledBindingsByDefault=false` from `Foreman.Mac.csproj:16` (the sole site of that flag; `Foreman.Core.csproj` never sets it, it doesn't build XAML). The other 12 axaml files need nothing beyond the flag removal itself — they have no bindings to break.

The CommunityToolkit.Mvvm migration is the separate, larger effort: it targets the **event-wiring and code-behind state** layer, not the binding layer. Evidence of scale: 40 `+=` event-handler wire-up sites in `src/Foreman.Mac/**/*.cs` (Click/PointerPressed/PointerReleased/Loaded), concentrated in the largest code-behind files — `MainWindow.axaml.cs` (1010 lines, 9 event-wiring sites, 25 field/property declarations, zero XAML bindings backing any of it), `SettingsWindow.axaml.cs` (837 lines), `GraphSummaryWindow.axaml.cs` (814 lines). `ShellCommands.cs` is the concrete, named precedent for what CommunityToolkit.Mvvm replaces: a 55-line hand-rolled `ICommand` (`ShellCommands.cs:6-21`) with a permanently-empty `CanExecuteChanged` (`add { } remove { } `, line 16 — `IsImplemented` never changes after construction, so re-evaluation was never wired), backing 12 `ShellCommand` properties (`New`, `Load`, `Save`, ... `AlignSelection`, lines 25-37). This is exactly the shape `[RelayCommand]` source-generation replaces, and it's the one place in the port that already tried to build MVVM-command infrastructure by hand.

Landing compiled bindings before MVVM also avoids the two passes fighting over the same XAML files: MVVM-introduced ViewModels can declare `x:DataType` against the new types from day one instead of being written once, then re-touched for compiled bindings.

## 2. Legacy 1.x-removal surface

Total footprint is small and precisely bounded — no undiscovered sprawl.

**`OldImport` flag** — 6 touch points across 3 files, and never once branched on for behavior; it's a pure round-trip:
- `GraphSaveDocuments.cs:119` — `public bool OldImport { get; init; }` (record property)
- `GraphSaveWireModels.cs:43` — `public bool OldImport { get; set; }` (wire DTO twin)
- `GraphSaveWireMapper.cs:148-149` — `if (ui.OldImport) wire.OldImport = true;` (write side)
- `GraphSaveWireMapper.cs:164` — `OldImport = wire.OldImport` (read side)
- `GraphViewerSaveAssembler.cs:31` — `OldImport = false,` — the port's own save-writer **always** writes `false`; the only way this field is ever `true` at runtime is loading an `.fjson` that was saved `true` by a pre-2.0-era build. Nothing downstream reads the value to change parsing, UI, or solver behavior. Deleting the property from 3 types and the 4 pass-through lines is the entire task — no hidden call sites.

**"Old Foreman files (*.json)" load filter** — 2 call sites, both in `MainWindow.axaml.cs`: the "Open Graph" picker (`:304`) and the "Import Graph" picker (`:497`), both `FileTypeFilter = [new FilePickerFileType("Foreman files") { Patterns = ["*.fjson", "*.json"] }]`. The Save picker (`:560-561`) only ever offered `*.fjson` — there's no legacy save path to remove, only the legacy *open* pattern. This is orthogonal to `GraphSaveFormat.SaveFormatVersion`/`MinReadableSaveVersion` (currently 8/7, `GraphSaveFormat.cs:5,8`) — that's an active, in-window schema-migration mechanism for the current `.fjson` format and is explicitly **not** part of the 1.x removal; don't touch `GraphSaveMigration.cs`.

**Already excluded, confirmed, nothing to do:** `_1.0.0`-suffixed bundled-mod variants — zero matches anywhere in `src/`. This item in the spec is already resolved.

**Undocumented bonus find — a genuinely dead WinForms artifact:** `src/Foreman.Core/Settings.cs` (24 lines) is a `.NET Settings.settings`-designer-style stub — `internal sealed partial class Settings` in `namespace Foreman.Properties`, with a commented-out `SettingChanging`/`SettingsSaving` event-handler scaffold, verbatim WinForms auto-gen boilerplate. `grep -rn "Properties.Settings\|new Settings()" src` outside this file returns **zero live references** — every hit is a comment in `AppSettings.cs`/`MainWindow.axaml.cs`/canvas element files *describing* the historical mapping from upstream's `Properties.Settings.Default` to the port's actual JSON-backed `AppSettings`/`SettingsService` (§5). The class compiles, does nothing, and nothing calls it. Not named in the spec's list but meets the same bar ("pre-2.0 loader paths ... dead-code mass") — cheap addition to the same removal pass.

**Estimated total dead-code mass for this item:** ~30 lines net (6 OldImport touch points + 24-line Settings.cs stub) plus 2 one-token array-literal edits (dropping `"*.json"`). Small; do it first (§6).

## 3. Code-modernization inventory

### 3a. Baseline is already solid

`Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `EnableNETAnalyzers=true` are set identically across all 4 real `.csproj` files (`Foreman.Core`, `Foreman.Mac`, `ForemanTest`, `Foreman.Mac.UiTests`) — this was carried forward faithfully from `upstream/Foreman/Foreman.csproj:22-27`, not weakened. Only 6 files repo-wide use `#nullable disable` or a `!.` null-forgiving operator — a small, plausibly-justified set, not a systemic nullable-suppression problem. Collection expressions (`[.. x]`) are already used 94 times across `src/Foreman.Mac` + `src/Foreman.Core` — the codebase has already adopted this C#12+ idiom broadly; it is not a gap.

**Gap found:** `.editorconfig` exists only at `upstream/.editorconfig` — there is no repo-root or `src/`-scoped `.editorconfig` for the port at all. Every csproj opts into `AnalysisLevel=latest-recommended`, but without an `.editorconfig` there's no enforced style layer (using-directive placement, brace style, preferred collection/pattern-matching idioms) backing that — an ecosystem-default gap the analyzer settings alone don't close.

**Gap found:** no `Microsoft.Extensions.DependencyInjection` anywhere (`grep -rln "ServiceCollection\|IServiceProvider"` — zero hits). The spec says this "stays case-by-case if it earns its keep" — currently it hasn't been tried at all, so phase 9 is a first decision, not a continuation.

### 3b. Event wiring and code-behind state

40 `+=` event-handler sites in `src/Foreman.Mac`, concentrated in the three largest code-behind files (§1b). `ShellCommands.cs`'s hand-rolled `ICommand` is the clearest single artifact of "WinForms-idiom carried into Avalonia without the ecosystem's own answer" — `[RelayCommand]` generates the same shape with working `CanExecuteChanged` for free. `MainWindow.axaml.cs` at 1010 lines with 0 XAML bindings backing it is the single highest-value CommunityToolkit.Mvvm target: today the entire main window is state-and-event-driven code-behind, the exact pattern the migration is meant to replace.

### 3c. Sync-over-async — audited clean, with two real exceptions

`grep -rn '\.Result\b\|\.Wait();\|GetAwaiter().GetResult()' src` surfaces 9 lines; most are false-positive matches against a custom `Result` *type name* (`PresetImporter.Result`, `SaveFileReader.Result` — these are DTOs, not `Task<T>.Result`) or already-correct `await Task.Run(...)` sites (`SaveFileLoadWindow.axaml.cs:106`, `PresetImportWindow.axaml.cs:260`). Two are real, narrow, and arguably fine as-is:
- `GraphCanvasControl.cs:257` — `clipboard.TryGetTextAsync().GetAwaiter().GetResult()` — blocking clipboard read. Small, UI-thread-adjacent, likely intentional (clipboard paste is a synchronous user gesture in the existing code path); worth a look during the modernization pass but not a load-bearing freeze risk on the order of §4's solver finding.
- `MainWindow.axaml.cs:443`, `ShellBootstrapper.cs:99`, `PresetComparatorWindow.axaml.cs:228` — all `SomeWindow.Result` where `SomeWindow` is an Avalonia `Window` shown via `ShowDialog<T>()`; `.Result` here is the **already-awaited** dialog result field, not a blocking `Task.Result` — need per-site confirmation during implementation, but the grep pattern alone overstates the finding.

Net: sync-over-async is **not** a significant modernization item in this codebase, contrary to what the raw grep count might suggest. Worth one confirmation pass on the clipboard call, not a sweep.

### 3d. LINQ and string-formatting: hot-path audit

No LINQ or `string.Format` calls exist inside the actual per-frame `Draw`/`Paint` methods (`Elements/*.cs`) — the perf-packaging-reference.md's own hot-path audit (§1b there) already covers that surface and found the per-frame cost is `SKPaint` allocation churn, not LINQ. The LINQ hits found in `src/Foreman.Mac/Canvas` are all in **interaction-time** code: clipboard/paste assembly (`NodeClipboard.cs`), annotation selection/lasso (`GraphViewer.Annotations.cs`), and — the one worth flagging — the chooser panel's recipe filter, `RecipeChooserPanel.cs:81-159`, which runs a `.Where`/`.Any` chain per filter-string keystroke over the full recipe list (`RecipeMatchesKeyItem`'s `.Any(a => a.Fuels.Contains(...) && ...)` nested lambdas, `:116-117,151-159`). At preset scale (Space Age ships 1098 icon entries, so a comparable order of recipes/items) this runs on every keystroke while typing a filter, not once per frame — a real but low-severity optimization candidate (debounce or cache the filtered set), not a render-loop regression.

`string.Format(DisplayCulture.Format, ...)` is used 8+ times in `EditRecipePanel.cs` alone (`:706,795,862,950,959,960,985,991,994,1003`) for label text rebuilt on every property-panel refresh (fuel/module/beacon selection changes). This is the concrete "ecosystem-default gap beyond the named list": modern C#/.NET (10, which this project targets) prefers `string.Create(DisplayCulture.Format, $"...")` interpolated-string handlers over `string.Format` — same culture-safety, no format-string re-parsing per call, and the analyzer set already enabled (`AnalysisLevel=latest-recommended`) will flag `string.Format` calls with `IDE0071`/similar once someone looks. Not a perf hotspot (panel refresh, not per-frame) but a clean, mechanical, high-count (8+ sites in one file, likely more elsewhere) modernization item that fits squarely in "analyzer opportunities."

## 4. Solver evaluation groundwork

### 4a. Where GLOP-specific behavior leaks into expectations

`Google.OrTools` `9.15.6755` is referenced in exactly one production file pair: `GoogleSolver.cs` (constructs `Solver_t.CreateSolver("GLOP")`, `:19`) and `ProductionSolver.cs` (builds the LP model). Everything else in the solver stack (`GraphOptimisation.cs`, `ProductionGraph.cs`) is solver-agnostic LP-model construction — a real asset for a swap, the blast radius is genuinely two files.

**Headline finding: the production default overflows to `+Infinity`, and only GLOP's floating-point simplex is known to tolerate it.** `GraphOptimisation.cs:28` computes the low-priority-recipe objective multiplier as `Math.Pow(10, LowPriorityPower)`. `ProductionGraph.cs:127` sets the field's default: `LowPriorityPower = 1e5;` — i.e. `Math.Pow(10, 100000)`. Verified directly on this machine (.NET 10, `net10.0`, IEEE-754 double):

```
> Math.Pow(10, 1e5)
∞
> double.IsPositiveInfinity(Math.Pow(10, 1e5))
True
```

This is **verbatim upstream behavior** (`upstream/Foreman/Models/ProductionGraph.cs:127`, `upstream/Foreman/Models/Solver/GraphOptimisation.cs:28` — identical), not a porting defect, and the field's own doc comment (`ProductionGraph.cs:52`, "10,000 is a nice value here") describes the *multiplier*, not the exponent that actually gets fed to `Math.Pow` — the comment and the code disagree about what "a nice value" means, which is presumably why the shipped default (`1e5` as an exponent) is three orders of magnitude past where the comment thinks it lands. GLOP evidently handles an `Infinity` objective coefficient gracefully (an LP minimizer with an unboundedly-costly term will drive that variable to zero whenever any finite-cost alternative exists, which is the intended "never pick a low-priority recipe unless forced" behavior) — but **this exact case is untested**. `tests/ForemanTest/GraphOptionsSolverFieldsTest.cs`'s two `LowPriorityPower_*` tests (`:23,41`) deliberately use small exponents (`1` and `2`) to exercise the crossover math by hand (`0.01*1*10^1` vs `0.01*15*1`, per the tests' own inline derivation comments) — neither test, nor any other in the 22-case `SolverTest.cs` + 7-case `GraphOptionsSolverFieldsTest.cs` suite, exercises the actual shipped default. **A HiGHS or managed-simplex swap must be independently verified against an `Infinity` (or near-`Infinity`) objective coefficient before it can be trusted at default settings** — a different solver could reject it, propagate `NaN`, or silently produce a different optimum; this is exactly the kind of GLOP-specific tolerance the decision gate needs to know about, and today's test corpus doesn't cover it.

Related coefficient constants worth carrying into the eval, all upstream-verbatim and all in `ProductionSolver.cs`/`ProductionGraph.cs`: the io-ratio construction (`ProductionSolver.cs:62`) `1e-2, 1e-1 / Math.Min(1e-3, minRecipeOutRate), 1e2 / Math.Min(1e-3, minRecipeOutRate)` — a fixed `1e-2` factory-cost coefficient and two rate-dependent bound terms; `ProductionGraph.cs:42-45`'s `MaxSetFlow = 1e7`, `MaxFactories = 1e6`, `MaxTiles = 1e7`, `MaxInventorySlots = 1e6` — all round-number LP variable bounds, unlikely to be solver-sensitive on their own but part of the same "GLOP has quietly tolerated whatever we feed it" pattern worth a deliberate look.

### 4b. Test-suite compatibility surface

Every solver assertion in the port goes through one of two private `AssertFloatsAreEqual` helpers (`SolverTest.cs:504-506`, `GraphOptionsSolverFieldsTest.cs:172-174`), both `Assert.AreEqual(expected, actual, 0.0001)` — a fixed **1e-4 absolute tolerance**, applied uniformly across 22 `TestMethod`s in `SolverTest.cs` and 7 in `GraphOptionsSolverFieldsTest.cs` (29 solve-and-assert cases total, plus 3 warmup-only cases in `SolverWarmupTests.cs` that don't assert numeric output). This tolerance, not GLOP's own internal precision, is the actual bar an alternative solver must clear: "identical solve results across the test-suite graph corpus" (the spec's decision gate) concretely means every one of these 29 cases reproducing its expected value within 1e-4. `SolverWarmupTests.cs` (43 lines, phase-7 infrastructure — see perf-packaging-reference.md §1c) also needs a decision either way: it warms a `GoogleSolver` instance specifically; a solver swap either repoints this warmup at the new solver's equivalent cold-load cost, or — if the replacement has no comparable native-load cost (a pure-managed simplex has none) — deletes the warmup machinery entirely as no-longer-needed.

### 4c. Sizing HiGHS vs. a managed simplex, honestly

Foreman's LPs are, per the spec, hundreds of variables — one `ProductionSolver` instance per connected node-group (`GraphOptimisation.cs:17-18` solves each connected component separately, not one monolithic LP for a 1000-node graph), so the actual per-solve problem size is smaller still. At that scale:

- **A pure-managed simplex** (dense or revised simplex, no native dependency) is comfortably tractable — a few hundred variables and constraints solves in low single-digit milliseconds even with a naive dense-tableau implementation; this is well inside "fast enough that the first-solve freeze problem disappears by construction" (no native dylib to cold-load, no `SolverWarmup` needed at all). The tradeoff is engineering cost: GLOP is a mature, heavily-tested simplex with documented numerical robustness (degenerate/cyclic problems, the kind Foreman's cyclic-recipe test cases exercise — `SolverTest.cs:239-254`'s `TestCycle`/`TestRecipeLinkedToSelf`); a hand-rolled managed simplex needs its own robustness validation against exactly those edge cases, not just the happy-path numeric ones. This is real work, not a drop-in.
- **HiGHS** is a genuinely lighter native dependency than the OrTools tree (perf-packaging-reference.md §1c: ~55 MB, 100 dylibs, including a bundled Coin-OR MIP/LP family Foreman never uses since it only ever asks for `"GLOP"`) — HiGHS ships as a single, much smaller native library and is a first-class, actively-maintained LP/MIP solver with a stable C API. What this deep-dive can't verify without a build attempt: which .NET binding package (if any) ships pre-built `osx-arm64` (and `linux-x64`, since phase 8 makes Linux a supported target too) natives with acceptable licensing, matching the same verification phase 1 already did for OrTools' own `osx-arm64` natives. That's a concrete, bounded first task for whoever picks up the solver eval — same shape as the phase-1 OrTools verification, not a new kind of risk.

Recommendation for the eval order: verify the `Infinity`-coefficient behavior (§4a) and extend the test corpus to cover it *before* attempting either replacement — otherwise the decision gate ("identical solves across the corpus") is silently blind to the one input value real graphs actually use.

## 5. Storage groundwork

### 5a. Current inventory

**`settings.json`** — `SettingsService.cs:22-38` writes one flat JSON object (`System.Text.Json`, `WriteIndented=true`, whole-file rewrite on every `Save()`, no partial updates) at `~/Library/Application Support/Foreman/settings.json` (macOS) / `$XDG_DATA_HOME/Foreman/settings.json` (Linux). The shape is `AppSettings.cs` (63 lines) — 46 flat properties (bools, ints, a few enums, 2 `decimal`s for the same `LowPriorityPower`/`PullConsumerNodesPower` solver fields §4 covers, string font/color fields for annotation defaults). One custom `JsonConverter<ThemeMode>` (`SettingsService.cs:8-19`) backward-compat-reads upstream's old `bool FlagDarkMode` as the port's new tri-state `ThemeMode` enum — this converter is a real migration-compatibility precedent for whatever the SQLite settings table's own back-compat read needs to do.

**Preset pipeline** — 2 bundled presets, 4 files, 40 MB total in `src/Foreman.Mac/Presets/`:

| Preset | `.pjson` (data) | `.dat` (icons, FOIC format) |
|---|---|---|
| Factorio 2.0 Space Age | 1,174,101 B | 25,726,184 B |
| Factorio 2.0 Vanilla | 523,872 B | 14,367,001 B |

`.pjson` is read whole (`PresetProcessor.cs:34,54` — `Utf8File.ReadAllText` → `PresetJson.ParseObject`), optionally overlaid with a `presetCustom.json` merge (`PresetProcessor.cs:55-56`, `PresetJson.MergePresetOverlay`) — the same overlay mechanism `PresetImporter` uses for `baseCustom.json` on import (one-time-import surface: `PresetImporter.cs:200` copies `baseCustom.json` from the bundle into the user's writable preset directory during import).

**Icon cache (`.dat`)** — a from-scratch, already well-engineered binary format, not a naive blob dump: `ForemanIconCacheFile.cs`, "FOIC v1" (`:14-16`) — 20-byte header (4-byte magic, u16 version, u16 reserved, u32 entry count, u32 uncompressed payload size, u32 zstd-compressed size), then one zstd-compressed (`ZstdSharp`, level 19, `:24`) stream containing a flat sequence of `(u16 name-length, UTF-8 name, i32 ARGB tint, i32 PNG-length, PNG bytes)` entries. Consumption is eager and whole-atlas: `ReadAsync` (`:69-87`) decodes **every** entry's PNG to an `SKBitmap` during the read loop (`:141-154`), reporting progress but never deferring — this matches the design doc's own framing ("whole-atlas decode at boot") and confirms the "lazy per-icon loads" candidate is a real, currently-absent capability, not already-partially-done.

### 5b. Measured icon duplication

The spec proposes "deduplicated icon blobs keyed by content hash" as a candidate without a number attached. Measured directly by parsing both bundled `.dat` files (decompress via the same zstd algorithm `ForemanIconCacheFile` writes, walk the FOIC entry format above, SHA-256 each PNG blob):

| Preset | Entries | Distinct PNG content (by hash) | Total PNG bytes | Unique-content bytes | Savings from content-hash dedup |
|---|---|---|---|---|---|
| Space Age | 1098 | 958 | 33,321,713 | 26,135,574 | **7,186,139 B (21.6%)** |
| Vanilla | 524 | 413 | 20,923,999 | 14,408,493 | **6,515,506 B (31.1%)** |

The duplication is concentrated exactly where you'd expect: technology-level icon variants share pixel-identical PNGs across their tiers — e.g. `icon.t.braking-force-1` through `-6` (Space Age) are 7 entries, one distinct 77,581-byte PNG; same pattern for `icon.t.inserter-capacity-bonus-*`, `icon.t.laser-shooting-speed-*`, `icon.t.laser-weapons-damage-*`, `icon.t.refined-flammables-*` (each a 7-member group, one shared blob). This is a real, sizeable, cheaply-realized win — a content-hash-keyed blob table with entries pointing at shared rows would cut the raw icon payload by roughly a fifth to a third *before* compression is even applied, on top of whatever SQLite's own storage does. (Numbers are pre-compression PNG-byte savings; post-zstd the percentage will differ since duplicate PNGs already compress well against each other as a compression-stream artifact — the real-file win is in decode time and row count, not primarily on-disk bytes, since zstd already exploits some of this redundancy today.)

### 5c. What EF Core/SQLite replaces, and the import surface

Three independent stores collapse into candidate SQLite tables: `settings.json` (a `Settings` table, one row, or a wide key-value table given the flat-POCO shape), the `.pjson` preset data (structured tables for items/recipes/assemblers/etc. — the biggest schema-design lift, since `.pjson` is currently one big untyped `JsonObject` parsed on demand, not a strongly-typed model), and the `.dat` icon cache (a content-hash-keyed blob table per §5b, replacing the whole-atlas FOIC file). The one-time-import path is the same shape `PresetImporter` already implements for `baseCustom.json` today (§5a) — copy-and-merge from the bundle into a user-writable location — so the migration path (bundle ships `.pjson`/`.dat`, first launch imports into SQLite) has a working precedent to model, not a green-field design.

## 6. Ordering recommendation

Dependency-ordered, with the reasoning for each placement:

1. **1.x-legacy removal** (§2). No dependencies on anything else in phase 9; touches `GraphSaveWireMapper.cs`/`GraphSaveDocuments.cs`/`GraphSaveWireModels.cs`/`GraphViewerSaveAssembler.cs` (serialization) plus 2 lines in `MainWindow.axaml.cs` and the dead `Settings.cs` stub. Doing this first shrinks the surface every later pass has to read past — small, ~30 net lines, low risk, no reason to sequence it anywhere but first.
2. **Analyzer/style baseline** — add a repo-scoped `.editorconfig` (§3a), sweep the `string.Format` → interpolated-string sites (§3d). Mechanical, sets the style contract before the bigger structural passes (§3, §4) touch the same files repeatedly.
3. **Compiled bindings** (§1b). Proven independent of MVVM above — do it before CommunityToolkit.Mvvm so the 4 already-bound windows don't get touched twice, and so `AvaloniaUseCompiledBindingsByDefault=false` comes off the solution (`Foreman.Mac.csproj:16`) early rather than lingering through a longer MVVM effort.
4. **CommunityToolkit.Mvvm migration** (§3b). The largest single item — 40 event-wiring sites, `MainWindow.axaml.cs`'s 1010-line code-behind, `ShellCommands.cs`'s hand-rolled `ICommand`. Sequenced after compiled bindings so new ViewModels this pass introduces adopt `x:DataType` from day one instead of being written once and revisited.
5. **Solver alternatives evaluation** (§4) — independent subsystem (`Foreman.Core/Models/Solver`, 2 files touched), no UI dependency, can run in parallel with 3-4 if resourced separately. Gate its own decision on first fixing the `Infinity`-coefficient test gap (§4a) — don't let the decision gate pass on a corpus that's blind to the default the app actually ships.
6. **SQLite/EF Core storage** (§5) — independent subsystem (`Foreman.Core/DataCaching`, `Foreman.Mac/Services/SettingsService.cs`), no UI-layer dependency either, can also run in parallel with 3-4/5. The one soft dependency: schema design benefits from the modernization pass's analyzer/style baseline (step 2) being in place first, since this is new code that should launch clean rather than launch-then-retrofit.
7. **Display scaling** (spec, not separately sectioned above since it's UI-visual, but structurally: touches every window and every Canvas panel uniformly). Sequence after steps 3-4 settle — a uniform scale factor is far easier to thread through CommunityToolkit.Mvvm-backed, compiled-binding XAML than through the current per-control code-behind sizing, so doing it after avoids threading the same logic through both the old and new patterns.
8. **Dark-mode extension** (themed node/link/annotation palettes) — pairs naturally with step 7 (both touch rendering broadly), and benefits from step 6 (storage) existing for persisting the manual override setting, though it doesn't strictly block on it.
9. **Undo/redo** — last, per the spec's own reasoning ("lands after the modernization pass has settled the code it instruments") — every node-graph mutation path is a potential command-boundary, and CommunityToolkit.Mvvm's `RelayCommand`/observable-property patterns (step 4) give the undo/redo command layer a natural home to compose into, rather than wrapping the pre-migration event-handler soup.

Steps 5 and 6 are the two genuine parallelization opportunities — both are self-contained subsystems with no XAML surface, so they don't contend with the UI-layer sequence (1→2→3→4→7→8→9) for the same files.
