using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Storage;
using Foreman.Mac;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Settings;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //C2c Task 4: SettingsWindow is a non-modal, toolbar-tabbed shell now - Presets and Enabled Objects keep
    //their old content and behavior (just reachable through their own tab UserControl), the old single
    //Graph Options tab splits across General/Advanced/Annotations/Appearance, and there's no more Confirm/
    //Cancel: every widget writes straight through SettingsViewModel. MainWindow-level live-apply (save/
    //resolve debounce, preset-switch reload cascade) is MainWindowSettingsDialogTests.cs's job, not this
    //file's - this file covers the window and its tabs in isolation.
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

        //--- Tab shell -----------------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task TabButtons_ArePresetsEnabledObjectsGeneralAdvancedAnnotationsAppearanceInOrder() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            window.Show(); //button templates (and so their Content's visual children) only realize once rooted

            var labels = window.TabButtonControls
                .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text)
                .ToList();

            Assert.Equal(["Presets", "Enabled Objects", "General", "Advanced", "Annotations", "Appearance"], labels);
        }

        [AvaloniaFact]
        public async Task TabButtons_EachCarriesAPathIconBoundToItsOwnGlyph() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            window.Show();

            foreach (Button button in window.TabButtonControls)
                Assert.NotNull(button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().FirstOrDefault()?.Data);
        }

        [AvaloniaFact]
        public async Task SimulateTabClick_SetsActiveClassOnThatButtonOnlyAndSwapsTabBodyContent() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));

            window.SimulateTabClick(2); //General

            for (int i = 0; i < window.TabButtonControls.Count; i++)
                Assert.Equal(i == 2, window.TabButtonControls[i].Classes.Contains("active"));
            Assert.Same(window.GeneralTabControl, window.TabBodyControl.Content);
            Assert.Equal(2, window.ActiveTabIndex);
        }

        [AvaloniaFact]
        public async Task FooterText_IsForemanTwoBulletVersionedDisplay() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));

            Assert.Equal("Foreman 2 · " + AppVersion.VersionedDisplay, window.FooterTextControl.Text);
        }

        //R9/contract #modals: SettingsWindow is non-modal - Show(owner) must never disable the owner the way
        //ShowDialog would.
        [AvaloniaFact]
        public async Task Show_LeavesTheOwnerWindowEnabledAndVisible() {
            var owner = new Window();
            owner.Show();
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));

            window.Show(owner);

            Assert.True(owner.IsEnabled);
            Assert.True(window.IsVisible);
        }

        //R5: closing hides nothing behind it and needs no Confirm - Esc/⌘W both just close this instance
        //(MainWindow.ShowSettings hands out a fresh one next time).
        [AvaloniaFact]
        public async Task EscapeKey_ClosesTheWindow() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            window.Show();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.True(closed);
        }

        //Property-coverage regression (brief's "nothing lost in the split"): every AppSettings field the OLD
        //Graph Options tab bound (git show HEAD:src/Foreman.Mac/Views/SettingsWindow.axaml) must still have a
        //bound control on one of the four tabs that replaced it.
        [AvaloniaFact]
        public async Task EveryOldGraphOptionsAppSettingsField_HasABoundControlOnOneOfTheFourReplacementTabs() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            GeneralTab general = window.GeneralTabControl;
            AdvancedTab advanced = window.AdvancedTabControl;
            AnnotationsTab annotations = window.AnnotationsTabControl;
            AppearanceTab appearance = window.AppearanceTabControl;

            var controlByAppSettingsProperty = new Dictionary<string, object?> {
                [nameof(AppSettings.QualitySteps)] = general.QualityStepsInputControl,
                [nameof(AppSettings.ShowRecipeToolTip)] = general.ShowNodeRecipeCheckBoxControl,
                [nameof(AppSettings.RoundAssemblerCount)] = general.RoundAssemblerCountCheckBoxControl,
                [nameof(AppSettings.DefaultAssemblerOption)] = general.AssemblerSelectorStyleDropDownControl,
                [nameof(AppSettings.DefaultModuleOption)] = general.ModuleSelectorStyleDropDownControl,
                [nameof(AppSettings.DefaultNodeDirection)] = general.NodeDirectionDropDownControl,
                [nameof(AppSettings.SmartNodeDirection)] = general.SmartNodeDirectionCheckBoxControl,
                [nameof(AppSettings.SimplePassthroughNodes)] = general.SimplePassthroughNodesCheckBoxControl,

                [nameof(AppSettings.EnableExtraProductivityForNonMiners)] = advanced.ShowProductivityBonusOnAllCheckBoxControl,
                [nameof(AppSettings.ShowUnavailable)] = advanced.ShowUnavailablesCheckBoxControl,
                [nameof(AppSettings.UseRecipeBWfilters)] = advanced.LoadBarrelingCheckBoxControl,
                [nameof(AppSettings.LowPriorityPower)] = advanced.LowPriorityPowerInputControl,
                [nameof(AppSettings.PullConsumerNodes)] = advanced.PullConsumerNodesCheckBoxControl,
                [nameof(AppSettings.PullConsumerNodesPower)] = advanced.PullConsumerNodesPowerInputControl,

                [nameof(AppSettings.ShowErrorArrows)] = annotations.ErrorArrowsCheckBoxControl,
                [nameof(AppSettings.ShowWarningArrows)] = annotations.WarningArrowsCheckBoxControl,
                [nameof(AppSettings.ShowDisconnectedArrows)] = annotations.DisconnectedArrowsCheckBoxControl,
                [nameof(AppSettings.ShowOUSuppliedArrows)] = annotations.OUSuppliedArrowsCheckBoxControl,
                [nameof(AppSettings.FlagOUSuppliedNodes)] = annotations.FlagOUSupplyNodesCheckBoxControl,

                [nameof(AppSettings.LevelOfDetail)] = appearance.LowLodRadioButtonControl,
                [nameof(AppSettings.NodeCountForSimpleView)] = appearance.NodeCountForSimpleViewInputControl,
                [nameof(AppSettings.IconsSize)] = appearance.IconsSizeInputControl,
                [nameof(AppSettings.ArrowsOnLinks)] = appearance.ArrowsOnLinksCheckBoxControl,
                [nameof(AppSettings.DynamicLineWidth)] = appearance.DynamicLWCheckBoxControl,
                [nameof(AppSettings.AbbreviateSciPacks)] = appearance.AbbreviateSciPackCheckBoxControl,
                [nameof(AppSettings.FlagDarkMode)] = appearance.FlagDarkModeDropDownControl,
                [nameof(AppSettings.UiScale)] = appearance.UiScaleDropDownControl,
            };

            //LevelOfDetail is one AppSettings field but three radio buttons - checked alongside its primary
            //entry above rather than a second dictionary key.
            Assert.NotNull(appearance.MediumLodRadioButtonControl);
            Assert.NotNull(appearance.HighLodRadioButtonControl);

            foreach ((string appSettingsProperty, object? control) in controlByAppSettingsProperty)
                Assert.True(control is not null, $"{appSettingsProperty} has no bound control on any of General/Advanced/Annotations/Appearance.");
        }

        //--- Current preset label + preset list contents ------------------------------------------------

        [AvaloniaFact]
        public async Task PresetListBox_ExcludesActivePresetAndShowsCurrentPresetLabel() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current, other]));

            Assert.Equal(VanillaPresetName, window.PresetsTabControl.CurrentPresetLabelControl.Text);
            var listed = window.PresetsTabControl.PresetListBoxControl.ItemsSource!.Cast<Preset>().ToList();
            Assert.DoesNotContain(current, listed);
            Assert.Contains(other, listed);
        }

        [AvaloniaFact]
        public async Task CurrentPresetLabel_Click_ClearsPresetListSelection() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current, other]));
            window.PresetsTabControl.PresetListBoxControl.SelectedItem = other;

            window.SimulateCurrentPresetLabelClick();

            Assert.Null(window.PresetsTabControl.PresetListBoxControl.SelectedItem);
        }

        //Minor#2 (final fix wave, upstream SettingsForm.cs:263-265): bold marks "this is the active preset",
        //so it drops once the list picks out a different candidate and returns once that pick is cleared.
        [AvaloniaFact]
        public async Task CurrentPresetLabel_BoldOnlyWhenPresetListSelectionIsEmpty() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current, other]));
            Assert.Equal(FontWeight.Bold, window.PresetsTabControl.CurrentPresetLabelControl.FontWeight);

            window.PresetsTabControl.PresetListBoxControl.SelectedItem = other;
            Assert.Equal(FontWeight.Normal, window.PresetsTabControl.CurrentPresetLabelControl.FontWeight);

            window.PresetsTabControl.PresetListBoxControl.SelectedItem = null;
            Assert.Equal(FontWeight.Bold, window.PresetsTabControl.CurrentPresetLabelControl.FontWeight);
        }

        [AvaloniaFact]
        public async Task ModSelectionBox_ReflectsSelectedPresetModList() {
            var current = new Preset(VanillaPresetName, true, true);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current]));

            var mods = window.PresetsTabControl.ModSelectionBoxControl.ItemsSource?.Cast<string>().ToList();

            Assert.Equal(VanillaPresetModList, mods);
        }

        //--- Preset switch via double-click / select menu item --------------------------------------------
        //R5: picking a preset swaps the whole live DataCache, so it no longer closes with a bool result -
        //it sets SelectedPreset and asks MainWindow (via ReloadRequested) to close/reload/reopen.

        [AvaloniaFact]
        public async Task PresetListBox_DoubleClickPreset_SetsSelectedPresetAndRequestsReload() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var options = NewOptions(await GetCacheAsync(), [current, other]);
            var window = new SettingsWindow(options);
            int reloadRequests = 0;
            window.ReloadRequested += () => reloadRequests++;

            window.SimulateDoubleClickPreset(other);

            Assert.Same(other, options.SelectedPreset);
            Assert.Equal(1, reloadRequests);
        }

        [AvaloniaFact]
        public async Task SelectPresetMenuItem_Click_SetsSelectedPresetAndRequestsReload() {
            var current = new Preset(VanillaPresetName, true, true);
            var other = new Preset(SpaceAgePresetName, false, false);
            var options = NewOptions(await GetCacheAsync(), [current, other]);
            var window = new SettingsWindow(options);
            int reloadRequests = 0;
            window.ReloadRequested += () => reloadRequests++;

            window.SimulateSelectPresetMenuItemClick(other);

            Assert.Same(other, options.SelectedPreset);
            Assert.Equal(1, reloadRequests);
        }

        //--- Dynamic right-click menu captions -------------------------------------------------------------

        [AvaloniaFact]
        public async Task ContextMenu_OrdinaryPreset_ShowsUseThisPresetAndDeleteThisPreset() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            var ordinary = new Preset("Some Other Preset", false, false);

            window.UpdatePresetMenuCaptionsFor(ordinary);

            Assert.Equal("Use This Preset", window.PresetsTabControl.SelectPresetMenuItemControl.Header);
            Assert.True(window.PresetsTabControl.SelectPresetMenuItemControl.IsEnabled);
            Assert.Equal("Delete This Preset", window.PresetsTabControl.DeletePresetMenuItemControl.Header);
            Assert.True(window.PresetsTabControl.DeletePresetMenuItemControl.IsEnabled);
        }

        [AvaloniaFact]
        public async Task ContextMenu_DefaultPreset_ShowsDisabledDefaultPresetCaption() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(SpaceAgePresetName, true, false)]));
            var defaultPreset = new Preset(VanillaPresetName, false, true);

            window.UpdatePresetMenuCaptionsFor(defaultPreset);

            Assert.Equal("Use This Preset", window.PresetsTabControl.SelectPresetMenuItemControl.Header);
            Assert.Equal("Default Preset", window.PresetsTabControl.DeletePresetMenuItemControl.Header);
            Assert.False(window.PresetsTabControl.DeletePresetMenuItemControl.IsEnabled);
        }

        //Defensive branch: unreachable via the real UI (the active preset never appears in PresetListBox),
        //ported anyway since upstream keeps the same branch (reference SettingsForm.cs:277-291).
        [AvaloniaFact]
        public async Task ContextMenu_CurrentlySelectedPreset_ShowsDisabledCurrentPresetCaption() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            var currentlySelected = new Preset("Weird Preset", true, false);

            window.UpdatePresetMenuCaptionsFor(currentlySelected);

            Assert.Equal("Current Preset", window.PresetsTabControl.SelectPresetMenuItemControl.Header);
            Assert.False(window.PresetsTabControl.SelectPresetMenuItemControl.IsEnabled);
            Assert.Equal("Delete This Preset", window.PresetsTabControl.DeletePresetMenuItemControl.Header);
            Assert.False(window.PresetsTabControl.DeletePresetMenuItemControl.IsEnabled);
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
            Assert.DoesNotContain(doomed, window.PresetsTabControl.PresetListBoxControl.ItemsSource!.Cast<Preset>());
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

        //--- Delete preset db cleanup (Task 7 fix round) --------------------------------------------------

        private static ForemanDbContext OpenSharedDb() =>
            ForemanDbContext.Open(StoragePaths.DbPath(PresetProcessor.ResolveUserDataDirectory()));

        private static async Task SeedDbPresetAsync(string presetName, string pjsonText, string iconName, byte colorSeed) {
            string datPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dat");
            using var bitmap = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
                canvas.Clear(new SKColor(colorSeed, colorSeed, colorSeed, 255));
            var icons = new Dictionary<string, IconColorPair> {
                [iconName] = new IconColorPair(bitmap, System.Drawing.Color.FromArgb(255, colorSeed, colorSeed, colorSeed)),
            };
            await ForemanIconCacheFile.WriteAsync(datPath, icons);

            using ForemanDbContext db = OpenSharedDb();
            PresetStore.Import(db, presetName, pjsonText, null);
            IconStore.ImportFoic(db, presetName, datPath);
            File.Delete(datPath);
        }

        private static void DeleteDbRowsForTestPreset(string presetName) {
            using ForemanDbContext db = OpenSharedDb();
            if (db.Presets.Find(presetName) is PresetDataRow row)
                db.Presets.Remove(row);
            db.PresetIcons.RemoveRange(db.PresetIcons.Where(r => r.PresetName == presetName));
            db.SaveChanges();
        }

        [AvaloniaFact]
        public async Task DeletePresetMenuItem_Confirmed_RemovesDbRowsAndOrphanedBlobs_LeavesOtherPresetsIntact() {
            string presetToDelete = "Delete-Db-Doomed-" + Guid.NewGuid().ToString("N");
            string presetToKeep = "Delete-Db-Kept-" + Guid.NewGuid().ToString("N");
            try {
                await SeedDbPresetAsync(presetToDelete, "{\"marker\":\"doomed\"}", "doomed-icon", 41);
                await SeedDbPresetAsync(presetToKeep, "{\"marker\":\"kept\"}", "kept-icon", 42);

                string deletedBlobHash;
                string keptBlobHash;
                using (ForemanDbContext seedDb = OpenSharedDb()) {
                    deletedBlobHash = seedDb.PresetIcons.Single(r => r.PresetName == presetToDelete).Sha256;
                    keptBlobHash = seedDb.PresetIcons.Single(r => r.PresetName == presetToKeep).Sha256;
                }

                var current = new Preset(VanillaPresetName, true, true);
                var doomed = new Preset(presetToDelete, false, false);
                var options = NewOptions(await GetCacheAsync(), [current, doomed]);
                var window = new SettingsWindow(options) { DeleteConfirmationStub = _ => Task.FromResult(true) };

                await window.SimulateDeletePresetMenuItemClickAsync(doomed);

                using ForemanDbContext verifyDb = OpenSharedDb();
                Assert.Null(verifyDb.Presets.Find(presetToDelete));
                Assert.Empty(verifyDb.PresetIcons.Where(r => r.PresetName == presetToDelete));
                Assert.Null(verifyDb.IconBlobs.Find(deletedBlobHash));

                Assert.NotNull(verifyDb.Presets.Find(presetToKeep));
                Assert.Single(verifyDb.PresetIcons.Where(r => r.PresetName == presetToKeep));
                Assert.NotNull(verifyDb.IconBlobs.Find(keptBlobHash));
            } finally {
                DeleteDbRowsForTestPreset(presetToDelete);
                DeleteDbRowsForTestPreset(presetToKeep);
            }
        }

        //Pins the staleness regression the review found: before this fix, DeletePresetAsync cleared only
        //the disk trio, so a different preset hand-placed under the deleted name still read back the first
        //preset's cached db content instead of its own - silently wrong data, not a crash.
        [AvaloniaFact]
        public async Task DeletePresetMenuItem_Confirmed_ThenDifferentPresetHandPlacedUnderSameName_ServesTheNewContent() {
            string presetName = "Delete-Staleness-" + Guid.NewGuid().ToString("N");
            string basePath = PresetProcessor.GetUserPresetPath(presetName, "");
            try {
                await SeedDbPresetAsync(presetName, "{\"marker\":\"old\"}", "old-icon", 51);

                var current = new Preset(VanillaPresetName, true, true);
                var doomed = new Preset(presetName, false, false);
                var options = NewOptions(await GetCacheAsync(), [current, doomed]);
                var window = new SettingsWindow(options) { DeleteConfirmationStub = _ => Task.FromResult(true) };
                await window.SimulateDeletePresetMenuItemClickAsync(doomed);

                Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
                File.WriteAllText(basePath + ".pjson", "{\"marker\":\"new\"}");
                using (var newBitmap = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul)) {
                    using (var canvas = new SKCanvas(newBitmap))
                        canvas.Clear(new SKColor(52, 52, 52, 255));
                    var newIcons = new Dictionary<string, IconColorPair> {
                        ["new-icon"] = new IconColorPair(newBitmap, System.Drawing.Color.FromArgb(255, 52, 52, 52)),
                    };
                    await ForemanIconCacheFile.WriteAsync(basePath + ".dat", newIcons);
                }

                JsonObject loaded = PresetProcessor.PrepPreset(new Preset(presetName, false, false));

                Assert.Equal("new", loaded["marker"]!.GetValue<string>());
                var iconNames = PresetProcessor.GetPresetIconEntries(new Preset(presetName, false, false)).Select(i => i.Name).ToList();
                Assert.Contains("new-icon", iconNames);
                Assert.DoesNotContain("old-icon", iconNames);
            } finally {
                foreach (string extension in new[] { ".pjson", ".json", ".dat" }) {
                    string path = basePath + extension;
                    if (File.Exists(path))
                        File.Delete(path);
                }
                DeleteDbRowsForTestPreset(presetName);
            }
        }

        //--- Import (reference io-reference.md §4/§7) -----------------------------------------------------

        [AvaloniaFact]
        public async Task ImportPresetButton_Click_OverwroteTheActivePreset_SetsSelectedPresetAndRequestsReload() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options);
            window.ImportPresetDialogStub = w => { w.SimulateImportSuccess(VanillaPresetName); return Task.CompletedTask; };
            int reloadRequests = 0;
            window.ReloadRequested += () => reloadRequests++;

            await window.SimulateImportPresetClickAsync();

            Assert.Equal(VanillaPresetName, options.SelectedPreset?.Name);
            Assert.Equal(1, reloadRequests);
        }

        [AvaloniaFact]
        public async Task ImportPresetButton_Click_NewPresetName_AddsItToTheListAndOffersToSwitch() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options) { ImportSwitchConfirmationStub = () => Task.FromResult(false) };
            window.ImportPresetDialogStub = w => { w.SimulateImportSuccess("Brand New Preset"); return Task.CompletedTask; };
            int reloadRequests = 0;
            window.ReloadRequested += () => reloadRequests++;

            await window.SimulateImportPresetClickAsync();

            Assert.Contains(options.Presets!, p => p.Name == "Brand New Preset");
            Assert.Contains(window.PresetsTabControl.PresetListBoxControl.ItemsSource!.Cast<Preset>(), p => p.Name == "Brand New Preset");
            Assert.Equal(0, reloadRequests); //declined the switch - Settings stays open, no reload
        }

        [AvaloniaFact]
        public async Task ImportPresetButton_Click_NewPresetName_SwitchAccepted_SelectsItAndRequestsReload() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options) { ImportSwitchConfirmationStub = () => Task.FromResult(true) };
            window.ImportPresetDialogStub = w => { w.SimulateImportSuccess("Brand New Preset"); return Task.CompletedTask; };
            int reloadRequests = 0;
            window.ReloadRequested += () => reloadRequests++;

            await window.SimulateImportPresetClickAsync();

            Assert.Equal("Brand New Preset", options.SelectedPreset?.Name);
            Assert.Equal(1, reloadRequests);
        }

        [AvaloniaFact]
        public async Task ImportPresetButton_Click_CancelledDialog_LeavesOptionsUnchanged() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options);
            window.ImportPresetDialogStub = _ => Task.CompletedTask; //NewPresetName stays "" - nothing simulated
            int reloadRequests = 0;
            window.ReloadRequested += () => reloadRequests++;

            await window.SimulateImportPresetClickAsync();

            Assert.Single(options.Presets!);
            Assert.Equal(0, reloadRequests);
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

        //--- Enabled Objects tab: sub-tab shell -------------------------------------------------------------
        //R5: TransitioningContentControl only wires its Content into the logical tree (and so propagates
        //DataContext to it) once the control tree is actually rooted - Show() first, matching what a real
        //click always gets for free (the window is already on screen by the time anyone can click a tab).
        //Enabled Objects also now writes straight onto DataObject.Enabled on toggle (R5 live apply) - the
        //old Options.EnabledObjects buffer always started empty per test, so resetting every object back to
        //disabled here reproduces that same starting condition and keeps these tests order-independent
        //against each other and against whatever a prior test left enabled on the shared fixture cache.
        private static SettingsWindow NewEnabledObjectsWindow(DataCache cache) {
            foreach (IRecipe recipe in cache.Recipes.Values) recipe.Enabled = false;
            foreach (IAssembler assembler in cache.Assemblers.Values) assembler.Enabled = false;
            foreach (IBeacon beacon in cache.Beacons.Values) beacon.Enabled = false;
            foreach (IModule module in cache.Modules.Values) module.Enabled = false;
            foreach (IQuality quality in cache.Qualities.Values) quality.Enabled = false;

            var window = new SettingsWindow(NewOptions(cache, [new Preset(VanillaPresetName, true, true)]));
            window.Show();
            window.SimulateTabClick(1);
            Dispatcher.UIThread.RunJobs(); //completes the virtualizing panel's pending realization before any test reads it
            return window;
        }

        [AvaloniaFact]
        public async Task EnabledObjectsTabControl_HasSevenSubTabsInOrder() {
            var window = NewEnabledObjectsWindow(await GetCacheAsync());

            var headers = window.EnabledObjectsTabControl.FindControl<TabControl>("EnabledObjectsTabControl")!.Items
                .OfType<TabItem>().Select(t => t.Header).ToList();

            Assert.Equal(
                new object?[] { "Assemblers", "Miners", "Power", "Beacons", "Modules", "Recipes", "Qualities" },
                headers);
        }

        [AvaloniaFact]
        public async Task SubTabLists_ContainCountsMatchingTheFixtureCacheDictionaries() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.EnabledObjectsTabControl.ShowUnavailablesFilterCheckBoxControl.IsChecked = true;

            int expectedAssemblers = cache.Assemblers.Values.Count(a => a.EntityType == EntityType.Assembler);
            int expectedMiners = cache.Assemblers.Values.Count(a => a.EntityType is EntityType.Miner or EntityType.OffshorePump);
            int expectedPower = cache.Assemblers.Values.Count(a => a.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor);

            Assert.Equal(expectedAssemblers, window.EnabledObjectsTabControl.AssemblerListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(expectedMiners, window.EnabledObjectsTabControl.MinerListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(expectedPower, window.EnabledObjectsTabControl.PowerListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(cache.Beacons.Count, window.EnabledObjectsTabControl.BeaconListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(cache.Modules.Count, window.EnabledObjectsTabControl.ModuleListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(cache.Recipes.Count, window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<object>().Count());
            Assert.Equal(cache.Qualities.Count, window.EnabledObjectsTabControl.QualityListViewControl.ItemsSource!.Cast<object>().Count());
        }

        [AvaloniaFact]
        public async Task RecipeListView_WithBoundedHeight_RealizesFewerContainersThanTotalItems() {
            var cache = await GetCacheAsync();
            Assert.True(cache.Recipes.Count > 50, "Fixture cache should carry enough recipes for virtualization to matter.");
            var window = NewEnabledObjectsWindow(cache);
            window.EnabledObjectsTabControl.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            window.Show();

            int shown = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<object>().Count();
            int realized = window.EnabledObjectsTabControl.RecipeListViewControl.GetVisualDescendants().OfType<ListBoxItem>().Count();
            Assert.True(realized > 0, "Expected at least one realized row.");
            Assert.True(realized < shown, $"Expected virtualization to realize fewer than {shown} shown rows, got {realized}.");
        }

        //--- Enabled Objects tab: filter row -----------------------------------------------------------------

        [AvaloniaFact]
        public async Task FilterRow_DefaultState_HidesUnavailableRecipes() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);

            int shown = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<object>().Count();

            Assert.Equal(cache.Recipes.Values.Count(r => r.Available), shown);
        }

        [AvaloniaFact]
        public async Task ShowUnavailablesFilterCheckBox_Checked_RevealsUnavailableRecipes() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);

            window.EnabledObjectsTabControl.ShowUnavailablesFilterCheckBoxControl.IsChecked = true;

            Assert.Equal(cache.Recipes.Count, window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<object>().Count());
        }

        [AvaloniaFact]
        public async Task FilterTextBox_FiltersRecipesByNameOrdinalIgnoreCase() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            IRecipe target = cache.Recipes.Values.First(r => r.Available);
            string needle = target.FriendlyName[..Math.Min(4, target.FriendlyName.Length)].ToUpperInvariant();

            window.EnabledObjectsTabControl.FilterTextBoxControl.Text = needle;

            var shown = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().ToList();
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
            window.EnabledObjectsTabControl.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();

            //Default filtered state only (available rows) - toggling ShowUnavailablesFilterCheckBox rebinds
            //RecipeObjects to a fresh list (FRESH-LIST rule) and needs a layout pump most other tests in
            //this file don't; Dispatcher.UIThread.RunJobs() is process-wide (reference global-constraints.md's
            //headless-facts note) and was observed to interleave with sibling tests' own pending windows, so
            //this test proves the unavailable-row contrast against a standalone row instead of the live UI.
            var rows = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().ToList();
            EnabledObjectsListItem availableRow = rows.First(r => r.DataObject.Available);
            ContrastAssert.Readable(availableRow.Foreground, availableRow.RowBackground);

            IRecipe? unavailableRecipe = cache.Recipes.Values.FirstOrDefault(r => !r.Available);
            if (unavailableRecipe is not null) {
                var unavailableRow = new EnabledObjectsListItem(unavailableRecipe, icon: null);
                ContrastAssert.Readable(unavailableRow.Foreground, unavailableRow.RowBackground);
            }

            //Confirms the XAML binding actually reaches the realized TextBlock, not just the row model.
            TextBlock realizedNameText = window.EnabledObjectsTabControl.RecipeListViewControl.GetVisualDescendants().OfType<TextBlock>()
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
            window.EnabledObjectsTabControl.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            window.Show();

            CheckBox realizedBox = window.EnabledObjectsTabControl.RecipeListViewControl.GetVisualDescendants().OfType<CheckBox>().First();
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
            window.EnabledObjectsTabControl.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            window.Show();

            //Floor is the 24px icon plus the two 1px top/bottom padding pairs (Border's own Padding="4,1" and
            //the ListBox.compact style's Padding="2,1") - 24 + 2 + 2 = 28px, unchanged by the CheckBox's
            //Viewbox: it's sized to 20px, under the icon's 24px, so it was never the row's height driver.
            ListBoxItem realizedRow = window.EnabledObjectsTabControl.RecipeListViewControl.GetVisualDescendants().OfType<ListBoxItem>().First();
            Assert.Equal(28d, realizedRow.Bounds.Height);
        }

        //--- Enabled Objects tab: live check toggle -----------------------------------------------------------
        //R5: a checked row now writes straight onto the live DataObject.Enabled too, not just the Options
        //buffer - both are asserted below.

        [AvaloniaFact]
        public async Task CheckingARecipeRow_AddsItToEnabledObjectsAndEnablesItLiveImmediately() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            var item = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().First();
            Assert.False(item.IsChecked); //NewEnabledObjectsWindow resets the fixture cache to all-disabled
            Assert.DoesNotContain(item.DataObject, window.Options.EnabledObjects);

            item.IsChecked = true;

            Assert.Contains(item.DataObject, window.Options.EnabledObjects);
            Assert.True(item.DataObject.Enabled);
        }

        [AvaloniaFact]
        public async Task UncheckingARecipeRow_RemovesItFromEnabledObjectsAndDisablesItLiveImmediately() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            var item = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().First();
            item.IsChecked = true;

            item.IsChecked = false;

            Assert.DoesNotContain(item.DataObject, window.Options.EnabledObjects);
            Assert.False(item.DataObject.Enabled);
        }

        //--- Enabled Objects tab: selected-rows bulk toggle --------------------------------------------------------

        [AvaloniaFact]
        public async Task TogglingOneSelectedRowsCheckbox_AppliesTheSameStateToEveryOtherSelectedRow() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.EnabledObjectsTabControl.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            window.Show();
            window.EnabledObjectsTabControl.RecipeListViewControl.Focus();
            CanvasKeys.KeyDownOn(window.EnabledObjectsTabControl.RecipeListViewControl, Key.A, KeyModifiers.Meta);
            var rows = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().ToList();

            rows[0].IsChecked = true;

            Assert.All(rows, row => Assert.True(row.IsChecked));
            Assert.True(window.Options.EnabledObjects.SetEquals(rows.Select(r => r.DataObject)));
        }

        [AvaloniaFact]
        public async Task TogglingARowsCheckbox_OutsideTheSelection_OnlyAffectsThatRow() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            var rows = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().ToList();
            window.EnabledObjectsTabControl.RecipeListViewControl.SelectedItems!.Add(rows[0]);
            window.EnabledObjectsTabControl.RecipeListViewControl.SelectedItems!.Add(rows[1]);

            rows[2].IsChecked = true;

            Assert.True(rows[2].IsChecked);
            Assert.False(rows[0].IsChecked);
            Assert.False(rows[1].IsChecked);
            Assert.DoesNotContain(rows[0].DataObject, window.Options.EnabledObjects);
            Assert.DoesNotContain(rows[1].DataObject, window.Options.EnabledObjects);
        }

        //--- Enabled Objects tab: Enable All -------------------------------------------------------------------

        [AvaloniaFact]
        public async Task EnableAllButton_Click_ClearsThenRepopulatesFromEveryAvailableCategoryPlusPlayerAssembler() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.Options.EnabledObjects.Add(cache.Recipes.Values.First()); //stale entry Enable All must clear away first

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

            Assert.True(window.Options.EnabledObjects.SetEquals(expected));
            Assert.All(expected, o => Assert.True(o.Enabled));
        }

        //--- Enabled Objects tab: Cmd+A selects without checking -------------------------------------------------

        [AvaloniaFact]
        public async Task CmdA_OnActiveSubTab_SelectsEveryRowWithoutCheckingThem() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.EnabledObjectsTabControl.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            window.Show();
            window.EnabledObjectsTabControl.RecipeListViewControl.Focus();

            CanvasKeys.KeyDownOn(window.EnabledObjectsTabControl.RecipeListViewControl, Key.A, KeyModifiers.Meta);

            int totalRows = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<object>().Count();
            Assert.Equal(totalRows, window.EnabledObjectsTabControl.RecipeListViewControl.SelectedItems!.Count);
            Assert.Empty(window.Options.EnabledObjects);
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): Ctrl+A on
        //Linux does the same thing Cmd+A does on macOS, via the UseIsMacOs seam.
        [AvaloniaFact]
        public async Task CtrlA_OnActiveSubTab_OnLinux_SelectsEveryRowWithoutCheckingThem() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.EnabledObjectsTabControl.FindControl<TabControl>("EnabledObjectsTabControl")!.SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            window.Show();
            window.EnabledObjectsTabControl.RecipeListViewControl.Focus();

            CanvasKeys.KeyDownOn(window.EnabledObjectsTabControl.RecipeListViewControl, Key.A, KeyModifiers.Control);

            int totalRows = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<object>().Count();
            Assert.Equal(totalRows, window.EnabledObjectsTabControl.RecipeListViewControl.SelectedItems!.Count);
            Assert.Empty(window.Options.EnabledObjects);
        }

        //--- Enabled Objects tab: Load from save (reference io-reference.md §5) -------------------------------

        //Drives SaveFileLoadSheet's own seams through LoadFromSaveDialogStub instead of a real SheetHost
        //presentation - RunAsync is exactly what the sheet's Loaded handler would otherwise trigger.
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

        //Drives SciencePacksSheet's own Accepted flag through AssignFromSciencePacksDialogStub instead of
        //a real SheetHost presentation - mirrors LoadEnabledFromSaveButton's stub convention above.
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

            var recipeItem = window.EnabledObjectsTabControl.RecipeListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().First();
            var assemblerItem = window.EnabledObjectsTabControl.AssemblerListViewControl.ItemsSource!.Cast<EnabledObjectsListItem>().First();

            Assert.IsType<Image>(recipeItem.TooltipContent);
            Assert.Null(assemblerItem.TooltipContent);
        }

        //--- General/Advanced/Annotations/Appearance tabs: standalone binding + wiring checks ------------------
        //These four are plain SettingsViewModel-bound UserControls (reference task-4-brief.md's R7 split) -
        //no window services involved, so they're exercised directly with an explicit DataContext rather than
        //through the full SettingsWindow (which only attaches the active tab - see the Enabled Objects
        //section's own comment on that).

        [AvaloniaFact]
        public void GeneralTab_SelectorDropDowns_CarryTheirStyleNameLists() {
            var tab = new GeneralTab { DataContext = new SettingsViewModel() };

            Assert.Equal(AssemblerSelector.StyleNames, tab.AssemblerSelectorStyleDropDownControl.ItemsSource);
            Assert.Equal(ModuleSelector.StyleNames, tab.ModuleSelectorStyleDropDownControl.ItemsSource);
        }

        [AvaloniaFact]
        public void GeneralTab_NodeDirectionDropDown_OffersUpThenDown() {
            var tab = new GeneralTab { DataContext = new SettingsViewModel() };

            var items = tab.NodeDirectionDropDownControl.Items.OfType<ComboBoxItem>().Select(i => i.Content).ToList();

            Assert.Equal(["Up (default)", "Down"], items);
        }

        [AvaloniaFact]
        public void GeneralTab_QualityStepsInput_BindsTwoWayToViewModel() {
            var vm = new SettingsViewModel();
            var tab = new GeneralTab { DataContext = vm };

            tab.QualityStepsInputControl.Value = 12m;
            Assert.Equal(12m, vm.QualityStepsValue);

            vm.QualityStepsValue = 7m;
            Assert.Equal(7m, tab.QualityStepsInputControl.Value);
        }

        //Contract: docs/panels-reference.md:222/upstream SettingsForm.Designer.cs:1457,1460,1619-1633. The
        //caption stays upstream's verbatim stale "2 default" text even though the real default (AppSettings.
        //LowPriorityPower) is 4 - this widget is a straight text/range port, not a corrected re-derivation.
        [AvaloniaFact]
        public void AdvancedTab_LowPriorityPowerCaptionAndRanges_MatchUpstreamVerbatim() {
            var tab = new AdvancedTab { DataContext = new SettingsViewModel() };
            var host = new Window { Content = tab };
            host.Show(); //GetVisualDescendants needs the tab's own template rooted to realize its content

            TextBlock caption = tab.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text != null && t.Text.StartsWith("Low priority multiplier", StringComparison.Ordinal));
            Assert.Equal("Low priority multiplier   (10^n, 2 default):", caption.Text);
            Assert.Equal(0.2m, tab.LowPriorityPowerInputControl.Increment);
        }

        [AvaloniaFact]
        public void AppearanceTab_NodeCountForSimpleViewInput_MatchesUpstreamRange() {
            var tab = new AppearanceTab { DataContext = new SettingsViewModel() };

            Assert.Equal(25m, tab.NodeCountForSimpleViewInputControl.Increment);
            Assert.Equal(2000m, tab.NodeCountForSimpleViewInputControl.Maximum);
        }

        //LOD's mutual exclusion is SettingsViewModel's job (OnXxxLodCheckedChanged); this just pins the
        //three RadioButtons actually share one GroupName, or that VM-level exclusion has nothing to bind to.
        [AvaloniaFact]
        public void AppearanceTab_LevelOfDetailRadioButtons_ShareOneGroupName() {
            var tab = new AppearanceTab { DataContext = new SettingsViewModel() };

            Assert.Equal(tab.LowLodRadioButtonControl.GroupName, tab.MediumLodRadioButtonControl.GroupName);
            Assert.Equal(tab.LowLodRadioButtonControl.GroupName, tab.HighLodRadioButtonControl.GroupName);
        }

        //UI-level pin: the ComboBox itself carries exactly the three ThemeMode values, in enum order.
        [AvaloniaFact]
        public void AppearanceTab_FlagDarkModeDropDown_ShowsExactlyTheThreeThemeModes() {
            var tab = new AppearanceTab { DataContext = new SettingsViewModel() };

            Assert.Equal<object>([ThemeMode.System, ThemeMode.Light, ThemeMode.Dark],
                tab.FlagDarkModeDropDownControl.ItemsSource!.Cast<object>());
        }

        //UI-level pin: the ComboBox carries exactly the five discrete steps, in ascending order.
        [AvaloniaFact]
        public void AppearanceTab_UiScaleDropDown_ShowsExactlyTheFiveSteps() {
            var tab = new AppearanceTab { DataContext = new SettingsViewModel() };

            Assert.Equal<object>(["100%", "110%", "125%", "150%", "200%"],
                tab.UiScaleDropDownControl.ItemsSource!.Cast<object>());
        }

        [AvaloniaFact]
        public void AnnotationsTab_ArrowCheckBoxes_BindTwoWayToViewModel() {
            var vm = new SettingsViewModel();
            var tab = new AnnotationsTab { DataContext = vm };

            tab.ErrorArrowsCheckBoxControl.IsChecked = true;
            tab.FlagOUSupplyNodesCheckBoxControl.IsChecked = true;

            Assert.True(vm.ErrorArrowsChecked);
            Assert.True(vm.FlagOUSupplyNodesChecked);
        }
    }
}
