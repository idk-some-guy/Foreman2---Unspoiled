using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //C2c Task 4: Settings applies live now - there's no more Confirm/Cancel cascade to test (that whole
    //buffer-everything-then-commit-once model is gone). This covers MainWindow.ShowSettings's own seams:
    //seeding the new window from live AppSettings, reusing the one instance, and SettingsSession's
    //save/resolve reaction to a real property change (theme/scale immediacy is SettingsSessionTests.cs's
    //job, not this file's - it needs no live graph).
    public class MainWindowSettingsDialogTests {
        private static DataCache MinimalCache() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;
            return cache;
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static MainWindow NewWindowReadyForSettings(AppSettings settings) {
            var window = new MainWindow();
            window.Show();
            window.DataCache = MinimalCache();
            window.Settings = settings;
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();
            return window;
        }

        //Drives ShowSettings through its stub seam (skips the real Show(this)) and hands back the
        //fully-wired SettingsWindow instance.
        private static SettingsWindow OpenSettings(MainWindow window) {
            SettingsWindow? captured = null;
            window.SettingsDialogStub = w => captured = w;
            window.ShowSettings();
            return captured!;
        }

        [AvaloniaFact]
        public void ShowSettings_SeedsGeneralTabFromTheLiveAppSettingsInstance() {
            var settings = new AppSettings { QualitySteps = 7, SmartNodeDirection = false };
            var window = NewWindowReadyForSettings(settings);

            SettingsWindow opened = OpenSettings(window);

            Assert.Equal(7m, opened.ViewModel.QualityStepsValue);
            Assert.False(opened.ViewModel.SmartNodeDirectionChecked);
        }

        //R5: every change writes onto AppSettings at once; the save itself coalesces 300ms - FlushAsync is
        //the deterministic seam, no real timer wait needed.
        [AvaloniaFact]
        public async Task TogglingSmartDirection_ChangesSettingsAtOnce_AndSavesExactlyOnceAfterFlush() {
            var settings = new AppSettings { SmartNodeDirection = true };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);

            opened.ViewModel.SmartNodeDirectionChecked = false;

            Assert.False(settings.SmartNodeDirection);
            Assert.Equal(0, window.CurrentSettingsSession!.SaveCallCount);

            await window.CurrentSettingsSession!.FlushAsync();

            Assert.Equal(1, window.CurrentSettingsSession!.SaveCallCount);
        }

        //R5's named solver-affecting field: a re-solve is requested (debounced) and, once flushed, actually
        //runs against the newly-committed value.
        [AvaloniaFact]
        public async Task ChangingDefaultAssemblerOption_RequestsExactlyOneReSolveAfterFlush() {
            var settings = new AppSettings { DefaultAssemblerOption = AssemblerSelector.Style.Worst };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);

            int solveCount = 0;
            AssemblerSelector.Style styleAtSolve = AssemblerSelector.Style.Worst;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => {
                solveCount++;
                styleAtSolve = window.GraphCanvas.Viewer.Graph.AssemblerSelector.DefaultSelectionStyle;
            };

            opened.ViewModel.AssemblerSelectorStyleIndex = (int)AssemblerSelector.Style.Best;
            Assert.Equal(0, window.CurrentSettingsSession!.ResolveCallCount);

            await window.CurrentSettingsSession!.FlushAsync();

            Assert.Equal(1, window.CurrentSettingsSession!.ResolveCallCount);
            Assert.Equal(AssemblerSelector.Style.Best, settings.DefaultAssemblerOption);
            Assert.Equal(1, solveCount);
            Assert.Equal(AssemblerSelector.Style.Best, styleAtSolve); //re-solve must run after the new value is already live
        }

        //Review fix round 1, critical: closing the window used to null the session straight away, dropping
        //whatever save/resolve was still mid-debounce. OnClosing now flushes first - this proves it without
        //the test ever calling FlushAsync itself.
        [AvaloniaFact]
        public async Task ClosingWithinTheCoalesceWindow_StillSavesExactlyOnceWithTheNewValue() {
            var settings = new AppSettings { SmartNodeDirection = true };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);
            SettingsSession session = window.CurrentSettingsSession!;

            opened.ViewModel.SmartNodeDirectionChecked = false; //schedules the 300ms coalesced save, not yet run

            var closed = new TaskCompletionSource();
            opened.Closed += (_, _) => closed.SetResult();
            opened.Close();
            await closed.Task;

            Assert.False(settings.SmartNodeDirection);
            Assert.Equal(1, session.SaveCallCount);
            Assert.False(window.SettingsService!.Load().SmartNodeDirection);
        }

        [AvaloniaFact]
        public async Task ClosingWithAPendingReSolve_StillResolvesExactlyOnceBeforeClosing() {
            var settings = new AppSettings { DefaultAssemblerOption = AssemblerSelector.Style.Worst };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);
            SettingsSession session = window.CurrentSettingsSession!;

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            opened.ViewModel.AssemblerSelectorStyleIndex = (int)AssemblerSelector.Style.Best; //schedules the debounced re-solve, not yet run

            var closed = new TaskCompletionSource();
            opened.Closed += (_, _) => closed.SetResult();
            opened.Close();
            await closed.Task;

            Assert.Equal(1, session.ResolveCallCount);
            Assert.Equal(1, solveCount);
            Assert.Equal(AssemblerSelector.Style.Best, settings.DefaultAssemblerOption);
        }

        //UseRecipeBWfilters changes which recipes even exist, so it can't be live-applied in place -
        //OnSettingsChanged special-cases it to close/reload/reopen instead (reference upstream MainForm.cs
        //413-415's old presetReloaded three-way OR, now split into its own path). With no preset on disk to
        //actually reload (NewTempDir's PresetResolver finds none), HandlePresetReloadAsync's reload branch
        //is a no-op and the whole close+reopen runs synchronously within this call - no wait needed.
        [AvaloniaFact]
        public void TogglingLoadBarreling_ClosesAndReopensAFreshSettingsWindow() {
            var settings = new AppSettings { UseRecipeBWfilters = true };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow first = OpenSettings(window);
            bool firstClosed = false;
            first.Closed += (_, _) => firstClosed = true;

            first.ViewModel.LoadBarrelingChecked = !first.ViewModel.LoadBarrelingChecked;

            Assert.True(firstClosed);
            Assert.False(settings.UseRecipeBWfilters); //LoadBarrelingChecked starts true (UseRecipeBWfilters true); one flip inverts it
            Assert.NotSame(first, window.CurrentSettingsWindow);
        }

        //Phase 7 deferred-minors sweep: the non-live-apply Enabled-Objects sync used to index
        //cache.Assemblers["rocket-silo"] directly, a KeyNotFoundException on any preset without a rocket
        //silo entity. RocketAssembler itself is a synthetic pseudo-assembler DataCacheBootstrap always
        //creates regardless of preset content, so its own null-conditional assignment never short-circuits
        //the indexer away - EnableAll's live write (SettingsWindow.ApplyEnabledObjectsLiveFromOptions) must
        //still guard the same lookup.
        private static DataCache CacheWithRocketAssemblerButNoSilo() {
            DataCache cache = MinimalCache();
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.RocketAssembler = new AssemblerPrototype(cache, "§§a:rocket-assembler", "Rocket", EntityType.Rocket, EnergySource.Void);
            return cache;
        }

        [AvaloniaFact]
        public void EnableAll_OnASiloLessPreset_DoesNotThrow() {
            var settings = new AppSettings();
            var window = new MainWindow();
            window.Show();
            window.DataCache = CacheWithRocketAssemblerButNoSilo();
            window.Settings = settings;
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();
            SettingsWindow opened = OpenSettings(window);

            opened.SimulateEnableAllClick();

            Assert.False(window.DataCache!.RocketAssembler!.Enabled);
        }
        private static void AssertSize(SettingsWindow window, double width, double height) {
            Assert.Equal(width, window.ClientSize.Width);
            Assert.Equal(height, window.ClientSize.Height);
        }

        [AvaloniaFact]
        public async Task Settings_EnabledObjectsAndAdvanced_Resize520To900By480To760_AndRememberTheirSize() {
            var settings = new AppSettings();
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);
            opened.Show(window);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsEnabled);

            foreach (int fixedPane in new[] { 0, 2, 4, 5 }) {
                opened.SimulateTabClick(fixedPane);
                Dispatcher.UIThread.RunJobs();
                Assert.False(opened.CanResize);
                AssertSize(opened, 520, 480);
            }

            foreach (int resizablePane in new[] { 1, 3 }) {
                opened.SimulateTabClick(resizablePane);
                Dispatcher.UIThread.RunJobs();
                Assert.True(opened.CanResize);
                Assert.Equal((520, 900, 480, 760), (opened.MinWidth, opened.MaxWidth, opened.MinHeight, opened.MaxHeight));
                AssertSize(opened, 520, 480);
            }

            opened.SimulateTabClick(1);
            opened.Width = 700;
            opened.Height = 600;
            Dispatcher.UIThread.RunJobs();
            opened.SimulateTabClick(3);
            Dispatcher.UIThread.RunJobs();
            AssertSize(opened, 520, 480);
            opened.Width = 800;
            opened.Height = 700;
            Dispatcher.UIThread.RunJobs();

            opened.SimulateTabClick(2);
            Dispatcher.UIThread.RunJobs();
            AssertSize(opened, 520, 480);
            opened.SimulateTabClick(1);
            Dispatcher.UIThread.RunJobs();
            AssertSize(opened, 700, 600);

            Assert.Equal((700.0, 600.0), (settings.SettingsEnabledObjectsWidth, settings.SettingsEnabledObjectsHeight));
            Assert.Equal((800.0, 700.0), (settings.SettingsAdvancedWidth, settings.SettingsAdvancedHeight));
            int savesBefore = window.CurrentSettingsSession!.SaveCallCount;
            await window.CurrentSettingsSession!.FlushAsync();
            Assert.Equal(savesBefore + 1, window.CurrentSettingsSession!.SaveCallCount);

            opened.Close();
            Dispatcher.UIThread.RunJobs();
            SettingsWindow reopened = OpenSettings(window);
            Assert.NotSame(opened, reopened);
            reopened.Show(window);
            reopened.SimulateTabClick(3);
            Dispatcher.UIThread.RunJobs();
            AssertSize(reopened, 800, 700);
            reopened.SimulateTabClick(1);
            Dispatcher.UIThread.RunJobs();
            AssertSize(reopened, 700, 600);

            try {
                WindowScaler.SetScale(1.5);
                Dispatcher.UIThread.RunJobs();
                AssertSize(reopened, 1050, 900);
                Assert.Equal((780, 1350, 720, 1140), (reopened.MinWidth, reopened.MaxWidth, reopened.MinHeight, reopened.MaxHeight));
                Assert.Equal((700.0, 600.0), (settings.SettingsEnabledObjectsWidth, settings.SettingsEnabledObjectsHeight));

                reopened.SimulateTabClick(2);
                Dispatcher.UIThread.RunJobs();
                Assert.False(reopened.CanResize);
                Assert.Equal((780, 780, 720, 720), (reopened.MinWidth, reopened.MaxWidth, reopened.MinHeight, reopened.MaxHeight));
                AssertSize(reopened, 780, 720);

                reopened.SimulateTabClick(1);
                Dispatcher.UIThread.RunJobs();
                AssertSize(reopened, 1050, 900);
                reopened.Width = 1200;
                reopened.Height = 960;
                Dispatcher.UIThread.RunJobs();

                WindowScaler.SetScale(1.0);
                Dispatcher.UIThread.RunJobs();
                AssertSize(reopened, 800, 640);
                Assert.Equal((800.0, 640.0), (settings.SettingsEnabledObjectsWidth, settings.SettingsEnabledObjectsHeight));
                Assert.Equal((800.0, 700.0), (settings.SettingsAdvancedWidth, settings.SettingsAdvancedHeight));
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        private static readonly Dictionary<int, string[]> PaneFields = new() {
            [2] = [nameof(AppSettings.DefaultAssemblerOption), nameof(AppSettings.DefaultModuleOption), nameof(AppSettings.DefaultNodeDirection),
                nameof(AppSettings.SmartNodeDirection), nameof(AppSettings.SimplePassthroughNodes), nameof(AppSettings.RoundAssemblerCount),
                nameof(AppSettings.QualitySteps), nameof(AppSettings.ShowRecipeToolTip)],
            [3] = [nameof(AppSettings.EnableExtraProductivityForNonMiners), nameof(AppSettings.ShowUnavailable), nameof(AppSettings.UseRecipeBWfilters),
                nameof(AppSettings.LowPriorityPower), nameof(AppSettings.PullConsumerNodes), nameof(AppSettings.PullConsumerNodesPower)],
            [4] = [nameof(AppSettings.ShowErrorArrows), nameof(AppSettings.ShowWarningArrows), nameof(AppSettings.ShowDisconnectedArrows),
                nameof(AppSettings.ShowOUSuppliedArrows), nameof(AppSettings.FlagOUSuppliedNodes)],
            [5] = [nameof(AppSettings.FlagDarkMode), nameof(AppSettings.UiScale), nameof(AppSettings.LevelOfDetail), nameof(AppSettings.NodeCountForSimpleView),
                nameof(AppSettings.IconsSize), nameof(AppSettings.ArrowsOnLinks), nameof(AppSettings.DynamicLineWidth), nameof(AppSettings.AbbreviateSciPacks)],
        };

        private static Dictionary<string, object?> Snapshot(AppSettings settings) =>
            typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name, p => p.GetValue(settings));

        [AvaloniaFact]
        public void Settings_RestoreDefaults_IsInTheFooter_ConfirmsFirst_AndResetsOnlyTheVisiblePane() {
            var settings = new AppSettings {
                QualitySteps = 5, SmartNodeDirection = false,
                LowPriorityPower = 2, PullConsumerNodes = true,
                ShowErrorArrows = false, FlagOUSuppliedNodes = true,
                IconsSize = 32, ArrowsOnLinks = true,
            };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);
            opened.Show(window);
            Dispatcher.UIThread.RunJobs();
            Button restore = opened.RestoreDefaultsButtonControl;

            foreach (int hiddenPane in new[] { 0, 1 }) {
                opened.SimulateTabClick(hiddenPane);
                Dispatcher.UIThread.RunJobs();
                Assert.False(restore.IsVisible);
            }
            Assert.Equal("Enable All", opened.EnabledObjectsTabControl.EnableAllButtonControl.Content);
            Assert.DoesNotContain(opened.EnabledObjectsTabControl.GetLogicalDescendants().OfType<ContentControl>(), c => c.Content as string == "Restore Defaults");

            AppSettings defaults = new();
            string[] names = ["", "", "General", "Advanced", "Annotations", "Appearance"];

            foreach ((int pane, string[] fields) in PaneFields) {
                opened.SimulateTabClick(pane);
                Dispatcher.UIThread.RunJobs();
                Assert.True(restore.IsVisible);
                Assert.Same(restore, opened.FooterRowControl.Children[^1]);
                Assert.DoesNotContain("destructive", restore.Classes);
                Assert.False(restore.IsDefault);
                Assert.True(restore.IsEnabled);

                Dictionary<string, object?> before = Snapshot(settings);
                SheetFixture.Click(opened, restore);
                AnimationClock.Settle();

                AlertSheet shown = Assert.IsType<AlertSheet>(SheetHost.For(opened).Content);
                Assert.Equal($"Restore the default {names[pane]} settings?", shown.FrameControl.Headline);
                Assert.Equal("Every setting in this pane goes back to its original value. You can’t undo this action.", shown.FrameControl.InformativeText);
                Assert.Equal(["Restore Defaults", "Cancel"], shown.Buttons!.Items.Cast<Button>().Select(b => b.Content as string));
                Assert.Contains("destructive", shown.YesButtonControl.Classes);
                Assert.Same(shown.NoButtonControl, shown.DefaultButton);
                if (pane == 2) {
                    CanvasKeys.KeyDownOn(shown.YesButtonControl, Key.D4, KeyModifiers.Meta);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal((2, "General", false), (opened.ActiveTabIndex, opened.Title, opened.CanResize));
                    AssertSize(opened, 520, 480);
                    Assert.True(SheetHost.For(opened).IsOpen);
                    Assert.Same(shown, SheetHost.For(opened).Content);
                }
                CanvasKeys.KeyDownOn(shown.YesButtonControl, Key.Enter);
                AnimationClock.Settle();
                Assert.Equal(before, Snapshot(settings));

                SheetFixture.Click(opened, restore);
                AnimationClock.Settle();
                shown = Assert.IsType<AlertSheet>(SheetHost.For(opened).Content);
                SheetFixture.Click(opened, shown.NoButtonControl);
                AnimationClock.Settle();
                Assert.False(SheetHost.For(opened).IsOpen);
                Assert.Equal(before, Snapshot(settings));

                SheetFixture.Click(opened, restore);
                AnimationClock.Settle();
                shown = Assert.IsType<AlertSheet>(SheetHost.For(opened).Content);
                CanvasKeys.KeyDownOn(shown.YesButtonControl, Key.Back, KeyModifiers.Meta);
                AnimationClock.Settle();

                Dictionary<string, object?> after = Snapshot(settings);
                Dictionary<string, object?> expected = Snapshot(defaults);
                foreach (string field in fields)
                    Assert.True(Equals(expected[field], after[field]), $"{field} is {after[field]}, not its default {expected[field]}");
                foreach (string field in before.Keys.Except(fields))
                    Assert.True(Equals(before[field], after[field]), $"{field} changed from {before[field]} to {after[field]}");
                Assert.False(restore.IsEnabled);
                var focused = TopLevel.GetTopLevel(opened)!.FocusManager!.GetFocusedElement() as Visual;
                Assert.True(focused is not null && focused.GetVisualAncestors().Contains(opened.TabBodyControl.Content as Visual), $"focus is on {focused}");
            }

            opened.SimulateTabClick(4);
            opened.ViewModel.ErrorArrowsChecked = false;
            Assert.True(restore.IsEnabled);
            SheetFixture.Click(opened, restore);
            AnimationClock.Settle();
            AlertSheet confirm = Assert.IsType<AlertSheet>(SheetHost.For(opened).Content);
            SheetFixture.Click(opened, confirm.YesButtonControl);
            AnimationClock.Settle();
            Assert.False(SheetHost.For(opened).IsOpen);
            Assert.Equal(defaults.ShowErrorArrows, settings.ShowErrorArrows);
            Assert.False(restore.IsEnabled);
        }
    }
}
