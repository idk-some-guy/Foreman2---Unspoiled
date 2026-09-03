using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Models;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Covers io-reference.md §2's Save/SaveAs/dirty-tracking/title-bar/Load-bookkeeping contract (phase 6
    //Task 1). Save/SaveAs route through MainWindow.SaveFilePathStub - the same picker-bypass seam
    //GraphSummaryWindow.SaveFilePathStub already established - rather than a real StorageProvider dialog.
    public class MainWindowSaveLoadTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        private static string TempSavePath() => Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".fjson");

        //---- Save / SaveAs round-trip -------------------------------------------------------------------

        [AvaloniaFact]
        public async Task SaveGraphAsAsync_WritesFjsonAndCapturesBaselineAndTitleBar() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = new DataCache(filterRecipes: true);
            window.Settings = new AppSettings { CurrentPresetName = "Some Preset" };
            string path = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(path);

            try {
                await window.SaveGraphAsAsync();

                Assert.True(File.Exists(path));
                Assert.Equal(path, window.SaveFilePath);
                Assert.NotNull(window.SaveFileBaselineJson);
                Assert.Equal(File.ReadAllText(path), window.SaveFileBaselineJson);
                Assert.Equal("Foreman 2 (Some Preset) - " + path, window.Title);
                Assert.NotNull(GraphSaveCodec.ReadViewer(window.SaveFileBaselineJson!));
            } finally {
                File.Delete(path);
            }
        }

        [AvaloniaFact]
        public async Task SaveGraphAsAsync_NullFromStub_DoesNotWriteOrSetPath() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = new DataCache(filterRecipes: true);
            window.SaveFilePathStub = () => Task.FromResult<string?>(null);

            await window.SaveGraphAsAsync();

            Assert.Null(window.SaveFilePath);
        }

        [AvaloniaFact]
        public async Task SaveGraphOrPromptAsync_NoExistingPath_FallsBackToSaveAs() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = new DataCache(filterRecipes: true);
            string path = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(path);

            try {
                await window.SaveGraphOrPromptAsync();

                Assert.Equal(path, window.SaveFilePath);
                Assert.True(File.Exists(path));
            } finally {
                File.Delete(path);
            }
        }

        [AvaloniaFact]
        public async Task SaveGraphOrPromptAsync_ExistingPath_OverwritesWithoutReopeningThePicker() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = new DataCache(filterRecipes: true);
            string path = TempSavePath();
            int stubCalls = 0;
            window.SaveFilePathStub = () => { stubCalls++; return Task.FromResult<string?>(path); };

            try {
                await window.SaveGraphOrPromptAsync();
                await window.SaveGraphOrPromptAsync();

                Assert.Equal(1, stubCalls);
                Assert.Equal(path, window.SaveFilePath);
            } finally {
                File.Delete(path);
            }
        }

        //---- TestGraphSavedStatus: empty-untitled-graph-never-prompts rule ------------------------------

        [AvaloniaFact]
        public async Task TestGraphSavedStatusAsync_EmptyUntitledGraph_NeverPrompts() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = new DataCache(filterRecipes: true);
            window.DiscardUnsavedGraphConfirmStub = () => throw new InvalidOperationException("must not prompt for an empty untitled graph");

            bool result = await window.TestGraphSavedStatusAsync();

            Assert.True(result);
        }

        [AvaloniaFact]
        public async Task TestGraphSavedStatusAsync_NonEmptyUntitledGraph_PromptsAndHonorsCancel() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);
            window.DiscardUnsavedGraphConfirmStub = () => Task.FromResult(false);

            Assert.False(await window.TestGraphSavedStatusAsync());

            window.DiscardUnsavedGraphConfirmStub = () => Task.FromResult(true);

            Assert.True(await window.TestGraphSavedStatusAsync());
        }

        //---- TestGraphSavedStatus: re-serialize diff against the save baseline --------------------------

        [AvaloniaFact]
        public async Task TestGraphSavedStatusAsync_SavedPathUnmodified_ReturnsTrueWithoutPrompting() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = new DataCache(filterRecipes: true);
            string path = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(path);
            window.SaveBeforeContinuingChoiceStub = () => throw new InvalidOperationException("must not prompt when nothing changed since the save");

            try {
                await window.SaveGraphAsAsync();

                Assert.True(await window.TestGraphSavedStatusAsync());
            } finally {
                File.Delete(path);
            }
        }

        [AvaloniaFact]
        public async Task TestGraphSavedStatusAsync_SavedPathModified_ChoiceGatesTheOutcome() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            string path = TempSavePath();
            window.SaveFilePathStub = () => Task.FromResult<string?>(path);

            try {
                await window.SaveGraphAsAsync();
                var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
                window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);

                window.SaveBeforeContinuingChoiceStub = () => Task.FromResult(ConfirmChoice.Cancel);
                Assert.False(await window.TestGraphSavedStatusAsync());

                string baselineBeforeNo = window.SaveFileBaselineJson!;
                window.SaveBeforeContinuingChoiceStub = () => Task.FromResult(ConfirmChoice.No);
                Assert.True(await window.TestGraphSavedStatusAsync());
                Assert.Equal(baselineBeforeNo, window.SaveFileBaselineJson); //"No" continues without saving

                window.SaveBeforeContinuingChoiceStub = () => Task.FromResult(ConfirmChoice.Yes);
                Assert.True(await window.TestGraphSavedStatusAsync());
                Assert.NotEqual(baselineBeforeNo, window.SaveFileBaselineJson); //"Yes" re-saves over the path
            } finally {
                File.Delete(path);
            }
        }

        //---- New calls TestGraphSavedStatus first -------------------------------------------------------

        [AvaloniaFact]
        public async Task NewGraphAsync_DirtyGraphCancelled_LeavesGraphIntact() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);
            window.DiscardUnsavedGraphConfirmStub = () => Task.FromResult(false);

            await window.NewGraphAsync();

            Assert.NotEmpty(window.GraphCanvas.Viewer.NodeElements);
        }

        //---- Load bookkeeping: enabled-flag application + the 4 non-rate-unit Default* settings ---------

        [AvaloniaFact]
        public async Task LoadDocument_AppliesSaveUiOntoLiveCacheEnabledFlagsAndSelectors() {
            DataCache cache = await GetCacheAsync();
            IRecipe keepEnabled = cache.Recipes.Values.First(r => r.Enabled);
            IRecipe forceDisabled = cache.Recipes.Values.First(r => r.Enabled && r != keepEnabled);
            string fuelName = cache.Items.Values.First().Name;

            var document = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(new ProductionGraph()),
                Ui = new GraphViewerUiSaveData {
                    AssemblerSelectorStyle = AssemblerSelector.Style.Best,
                    ModuleSelectorStyle = ModuleSelector.Style.Productivity,
                    FuelPriorityList = [fuelName],
                    EnabledRecipes = [keepEnabled.Name],
                    EnabledAssemblers = [.. cache.Assemblers.Values.Where(a => a.Enabled).Select(a => a.Name)],
                    EnabledModules = [.. cache.Modules.Values.Where(m => m.Enabled).Select(m => m.Name)],
                    EnabledBeacons = [.. cache.Beacons.Values.Where(b => b.Enabled).Select(b => b.Name)],
                },
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(document);

            var control = new GraphCanvasControl();
            GraphLoadResult result = control.Viewer.LoadDocument(cache, json);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(keepEnabled.Enabled);
            Assert.False(forceDisabled.Enabled);
            Assert.Equal(AssemblerSelector.Style.Best, control.Viewer.Graph.AssemblerSelector.DefaultSelectionStyle);
            Assert.Equal(ModuleSelector.Style.Productivity, control.Viewer.Graph.ModuleSelector.DefaultSelectionStyle);
            Assert.Contains(control.Viewer.Graph.FuelSelector.FuelPriority, i => i.Name == fuelName);
        }

        //Ports the setEnablesFromJson gate (phase 6 Task 8, upstream ProductionGraphViewer.cs:1289): a
        //preset-switch reload must not clobber the freshly booted cache's own enabled flags against the OLD
        //preset's saved enabled-name list. Dedicated cache (not GetCacheAsync's shared fixture) - this test's
        //whole point is proving no mutation happens, so a bug here must not also poison every other test.
        [AvaloniaFact]
        public async Task LoadDocument_SetEnablesFromJsonFalse_LeavesLiveCacheEnabledFlagsUntouched() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
            IRecipe enabledBefore = cache.Recipes.Values.First(r => r.Enabled);
            IRecipe disabledBefore = cache.Recipes.Values.First(r => !r.Enabled);

            var document = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(new ProductionGraph()),
                Ui = new GraphViewerUiSaveData {
                    //Names a completely different enabled set than the live cache's own - if the gate didn't
                    //hold, this alone would flip enabledBefore off and leave disabledBefore off too.
                    EnabledRecipes = [],
                    EnabledAssemblers = [],
                    EnabledModules = [],
                    EnabledBeacons = [],
                },
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(document);

            var control = new GraphCanvasControl();
            GraphLoadResult result = control.Viewer.LoadDocument(cache, json, setEnablesFromJson: false);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(enabledBefore.Enabled);
            Assert.False(disabledBefore.Enabled);
        }

        [AvaloniaFact]
        public void ApplyLoadedGraphUiState_ResyncsDefaultAssemblerModuleExtraProdAndNodeDirectionSettings() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            window.GraphCanvas.Viewer.Graph.AssemblerSelector.DefaultSelectionStyle = AssemblerSelector.Style.Best;
            window.GraphCanvas.Viewer.Graph.ModuleSelector.DefaultSelectionStyle = ModuleSelector.Style.Productivity;
            window.GraphCanvas.Viewer.Graph.EnableExtraProductivityForNonMiners = true;
            window.GraphCanvas.Viewer.Graph.DefaultNodeDirection = NodeDirection.Down;

            window.ApplyLoadedGraphUiState("/tmp/does-not-need-to-exist.fjson");

            Assert.Equal("/tmp/does-not-need-to-exist.fjson", window.SaveFilePath);
            Assert.Equal(AssemblerSelector.Style.Best, window.Settings.DefaultAssemblerOption);
            Assert.Equal(ModuleSelector.Style.Productivity, window.Settings.DefaultModuleOption);
            Assert.True(window.Settings.EnableExtraProductivityForNonMiners);
            Assert.Equal(NodeDirection.Down, window.Settings.DefaultNodeDirection);
        }
    }
}
