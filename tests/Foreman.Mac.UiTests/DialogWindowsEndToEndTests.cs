using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models;
using Foreman.Models.Nodes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Task 6's phase gate (docs/superpowers/plans/2026-09-02-phase5b-dialog-windows.md): the three dialog
    //windows (Settings/GraphSummary/PresetComparator, tasks 1-5) are each review-clean on their own; this
    //covers the seams between them and the real MainWindow - the Confirm/Cancel cascade, a preset switch
    //with a live graph, a summary reopened after an edit, Compare launched from Settings, and repeated
    //open/close cycles - rather than re-testing any window in isolation.
    public class DialogWindowsEndToEndTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string SpaceAgePresetName = "Factorio 2.0 Space Age";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedVanillaCache;
        private static DataCache? sharedSpaceAgeCache;

        private static async Task<DataCache> GetSharedVanillaCacheAsync() {
            if (sharedVanillaCache is not null)
                return sharedVanillaCache;
            await CacheGate.WaitAsync();
            try {
                sharedVanillaCache ??= await LoadCacheAsync(VanillaPresetName);
            } finally {
                CacheGate.Release();
            }
            return sharedVanillaCache;
        }

        private static async Task<DataCache> GetSharedSpaceAgeCacheAsync() {
            if (sharedSpaceAgeCache is not null)
                return sharedSpaceAgeCache;
            await CacheGate.WaitAsync();
            try {
                sharedSpaceAgeCache ??= await LoadCacheAsync(SpaceAgePresetName);
            } finally {
                CacheGate.Release();
            }
            return sharedSpaceAgeCache;
        }

        private static async Task<DataCache> LoadCacheAsync(string presetName) {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(presetName, true, true), new Progress<KeyValuePair<int, string>>());
            return cache;
        }

        private static string NewTempHome() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(home);
            return home;
        }

        //Isolated PresetResolver by default (a fresh temp dir has no Presets folder, so BuildPresetList just
        //returns the current preset alone) - seams that need a real preset switch pass presetsOverride: null
        //to fall through to the real bundled Presets directory instead.
        private static MainWindow NewReadyWindow(DataCache cache, AppSettings settings, string tempHome, string? presetsOverride = "") {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.Viewport.SetSize(800, 800);
            window.DataCache = cache;
            window.Settings = settings;
            window.PresetResolver = new PresetResolver(presetsOverride == "" ? tempHome : presetsOverride);
            window.SettingsService = new SettingsService(tempHome);
            window.ApplyLoadedSettings();
            return window;
        }

        private static string SettingsJsonPath(string tempHome) =>
            Path.Combine(tempHome, "Library", "Application Support", "Foreman", "settings.json");

        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000) {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
                await Task.Delay(10);
        }

        private static Task<RecipeNodeElement> AddRecipeViaChooser(GraphCanvasControl control, DataCache cache, string recipeName, Point location) {
            IRecipe recipe = cache.Recipes[recipeName];
            int before = control.NodeElements.Count;
            control.AddRecipeAsync(location);
            QuickAddPalette palette = Assert.IsType<QuickAddPalette>(control.OpenPalette);
            ((QuickAddViewModel)palette.DataContext!).Query = recipe.FriendlyName;
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);
            control.ClosePanels();
            Assert.Equal(before + 1, control.NodeElements.Count);
            return Task.FromResult((RecipeNodeElement)control.NodeElements[^1]);
        }

        private static void SetManualRate(GraphCanvasControl control, NodeId nodeId, double value) {
            if (control.Viewer.Session.Editor.RequestNodeController(nodeId) is not BaseNodeController controller)
                throw new InvalidOperationException("Node controller not found for " + nodeId);
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(value);
        }

        //================================================================================================
        // Seam 1: settings cascade end-to-end (Confirm applies + persists; Cancel discards).
        //================================================================================================

        [AvaloniaFact]
        public async Task SettingsLiveApply_GraphOptionEditsAndRecipeToggle_PersistToDiskAndSolveOnceAfterFlush() {
            DataCache cache = await LoadCacheAsync(VanillaPresetName); //dedicated cache: this test disables a recipe permanently
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            IRecipe ironPlateRecipe = cache.Recipes["iron-plate"];
            Assert.True(ironPlateRecipe.Enabled); //sanity: starts enabled, so the toggle below is a real flip

            SettingsWindow? settingsWindow = null;
            window.SettingsDialogStub = w => settingsWindow = w;
            window.ShowSettings();

            settingsWindow!.AdvancedTabControl.LowPriorityPowerStepperControl.Value = 5.5;
            settingsWindow.GeneralTabControl.QualityStepsStepperControl.Value = 9;

            var recipeItem = settingsWindow.ViewModel.RecipeObjects.First(i => i.DataObject == ironPlateRecipe);
            recipeItem.IsChecked = false;

            await window.CurrentSettingsSession!.FlushAsync();

            Assert.Equal(1, solveCount);
            Assert.Equal(5.5m, settings.LowPriorityPower);
            Assert.Equal(9, settings.QualitySteps);
            Assert.Equal(5.5d, window.GraphCanvas.Viewer.Graph.LowPriorityPower);
            Assert.Equal(9u, window.GraphCanvas.Viewer.Graph.MaxQualitySteps);
            Assert.False(cache.Recipes["iron-plate"].Enabled);

            AppSettings persisted = new SettingsService(tempHome).Load();
            Assert.Equal(5.5m, persisted.LowPriorityPower);
            Assert.Equal(9, persisted.QualitySteps);
        }

        [AvaloniaFact]
        public async Task SettingsWindow_OpenedAndClosedWithoutChanges_LeavesSettingsCacheAndSolveCountUnchanged() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName, LowPriorityPower = 4m };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;
            IRecipe ironPlateRecipe = cache.Recipes["iron-plate"];
            bool enabledBefore = ironPlateRecipe.Enabled;

            SettingsWindow? settingsWindow = null;
            window.SettingsDialogStub = w => settingsWindow = w;
            window.ShowSettings();
            settingsWindow!.Close();
            await Task.CompletedTask;

            Assert.Equal(0, solveCount);
            Assert.Equal(4m, settings.LowPriorityPower);
            Assert.Equal(enabledBefore, cache.Recipes["iron-plate"].Enabled);
            Assert.False(File.Exists(SettingsJsonPath(tempHome)));
        }

        [AvaloniaFact]
        public async Task SettingsLiveApply_LoadFromSave_PersistsLastSaveFileLocationImmediately() {
            DataCache cache = await LoadCacheAsync(VanillaPresetName);
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            SettingsWindow? settingsWindow = null;
            window.SettingsDialogStub = w => settingsWindow = w;
            window.ShowSettings();

            settingsWindow!.LoadFromSaveDialogStub = w => {
                w.OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip");
                w.LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = new SaveFileInfo() };
                w.ConfirmDialogStub = (_, _) => Task.FromResult(true); //mods mismatch is expected here - the stub result carries none.
                return w.RunAsync();
            };

            await settingsWindow.SimulateLoadFromSaveClickAsync().ConfigureAwait(true);

            AppSettings persisted = new SettingsService(tempHome).Load();
            Assert.Equal(Path.GetDirectoryName("/tmp/saves/mysave.zip"), persisted.LastSaveFileLocation);
        }

        //================================================================================================
        // Seam 2: preset-switch cascade with an existing graph (Task 1 remap x Task 2 pre-population).
        //================================================================================================

        [AvaloniaFact]
        public async Task PresetSwitchCascade_NodeSurvivesRemap_SettingsPersist_EnabledObjectsComeFromNewCache() {
            DataCache vanilla = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(vanilla, settings, tempHome, presetsOverride: null); //real Presets dir: need a genuine second preset to switch to

            RecipeNodeElement recipeNode = await AddRecipeViaChooser(window.GraphCanvas, vanilla, "iron-plate", new Point(0, 0));
            Assert.Single(window.GraphCanvas.Viewer.Session.View.Nodes);

            SettingsWindow? settingsWindow = null;
            window.SettingsDialogStub = w => settingsWindow = w;
            window.ShowSettings();

            Preset spaceAge = settingsWindow!.Options.Presets!.First(p => p.Name == SpaceAgePresetName);
            settingsWindow.SimulateSelectPresetMenuItemClick(spaceAge);

            await WaitUntilAsync(() => !ReferenceEquals(window.DataCache, vanilla));

            Assert.Equal(SpaceAgePresetName, settings.CurrentPresetName);
            Assert.NotSame(vanilla, window.DataCache);
            List<INodeViewModel> remappedNodes = [.. window.GraphCanvas.Viewer.Session.View.Nodes];
            Assert.Single(remappedNodes); //iron-plate exists in both presets, so the node survives the reload instead of being dropped

            AppSettings persisted = new SettingsService(tempHome).Load();
            Assert.Equal(SpaceAgePresetName, persisted.CurrentPresetName);

            //DataObjectBasePrototype.Equals compares by (Type, Name), so a plain Contains can't tell the new
            //cache's "iron-plate" recipe apart from the old one - only reference identity can.
            IRecipe ironPlateInNewCache = window.DataCache!.Recipes["iron-plate"];
            var recipeDataObjects = settingsWindow.ViewModel.RecipeObjects.Select(i => i.DataObject).ToList();
            Assert.Contains(recipeDataObjects, o => ReferenceEquals(o, ironPlateInNewCache));
            Assert.DoesNotContain(recipeDataObjects, o => ReferenceEquals(o, vanilla.Recipes["iron-plate"]));
        }

        //Phase 6 Task 8: ReloadForPresetAsync now serializes through GraphViewerSaveAssembler.BuildSaveDocument
        //instead of a bare ProductionGraphSaveDocument wrapper, so annotations and the viewport round-trip
        //across a preset switch same as a real save/load (docs/upstream-divergences.md, the entry this test
        //closes out).
        [AvaloniaFact]
        public async Task PresetSwitchCascade_AnnotationAndViewportSurviveRemap() {
            DataCache vanilla = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(vanilla, settings, tempHome, presetsOverride: null); //real Presets dir: need a genuine second preset to switch to

            //A node keeps Graph.Bounds non-empty, so the post-load UpdateGraphBounds(Graph.Bounds) fit
            //doesn't clamp the offset below back to (0,0) against an empty-graph rectangle - orthogonal to
            //what this test is actually checking (the viewport/annotation round trip itself).
            await AddRecipeViaChooser(window.GraphCanvas, vanilla, "iron-plate", new Point(0, 0));
            window.GraphCanvas.Viewer.AddAnnotationElement(new TextAnnotationElement(new Point(40, 60)) { Text = "Survives the switch" });
            window.GraphCanvas.Viewport.ViewOffset = new Point(20, 30);
            window.GraphCanvas.Viewport.ViewScale = 2.5f;

            SettingsWindow? settingsWindow = null;
            window.SettingsDialogStub = w => settingsWindow = w;
            window.ShowSettings();

            Preset spaceAge = settingsWindow!.Options.Presets!.First(p => p.Name == SpaceAgePresetName);
            settingsWindow.SimulateSelectPresetMenuItemClick(spaceAge);

            await WaitUntilAsync(() => !ReferenceEquals(window.DataCache, vanilla));

            Assert.Equal(SpaceAgePresetName, settings.CurrentPresetName);
            TextAnnotationElement annotation = Assert.IsType<TextAnnotationElement>(Assert.Single(window.GraphCanvas.Viewer.Annotations));
            Assert.Equal("Survives the switch", annotation.Text);
            Assert.Equal(new Point(40, 60), annotation.Location);
            Assert.Equal(new Point(20, 30), window.GraphCanvas.Viewport.ViewOffset);
            Assert.Equal(2.5f, window.GraphCanvas.Viewport.ViewScale);
        }

        //Final fix wave I2: MainWindow.axaml.cs's ReloadForPresetAsync call passes setEnablesFromJson: false
        //deliberately (see its own comment) so the freshly booted cache's own default-enabled state survives
        //a preset switch, instead of Vanilla's serialized enabled-recipe NAMES zeroing out anything in Space
        //Age that doesn't share one. A recipe that exists only in Space Age (never in Vanilla's Recipes
        //dictionary at all, so it can never appear in the reload document's EnabledRecipes list either) makes
        //that distinction unmissable - flipping the literal to true zeroes it, since ApplyEnabledList resets
        //every recipe to disabled first and only re-enables names it finds in that list.
        [AvaloniaFact]
        public async Task PresetSwitchCascade_SpaceAgeExclusiveRecipe_KeepsItsOwnDefaultEnabledState() {
            DataCache vanilla = await GetSharedVanillaCacheAsync();
            DataCache spaceAge = await GetSharedSpaceAgeCacheAsync();
            string spaceAgeExclusiveEnabledRecipeName = spaceAge.Recipes.Values
                .First(r => r.Enabled && !vanilla.Recipes.ContainsKey(r.Name)).Name;

            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(vanilla, settings, tempHome, presetsOverride: null); //real Presets dir: need a genuine second preset to switch to

            SettingsWindow? settingsWindow = null;
            window.SettingsDialogStub = w => settingsWindow = w;
            window.ShowSettings();

            Preset spaceAgePreset = settingsWindow!.Options.Presets!.First(p => p.Name == SpaceAgePresetName);
            settingsWindow.SimulateSelectPresetMenuItemClick(spaceAgePreset);

            await WaitUntilAsync(() => !ReferenceEquals(window.DataCache, vanilla));

            Assert.Equal(SpaceAgePresetName, settings.CurrentPresetName);
            Assert.True(window.DataCache!.Recipes[spaceAgeExclusiveEnabledRecipeName].Enabled);
        }

        //================================================================================================
        // Seam 3: summary over a live, edited graph - totals against hand-derived numbers, no stale cache
        // on reopen. Two independent manual-rate supplier nodes keep the hand-derived numbers trivial: each
        // item's Input total is exactly the fixed rate that node was set to, no solver/recipe math involved.
        //================================================================================================

        [AvaloniaFact]
        public async Task GraphSummary_OverLiveEditedGraph_TotalsMatchHandDerivedValues() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            IItem ironPlate = cache.Items["iron-plate"];
            IQuality quality = cache.DefaultQuality!;

            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(ironPlate, quality), new Point(0, 0));
            SetManualRate(window.GraphCanvas, supplierId, 12.5); //hand-derived: Input = 12.5, the fixed rate, no other node feeds or drains it
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            window.ShowGraphSummary();
            GraphSummaryWindow summary = window.CurrentGraphSummaryWindow!;

            Assert.Equal("#Buildings: 0", summary.BuildingCountText); //no recipe/assembler nodes yet
            ItemRow ironRowFirst = Assert.Single(summary.ViewModel.Items, r => ((ItemQualityPair)r.Tag).Item == ironPlate);
            Assert.Equal(12.5, ironRowFirst.InValue, 3);

            summary.Close();
        }

        [AvaloniaFact]
        public async Task RateCells_FollowTheGraphRateUnit_ThroughTheHostTheAppBuilds() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            MainWindow window = NewReadyWindow(cache, new AppSettings { CurrentPresetName = VanillaPresetName }, NewTempHome());
            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!), new Point(0, 0));
            SetManualRate(window.GraphCanvas, supplierId, 12.5);
            window.GraphCanvas.Viewer.Graph.SelectedRateUnit = ProductionGraph.RateUnit.Per1Min;
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            window.ShowGraphSummary();
            GraphSummaryWindow summary = window.CurrentGraphSummaryWindow!;
            summary.SelectSegment(1);
            Dispatcher.UIThread.RunJobs();
            summary.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            SummaryRow row = summary.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Plate", StringComparison.OrdinalIgnoreCase));
            Assert.NotEqual(0, row.Values[1]);
            Assert.Equal(NodeCardPainter.Rate(row.Values[1], ProductionGraph.RateUnit.Per1Min), row.Cells[1]);
            Assert.EndsWith("/min", row.Cells[1]);
            ListBoxItem container = summary.TableControl.GetVisualDescendants().OfType<ListBoxItem>().Single(c => ReferenceEquals(c.Content, row));
            Assert.Contains($"{NodeCardPainter.Number(row.Values[1])} per minute", Avalonia.Automation.AutomationProperties.GetName(container));

            summary.Close();
        }

        //Phase5b hands-on gate (Finding 3): the human reported a solved factory summing to 0 buildings.
        //Every prior summary test either has zero recipe nodes or pins the recipe node's own rate by hand -
        //neither exercises what MainWindow hands GraphSummaryWindow for an auto-solved recipe node reached
        //through the real chooser flow (AddRecipeViaChooser), which is what "a solved graph with real recipe
        //nodes" actually looks like in the live app (mirrors CoreEndToEndTests' iron-gear-wheel fixture: a
        //supplier/consumer pair pins the demand, the recipe node's own rate is left on Auto so the LP
        //solver derives it). This closes that gap; it passes on the current code, so it stands as the
        //regression guard rather than evidence of a live-only defect (see docs/upstream-divergences.md).
        [AvaloniaFact]
        public async Task GraphSummary_OverLiveAutoSolvedRecipeNode_ShowsNonZeroBuildingCount() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            IItem inputItem = cache.Items["iron-plate"];
            IItem outputItem = cache.Items["iron-gear-wheel"];
            IQuality quality = cache.DefaultQuality!;
            var inputPair = new ItemQualityPair(inputItem, quality);
            var outputPair = new ItemQualityPair(outputItem, quality);

            IProductionGraphEditor editor = window.GraphCanvas.Viewer.Session.Editor;
            NodeId supplierId = editor.CreateSupplierNode(inputPair, new Point(0, 0));
            RecipeNodeElement recipeElement = await AddRecipeViaChooser(window.GraphCanvas, cache, "iron-gear-wheel", new Point(200, 0));
            NodeId recipeId = recipeElement.ViewModel.Id;
            NodeId consumerId = editor.CreateConsumerNode(outputPair, new Point(400, 0));
            editor.CreateLink(supplierId, recipeId, inputPair);
            editor.CreateLink(recipeId, consumerId, outputPair);

            SetManualRate(window.GraphCanvas, consumerId, 10); //forces demand; the recipe node's own rate stays Auto so the LP solver derives ActualSetValue, same as CoreEndToEndTests
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            window.ShowGraphSummary();
            GraphSummaryWindow summary = window.CurrentGraphSummaryWindow!;

            Assert.NotEqual("#Buildings: 0", summary.BuildingCountText);
            BuildingRow assemblerRow = Assert.Single(summary.ViewModel.Assemblers);
            Assert.NotEqual("0", assemblerRow.CountText);
            summary.Close();
        }

        //================================================================================================
        // Seam 4: comparator launched from Settings, over the real fixture presets; closing both restores
        // canvas interactivity (the FloatingPanelHost focus-restore contract, extended to modal windows).
        //================================================================================================

        [AvaloniaFact]
        public async Task ComparatorFromSettings_OverRealFixturePresets_ClosingBothRestoresCanvasShortcuts() {
            DataCache vanilla = await GetSharedVanillaCacheAsync();
            //Dedicated caches, not the shared fixtures: PresetComparatorWindow.Close() clears whatever it
            //was handed (LeftCache?.Clear()/RightCache?.Clear(), matching upstream's own disposal), which
            //would otherwise wreck every other test still relying on the shared Vanilla/SpaceAge caches.
            DataCache comparatorVanilla = await LoadCacheAsync(VanillaPresetName);
            DataCache comparatorSpaceAge = await LoadCacheAsync(SpaceAgePresetName);
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(vanilla, settings, tempHome, presetsOverride: null); //real Presets dir: need the real >=2-preset guard to pass

            window.GraphCanvas.Focus();
            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            CheckBox gridlinesCheckbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GridlinesCheckbox");
            window.GraphCanvas.Grid.ShowGrid = false;
            gridlinesCheckbox.IsChecked = false;

            SettingsWindow? settingsWindow = null;
            window.SettingsDialogStub = w => settingsWindow = w;
            window.ShowSettings();

            bool comparatorPopulated = false;
            settingsWindow!.ComparePresetsDialogStub = async presets => {
                var comparator = new PresetComparatorWindow(presets) {
                    LoadCacheStub = preset => Task.FromResult(new DataLoadResult(preset.Name == VanillaPresetName ? comparatorVanilla : preset.Name == SpaceAgePresetName ? comparatorSpaceAge : null, DataLoadOutcome.Loaded)),
                };
                await comparator.SimulateCompareClickAsync().ConfigureAwait(true);
                comparatorPopulated = comparator.LeftCache is not null && comparator.RightCache is not null;
                comparator.Close();
            };
            await settingsWindow.SimulateComparePresetsClickAsync().ConfigureAwait(true);
            settingsWindow.Close();

            Assert.True(comparatorPopulated);

            //No explicit re-focus here on purpose: a real user's next keystroke after closing both modals
            //should already reach the canvas.
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(window.GraphCanvas.Grid.ShowGrid);
            Assert.True(gridlinesCheckbox.IsChecked);
        }

        //================================================================================================
        // Seam 5: open/close cycles leak check - repeat each window 3x through the real commands, then
        // confirm one real trigger still produces exactly one solve (Task 3's bind-once lesson).
        //================================================================================================

        [AvaloniaFact]
        public async Task OpenCloseCycles_ThreeTimesEach_LeaveExactlyOneSolvePerSubsequentTrigger() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome, presetsOverride: null); //real Presets dir: the comparator cycle below needs the real >=2-preset guard to pass

            for (int i = 0; i < 3; i++) {
                window.SettingsDialogStub = w => w.Close();
                window.ShowSettings();
            }

            for (int i = 0; i < 3; i++) {
                window.ShowGraphSummary();
                window.CurrentGraphSummaryWindow!.Close();
            }

            for (int i = 0; i < 3; i++) {
                window.SettingsDialogStub = w => {
                    w.ComparePresetsDialogStub = presets => {
                        //Never started (SimulateProcessPresetsClickAsync isn't called), so PresetComparatorWindow's
                        //own Closed handler no-ops (comparing stays false) rather than clearing a cache this test
                        //never loaded.
                        new PresetComparatorWindow(presets).Close();
                        return Task.CompletedTask;
                    };
                    Async.Fire(w.SimulateComparePresetsClickAsync(), "SimulateComparePresetsClickAsync");
                    w.Close();
                };
                window.ShowSettings();
            }

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var rateOptions = window.GetVisualDescendants().OfType<PopUp>().Single(c => c.Name == "RateOptionsDropDown");
            string nextRateUnit = MainWindow.RateUnitOptions[(Array.IndexOf(MainWindow.RateUnitOptions, rateOptions.SelectedItem) + 1) % MainWindow.RateUnitOptions.Length];
            rateOptions.SelectedItem = nextRateUnit;
            rateOptions.Command?.Execute(nextRateUnit);

            Assert.Equal(1, solveCount);
        }

        //================================================================================================
        // Seam 6: Imp#3 (final fix wave) - RealShowSettingsDialogAsync/RealShowGraphSummaryAsync's own
        // GraphCanvas.Focus() calls, driven through a genuine child Window.ShowDialog rather than the
        // SettingsDialogStub/GraphSummaryDialogStub every other seam above uses (a stub never reaches
        // either real method, so it can't exercise this focus-restore at all). Minor#5 rides along: the
        // same two call sites must not steal focus from an already-open floating panel.
        //================================================================================================

        [AvaloniaFact]
        public async Task RealShowSettings_Closing_RestoresGraphCanvasFocus() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            Button newGraphButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewGraphButton");
            newGraphButton.Focus();
            Assert.True(newGraphButton.IsFocused);

            window.ShowSettings();
            SettingsWindow settingsWindow = window.OwnedWindows.OfType<SettingsWindow>().Single();
            settingsWindow.Close();

            Assert.True(window.GraphCanvas.IsFocused);
        }

        [AvaloniaFact]
        public async Task ShowGraphSummary_Closing_RestoresGraphCanvasFocus() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            Button newGraphButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NewGraphButton");
            newGraphButton.Focus();
            Assert.True(newGraphButton.IsFocused);

            window.ShowGraphSummary();
            window.CurrentGraphSummaryWindow!.Close();

            Assert.True(window.GraphCanvas.IsFocused);
        }

        //Phase 7 deferred-minors sweep (toolbar-staleness fix, widened scope): OpenSettingsAsync now closes
        //any open floating panel before it opens itself - a toolbar/menu click never reaches
        //GraphCanvasControl.OnPointerPressed's own click-outside-closes-panel guard, so a panel left open
        //under Settings used to stay stale once the dialog closed. Supersedes this test's own prior
        //assertion (the dialog's open-panel focus-restore guard skipping GraphCanvas.Focus() while a panel
        //was still open) - the panel is gone by the time that guard runs now, so focus always restores.
        [AvaloniaFact]
        public async Task RealShowSettings_OpenedWhilePanelOpen_ClosesPanelAndRestoresCanvasFocusOnClose() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);
            window.GraphCanvas.AddRecipeAsync(new Point(0, 0));
            Assert.True(window.GraphCanvas.IsPanelOpen);

            window.ShowSettings();

            Assert.False(window.GraphCanvas.IsPanelOpen);

            SettingsWindow settingsWindow = window.OwnedWindows.OfType<SettingsWindow>().Single();
            settingsWindow.Close();

            Assert.True(window.GraphCanvas.IsFocused);
        }
    }
}
