using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
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
    //Covers the failed Open/Import/export alerts and the Load-bookkeeping contract.
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

        //---- The alerts the real dialogs raise: ruled copy, rendered, resolved by their keys ------------

        private static AlertSheet ShownAlert(MainWindow window) {
            Dispatcher.UIThread.RunJobs();
            return Assert.IsType<AlertSheet>(SheetHost.For(window).GetVisualDescendants().OfType<AlertSheet>().Last());
        }

        private static string[] Rendered(AlertSheet alert) => [
            alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Headline").Text ?? "",
            alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Informative").Text ?? "",
            .. alert.Buttons!.Items.Cast<Button>().Select(b => b.Content as string ?? ""),
        ];

        [AvaloniaFact]
        public async Task FailedOpenImportAndExport_RaiseOKAlerts_WithTheRuledCopy() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();

            Task opened = window.LoadGraphJsonAsync("{}", "Iron smelting");
            AlertSheet open = ShownAlert(window);
            Assert.Equal(["Couldn’t open “Iron smelting”.",
                "The file is too old or damaged to read. Open it in the previous Foreman release, save it again, then open the new copy here.", "OK"], Rendered(open));
            CanvasKeys.KeyDownOn(open.YesButtonControl, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(opened.IsCompleted);
            await opened;
            AnimationClock.Settle();

            Task imported = window.ImportGraphJsonAsync("{}", "/tmp/graphs/Copper wire.fjson");
            AlertSheet import = ShownAlert(window);
            Assert.Equal(["Couldn’t import “Copper wire”.", "Foreman couldn’t read it as a graph. The details are in Foreman’s log.", "OK"], Rendered(import));
            CanvasKeys.KeyDownOn(import.YesButtonControl, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.True(imported.IsCompleted);
            await imported;
            AnimationClock.Settle();

            Task<bool> saved = window.ExportGraphAsync(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "Green circuits.fjson"));
            AlertSheet save = ShownAlert(window);
            Assert.Equal(["Couldn’t save “Green circuits”.", "Your graph is still open and unchanged. The details are in Foreman’s log.", "OK"], Rendered(save));
            CanvasKeys.KeyDownOn(save.YesButtonControl, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(saved.IsCompleted);
            Assert.False(await saved);
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

        //Ports the setEnablesFromJson gate (upstream ProductionGraphViewer.cs:1289): a
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

            window.ApplyLoadedGraphUiState("Loaded graph");

            Assert.Equal("Loaded graph", window.ViewModel.DocumentTitle);
            Assert.Equal(AssemblerSelector.Style.Best, window.Settings.DefaultAssemblerOption);
            Assert.Equal(ModuleSelector.Style.Productivity, window.Settings.DefaultModuleOption);
            Assert.True(window.Settings.EnableExtraProductivityForNonMiners);
            Assert.Equal(NodeDirection.Down, window.Settings.DefaultNodeDirection);
        }
    }
}
