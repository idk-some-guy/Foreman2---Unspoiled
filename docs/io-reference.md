# I/O reference — file/preset I/O (phase 6 prep)

Source of truth: `upstream/Foreman/Forms/{MainForm,ImageExportForm,PresetImportForm,SavefileLoadForm,SciencePacksLoadForm,PresetSelectionForm}.cs(+.Designer.cs)` (12 files, ~3096 LOC), `upstream/Foreman/{FactorioBenchmarkRunner,FactorioInstallValidator,FactorioModListHelper,FactorioBundledModHelper}.cs` (4 files, ~141 LOC), `upstream/Foreman/DataCaching/{FactorioPathsProcessor,PresetProcessor,InfoPackageClasses}.cs` (3 files, ~489 LOC), `upstream/Foreman/Serialization/GraphSaveCodec.cs` (75 LOC), and the call sites in `ProductionGraphView/ProductionGraphViewer.cs:1290-1557`. Every file was read in full. Every `file:line` below is an `upstream/Foreman/` path.

**Headline finding: the data layer for this phase is almost entirely already ported.** `src/Foreman.Core/Serialization/` carries all 13 upstream serialization files at near-identical LOC counts (`GraphSaveDocuments.cs` 152/152, `GraphIncludedSetCollector.cs` 91/91, `PresetProcessor.cs` 288/289 including `TestPreset` and `PresetErrorPackage` verbatim). What phase 6 mostly adds is **UI orchestration and a handful of small glue methods** the WinForms-only classes (`ProductionGraphViewer`, `Application.StartupPath`) never let port straight across. See §9 for the precise gap list.

## 1. Class/file inventory

| Class/file | LOC | Responsibility | Port status |
|---|---|---|---|
| `MainForm.cs` Save/Load/Import/New region | ~200 of 662 (`:137-353`) | Save/SaveAs/Load/Import/New handlers, dirty-tracking, title-bar text, `SettingsButton_Click`'s preset-reload cascade | `Save`/`SaveAs`/`Import` are disabled `ShellCommand` stubs (`src/Foreman.Mac/ShellCommands.cs:28-30`); `Load`/`New` are wired but skip dirty-check/title-bar bookkeeping (`src/Foreman.Mac/MainWindow.axaml.cs:253-279`) |
| `ImageExportForm.cs`+`.Designer.cs` | 124+156=280 | PNG export dialog: scale selector, transparency/view-limit toggles, file picker, bitmap render | `ExportImage` is a disabled stub (`ShellCommands.cs:31`); the render math it calls (`GraphExportBounds`, `Viewer.Paint(canvas, fullGraph:)`) is already ported — see §3 |
| `PresetImportForm.cs`+`.designer.cs` | 445+442=887 | Import-preset-from-Factorio dialog + the whole export pipeline orchestration | Not started — `SettingsWindow`'s `ImportPresetButton` shows a placeholder (`src/Foreman.Mac/Views/SettingsWindow.axaml.cs:624-628`) |
| `SavefileLoadForm.cs`+`.Designer.cs` | 280+111=391 | Load-from-Factorio-save dialog: runs Factorio with a reader mod, parses enabled recipes/mods | Not started — `SettingsWindow`'s `LoadEnabledFromSaveButton` shows a placeholder (`:526-530`) |
| `SciencePacksLoadForm.cs`+`.Designer.cs` | 163+137=300 | Science-pack picker grid → enabled-object derivation via tech unlocks | Not started — `SetEnabledFromSciencePacksButton` shows a placeholder (`:532-536`) |
| `PresetSelectionForm.cs`+`.Designer.cs` | 80+214=294 | "No exact preset match" picker, shown when a loaded save's preset can't be resolved | Not started — no equivalent dialog exists in the port at all |
| `FactorioBenchmarkRunner.cs` | 56 | Launches `factorio.exe` as a hidden headless process, captures stdout, detects crash/another-instance | Not started (Windows `Process`/`ProcessWindowStyle.Hidden`, portable to mac's `Process` API but needs re-verification) |
| `FactorioInstallValidator.cs` | 37 | Validates a Factorio executable's version via `FileVersionInfo` (must be 2.0.7 – 2.x) | Not started — `FileVersionInfo.GetVersionInfo` is Windows-PE-resource-only, no mac equivalent |
| `FactorioModListHelper.cs` | 36 | Enable/disable/remove one mod entry in `mod-list.json` | Not started, but trivially portable (pure JSON edit, no OS dependency) |
| `FactorioBundledModHelper.cs` | 12 | Copies a bundled Lua mod folder (from the app's own `Mods/` dir) into the Factorio mods folder | Not started; the bundled Lua mod sources (`foremanexport_2.0.0/`, `foremansavereader_2.0.0/`) exist under `upstream/Foreman/Mods/` and need to ship as Content in `Foreman.Mac`/`Foreman.Core` the same way `Graphics/*.png` already does (`docs/upstream-divergences.md:28`) |
| `DataCaching/FactorioPathsProcessor.cs` | 134 | Auto-detects Factorio install/user-data paths (Windows registry Steam lookup, `config-path.cfg` parsing) | Not started; port already has a macOS-native equivalent per the task brief (executable-based detection, `~/Library/Application Support/factorio`, Steam `.app` path) — this file's *logic shape* (parse `config-path.cfg`'s `write-data=` line, resolve `.factorio`/`__PATH__executable__`/`__PATH__system-write-data__` tokens) still needs porting for the save-reader/import flows even though the *install-location* half is superseded |
| `DataCaching/PresetProcessor.cs` `TestPreset`+friends | ~205 of 289 (`:50-263`) | Loads a "light" preset (items/recipes/mods/qualities as strings only) and diffs it against a saved graph's included-set, producing a `PresetErrorPackage` | **Already ported verbatim** — `src/Foreman.Core/DataCaching/PresetProcessor.cs:49-262` |
| `DataCaching/InfoPackageClasses.cs` `PresetErrorPackage` | 48 of 66 (`:18-65`) | Error/severity counters + `IComparable` sort for ranking candidate presets | **Already ported verbatim** — `src/Foreman.Core/DataCaching/InfoPackageClasses.cs` |
| `Serialization/GraphSaveCodec.cs` | 75 | Domain↔JSON pipeline facade | Ported minus two overloads that took the WinForms `ProductionGraphViewer` type directly (`BuildViewer`, `WriteViewerToString(viewer, ...)`) — see §2 |
| `ProductionGraphView/Annotations/GraphExportBounds.cs` | 69 | Export-rectangle math: `IsExportable`, `ScaledWidth`/`ScaledHeight`, view-limit clamping | **Already ported verbatim** — `src/Foreman.Mac/Canvas/GraphExportBounds.cs` |

## 2. Graph save/save-as/load/import (MainForm.cs:137-353, 509-515, 533-537)

### Save / SaveAs

- `SaveButton_Click` (`:140-143`): `if (savefilePath == null || !SaveGraph(savefilePath)) SaveGraphAs();` — Save silently falls back to Save-As when there's no path yet or the write failed.
- `SaveAsGraphButton_Click` (`:145-147`) → `SaveGraphAs()` (`:165-179`): `SaveFileDialog`, `DefaultExt=".fjson"`, `Filter="Foreman files (*.fjson)|*.fjson|All files|*.*"`, `InitialDirectory=Application.StartupPath/"Saved Graphs"` (created if missing), `AddExtension=true`, `OverwritePrompt=true`, default `FileName="Flowchart.fjson"`.
- `SaveGraph(path)` (`:181-196`): `Graph.SerializeNodeIdSet = null` (save everything, not a selection subset), `GraphSaveCodec.WriteViewerToString(GraphViewer, writeIndented: true)`, `Utf8File.WriteAllText(path, json)`, then sets `savefilePath = path` and **`savefileBaselineJson = json`** (the dirty-check baseline — a captured string, not a re-read from disk), and updates the title bar. Catches all exceptions → `UserMessages.Show("Could not save this file. See log for more details")` + `ErrorLogging.LogException`, returns `false`.
- Title-bar format, used identically after Save/Load/New/preset-reload (`:188,240,263,444`): `string.Format(DisplayCulture.Format, DefaultAppName + " ({0}) - {1}", Properties.Settings.Default.CurrentPresetName, savefilePath ?? "Untitled")` — `DefaultAppName` is captured once at form construction from the Designer's initial `Text`. No literal "*" dirty-asterisk anywhere; the title only ever shows preset name + path, dirtiness is surfaced solely through the exit-time/load-time prompt below.

### Dirty tracking — `TestGraphSavedStatus` (`:298-321`)

Called from `FormClosing`, `NewGraph`, and `LoadGraph()` (the no-path overload) — **not** called by Save/SaveAs/Import themselves.

```
if (savefilePath == null)
    return !Graph.Nodes.Any() || Show(exitMsg, exitTitle, OKCancel) == OK;   // empty graph never prompts
if (!File.Exists(savefilePath))
    return Show(exitMsg, exitTitle, OKCancel) == OK;                          // file vanished since save

Graph.SerializeNodeIdSet = null;
string currentSaveJson = GraphSaveCodec.WriteViewerToString(GraphViewer, writeIndented: true);
string? savedJson = savefileBaselineJson ?? Utf8File.ReadAllText(savefilePath);
if (savedJson != currentSaveJson) {
    result = Show("The current graph has been modified!\nDo you wish to save before continuing?", exitTitle, YesNoCancel);
    if (result == Cancel) return false;
    if (result == OK) SaveGraph(savefilePath);   // dead code: a YesNoCancel box never returns OK, so
                                                  // "Yes" silently falls through to `return true` below
                                                  // without saving - an upstream defect, not a real branch
}
return true;
```
`exitMsg` = `"The current graph hasn't been saved!\nIf you continue, you will lose it forever!"`, `exitTitle` = `"Are you sure?"` (both `const string`, `:299-300`). The comparison is a **full round-trip re-serialize**, not a boolean flag — any node/annotation/UI-state mutation that changes the JSON output trips it, and `savefileBaselineJson` is refreshed by `SaveGraph`, `CaptureSaveBaseline` (`:266-269`, called right after a successful load), and cleared to `null` on `NewGraph` (`:256-257`). **Port has none of this**: no `savefilePath`/`savefileBaselineJson` fields on `MainWindow`, no `TestGraphSavedStatus` equivalent — `New`/`Load` currently discard the in-memory graph unconditionally.

### Load

- `LoadGraphButton_Click` → `LoadGraph()` (`:198-212`): calls `TestGraphSavedStatus()` first (returns early if the user cancels), then `OpenFileDialog` (`Filter="Foreman files (*.fjson)|*.fjson|Old Foreman files (*.json)|*.json"`, same `Saved Graphs` `InitialDirectory`, `CheckFileExists=true`) → `LoadGraph(path)`.
- `LoadGraph(path)` (`:214-225`, async): `await GraphViewer.LoadFromJson(text, useFirstPreset: false, setEnablesFromJson: true)`, then on success `ApplyLoadedGraphUiState(path)` (`:227-241`) sets `savefilePath`, calls `CaptureSaveBaseline()`, re-syncs `RateOptionsDropDown` + 5 `Properties.Settings.Default.Default*` fields from the now-loaded graph, and rewrites the title bar. On any exception: `"This save file is too old or corrupt. Try opening it in the previous Foreman release and saving it again, then open the new file here."`
- **Port's `LoadGraphAsync`** (`src/Foreman.Mac/MainWindow.axaml.cs:256-279`) already reproduces the file-picker + `GraphViewer.LoadDocument` + same error message, but its own doc comment (`:253-255`) says it deliberately skips the dirty-check prompt and the title-bar/`savefilePath` bookkeeping "since Save/SaveAs stay stubbed disabled this phase." It also always calls `LoadDocument` against the **currently-booted** `DataCache` — no preset-mismatch detection (§8) and no `setEnablesFromJson` handling (the loaded save's `Ui.EnabledRecipes`/etc. lists are read into the document model but never applied to the live `DCache`'s `.Enabled` flags — compare upstream `ApplySaveUi`, not present anywhere in `GraphViewer.LoadDocument`).

### Import Graph — merge into current graph (MainForm.cs:271-296, ProductionGraphViewer.cs:1311-1363)

- `ImportGraphButton_Click` → `ImportGraph()` (`:271-282`): **no dirty-check** (unlike Load) — same `OpenFileDialog` shape as Load, filter, `Saved Graphs` initial dir.
- `ImportGraph(path)` (`:284-296`): `GraphSaveCodec.ReadGraphPayload(text)` — this accepts **either** a bare `ProductionGraphSaveDocument` fragment **or** a full `GraphViewerSaveDocument` and unwraps its `.ProductionGraph` (`GraphSaveCodec.cs:38-43`), so Import can consume either a `.fjson` viewer save or a raw graph-fragment `.json`. On success: `GraphViewer.ImportNodesFromDocument(document, ScreenToGraph(viewer-center), applySolverSettings: true)`. On failure: `"Could not import this file. See log for more details."`
- `ImportNodesFromDocument` (`ProductionGraphViewer.cs:1325-1363`) — the actual merge/offset/selection algorithm, verbatim:
  1. `Graph.InsertNodesFromDocument(cache, document, applySolverSettings)` → a `NewNodeBatch`; **missing items/recipes referenced by the import get added to the live `DataCache` as a side effect** (comment at `:1329`). Returns early (no-op) if the batch is empty.
  2. Compute the **centroid** of all newly-inserted nodes' upstream-saved `Location`s (`xAve`/`yAve`, integer-truncated average).
  3. `offset = Grid.AlignToGrid(origin - centroid)` where `origin` is the screen-center-to-graph point the caller passed (`ScreenToGraph(Width/2, Height/2)`) — so the whole imported cluster is translated as one rigid block, snapped to the grid, centered on the current viewport, **not** dropped at each node's original saved coordinates.
  4. Every new node's controller gets `SetLocation(savedLocation + offset)`.
  5. `ClearSelection()`, then every newly-imported node becomes selected + `Highlighted = true` (visual feedback for "here's what just landed").
  6. `UpdateGraphBounds()` + `Graph.UpdateNodeValues()`.
- This is the exact same code path `ImportNodesFromFragment` (clipboard paste, `:1311-1323`) uses after `ReadGraphPayload` — Import Graph is architecturally "paste from a file" rather than a separate merge algorithm, which matters for the port since `src/Foreman.Mac/Canvas/NodeClipboard.cs` already implements the paste half (`ToSaveDocument`/clipboard round-trip) and `ImportNodesFromDocument`'s centroid-offset-selection logic likely already exists there in some form worth reusing rather than re-deriving.
- **Port status**: `ShellCommands.Import` is a disabled stub (`ShellCommands.cs:30`); `GraphViewer` has no `ImportNodesFromDocument`/`InsertNodesFromDocument` equivalent yet (only full-graph `LoadDocument`, which clears the existing graph rather than merging into it).

### Cmd+S / key path (already logged in `docs/upstream-divergences.md:141-142`)

`MainForm_KeyDown` (`:533-537`): `if (Keys.S && Control held) { if (savefilePath == null || !SaveGraph(savefilePath)) SaveGraphAs(); }` — identical fallback logic to the button handler, just Ctrl+S-triggered directly (no `ICommand` indirection upstream). The port's Cmd+S is currently captured by `GraphCanvasControl.HandleMovementKey`'s WASD pan branch instead (unconditional `Key.S` read, no `KeyModifiers.Meta` gate) — wiring real Save requires gating that pan branch on `!modifiers.HasFlag(KeyModifiers.Meta)` per the existing divergence note, not just binding the shortcut.

### What's missing to build a `GraphViewerSaveDocument` from the live Mac viewer

Upstream's `GraphSaveCodec.WriteViewerToString`/`BuildViewer` (`GraphSaveCodec.cs:18-22,66-67`) delegate to `GraphSaveWriter.WriteViewer(ProductionGraphViewer, DataCache)` (upstream `GraphSaveWriter.cs:33-58`), which the port's `Foreman.Core.Serialization.GraphSaveWriter` does **not** carry — it can't, since `ProductionGraphViewer` is a WinForms UI type with no Core-layer equivalent. The port needs its own small assembly function in `Foreman.Mac` (not Core), built entirely from pieces that already exist:
```
GraphViewerSaveDocument {
    Version = GraphSaveFormat.SaveFormatVersion,
    SavedPresetName = cache.PresetName,                          // DataCache.PresetName — exists
    IncludedMods = new(cache.IncludedMods),                       // DataCache.IncludedMods — exists
    ProductionGraph = GraphSaveWriter.WriteProductionGraph(viewer.Graph),  // ported, reuse directly
    Ui = <new GraphViewerUiSaveData> {
        Unit = viewer.Graph.SelectedRateUnit, ViewOffset = viewer.Viewport.ViewOffset, ViewScale = viewer.Viewport.ViewScale,
        ExtraProdForNonMiners = ..., AssemblerSelectorStyle = viewer.Graph.AssemblerSelector.DefaultSelectionStyle,       // ProductionGraph already carries AssemblerSelector/ModuleSelector/FuelSelector
        ModuleSelectorStyle = ..., FuelPriorityList = [.. viewer.Graph.FuelSelector.FuelPriority.Select(i => i.Name)],
        EnabledRecipes/Assemblers/Modules/Beacons = <sort cache.{Recipes,Assemblers,Modules,Beacons}.Values.Where(x => x.Enabled).Select(x => x.Name)>,  // upstream's private SortEnabled<T>, ~4 LOC, needs re-adding
        OldImport = false
    },
    Annotations = viewer's live AnnotationElements.Select(a => a.ToSaveData()).ToList(),  // AnnotationElement.ToSaveData() already exists (Canvas/Elements/AnnotationElement.cs:296)
    AnnotationDpi = AnnotationDeviceDpi                            // private const already on GraphViewer (`:34`) - the assembly function needs to live in/alongside GraphViewer to reach it, or the const needs widening
}
```
Every field on the right has a live source already in the port; only the assembly function itself (~40-60 LOC) and `SortEnabled` are new.

## 3. PNG/image export (ImageExportForm.cs+.Designer.cs)

Modal `Form`, opened manual-positioned at owner+50/+50 (`MainForm.cs:509-515`, matching the `GraphSummaryForm`/`PresetComparatorForm` pattern documented in `docs/panels-reference.md` §6).

### Fields (captions verbatim, `ImageExportForm.Designer.cs`)

- `fileTextBox` (readonly path display) + `button1` "Browse..." → `SaveFileDialog` (`AddExtension=true`, `Filter="PNG files (*.png)|*.png"`, `InitialDirectory=Application.StartupPath/"Exported Graphs"` created if missing, default `FileName="Foreman Production Flowchart.png"`, `ValidateNames=true`, `OverwritePrompt=true`) — picking a file only fills the textbox, doesn't export yet.
- `groupBox1` "Scale" → `ScaleSelectionBox` (ComboBox), items `["1/20","1/10","1/5","1/2","1","2","3"]` mapped to multipliers `[0.05f,0.1f,0.2f,0.5f,1f,2f,3f]`, default index 4 (scale ×1).
- `TransparencyCheckBox` "Transparent Background" — unchecked ⇒ `graphics.Clear(graphViewer.BackColor)` before drawing; checked ⇒ leaves the bitmap's native alpha=0 background.
- `ViewLimitCheckBox` "Limit to View" — checked ⇒ export exactly what's currently on-screen (`ViewLimitedBounds()` = `(0,0, Width/ViewScale, Height/ViewScale)` in graph space, transform via `ConfigureViewLimitedTransform`); unchecked ⇒ export the whole graph's computed bounds via `graphViewer.GetExportBounds()`.
- `ImageSizeLabel` "Image Size: x x y" — live-recomputed on both checkbox/combo change via `UpdateSizeLabel()`; shows `"Image Size: — (nothing to export)"` when `GraphExportBounds.IsExportable(bounds)` is false (empty graph, no annotations).
- `ExportButton` "Export".

### Export flow (`ImageExportForm.cs:43-91`)

1. Guard: `fileTextBox.Text` non-empty, its directory exists → else `"Directory doesn't exist!"`.
2. `graphViewer.ClearSelection()` (no selection highlight baked into the exported image).
3. View-limit branch: `ExportBitmap(ViewLimitedBounds(), scale, ConfigureViewLimitedTransform)`.
4. Full-graph branch: `exportBounds = graphViewer.GetExportBounds()`; if `!IsExportable(exportBounds)` → `"There is nothing to export. Add nodes or annotations to the graph first."`; else `ExportBitmap(exportBounds, scale, (g,b) => { g.ScaleTransform(scale,scale); g.TranslateTransform(-b.X,-b.Y); })`.
5. `ExportBitmap` (`:72-91`): `new Bitmap(ScaledWidth(bounds,scale), ScaledHeight(bounds,scale))`, `Graphics.FromImage`, `ResetTransform()` then the caller's transform, `SmoothingMode.HighQuality`, conditional `Clear(BackColor)`, **`graphViewer.Paint(graphics, FullGraph: true)`** (same paint entry point the live canvas uses, just against an off-screen `Graphics` and with `FullGraph` forcing every node/link/annotation to draw regardless of viewport culling), `image.Save(path, ImageFormat.Png)`, `Close()` on success. Failure → `"Error saving image. See log for more details."`

### Port status

`GraphExportBounds` (bounds math, `IsExportable`, `ScaledWidth`/`ScaledHeight`) is **already ported verbatim** to `src/Foreman.Mac/Canvas/GraphExportBounds.cs`, and `Foreman.Mac.Canvas.GraphViewer.Paint(SKCanvas, bool fullGraph = false, ...)` (`GraphViewer.cs:295`) already matches the exact call shape `graphViewer.Paint(graphics, FullGraph: true)` needs (SkiaSharp `SKCanvas` standing in for GDI+ `Graphics`, same `fullGraph` bypass-culling flag). What's missing is purely the **dialog itself** — a new Avalonia window with the four fields above, wired to `SKBitmap`/`SKSurface` construction + `SKImage.Encode(SKEncodedImageFormat.Png)` + a `StorageProvider` save-file picker in place of `SaveFileDialog`. `ShellCommands.ExportImage` is currently a disabled stub (`ShellCommands.cs:31`).

## 4. PresetImportForm + the Factorio export pipeline (PresetImportForm.cs, full file)

Modal `Form`. Fields (`PresetImportForm.designer.cs`, captions verbatim): `FactorioLocationGroup` "Factorio Location:" (`FactorioLocationComboBox` pre-seeded from `FactorioPathsProcessor.GetFactorioInstallLocations()`, `FactorioBrowseButton` "Browse..."), `FactorioSettingsGroup` "Factorio Settings:" (static note "NOTE:" / "Language, Active Mods, and Mod options are to be set within Factorio!"), `PresetNameGroup` "Preset Name:" (`PresetNameTextBox`, default text `"Factorio 2.0 Space Age"`, hint labels "5-40 characters: letters, numbers," / "brackets, dash or underscore."), `FactorioModLocationGroup` "Mod Folder Location (leave blank for au..." [auto-detect] (`ModsLocationComboBox`, `ModsBrowseButton` "Browse..."), `OKButton` "Import", `CancelImportButton`/`CancelImportButtonB` "Cancel", `ImportProgressBar` (the `CustomProgressBar` from `docs/panels-reference.md` §1/§45-49, showing percent + `CustomText` status).

### Pre-flight validation (`OKButton_Click`, `:83-129`)

1. `FactorioLocationComboBox.Text` must be an existing directory → `"That directory doesn't seem to exist"`.
2. `PresetNameTextBox.Text.Length >= 5` → `"Preset name has to be longer than 5!"`.
3. Name must not equal `MainForm.DefaultPreset` ("Factorio 2.0 Vanilla") case-insensitively → `"Cant overwrite default preset!"`.
4. If the name matches an existing preset (case-insensitive) → Yes/No `"This preset name is already in use. Do you wish to overwrite?"`.
5. Locate `factorio.exe`: try `installPath/bin/x64/factorio.exe` directly, else accept a path that's already `.../bin/x64` and walk up two levels — else `"Couldnt find factorio.exe (/bin/x64/factorio.exe) - please select a valid Factorio install location"`.
6. `FactorioInstallValidator.TryValidateExecutable` — version must be exactly 2.x and ≥ 2.0.7 (else the exact rejection message from `FactorioInstallValidator.cs:11,17,23,29`).
7. Mods path: use the textbox if it has a `mod-list.json`, else auto-derive via `FactorioPathsProcessor.GetFactorioUserPath(installPath, verboseFail:true)/"mods"` — failure: `"Couldnt auto-locate the mods folder - please manually locate the folder"`.
8. `PresetNameTextBox_TextChanged` (`:428-443`) live-validates on every keystroke independent of the OK guard above: filters to `letters/digits + "()-_. "`, then colors the box Moccasin (<5 chars) / Pink (name collision) / LightGreen (OK) — a background-color-only affordance, no separate label.

### Export pipeline — `ProcessPreset` (`:176-387`), all on a background `Task.Run`

1. **Create test save**: `FactorioBenchmarkRunner.Run(exePath, "--mod-directory \"{modsPath}\" --create temp-save.zip", token)`. `temp-save.zip` lands at `Application.StartupPath/temp-save.zip`. Failure paths: cancellation → silent return; `IsAnotherInstanceRunning` (stdout contains `"Is another instance already running?"`) → warn + cleanup; `run.Crashed` (stdout contains any of `"Received SIGSEGV"`/`"Factorio crashed"`/`"Generating symbolized stacktrace"`/`"Error CrashHandler.cpp"`/`"CrashDump success"`, or nonzero exit + `"Unexpected error occurred"`) → `ReportFactorioCrashIfNeeded` shows the crash message and writes `errorExporting.json`; missing `temp-save.zip` after all that → its own "Factorio did not create the test save..." message.
2. Enable the `foremanexport` mod entry (`FactorioModListHelper.SetModState(modsPath, "foremanexport", enabled:true, removeFromListWhenDisabled:false)`), then copy the bundled Lua mod folder in: `FactorioBundledModHelper.CopyToModsFolder(foremanModName, modsPath, "info.json", "instrument-after-data.lua", "instrument-control.lua")` where `foremanModName = "foremanexport_" + factorioVersionInfo.ProductMajorPart + ".0.0"` (i.e. `foremanexport_2.0.0` for any Factorio 2.x — the source folder for this already exists under `upstream/Foreman/Mods/foremanexport_2.0.0/`).
3. **Run the export**: `--mod-directory "{modsPath}" --instrument-mod foremanexport --benchmark temp-save.zip --benchmark-ticks 1 --benchmark-runs 1`. Deletes `temp-save.zip` and the copied mod folder immediately after (success or failure both clean these up). Result must contain both `<<<END-EXPORT-P1>>>` and `<<<END-EXPORT-P2>>>` markers or it's treated as a mod-conflict/partial-crash failure (with a save-specific sub-message when the output mentions `"temp-save.zip does not exist"`).
4. **Parse the marker-delimited output**: three `<<<START/END-EXPORT-{LN,P1,P2}>>>` sections pulled out by `IndexOf`/substring (not structured parsing) — `LN` = newline-joined localized-name pairs (`$0`→name, `$1`→name, ...), `P1` = icon JSON, `P2` = data JSON. Every `"lid"` property in the data JSON gets resolved against the `LN` dictionary and rewritten to `"localised_name"` before the `lid` key is stripped.
5. Write the new preset: `Application.StartupPath/Presets/{name}.pjson` = pretty-printed data JSON; `{name}.json` = a copy of the app's own `baseCustom.json` template (the per-preset custom-overlay file `PresetJson.MergePresetOverlay` reads — an empty/default overlay, not derived from the export).
6. **Icon processing**: `IconCacheProcessor.PrepareModPaths` + `CreateIconCache` build `{name}.dat` from the icon JSON, resolving each icon path against the mod zips/folders under `installPath/data` + the mods folder — this is the same icon-caching machinery `DataLoadForm` already uses for normal preset loading, not new pipeline code. A partial-failure path here (`X/Y images not found`) is a Yes/No "continue anyway?" prompt, not a hard failure.
7. Cleanup: disable+remove the `foremanexport` mod-list entry. Returns the new preset name on success, `""` on any failure branch (every failure branch already showed its own message and called `CleanupFailedImport`).
8. `CleanupFailedImport` (`:407-426`): best-effort undo — disable/remove the export mod entry, delete `temp-save.zip`, delete the copied mod folder, and (only when a `presetPath`+`foremanModName` were both supplied, i.e. failure happened after step 5) delete the half-written `.pjson`/`.json`/`.dat`.

### Windows-specific pieces that need a macOS rewrite, not a straight port

- **`FactorioBenchmarkRunner.Run`** (`FactorioBenchmarkRunner.cs:29-53`): `Process.Start` with `WindowStyle=Hidden`/`CreateNoWindow=true`/`RedirectStandardOutput` is portable to macOS's `Process` API directly (no `ProcessWindowStyle.Hidden` concept on mac, but `CreateNoWindow`+no `UseShellExecute` already suppresses any window) — the polling loop (`while (!process.HasExited) { read; sleep 100ms; }`) and cancellation-via-`process.Close()` are OS-agnostic and can port near-verbatim. **Task brief already flags** that this needs "a mac-native process approach" — the concrete gap is launching the right binary: mac Factorio is `factorio.app/Contents/MacOS/factorio`, not `bin\x64\factorio.exe`, and mac's `--mod-directory`/`--create`/`--benchmark*` CLI flags need re-verification against the port's existing macOS Factorio-detection code (executable-based, `~/Library/Application Support/factorio` user data, Steam `.app` bundle path per the task brief) rather than reusing this file's hardcoded `bin/x64/factorio.exe` join. Caution for Tasks 5/7's Cancel-button wiring: cancellation isn't preemptive — the read loop's `ReadToEnd()` blocks until the child process exits, so the token is only checked between blocking reads, and a long-running Factorio benchmark won't actually stop early on cancel (see `FactorioBenchmarkRunnerTests.Run_TokenAlreadyCancelledBeforeCall_ReturnsCancelledSentinel_DiscardingTheRealOutput`, which covers only the pre-cancelled case).
- **`FactorioInstallValidator.TryValidateExecutable`** (`FactorioInstallValidator.cs:8-35`): `FileVersionInfo.GetVersionInfo` reads a Windows PE resource section — **no macOS equivalent**. A mac port needs a different version-detection source entirely: likely `factorio.app/Contents/Info.plist`'s `CFBundleShortVersionString`, or parsing `factorio-current.log`'s startup banner, or invoking `factorio --version` and parsing stdout. Whichever is chosen, the four version-gate messages (`:11,17,23,29` — pre-2.0, post-2.x, pre-2.0.7) should carry over verbatim since they're pure UX text with no OS dependency.
- **`DataCaching/FactorioPathsProcessor.cs`**: `GetFactorioInstallLocations()` (`:8-40`) is 100% Windows (registry Steam lookup, `c:\Program Files\Factorio`) and is explicitly **superseded** by the port's existing macOS detection per the task brief — do not port this method. `GetFactorioUserPath`/`ProcessPathString` (`:44-113`) parse `config-path.cfg`'s `write-data=` line and resolve three path-token prefixes (`.factorio`, `__PATH__executable__`, `__PATH__system-write-data__`) against backslash-joined Windows paths — **this logic itself (the token grammar) is cross-platform config-file format**, since `config-path.cfg` is the same file format on every OS; only the path-separator handling (`Replace("/", "\\")`, `Path.Combine` assuming Windows semantics) needs rewriting to forward-slash-native `Path.Combine`, and `__PATH__system-write-data__`'s Windows `ApplicationData` folder needs to become `~/Library/Application Support`. This file is used by **both** `PresetImportForm` (mods-folder auto-detect) and `SaveFileLoadForm`/§5 (initial saves-folder guess) — port it once, share it.
- **`FactorioModListHelper`**/**`FactorioBundledModHelper`**: pure JSON/file-copy, zero OS dependency, port as-is.

### Bundled Lua mods that must ship with the mac build

`upstream/Foreman/Mods/foremanexport_2.0.0/` (info.json, instrument-after-data.lua, instrument-control.lua) and `upstream/Foreman/Mods/foremansavereader_2.0.0/` (info.json, instrument-control.lua) are the only two folders this phase's pipelines need (the `_1.0.0` folders are pre-2.0-Factorio legacy, out of scope per `FactorioInstallValidator`'s 2.x-only gate). These need to become `Content`/`PreserveNewest` items in `Foreman.Mac.csproj` (or `Foreman.Core.csproj`, matching how `Graphics/*.png` was added — `docs/upstream-divergences.md:28`), read relative to `AppPaths.ExecutableDirectory` rather than `Application.StartupPath`.

## 5. SaveFileLoadForm — enabled objects from a Factorio save (SavefileLoadForm.cs, full file)

Not modal-styled like the others — `ProgressForm_Load` fires immediately on show, runs the whole flow, then closes itself; the visible UI is just `"Please wait... Running Factorio"` + a `CancellationButton` "Cancel" (`SavefileLoadForm.Designer.cs`).

### Flow (`:62-197`)

1. **File picker**: `OpenFileDialog`, `Filter="factorio saves (*.zip)|*.zip"`, `InitialDirectory` = `Properties.Settings.Default.LastSaveFileLocation` if it still resolves to a valid Factorio user-data folder (walks up from the remembered path to find a `saves` folder, then checks `factorio-current.log` exists one level up — stale/moved installs silently fall back), else the first auto-detected install's `.../saves` folder via `FactorioPathsProcessor`. Cancel here → `DialogResult.Cancel` immediately, no error shown (`:73-80`).
2. **Locate factorio.exe from the save's own log**: walk up from the chosen `.zip` to find `saves/`'s parent (the user-data dir), read `factorio-current.log`, scan every line containing `"Program arguments"` for the quoted first argument (the exe path Factorio itself was launched with last time) — **this is how upstream avoids needing a separate "where's Factorio" prompt for this dialog**, it trusts the save's own directory structure. `FactorioInstallValidator.TryValidateExecutable` gates on that discovered path same as §4.
3. Copy `foremansavereader_2.0.0` mod in (same `FactorioBundledModHelper` call as §4's export mod, different bundled folder), enable it via `FactorioModListHelper.SetModState(modsPath, "foremansavereader", enabled:true)`.
4. Run: `--instrument-mod foremansavereader --benchmark "{saveFileName}" --benchmark-ticks 1 --benchmark-runs 1` (note: passes the save's **file name only**, not a full path — Factorio resolves it against its own `saves/` folder). Same crash/another-instance detection as §4; a **missing `<<<END-EXPORT-P0>>>` marker** (mod didn't complete) is the specific trigger for `DialogResult.Abort`, which `SettingsWindow.LoadEnabledFromSaveButton_Click`'s caller (`SettingsForm.cs:544-545`) turns into: **`"Error while reading save file. Try running factorio, opening the save game, saving again, and retrying?"`** — this is the exact "re-save in Factorio" message `docs/panels-reference.md` §5 flags without showing its text; now captured verbatim.
5. Parse the single `<<<START/END-EXPORT-P0>>>` section into `SaveFileInfo { Mods, Technologies, Recipes }` (name→version / name→enabled-bool dictionaries).
6. On success, `Properties.Settings.Default.LastSaveFileLocation` is updated to the save's containing folder (the memory the next dialog open reads back in step 1) — **already present in the port** as `AppSettings.LastSaveFileLocation` (`src/Foreman.Mac/Services/AppSettings.cs:22`), just unused so far.

### `ProcessSaveData()` (`:199-271`) — the actual enabled-objects derivation, runs only on `DialogResult.OK`

1. Mod-mismatch check (excluding `foremanexport`/`foremansavereader`/`core`): builds three comma-joined strings (Missing/Wrong-Version/Added mods) and, if any are non-empty, shows a combined OKCancel warning (`MessageBoxButtons.OKCancel`, `SavefileLoadForm.cs:232`) (`"selected save file mods do not match preset mods; out of {0} mods:" + ...`) — Cancel aborts the whole apply, leaving `EnabledObjects` untouched.
2. `EnabledObjects.Clear()`, unconditionally re-add `DCache.PlayerAssembler` if present.
3. Every recipe whose name starts with `"§§"` (Foreman's synthetic pseudo-recipes — heat, resource extraction, etc.) is **always enabled** regardless of the save data; every other recipe is enabled iff the save's `Recipes` dict has it **and** marked `true`.
4. Assemblers/Beacons/Modules are derived transitively, not read from the save directly: an assembler/beacon is enabled iff **any** of its associated items has **any** production recipe that ended up enabled in step 3; a module the same way through its single `AssociatedItem`. (Identical derivation shape to §6's science-pack path — worth sharing one helper.)

## 6. SciencePacksLoadForm — assign from science packs (SciencePacksLoadForm.cs, full file)

Non-modal-styled like §5: a grid of science-pack icon buttons populates immediately in the constructor (`PopulateSciencePackOptions`, `:35-77`) — no Factorio process involved at all, purely local `DataCache` computation. `MaxColumns=14`, 48px `NFButton`s laid into an auto-computed row/column grid sized to `DCache.SciencePacks.Count`.

- Each button starts `DisabledPackBGColor` (`DarkRed`); clicking toggles it to `EnabledPackBGColor` (`DeepSkyBlue`) and cascades (`Button_Click`, `:79-105`): enabling a pack also force-enables every pack **it depends on** (`DCache.SciencePackPrerequisites[clicked].Contains(other)`); disabling a pack also force-disables every pack **that depends on it**. Upstream's own code comment (`:87-88`) flags this as imprecise for a science pack reachable via multiple alternate tech-tree branches (OR-prerequisites get treated as AND) — a known, accepted upstream limitation, not a bug to silently fix in the port.
- Hover shows `dob.FriendlyName` via a plain `ToolTip` positioned at cursor+`(15,5)`.
- `ConfirmationButton` "Confirm" (`:121-161`): collects the checked packs into a `HashSet<IItem>`, clears `EnabledObjects`, re-adds `PlayerAssembler`, then for every `ITechnology` that's `Available` **and** whose full `SciPackList` is a subset of the accepted packs (`!tech.SciPackList.Except(accepted).Any()`), unions in `tech.UnlockedRecipes`. Assembler/Beacon/Module derivation is the **identical transitive-dependency block** as §5's `ProcessSaveData` steps — same shared-helper opportunity.
- `CancellationButton` "Cancel" just closes with `DialogResult.Cancel`.
- Caption: dialog title `"Select researched science packs"`.

## 7. Preset management (delete + import wiring)

- **Delete preset — already fully ported.** `SettingsWindow.axaml.cs:599-616` (`DeletePresetAsync`) matches upstream's `DeletePresetMenuItem_Click` (`SettingsForm.cs:310-329`) essentially line-for-line: same confirm message, same three-extension delete (`.pjson`/`.json`/`.dat`) via `PresetProcessor.GetPresetPath`, same list-removal. No further work needed here; phase 6 doesn't need to touch this.
  - Worth a close read before touching adjacent code, not fixing: upstream's own enable-guard is internally inconsistent — the context-menu caption logic disables `DeletePresetMenuItem` when `rclickedPreset.IsCurrentlySelected` is true (`SettingsForm.cs:290`), while the click handler's own safety check requires `selectedPreset.IsCurrentlySelected` to be true to do anything (`SettingsForm.cs:311`) — and since `PresetListBox` itself excludes the currently-active preset from its `Items` entirely (`docs/panels-reference.md` §5), `IsCurrentlySelected` is always `false` for every row in that list, meaning the click handler's guard condition can never be satisfied upstream. The port's `DeletePresetAsync` sidesteps this by not replicating the guard at all (any non-default preset can be deleted) — that's a deliberate, already-shipped deviation from upstream's apparently-dead code path, not something phase 6 needs to "fix."
- **Import preset wiring** — `ImportPresetButton` already calls through to a placeholder (`SettingsWindow.axaml.cs:626-628`); once `PresetImportForm` (§4) exists, this button's real job per upstream `SettingsForm.cs` (referenced from `docs/upstream-divergences.md:158`) is: open the import dialog, and on success either force-reload (if the imported name overwrote the *active* preset — set `Options.RequireReload=true`, which `SettingsWindow`'s options record already has a field for) or prompt Yes/No to switch to the newly-imported preset now.

## 8. Cross-preset save compatibility (ProductionGraphViewer.cs:1409-1507, PresetSelectionForm.cs)

**The matching engine is already fully ported** (§1) — `PresetProcessor.TestPreset` + `PresetErrorPackage`, and every input field it needs (`GraphViewerSaveDocument.SavedPresetName`/`IncludedMods`, `ProductionGraphSaveDocument.IncludedItems`/`IncludedAssemblers`/`IncludedQualities`/`IncludedRecipes`/`IncludedPlantProcesses`) all exist verbatim in `src/Foreman.Core/Serialization/GraphSaveDocuments.cs:95-99,124-125`. What's missing is **only the orchestration loop and the picker dialog** — no logic needs re-deriving, just wiring.

### `ResolveChosenPresetAsync` (`ProductionGraphViewer.cs:1434-1507`) — the orchestration upstream's `LoadFromSaveDocument` runs before touching the graph

1. Pull `modSet`/`itemNames`/`assemblerNames`/`qualityNames`/`recipeShorts`/`plantShorts` straight off the save document's `Included*` fields (all already-ported types).
2. `allPresets = MainForm.GetValidPresetsList()` — the port's `PresetResolver.BuildPresetList` (`src/Foreman.Mac/Services/PresetResolver.cs:30-42`) is the direct equivalent, already exists.
3. **Fast path**: if the save's `SavedPresetName` matches an installed preset by name, run `TestPreset` against *just that one* preset; if `errors.ErrorCount == 0`, use it directly with no dialog. Otherwise that preset is dropped from the candidate list and its errors are kept for the slow path below.
4. **Slow path** (no exact name match, or the name match had errors): run `TestPreset` against **every** remaining installed preset (parallel-safe, all `async`/pure-read), collect one `PresetErrorPackage` per preset, then show `PresetSelectionForm` with the full list. Returns `null` (abort the load) if the user cancels.
5. **Silent-switch path**: exact name match with zero errors, but that name isn't the *currently active* preset (`Properties.Settings.Default.CurrentPresetName`) — no dialog at all, just an info message: `string.Format("Loaded graph uses a different Preset.\nPreset switched from \"{0}\" to \"{1}\"", previousPresetName, newPresetName)`, then the setting is updated and saved. **This is the common case for "load a graph you saved under this same preset yesterday, but you've since switched presets in the app"** — worth calling out since it's silent/automatic, not a dialog.

### `PresetSelectionForm` (80+214=294 LOC) — the picker dialog, shown only on the slow path

- Title `"Please select Preset"`; body labels `"No preset was found to match the saved graph exactly."` / `"Please select which preset you wish to use based on the given compatibility ratings."`.
- `PresetSelectionListView`: 4 columns `Preset` / `Mods (%)` / `Items (%)` / `Recipes (%)`, one row per candidate preset, sorted by `PresetErrorPackage.CompareTo` (mods-missing-count, then mods-added-count, then `MICount` — ascending severity, so the closest match sorts first per `InfoPackageClasses.cs:40-47`, already ported). Percentages: `(required - missing - wrongVersion - added) / required` for mods, `(required - missing) / required` for items, `(required - missing - incorrect) / required` for recipes, formatted `"%00"`.
- Row tooltip: a multi-line breakdown (`"Mods:\n     ({0}) Correct\n     ({0}) Missing\n..."` etc., verbatim strings at `PresetSelectionForm.cs:42-54`) — full Correct/Missing/Extra/Wrong-Version counts per category.
- `ConfirmationButton` "Load with seleted preset" [sic — upstream typo, preserve verbatim] and double-click both commit the selected row's preset as `ChosenPreset` + `DialogResult.OK`; `CancellingButton` "Dont Load" [sic] → `DialogResult.Cancel`.

### Port status

Nothing exists yet: no `ResolveChosenPresetAsync` equivalent (the port's `GraphViewer.LoadDocument`, `src/Foreman.Mac/Canvas/GraphViewer.cs:115-147`, loads straight into whatever `DataCache` is already booted — see its own doc comment at `:111-114` acknowledging this is out of scope pending "a dialog"), and no `PresetSelectionForm` window. Given the matching engine is done, this is realistically a ~150-250 LOC task (orchestration method + one new Avalonia window), not the open-ended research item the "previously backlogged" framing might suggest.

## 9. Cross-cutting: current port state summary

| Piece | Port status | Where |
|---|---|---|
| `GraphSaveCodec`/`GraphSaveWriter`/`GraphSaveLoader`/`GraphSaveReader`/`GraphSaveJson`/wire mapper | Ported (minus the two `ProductionGraphViewer`-typed overloads, §2) | `src/Foreman.Core/Serialization/` |
| `PresetProcessor.TestPreset`, `PresetErrorPackage` | Ported verbatim | `src/Foreman.Core/DataCaching/{PresetProcessor,InfoPackageClasses}.cs` |
| `GraphExportBounds` | Ported verbatim | `src/Foreman.Mac/Canvas/GraphExportBounds.cs` |
| `GraphViewer.Paint(canvas, fullGraph:)` | Ported, matches upstream's export call shape | `src/Foreman.Mac/Canvas/GraphViewer.cs:295` |
| `AnnotationElement.ToSaveData()`/`FromSaveData()` | Ported | `src/Foreman.Mac/Canvas/Elements/AnnotationElement.cs:296-300` |
| `AppSettings.LastSaveFileLocation`/`CurrentPresetName` | Ported, fields exist, unused so far | `src/Foreman.Mac/Services/AppSettings.cs:10,22` |
| Delete preset | Ported | `src/Foreman.Mac/Views/SettingsWindow.axaml.cs:599-616` |
| Load graph (file picker + `LoadDocument`) | Ported, missing dirty-check + title-bar + preset-mismatch handling | `src/Foreman.Mac/MainWindow.axaml.cs:256-279` |
| Save/SaveAs/Import/ExportImage/Help | Disabled `ShellCommand` stubs | `src/Foreman.Mac/ShellCommands.cs:28-31,37` |
| Cmd+S keybinding | Captured by WASD pan instead | `docs/upstream-divergences.md:141-142` |
| `PresetImportForm`, `SaveFileLoadForm`, `SciencePacksLoadForm`, `PresetSelectionForm` | Not started; buttons show placeholders | `src/Foreman.Mac/Views/SettingsWindow.axaml.cs:526-536,624-628` |
| `FactorioBenchmarkRunner`/`FactorioInstallValidator`/`FactorioModListHelper`/`FactorioBundledModHelper`/`FactorioPathsProcessor` (save/import halves) | Not started | n/a |
| Recent-files list, autosave | **Upstream has neither** — negative finding, nothing to port | grep-confirmed absent from `MainForm.cs`/`.Designer.cs`/`Properties/Settings.cs` |

## 10. Porting sequence (dependency-ordered)

1. **`GraphSaveWriter`-from-live-viewer glue** (~50 LOC new, `Foreman.Mac`) — the `BuildViewer`-equivalent assembly function from §2's spec, plus a `SortEnabled<T>` helper. Everything below that touches Save depends on this.
2. **Save/SaveAs + dirty-tracking + title bar** (~150 LOC) — `savefilePath`/`savefileBaselineJson` fields on `MainWindow`, `SaveGraph`/`SaveGraphAs` (StorageProvider save-file picker, `.fjson` filter, `Saved Graphs`-equivalent default folder — pick a macOS-appropriate location per the task's never-Windows constraint, e.g. `~/Documents/Foreman` or Application Support, not next to the executable), `TestGraphSavedStatus` (needs a Yes/No/Cancel `Dialogs` variant — only Yes/No exists today, `src/Foreman.Mac/Services/Dialogs.cs`), title-bar format string, wiring `ShellCommands.Save`/`SaveAs` to `IsImplemented:true`, and un-panning Cmd+S per the existing divergence note.
3. **Wire `Load`'s dirty-check + title-bar bookkeeping** into the already-working `LoadGraphAsync` (~30 LOC) — small follow-up now that step 2 has the fields/format string to reuse.
4. **Import Graph** (~120 LOC) — `ImportNodesFromDocument`'s centroid/offset/selection algorithm (§2) on `Foreman.Mac.Canvas.GraphViewer`, reusing whatever `NodeClipboard`'s paste path already has in common; wire `ShellCommands.Import` + the file picker.
5. **PNG export dialog** (~250 LOC) — new Avalonia window reproducing §3's four fields, `SKBitmap`/`SKSurface` construction + PNG encode + StorageProvider save picker around the already-ported `GraphExportBounds`/`Viewer.Paint(fullGraph:)`. No dependency on steps 1-4; can run in parallel.
6. **`FactorioPathsProcessor`'s config-path parsing** (~90 LOC, cross-platform-rewritten per §4) — shared prerequisite for steps 7 and 8's mods-folder/saves-folder auto-detection.
7. **`FactorioBenchmarkRunner`/`FactorioModListHelper`/`FactorioBundledModHelper` + a macOS `FactorioInstallValidator`** (~150 LOC) — the shared process-launch/mod-list/version-check trio steps 8 and 9 both need. Bundle `Mods/foremanexport_2.0.0/` and `Mods/foremansavereader_2.0.0/` as Content per §4.
8. **`SaveFileLoadForm`** (~350 LOC) — depends on step 6+7. §5's flow + `ProcessSaveData`'s transitive enabled-object derivation (factor as a shared helper, since §6 needs the identical block).
9. **`SciencePacksLoadForm`** (~280 LOC) — no dependency on step 6/7 (pure local computation), but shares the transitive-derivation helper from step 8; can be built in parallel with step 8.
10. **`PresetImportForm`** (~900 LOC, the largest single item) — depends on step 6+7. §4's full validation + pipeline + `PresetImportForm`'s own `Import`/`Delete`-adjacent `SettingsWindow` wiring (force-reload / switch-to-new-preset prompt, §7).
11. **`ResolveChosenPresetAsync` + `PresetSelectionForm`** (~250 LOC) — depends on nothing above except the already-ported `TestPreset`/`PresetErrorPackage`; wire into `GraphViewer.LoadDocument`'s call sites (`LoadGraphAsync`, and the "reload for current preset" path once Save/SaveAs exist to make round-tripping meaningful). Can be built any time after step 1; ordered last here only because it's lowest-urgency (silent-switch and exact-match cases work today without it — only the "no match at all" case needs the dialog).

Steps 1-4 (~350 LOC) close out the graph-file half; steps 5 (~250 LOC) and 11 (~250 LOC) are independent side quests; steps 6-10 (~1770 LOC) are the Factorio-process pipeline and its two consumer dialogs, the single largest chunk of this phase by LOC and by macOS-specific risk.

## 11. Biggest porting risks

1. **`FactorioInstallValidator`'s version check has no macOS source to read from.** `FileVersionInfo.GetVersionInfo` is a Windows-PE-only API; there's no drop-in .NET equivalent for a mac `.app` bundle. Whatever replacement is chosen (`Info.plist` `CFBundleShortVersionString`, log-file parsing, or `factorio --version` stdout) needs its own validation against a real installed Factorio 2.x before the rest of §4/§5's pipeline can be trusted — get this wrong and every downstream error message ("Factorio version below 2.0...") reports nonsense.
2. **`FactorioBenchmarkRunner`'s crash/another-instance detection is stdout-substring matching against Windows Factorio's exact log wording** (`"Received SIGSEGV"`, `"Is another instance already running?"`, etc.) — macOS Factorio's crash-handler and instance-lock messages have not been verified to use identical strings. A silent mismatch here doesn't fail loudly; it just makes the import/save-read pipeline hang or report a generic failure instead of the specific, actionable message upstream shows.
3. **The `--mod-directory`/`--benchmark`/`--instrument-mod` CLI contract is assumed identical across platforms but unverified.** All three pipelines (§4 export, §5 save-read) depend on Factorio's headless benchmark-mode flags behaving the same on macOS as Windows, including where `--create`/`--benchmark` resolve relative filenames (`temp-save.zip`, the save's bare file name in §5) against `saves/` — if the working-directory assumption differs on mac (e.g. sandboxed `.app` launches with a different cwd than the process's own directory), every file-existence check downstream (`temp-save.zip` missing, marker strings absent) will misfire with the wrong error message even when the underlying operation actually succeeded.
4. **Writing into `AppPaths.ExecutableDirectory` (Presets/Saved-Graphs/Exported-Graphs, all currently resolved relative to the executable, matching upstream's `Application.StartupPath` pattern) is a real design question for a signed macOS `.app` bundle**, not just a path-separator fix. `PresetResolver`/`PresetProcessor`/the already-shipped `DeletePresetAsync` all write there today and it works in dev, but a distributed, code-signed `.app` bundle is conventionally read-only at its own bundle path — Preset Import (§4, writes `.pjson`/`.json`/`.dat`) and Save/SaveAs's default folder (§2) are the two places this phase adds *new* writes into that same directory, at higher volume than the existing delete-only case. Decide once (e.g. move to `~/Library/Application Support/Foreman/` for Presets, `~/Documents` for saved graphs) rather than each new dialog picking its own convention.
5. **`ProcessSaveData` (§5) and `SciencePacksLoadForm`'s `ConfirmationButton_Click` (§6) implement the identical assembler/beacon/module transitive-enable derivation twice upstream** (`SavefileLoadForm.cs:244-269` vs `SciencePacksLoadForm.cs:132-157`, byte-for-byte identical loop structure). Porting each in isolation risks porting the duplication too; factor it into one shared helper the first time either dialog is built, since the second dialog's implementation should just call it.
6. **Validating against a real, installed Factorio binary is exclusively Jozef's-gate scope.** It's a manual check he runs himself, never part of the automated test suite — launching the real Factorio process from a test would be slow, machine-dependent, and (per this project's global constraints) a real Factorio launch isn't permitted from an agent-run verification pass.
