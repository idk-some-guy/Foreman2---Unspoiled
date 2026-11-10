using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
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
using PopUp = Foreman.Mac.Kit.Controls.PopUp;
using Segmented = Foreman.Mac.Kit.Controls.Segmented;
using Switch = Foreman.Mac.Kit.Controls.Switch;

namespace Foreman.Mac.UiTests {
    //Covers OpenSettingsAsync's Graph Options tab wiring (reference §5, upstream MainForm.cs:355-440):
    //population from live GraphViewer/Graph state, the Confirm apply path back onto settings/live graph,
    //and the presetReloaded three-way OR fix (MainForm.cs:413-415's third term).
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

        [AvaloniaFact]
        public async Task ClosingWithinTheCoalesceWindow_StillSavesExactlyOnceWithTheNewValue() {
            var settings = new AppSettings { SmartNodeDirection = true };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenSettings(window);
            SettingsSession session = window.CurrentSettingsSession!;

            opened.ViewModel.SmartNodeDirectionChecked = false;

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

            opened.ViewModel.AssemblerSelectorStyleIndex = (int)AssemblerSelector.Style.Best;

            var closed = new TaskCompletionSource();
            opened.Closed += (_, _) => closed.SetResult();
            opened.Close();
            await closed.Task;

            Assert.Equal(1, session.ResolveCallCount);
            Assert.Equal(1, solveCount);
            Assert.Equal(AssemblerSelector.Style.Best, settings.DefaultAssemblerOption);
        }

        [AvaloniaFact]
        public void TogglingLoadBarreling_ClosesAndReopensAFreshSettingsWindow() {
            var settings = new AppSettings { UseRecipeBWfilters = true };
            var window = NewWindowReadyForSettings(settings);
            SettingsWindow first = OpenSettings(window);
            bool firstClosed = false;
            first.Closed += (_, _) => firstClosed = true;

            first.ViewModel.LoadBarrelingChecked = !first.ViewModel.LoadBarrelingChecked;

            Assert.True(firstClosed);
            Assert.False(settings.UseRecipeBWfilters);
            Assert.NotSame(first, window.CurrentSettingsWindow);
        }

        [AvaloniaFact]
        public void PresetSwitch_ChooseAnother_ReopensSettingsOnPresets_WithTheFailedRowAndItsWarning() {
            var settings = new AppSettings { CurrentPresetName = "Factorio 2.0 Vanilla" };
            var window = NewWindowReadyForSettings(settings);
            window.PresetResolver = new PresetResolver(userPresetsDirectoryOverride: NewTempDir());
            DataCache before = window.DataCache!;
            window.DataLoadStub = (_, _) => Task.FromResult(new DataLoadResult(null, DataLoadOutcome.ChooseAnother));
            SettingsWindow first = OpenSettings(window);
            Preset failed = first.Options.Presets!.First(p => p.Name != settings.CurrentPresetName);
            first.Options.SelectedPreset = failed;

            first.RequestReload();
            Dispatcher.UIThread.RunJobs();

            SettingsWindow reopened = window.CurrentSettingsWindow!;
            Assert.NotSame(first, reopened);
            Assert.Same(before, window.DataCache);
            Assert.Equal("Factorio 2.0 Vanilla", settings.CurrentPresetName);
            Assert.Equal(0, reopened.PaneStripControl.SelectedIndex);
            Assert.Equal(failed.Name, Assert.IsType<Preset>(reopened.PresetsTabControl.PresetListBoxControl.SelectedItem).Name);
            Border warning = Assert.IsType<Border>(reopened.PresetsTabControl.ModsStateHostControl.Child);
            Assert.Contains(warning.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("couldn\u2019t be read") == true);
            Assert.True(reopened.PresetsTabControl.RetryPresetReadButtonControl.IsEffectivelyVisible);
        }

        //Phase 7 deferred-minors sweep: OpenSettingsAsync's non-preset-reload branch indexed
        //cache.Assemblers["rocket-silo"] directly, a KeyNotFoundException on any preset without a rocket
        //silo entity. RocketAssembler itself is a synthetic pseudo-assembler DataCacheBootstrap always
        //creates regardless of preset content (DataCacheBootstrap.cs:97), so its own null-conditional
        //assignment never short-circuits the indexer away - this needs a cache with a real RocketAssembler
        //but no "rocket-silo" entry, which MinimalCache() (RocketAssembler left null) doesn't reproduce.
        private static DataCache CacheWithRocketAssemblerButNoSilo() {
            DataCache cache = MinimalCache();
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.RocketAssembler = new AssemblerPrototype(cache, "§§a:rocket-assembler", "Rocket", EntityType.Rocket, EnergySource.Void);
            return cache;
        }

        [AvaloniaFact]
        public void EnableAll_OnASiloLessPreset_LeavesTheRocketAssemblerDisabled() {
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
                nameof(AppSettings.QualitySteps)],
            [3] = [nameof(AppSettings.EnableExtraProductivityForNonMiners), nameof(AppSettings.ShowUnavailable), nameof(AppSettings.UseRecipeBWfilters),
                nameof(AppSettings.LowPriorityPower), nameof(AppSettings.PullConsumerNodes), nameof(AppSettings.PullConsumerNodesPower)],
            [4] = [nameof(AppSettings.ShowErrorArrows), nameof(AppSettings.ShowWarningArrows), nameof(AppSettings.ShowDisconnectedArrows),
                nameof(AppSettings.ShowOUSuppliedArrows), nameof(AppSettings.FlagOUSuppliedNodes)],
            [5] = [nameof(AppSettings.FlagDarkMode), nameof(AppSettings.UiScale), nameof(AppSettings.LevelOfDetail), nameof(AppSettings.NodeCountForSimpleView),
                nameof(AppSettings.IconsSize), nameof(AppSettings.ArrowsOnLinks), nameof(AppSettings.DynamicLineWidth), nameof(AppSettings.AbbreviateSciPacks),
                nameof(AppSettings.DefaultLinkRouting)],
        };

        private static Dictionary<string, object?> Snapshot(AppSettings settings) =>
            typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name, p => p.GetValue(settings));

        [AvaloniaFact]
        public void Settings_RestoreDefaults_IsInTheFooter_ConfirmsFirst_AndResetsOnlyTheVisiblePane() {
            var settings = new AppSettings {
                QualitySteps = 5, SmartNodeDirection = false,
                LowPriorityPower = 2, PullConsumerNodes = true,
                ShowErrorArrows = false, FlagOUSuppliedNodes = true,
                IconsSize = 32, ArrowsOnLinks = true, DefaultLinkRouting = LinkRouting.Orthogonal,
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
                shown = Assert.IsType<AlertSheet>(SheetHost.For(opened).Content);
                AnimationClock.UntilSheetOpen(shown.NoButtonControl);
                SheetFixture.Click(opened, shown.NoButtonControl);
                AnimationClock.Settle();
                Assert.False(SheetHost.For(opened).IsOpen);
                Assert.Equal(before, Snapshot(settings));

                ScrollViewer? scroller = pane == 2 ? Assert.IsType<ScrollViewer>(opened.GeneralTabControl.Content) : null;
                scroller?.ScrollToEnd();
                Dispatcher.UIThread.RunJobs();
                double scrolledTo = scroller?.Offset.Y ?? 0;
                Assert.True(scroller is null || scrolledTo > 0);
                SheetFixture.Click(opened, restore);
                AnimationClock.Settle();
                shown = Assert.IsType<AlertSheet>(SheetHost.For(opened).Content);
                CanvasKeys.KeyDownOn(shown.YesButtonControl, Key.Back, KeyModifiers.Meta);
                AnimationClock.Settle();
                if (scroller is not null) {
                    AnimationClock.UntilReflowsSettled(opened);
                    Assert.Equal(scrolledTo, scroller.Offset.Y, 0.5);
                }

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
            AlertSheet confirm = Assert.IsType<AlertSheet>(SheetHost.For(opened).Content);
            AnimationClock.UntilSheetOpen(confirm.YesButtonControl);
            SheetFixture.Click(opened, confirm.YesButtonControl);
            AnimationClock.Settle();
            Assert.False(SheetHost.For(opened).IsOpen);
            Assert.Equal(defaults.ShowErrorArrows, settings.ShowErrorArrows);
            Assert.False(restore.IsEnabled);
        }

        private static SettingsWindow OpenAppearance(MainWindow window) {
            SettingsWindow opened = OpenSettings(window);
            opened.Show(window);
            opened.SimulateTabClick(5);
            AnimationClock.UntilReflowsSettled(opened);
            return opened;
        }

        private static void ClickSegment(Window window, Segmented segmented, string label) =>
            SheetFixture.Click(window, segmented.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label));

        [AvaloniaFact]
        public void Appearance_Theme_IsASegmentedSystemLightDark_WritingThroughAppApplyTheme() {
            var app = (App)Application.Current!;
            var settings = new AppSettings();
            SettingsWindow opened = OpenAppearance(NewWindowReadyForSettings(settings));
            Segmented theme = opened.AppearanceTabControl.ThemeSegmentedControl;

            try {
                Assert.Equal(["System", "Light", "Dark"], theme.Items);
                Assert.Equal(0, theme.SelectedIndex);

                ClickSegment(opened, theme, "Dark");
                Assert.Equal(ThemeMode.Dark, settings.FlagDarkMode);
                Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
                Assert.Equal(ThemeVariant.Dark, app.ActualThemeVariant);

                ClickSegment(opened, theme, "Light");
                Assert.Equal(ThemeMode.Light, settings.FlagDarkMode);
                Assert.Equal(ThemeVariant.Light, app.ActualThemeVariant);

                ClickSegment(opened, theme, "System");
                Assert.Equal(ThemeMode.System, settings.FlagDarkMode);
                Assert.Equal(0, theme.SelectedIndex);
                Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
            } finally {
                app.ApplyTheme(ThemeMode.System);
            }
        }

        [AvaloniaFact]
        public void Appearance_InterfaceScale_ListsTheFiveScaleContractSteps_AndWritesThroughWindowScalerSetScale() {
            var settings = new AppSettings();
            SettingsWindow opened = OpenAppearance(NewWindowReadyForSettings(settings));
            PopUp scale = opened.AppearanceTabControl.UiScalePopUpControl;

            try {
                Assert.Equal(ScaleContract.Steps.Select(s => $"{s * 100:0} %"), scale.Items);
                Assert.Equal(["100 %", "110 %", "125 %", "150 %", "200 %"], scale.Items);
                Assert.Equal("100 %", scale.SelectedItem);

                scale.SelectedItem = "150 %";
                Assert.Equal(1.5, settings.UiScale);
                Assert.Equal(1.5, WindowScaler.CurrentScale);

                scale.SelectedItem = "110 %";
                Assert.Equal(1.1, settings.UiScale);
                Assert.Equal(1.1, WindowScaler.CurrentScale);
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        [AvaloniaFact]
        public async Task Appearance_LinkRoutingDefault_PersistsAcrossAReload() {
            string home = NewTempDir();
            var settings = new AppSettings();
            MainWindow window = NewWindowReadyForSettings(settings);
            window.SettingsService = new SettingsService(home);
            SettingsWindow opened = OpenAppearance(window);
            Segmented routing = opened.AppearanceTabControl.LinkRoutingSegmentedControl;

            Assert.Equal(["Rounded", "Orthogonal"], routing.Items);
            Assert.Equal(0, routing.SelectedIndex);
            Assert.Equal(LinkRouting.Rounded, settings.DefaultLinkRouting);

            ClickSegment(opened, routing, "Orthogonal");
            Assert.Equal(LinkRouting.Orthogonal, settings.DefaultLinkRouting);
            await window.CurrentSettingsSession!.FlushAsync();

            AppSettings reloaded = new SettingsService(home).Load();
            Assert.Equal(LinkRouting.Orthogonal, reloaded.DefaultLinkRouting);
            var fresh = new SettingsViewModel();
            fresh.LoadFrom(reloaded);
            Assert.Equal(1, fresh.LinkRoutingIndex);
        }

        [AvaloniaFact]
        public void Appearance_WidthByThroughput_DefaultsOff_AndAPointerClickBelowTheFoldDoesNotScroll() {
            var settings = new AppSettings();
            Assert.False(settings.DynamicLineWidth);
            MainWindow window = NewWindowReadyForSettings(settings);
            SettingsWindow opened = OpenAppearance(window);
            Switch width = opened.AppearanceTabControl.DynamicLineWidthSwitchControl;

            Assert.False(width.IsChecked);
            Assert.False(window.GraphCanvas.Viewer.Context.DynamicLinkWidth);
            FormRow row = width.FindLogicalAncestorOfType<FormRow>()!;
            Assert.Equal("Width by throughput", row.Label);

            var scroller = (ScrollViewer)opened.AppearanceTabControl.Content!;
            double TopIn(Control c) => c.TranslatePoint(default, scroller)!.Value.Y;
            double ContentBottom(Control c) => TopIn(c) + c.Bounds.Height + scroller.Offset.Y;
            scroller.Offset = new Vector(0, ContentBottom(width) + 2 - scroller.Viewport.Height);
            AnimationClock.UntilReflowsSettled(opened);
            double before = scroller.Offset.Y;
            Assert.True(TopIn(width) >= 0 && TopIn(width) + width.Bounds.Height <= scroller.Viewport.Height, "the switch starts fully in view");
            Assert.True(TopIn(row) + row.Bounds.Height > scroller.Viewport.Height, "the note starts below the fold");

            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label" && t.TemplatedParent == row);
            Assert.False(width.IsFocused);
            SheetFixture.Click(opened, label);
            Assert.True(width.IsFocused);
            Assert.Equal(before, scroller.Offset.Y, 0.5);

            SheetFixture.Click(opened, width);
            Assert.True(width.IsChecked);
            Assert.True(settings.DynamicLineWidth);
            Assert.True(window.GraphCanvas.Viewer.Context.DynamicLinkWidth);
            Assert.Equal(before, scroller.Offset.Y, 0.5);

            opened.ViewModel.RestoreDefaults(SettingsPane.Appearance);
            Assert.False(width.IsChecked);
            Assert.False(settings.DynamicLineWidth);

            Switch packs = opened.AppearanceTabControl.AbbreviateSciPacksSwitchControl;
            FormRow packsRow = packs.FindAncestorOfType<FormRow>()!;
            const double clip = 4;
            scroller.Offset = new Vector(0, ContentBottom(packs) - clip - scroller.Viewport.Height);
            AnimationClock.UntilReflowsSettled(opened);
            before = scroller.Offset.Y;
            Assert.Equal(scroller.Viewport.Height - packs.Bounds.Height + clip, TopIn(packs), 0.5);
            SheetFixture.Click(opened, packs);
            Assert.True(scroller.Offset.Y - before <= clip + 0.5, $"the pane scrolled {scroller.Offset.Y - before} for a {clip} pt clip");
            Assert.True(TopIn(packsRow) + packsRow.Bounds.Height > scroller.Viewport.Height, "the note stays below the fold");
        }
    }
}
