using Avalonia;
using Point = Avalonia.Point;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
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
using Foreman.Mac.Kit.Composites;
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
using System.Reflection;
using System.Text.RegularExpressions;
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

        private static readonly string[] PaneNames = ["Presets", "Enabled Objects", "General", "Advanced", "Annotations", "Appearance"];

        private static Border[] PaneItems(SettingsWindow window) =>
            [.. window.PaneStripControl.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Track").Children.OfType<Border>()];

        [AvaloniaFact]
        public async Task Settings_PaneStrip_ListsSixLabelledPanes_AndNeverCollapsesToIconOnly() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(520, window.ClientSize.Width);
            Assert.Equal(PaneNames, window.PaneStripControl.Items.Select(i => i.Text));
            Border[] items = PaneItems(window);
            Assert.Equal(6, items.Length);
            for (int i = 0; i < items.Length; i++) {
                TextBlock label = items[i].GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.Equal(PaneNames[i], label.Text);
                Assert.True(label.IsEffectivelyVisible);
                Assert.True(label.Bounds.Width > 0);
                Assert.Equal(TextTrimming.None, label.TextTrimming);
                Assert.True(label.Bounds.Width >= label.DesiredSize.Width);
                Assert.NotNull(items[i].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data);
            }
            Assert.DoesNotContain("icon-only", window.PaneStripControl.Classes);

            AutomationPeer strip = ControlAutomationPeer.CreatePeerForElement(window.PaneStripControl);
            Assert.Equal(AutomationControlType.Tab, strip.GetAutomationControlType());
            Assert.Equal("Settings panes", strip.GetName());
            IReadOnlyList<AutomationPeer> tabs = strip.GetChildren();
            Assert.Equal(PaneNames, tabs.Select(t => t.GetName()));
            for (int i = 0; i < tabs.Count; i++) {
                Assert.Equal(AutomationControlType.TabItem, tabs[i].GetAutomationControlType());
                Assert.Equal(i == 0, tabs[i].GetProvider<ISelectionItemProvider>()!.IsSelected);
                Assert.Contains($"⌘{i + 1}", tabs[i].GetHelpText());
            }

            try {
                foreach (double scale in new[] { 1.0, 1.5 }) {
                    WindowScaler.SetScale(scale);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(520 * scale, window.ClientSize.Width);
                    Assert.Equal((0.0, 0.0), (window.PaneStripControl.Margin.Left, window.PaneStripControl.Margin.Right));
                    Assert.True(window.PaneStripControl.TryFindResource("Foreman.Thickness.TabStripPadding", window.PaneStripControl.ActualThemeVariant, out object? token));
                    var padding = Assert.IsType<Thickness>(token);
                    Border[] scaled = PaneItems(window);
                    Assert.Equal(padding.Left, scaled[0].TranslatePoint(default, window)!.Value.X);
                    Assert.All(scaled.Select(i => i.GetVisualDescendants().OfType<TextBlock>().Single()), l => Assert.True(l.Bounds.Width >= l.DesiredSize.Width));
                    Assert.True(window.PaneStripControl.DesiredSize.Width <= window.ClientSize.Width,
                        $"strip wants {window.PaneStripControl.DesiredSize.Width} of {window.ClientSize.Width} at {scale}");
                }
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        [AvaloniaFact]
        public async Task Settings_Cmd1ThroughCmd6_SwitchPanes_AndEachTooltipNamesItsShortcut() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Border[] items = PaneItems(window);
            UserControl[] panes = [window.PresetsTabControl, window.EnabledObjectsTabControl, window.GeneralTabControl,
                window.AdvancedTabControl, window.AnnotationsTabControl, window.AppearanceTabControl];

            for (int i = 0; i < items.Length; i++)
                Assert.Equal($"{PaneNames[i]} — ⌘{i + 1}", ToolTip.GetTip(items[i]));
            Assert.Equal("Presets", window.Title);

            Point general = items[2].TranslatePoint(new Point(items[2].Bounds.Width / 2, items[2].Bounds.Height / 2), window) ?? default;
            window.MouseDown(general, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(general, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, window.ActiveTabIndex);
            Assert.Same(window.GeneralTabControl, window.TabBodyControl.Content);

            int previous = 2;
            foreach (int n in new[] { 6, 5, 4, 3, 2, 1 }) {
                CanvasKeys.KeyDownOn(window.TabBodyControl, Key.D0 + n, KeyModifiers.Meta);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(n - 1, window.ActiveTabIndex);
                Assert.Equal(n - 1, window.PaneStripControl.SelectedIndex);
                Assert.Equal(PaneNames[n - 1], window.Title);
                Assert.Same(panes[n - 1], window.TabBodyControl.Content);
                Assert.Equal(1, panes[n - 1].Opacity);
                if (previous != n - 1)
                    Assert.Null(panes[previous].GetVisualParent());
                previous = n - 1;
            }
        }

        [AvaloniaFact]
        public async Task Settings_Footer_ShowsTheVersion_AndAboutStillDoesToo() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            window.Show();

            for (int i = 0; i < PaneNames.Length; i++) {
                window.SimulateTabClick(i);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Foreman 2 · " + AppVersion.VersionedDisplay, window.FooterTextControl.Text);
                Assert.True(window.FooterTextControl.IsEffectivelyVisible);
                Assert.Same(window.FooterTextControl, window.FooterRowControl.Children[0]);
                Assert.Contains("caption", window.FooterTextControl.Classes);
                Assert.Contains("tertiary", window.FooterTextControl.Classes);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.Text?.Contains("resize", StringComparison.OrdinalIgnoreCase) == true);
            }

            var about = new AboutWindow();
            about.Show();
            Assert.Equal(AppVersion.VersionedDisplay, about.VersionTextBlockControl.Text);
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
                [nameof(AppSettings.QualitySteps)] = general.QualityStepsStepperControl,
                [nameof(AppSettings.RoundAssemblerCount)] = general.RoundAssemblerCountSwitchControl,
                [nameof(AppSettings.DefaultAssemblerOption)] = general.AssemblerSelectorStylePopUpControl,
                [nameof(AppSettings.DefaultModuleOption)] = general.ModuleSelectorStylePopUpControl,
                [nameof(AppSettings.DefaultNodeDirection)] = general.NodeDirectionSegmentedControl,
                [nameof(AppSettings.SmartNodeDirection)] = general.SmartNodeDirectionSwitchControl,
                [nameof(AppSettings.SimplePassthroughNodes)] = general.SimplePassthroughNodesSwitchControl,

                [nameof(AppSettings.EnableExtraProductivityForNonMiners)] = advanced.ShowProductivityBonusOnAllSwitchControl,
                [nameof(AppSettings.ShowUnavailable)] = advanced.ShowUnavailablesSwitchControl,
                [nameof(AppSettings.UseRecipeBWfilters)] = advanced.LoadBarrelingSwitchControl,
                [nameof(AppSettings.LowPriorityPower)] = advanced.LowPriorityPowerStepperControl,
                [nameof(AppSettings.PullConsumerNodes)] = advanced.PullConsumerNodesSwitchControl,
                [nameof(AppSettings.PullConsumerNodesPower)] = advanced.PullConsumerNodesPowerStepperControl,

                [nameof(AppSettings.ShowErrorArrows)] = annotations.ErrorArrowsSwitchControl,
                [nameof(AppSettings.ShowWarningArrows)] = annotations.WarningArrowsSwitchControl,
                [nameof(AppSettings.ShowDisconnectedArrows)] = annotations.DisconnectedArrowsSwitchControl,
                [nameof(AppSettings.ShowOUSuppliedArrows)] = annotations.OUSuppliedArrowsSwitchControl,
                [nameof(AppSettings.FlagOUSuppliedNodes)] = annotations.FlagOUSupplyNodesSwitchControl,

                [nameof(AppSettings.LevelOfDetail)] = appearance.LevelOfDetailSegmentedControl,
                [nameof(AppSettings.NodeCountForSimpleView)] = appearance.NodeCountForSimpleViewStepperControl,
                [nameof(AppSettings.IconsSize)] = appearance.IconsSizeStepperControl,
                [nameof(AppSettings.ArrowsOnLinks)] = appearance.ArrowsOnLinksSwitchControl,
                [nameof(AppSettings.DynamicLineWidth)] = appearance.DynamicLineWidthSwitchControl,
                [nameof(AppSettings.AbbreviateSciPacks)] = appearance.AbbreviateSciPacksSwitchControl,
                [nameof(AppSettings.FlagDarkMode)] = appearance.ThemeSegmentedControl,
                [nameof(AppSettings.UiScale)] = appearance.UiScalePopUpControl,
                [nameof(AppSettings.DefaultLinkRouting)] = appearance.LinkRoutingSegmentedControl,
            };

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
        public async Task ModSelectionBox_ReflectsSelectedPresetModList_BesideOtherPresetsInOneFillRow_WithNoPaneScroller() {
            var current = new Preset(VanillaPresetName, true, true);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [current, new Preset(SpaceAgePresetName, false, false)]));
            await window.ModListRead;

            var mods = window.PresetsTabControl.ModSelectionBoxControl.ItemsSource?.Cast<string>().ToList();
            Assert.Equal(VanillaPresetModList, mods);

            window.Show();
            AnimationClock.UntilReflowsSettled(window);
            PresetsTab pane = window.PresetsTabControl;
            ListBox presets = pane.PresetListBoxControl, modList = pane.ModSelectionBoxControl;
            foreach (ListBox list in new[] { presets, modList }) {
                AssertNoPaneScroller(pane, list);
                Assert.Equal(ListMinimum(list, 3), list.MinHeight, 3);
            }
            Rect left = BoundsIn(presets, pane), right = BoundsIn(modList, pane);
            Assert.Equal(left.Y, right.Y, 0.5);
            Assert.Equal(left.Height, right.Height, 0.5);
            Assert.Equal(left.Width, right.Width, 1.0);
            Grid cell = Assert.IsType<Grid>(presets.GetVisualParent());
            Assert.Equal(Math.Max(cell.RowDefinitions[1].ActualHeight, presets.MinHeight), left.Height, 0.5);

            FormGroup group = Assert.Single(pane.GetVisualDescendants().OfType<FormGroup>());
            Assert.Equal(pane.Bounds.Height - 20, BottomIn(group, pane), 1.0);
            Rect paneRect = new(pane.Bounds.Size);
            foreach (Control control in pane.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && !c.GetVisualAncestors().OfType<ListBox>().Any())) {
                Rect r = BoundsIn(control, pane);
                Assert.True(r.X >= -0.5 && r.Y >= -0.5 && r.Right <= paneRect.Right + 0.5 && r.Bottom <= paneRect.Bottom + 0.5, $"{control.GetType().Name} {control.Name} lies outside the pane at {r}");
            }

            //Headless glyphs are one em wide, so the notes wrap far taller than live; the fill is proved on a taller pane.
            GrowPresetsPane(window, 680);
            TextBlock[] notes = [.. cell.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == 2).OrderBy(Grid.GetColumn)];
            Assert.Equal(2, notes.Length);
            Control[] fixedRows = [Assert.Single(pane.GetVisualDescendants().OfType<FormRow>()), pane.FindControl<TextBlock>("PresetListLabel")!,
                pane.FindControl<TextBlock>("ModListLabel")!, .. notes, (Control)pane.FindControl<ButtonRow>("PresetButtonRow")!.GetVisualParent()!];
            (ListBox List, TextBlock Note)[] columns = [(presets, notes[0]), (modList, notes[1])];
            foreach ((ListBox list, TextBlock note) in columns) {
                Assert.True(list.Bounds.Height > list.MinHeight + 0.5, $"{list.Name} is {list.Bounds.Height} pt, its minimum {list.MinHeight}");
                Assert.True(BoundsIn(list, pane).Bottom <= BoundsIn(note, pane).Y + 0.5, $"{list.Name} {BoundsIn(list, pane)} overlaps its note {BoundsIn(note, pane)}");
            }
            left = BoundsIn(presets, pane);
            right = BoundsIn(modList, pane);
            Assert.Equal(left.Y, right.Y, 0.5);
            Assert.Equal(left.Height, right.Height, 0.5);

            double[] fixedHeights = [.. fixedRows.Select(c => c.Bounds.Height)];
            GrowPresetsPane(window, 780);
            Assert.Equal(left.Height + 100, presets.Bounds.Height, 1.0);
            Assert.Equal(right.Height + 100, modList.Bounds.Height, 1.0);
            Assert.Equal(fixedHeights, fixedRows.Select(c => c.Bounds.Height));
        }

        private static void GrowPresetsPane(SettingsWindow window, double height) {
            window.MaxHeight = height;
            window.Height = height;
            AnimationClock.UntilReflowsSettled(window);
        }

        private static Rect BoundsIn(Control control, Visual root) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

        private static void AssertTheStateHostTakesTheModListsPlace(PresetsTab pane, Rect listBounds, Rect groupBounds) {
            Assert.False(pane.ModSelectionBoxControl.IsVisible);
            Rect host = BoundsIn(pane.ModsStateHostControl, pane);
            Assert.Equal(listBounds.X, host.X, 0.5);
            Assert.Equal(listBounds.Y, host.Y, 0.5);
            Assert.Equal(listBounds.Width, host.Width, 0.5);
            Assert.Equal(listBounds.Height, host.Height, 0.5);
            Assert.Equal(groupBounds, BoundsIn(pane.PresetsGroupControl, pane));
        }

        [AvaloniaFact]
        public async Task Presets_WhileReading_ShowsTheStatePanelLoadingInTheModListsCell() {
            var reading = new TaskCompletionSource<PresetInfo>();
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)])) {
                PresetInfoReaderStub = _ => reading.Task,
            };
            window.Show();
            AnimationClock.UntilReflowsSettled(window);
            PresetsTab pane = window.PresetsTabControl;
            Rect listBounds = BoundsIn(pane.ModSelectionBoxControl, pane), groupBounds = BoundsIn(pane.PresetsGroupControl, pane);
            window.SimulateModListReload();
            AnimationClock.UntilReflowsSettled(window);

            Border host = pane.ModsStateHostControl;
            StatePanel loading = Assert.IsType<StatePanel>(host.Child);
            Assert.Equal(StatePanelShape.Loading, loading.Shape);
            Assert.Null(loading.Headline);
            Assert.Equal("Reading this preset’s mods.", loading.Sentence);
            Assert.True(loading.ForceCompact);
            AssertTheStateHostTakesTheModListsPlace(pane, listBounds, groupBounds);
            Assert.DoesNotContain(pane.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Reading the presets stored with the app.");
            Assert.Null(pane.PresetsGroupControl.StateContent);

            reading.SetResult(new PresetInfo(new Dictionary<string, string> { ["base"] = "2.0.76" }, false, false));
            await window.ModListRead;
            Dispatcher.UIThread.RunJobs();

            Assert.False(host.IsVisible);
            Assert.True(pane.ModSelectionBoxControl.IsVisible);
            Assert.Equal(["base_2.0.76"], window.PresetsTabControl.ModSelectionBoxControl.ItemsSource!.Cast<string>());
        }

        [AvaloniaFact]
        public async Task Presets_LoadFailure_ShowsAnInlineWarningRowWithRetry_AndOpensNoAlert() {
            int reads = 0;
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)])) {
                PresetInfoReaderStub = _ => Task.FromResult(++reads == 1
                    ? new PresetInfo(null, false, false)
                    : new PresetInfo(new Dictionary<string, string> { ["base"] = "2.0.76" }, false, false)),
            };
            window.WarningDialogStub = (_, _) => throw new InvalidOperationException("a preset-load failure never opens an alert");
            window.Show();
            GrowPresetsPane(window, 680);
            PresetsTab pane = window.PresetsTabControl;
            Rect listBounds = BoundsIn(pane.ModSelectionBoxControl, pane), groupBounds = BoundsIn(pane.PresetsGroupControl, pane);
            window.SimulateModListReload();
            await window.ModListRead;
            AnimationClock.UntilReflowsSettled(window);

            Border host = pane.ModsStateHostControl;
            Border warning = Assert.IsType<Border>(host.Child);
            AssertTheStateHostTakesTheModListsPlace(pane, listBounds, groupBounds);
            Assert.Equal("“Factorio 2.0 Vanilla” couldn’t be read — the preset file may be damaged.",
                warning.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("callout")).Text);
            Assert.True(warning.TryFindResource("Foreman.Color.WarningSoft", warning.ActualThemeVariant, out object? soft));
            Assert.Equal((Avalonia.Media.Color)soft!, Assert.IsAssignableFrom<ISolidColorBrush>(warning.Background).Color);
            Button retry = warning.GetVisualDescendants().OfType<Button>().Single();
            Assert.Equal("Retry", retry.Content);
            Rect hostRect = BoundsIn(host, pane), retryRect = BoundsIn(retry, pane);
            Assert.True(retryRect.X >= hostRect.X && retryRect.Right <= hostRect.Right && retryRect.Y >= hostRect.Y && retryRect.Bottom <= hostRect.Bottom,
                $"Retry {retryRect} lies outside the state host {hostRect}");
            Assert.False(SheetHost.For(window).IsOpen);

            retry.BringIntoView();
            AnimationClock.Settle();
            AnimationClock.UntilReflowsSettled(window);
            Point centre = retry.TranslatePoint(new Point(retry.Bounds.Width / 2, retry.Bounds.Height / 2), window) ?? default;
            window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
            await window.ModListRead;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, reads);
            Assert.False(host.IsVisible);
            Assert.Equal(["base_2.0.76"], window.PresetsTabControl.ModSelectionBoxControl.ItemsSource!.Cast<string>());
            Assert.False(SheetHost.For(window).IsOpen);
        }

        //--- Form grammar across the five panes --------------------------------------------------------------

        private static readonly int[] FormPanes = [0, 1, 2, 3, 4, 5];

        private static UserControl PaneAt(SettingsWindow window, int index) => index switch {
            0 => window.PresetsTabControl,
            1 => window.EnabledObjectsTabControl,
            2 => window.GeneralTabControl,
            3 => window.AdvancedTabControl,
            4 => window.AnnotationsTabControl,
            _ => window.AppearanceTabControl,
        };

        [AvaloniaFact]
        public async Task Settings_EveryPane_UsesOneLabelColumnAndOneControlEdge_WithNoBorderBetweenRows() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true), new Preset(SpaceAgePresetName, false, false)]));
            window.ViewModel.PullConsumerNodesChecked = true;
            window.Show();

            foreach (int index in FormPanes.Where(i => i != 1)) {
                window.SimulateTabClick(index);
                AnimationClock.UntilReflowsSettled(window);
                UserControl pane = PaneAt(window, index);

                FormGroup[] groups = [.. pane.GetVisualDescendants().OfType<FormGroup>()];
                Assert.NotEmpty(groups);
                Assert.All(groups, g => Assert.True(g.IsBoxed, $"{g.Title} on pane {index} is not boxed"));
                if (index != 0)
                    Assert.All(groups, g => Assert.All(g.Items, item => Assert.IsType<FormRow>(item)));

                FormRow[] rows = [.. pane.GetVisualDescendants().OfType<FormRow>()];
                Assert.NotEmpty(rows);
                Assert.All(rows, r => Assert.Equal(FormRowLabelColumn.Window, r.LabelColumn));
                double[] edges = [.. rows.Select(r => r.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_ControlColumn" && c.TemplatedParent == r).TranslatePoint(default, pane)!.Value.X)];
                Assert.All(edges, x => Assert.Equal(edges[0], x, 3));
                double[] labelEdges = [.. rows.Where(r => !string.IsNullOrEmpty(r.Label)).Select(r => {
                    TextBlock label = r.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label" && t.TemplatedParent == r);
                    return label.TranslatePoint(new Point(label.Bounds.Width, 0), pane)!.Value.X;
                })];
                Assert.All(labelEdges, x => Assert.Equal(labelEdges[0], x, 3));

                Assert.DoesNotContain(pane.GetVisualDescendants(), v => v is Separator);
                Assert.DoesNotContain(pane.GetVisualDescendants().OfType<Border>(), b =>
                    (b.BorderThickness.Top > 0) != (b.BorderThickness.Bottom > 0) && b.BorderThickness.Left == 0 && b.BorderThickness.Right == 0);

                if (index >= 2)
                    AssertThePaneScrollsAsOnePage(window, pane, index);
            }
        }

        private static void AssertThePaneScrollsAsOnePage(SettingsWindow window, UserControl pane, int index) {
            (string[] titles, Control belowFold) = index switch {
                2 => (new[] { "New nodes", "Counts" }, (Control)window.GeneralTabControl.QualityStepsStepperControl),
                3 => (new[] { "Calculations", "Data", "Solver" }, window.AdvancedTabControl.PullConsumerNodesPowerStepperControl),
                4 => (new[] { "Guide arrows" }, window.AnnotationsTabControl.FlagOUSupplyNodesSwitchControl),
                _ => (new[] { "Appearance", "Links", "Nodes" }, window.AppearanceTabControl.AbbreviateSciPacksSwitchControl),
            };
            Assert.DoesNotContain(pane.GetVisualDescendants(), v => v is ComboBox or RadioButton or NumericUpDown
                || (v is CheckBox && v.GetType() == typeof(CheckBox)));
            var scroller = Assert.IsType<ScrollViewer>(pane.Content);
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
            Assert.True(scroller.AllowAutoHide);
            var stack = Assert.IsType<StackPanel>(scroller.Content);
            Assert.Equal(new Thickness(20, 18, 20, 20), stack.Margin);

            FormGroup[] groups = [.. pane.GetVisualDescendants().OfType<FormGroup>()];
            Assert.Equal(titles, groups.Select(g => g.Title));
            Assert.All(groups, g => Assert.Same(scroller, g.FindAncestorOfType<ScrollViewer>()));
            Assert.All(pane.GetVisualDescendants().OfType<FormRow>(), r => Assert.Same(scroller, r.FindAncestorOfType<ScrollViewer>()));
            Assert.Equal(0, groups[^1].TrailingGap);

            Assert.Equal(0, scroller.Offset.Y);
            Assert.Equal(376, scroller.Viewport.Height, 1.0);
            Assert.Equal(scroller.Bounds.Width, scroller.Viewport.Width, 0.5);
            Assert.Equal(scroller.Viewport.Width, stack.Bounds.Width + stack.Margin.Left + stack.Margin.Right, 0.5);
            double lastBottom = groups[^1].TranslatePoint(new Point(0, groups[^1].Bounds.Height), stack)!.Value.Y + stack.Margin.Top;
            Assert.Equal(lastBottom + 20, scroller.Extent.Height, 1.0);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height, $"pane {index} extent {scroller.Extent.Height} fits {scroller.Viewport.Height}");

            FormRow row = belowFold.FindAncestorOfType<FormRow>()!;
            Assert.True(RowTopIn(scroller, row) + row.Bounds.Height > scroller.Viewport.Height, $"{row.Label} starts in view");
            InputElement target = new[] { belowFold }.Concat(belowFold.GetVisualDescendants()).OfType<InputElement>().First(e => e.Focusable && e.IsEffectivelyEnabled);
            Assert.True(target.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            AssertTheWholeRowIsInView(scroller, row);
            double landed = scroller.Offset.Y;
            AnimationClock.Settle();
            Assert.Equal(landed, scroller.Offset.Y);

            Rect strip = BoundsIn(window.PaneStripControl, window), footer = BoundsIn(window.FooterRowControl, window);
            scroller.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(scroller.Extent.Height - scroller.Viewport.Height, scroller.Offset.Y, 0.5);
            Assert.Equal(strip, BoundsIn(window.PaneStripControl, window));
            Assert.Equal(footer, BoundsIn(window.FooterRowControl, window));

            CanvasKeys.KeyDownOn(window.TabBodyControl, Key.D1, KeyModifiers.Meta);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(window.TabBodyControl, Key.D1 + index, KeyModifiers.Meta);
            AnimationClock.UntilReflowsSettled(window);
            Assert.Same(pane, window.TabBodyControl.Content);
            Assert.Equal(0, scroller.Offset.Y);

            if (index == 3) {
                scroller.ScrollToEnd();
                window.Width = 900;
                window.Height = 760;
                AnimationClock.UntilReflowsSettled(window);
                Assert.Equal(656, scroller.Viewport.Height, 1.0);
                Assert.InRange(scroller.Offset.Y, 0, scroller.Extent.Height - scroller.Viewport.Height + 0.5);
            }
        }

        private static double RowTopIn(ScrollViewer scroller, FormRow row) => row.TranslatePoint(default, scroller)!.Value.Y;

        private static void AssertTheWholeRowIsInView(ScrollViewer scroller, FormRow row) {
            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note" && t.TemplatedParent == row);
            Assert.True(note.IsEffectivelyVisible && !string.IsNullOrEmpty(note.Text), $"{row.Label} has no help sentence");
            double top = RowTopIn(scroller, row);
            Assert.True(top >= -0.5 && top + row.Bounds.Height <= scroller.Viewport.Height + 0.5,
                $"{row.Label} spans {top}..{top + row.Bounds.Height} of a {scroller.Viewport.Height} viewport at offset {scroller.Offset.Y}");
        }

        [AvaloniaFact]
        public async Task Settings_HelpText_IsASentence_AndNamesNoAppSettingsField() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            string[] fields = [.. typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)];
            var help = new List<string>();
            var names = new List<string>();

            foreach (int index in FormPanes) {
                UserControl pane = PaneAt(window, index);
                FormRow[] rows = [.. pane.GetLogicalDescendants().OfType<FormRow>()];
                Assert.True(index == 1 || rows.Length > 0, $"pane {index} has no form rows");
                foreach (FormRow row in rows) {
                    Assert.False(string.IsNullOrWhiteSpace(row.Note), $"'{row.Label}' on pane {index} has no help sentence");
                    help.Add(row.Note!);
                    names.Add(row.Label ?? "");
                }
                help.AddRange(pane.GetLogicalDescendants().OfType<Control>().Select(AutomationProperties.GetHelpText).OfType<string>().Where(h => h.Length > 0));
                names.AddRange(pane.GetLogicalDescendants().OfType<FormGroup>().Select(g => g.Title ?? ""));
            }
            help.Add((string)ToolTip.GetTip(window.PresetsTabControl.ComparePresetsButtonControl)!);

            EnabledObjectsTab objects = window.EnabledObjectsTabControl;
            (Control Control, string Name, string Sentence)[] filters = [
                (objects.CategoryPopUpControl, "Category", "Chooses which kind of object the list below shows."),
                (objects.FilterFieldControl, "Filter", "Narrows the list to objects whose name contains this text."),
                (objects.ShowSegmentedControl, "Show", "Limits the list to objects that are enabled or disabled."),
                (objects.ShowUnavailablesFilterCheckBoxControl, "Show unavailable", "Also lists objects the preset marks as unavailable."),
                (objects.ObjectListControl, "Objects", "Unticking an object hides it from the browser, the palette and every calculation."),
            ];
            foreach ((Control control, string name, string sentence) in filters) {
                Assert.Equal(name, AutomationProperties.GetName(control));
                Assert.Equal(sentence, AutomationProperties.GetHelpText(control));
                Assert.Contains(sentence, help);
                if (control is not ListBox)
                    Assert.Equal(sentence, ToolTip.GetTip(control));
            }

            foreach (string sentence in help) {
                string s = sentence.Trim();
                Assert.True(s.Length > 0 && char.IsUpper(s[0]), $"'{s}' does not start with a capital");
                Assert.EndsWith(".", s);
                Assert.Single(Regex.Matches(s, @"[.!?](?=\s|$)"));
                Assert.DoesNotContain("!", s);
                Assert.DoesNotContain("?", s);
                Assert.DoesNotContain("(DEV)", s);
            }
            foreach (string text in help.Concat(names)) {
                Assert.DoesNotContain("AppSettings", text);
                foreach (string field in fields)
                    Assert.False(Regex.IsMatch(text, $@"\b{field}\b"), $"'{text}' names the field {field}");
            }
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

        //--- Context menu ----------------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task ContextMenu_ReadsUsePresetAndDeletePreset_WithDeleteDisabledForTheDefaultPreset() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(SpaceAgePresetName, true, false)]));
            MenuItem use = window.PresetsTabControl.SelectPresetMenuItemControl;
            MenuItem delete = window.PresetsTabControl.DeletePresetMenuItemControl;

            window.UpdatePresetMenuCaptionsFor(new Preset("Some Other Preset", false, false));
            Assert.Equal(("Use Preset", true, "Delete Preset", true), ((string?)use.Header, use.IsEnabled, (string?)delete.Header, delete.IsEnabled));

            window.UpdatePresetMenuCaptionsFor(new Preset(VanillaPresetName, false, true));
            Assert.Equal(("Use Preset", true, "Delete Preset", false), ((string?)use.Header, use.IsEnabled, (string?)delete.Header, delete.IsEnabled));
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
        public async Task DeletePresetMenuItem_RealAlert_RendersTheRuledCopy_AndReturnDeclines_KeepingThePreset() {
            var current = new Preset(VanillaPresetName, true, true);
            var spared = new Preset("Nonexistent Test Preset", false, false);
            var options = NewOptions(await GetCacheAsync(), [current, spared]);
            var window = new SettingsWindow(options);
            window.Show();

            Task deleting = window.SimulateDeletePresetMenuItemClickAsync(spared);
            Dispatcher.UIThread.RunJobs();
            AlertSheet alert = Assert.IsType<AlertSheet>(SheetHost.For(window).Content);
            string[] rendered = [
                alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Headline").Text ?? "",
                alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Informative").Text ?? "",
                .. alert.Buttons!.Items.Cast<Button>().Select(b => b.Content as string ?? ""),
            ];
            Assert.Equal(["Delete the preset “Nonexistent Test Preset”?",
                "Its items, recipes, and icons are removed from Foreman. You can’t undo this action.", "Delete", "Cancel"], rendered);
            Assert.Contains("destructive", alert.YesButtonControl.Classes);

            CanvasKeys.KeyDownOn(alert.YesButtonControl, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(deleting.IsCompleted);
            await deleting;

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
        public async Task ImportPresetButton_Click_NewPresetName_AddsItToTheList_AndTheSwitchAlertReadsTheRuledCopy() {
            var current = new Preset(VanillaPresetName, true, true);
            var options = NewOptions(await GetCacheAsync(), [current]);
            var window = new SettingsWindow(options);
            window.ImportPresetDialogStub = w => { w.SimulateImportSuccess("Brand New Preset"); return Task.CompletedTask; };
            int reloadRequests = 0;
            window.ReloadRequested += () => reloadRequests++;
            window.Show();

            Task importing = window.SimulateImportPresetClickAsync();
            Dispatcher.UIThread.RunJobs();
            AlertSheet alert = Assert.IsType<AlertSheet>(SheetHost.For(window).Content);
            Button[] buttons = [.. alert.Buttons!.Items.Cast<Button>()];
            Assert.Equal(["Switch to the new preset “Brand New Preset”?",
                "The import is saved either way; switching reloads the open graph with this preset’s items and recipes.", "Cancel", "Switch Preset"], [
                alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Headline").Text ?? "",
                alert.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Informative").Text ?? "",
                .. buttons.Select(b => b.Content as string ?? "")]);
            Assert.Same(buttons[1], alert.DefaultButton);

            CanvasKeys.KeyDownOn(alert.YesButtonControl, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            await importing;

            Assert.Contains(options.Presets!, p => p.Name == "Brand New Preset");
            Assert.Contains(window.PresetsTabControl.PresetListBoxControl.ItemsSource!.Cast<Preset>(), p => p.Name == "Brand New Preset");
            Assert.True(window.PresetsTabControl.ComparePresetsButtonControl.IsEnabled);
            Assert.Equal(0, reloadRequests);
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
        public async Task ComparePresetsButton_FewerThanTwoPresets_IsDisabledWithTheRuledTooltip_AndOpensNoAlert() {
            var window = new SettingsWindow(NewOptions(await GetCacheAsync(), [new Preset(VanillaPresetName, true, true)]));
            window.Show();
            bool compared = false;
            window.ComparePresetsDialogStub = _ => { compared = true; return Task.CompletedTask; };
            Button compare = window.PresetsTabControl.ComparePresetsButtonControl;

            await window.SimulateComparePresetsClickAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.False(compare.IsEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(compare));
            Assert.Equal("Import another preset to compare it with this one.", ToolTip.GetTip(compare));
            Assert.False(SheetHost.For(window).IsOpen);
            Assert.False(compared);
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

        private static readonly string[] CategoryNames = ["Assemblers", "Miners", "Power", "Beacons", "Modules", "Recipes", "Qualities"];

        private static ListBox ShowRecipes(SettingsWindow window) {
            window.EnabledObjectsTabControl.CategoryPopUpControl.SelectedItem = window.EnabledObjectsTabControl.CategoryPopUpControl.Items[5];
            Dispatcher.UIThread.RunJobs();
            return window.EnabledObjectsTabControl.ObjectListControl;
        }

        [AvaloniaFact]
        public async Task EnabledObjects_CategoryPopUp_ListsTheSevenCategoriesWithCounts_AndSwitchesTheList() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            SettingsViewModel vm = window.ViewModel;
            window.EnabledObjectsTabControl.ShowUnavailablesFilterCheckBoxControl.IsChecked = true;
            int[] all = [vm.AssemblerObjects.Count, vm.MinerObjects.Count, vm.PowerObjects.Count, vm.BeaconObjects.Count,
                vm.ModuleObjects.Count, vm.RecipeObjects.Count, vm.QualityObjects.Count];

            Assert.Equal(CategoryNames.Select((n, i) => $"{n} ({all[i]})"), window.EnabledObjectsTabControl.CategoryPopUpControl.Items);
            Assert.Equal(cache.Recipes.Count, all[5]);

            ListBox list = ShowRecipes(window);
            Assert.Same(vm.RecipeObjects, list.ItemsSource);
        }

        [AvaloniaFact]
        public async Task SubTabLists_ContainCountsMatchingTheFixtureCacheDictionaries() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            window.EnabledObjectsTabControl.ShowUnavailablesFilterCheckBoxControl.IsChecked = true;
            SettingsViewModel vm = window.ViewModel;

            Assert.Equal(cache.Assemblers.Values.Count(a => a.EntityType == EntityType.Assembler), vm.AssemblerObjects.Count);
            Assert.Equal(cache.Assemblers.Values.Count(a => a.EntityType is EntityType.Miner or EntityType.OffshorePump), vm.MinerObjects.Count);
            Assert.Equal(cache.Assemblers.Values.Count(a => a.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor), vm.PowerObjects.Count);
            Assert.Equal(cache.Beacons.Count, vm.BeaconObjects.Count);
            Assert.Equal(cache.Modules.Count, vm.ModuleObjects.Count);
            Assert.Equal(cache.Recipes.Count, vm.RecipeObjects.Count);
            Assert.Equal(cache.Qualities.Count, vm.QualityObjects.Count);
        }

        private static double TokenDouble(Control scope, string key) {
            Assert.True(scope.TryFindResource(key, scope.ActualThemeVariant, out object? value), key);
            return Assert.IsType<double>(value);
        }

        private static double ListMinimum(ListBox list, int rows) {
            Assert.True(list.TryFindResource("Foreman.Thickness.ListFramePadding", list.ActualThemeVariant, out object? padding));
            Thickness frame = Assert.IsType<Thickness>(padding);
            return (rows * TokenDouble(list, "Foreman.Size.ListRowHeight")) + frame.Top + frame.Bottom + list.BorderThickness.Top + list.BorderThickness.Bottom;
        }

        private static void AssertNoPaneScroller(UserControl pane, ListBox list) {
            Assert.DoesNotContain(list.GetVisualAncestors().TakeWhile(v => v != pane), v => v is ScrollViewer);
            Assert.True(double.IsNaN(list.Height), $"{list.Name} carries a local Height");
        }

        private static double BottomIn(Control control, Visual root) => control.TranslatePoint(new Point(0, control.Bounds.Height), root)!.Value.Y;

        [AvaloniaFact]
        public async Task EnabledObjects_ListFillsTheOnlyStarRow_WithNoPaneScroller_AndVirtualises() {
            var cache = await GetCacheAsync();
            Assert.True(cache.Recipes.Count > 50, "Fixture cache should carry enough recipes for virtualization to matter.");
            var window = NewEnabledObjectsWindow(cache);
            EnabledObjectsTab tab = window.EnabledObjectsTabControl;
            ListBox list = ShowRecipes(window);
            AnimationClock.UntilReflowsSettled(window);

            AssertNoPaneScroller(tab, list);
            Assert.Equal(ListMinimum(list, 4), list.MinHeight, 3);
            Assert.Empty(tab.GetVisualDescendants().OfType<FormGroup>());
            Assert.Empty(tab.GetVisualDescendants().OfType<FormRow>());
            Assert.True(list.Bounds.Height >= ListMinimum(list, 6), $"list is {list.Bounds.Height} pt at 520 × 480");
            ButtonRow bulk = tab.EnableAllButtonControl.GetVisualAncestors().OfType<ButtonRow>().First();
            Assert.Equal(tab.Bounds.Height - 20, BottomIn(bulk, tab), 1.0);

            Control[] fixedRows = [tab.FindControl<Control>("FilterLine")!, tab.FindControl<Control>("ShowLine")!, tab.FindControl<Control>("ListNote")!, bulk];
            double[] fixedHeights = [.. fixedRows.Select(c => c.Bounds.Height)];
            double listHeight = list.Bounds.Height;
            window.Height = 760;
            AnimationClock.UntilReflowsSettled(window);

            Assert.Equal(listHeight + 280, list.Bounds.Height, 1.0);
            Assert.Equal(fixedHeights, fixedRows.Select(c => c.Bounds.Height));

            double stack = list.Bounds.Height + fixedRows.Sum(c => c.Bounds.Height);
            window.Width = 900;
            AnimationClock.UntilReflowsSettled(window);

            Assert.Equal(stack, list.Bounds.Height + fixedRows.Sum(c => c.Bounds.Height), 1.0);
            Assert.Equal(tab.Bounds.Height - 20, BottomIn(bulk, tab), 1.0);

            Rect settled = list.Bounds;
            int passes = 0;
            window.LayoutUpdated += (_, _) => passes++;
            AnimationClock.Settle(300);
            Assert.Equal(settled, list.Bounds);
            Assert.Equal(0, passes);

            int shown = list.ItemsSource!.Cast<object>().Count();
            int realized = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
            Assert.True(realized > 0, "Expected at least one realized row.");
            Assert.True(realized < shown, $"Expected virtualization to realize fewer than {shown} shown rows, got {realized}.");
        }

        //--- Enabled Objects tab: filter row -----------------------------------------------------------------

        [AvaloniaFact]
        public async Task FilterRow_DefaultState_HidesUnavailableRecipes() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);

            Assert.Equal(cache.Recipes.Values.Count(r => r.Available), window.ViewModel.RecipeObjects.Count);
        }

        [AvaloniaFact]
        public async Task ShowUnavailablesFilterCheckBox_Checked_RevealsUnavailableRecipes_DimmedButStillCheckable() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);

            window.EnabledObjectsTabControl.ShowUnavailablesFilterCheckBoxControl.IsChecked = true;

            Assert.Equal(cache.Recipes.Count, window.ViewModel.RecipeObjects.Count);

            ListBox list = ShowRecipes(window);
            Assert.True(list.TryFindResource("Foreman.Opacity.Unavailable", list.ActualThemeVariant, out object? token) && token is double);
            double unavailableOpacity = (double)token!;
            EnabledObjectsListItem unavailable = window.ViewModel.RecipeObjects.First(r => r.IsUnavailable);
            EnabledObjectsListItem available = window.ViewModel.RecipeObjects.First(r => !r.IsUnavailable);

            Grid RowOf(EnabledObjectsListItem item) {
                list.ScrollIntoView(item);
                Dispatcher.UIThread.RunJobs();
                return list.ContainerFromItem(item)!.GetVisualDescendants().OfType<Grid>().First(g => g.DataContext == item);
            }

            Grid availableRow = RowOf(available);
            Assert.Equal(1, availableRow.Children.OfType<Image>().Single().Opacity);
            Assert.Equal(1, availableRow.Children.OfType<TextBlock>().Single().Opacity);

            Grid unavailableRow = RowOf(unavailable);
            Assert.Equal(unavailableOpacity, unavailableRow.Children.OfType<Image>().Single().Opacity);
            Assert.Equal(unavailableOpacity, unavailableRow.Children.OfType<TextBlock>().Single().Opacity);
            CheckBox box = unavailableRow.Children.OfType<CheckBox>().Single();
            Assert.Equal(1, box.Opacity);
            Assert.True(box.IsEnabled);

            box.BringIntoView();
            AnimationClock.Settle();
            AnimationClock.UntilReflowsSettled(window);
            Point centre = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window) ?? default;
            window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(unavailable.IsChecked);
            Assert.True(unavailable.DataObject.Enabled);
            Assert.Contains(unavailable.DataObject, window.Options.EnabledObjects);
        }

        [AvaloniaFact]
        public async Task FilterTextBox_FiltersRecipesByNameOrdinalIgnoreCase() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            IRecipe target = cache.Recipes.Values.First(r => r.Available);
            string needle = target.FriendlyName[..Math.Min(4, target.FriendlyName.Length)].ToUpperInvariant();

            window.EnabledObjectsTabControl.FilterFieldControl.Text = needle;

            var shown = window.ViewModel.RecipeObjects.ToList();
            Assert.NotEmpty(shown);
            Assert.All(shown, item => Assert.Contains(needle, item.Name, StringComparison.OrdinalIgnoreCase));
        }

        [AvaloniaFact]
        public async Task EnabledObjects_FilterMatchesNothing_ShowsTheStatePanelNoResults() {
            var window = NewEnabledObjectsWindow(await GetCacheAsync());
            EnabledObjectsTab tab = window.EnabledObjectsTabControl;
            ListBox list = ShowRecipes(window);
            Assert.False(tab.NoResultsPanelControl.IsVisible);
            AnimationClock.UntilReflowsSettled(window);
            Rect listBounds = list.Bounds;

            tab.FilterFieldControl.Text = "beaconium";
            tab.ShowSegmentedControl.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            StatePanel panel = tab.NoResultsPanelControl;
            Assert.True(panel.IsVisible);
            Assert.False(list.IsVisible);
            AnimationClock.UntilReflowsSettled(window);
            Assert.Equal(listBounds, panel.Bounds);
            Assert.Equal(StatePanelShape.NoResults, panel.Shape);
            Assert.Equal("No objects match “beaconium”", panel.Headline);
            Assert.Equal("Check the spelling, or clear the search and the filter to see every object.", panel.Sentence);
            Button showAll = Assert.Single(panel.Buttons!.Items.OfType<Button>());
            Assert.Equal("Show All Objects", showAll.Content);

            showAll.BringIntoView();
            AnimationClock.Settle();
            AnimationClock.UntilReflowsSettled(window);
            Point centre = showAll.TranslatePoint(new Point(showAll.Bounds.Width / 2, showAll.Bounds.Height / 2), window) ?? default;
            window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("", tab.FilterFieldControl.Text ?? "");
            Assert.Equal(0, tab.ShowSegmentedControl.SelectedIndex);
            Assert.False(panel.IsVisible);
            Assert.True(list.IsVisible);
            Assert.NotEmpty(list.ItemsSource!.Cast<object>());
        }

        //--- Enabled Objects tab: live check toggle -----------------------------------------------------------
        //R5: a checked row now writes straight onto the live DataObject.Enabled too, not just the Options
        //buffer - both are asserted below.

        [AvaloniaFact]
        public async Task CheckingARecipeRow_AddsItToEnabledObjectsAndEnablesItLiveImmediately() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            var item = window.ViewModel.RecipeObjects[0];
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
            var item = window.ViewModel.RecipeObjects[0];
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
            ListBox list = ShowRecipes(window);
            list.Focus();
            CanvasKeys.KeyDownOn(list, Key.A, KeyModifiers.Meta);
            var rows = window.ViewModel.RecipeObjects.ToList();

            rows[0].IsChecked = true;

            Assert.All(rows, row => Assert.True(row.IsChecked));
            Assert.True(window.Options.EnabledObjects.SetEquals(rows.Select(r => r.DataObject)));
        }

        [AvaloniaFact]
        public async Task TogglingARowsCheckbox_OutsideTheSelection_OnlyAffectsThatRow() {
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            ListBox list = ShowRecipes(window);
            var rows = window.ViewModel.RecipeObjects.ToList();
            list.SelectedItems!.Add(rows[0]);
            list.SelectedItems!.Add(rows[1]);

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
            ListBox list = ShowRecipes(window);
            list.Focus();

            CanvasKeys.KeyDownOn(list, Key.A, KeyModifiers.Meta);

            int totalRows = list.ItemsSource!.Cast<object>().Count();
            Assert.Equal(totalRows, list.SelectedItems!.Count);
            Assert.Empty(window.Options.EnabledObjects);
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): Ctrl+A on
        //Linux does the same thing Cmd+A does on macOS, via the UseIsMacOs seam.
        [AvaloniaFact]
        public async Task CtrlA_OnActiveSubTab_OnLinux_SelectsEveryRowWithoutCheckingThem() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            var cache = await GetCacheAsync();
            var window = NewEnabledObjectsWindow(cache);
            ListBox list = ShowRecipes(window);
            list.Focus();

            CanvasKeys.KeyDownOn(list, Key.A, KeyModifiers.Control);

            int totalRows = list.ItemsSource!.Cast<object>().Count();
            Assert.Equal(totalRows, list.SelectedItems!.Count);
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
        public async Task LoadEnabledFromSaveButton_Click_OutcomeAbort_ShowsTheRuledReSaveAlert() {
            var window = NewEnabledObjectsWindow(await GetCacheAsync());
            (string, string)? captured = null;
            window.WarningDialogStub = (title, message) => { captured = (title, message); return Task.CompletedTask; };
            window.LoadFromSaveDialogStub = w => {
                w.OpenSaveFilePathStub = () => Task.FromResult<string?>("/tmp/saves/mysave.zip");
                w.LoadPipelineStub = (_, _) => new SaveFileReader.Result { Outcome = SaveFileLoadOutcome.Abort };
                return w.RunAsync();
            };

            await window.SimulateLoadFromSaveClickAsync();

            Assert.Equal(
                ("Couldn’t read the save file.", "Open the save in Factorio, save it again, and then try loading it here."),
                captured);
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

            var recipeItem = window.ViewModel.RecipeObjects[0];
            var assemblerItem = window.ViewModel.AssemblerObjects[0];

            Assert.IsType<Image>(recipeItem.TooltipContent);
            Assert.Null(assemblerItem.TooltipContent);
        }

        //--- General/Advanced/Annotations/Appearance tabs: standalone binding + wiring checks ------------------
        //These four are plain SettingsViewModel-bound UserControls (reference task-4-brief.md's R7 split) -
        //no window services involved, so they're exercised directly with an explicit DataContext rather than
        //through the full SettingsWindow (which only attaches the active tab - see the Enabled Objects
        //section's own comment on that).

        [AvaloniaFact]
        public void SettingsPanes_CarryTheRuledValuesAndUpstreamRanges() {
            var general = new GeneralTab { DataContext = new SettingsViewModel() };
            var advanced = new AdvancedTab { DataContext = new SettingsViewModel() };
            var appearance = new AppearanceTab { DataContext = new SettingsViewModel() };

            Assert.Equal(["Low", "Medium", "High"], appearance.LevelOfDetailSegmentedControl.Items);
            Assert.Equal((0d, 2000d, 25d, 0), (appearance.NodeCountForSimpleViewStepperControl.Minimum, appearance.NodeCountForSimpleViewStepperControl.Maximum,
                appearance.NodeCountForSimpleViewStepperControl.Increment, appearance.NodeCountForSimpleViewStepperControl.Decimals));
            Assert.Equal((8d, 256d, 1d, 0), (appearance.IconsSizeStepperControl.Minimum, appearance.IconsSizeStepperControl.Maximum,
                appearance.IconsSizeStepperControl.Increment, appearance.IconsSizeStepperControl.Decimals));

            Assert.Equal(AssemblerSelector.StyleNames, general.AssemblerSelectorStylePopUpControl.Items);
            Assert.Equal(ModuleSelector.StyleNames, general.ModuleSelectorStylePopUpControl.Items);
            Assert.Equal(["Up", "Down"], general.NodeDirectionSegmentedControl.Items);
            Assert.Equal((1d, 20d, 1d, 0), (general.QualityStepsStepperControl.Minimum, general.QualityStepsStepperControl.Maximum,
                general.QualityStepsStepperControl.Increment, general.QualityStepsStepperControl.Decimals));
            Assert.Equal((1d, 6d, 0.2d, 1), (advanced.LowPriorityPowerStepperControl.Minimum, advanced.LowPriorityPowerStepperControl.Maximum,
                advanced.LowPriorityPowerStepperControl.Increment, advanced.LowPriorityPowerStepperControl.Decimals));
            Assert.Equal((0d, 5d, 0.1d, 1), (advanced.PullConsumerNodesPowerStepperControl.Minimum, advanced.PullConsumerNodesPowerStepperControl.Maximum,
                advanced.PullConsumerNodesPowerStepperControl.Increment, advanced.PullConsumerNodesPowerStepperControl.Decimals));
        }

        [AvaloniaFact]
        public void GeneralTab_ControlsBindTwoWayToViewModel() {
            var vm = new SettingsViewModel();
            var tab = new GeneralTab { DataContext = vm };

            tab.QualityStepsStepperControl.Value = 12;
            tab.AssemblerSelectorStylePopUpControl.SelectedItem = AssemblerSelector.StyleNames[3];
            tab.NodeDirectionSegmentedControl.SelectedIndex = 1;
            Assert.Equal((12m, 3, 1), (vm.QualityStepsValue, vm.AssemblerSelectorStyleIndex, vm.NodeDirectionIndex));

            vm.QualityStepsValue = 7m;
            vm.ModuleSelectorStyleIndex = 2;
            Assert.Equal(7d, tab.QualityStepsStepperControl.Value);
            Assert.Equal(ModuleSelector.StyleNames[2], tab.ModuleSelectorStylePopUpControl.SelectedItem);
        }

        [AvaloniaFact]
        public void AdvancedTab_OutputPullStrength_IsDisabledWhileMaximizeIsOff_AndKeepsItsSentence() {
            var vm = new SettingsViewModel { PullConsumerNodesChecked = false };
            var tab = new AdvancedTab { DataContext = vm };

            Assert.False(tab.PullConsumerNodesPowerStepperControl.IsEnabled);
            FormRow row = tab.PullConsumerNodesPowerStepperControl.FindLogicalAncestorOfType<FormRow>()!;
            Assert.True(row.IsEnabled);
            Assert.Equal(1, row.Opacity);

            tab.PullConsumerNodesSwitchControl.IsChecked = true;
            Assert.True(vm.PullConsumerNodesChecked);
            Assert.True(tab.PullConsumerNodesPowerStepperControl.IsEnabled);
        }

        [AvaloniaFact]
        public void AnnotationsTab_ArrowSwitches_BindTwoWayToViewModel() {
            var vm = new SettingsViewModel();
            var tab = new AnnotationsTab { DataContext = vm };

            tab.ErrorArrowsSwitchControl.IsChecked = true;
            tab.FlagOUSupplyNodesSwitchControl.IsChecked = true;

            Assert.True(vm.ErrorArrowsChecked);
            Assert.True(vm.FlagOUSupplyNodesChecked);
        }
    }
}
