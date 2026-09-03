using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Models;
using Foreman.Models.Nodes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using InputPointer = Avalonia.Input.Pointer;

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

        //---- shared chooser-flow helper (EditingEndToEndTests' own copy, adapted to a caller-supplied
        //control rather than one it built itself) ----

        private static void Click(Avalonia.Controls.Control control) {
            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            var args = new PointerReleasedEventArgs(control, pointer, control, default, 0, properties, KeyModifiers.None, MouseButton.Left);
            control.RaiseEvent(args);
        }

        private static Task<RecipeNodeElement> AddRecipeViaChooser(GraphCanvasControl control, DataCache cache, string recipeName, Point location) {
            IRecipe recipe = cache.Recipes[recipeName];
            int before = control.NodeElements.Count;
            control.AddRecipeAsync(location);
            var panel = Assert.IsType<RecipeChooserPanel>(control.FloatingPanelHost.Content);

            IconButton? cell = panel.GetVisualDescendants().OfType<IconButton>().FirstOrDefault(b => Equals(b.DataObject, recipe));
            if (cell is null && recipe.MySubgroup.MyGroup is IGroup targetGroup) {
                IconButton groupButton = panel.GetVisualDescendants().OfType<IconButton>().First(b => Equals(b.DataObject, targetGroup));
                Click(groupButton);
                cell = panel.GetVisualDescendants().OfType<IconButton>().First(b => Equals(b.DataObject, recipe));
            }
            Click(cell!);
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
        public async Task SettingsCascade_ConfirmWithGraphOptionEditAndRecipeToggle_PersistsToDiskCacheAndSolvesOnce() {
            DataCache cache = await LoadCacheAsync(VanillaPresetName); //dedicated cache: this test disables a recipe permanently
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            IRecipe ironPlateRecipe = cache.Recipes["iron-plate"];
            Assert.True(ironPlateRecipe.Enabled); //sanity: starts enabled, so the toggle below is a real flip

            window.SettingsDialogStub = options => {
                var settingsWindow = new SettingsWindow(options);
                settingsWindow.LowPriorityPowerInputControl.Value = 5.5m;
                settingsWindow.QualityStepsInputControl.Value = 9m;

                var recipeItem = settingsWindow.RecipeListViewControl.ItemsSource!
                    .Cast<EnabledObjectsListItem>().First(i => i.DataObject == ironPlateRecipe);
                recipeItem.IsChecked = false;

                settingsWindow.SimulateConfirmClick();
                return Task.FromResult<bool?>(settingsWindow.DialogResultValue);
            };

            await window.OpenSettingsAsync();

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
        public async Task SettingsCascade_Cancel_LeavesSettingsCacheAndSolveCountUnchanged() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName, LowPriorityPower = 4m };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;
            IRecipe ironPlateRecipe = cache.Recipes["iron-plate"];
            bool enabledBefore = ironPlateRecipe.Enabled;

            window.SettingsDialogStub = options => {
                var settingsWindow = new SettingsWindow(options);
                settingsWindow.LowPriorityPowerInputControl.Value = 5.5m;
                var recipeItem = settingsWindow.RecipeListViewControl.ItemsSource!
                    .Cast<EnabledObjectsListItem>().First(i => i.DataObject == ironPlateRecipe);
                recipeItem.IsChecked = !recipeItem.IsChecked;

                settingsWindow.SimulateCancelClick();
                return Task.FromResult<bool?>(settingsWindow.DialogResultValue);
            };

            await window.OpenSettingsAsync();

            Assert.Equal(0, solveCount);
            Assert.Equal(4m, settings.LowPriorityPower);
            Assert.Equal(enabledBefore, cache.Recipes["iron-plate"].Enabled);
            Assert.False(File.Exists(SettingsJsonPath(tempHome)));
        }

        //Regression (C1): "Load from save" writes LastSaveFileLocation onto AppSettings through its own
        //SettingsService as soon as it succeeds, but OpenSettingsAsync's own tail then unconditionally saves
        //ITS settings object over that - reverting the value on the very Confirm that's supposed to finish
        //the feature. Options.LastSaveFileLocation is the seam that carries the value back to that tail.
        [AvaloniaFact]
        public async Task SettingsCascade_LoadFromSaveThenConfirm_PersistsLastSaveFileLocation() {
            DataCache cache = await LoadCacheAsync(VanillaPresetName); //dedicated cache: the empty stub SaveFileInfo disables every non-pseudo recipe on Confirm
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            window.SettingsDialogStub = async options => {
                var settingsWindow = new SettingsWindow(options) { SettingsService = new SettingsService(tempHome) };
                settingsWindow.LoadFromSaveDialogStub = w => {
                    w.OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip");
                    w.LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = new SaveFileInfo() };
                    w.ConfirmDialogStub = (_, _) => Task.FromResult(true); //mods mismatch is expected here - the stub result carries none.
                    return w.RunAsync();
                };

                await settingsWindow.SimulateLoadFromSaveClickAsync().ConfigureAwait(true);
                settingsWindow.SimulateConfirmClick();
                return settingsWindow.DialogResultValue;
            };

            await window.OpenSettingsAsync();

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
            NodeId originalId = recipeNode.ViewModel.Id;

            window.SettingsDialogStub = options => {
                Preset spaceAge = options.Presets!.First(p => p.Name == SpaceAgePresetName);
                options.SelectedPreset = spaceAge;
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.Equal(SpaceAgePresetName, settings.CurrentPresetName);
            Assert.NotSame(vanilla, window.DataCache);
            List<INodeViewModel> remappedNodes = [.. window.GraphCanvas.Viewer.Session.View.Nodes];
            Assert.Single(remappedNodes); //iron-plate exists in both presets, so the node survives the reload instead of being dropped

            AppSettings persisted = new SettingsService(tempHome).Load();
            Assert.Equal(SpaceAgePresetName, persisted.CurrentPresetName);

            SettingsWindow.SettingsWindowOptions? reopenedOptions = null;
            window.SettingsDialogStub = options => {
                reopenedOptions = options;
                return Task.FromResult<bool?>(false);
            };
            await window.OpenSettingsAsync();

            //DataObjectBasePrototype.Equals compares by (Type, Name), so a plain Contains can't tell the new
            //cache's "iron-plate" recipe apart from the old one - only reference identity can.
            IRecipe ironPlateInNewCache = window.DataCache!.Recipes["iron-plate"];
            Assert.Contains(reopenedOptions!.EnabledObjects, o => ReferenceEquals(o, ironPlateInNewCache));
            Assert.DoesNotContain(reopenedOptions.EnabledObjects, o => ReferenceEquals(o, vanilla.Recipes["iron-plate"]));
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

            window.SettingsDialogStub = options => {
                Preset spaceAge = options.Presets!.First(p => p.Name == SpaceAgePresetName);
                options.SelectedPreset = spaceAge;
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

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

            window.SettingsDialogStub = options => {
                Preset spaceAgePreset = options.Presets!.First(p => p.Name == SpaceAgePresetName);
                options.SelectedPreset = spaceAgePreset;
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.Equal(SpaceAgePresetName, settings.CurrentPresetName);
            Assert.True(window.DataCache!.Recipes[spaceAgeExclusiveEnabledRecipeName].Enabled);
        }

        //================================================================================================
        // Seam 3: summary over a live, edited graph - totals against hand-derived numbers, no stale cache
        // on reopen. Two independent manual-rate supplier nodes keep the hand-derived numbers trivial: each
        // item's Input total is exactly the fixed rate that node was set to, no solver/recipe math involved.
        //================================================================================================

        [AvaloniaFact]
        public async Task GraphSummary_OverLiveEditedGraph_TotalsMatchHandDerivedValues_UpdateAfterEdit() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            IItem ironPlate = cache.Items["iron-plate"];
            IItem copperPlate = cache.Items["copper-plate"];
            IQuality quality = cache.DefaultQuality!;

            NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(ironPlate, quality), new Point(0, 0));
            SetManualRate(window.GraphCanvas, supplierId, 12.5); //hand-derived: Input = 12.5, the fixed rate, no other node feeds or drains it
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            GraphSummaryWindow? firstOpen = null;
            window.GraphSummaryDialogStub = session => firstOpen = new GraphSummaryWindow(session, window.GraphCanvas.Viewer.Graph.GetRateName());
            await window.OpenGraphSummaryAsync();

            Assert.Equal("#Buildings: 0", firstOpen!.BuildingCountLabelControl.Text); //no recipe/assembler nodes yet
            GraphSummaryWindow.ItemRow ironRowFirst = Assert.Single(firstOpen.UnfilteredItemsList, r => ((ItemQualityPair)r.Tag).Item == ironPlate);
            Assert.Equal(12.5, ironRowFirst.InValue, 3);
            firstOpen.Close();

            NodeId supplier2Id = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(copperPlate, quality), new Point(200, 0));
            SetManualRate(window.GraphCanvas, supplier2Id, 7); //hand-derived: Input = 7 for the newly added item
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            GraphSummaryWindow? secondOpen = null;
            window.GraphSummaryDialogStub = session => secondOpen = new GraphSummaryWindow(session, window.GraphCanvas.Viewer.Graph.GetRateName());
            await window.OpenGraphSummaryAsync();

            Assert.Equal(2, secondOpen!.UnfilteredItemsList.Count); //reopen picks up the edit rather than reusing a stale snapshot
            GraphSummaryWindow.ItemRow ironRowSecond = Assert.Single(secondOpen.UnfilteredItemsList, r => ((ItemQualityPair)r.Tag).Item == ironPlate);
            Assert.Equal(12.5, ironRowSecond.InValue, 3);
            GraphSummaryWindow.ItemRow copperRow = Assert.Single(secondOpen.UnfilteredItemsList, r => ((ItemQualityPair)r.Tag).Item == copperPlate);
            Assert.Equal(7, copperRow.InValue, 3);
            secondOpen.Close();
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

            GraphSummaryWindow? summary = null;
            window.GraphSummaryDialogStub = session => summary = new GraphSummaryWindow(session, window.GraphCanvas.Viewer.Graph.GetRateName());
            await window.OpenGraphSummaryAsync();

            Assert.NotEqual("#Buildings: 0", summary!.BuildingCountLabelControl.Text);
            GraphSummaryWindow.BuildingRow assemblerRow = Assert.Single(summary.UnfilteredAssemblerList);
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
            CheckBox gridlinesCheckbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GridlinesCheckbox");
            window.GraphCanvas.Grid.ShowGrid = false;
            gridlinesCheckbox.IsChecked = false;

            bool comparatorPopulated = false;
            window.SettingsDialogStub = async options => {
                var settingsWindow = new SettingsWindow(options);
                settingsWindow.ComparePresetsDialogStub = async presets => {
                    var comparator = new PresetComparatorWindow(presets) {
                        LoadCacheStub = preset => Task.FromResult<DataCache?>(preset.Name == VanillaPresetName ? comparatorVanilla : preset.Name == SpaceAgePresetName ? comparatorSpaceAge : null),
                    };
                    await comparator.SimulateProcessPresetsClickAsync().ConfigureAwait(true);
                    comparatorPopulated = comparator.LeftCache is not null && comparator.RightCache is not null;
                    comparator.Close();
                };
                await settingsWindow.SimulateComparePresetsClickAsync().ConfigureAwait(true);
                settingsWindow.SimulateCancelClick();
                return settingsWindow.DialogResultValue;
            };

            await window.OpenSettingsAsync();

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
                window.SettingsDialogStub = options => {
                    var settingsWindow = new SettingsWindow(options);
                    settingsWindow.SimulateConfirmClick(); //Confirm (not Cancel): exercises MainWindow's rebind-guarded ApplyLoadedSettings path each cycle
                    return Task.FromResult<bool?>(settingsWindow.DialogResultValue);
                };
                await window.OpenSettingsAsync();
            }

            for (int i = 0; i < 3; i++) {
                window.GraphSummaryDialogStub = session => new GraphSummaryWindow(session, window.GraphCanvas.Viewer.Graph.GetRateName()).Close();
                await window.OpenGraphSummaryAsync();
            }

            for (int i = 0; i < 3; i++) {
                window.SettingsDialogStub = async options => {
                    var settingsWindow = new SettingsWindow(options);
                    settingsWindow.ComparePresetsDialogStub = presets => {
                        //Never started (SimulateProcessPresetsClickAsync isn't called), so PresetComparatorWindow's
                        //own Closed handler no-ops (comparing stays false) rather than clearing a cache this test
                        //never loaded.
                        new PresetComparatorWindow(presets).Close();
                        return Task.CompletedTask;
                    };
                    await settingsWindow.SimulateComparePresetsClickAsync().ConfigureAwait(true);
                    settingsWindow.SimulateCancelClick();
                    return settingsWindow.DialogResultValue;
                };
                await window.OpenSettingsAsync();
            }

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            var rateOptions = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "RateOptionsDropDown");
            rateOptions.SelectedIndex = (rateOptions.SelectedIndex + 1) % rateOptions.ItemCount;

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
        public async Task RealShowSettingsDialogAsync_Closing_RestoresGraphCanvasFocus() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            CheckBox gridlinesCheckbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GridlinesCheckbox");
            gridlinesCheckbox.Focus();
            Assert.True(gridlinesCheckbox.IsFocused);

            Task openTask = window.OpenSettingsAsync();
            SettingsWindow settingsWindow = window.OwnedWindows.OfType<SettingsWindow>().Single();
            settingsWindow.SimulateCancelClick();
            await openTask;

            Assert.True(window.GraphCanvas.IsFocused);
        }

        [AvaloniaFact]
        public async Task RealShowGraphSummaryAsync_Closing_RestoresGraphCanvasFocus() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);

            CheckBox gridlinesCheckbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GridlinesCheckbox");
            gridlinesCheckbox.Focus();
            Assert.True(gridlinesCheckbox.IsFocused);

            Task openTask = window.OpenGraphSummaryAsync();
            GraphSummaryWindow summaryWindow = window.OwnedWindows.OfType<GraphSummaryWindow>().Single();
            summaryWindow.Close();
            await openTask;

            Assert.True(window.GraphCanvas.IsFocused);
        }

        //Phase 7 deferred-minors sweep (toolbar-staleness fix, widened scope): OpenSettingsAsync now closes
        //any open floating panel before it opens itself - a toolbar/menu click never reaches
        //GraphCanvasControl.OnPointerPressed's own click-outside-closes-panel guard, so a panel left open
        //under Settings used to stay stale once the dialog closed. Supersedes this test's own prior
        //assertion (the dialog's open-panel focus-restore guard skipping GraphCanvas.Focus() while a panel
        //was still open) - the panel is gone by the time that guard runs now, so focus always restores.
        [AvaloniaFact]
        public async Task RealShowSettingsDialogAsync_OpenedWhilePanelOpen_ClosesPanelAndRestoresCanvasFocusOnClose() {
            DataCache cache = await GetSharedVanillaCacheAsync();
            string tempHome = NewTempHome();
            var settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            MainWindow window = NewReadyWindow(cache, settings, tempHome);
            window.GraphCanvas.AddRecipeAsync(new Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            Task openTask = window.OpenSettingsAsync();

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);

            SettingsWindow settingsWindow = window.OwnedWindows.OfType<SettingsWindow>().Single();
            settingsWindow.SimulateCancelClick();
            await openTask;

            Assert.True(window.GraphCanvas.IsFocused);
        }
    }
}
