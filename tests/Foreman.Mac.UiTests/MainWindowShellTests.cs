using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class MainWindowShellTests {
        //Task 4 (contract #toolbar): the unified titlebar toolbar keeps the ten highest-frequency commands
        //as buttons; Save As/Import Graph/Help move to menu-bar-only (BuildNativeMenu already wires all
        //three - see NativeMenu_FileNewSaveImportAndExportAreEnabled below and the Help menu item).
        private static readonly string[] ExpectedButtonNames = [
            "New Graph", "Load", "Save", "Export Image",
            "Add Item", "Add Recipe", "Autoconnect", "Settings",
            "Graph Summary", "Align Selected",
        ];

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

        private static string FlowchartJson() {
            string repoRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
            return File.ReadAllText(Path.Combine(repoRoot, "tests", "ForemanTest", "assets", "Flowchart.fjson"));
        }

        [AvaloniaFact]
        public void Toolbar_ContainsAllTenButtons() {
            var window = new MainWindow();
            window.Show();

            var names = window.GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName).ToList();

            foreach (string expected in ExpectedButtonNames)
                Assert.Contains(expected, names);
        }

        //Task 4 (phase 9 toolbar rebuild): Help's toolbar button was retired along with Save As/Import Graph
        //(menu-bar-only now), so every remaining toolbar button is a wired, enabled command. Align Selected
        //is excluded here since task 7 gives it a selection-driven CanExecute (contract #toolbar's
        //.disabled empty-selection state) - a fresh window has no selection, so it starts disabled; its own
        //states are asserted separately below.
        [AvaloniaFact]
        public void Toolbar_AllTenButtonsAreEnabled() {
            var window = new MainWindow();
            window.Show();

            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(b => ExpectedButtonNames.Contains(AutomationProperties.GetName(b)))
                .ToDictionary(b => AutomationProperties.GetName(b)!, b => b.IsEnabled);

            Assert.Equal(ExpectedButtonNames.Length, buttons.Count);
            foreach (string name in ExpectedButtonNames.Where(n => n != "Align Selected"))
                Assert.True(buttons[name], name);
        }

        //Task 4 new: the contract retires Save As/Import Graph/Help from the toolbar, but every one of the
        //13 ShellViewModel commands must still be reachable from somewhere - the toolbar's 10 buttons plus
        //BuildNativeMenu's own bindings for the other 3 (Save As, Import, Help all already wired there).
        [AvaloniaFact]
        public void AllThirteenCommands_AreReachableFromToolbarOrNativeMenu() {
            var window = new MainWindow();
            window.Show();

            var commandsByProperty = typeof(ShellViewModel).GetProperties()
                .Where(p => typeof(IRelayCommand).IsAssignableFrom(p.PropertyType))
                .ToDictionary(p => p.Name, p => (IRelayCommand)p.GetValue(window.Commands)!);
            Assert.Equal(13, commandsByProperty.Count);

            var toolbarCommands = window.GetVisualDescendants().OfType<Button>()
                .Select(b => b.Command).OfType<IRelayCommand>().ToHashSet();
            //C1 wraps every menu item's Command in DeferredMenuCommand; unwrap to compare against the raw instances.
            var menuCommands = AllNativeMenuItems(NativeMenu.GetMenu(window)!)
                .Select(i => i.Command).OfType<MainWindow.DeferredMenuCommand>()
                .Select(d => d.Inner).OfType<IRelayCommand>().ToHashSet();

            foreach ((string name, IRelayCommand command) in commandsByProperty)
                Assert.True(toolbarCommands.Contains(command) || menuCommands.Contains(command), name);
        }

        private static IEnumerable<NativeMenuItem> AllNativeMenuItems(NativeMenu menu) {
            foreach (NativeMenuItem item in menu.Items.OfType<NativeMenuItem>()) {
                yield return item;
                if (item.Menu is NativeMenu submenu)
                    foreach (NativeMenuItem nested in AllNativeMenuItems(submenu))
                        yield return nested;
            }
        }

        //Task 4: the empty titlebar background is the drag region; a button sitting on top of it must
        //intercept its own click first (macos-design: buttons stay clickable, not draggable).
        [AvaloniaFact]
        public void TitleBar_EmptyBackgroundPress_RequestsMoveDrag_ButtonPress_DoesNot() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");
            Avalonia.Point emptyPoint = WindowCenterOf(titleBar, window); //center of the titlebar's flexible middle column - empty of buttons, and the title TextBlock there has no Background so pointer events pass through to the Grid
            window.MouseDown(emptyPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(emptyPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(1, window.TitleBarDragRequestCount);

            RealClick(window, "Settings");

            Assert.Equal(1, window.TitleBarDragRequestCount);
        }

        //Task 4: double-clicking the empty titlebar toggles maximize, same as a native macOS titlebar
        //(macos-design: preserve system window behaviors, including zoom).
        [AvaloniaFact]
        public void TitleBar_EmptyBackgroundDoubleClick_TogglesMaximizedState() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Maximized, window.WindowState);

            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");
            Avalonia.Point emptyPoint = WindowCenterOf(titleBar, window); //center of the titlebar's flexible middle column - empty of buttons, and the title TextBlock there has no Background so pointer events pass through to the Grid
            window.MouseDown(emptyPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(emptyPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseDown(emptyPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(emptyPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(WindowState.Normal, window.WindowState);
        }

        //Task 4: contract #toolbar groups New/Load/Save first, then Add Item/Add Recipe/Autoconnect/Align
        //Selected, then Export Image/Graph Summary/View Options - proven here by X-position ordering rather
        //than assuming Avalonia enumerates visual children left-to-right.
        [AvaloniaFact]
        public void Toolbar_ButtonsAreOrderedPerContractGroups() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            string[] expectedOrder = [
                "New Graph", "Load", "Save",
                "Add Item", "Add Recipe", "Autoconnect", "Align Selected",
                "Export Image", "Graph Summary", "View Options",
            ];
            double[] positions = [.. expectedOrder
                .Select(name => window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name))
                .Select(b => WindowCenterOf(b, window).X)];

            for (int i = 1; i < positions.Length; i++)
                Assert.True(positions[i] > positions[i - 1], $"{expectedOrder[i]} should sit right of {expectedOrder[i - 1]}");
        }

        //Task 4: both-theme render sanity on the titlebar surface (phase 5b ContrastAssert pattern) - the
        //title text and toolbar glyphs must resolve different, legible colors in light vs dark.
        [AvaloniaFact]
        public void TitleBar_ThemedAcrossLightAndDark_ResolvesDifferentColors() {
            var app = (App)Application.Current!;

            app.ApplyTheme(ThemeMode.Light);
            var lightWindow = new MainWindow();
            lightWindow.Show();
            Color lightTitlebar = TitleBarBackgroundColor(lightWindow);
            Color lightGlyph = ToolbarGlyphColor(lightWindow, "New Graph");

            app.ApplyTheme(ThemeMode.Dark);
            var darkWindow = new MainWindow();
            darkWindow.Show();
            Color darkTitlebar = TitleBarBackgroundColor(darkWindow);
            Color darkGlyph = ToolbarGlyphColor(darkWindow, "New Graph");

            Assert.NotEqual(lightTitlebar, darkTitlebar);
            Assert.NotEqual(lightGlyph, darkGlyph);
        }

        private static Color TitleBarBackgroundColor(MainWindow window) {
            var titleBar = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "TitleBarRow");
            return Assert.IsAssignableFrom<ISolidColorBrush>(titleBar.Background).Color;
        }

        private static Color ToolbarGlyphColor(MainWindow window, string buttonAutomationName) {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == buttonAutomationName);
            var glyph = button.GetVisualDescendants().OfType<ShapePath>().Single(p => p.Classes.Contains("foreman-toolbar-glyph"));
            return Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Stroke).Color;
        }

        //Task 8 (contract #toolbar, §05 B): every action button is icon+label now - a vector Path (lifted
        //from the mock's own SVGs) plus a text label, never one without the other.
        [AvaloniaFact]
        public void Toolbar_EveryActionButton_HasPathIconAndLabel() {
            var window = new MainWindow();
            window.Show();

            foreach (string name in ExpectedButtonNames.Append("View Options")) {
                var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
                Assert.True(button.Classes.Contains("foreman-toolbar-wide"), name);
                Assert.Contains(button.GetVisualDescendants().OfType<ShapePath>(), p => p.Classes.Contains("foreman-toolbar-glyph"));
                var label = button.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(t => t.Classes.Contains("foreman-toolbar-label"));
                Assert.False(string.IsNullOrWhiteSpace(label?.Text), name);
            }
        }

        //Task 8 (contract #05 B "Toolbar action labels", ratified): the exact short label text per button.
        [AvaloniaFact]
        public void Toolbar_ActionButtons_ShowRatifiedLabelText() {
            var window = new MainWindow();
            window.Show();

            var expectedLabels = new Dictionary<string, string> {
                ["New Graph"] = "New",
                ["Load"] = "Load",
                ["Save"] = "Save",
                ["Add Item"] = "Item",
                ["Add Recipe"] = "Recipe",
                ["Autoconnect"] = "Autoconnect",
                ["Align Selected"] = "Align",
                ["Export Image"] = "Export",
                ["Graph Summary"] = "Summary",
                ["View Options"] = "View Options",
                ["Settings"] = "Settings",
            };

            foreach ((string automationName, string expectedLabel) in expectedLabels) {
                var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == automationName);
                var label = button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("foreman-toolbar-label"));
                Assert.Equal(expectedLabel, label.Text);
            }
        }

        //Task 8 (contract #toolbar's document title, ratified "Iron Plate Line — Edited"): two Runs so the
        //suffix can carry its own weight/color, live-tracking IsDirty without rebuilding the TextBlock.
        [AvaloniaFact]
        public void TitleBarTitle_TwoRuns_TrackIsDirtyLive() {
            var window = new MainWindow();
            window.Show();
            window.ViewModel.CurrentFileName = "Iron Plate Line.fjson";
            Dispatcher.UIThread.RunJobs();

            var title = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("foreman-window-title"));
            var runs = title.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
            Assert.Equal(2, runs.Count);
            Assert.Equal("Iron Plate Line", runs[0].Text);
            Assert.Equal("", runs[1].Text);

            window.ViewModel.IsDirty = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(" — Edited", runs[1].Text);
        }

        //Fix round 1 (popover redo, contract #decisions §1 mockup): View Options is a Popup anchored under
        //its toolbar button now, not a docked Grid.Row strip - opens it first via the same command the
        //button click drives, the open-then-locate shape DialogWindowsEndToEndTests already uses elsewhere.
        private static void OpenViewOptions(MainWindow window) {
            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void ViewOptionsPopover_DefaultsToClosed() {
            var window = new MainWindow();
            window.Show();

            var popover = window.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "ViewOptionsPopover");

            Assert.False(popover.IsOpen);
        }

        [AvaloniaFact]
        public void ViewOptionsButton_RealClick_OpensThenClosesThePopover() {
            var window = new MainWindow();
            window.Show();
            var popover = window.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "ViewOptionsPopover");

            RealClick(window, "View Options");
            Assert.True(popover.IsOpen);

            RealClick(window, "View Options");
            Assert.False(popover.IsOpen);
        }

        //Fix round 2 (Jozef's live finding): a macOS popover light-dismisses on an outside click rather
        //than waiting for its own button to be pressed again.
        [AvaloniaFact]
        public void ViewOptionsPopover_PointerPressOutside_ClosesIt() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var popover = window.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "ViewOptionsPopover");

            Avalonia.Point outsidePoint = WindowCenterOf(window.GraphCanvas, window);
            window.MouseDown(outsidePoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(popover.IsOpen);
            Assert.False(window.ViewModel.IsViewOptionsOpen);
        }

        //The dismissing press must be consumed, not passed through to the button underneath - otherwise
        //the same click that closes it immediately reopens it.
        [AvaloniaFact]
        public void ViewOptionsButton_RealClick_WhileOpen_ClosesAndStaysClosed() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var popover = window.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "ViewOptionsPopover");

            RealClick(window, "View Options");
            Dispatcher.UIThread.RunJobs();

            Assert.False(popover.IsOpen);
            Assert.False(window.ViewModel.IsViewOptionsOpen);
        }

        //Contract #decisions §1: "anchored under the button", not a full-width docked strip.
        [AvaloniaFact]
        public void ViewOptionsPopover_AnchoredUnderViewOptionsButton() {
            var window = new MainWindow();
            window.Show();
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "View Options");
            var popover = window.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "ViewOptionsPopover");

            Assert.Same(button, popover.PlacementTarget);
            Assert.Equal(PlacementMode.Bottom, popover.Placement);
        }

        //Contract #decisions §1: a compact rounded card, not the old full-width Grid.Row strip.
        [AvaloniaFact]
        public void ViewOptionsPopover_WhenOpen_CardIsCompactNotFullWindowWidth() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);

            var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ViewOptionsCard");

            Assert.True(card.Bounds.Width < window.Width / 2,
                $"expected a compact card, got width {card.Bounds.Width} against window width {window.Width}");
        }

        [AvaloniaFact]
        public void GridlinesAndGraphOptionsControls_AreEnabled() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);

            foreach (string name in new[] { "MinorGridlinesDropDown", "MajorGridlinesDropDown", "RateOptionsDropDown" })
                Assert.True(window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == name).IsEnabled);
            foreach (string name in new[] { "GridlinesCheckbox", "IconViewCheckBox", "PauseUpdatesCheckbox" })
                Assert.True(window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == name).IsEnabled);
        }

        //Ports GraphViewer_KeyDown's Space handler (reference §7): wired as a separate KeyDown subscription
        //on GraphCanvas rather than the control's own OnKeyDown override, since it needs to reach the
        //toolbar's gridlines checkbox.
        [AvaloniaFact]
        public void SpaceKey_OnGraphCanvas_TogglesShowGridAndSyncsTheGridlinesCheckbox() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GridlinesCheckbox");
            window.GraphCanvas.Grid.ShowGrid = false;
            checkbox.IsChecked = false;
            window.GraphCanvas.Focus();

            CanvasKeys.PressOnCanvas(window.GraphCanvas, Key.Space);

            Assert.True(window.GraphCanvas.Grid.ShowGrid);
            Assert.True(checkbox.IsChecked);

            CanvasKeys.PressOnCanvas(window.GraphCanvas, Key.Space);

            Assert.False(window.GraphCanvas.Grid.ShowGrid);
            Assert.False(checkbox.IsChecked);
        }

        //Retires docs/upstream-divergences.md's WASD-S/Cmd+S entry: plain S still pans, but Cmd+S must reach
        //Save (the NativeMenu KeyGesture) instead of also panning the view underneath it. A node is required
        //so Graph.Bounds is non-empty - Viewport.UpdateGraphBounds forces ViewOffset back to (0,0) whenever
        //the graph is empty, regardless of any pan delta, so an empty graph can't observe panning at all.
        //
        //Dispatches via RaiseEvent(KeyDownEvent) on GraphCanvas rather than window.KeyPressQwerty: the
        //latter routes through the process-wide IKeyboardDevice's ambient FocusedElement, which every other
        //Avalonia.Headless test collection shares on the one Dispatcher.UIThread - a concurrent collection's
        //own Focus() call can win that race and steal the key event before it ever reaches this canvas (see
        //SKey_OnGraphCanvas_PansEvenWhenAmbientFocusIsStolen for a deterministic reproduction). Raising the
        //event straight on GraphCanvas still runs the exact production handler (OnKeyDown), just without
        //depending on which window last touched that shared singleton.
        [AvaloniaFact]
        public async Task SKey_OnGraphCanvas_PansOnlyWithoutMeta() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(2000, 2000));
            window.GraphCanvas.Focus();
            System.Drawing.Point before = window.GraphCanvas.Viewport.ViewOffset;

            window.GraphCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, KeyModifiers = KeyModifiers.Meta });

            Assert.Equal(before, window.GraphCanvas.Viewport.ViewOffset);

            window.GraphCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S });

            Assert.NotEqual(before, window.GraphCanvas.Viewport.ViewOffset);
        }

        //Regression for the flake above: a second window stealing the shared IKeyboardDevice's
        //FocusedElement between our Focus() call and the key dispatch used to make the S press land
        //nowhere (window.KeyPressQwerty routes through that ambient singleton). Dispatching straight at
        //GraphCanvas is immune, since it never consults FocusedElement.
        [AvaloniaFact]
        public async Task SKey_OnGraphCanvas_PansEvenWhenAmbientFocusIsStolen() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(2000, 2000));
            window.GraphCanvas.Focus();
            System.Drawing.Point before = window.GraphCanvas.Viewport.ViewOffset;

            var thief = new Window { Content = new TextBox() };
            thief.Show();
            ((TextBox)thief.Content!).Focus();

            window.GraphCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S });

            Assert.NotEqual(before, window.GraphCanvas.Viewport.ViewOffset);
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): Ctrl+S gates
        //the pan on Linux the same way Cmd+S does on macOS, via the UseIsMacOs seam.
        //Dispatches via RaiseEvent rather than KeyPressQwerty for the same ambient-focus reason as the test above.
        [AvaloniaFact]
        public async Task SKey_OnGraphCanvas_OnLinux_PansOnlyWithoutCtrl() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.Show();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(2000, 2000));
            window.GraphCanvas.Focus();
            System.Drawing.Point before = window.GraphCanvas.Viewport.ViewOffset;

            window.GraphCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S, KeyModifiers = KeyModifiers.Control });

            Assert.Equal(before, window.GraphCanvas.Viewport.ViewOffset);

            window.GraphCanvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.S });

            Assert.NotEqual(before, window.GraphCanvas.Viewport.ViewOffset);
        }

        //Regression: ApplyLoadedSettings must seed GraphCanvas.Viewer.IconsOnly from settings.IconsOnlyView
        //before the Icon View checkbox reads it (mirroring how it already seeds Graph.SelectedRateUnit
        //before the rate-unit dropdown reads it) - otherwise a persisted "true" is silently discarded on
        //every launch, since BindViewOptionControls only ever reads Viewer.IconsOnly back into the checkbox.
        [AvaloniaFact]
        public void ApplyLoadedSettings_IconsOnlyViewTrue_SeedsViewerAndChecksTheIconViewBox() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { IconsOnlyView = true };

            window.ApplyLoadedSettings();

            Assert.True(window.GraphCanvas.Viewer.IconsOnly);
            OpenViewOptions(window);
            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconViewCheckBox");
            Assert.True(checkbox.IsChecked);
        }

        //Regression: ApplyLoadedSettings only ever wired the gridline/rate-unit/icon-view/pause-updates
        //controls into the canvas; everything else upstream's MainForm_Load also applies at launch
        //(LevelOfDetail, ArrowsOnLinks, NodeCountForSimpleView, IconsSize, DynamicLineWidth,
        //FlagOUSuppliedNodes, RoundAssemblerCount, AbbreviateSciPacks, ShowRecipeToolTip, and the four
        //guide-arrow toggles) stayed at code-level defaults regardless of what was saved.
        [AvaloniaFact]
        public void ApplyLoadedSettings_NonDefaultSettings_SeedsContextViewerAndArrowRenderer() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings {
                LevelOfDetail = (int)LevelOfDetail.High,
                ArrowsOnLinks = true,
                NodeCountForSimpleView = 42,
                IconsSize = 64,
                DynamicLineWidth = true,
                FlagOUSuppliedNodes = true,
                RoundAssemblerCount = true,
                AbbreviateSciPacks = false,
                ShowRecipeToolTip = false,
                ShowErrorArrows = true,
                ShowWarningArrows = true,
                ShowDisconnectedArrows = true,
                ShowOUSuppliedArrows = true,
            };

            window.ApplyLoadedSettings();

            NodeElementContext context = window.GraphCanvas.Viewer.Context;
            Assert.Equal(LevelOfDetail.High, context.LevelOfDetail);
            Assert.True(context.ArrowsOnLinks);
            Assert.Equal(64, context.IconsDrawSize);
            Assert.True(context.DynamicLinkWidth);
            Assert.True(context.FlagOUSuppliedNodes);
            Assert.True(context.RoundAssemblerCount);
            Assert.False(context.AbbreviateSciPacks);
            Assert.False(context.ShowRecipeToolTip);
            Assert.Equal(42, window.GraphCanvas.Viewer.NodeCountForSimpleView);

            PointingArrowRenderer arrows = window.GraphCanvas.Viewer.ArrowRenderer;
            Assert.True(arrows.ShowErrorArrows);
            Assert.True(arrows.ShowWarningArrows);
            Assert.True(arrows.ShowDisconnectedArrows);
            Assert.True(arrows.ShowOUNodeArrows);
        }

        //Regression: ApplyLoadedSettings never seeded EnableExtraProductivityForNonMiners (upstream
        //MainForm.cs:102), so a persisted "true" silently reverted to the code-level false default on every
        //launch; seeds both Graph (the value LoadDocument reads/writes) and Context (what RecipeNodeElement
        //actually checks while drawing), mirroring the pair LoadDocument already keeps in sync.
        [AvaloniaFact]
        public void ApplyLoadedSettings_EnableExtraProductivityForNonMinersTrue_SeedsGraphAndContext() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { EnableExtraProductivityForNonMiners = true };

            window.ApplyLoadedSettings();

            Assert.True(window.GraphCanvas.Viewer.Graph.EnableExtraProductivityForNonMiners);
            Assert.True(window.GraphCanvas.Viewer.Context.EnableExtraProductivityForNonMiners);
        }

        //Regression (task 7, reference §8): SmartNodeDirection/DefaultNodeDirection were never read off
        //AppSettings anywhere - GraphCanvas.SmartNodeDirection stayed permanently false and Graph.
        //DefaultNodeDirection stayed permanently Up regardless of what was saved.
        [AvaloniaFact]
        public void ApplyLoadedSettings_SmartNodeDirectionAndDefaultNodeDirection_SeedsCanvasAndGraph() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { SmartNodeDirection = false, DefaultNodeDirection = NodeDirection.Down };

            window.ApplyLoadedSettings();

            Assert.False(window.GraphCanvas.SmartNodeDirection);
            Assert.Equal(NodeDirection.Down, window.GraphCanvas.Viewer.Graph.DefaultNodeDirection);
        }

        //Regression: ApplyLoadedSettings never applied FlagDarkMode to the canvas itself - App.ApplyTheme
        //only swapped the window chrome's ThemeVariant, leaving GraphViewer.BackgroundColor stuck on its
        //light-mode default (upstream MainForm.SetDarkMode/SetLightMode recolors the graph on every load).
        [AvaloniaFact]
        public void ApplyLoadedSettings_DarkTheme_AppliesDarkThemeToViewer() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Dark };

            window.ApplyLoadedSettings();

            Assert.Equal(new SKColor(0x1E, 0x1E, 0x20), window.GraphCanvas.Viewer.BackgroundColor);
        }

        [AvaloniaFact]
        public void ApplyLoadedSettings_LightTheme_AppliesLightThemeToViewer() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Dark };
            window.ApplyLoadedSettings();

            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Light };
            window.ApplyLoadedSettings();

            Assert.Equal(new SKColor(0xFA, 0xFA, 0xFA), window.GraphCanvas.Viewer.BackgroundColor);
        }

        //Regression (human nit 1, upstream MainForm.cs:36-46): a Settings Confirm with "Enable Dark Mode"
        //unchecked now commits ThemeMode.Light (see SettingsWindow.CommitGraphOptionsTab), which must force
        //both the canvas and the app chrome to light even with the app already themed dark - simulating an
        //OS whose own appearance is dark, since Avalonia's RequestedThemeVariant otherwise wins over it.
        [AvaloniaFact]
        public void ApplyLoadedSettings_LightTheme_ForcesLightRegardlessOfAmbientDarkSystem() {
            var app = (App)Application.Current!;
            app.ApplyTheme(ThemeMode.Dark);

            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Light };

            window.ApplyLoadedSettings();
            app.ApplyTheme(window.Settings.FlagDarkMode);

            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            Assert.Equal(new SKColor(0xFA, 0xFA, 0xFA), window.GraphCanvas.Viewer.BackgroundColor);
        }

        //I-1 (final fix wave regression): OpenSettingsAsync's own tail used to run ApplyLoadedSettings
        //(which reads Application.Current.ActualThemeVariant for FlagDarkMode.System's isDark check)
        //before App.ApplyTheme actually updated RequestedThemeVariant, so a System Confirm read the stale
        //pre-Confirm variant instead of the one it had just committed. With the app forced Dark going in
        //and nothing else overriding this headless harness's own Light platform default, a correctly
        //ordered Confirm to System must land the canvas Light, not stay stuck on the pre-Confirm Dark.
        [AvaloniaFact]
        public async Task OpenSettingsAsync_ConfirmSystemWhileAppCurrentlyDark_CanvasResolvesLight() {
            var app = (App)Application.Current!;
            app.ApplyTheme(ThemeMode.Dark);

            var window = new MainWindow();
            window.Show();
            window.DataCache = MinimalCacheWithDefaultQuality();
            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Dark };
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();

            window.SettingsDialogStub = options => {
                options.FlagDarkMode = ThemeMode.System;
                return Task.FromResult<bool?>(true);
            };

            await window.OpenSettingsAsync();

            Assert.Equal(ThemeVariant.Light, Application.Current!.ActualThemeVariant);
            Assert.Equal(new SKColor(0xFA, 0xFA, 0xFA), window.GraphCanvas.Viewer.BackgroundColor);
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        //Regression (task 7): DCache/DefaultAssemblerQuality were only ever assigned inside LoadDocument, so
        //a fresh/New graph had neither before the first file load - blocking recipe-node creation and this
        //task's Add Item/Add Recipe choosers (both need Viewer.Context.DCache) from ever working at boot.
        [AvaloniaFact]
        public void ApplyLoadedSettings_DataCacheAssigned_SeedsViewerDCacheAndGraphDefaultAssemblerQuality() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = MinimalCacheWithDefaultQuality();
            window.DataCache = cache;
            window.Settings = new AppSettings();

            window.ApplyLoadedSettings();

            Assert.Same(cache, window.GraphCanvas.Viewer.Context.DCache);
            Assert.Same(cache.DefaultQuality, window.GraphCanvas.Viewer.Graph.DefaultAssemblerQuality);
        }

        private static DataCache MinimalCacheWithDefaultQuality() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var store = (DataCacheStore)field.GetValue(cache)!;
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;
            return cache;
        }

        //Regression: upstream clamps IconsSize into [8,256] before applying it (MainForm.cs:117-119); an
        //out-of-range persisted value (from an older build, a hand-edited settings.json, etc.) must not
        //reach the canvas unclamped.
        [AvaloniaFact]
        public void ApplyLoadedSettings_IconsSizeOutOfRange_ClampsToEightAndTwoFiftySix() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings { IconsSize = 4 };

            window.ApplyLoadedSettings();

            Assert.Equal(8, window.GraphCanvas.Viewer.Context.IconsDrawSize);

            window.Settings = new AppSettings { IconsSize = 4000 };
            window.ApplyLoadedSettings();

            Assert.Equal(256, window.GraphCanvas.Viewer.Context.IconsDrawSize);
        }

        //Regression: LoadDocument sets Graph.SelectedRateUnit from the save but nothing resynced
        //RateOptionsDropDown.SelectedIndex or persisted it back to settings (upstream's
        //ApplyLoadedGraphUiState does both, first thing, reference MainForm.cs:227-231).
        //ResyncRateOptionsDropDown is LoadGraphAsync's post-load step (internal, exercised directly here -
        //LoadGraphAsync itself needs a real file-picker StorageProvider this headless test has none of).
        [AvaloniaFact]
        public async Task LoadDocument_SaveWithNonDefaultRateUnit_ResyncDropDownMatchesSettingsAndAvoidsDoubleSolve() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            DataCache cache = await GetCacheAsync();

            GraphLoadResult result = window.GraphCanvas.Viewer.LoadDocument(cache, FlowchartJson());
            Assert.True(result.Success, result.ErrorMessage);
            ProductionGraph.RateUnit expectedUnit = window.GraphCanvas.Viewer.Graph.SelectedRateUnit;

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;
            window.ResyncRateOptionsDropDown();

            OpenViewOptions(window);
            var rateOptions = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "RateOptionsDropDown");
            Assert.Equal((int)expectedUnit, rateOptions.SelectedIndex);
            Assert.Equal(expectedUnit, window.Settings!.DefaultRateUnit);
            Assert.Equal(0, solveCount); //setting the dropdown's index alone must not re-trigger UpdateNodeValues
        }

        //Regression: BindViewOptionControls subscribed its six control event handlers with += on every call,
        //never unsubscribing first (unlike its own ResyncRateOptionsDropDown pattern above). It's called from
        //boot, ReloadForPresetAsync, and now every OpenSettingsAsync Confirm, so a second ApplyLoadedSettings
        //call left two PauseUpdatesCheckbox handlers stacked - one user toggle then fired UpdateNodeValues
        //twice instead of once.
        [AvaloniaFact]
        public void ApplyLoadedSettings_CalledTwice_TogglingPauseUpdatesStillResolvesExactlyOnce() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            window.ApplyLoadedSettings();

            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            OpenViewOptions(window);
            var pauseUpdates = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "PauseUpdatesCheckbox");
            pauseUpdates.IsChecked = true;
            pauseUpdates.IsChecked = false; //resuming updates fires UpdateNodeValues once per subscribed handler

            Assert.Equal(1, solveCount);
        }

        //Minor#4 (final fix wave): BindViewOptionControls wrote rateOptions.SelectedIndex directly, with the
        //handler already subscribed after the first bind - a second ApplyLoadedSettings call (e.g. a preset-
        //switching Confirm) that also changed settings.DefaultRateUnit fired OnRateOptionsSelectionChanged
        //and re-solved, on top of the apply-tail's own explicit solve.
        [AvaloniaFact]
        public void ApplyLoadedSettings_CalledTwiceWithChangedRateUnit_ResyncsWithoutExtraSolve() {
            var window = new MainWindow();
            window.Show();
            var settings = new AppSettings { DefaultRateUnit = ProductionGraph.RateUnit.Per1Min };
            window.Settings = settings;
            window.ApplyLoadedSettings();

            settings.DefaultRateUnit = ProductionGraph.RateUnit.Per1Hour;
            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            window.ApplyLoadedSettings();

            OpenViewOptions(window);
            var rateOptions = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "RateOptionsDropDown");
            Assert.Equal((int)ProductionGraph.RateUnit.Per1Hour, rateOptions.SelectedIndex);
            Assert.Equal(0, solveCount);
        }

        //Regression: New used to detach GraphCanvas from GraphViewerHost entirely (Content = null), leaving
        //a subsequent Load rendering into an off-tree control. Upstream's NewGraph() only ever clears the
        //live viewer's graph (GraphViewer.ClearGraph() -> Graph.ClearGraph()) and never touches the host.
        [AvaloniaFact]
        public async Task NewGraphCommand_Execute_ClearsViewerButKeepsCanvasMounted_ThenLoadStillRenders() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, System.Drawing.Point.Empty);
            Assert.NotEmpty(window.GraphCanvas.Viewer.NodeElements);
            int redrawCountBeforeNew = window.GraphCanvas.RedrawRequestCount;

            window.Commands.NewCommand.Execute(null);

            Assert.Empty(window.GraphCanvas.Viewer.NodeElements);
            Assert.Same(window.GraphCanvas, window.GraphViewerHost.Content);
            Assert.True(window.GraphCanvas.RedrawRequestCount > redrawCountBeforeNew); //New must repaint the now-empty graph, same as Autoconnect/AlignSelection already do

            GraphLoadResult result = window.GraphCanvas.Viewer.LoadDocument(cache, FlowchartJson());
            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEmpty(window.GraphCanvas.Viewer.NodeElements);
        }

        //IMPORTANT 4 (final fix wave): New left an open panel bound to a node the clear just deleted -
        //upstream's ClearFloatingControls has no equivalent on this port's own New path.
        [AvaloniaFact]
        public void NewGraphCommand_Execute_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            window.Commands.NewCommand.Execute(null);

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        //IMPORTANT 4 (final fix wave): loading a save over a graph with an open edit panel left it bound to
        //a now-dead node, same gap as New above - GraphCanvasControl.LoadDocument wraps Viewer.LoadDocument
        //so LoadGraphAsync's real call site gets the close for free instead of duplicating it there.
        [AvaloniaFact]
        public async Task GraphCanvasLoadDocument_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            GraphLoadResult result = window.GraphCanvas.LoadDocument(cache, FlowchartJson());

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        //Phase 7 deferred-minors sweep (widened toolbar-staleness scope): a toolbar button click is a plain
        //Avalonia Button press, never a canvas pointer event, so it never reached GraphCanvasControl.
        //OnPointerPressed's own click-outside-closes-panel guard - the panel just sat open and stale behind
        //whatever the button did. Real MouseDown/MouseUp at the button's own screen position (not
        //Commands.X.Execute(null)) so this exercises Avalonia's genuine press/release/Click pipeline, the
        //same real-click precedent LinkDragTests.WindowCenterOf documents.
        private static Avalonia.Point WindowCenterOf(Avalonia.Visual control, Window window) =>
            Avalonia.VisualExtensions.TranslatePoint(control, new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        private static void RealClick(Window window, string automationName) {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); //forces layout so the button has real Bounds
            Button button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == automationName);
            Avalonia.Point screenPoint = WindowCenterOf(button, window);
            window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);
        }

        [AvaloniaFact]
        public void AutoconnectButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Autoconnect");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public async Task AlignSelectionButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            window.GraphCanvas.Viewer.LoadDocument(cache, FlowchartJson());
            window.GraphCanvas.Viewer.SetSelection([.. window.GraphCanvas.Viewer.NodeElements.Take(2)]); //a selection-gated button (task 7) needs one to be clickable at all
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Align Selected");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        //Task 7 (contract #toolbar's Align Selected .disabled empty-selection state): CanExecute now tracks
        //the canvas's live selection instead of always being true.
        [AvaloniaFact]
        public void AlignSelectionCommand_EmptySelection_CanExecuteIsFalse() {
            var window = new MainWindow();
            window.Show();

            Assert.False(window.Commands.AlignSelectionCommand.CanExecute(null));
        }

        [AvaloniaFact]
        public async Task AlignSelectionCommand_TwoNodesSelected_CanExecuteIsTrue() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            window.GraphCanvas.Viewer.LoadDocument(cache, FlowchartJson());

            window.GraphCanvas.Viewer.SetSelection([.. window.GraphCanvas.Viewer.NodeElements.Take(2)]);

            Assert.True(window.Commands.AlignSelectionCommand.CanExecute(null));
        }

        [AvaloniaFact]
        public async Task AlignSelectionCommand_SelectionClearedAfterSelecting_CanExecuteReturnsToFalse() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            window.GraphCanvas.Viewer.LoadDocument(cache, FlowchartJson());
            window.GraphCanvas.Viewer.SetSelection([.. window.GraphCanvas.Viewer.NodeElements.Take(2)]);
            Assert.True(window.Commands.AlignSelectionCommand.CanExecute(null));

            window.GraphCanvas.Viewer.ClearSelection();

            Assert.False(window.Commands.AlignSelectionCommand.CanExecute(null));
        }

        //Fires-only-when-enabled: an empty-selection Execute() call must be a genuine no-op button state,
        //not merely a command that happens to align zero nodes - RealClick on a disabled Button never
        //dispatches Command.Execute at all (Avalonia hit-tests IsEnabled first).
        [AvaloniaFact]
        public void AlignSelectionButton_EmptySelection_RealClick_DoesNotOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            int redrawCountBefore = window.GraphCanvas.RedrawRequestCount;

            RealClick(window, "Align Selected");

            Assert.Equal(redrawCountBefore, window.GraphCanvas.RedrawRequestCount); //onAlignSelection's own RequestRedraw never ran
        }

        //---- Zoom stepper (task 7, contract #toolbar's zoom stepper marked "(new)") -----------------------

        //The stepper must not open a second zoom path alongside the View menu's Zoom In/Zoom Out (task 5,
        //NativeMenuTests.ViewMenu_ZoomIn_IncreasesViewScale) - both bind the exact same MainWindow command
        //instance, so this proves it structurally rather than just observing the same end effect twice.
        [AvaloniaFact]
        public void ZoomStepper_InAndOutButtons_ShareCommandInstancesWithViewMenu() {
            var window = new MainWindow();
            window.Show();
            var viewMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>()
                .Single(i => i.Header == "View").Menu!;
            NativeMenuItem menuZoomIn = viewMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Zoom In");
            NativeMenuItem menuZoomOut = viewMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Zoom Out");

            Button stepperZoomIn = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Zoom In");
            Button stepperZoomOut = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Zoom Out");

            //C1 wraps the native menu item's Command in a deferring forwarder - unwrap to compare instances.
            Assert.Same(((MainWindow.DeferredMenuCommand)menuZoomIn.Command!).Inner, stepperZoomIn.Command);
            Assert.Same(((MainWindow.DeferredMenuCommand)menuZoomOut.Command!).Inner, stepperZoomOut.Command);
        }

        [AvaloniaFact]
        public void ZoomStepper_ZoomInButton_RealClick_IncreasesViewScale() {
            var window = new MainWindow();
            window.Show();
            float before = window.GraphCanvas.Viewport.ViewScale;

            RealClick(window, "Zoom In");

            Assert.True(window.GraphCanvas.Viewport.ViewScale > before);
        }

        [AvaloniaFact]
        public void ZoomStepper_ZoomOutButton_RealClick_DecreasesViewScale() {
            var window = new MainWindow();
            window.Show();
            float before = window.GraphCanvas.Viewport.ViewScale;

            RealClick(window, "Zoom Out");

            Assert.True(window.GraphCanvas.Viewport.ViewScale < before);
        }

        [AvaloniaFact]
        public void ZoomStepper_ValueLabel_TracksViewScaleAfterZoomInClick() {
            var window = new MainWindow();
            window.Show();

            RealClick(window, "Zoom In");

            var valueLabel = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ZoomValueLabel");
            int expectedPercent = (int)Math.Round(window.GraphCanvas.Viewport.ViewScale * 100);
            Assert.Equal($"{expectedPercent}%", valueLabel.Text);
        }

        //Fix round 1: mouse-wheel zoom went through GraphCanvasControl.OnPointerWheelChanged directly,
        //bypassing every one of ResyncZoomLabel's four call sites, so the readout went stale on a wheel-zoom
        //session until the next stepper/menu click. Exercises the real input pipeline (MouseWheel, not a
        //direct Viewport.ZoomAt call) so it only passes once GraphCanvasControl's own wheel handler notifies
        //MainWindow the same way a stepper click does.
        [AvaloniaFact]
        public void ZoomValueLabel_TracksViewScaleAfterMouseWheelZoom() {
            var window = new MainWindow();
            window.Show();

            window.MouseWheel(new Point(750, 400), new Vector(0, 1));

            var valueLabel = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ZoomValueLabel");
            int expectedPercent = (int)Math.Round(window.GraphCanvas.Viewport.ViewScale * 100);
            Assert.Equal($"{expectedPercent}%", valueLabel.Text);
        }

        [AvaloniaFact]
        public void SettingsButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            //No DataCache/Settings wired up: OpenSettingsAsync's own early-return guard fires right after the
            //close, so this never risks opening a real (unstubbed, unclosable-in-headless) modal.
            RealClick(window, "Settings");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void GraphSummaryButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphSummaryDialogStub = _ => { };
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Graph Summary");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void ExportImageButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.ImageExportDialogStub = _ => { };
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Export Image");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        //I-4 (final fix wave): the titlebar's own VersionLabel TextBlock is gone (contract #toolbar retired
        //it in favor of the About panel) - these two tests move to the real window ShowAboutAsync opens.
        //Task 10: the panel became a real AboutWindow (design doc .about-card) instead of a MessageDialog.
        [AvaloniaFact]
        public async Task AboutPanel_ShowsAppVersion() {
            var window = new MainWindow();
            window.Show();

            Task openTask = window.ShowAboutAsync();
            AboutWindow about = window.OwnedWindows.OfType<AboutWindow>().Single();

            Assert.Equal(AppVersion.VersionedDisplay, about.VersionTextBlockControl.Text);

            about.Close();
            await openTask;
        }

        [AvaloniaFact]
        public async Task AboutPanel_MatchesSpecFormatVerbatim() {
            var window = new MainWindow();
            window.Show();

            Task openTask = window.ShowAboutAsync();
            AboutWindow about = window.OwnedWindows.OfType<AboutWindow>().Single();

            var informational = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            var shortSemVer = informational?.Split('+')[0];
            Assert.Matches(@"^\d+\.\d+\.\d+$", shortSemVer);

            var upstream = typeof(AppVersion).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpstreamVersion")?.Value;
            Assert.Matches(@"^\d+\.\d+\.\d+$", upstream);

            Assert.Equal("Foreman 2", about.AppNameTextBlockControl.Text);
            Assert.Equal($"v {shortSemVer} based on {upstream}", about.VersionTextBlockControl.Text);
            Assert.Equal("A Factorio production-chain planner", about.TaglineTextBlockControl.Text);

            about.Close();
            await openTask;
        }

        [AvaloniaFact]
        public void NativeMenu_HasTopLevelFileEditViewHelp() {
            var window = new MainWindow();

            var headers = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();

            Assert.Contains("File", headers);
            Assert.Contains("Edit", headers);
            Assert.Contains("View", headers);
            Assert.Contains("Help", headers);
        }

        [AvaloniaFact]
        public void NativeMenu_FileContainsSaveWithCmdSGesture() {
            var window = new MainWindow();

            var fileMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            var save = fileMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Save");

            Assert.Equal(Key.S, save.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, save.Gesture.KeyModifiers);
        }

        //Linux branch of the platform fork above (docs/upstream-divergences.md, phase 8 Task 2): every
        //NativeMenu gesture reads PlatformModifiers.Primary, which BuildNativeMenu bakes in once at
        //construction - forcing Linux before building the window proves the whole menu picks up Ctrl.
        [AvaloniaFact]
        public void NativeMenu_FileContainsSaveWithCtrlSGesture_OnLinux() {
            using IDisposable platform = PlatformModifiers.UseIsMacOs(false);
            var window = new MainWindow();

            var fileMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            var save = fileMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Save");

            Assert.Equal(Key.S, save.Gesture!.Key);
            Assert.Equal(KeyModifiers.Control, save.Gesture.KeyModifiers);
        }

        [AvaloniaFact]
        public void NativeMenu_FileNewSaveImportAndExportAreEnabled() {
            var window = new MainWindow();

            var fileMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            //Task 5: File now has separators (contract #menubar) - NativeMenuItemSeparator is itself a
            //NativeMenuItem (Header "-"), so it has to be filtered out explicitly before grouping by Header.
            var items = fileMenu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToDictionary(i => i.Header!, i => i.IsEnabled);

            Assert.True(items["New"]);
            Assert.True(items["Save"]);
            Assert.True(items["Save As"]);
            Assert.True(items["Import"]);
            Assert.True(items["Export Image"]);
        }

        [AvaloniaFact]
        public void Constructor_SetsWindowStateMaximized() {
            var window = new MainWindow();

            Assert.Equal(WindowState.Maximized, window.WindowState);
        }

        [AvaloniaFact]
        public void ApplyTheme_Dark_SetsActualThemeVariantDark() {
            var app = (App)Application.Current!;

            app.ApplyTheme(ThemeMode.Dark);
            var window = new MainWindow();

            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        }

        //Fix round 1: GridLinesGroupBox now lives inside the View Options popover - opened first on both
        //windows before the color lookup, same as every other repointed lookup above.
        [AvaloniaFact]
        public void GroupBoxBorders_ThemedAcrossLightAndDark_ResolveDifferentColors() {
            var app = (App)Application.Current!;

            app.ApplyTheme(ThemeMode.Light);
            var lightWindow = new MainWindow();
            lightWindow.Show();
            OpenViewOptions(lightWindow);
            var lightColor = GroupBoxBorderColor(lightWindow, "GridLinesGroupBox");

            app.ApplyTheme(ThemeMode.Dark);
            var darkWindow = new MainWindow();
            darkWindow.Show();
            OpenViewOptions(darkWindow);
            var darkColor = GroupBoxBorderColor(darkWindow, "GridLinesGroupBox");

            Assert.NotEqual(lightColor, darkColor);
        }

        private static Color GroupBoxBorderColor(MainWindow window, string borderName) {
            var border = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == borderName);
            return Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color;
        }

        //---- Task 8: GraphMutated replaces the five-event dirty wiring ------------------------------------

        [AvaloniaFact]
        public async Task NodeDrag_SetsDirty() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            BaseNodeElement element = window.GraphCanvas.Viewer.NodeElementDictionary[id];
            element.RequestStateUpdate();
            element.PrePaint(); //HitTest needs the element's paint bounds computed to find it
            window.ViewModel.IsDirty = false; //the add above already dirtied it - reset to isolate the drag
            Dispatcher.UIThread.RunJobs(); //forces layout so GraphCanvas has real Bounds to translate through

            Point canvasPoint = window.GraphCanvas.Viewport.GraphToScreen(element.Location);
            Point screenPoint = window.GraphCanvas.TranslatePoint(canvasPoint, window)!.Value;
            Point dragTo = screenPoint + new Point(40, 0);
            window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton); //crosses the drag threshold
            window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton); //arms BaseNodeElement.Dragged
            window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton); //actually moves the node
            window.MouseUp(dragTo, MouseButton.Left, RawInputModifiers.None);

            Assert.True(window.ViewModel.IsDirty);
        }

        //Regression for the C1 gate's M-4 finding: NodeValuesUpdated used to drive IsDirty directly, so a
        //plain solve (no edit at all) lit the Save tint with nothing new to save.
        [AvaloniaFact]
        public void SolveWithoutChange_DoesNotSetDirty() {
            var window = new MainWindow();
            window.Show();

            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public void AnnotationEdit_SetsDirty() {
            var window = new MainWindow();
            window.Show();
            var text = new TextAnnotationElement(new System.Drawing.Point(0, 0));
            window.GraphCanvas.Viewer.AddAnnotationElement(text);
            window.ViewModel.IsDirty = false; //the add above already dirtied it - reset to isolate the edit
            window.GraphCanvas.TextPropertiesDialogStub = _ => Task.FromResult(true); //simulates clicking OK

            window.GraphCanvas.Viewer.ShowAnnotationPropertiesDialog?.Invoke(text);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ViewModel.IsDirty);
        }

        //Fix round 1: every field on these two windows applies live onto the domain element as the user
        //types - OK persists it, Cancel reverts it, but closing via the red button (or Cmd+W, or the
        //window manager) used to skip both and leave the element mutated with no revert and no dirty
        //signal. Drives the real window (not TextPropertiesDialogStub) so OnClosing's revert actually runs.
        [AvaloniaFact]
        public void TextPropertiesWindow_CloseWithoutOkOrCancel_RevertsAndStaysClean() {
            var window = new MainWindow();
            window.Show();
            var text = new TextAnnotationElement(new System.Drawing.Point(0, 0));
            window.GraphCanvas.Viewer.AddAnnotationElement(text);
            string originalText = text.Text;
            window.ViewModel.IsDirty = false; //the add above already dirtied it - reset to isolate the close

            window.GraphCanvas.Viewer.ShowAnnotationPropertiesDialog?.Invoke(text);
            TextPropertiesWindow dialog = window.OwnedWindows.OfType<TextPropertiesWindow>().Single();
            dialog.TextInputControl.Text = originalText + " edited live";
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(originalText, text.Text); //confirms the live-apply actually landed before close

            dialog.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(originalText, text.Text);
            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public void ShapePropertiesWindow_CloseWithoutOkOrCancel_RevertsAndStaysClean() {
            var window = new MainWindow();
            window.Show();
            var shape = new ShapeAnnotationElement(new System.Drawing.Point(0, 0), 200, 150);
            window.GraphCanvas.Viewer.AddAnnotationElement(shape);
            int originalBorderWidth = shape.BorderWidth;
            window.ViewModel.IsDirty = false; //the add above already dirtied it - reset to isolate the close

            window.GraphCanvas.Viewer.ShowAnnotationPropertiesDialog?.Invoke(shape);
            ShapePropertiesWindow dialog = window.OwnedWindows.OfType<ShapePropertiesWindow>().Single();
            dialog.BorderWidthInputControl.Value = originalBorderWidth + 23;
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(originalBorderWidth, shape.BorderWidth); //confirms the live-apply actually landed

            dialog.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(originalBorderWidth, shape.BorderWidth);
            Assert.False(window.ViewModel.IsDirty);
        }
    }
}
