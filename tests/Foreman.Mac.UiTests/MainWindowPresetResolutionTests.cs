using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Services;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Covers io-reference.md §8's cross-preset resolution (phase 6 Task 8): MainWindow.ResolveChosenPresetAsync
    //and its wiring into LoadGraphJsonAsync. Runs against the two real bundled test presets (Vanilla/Space
    //Age, same fixtures DialogWindowsEndToEndTests/PresetComparatorWindowTests already use) rather than
    //hand-rolled preset files, since PresetProcessor.TestPreset always reads through PresetProcessor.
    //GetPresetPath - the user Presets directory override only ever points at an empty temp dir here, so
    //nothing under a real ~/Library path is ever written or relied on.
    public class MainWindowPresetResolutionTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string SpaceAgePresetName = "Factorio 2.0 Space Age";

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static MainWindow NewWindow(string currentPresetName, string settingsDir) {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { CurrentPresetName = currentPresetName };
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: NewTempDir());
            window.SettingsService = new SettingsService(settingsDir);
            return window;
        }

        private static GraphViewerSaveDocument NewEmptyDocument(string? savedPresetName, Dictionary<string, string>? includedMods = null) => new() {
            Version = GraphSaveFormat.SaveFormatVersion,
            SavedPresetName = savedPresetName,
            IncludedMods = includedMods ?? new Dictionary<string, string>(),
            ProductionGraph = GraphSaveCodec.BuildProductionGraph(new ProductionGraph()),
        };

        //An empty document (no IncludedItems/Recipes/etc.) matches any preset with zero errors as long as
        //IncludedMods matches that preset's own declared mod list exactly - PresetProcessor.TestPreset flags
        //anything in the preset's own mods that isn't in the requested set as "Added".
        private static Dictionary<string, string> RealModListFor(Preset preset) =>
            PresetProcessor.ReadPresetInfo(preset).ModList ?? new Dictionary<string, string>();

        //---- Fast path: exact SavedPresetName match, zero errors -> no dialog -------------------------

        [AvaloniaFact]
        public async Task ResolveChosenPresetAsync_ExactMatchZeroErrors_SkipsDialogAndReturnsMatchedPreset() {
            var vanilla = new Preset(VanillaPresetName, true, true);
            var window = NewWindow(VanillaPresetName, NewTempDir());
            window.PresetSelectionDialogStub = _ => throw new InvalidOperationException("fast path must not show the picker");
            window.PresetSwitchInfoStub = _ => throw new InvalidOperationException("fast path must not silently switch when already active");

            GraphViewerSaveDocument document = NewEmptyDocument(VanillaPresetName, RealModListFor(vanilla));

            Preset? chosen = await window.ResolveChosenPresetAsync(document);

            Assert.NotNull(chosen);
            Assert.Equal(VanillaPresetName, chosen!.Name);
            Assert.Equal(VanillaPresetName, window.Settings!.CurrentPresetName);
        }

        //---- Silent-switch: exact match, zero errors, but not the currently active preset -------------

        [AvaloniaFact]
        public async Task ResolveChosenPresetAsync_ExactMatchZeroErrors_DifferentActivePreset_SilentlySwitchesAndPersists() {
            var vanilla = new Preset(VanillaPresetName, true, true);
            string settingsDir = NewTempDir();
            var window = NewWindow(SpaceAgePresetName, settingsDir);
            window.PresetSelectionDialogStub = _ => throw new InvalidOperationException("silent switch must not show the picker");

            string? capturedMessage = null;
            window.PresetSwitchInfoStub = message => { capturedMessage = message; return Task.CompletedTask; };

            GraphViewerSaveDocument document = NewEmptyDocument(VanillaPresetName, RealModListFor(vanilla));

            Preset? chosen = await window.ResolveChosenPresetAsync(document);

            Assert.NotNull(chosen);
            Assert.Equal(VanillaPresetName, chosen!.Name);
            Assert.Equal(
                "Loaded graph uses a different Preset.\nPreset switched from \"" + SpaceAgePresetName + "\" to \"" + VanillaPresetName + "\"",
                capturedMessage);
            Assert.Equal(VanillaPresetName, window.Settings!.CurrentPresetName);

            AppSettings persisted = new SettingsService(settingsDir).Load();
            Assert.Equal(VanillaPresetName, persisted.CurrentPresetName);
        }

        //---- Slow path: no exact match -> every installed preset tested, ranked picker decides --------

        [AvaloniaFact]
        public async Task ResolveChosenPresetAsync_NoExactMatch_ShowsDialogWithEveryInstalledPreset() {
            string settingsDir = NewTempDir();
            var window = NewWindow(VanillaPresetName, settingsDir);

            List<PresetErrorPackage>? captured = null;
            window.PresetSelectionDialogStub = errors => {
                captured = errors;
                return Task.FromResult<Preset?>(errors.First(e => e.Preset.Name == SpaceAgePresetName).Preset);
            };

            GraphViewerSaveDocument document = NewEmptyDocument(savedPresetName: "Not An Installed Preset");

            Preset? chosen = await window.ResolveChosenPresetAsync(document);

            Assert.NotNull(captured);
            Assert.Equal(2, captured!.Count);
            Assert.Contains(captured, e => e.Preset.Name == VanillaPresetName);
            Assert.Contains(captured, e => e.Preset.Name == SpaceAgePresetName);

            Assert.NotNull(chosen);
            Assert.Equal(SpaceAgePresetName, chosen!.Name);
            Assert.Equal(SpaceAgePresetName, window.Settings!.CurrentPresetName);

            AppSettings persisted = new SettingsService(settingsDir).Load();
            Assert.Equal(SpaceAgePresetName, persisted.CurrentPresetName);
        }

        //A name match whose mods don't line up still contributes its own already-computed errors to the
        //slow-path list, rather than being silently dropped or re-tested a second time.
        [AvaloniaFact]
        public async Task ResolveChosenPresetAsync_ExactNameMatchHasErrors_FallsThroughWithThatPresetsErrorsIncluded() {
            var window = NewWindow(VanillaPresetName, NewTempDir());

            List<PresetErrorPackage>? captured = null;
            window.PresetSelectionDialogStub = errors => { captured = errors; return Task.FromResult<Preset?>(null); };

            var badMods = new Dictionary<string, string> { ["totally-not-a-real-mod"] = "1.0.0" };
            GraphViewerSaveDocument document = NewEmptyDocument(VanillaPresetName, badMods);

            await window.ResolveChosenPresetAsync(document);

            Assert.NotNull(captured);
            Assert.Equal(2, captured!.Count);
            PresetErrorPackage vanillaErrors = captured.Single(e => e.Preset.Name == VanillaPresetName);
            Assert.Contains("totally-not-a-real-mod|1.0.0", vanillaErrors.MissingMods);
        }

        //---- Cancel: dialog returns null -> resolution aborts, nothing persists ------------------------

        [AvaloniaFact]
        public async Task ResolveChosenPresetAsync_DialogCancelled_ReturnsNullAndLeavesSettingsUnchanged() {
            string settingsDir = NewTempDir();
            var window = NewWindow(VanillaPresetName, settingsDir);
            window.PresetSelectionDialogStub = _ => Task.FromResult<Preset?>(null);

            GraphViewerSaveDocument document = NewEmptyDocument(savedPresetName: "Not An Installed Preset");

            Preset? chosen = await window.ResolveChosenPresetAsync(document);

            Assert.Null(chosen);
            Assert.Equal(VanillaPresetName, window.Settings!.CurrentPresetName);
            Assert.False(File.Exists(Path.Combine(settingsDir, "Library", "Application Support", "Foreman", "settings.json")));
        }

        //---- LoadGraphJsonAsync wiring: cancel aborts the load, leaving the current graph intact -------

        //A pre-populated node makes this a real intactness check rather than a vacuous one - an empty cache
        //trivially has nothing to lose, so a broken cancel path that cleared the graph anyway would still
        //pass "Assert.Empty" on nothing. Loading real data also lets the node actually exist (CreateSupplierNode
        //needs a live Items table), which the previous bare `new DataCache` couldn't offer.
        [AvaloniaFact]
        public async Task LoadGraphJsonAsync_ResolutionCancelled_LeavesGraphAndDataCacheUntouched() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
            var window = NewWindow(VanillaPresetName, NewTempDir());
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new Foreman.Models.ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);
            window.PresetSelectionDialogStub = _ => Task.FromResult<Preset?>(null);

            string json = GraphSaveCodec.WriteViewerDocumentToString(NewEmptyDocument(savedPresetName: "Not An Installed Preset"));

            await window.LoadGraphJsonAsync(json, "/tmp/does-not-matter.fjson");

            Assert.Same(cache, window.DataCache);
            Assert.Single(window.GraphCanvas.Viewer.NodeElements);
            Assert.Null(window.SaveFilePath);
        }
    }
}
