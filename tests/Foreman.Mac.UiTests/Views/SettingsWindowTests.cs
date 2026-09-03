using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    public class SettingsWindowTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string SpaceAgePresetName = "Factorio 2.0 Space Age";
        private static readonly string[] VanillaPresetModList = ["base_2.0.76", "core_1.0"];

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

        private static string NewTempHome() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(home);
            return home;
        }

        private static SettingsWindow.SettingsWindowOptions NewOptions(DataCache cache, List<Preset> presets) =>
            new(cache) { Presets = presets, SelectedPreset = presets.FirstOrDefault(p => p.IsCurrentlySelected) };

        //--- Tab shell ---------------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task MainTabControl_HasPresetsEnabledObjectsGraphOptionsInOrder() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));

            var headers = window.FindControl<TabControl>("MainTabControl")!.Items
                .OfType<TabItem>().Select(t => t.Header).ToList();

            Assert.Equal(new object?[] { "Presets", "Enabled Objects", "Graph Options" }, headers);
        }

        //--- Current preset label + preset list contents ------------------------------------------------

        [AvaloniaFact]
        public async Task PresetListBox_ExcludesActivePresetAndShowsCurrentPresetLabel() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current, other]));

            Assert.Equal(VanillaPresetName, window.CurrentPresetLabelControl.Text);
            var listed = window.PresetListBoxControl.ItemsSource!.Cast<Preset>().ToList();
            Assert.DoesNotContain(current, listed);
            Assert.Contains(other, listed);
        }

        [AvaloniaFact]
        public async Task CurrentPresetLabel_Click_ClearsPresetListSelection() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current, other]));
            window.PresetListBoxControl.SelectedItem = other;

            window.SimulateCurrentPresetLabelClick();

            Assert.Null(window.PresetListBoxControl.SelectedItem);
        }

        //Minor#2 (final fix wave, upstream SettingsForm.cs:263-265): bold marks "this is the active preset",
        //so it drops once the list picks out a different candidate and returns once that pick is cleared.
        [AvaloniaFact]
        public async Task CurrentPresetLabel_BoldOnlyWhenPresetListSelectionIsEmpty() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current, other]));
            Assert.Equal(FontWeight.Bold, window.CurrentPresetLabelControl.FontWeight);

            window.PresetListBoxControl.SelectedItem = other;
            Assert.Equal(FontWeight.Normal, window.CurrentPresetLabelControl.FontWeight);

            window.PresetListBoxControl.SelectedItem = null;
            Assert.Equal(FontWeight.Bold, window.CurrentPresetLabelControl.FontWeight);
        }

        [AvaloniaFact]
        public async Task ModSelectionBox_ReflectsSelectedPresetModList() {
            var current = new Preset(VanillaPresetName, true, true);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current]));

            var mods = window.ModSelectionBoxControl.ItemsSource?.Cast<string>().ToList();

            Assert.Equal(VanillaPresetModList, mods);
        }

        //--- Confirm / Cancel ----------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task ConfirmButton_Click_ClosesWithTrueAndLeavesSelectedPresetUnchanged() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current, new Preset(SpaceAgePresetName, false, false)]);
            var window = new SettingsWindow(options);

            window.SimulateConfirmClick();

            Assert.True(window.DialogResultValue);
            Assert.Same(current, options.SelectedPreset);
        }

        [AvaloniaFact]
        public async Task CancelButton_Click_ClosesWithFalseAndDiscardsAnySelection() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current, new Preset(SpaceAgePresetName, false, false)]);
            var window = new SettingsWindow(options);
            window.PresetListBoxControl.SelectedItem = options.Presets![1];

            window.SimulateCancelClick();

            Assert.False(window.DialogResultValue);
            Assert.Same(current, options.SelectedPreset);
        }

        //Imp#2 (final fix wave, upstream SettingsForm.Designer.cs:1687/1692): Confirm/Cancel are the
        //window's AcceptButton/CancelButton, so Enter/Escape must reach them without a click.
        [AvaloniaFact]
        public async Task EnterKey_ConfirmsDialog() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current, new Preset(SpaceAgePresetName, false, false)]);
            var window = new SettingsWindow(options);
            window.Show();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.True(window.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task EscapeKey_CancelsDialog() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current, new Preset(SpaceAgePresetName, false, false)]);
            var window = new SettingsWindow(options);
            window.Show();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.False(window.DialogResultValue);
        }

        //--- Preset switch via double-click / select menu item --------------------------------------------

        [AvaloniaFact]
        public async Task PresetListBox_DoubleClickPreset_SetsSelectedPresetAndClosesOk() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var options = NewOptions(await GetCacheAsync(), [current, other]);
            var window = new SettingsWindow(options);

            window.SimulateDoubleClickPreset(other);

            Assert.Same(other, options.SelectedPreset);
            Assert.True(window.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task SelectPresetMenuItem_Click_SetsSelectedPresetAndClosesOk() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var options = NewOptions(await GetCacheAsync(), [current, other]);
            var window = new SettingsWindow(options);

            window.SimulateSelectPresetMenuItemClick(other);

            Assert.Same(other, options.SelectedPreset);
            Assert.True(window.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task SelectPreset_CommitsPendingChangesBeforeClosing() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current, other]));

            window.SimulateDoubleClickPreset(other);

            Assert.Equal(1, window.CommitPendingChangesCallCount);
        }

        //--- Dynamic right-click menu captions -------------------------------------------------------------

        [AvaloniaFact]
        public async Task ContextMenu_OrdinaryPreset_ShowsUseThisPresetAndDeleteThisPreset() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            var ordinary = new Preset("Some Other Preset", false, false);

            window.UpdatePresetMenuCaptionsFor(ordinary);

            Assert.Equal("Use This Preset", window.SelectPresetMenuItemControl.Header);
            Assert.True(window.SelectPresetMenuItemControl.IsEnabled);
            Assert.Equal("Delete This Preset", window.DeletePresetMenuItemControl.Header);
            Assert.True(window.DeletePresetMenuItemControl.IsEnabled);
        }

        [AvaloniaFact]
        public async Task ContextMenu_DefaultPreset_ShowsDisabledDefaultPresetCaption() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(SpaceAgePresetName, true, false)]));
            var defaultPreset = new Preset(VanillaPresetName, false, true);

            window.UpdatePresetMenuCaptionsFor(defaultPreset);

            Assert.Equal("Use This Preset", window.SelectPresetMenuItemControl.Header);
            Assert.Equal("Default Preset", window.DeletePresetMenuItemControl.Header);
            Assert.False(window.DeletePresetMenuItemControl.IsEnabled);
        }

        //Defensive branch: unreachable via the real UI (the active preset never appears in PresetListBox),
        //ported anyway since upstream keeps the same branch (reference SettingsForm.cs:277-291).
        [AvaloniaFact]
        public async Task ContextMenu_CurrentlySelectedPreset_ShowsDisabledCurrentPresetCaption() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            var currentlySelected = new Preset("Weird Preset", true, false);

            window.UpdatePresetMenuCaptionsFor(currentlySelected);

            Assert.Equal("Current Preset", window.SelectPresetMenuItemControl.Header);
            Assert.False(window.SelectPresetMenuItemControl.IsEnabled);
            Assert.Equal("Delete This Preset", window.DeletePresetMenuItemControl.Header);
            Assert.False(window.DeletePresetMenuItemControl.IsEnabled);
        }

        //--- Delete preset ---------------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task DeletePresetMenuItem_Confirmed_RemovesPresetFromOptionsAndList() {
            var current = new Preset(VanillaPresetName, true, true);
            var doomed = new Preset("Nonexistent Test Preset", false, false);
            var options = NewOptions(await GetCacheAsync(), [current, doomed]);
            var window = new SettingsWindow(options) { DeleteConfirmationStub = _ => Task.FromResult(true) };

            await window.SimulateDeletePresetMenuItemClickAsync(doomed);

            Assert.DoesNotContain(doomed, options.Presets!);
            Assert.DoesNotContain(doomed, window.PresetListBoxControl.ItemsSource!.Cast<Preset>());
        }

        [AvaloniaFact]
        public async Task DeletePresetMenuItem_Declined_KeepsPresetInList() {
            var current = new Preset(VanillaPresetName, true, true);
            var spared = new Preset("Nonexistent Test Preset", false, false);
            var options = NewOptions(await GetCacheAsync(), [current, spared]);
            var window = new SettingsWindow(options) { DeleteConfirmationStub = _ => Task.FromResult(false) };

            await window.SimulateDeletePresetMenuItemClickAsync(spared);

            Assert.Contains(spared, options.Presets!);
        }

        //--- Import (reference io-reference.md §4/§7) -----------------------------------------------------

        [AvaloniaFact]
        public async Task ImportPresetButton_Click_OverwroteTheActivePreset_SetsRequireReloadAndCloses() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options);
            window.ImportPresetDialogStub = w => { w.SimulateImportSuccess(VanillaPresetName); return Task.CompletedTask; };

            await window.SimulateImportPresetClickAsync();

            Assert.True(options.RequireReload);
            Assert.Equal(true, window.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task ImportPresetButton_Click_NewPresetName_AddsItToTheListAndOffersToSwitch() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options) { ImportSwitchConfirmationStub = () => Task.FromResult(false) };
            window.ImportPresetDialogStub = w => { w.SimulateImportSuccess("Brand New Preset"); return Task.CompletedTask; };

            await window.SimulateImportPresetClickAsync();

            Assert.Contains(options.Presets!, p => p.Name == "Brand New Preset");
            Assert.Contains(window.PresetListBoxControl.ItemsSource!.Cast<Preset>(), p => p.Name == "Brand New Preset");
            Assert.False(options.RequireReload);
            Assert.Null(window.DialogResultValue); //declined the switch - Settings stays open
        }

        [AvaloniaFact]
        public async Task ImportPresetButton_Click_NewPresetName_SwitchAccepted_SelectsItAndCloses() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options) { ImportSwitchConfirmationStub = () => Task.FromResult(true) };
            window.ImportPresetDialogStub = w => { w.SimulateImportSuccess("Brand New Preset"); return Task.CompletedTask; };

            await window.SimulateImportPresetClickAsync();

            Assert.Equal("Brand New Preset", options.SelectedPreset?.Name);
            Assert.Equal(true, window.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task ImportPresetButton_Click_CancelledDialog_LeavesOptionsUnchanged() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options);
            window.ImportPresetDialogStub = _ => Task.CompletedTask; //NewPresetName stays "" - nothing simulated

            await window.SimulateImportPresetClickAsync();

            Assert.Single(options.Presets!);
            Assert.Null(window.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task ComparePresetsButton_Click_FewerThanTwoPresets_ShowsGuardMessageVerbatim() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            string? capturedMessage = null;
            window.WarningDialogStub = (_, message) => { capturedMessage = message; return Task.CompletedTask; };

            await window.SimulateComparePresetsClickAsync();

            Assert.Equal("Can not compare presets!\n...you only have 1 preset :/", capturedMessage);
        }

        [AvaloniaFact]
        public async Task ComparePresetsButton_Click_TwoOrMorePresets_OpensComparatorWithPresetList() {
            var presets = new List<Preset> { new(VanillaPresetName, true, true), new(SpaceAgePresetName, false, false) };
            var options = NewOptions(await GetCacheAsync(), presets);
            var window = new SettingsWindow(options);
            List<Preset>? captured = null;
            window.ComparePresetsDialogStub = list => { captured = list; return Task.CompletedTask; };

            await window.SimulateComparePresetsClickAsync();

            Assert.Same(presets, captured);
        }

        //--- MainWindow post-close cascade -----------------------------------------------------------------

        private static async Task<MainWindow> NewMainWindowAsync(string tempHome) {
            var window = new MainWindow();
            window.DataCache = await GetCacheAsync();
            window.Settings = new AppSettings { CurrentPresetName = VanillaPresetName };
            window.PresetResolver = new PresetResolver();
            window.SettingsService = new SettingsService(tempHome);
            return window;
        }

        private static string SettingsFilePath(string home) =>
            Path.Combine(home, "Library", "Application Support", "Foreman", "settings.json");

        [AvaloniaFact]
        public async Task OpenSettingsAsync_ConfirmWithoutPresetChange_PersistsSettingsAndDoesNotReload() {
            string home = NewTempHome();
            var window = await NewMainWindowAsync(home);
            var originalCache = window.DataCache;
            window.SettingsDialogStub = _ => Task.FromResult<bool?>(true);

            await window.OpenSettingsAsync();

            Assert.True(File.Exists(SettingsFilePath(home)));
            Assert.Same(originalCache, window.DataCache);
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_PopulatesEnabledObjectsFromTheLiveCacheBeforeShowingTheDialog() {
            string home = NewTempHome();
            var window = await NewMainWindowAsync(home);
            var cache = window.DataCache!;
            HashSet<IDataObjectBase>? captured = null;
            window.SettingsDialogStub = options => {
                captured = [.. options.EnabledObjects];
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            var expected = new HashSet<IDataObjectBase>();
            expected.UnionWith(cache.Recipes.Values.Where(r => r.Enabled));
            expected.UnionWith(cache.Assemblers.Values.Where(r => r.Enabled));
            expected.UnionWith(cache.Beacons.Values.Where(r => r.Enabled));
            expected.UnionWith(cache.Modules.Values.Where(r => r.Enabled));
            expected.UnionWith(cache.Qualities.Values.Where(r => r.Enabled));
            Assert.True(captured!.SetEquals(expected));
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_ConfirmWithoutPresetChange_SyncsEnabledObjectsOntoLiveCache() {
            string home = NewTempHome();
            var window = await NewMainWindowAsync(home);
            var cache = window.DataCache!;
            window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            IRecipe recipe = cache.Recipes.Values.First(r => r.Enabled && r.Available);
            RecipeNode node = window.GraphCanvas.Viewer.Graph.CreateRecipeNode(new RecipeQualityPair(recipe, cache.DefaultQuality!), new Point(0, 0));
            window.SettingsDialogStub = options => {
                options.EnabledObjects.UnionWith(cache.Recipes.Values.Where(r => r.Enabled));
                options.EnabledObjects.UnionWith(cache.Assemblers.Values.Where(r => r.Enabled));
                options.EnabledObjects.UnionWith(cache.Beacons.Values.Where(r => r.Enabled));
                options.EnabledObjects.UnionWith(cache.Modules.Values.Where(r => r.Enabled));
                options.EnabledObjects.UnionWith(cache.Qualities.Values.Where(r => r.Enabled));
                options.EnabledObjects.Remove(recipe);
                return Task.FromResult<bool?>(true);
            };

            try {
                await window.OpenSettingsAsync();

                Assert.False(recipe.Enabled);
                node.UpdateState();
                Assert.True(node.WarningSet.HasFlag(RecipeNode.Warnings.RecipeIsDisabled));
            } finally {
                recipe.Enabled = true; //restore the shared fixture cache for later tests
            }
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_Cancel_DoesNotPersistOrReload() {
            string home = NewTempHome();
            var window = await NewMainWindowAsync(home);
            var originalCache = window.DataCache;
            window.SettingsDialogStub = _ => Task.FromResult<bool?>(false);

            await window.OpenSettingsAsync();

            Assert.False(File.Exists(SettingsFilePath(home)));
            Assert.Same(originalCache, window.DataCache);
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_PresetSwitch_ReloadsDataCacheAndPersistsNewPresetName() {
            string home = NewTempHome();
            var window = await NewMainWindowAsync(home);
            var originalCache = window.DataCache;
            window.SettingsDialogStub = options => {
                options.SelectedPreset = options.Presets!.First(p => p.Name == SpaceAgePresetName);
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.NotSame(originalCache, window.DataCache);
            Assert.Equal(SpaceAgePresetName, window.Settings!.CurrentPresetName);
            Assert.True(File.Exists(SettingsFilePath(home)));
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_RequireReloadTrueWithSamePreset_StillReloads() {
            string home = NewTempHome();
            var window = await NewMainWindowAsync(home);
            var originalCache = window.DataCache;
            //RequireReload only fires once per real user action (e.g. Task 7's preset-overwrote-active-preset
            //import) - the reopen tail's own fresh dialog wouldn't set it again unless the user imports a
            //second time, so this stub mirrors that: true on the first call the reopen tail then reopens
            //against, false (a plain Cancel of that reopened dialog) on every call after.
            int callCount = 0;
            window.SettingsDialogStub = options => {
                callCount++;
                options.RequireReload = callCount == 1;
                return Task.FromResult<bool?>(callCount == 1);
            };

            await window.OpenSettingsAsync();

            Assert.Equal(2, callCount);

            Assert.NotSame(originalCache, window.DataCache);
            Assert.Equal(VanillaPresetName, window.Settings!.CurrentPresetName);
        }

        [AvaloniaFact]
        public async Task OpenSettingsAsync_PresetSwitch_RemapsExistingGraphNodesOntoNewPreset() {
            string home = NewTempHome();
            var window = await NewMainWindowAsync(home);
            IItem ironPlate = window.DataCache!.Items["iron-plate"];
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(ironPlate, window.DataCache!.DefaultQuality!), new Point(0, 0));
            window.SettingsDialogStub = options => {
                options.SelectedPreset = options.Presets!.First(p => p.Name == SpaceAgePresetName);
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.NotEmpty(window.GraphCanvas.Viewer.Graph.Nodes);
        }

        //--- Enabled Objects tab: sub-tab shell -------------------------------------------------------------

        private static SettingsWindow NewEnabledObjectsWindow(DataCache cache) =>
            new(NewOptions(cache, [new Preset(VanillaPresetName, true, true)]));

        [AvaloniaFact]
        public async Task EnabledObjectsTabControl_HasSevenSubTabsInOrder() {
            var window = NewEnabledObjectsWindow(await GetCacheAsync());

            var headers = window.FindControl<TabControl>("EnabledObjectsTabControl")!.Items
                .OfType<TabItem>().Select(t => t.Header).ToList();

            Assert.Equal(
                new object?[] { "Assemblers", "Miners", "Power", "Beacons", "Modules", "Recipes", "Qualities" },
                headers);
        }

        [AvaloniaFact]
        public async Task SubTabLists_ContainCountsMatchingTheFixtureCacheDictionaries() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.ShowUnavailablesFilterCheckBoxControl.IsChecked = true;

            int expectedAssemblers = cache.Assemblers.Values.Count(a => a.EntityType == EntityType.Assembler);
            int expectedMiners = cache.Assemblers.Values.Count(a => a.EntityType is EntityType.Miner or EntityType.OffshorePump);
            int expectedPower = cache.Assemblers.Values.Count(a => a.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor);

            Assert.Equal(expectedAssemblers, window.AssemblerListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(expectedMiners, window.MinerListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(expectedPower, window.PowerListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(cache.Beacons.Count, window.BeaconListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(cache.Modules.Count, window.ModuleListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(cache.Recipes.Count, window.RecipeListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(cache.Qualities.Count, window.QualityListViewControl.ItemsSource!.Cast<object>().Count());
        }

        [AvaloniaFact]
        public async Task RecipeListView_WithBoundedHeight_RealizesFewerContainersThanTotalItems() {
            var cache = await GetCacheAsync();
            Assert.True(cache.Recipes.Count > 50, "Fixture cache should carry enough recipes for virtualization to matter.");
            var window = NewEnabledObjectsWindow(cache);
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 1;
            window.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;

            window.Show();

            int shown = window.RecipeListViewControl.ItemsSource!.Cast<object>().Count();
            int realized = window.RecipeListViewControl.GetVisualDescendants().OfType<ListBoxItem>().Count();
            Assert.True(realized > 0, "Expected at least one realized row.");
            Assert.True(realized < shown, $"Expected virtualization to realize fewer than {shown} shown rows, got {realized}.");
        }

        //--- Enabled Objects tab: filter row -----------------------------------------------------------------

        [AvaloniaFact]
        public async Task FilterRow_DefaultState_HidesUnavailableRecipes() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);

            int shown = window.RecipeListViewControl.ItemsSource!.Cast<object>().Count();

            Assert.Equal(cache.Recipes.Values.Count(r => r.Available), shown);
        }

        [AvaloniaFact]
        public async Task ShowUnavailablesFilterCheckBox_Checked_RevealsUnavailableRecipes() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);

            window.ShowUnavailablesFilterCheckBoxControl.IsChecked = true;

            Assert.Equal(cache.Recipes.Count, window.RecipeListViewControl.ItemsSource!.Cast<object>().Count());
        }

        [AvaloniaFact]
        public async Task FilterTextBox_FiltersRecipesByNameOrdinalIgnoreCase() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            IRecipe target = cache.Recipes.Values.First(r => r.Available);
            string needle = target.FriendlyName[..Math.Min(4, target.FriendlyName.Length)].ToUpperInvariant();

            window.FilterTextBoxControl.Text = needle;

            var shown = window.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().ToList();
            Assert.NotEmpty(shown);
            Assert.All(shown, item => Assert.Contains(needle, item.Name, StringComparison.OrdinalIgnoreCase));
        }

        //Phase5b hands-on gate (Finding 1): rows bind Background (White for available, Pink for
        //unavailable) but never bound Foreground, so the live Fluent dark theme's default white TextBlock
        //foreground renders invisible text on the White rows.
        [AvaloniaFact]
        public async Task EnabledObjectRows_ForegroundIsReadableAgainstRowBackground() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.ShowUnavailablesFilterCheckBoxControl.IsChecked = true;
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 1;
            window.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            window.Show();

            var rows = window.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().ToList();
            EnabledObjectsListItem availableRow = rows.First(r => r.DataObject.Available);
            ContrastAssert.Readable(availableRow.Foreground, availableRow.RowBackground);

            EnabledObjectsListItem? unavailableRow = rows.FirstOrDefault(r => !r.DataObject.Available);
            if (unavailableRow is not null)
                ContrastAssert.Readable(unavailableRow.Foreground, unavailableRow.RowBackground);

            //Confirms the XAML binding actually reaches the realized TextBlock, not just the row model.
            TextBlock realizedNameText = window.RecipeListViewControl.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.DataContext is EnabledObjectsListItem item && item.Name == availableRow.Name);
            Assert.Equal(availableRow.Foreground, realizedNameText.Foreground);
        }

        //A descendant is fully contained (no negative clip) if the sum of its ancestors' Bounds offsets, up
        //to and including root, keeps its rectangle inside root's own (0,0,Width,Height) - Avalonia's Bounds
        //is parent-relative, so containment has to be checked by walking the chain rather than off a single
        //Bounds comparison.
        private static bool IsFullyContainedWithin(Avalonia.Visual root, Avalonia.Visual descendant) {
            double x = 0, y = 0;
            Avalonia.Visual? current = descendant;
            while (current is not null && current != root) {
                x += current.Bounds.X;
                y += current.Bounds.Y;
                current = current.GetVisualParent();
            }
            if (current != root)
                return false;
            return x >= 0 && y >= 0 && x + descendant.Bounds.Width <= root.Bounds.Width && y + descendant.Bounds.Height <= root.Bounds.Height;
        }

        //Regression: the Fluent CheckBox's real unclipped extent is 28x32 (measured unconstrained) - capping
        //the row's CheckBox to Height=20/MinHeight=0 shrinks its arranged box without shrinking the template's
        //own internal layout, so the indicator border renders past the bottom edge and gets clipped.
        [AvaloniaFact]
        public async Task EnabledObjectRow_CheckBox_RendersWithNoClippedDescendants() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 1;
            window.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            window.Show();

            CheckBox realizedBox = window.RecipeListViewControl.GetVisualDescendants().OfType<CheckBox>().First();
            foreach (Avalonia.Visual descendant in realizedBox.GetVisualDescendants())
                Assert.True(IsFullyContainedWithin(realizedBox, descendant),
                    $"{descendant.GetType().Name} at {descendant.Bounds} is clipped by its CheckBox (Bounds={realizedBox.Bounds}).");
        }

        //Regression: the Fluent theme's default ListBoxItem carries generous Padding/MinHeight, leaving
        //upstream's dense WinForms rows (24px icon, minimal chrome) looking sparse and hard to scan at a
        //glance.
        [AvaloniaFact]
        public async Task EnabledObjectRows_AreDenseLikeUpstream() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 1;
            window.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            window.Show();

            //Floor is the 24px icon plus the two 1px top/bottom padding pairs (Border's own Padding="4,1" and
            //the ListBox.compact style's Padding="2,1") - 24 + 2 + 2 = 28px, unchanged by the CheckBox's
            //Viewbox: it's sized to 20px, under the icon's 24px, so it was never the row's height driver.
            ListBoxItem realizedRow = window.RecipeListViewControl.GetVisualDescendants().OfType<ListBoxItem>().First();
            Assert.Equal(28d, realizedRow.Bounds.Height);
        }

        //--- Enabled Objects tab: live check toggle -----------------------------------------------------------

        [AvaloniaFact]
        public async Task CheckingARecipeRow_AddsItToEnabledObjectsImmediately() {
            var cache = await GetCacheAsync();
            var options = NewOptions(cache, [new Preset(VanillaPresetName, true, true)]);
            var window = new SettingsWindow(options);
            var item = window.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().First();
            Assert.DoesNotContain(item.DataObject, options.EnabledObjects);

            item.IsChecked = true;

            Assert.Contains(item.DataObject, options.EnabledObjects);
        }

        [AvaloniaFact]
        public async Task UncheckingARecipeRow_RemovesItFromEnabledObjectsImmediately() {
            var cache = await GetCacheAsync();
            var options = NewOptions(cache, [new Preset(VanillaPresetName, true, true)]);
            var window = new SettingsWindow(options);
            var item = window.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().First();
            item.IsChecked = true;

            item.IsChecked = false;

            Assert.DoesNotContain(item.DataObject, options.EnabledObjects);
        }

        //--- Enabled Objects tab: selected-rows bulk toggle --------------------------------------------------------

        [AvaloniaFact]
        public async Task TogglingOneSelectedRowsCheckbox_AppliesTheSameStateToEveryOtherSelectedRow() {
            var cache = await GetCacheAsync();
            var options = NewOptions(cache, [new Preset(VanillaPresetName, true, true)]);
            var window = new SettingsWindow(options);
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 1;
            window.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            window.Show();
            window.RecipeListViewControl.Focus();
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Meta);
            var rows = window.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().ToList();

            rows[0].IsChecked = true;

            Assert.All(rows, row => Assert.True(row.IsChecked));
            Assert.True(options.EnabledObjects.SetEquals(rows.Select(r => r.DataObject)));
        }

        [AvaloniaFact]
        public async Task TogglingARowsCheckbox_OutsideTheSelection_OnlyAffectsThatRow() {
            var cache = await GetCacheAsync();
            var options = NewOptions(cache, [new Preset(VanillaPresetName, true, true)]);
            var window = new SettingsWindow(options);
            var rows = window.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().ToList();
            window.RecipeListViewControl.SelectedItems!.Add(rows[0]);
            window.RecipeListViewControl.SelectedItems!.Add(rows[1]);

            rows[2].IsChecked = true;

            Assert.True(rows[2].IsChecked);
            Assert.False(rows[0].IsChecked);
            Assert.False(rows[1].IsChecked);
            Assert.DoesNotContain(rows[0].DataObject, options.EnabledObjects);
            Assert.DoesNotContain(rows[1].DataObject, options.EnabledObjects);
        }

        //--- Enabled Objects tab: Enable All -------------------------------------------------------------------

        [AvaloniaFact]
        public async Task EnableAllButton_Click_ClearsThenRepopulatesFromEveryAvailableCategoryPlusPlayerAssembler() {
            var cache = await GetCacheAsync();
            var options = NewOptions(cache, [new Preset(VanillaPresetName, true, true)]);
            options.EnabledObjects.Add(cache.Recipes.Values.First()); //stale entry Enable All must clear away first
            var window = new SettingsWindow(options);

            window.SimulateEnableAllClick();

            var expected = new HashSet<IDataObjectBase>();
            if (cache.PlayerAssembler is not null)
                expected.Add(cache.PlayerAssembler);
            foreach (var a in cache.Assemblers.Values.Where(a => a.AssociatedItems.Any(i => i.Available)))
                expected.Add(a);
            foreach (var b in cache.Beacons.Values.Where(b => b.AssociatedItems.Any(i => i.Available)))
                expected.Add(b);
            foreach (var m in cache.Modules.Values.Where(m => m.AssociatedItem.Available))
                expected.Add(m);
            foreach (var r in cache.Recipes.Values.Where(r => r.Available))
                expected.Add(r);
            foreach (var q in cache.Qualities.Values.Where(q => q.Available))
                expected.Add(q);

            Assert.True(options.EnabledObjects.SetEquals(expected));
        }

        //--- Enabled Objects tab: Cmd+A selects without checking -------------------------------------------------

        [AvaloniaFact]
        public async Task CmdA_OnActiveSubTab_SelectsEveryRowWithoutCheckingThem() {
            var cache = await GetCacheAsync();
            var options = NewOptions(cache, [new Preset(VanillaPresetName, true, true)]);
            var window = new SettingsWindow(options);
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 1;
            window.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            window.Show();
            window.RecipeListViewControl.Focus();

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Meta);

            int totalRows = window.RecipeListViewControl.ItemsSource!.Cast<object>().Count();
            Assert.Equal(totalRows, window.RecipeListViewControl.SelectedItems!.Count);
            Assert.Empty(options.EnabledObjects);
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): Ctrl+A on
        //Linux does the same thing Cmd+A does on macOS, via the UseIsMacOs seam.
        [AvaloniaFact]
        public async Task CtrlA_OnActiveSubTab_OnLinux_SelectsEveryRowWithoutCheckingThem() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            var cache = await GetCacheAsync();
            var options = NewOptions(cache, [new Preset(VanillaPresetName, true, true)]);
            var window = new SettingsWindow(options);
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 1;
            window.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            window.Show();
            window.RecipeListViewControl.Focus();

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            int totalRows = window.RecipeListViewControl.ItemsSource!.Cast<object>().Count();
            Assert.Equal(totalRows, window.RecipeListViewControl.SelectedItems!.Count);
            Assert.Empty(options.EnabledObjects);
        }

        //--- Enabled Objects tab: Load from save (reference io-reference.md §5) -------------------------------

        //Drives SaveFileLoadWindow's own seams through LoadFromSaveDialogStub instead of a real modal
        //ShowDialog - RunAsync is exactly what the window's Opened handler would otherwise trigger.
        //Regression (C2): the eager settings.json write in ShowLoadFromSaveAsync ran through this path
        //against the real ~/Library/.../settings.json until SettingsService gained an injectable seam -
        //pointing this at a temp home and asserting on that file (not the real one) guards against a
        //relapse.
        [AvaloniaFact]
        public async Task LoadEnabledFromSaveButton_Click_OutcomeOk_AppliesTheDerivedEnabledObjectsAndRefreshesCheckedState() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            string home = NewTempHome();
            window.SettingsService = new SettingsService(home);
            var saveInfo = new SaveFileInfo();
            window.LoadFromSaveDialogStub = w => {
                w.OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip");
                w.LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Ok, SaveFileInfo = saveInfo };
                w.ConfirmDialogStub = (_, _) => Task.FromResult(true); //mods mismatch is expected here - saveInfo carries none.
                return w.RunAsync();
            };

            await window.SimulateLoadFromSaveClickAsync();

            if (cache.PlayerAssembler is not null)
                Assert.Contains(cache.PlayerAssembler, window.Options.EnabledObjects);
            Assert.Equal("/tmp/saves", new SettingsService(home).Load().LastSaveFileLocation);
        }

        [AvaloniaFact]
        public async Task LoadEnabledFromSaveButton_Click_OutcomeAbort_ShowsTheVerbatimReSaveMessage() {
            var window = NewEnabledObjectsWindow(await GetCacheAsync());
            string? capturedMessage = null;
            window.WarningDialogStub = (_, message) => { capturedMessage = message; return Task.CompletedTask; };
            window.LoadFromSaveDialogStub = w => {
                w.OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip");
                w.LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Abort };
                return w.RunAsync();
            };

            await window.SimulateLoadFromSaveClickAsync();

            Assert.Equal(
                "Error while reading save file. Try running factorio, opening the save game, saving again, and retrying?",
                capturedMessage);
        }

        [AvaloniaFact]
        public async Task LoadEnabledFromSaveButton_Click_OutcomeCancel_ShowsNoDialogAndLeavesEnabledObjectsAlone() {
            var window = NewEnabledObjectsWindow(await GetCacheAsync());
            window.WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn on Cancel");
            window.LoadFromSaveDialogStub = w => {
                w.OpenSaveFilePathStub = () => Task.FromResult<string?>(null);
                return w.RunAsync();
            };

            await window.SimulateLoadFromSaveClickAsync();

            Assert.Empty(window.Options.EnabledObjects);
        }

        //--- Enabled Objects tab: Assign from science packs (reference io-reference.md §6) -----------------

        //Drives SciencePacksWindow's own Accepted flag through AssignFromSciencePacksDialogStub instead of
        //a real modal ShowDialog - mirrors LoadEnabledFromSaveButton's stub convention above.
        [AvaloniaFact]
        public async Task SetEnabledFromSciencePacksButton_Click_Accepted_RefreshesCheckedState() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.AssignFromSciencePacksDialogStub = w => {
                w.SimulateConfirmClick();
                return Task.CompletedTask;
            };

            await window.SimulateAssignFromSciencePacksClickAsync();

            if (cache.PlayerAssembler is not null)
                Assert.Contains(cache.PlayerAssembler, window.Options.EnabledObjects);
        }

        [AvaloniaFact]
        public async Task SetEnabledFromSciencePacksButton_Click_Cancelled_LeavesEnabledObjectsAlone() {
            var window = NewEnabledObjectsWindow(await GetCacheAsync());
            window.Options.EnabledObjects.Add(window.Options.DCache.Recipes.Values.First());
            window.AssignFromSciencePacksDialogStub = w => {
                w.SimulateCancelClick();
                return Task.CompletedTask;
            };

            await window.SimulateAssignFromSciencePacksClickAsync();

            Assert.Single(window.Options.EnabledObjects);
        }

        //--- Enabled Objects tab: Recipes hover tooltip ---------------------------------------------------------

        [AvaloniaFact]
        public async Task RecipeListItems_CarryABakedTooltipImage_OtherCategoriesDoNot() {
            var window = NewEnabledObjectsWindow(await GetCacheAsync());

            var recipeItem = window.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().First();
            var assemblerItem = window.AssemblerListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().First();

            Assert.IsType<Image>(recipeItem.TooltipContent);
            Assert.Null(assemblerItem.TooltipContent);
        }

        //--- Graph Options tab -----------------------------------------------------------------------------

        private static SettingsWindow NewGraphOptionsWindow(DataCache cache, Action<SettingsWindow.SettingsWindowOptions>? configure = null) {
            var options = NewOptions(cache, [new Preset(VanillaPresetName, true, true)]);
            configure?.Invoke(options);
            return new SettingsWindow(options);
        }

        private static void ConfigureNonDefaultGraphOptions(SettingsWindow.SettingsWindowOptions options) {
            options.QualitySteps = 9;
            options.LevelOfDetail = LevelOfDetail.High;
            options.NodeCountForSimpleView = 555;
            options.IconsOnlyIconSize = 64;
            options.ArrowsOnLinks = true;
            options.SimplePassthroughNodes = true;
            options.DynamicLinkWidth = true;
            options.AbbreviateSciPacks = false;
            options.ShowRecipeToolTip = false;
            options.RoundAssemblerCount = true;
            options.LockedRecipeEditPanelPosition = true;
            options.FlagOUSuppliedNodes = true;
            options.FlagDarkMode = ThemeMode.Dark;
            options.ShowErrorArrows = false;
            options.ShowWarningArrows = false;
            options.ShowDisconnectedArrows = true;
            options.ShowOUSuppliedArrows = true;
            options.DefaultAssemblerStyle = AssemblerSelector.Style.Best;
            options.DefaultModuleStyle = ModuleSelector.Style.Productivity;
            options.DefaultNodeDirection = NodeDirection.Down;
            options.SmartNodeDirection = false;
            options.EnableExtraProductivityForNonMiners = true;
            options.DevShowUnavailableItems = true;
            options.DevUseRecipeBWFilters = false;
            options.SolverLowPriorityPower = 2.5m;
            options.SolverPullConsumerNodes = true;
            options.SolverPullConsumerNodesPower = 3.5m;
        }

        //Contract: docs/panels-reference.md:222/upstream SettingsForm.Designer.cs:1457,1460,1619-1633. The
        //caption stays upstream's verbatim stale "2 default" text even though the real default (AppSettings.
        //LowPriorityPower) is 4 - this widget is a straight text/range port, not a corrected re-derivation.
        [AvaloniaFact]
        public async Task GraphOptionsTab_SolverAndNodeCountFields_MatchUpstreamCaptionAndRanges() {
            var window = NewGraphOptionsWindow(await GetCacheAsync());
            window.FindControl<TabControl>("MainTabControl")!.SelectedIndex = 2;
            window.Show();

            TextBlock caption = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text != null && t.Text.StartsWith("Low priority multiplier", StringComparison.Ordinal));
            Assert.Equal("Low priority multiplier   (10^n, 2 default):", caption.Text);

            Assert.Equal(0.2m, window.LowPriorityPowerInputControl.Increment);
            Assert.Equal(25m, window.NodeCountForSimpleViewInputControl.Increment);
            Assert.Equal(2000m, window.NodeCountForSimpleViewInputControl.Maximum);
        }

        [AvaloniaFact]
        public async Task GraphOptionsTab_Populates_AllWidgetsFromOptions() {
            var window = NewGraphOptionsWindow(await GetCacheAsync(), ConfigureNonDefaultGraphOptions);

            Assert.Equal(9m, window.QualityStepsInputControl.Value);
            Assert.True(window.HighLodRadioButtonControl.IsChecked);
            Assert.Equal(555m, window.NodeCountForSimpleViewInputControl.Value);
            Assert.Equal(64m, window.IconsSizeInputControl.Value);
            Assert.True(window.ArrowsOnLinksCheckBoxControl.IsChecked);
            Assert.True(window.SimplePassthroughNodesCheckBoxControl.IsChecked);
            Assert.True(window.DynamicLWCheckBoxControl.IsChecked);
            Assert.False(window.AbbreviateSciPackCheckBoxControl.IsChecked);
            Assert.False(window.ShowNodeRecipeCheckBoxControl.IsChecked);
            Assert.True(window.RoundAssemblerCountCheckBoxControl.IsChecked);
            Assert.True(window.RecipeEditPanelPositionLockCheckBoxControl.IsChecked);
            Assert.True(window.FlagOUSupplyNodesCheckBoxControl.IsChecked);
            Assert.True(window.FlagDarkModeCheckBoxControl.IsChecked);
            Assert.False(window.ErrorArrowsCheckBoxControl.IsChecked);
            Assert.False(window.WarningArrowsCheckBoxControl.IsChecked);
            Assert.True(window.DisconnectedArrowsCheckBoxControl.IsChecked);
            Assert.True(window.OUSuppliedArrowsCheckBoxControl.IsChecked);
            Assert.Equal((int)AssemblerSelector.Style.Best, window.AssemblerSelectorStyleDropDownControl.SelectedIndex);
            Assert.Equal((int)ModuleSelector.Style.Productivity, window.ModuleSelectorStyleDropDownControl.SelectedIndex);
            Assert.Equal(1, window.NodeDirectionDropDownControl.SelectedIndex);
            Assert.False(window.SmartNodeDirectionCheckBoxControl.IsChecked);
            Assert.True(window.ShowProductivityBonusOnAllCheckBoxControl.IsChecked);
            Assert.True(window.ShowUnavailablesCheckBoxControl.IsChecked);
            Assert.True(window.LoadBarrelingCheckBoxControl.IsChecked); //DevUseRecipeBWFilters false -> checkbox checked (inverted sense)
            Assert.Equal(2.5m, window.LowPriorityPowerInputControl.Value);
            Assert.True(window.PullConsumerNodesCheckBoxControl.IsChecked);
            Assert.Equal(3.5m, window.PullConsumerNodesPowerInputControl.Value);
        }

        [AvaloniaFact]
        public async Task GraphOptionsTab_ConfirmClick_CommitsAllWidgetsBackToOptions() {
            var options = NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]);
            var window = new SettingsWindow(options);

            window.QualityStepsInputControl.Value = 15m;
            window.HighLodRadioButtonControl.IsChecked = true;
            window.NodeCountForSimpleViewInputControl.Value = 42m;
            window.IconsSizeInputControl.Value = 200m;
            window.ArrowsOnLinksCheckBoxControl.IsChecked = true;
            window.SimplePassthroughNodesCheckBoxControl.IsChecked = true;
            window.DynamicLWCheckBoxControl.IsChecked = true;
            window.AbbreviateSciPackCheckBoxControl.IsChecked = false;
            window.ShowNodeRecipeCheckBoxControl.IsChecked = false;
            window.RoundAssemblerCountCheckBoxControl.IsChecked = true;
            window.RecipeEditPanelPositionLockCheckBoxControl.IsChecked = true;
            window.FlagOUSupplyNodesCheckBoxControl.IsChecked = true;
            window.FlagDarkModeCheckBoxControl.IsChecked = true;
            window.ErrorArrowsCheckBoxControl.IsChecked = true;
            window.WarningArrowsCheckBoxControl.IsChecked = true;
            window.DisconnectedArrowsCheckBoxControl.IsChecked = true;
            window.OUSuppliedArrowsCheckBoxControl.IsChecked = true;
            window.AssemblerSelectorStyleDropDownControl.SelectedIndex = (int)AssemblerSelector.Style.BestBurner;
            window.ModuleSelectorStyleDropDownControl.SelectedIndex = (int)ModuleSelector.Style.Efficiency;
            window.NodeDirectionDropDownControl.SelectedIndex = 1;
            window.SmartNodeDirectionCheckBoxControl.IsChecked = false;
            window.ShowProductivityBonusOnAllCheckBoxControl.IsChecked = true;
            window.ShowUnavailablesCheckBoxControl.IsChecked = true;
            window.LoadBarrelingCheckBoxControl.IsChecked = false; //-> DevUseRecipeBWFilters = true
            window.LowPriorityPowerInputControl.Value = 6m;
            window.PullConsumerNodesCheckBoxControl.IsChecked = true;
            window.PullConsumerNodesPowerInputControl.Value = 5m;

            window.SimulateConfirmClick();

            Assert.True(window.DialogResultValue);
            Assert.Equal(15u, options.QualitySteps);
            Assert.Equal(LevelOfDetail.High, options.LevelOfDetail);
            Assert.Equal(42, options.NodeCountForSimpleView);
            Assert.Equal(200, options.IconsOnlyIconSize);
            Assert.True(options.ArrowsOnLinks);
            Assert.True(options.SimplePassthroughNodes);
            Assert.True(options.DynamicLinkWidth);
            Assert.False(options.AbbreviateSciPacks);
            Assert.False(options.ShowRecipeToolTip);
            Assert.True(options.RoundAssemblerCount);
            Assert.True(options.LockedRecipeEditPanelPosition);
            Assert.True(options.FlagOUSuppliedNodes);
            Assert.Equal(ThemeMode.Dark, options.FlagDarkMode);
            Assert.True(options.ShowErrorArrows);
            Assert.True(options.ShowWarningArrows);
            Assert.True(options.ShowDisconnectedArrows);
            Assert.True(options.ShowOUSuppliedArrows);
            Assert.Equal(AssemblerSelector.Style.BestBurner, options.DefaultAssemblerStyle);
            Assert.Equal(ModuleSelector.Style.Efficiency, options.DefaultModuleStyle);
            Assert.Equal(NodeDirection.Down, options.DefaultNodeDirection);
            Assert.False(options.SmartNodeDirection);
            Assert.True(options.EnableExtraProductivityForNonMiners);
            Assert.True(options.DevShowUnavailableItems);
            Assert.True(options.DevUseRecipeBWFilters);
            Assert.Equal(6m, options.SolverLowPriorityPower);
            Assert.True(options.SolverPullConsumerNodes);
            Assert.Equal(5m, options.SolverPullConsumerNodesPower);
        }

        //Regression: every Graph Options widget write must route through CommitPendingChanges (Confirm,
        //preset double-click, "Use This Preset") - never live-on-click like Enabled Objects' membership
        //toggle (reference §5's CommitPendingChanges hook comment).
        [AvaloniaFact]
        public async Task GraphOptionsTab_WidgetEditWithoutCommit_LeavesOptionsUntouched() {
            var options = NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]);
            uint before = options.QualitySteps;
            var window = new SettingsWindow(options);

            window.QualityStepsInputControl.Value = 17m;
            window.PullConsumerNodesCheckBoxControl.IsChecked = true;

            Assert.Equal(before, options.QualitySteps);
            Assert.False(options.SolverPullConsumerNodes);
        }

        //Regression: a widget edit followed by a preset double-click (not Confirm) must still persist -
        //SelectPreset also routes through CommitPendingChanges (reference SettingsForm.cs:303/333).
        [AvaloniaFact]
        public async Task GraphOptionsTab_WidgetEditThenDoubleClickPreset_StillCommits() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var options = NewOptions(await GetCacheAsync(), [current, other]);
            var window = new SettingsWindow(options);

            window.LowPriorityPowerInputControl.Value = 6m;
            window.PresetListBoxControl.SelectedItem = other;
            window.SimulateDoubleClickPreset(other);

            Assert.Equal(6m, options.SolverLowPriorityPower);
            Assert.Same(other, options.SelectedPreset);
        }

        [AvaloniaTheory]
        [InlineData(true, ThemeMode.Dark)]
        [InlineData(false, ThemeMode.Light)]
        public async Task FlagDarkModeCheckBox_Confirm_MapsToThemeMode(bool checkedState, ThemeMode expected) {
            var options = NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]);
            var window = new SettingsWindow(options);

            window.FlagDarkModeCheckBoxControl.IsChecked = checkedState;
            window.SimulateConfirmClick();

            Assert.Equal(expected, options.FlagDarkMode);
        }

        //Regression (human nit 1, upstream MainForm.cs:36-46): the checkbox used to commit ThemeMode.System
        //when unchecked, which stays dark when the OS itself is dark - upstream's SetDarkMode/SetLightMode
        //pair is strictly binary, so unchecking must force light regardless of the ambient system theme.
        //Simulates that by leaving the app themed dark before Confirm runs.
        [AvaloniaFact]
        public async Task FlagDarkModeCheckBox_UncheckedConfirm_ForcesLightThemeEvenWithAmbientDarkSystem() {
            var app = (App)Avalonia.Application.Current!;
            app.ApplyTheme(ThemeMode.Dark);

            var options = NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]);
            options.FlagDarkMode = ThemeMode.Dark;
            var window = new SettingsWindow(options);

            window.FlagDarkModeCheckBoxControl.IsChecked = false;
            window.SimulateConfirmClick();
            app.ApplyTheme(options.FlagDarkMode);

            Assert.Equal(Avalonia.Styling.ThemeVariant.Light, app.RequestedThemeVariant);
        }
    }
}
