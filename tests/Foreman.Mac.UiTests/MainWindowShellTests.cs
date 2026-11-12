using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.DataCaching.Storage;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Models.Research;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Converters;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
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
        private static readonly string[] ExpectedButtonNames = [
            "New Graph", "Load", "Save",
            "Add Item", "Add Recipe",
            "Autoconnect", "Align Selected",
            "Export Image", "Graph Summary", "View Options",
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

        [AvaloniaFact]
        public void ToolbarItems_AllHaveAGlyphAndATheme_AndArrangeToNonZeroBounds() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            ToolbarItem[] items = [.. window.Toolbar.Groups.SelectMany(g => g.Items)];
            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.NotNull(item.Glyph));
            Assert.All(items, item => Assert.NotNull(item.Theme));
            Assert.All(items, item => Assert.NotEqual(0, item.Bounds.Width));
            Assert.All(items, item => Assert.NotEqual(0, item.Bounds.Height));
        }

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

        [AvaloniaFact]
        public void AllFifteenCommands_AreReachableFromToolbarOrNativeMenu() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.DataCache = MinimalCacheWithDefaultQuality();
            window.Settings = new AppSettings();
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());

            var toolbarCommands = window.GetVisualDescendants().OfType<Button>()
                .Select(b => b.Command).OfType<IRelayCommand>().ToHashSet();

            var menuCommands = AllNativeMenuItems(NativeMenu.GetMenu(window)!)
                .Select(i => i.Command).OfType<MainWindow.DeferredMenuCommand>()
                .Select(d => d.Inner).OfType<IRelayCommand>().ToHashSet();

            NativeMenuItem settingsItem = AllNativeMenuItems(NativeMenu.GetMenu(Application.Current!)!).Single(i => i.Header == "Settings…");
            Assert.Equal(new KeyGesture(Key.OemComma, PlatformModifiers.Primary), settingsItem.Gesture);
            bool settingsOpened = false;
            window.SettingsDialogStub = _ => settingsOpened = true;
            settingsItem.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(settingsOpened);

            var reachableCommands = toolbarCommands.Concat(menuCommands).Append(window.Commands.SettingsCommand).ToHashSet();

            var commandsByProperty = typeof(ShellViewModel).GetProperties()
                .Where(p => typeof(IRelayCommand).IsAssignableFrom(p.PropertyType))
                .ToDictionary(p => p.Name, p => (IRelayCommand)p.GetValue(window.Commands)!);

            Assert.Equal(15, commandsByProperty.Count);
            foreach ((string name, IRelayCommand command) in commandsByProperty)
                Assert.Contains(command, reachableCommands);
        }

        private static IEnumerable<NativeMenuItem> AllNativeMenuItems(NativeMenu menu) {
            foreach (NativeMenuItem item in menu.Items.OfType<NativeMenuItem>()) {
                yield return item;
                if (item.Menu is NativeMenu submenu)
                    foreach (NativeMenuItem nested in AllNativeMenuItems(submenu))
                        yield return nested;
            }
        }

        private static Avalonia.Point EmptyTitleBarPoint(MainWindow window) {
            Foreman.Mac.Kit.Controls.SearchField searchField = window.Toolbar.SearchField;
            Avalonia.Point searchLeft = searchField.TranslatePoint(new Avalonia.Point(0, searchField.Bounds.Height / 2), window)!.Value;
            Button lastItem = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "View Options");
            Avalonia.Point lastItemRight = lastItem.TranslatePoint(new Avalonia.Point(lastItem.Bounds.Width, lastItem.Bounds.Height / 2), window)!.Value;
            return new Avalonia.Point((searchLeft.X + lastItemRight.X) / 2, searchLeft.Y);
        }

        [AvaloniaFact]
        public void TitleBar_EmptyBackgroundPress_RequestsMoveDrag_ButtonPress_DoesNot() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Avalonia.Point emptyPoint = EmptyTitleBarPoint(window);
            window.MouseDown(emptyPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(emptyPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(1, window.TitleBarDragRequestCount);

            RealClick(window, "Save");

            Assert.Equal(1, window.TitleBarDragRequestCount);
        }

        [AvaloniaFact]
        public void TitleBar_EmptyBackgroundDoubleClick_TogglesMaximizedState() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Maximized, window.WindowState);

            Avalonia.Point emptyPoint = EmptyTitleBarPoint(window);
            window.MouseDown(emptyPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(emptyPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseDown(emptyPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(emptyPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(WindowState.Normal, window.WindowState);
        }

        [AvaloniaFact]
        public void Title_TracksIsDirty_ThroughTheCloseButtonDot_NotTitleText() {
            var window = new MainWindow();
            window.Show();
            window.ViewModel.DocumentName = "Iron Plate Line";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Iron Plate Line", window.Toolbar.Title);
            Assert.False(window.DocumentEdited);

            window.ViewModel.IsDirty = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Iron Plate Line", window.Toolbar.Title);
            Assert.True(window.DocumentEdited);
        }

        [AvaloniaFact]
        public void SettingsCommand_OpensTheSettingsDialog() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = MinimalCacheWithDefaultQuality();
            window.Settings = new AppSettings();
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());

            bool opened = false;
            window.SettingsDialogStub = _ => opened = true;
            window.Commands.SettingsCommand.Execute(null);

            Assert.True(opened);
        }

        [AvaloniaFact]
        public async Task SearchField_CmdF_FocusesIt_AndCmdG_StepsMatches() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId firstId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId secondId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            foreach ((NodeId id, string title) in new[] { (firstId, "Iron Feed A"), (secondId, "Iron Feed B") }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle(title);
            }
            Dispatcher.UIThread.RunJobs();

            CanvasKeys.KeyDownOn(window, Key.F, PlatformModifiers.Primary);
            Assert.True(window.Toolbar.SearchField.IsFocused);

            window.Toolbar.SearchField.Text = "Iron Feed";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(firstId, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal(secondId, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal(firstId, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary | KeyModifiers.Shift);
            Assert.Equal(secondId, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
        }

        [AvaloniaFact]
        public async Task SearchField_OrdersByCentreYThenCentreX_AndWrapsBothWays() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId lowY = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            NodeId lowYLeft = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId highY = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 100));
            foreach (NodeId id in new[] { lowY, lowYLeft, highY }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle("Order Test");
            }
            Dispatcher.UIThread.RunJobs();
            window.Toolbar.SearchField.Focus();
            window.Toolbar.SearchField.Text = "Order Test";
            Dispatcher.UIThread.RunJobs();

            NodeId SelectedId() => Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id;

            Assert.Equal(lowYLeft, SelectedId());

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal(lowY, SelectedId());

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal(highY, SelectedId());

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal(lowYLeft, SelectedId());

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary | KeyModifiers.Shift);
            Assert.Equal(highY, SelectedId());
        }

        [AvaloniaFact]
        public async Task SearchField_TypingWithASelectionAlreadyMade_AnchorsImmediately_AndStaysCurrentAsTheQueryIsEdited() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId first = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId second = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            NodeId third = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(400, 0));
            foreach ((NodeId id, string title) in new[] { (first, "Anchor One"), (second, "Anchor Two"), (third, "Anchor Three") }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle(title);
            }
            Dispatcher.UIThread.RunJobs();
            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElementDictionary[second]]);
            window.Toolbar.SearchField.Focus();

            window.Toolbar.SearchField.Text = "Anchor";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(second, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
            Assert.Equal("2 of 3", window.Toolbar.SearchField.MatchStatus);

            window.GraphCanvas.Viewer.SetSelection([window.GraphCanvas.Viewer.NodeElementDictionary[third]]);
            window.Toolbar.SearchField.Text = "Anchor T";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(second, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
            Assert.Equal("1 of 2", window.Toolbar.SearchField.MatchStatus);

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal(third, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
        }

        [AvaloniaFact]
        public async Task SearchField_QueryIsTrimmed_AndAnAllWhitespaceQueryMatchesNothing() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
            controller.SetKeyNode(true);
            controller.SetKeyNodeTitle("Trim Test");
            Dispatcher.UIThread.RunJobs();
            window.Toolbar.SearchField.Focus();

            window.Toolbar.SearchField.Text = "   ";
            Dispatcher.UIThread.RunJobs();
            Assert.Null(window.Toolbar.SearchField.MatchStatus);
            Assert.Empty(window.GraphCanvas.Viewer.SelectedNodes);

            window.Toolbar.SearchField.Text = " Trim Test ";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1 of 1", window.Toolbar.SearchField.MatchStatus);
            Assert.Equal(id, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
        }

        [AvaloniaFact]
        public async Task SearchField_SteppingBeforeTheDebounceElapses_KeepsTheStepAnnouncement() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId first = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId second = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            foreach (NodeId id in new[] { first, second }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle("Overwrite Test");
            }
            Dispatcher.UIThread.RunJobs();
            TextBlock announcer = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SearchAnnouncer");
            window.Toolbar.SearchField.Focus();

            window.Toolbar.SearchField.Text = "Overwrite Test";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal("2 of 2, Iron plate", AutomationProperties.GetName(announcer));

            AnimationClock.Settle(400);

            Assert.Equal("2 of 2, Iron plate", AutomationProperties.GetName(announcer));
        }

        [AvaloniaFact]
        public void SearchField_AccessibleName_IsFindNode_AndItsValueIsTheQuery() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Toolbar.SearchField.Text = "iron";
            Dispatcher.UIThread.RunJobs();

            AutomationPeer fieldPeer = ControlAutomationPeer.CreatePeerForElement(window.Toolbar.SearchField);
            TextBlock announcer = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SearchAnnouncer");

            Assert.Equal("Find node", fieldPeer.GetName());
            Assert.Equal("iron", fieldPeer.GetProvider<IValueProvider>()?.Value);
            Assert.NotEqual("Find node", ControlAutomationPeer.CreatePeerForElement(announcer).GetName());
        }

        [AvaloniaFact]
        public async Task SearchField_DeletingTheCurrentMatch_StepsTheIndex_AndTheCountFollowsTheGraph() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId first = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId second = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            NodeId third = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(400, 0));
            foreach (NodeId id in new[] { first, second, third }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle("Mid Find");
            }
            Dispatcher.UIThread.RunJobs();
            window.Toolbar.SearchField.Focus();
            window.Toolbar.SearchField.Text = "Mid Find";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal("2 of 3", window.Toolbar.SearchField.MatchStatus);
            Assert.Equal(second, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);

            window.GraphCanvas.Viewer.Session.Editor.DeleteNode(second);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("2 of 2", window.Toolbar.SearchField.MatchStatus);
            Assert.Equal(third, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
        }

        [AvaloniaFact]
        public async Task SearchField_AddingAndRenamingNodes_RecomputeTheRun_WithoutRetyping() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId first = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId second = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            foreach (NodeId id in new[] { first, second }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle("Live Find");
            }
            Dispatcher.UIThread.RunJobs();
            window.Toolbar.SearchField.Focus();
            window.Toolbar.SearchField.Text = "Live Find";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1 of 2", window.Toolbar.SearchField.MatchStatus);
            Assert.Equal(first, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);

            NodeId added = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, -100));
            var addedController = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(added)!;
            addedController.SetKeyNode(true);
            addedController.SetKeyNodeTitle("Live Find");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("2 of 3", window.Toolbar.SearchField.MatchStatus);
            Assert.Equal(first, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
            var elements = window.GraphCanvas.Viewer.NodeElementDictionary;
            Assert.True(elements[added].IsFindMatch);
            Assert.NotNull(elements[added].LaneARing());

            window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(first)!.SetKeyNodeTitle("Renamed Away");
            Dispatcher.UIThread.RunJobs();

            Assert.False(elements[first].IsFindMatch);
            Assert.Null(elements[first].LaneARing());
            Assert.True(elements[second].IsFindMatch);

            Assert.Equal("2 of 2", window.Toolbar.SearchField.MatchStatus);
            Assert.Equal(second, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);

            window.Toolbar.SearchField.Text = "Live Find 2";
            Dispatcher.UIThread.RunJobs();
            foreach (NodeId id in new[] { first, second, added }) {
                Assert.False(elements[id].IsFindMatch);
                Assert.Null(elements[id].LaneARing());
            }
        }

        [AvaloniaFact]
        public async Task SearchField_ReturnAndShiftReturn_StepForwardAndBackward_WithoutMovingFocus() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId first = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId second = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            foreach (NodeId id in new[] { first, second }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle("Return Test");
            }
            Dispatcher.UIThread.RunJobs();
            window.Toolbar.SearchField.Focus();
            window.Toolbar.SearchField.Text = "Return Test";
            Dispatcher.UIThread.RunJobs();

            CanvasKeys.KeyDownOn(window.Toolbar.SearchField, Key.Return);
            Assert.Equal(second, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
            Assert.True(window.Toolbar.SearchField.IsFocused);

            CanvasKeys.KeyDownOn(window.Toolbar.SearchField, Key.Return, KeyModifiers.Shift);
            Assert.Equal(first, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
            Assert.True(window.Toolbar.SearchField.IsFocused);
        }

        [AvaloniaFact]
        public async Task SearchField_Step_PansOnlyWhenTheMatchIsOffScreen_AndNeverZooms() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            window.DataCache = cache;
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId onScreen = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            var onScreenController = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(onScreen)!;
            onScreenController.SetKeyNode(true);
            onScreenController.SetKeyNodeTitle("Visible Match");
            Dispatcher.UIThread.RunJobs();

            float scaleBefore = window.GraphCanvas.Viewport.ViewScale;
            System.Drawing.Point offsetBefore = window.GraphCanvas.Viewport.ViewOffset;

            window.Toolbar.SearchField.Focus();
            window.Toolbar.SearchField.Text = "Visible Match";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            AnimationClock.Settle(400);

            Assert.Equal(offsetBefore, window.GraphCanvas.Viewport.ViewOffset);
            Assert.Equal(scaleBefore, window.GraphCanvas.Viewport.ViewScale);

            NodeId offScreen = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(20000, 20000));
            var offScreenController = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(offScreen)!;
            offScreenController.SetKeyNode(true);
            offScreenController.SetKeyNodeTitle("Distant Match");
            Dispatcher.UIThread.RunJobs();

            window.Toolbar.SearchField.Text = "Distant Match";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            AnimationClock.Settle(400);

            Assert.NotEqual(offsetBefore, window.GraphCanvas.Viewport.ViewOffset);
            Assert.Equal(scaleBefore, window.GraphCanvas.Viewport.ViewScale);
            Assert.Equal(offScreen, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
        }

        [AvaloniaFact]
        public async Task SearchField_MatchStatus_ReflectsEmptyNoMatchesAndStep() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId first = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId second = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            foreach (NodeId id in new[] { first, second }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle("Status Test");
            }
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.Toolbar.SearchField.MatchStatus);

            window.Toolbar.SearchField.Focus();
            window.Toolbar.SearchField.Text = "nothing matches this";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("No matches", window.Toolbar.SearchField.MatchStatus);

            window.Toolbar.SearchField.Text = "Status Test";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1 of 2", window.Toolbar.SearchField.MatchStatus);

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal("2 of 2", window.Toolbar.SearchField.MatchStatus);

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal("1 of 2", window.Toolbar.SearchField.MatchStatus);
        }

        [AvaloniaFact]
        public async Task SearchField_Announces_ResultSetThenStepsThenWraps_BothDirections() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId first = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId second = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            foreach (NodeId id in new[] { first, second }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle("Voice Test");
            }
            Dispatcher.UIThread.RunJobs();

            TextBlock announcer = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SearchAnnouncer");

            window.Toolbar.SearchField.Focus();
            window.Toolbar.SearchField.Text = "Voice Test";
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(400);
            Assert.Equal("2 matches", AutomationProperties.GetName(announcer));

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal("2 of 2, Iron plate", AutomationProperties.GetName(announcer));

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary);
            Assert.Equal("Wrapped to first match. 1 of 2, Iron plate", AutomationProperties.GetName(announcer));

            CanvasKeys.KeyDownOn(window, Key.G, PlatformModifiers.Primary | KeyModifiers.Shift);
            Assert.Equal("Wrapped to last match. 2 of 2, Iron plate", AutomationProperties.GetName(announcer));
        }

        [AvaloniaFact]
        public async Task SearchField_Announces_DebouncesABurstOfKeystrokes_ToOneAnnouncement() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
            controller.SetKeyNode(true);
            controller.SetKeyNodeTitle("Debounce Test");
            Dispatcher.UIThread.RunJobs();

            TextBlock announcer = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SearchAnnouncer");
            window.Toolbar.SearchField.Focus();

            foreach (char c in "Debounce Test") {
                window.Toolbar.SearchField.Text += c;
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Null(AutomationProperties.GetName(announcer));

            AnimationClock.Settle(400);
            Assert.Equal("1 match", AutomationProperties.GetName(announcer));
        }

        private static void OpenViewOptions(MainWindow window) {
            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        private static void CommitPopUp(PopUp popUp, string item) {
            popUp.SelectedItem = item;
            popUp.Command?.Execute(item);
        }

        [AvaloniaFact]
        public void ViewOptionsPopover_DefaultsToClosed() {
            var window = new MainWindow();
            window.Show();

            var popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single(p => p.Name == "ViewOptionsPopover");

            Assert.False(popover.IsOpen);
        }

        [AvaloniaFact]
        public void ViewOptionsButton_RealClick_OpensThenClosesThePopover() {
            var window = new MainWindow();
            window.Show();
            var popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single(p => p.Name == "ViewOptionsPopover");

            RealClick(window, "View Options");
            Assert.True(popover.IsOpen);
            Assert.Equal("View Options", AutomationProperties.GetName(popover));

            RealClick(window, "View Options");
            Assert.False(popover.IsOpen);
        }

        [AvaloniaFact]
        public void ViewOptionsButton_RealClick_WhileOpen_ClosesAndStaysClosed() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single(p => p.Name == "ViewOptionsPopover");

            RealClick(window, "View Options");
            Dispatcher.UIThread.RunJobs();

            Assert.False(popover.IsOpen);
            Assert.False(window.ViewModel.IsViewOptionsOpen);
        }

        [AvaloniaFact]
        public void ViewOptions_LightDismiss_SwallowsTheDismissingClick_AndReturnsFocusToTheToolbarItem() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single(p => p.Name == "ViewOptionsPopover");
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "View Options");
            int canvasPresses = 0;
            window.GraphCanvas.PointerPressed += (_, _) => canvasPresses++;

            Avalonia.Point outsidePoint = WindowCenterOf(window.GraphCanvas, window);
            window.MouseDown(outsidePoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(popover.IsOpen);
            Assert.False(window.ViewModel.IsViewOptionsOpen);
            Assert.True(button.IsFocused);
            Assert.False(window.GraphCanvas.IsFocused);
            Assert.Equal(0, canvasPresses);

            OpenViewOptions(window);
            var saveButton = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Save");
            Avalonia.Point savePoint = WindowCenterOf(saveButton, window);
            window.MouseDown(savePoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(savePoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(popover.IsOpen);
            Assert.True(button.IsFocused);
            Assert.False(saveButton.IsFocused);
        }

        [AvaloniaFact]
        public void GridlinesAndGraphOptionsControls_AreEnabled() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);

            foreach (string name in new[] { "MinorGridlinesDropDown", "MajorGridlinesDropDown", "RateOptionsDropDown" })
                Assert.True(window.GetVisualDescendants().OfType<PopUp>().Single(c => c.Name == name).IsEnabled);
            foreach (string name in new[] { "GridlinesCheckbox", "IconViewCheckBox", "PauseUpdatesCheckbox" })
                Assert.True(window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == name).IsEnabled);

            var popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single(p => p.Name == "ViewOptionsPopover");
            FormRow[] rows = [.. popover.GetVisualDescendants().OfType<FormRow>()];
            Assert.Equal(5, rows.Length);
            Application.Current!.TryGetResource("Foreman.Size.FormLabelColumnPopover", Application.Current.ActualThemeVariant, out object? resolved);
            foreach (FormRow row in rows) {
                Assert.Equal(FormRowLabelColumn.Popover, row.LabelColumn);
                Grid grid = row.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");
                Assert.Equal((double)resolved!, grid.ColumnDefinitions[0].Width.Value);
            }
        }

        [AvaloniaFact]
        public void ViewOptions_RateUnitPopUp_ListsAllSixValues() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);

            var rateOptions = window.GetVisualDescendants().OfType<PopUp>().Single(p => p.Name == "RateOptionsDropDown");

            Assert.Equal(MainWindow.RateUnitOptions, rateOptions.Items);
        }

        [AvaloniaFact]
        public void ViewOptions_CheckboxesAndPopUps_WriteThroughToSettings_AndMirrorTheViewMenu() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            OpenViewOptions(window);

            NativeMenuItem[] allItems = [.. AllNativeMenuItems(NativeMenu.GetMenu(window)!)];
            NativeMenuItem showGridlinesItem = allItems.Single(i => i.Header == "Show Gridlines");
            NativeMenuItem iconViewItem = allItems.Single(i => i.Header == "Icon View");
            NativeMenuItem pauseItem = allItems.Single(i => i.Header == "Pause All Calculations");
            NativeMenuItem minorSubmenu = allItems.Single(i => i.Header == "Minor Gridlines");
            NativeMenuItem rateSubmenu = allItems.Single(i => i.Header == "Rate Unit");

            var gridlinesCheckbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GridlinesCheckbox");
            gridlinesCheckbox.IsChecked = false;
            Assert.False(window.Settings!.AltGridlines);
            Assert.False(showGridlinesItem.IsChecked);

            var iconViewCheckbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconViewCheckBox");
            iconViewCheckbox.IsChecked = true;
            Assert.True(window.Settings!.IconsOnlyView);
            Assert.True(iconViewItem.IsChecked);

            var pauseCheckbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "PauseUpdatesCheckbox");
            pauseCheckbox.IsChecked = true;
            Assert.True(window.GraphCanvas.Viewer.Graph.PauseUpdates);
            Assert.True(pauseItem.IsChecked);

            var minorPopUp = window.GetVisualDescendants().OfType<PopUp>().Single(p => p.Name == "MinorGridlinesDropDown");
            CommitPopUp(minorPopUp, "64");
            Assert.Equal(Array.IndexOf(MainWindow.GridlineLabels, "64"), window.Settings!.MinorGridlines);
            NativeMenuItem checkedMinor = minorSubmenu.Menu!.Items.OfType<NativeMenuItem>().Single(i => i.IsChecked);
            Assert.Equal("64", checkedMinor.Header);

            NativeMenuItem majorSubmenu = allItems.Single(i => i.Header == "Major Gridlines");
            var majorPopUp = window.GetVisualDescendants().OfType<PopUp>().Single(p => p.Name == "MajorGridlinesDropDown");
            CommitPopUp(majorPopUp, "128");
            Assert.Equal(Array.IndexOf(MainWindow.GridlineLabels, "128"), window.Settings!.MajorGridlines);
            Assert.Equal(768, window.GraphCanvas.Grid.CurrentMajorGridUnit);
            NativeMenuItem checkedMajor = majorSubmenu.Menu!.Items.OfType<NativeMenuItem>().Single(i => i.IsChecked);
            Assert.Equal("128", checkedMajor.Header);
            Assert.Equal(Array.IndexOf(MainWindow.GridlineLabels, "64"), window.Settings!.MinorGridlines);

            var rateOptions = window.GetVisualDescendants().OfType<PopUp>().Single(p => p.Name == "RateOptionsDropDown");
            CommitPopUp(rateOptions, "per hour");
            Assert.Equal(ProductionGraph.RateUnit.Per1Hour, window.Settings!.DefaultRateUnit);
            NativeMenuItem checkedRate = rateSubmenu.Menu!.Items.OfType<NativeMenuItem>().Single(i => i.IsChecked);
            Assert.Equal("1 hour", checkedRate.Header);
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
        //KeyPressQwerty routes through the shared IKeyboardDevice's FocusedElement, which a concurrent headless test can steal; raise KeyDownEvent on GraphCanvas instead.
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

        [AvaloniaFact]
        public async Task ShowSettings_LiveApplyFlagDarkModeSystemWhileAppCurrentlyDark_CanvasResolvesLight() {
            var app = (App)Application.Current!;
            app.ApplyTheme(ThemeMode.Dark);

            var window = new MainWindow();
            window.Show();
            window.DataCache = MinimalCacheWithDefaultQuality();
            window.Settings = new AppSettings { FlagDarkMode = ThemeMode.Dark };
            window.PresetResolver = new PresetResolver(NewTempDir());
            window.SettingsService = new SettingsService(NewTempDir());
            window.ApplyLoadedSettings();

            SettingsWindow? settingsWindow = null;
            window.SettingsDialogStub = w => settingsWindow = w;
            window.ShowSettings();

            settingsWindow!.AppearanceTabControl.ThemeSegmentedControl.SelectedIndex = (int)ThemeMode.System;

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
            var rateOptions = window.GetVisualDescendants().OfType<PopUp>().Single(c => c.Name == "RateOptionsDropDown");
            Assert.Equal(MainWindow.RateUnitOptions[(int)expectedUnit], rateOptions.SelectedItem);
            Assert.Equal(expectedUnit, window.Settings!.DefaultRateUnit);
            Assert.Equal(0, solveCount);
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

        [AvaloniaFact]
        public void PausedCapsule_PointerClick_ResumesOnceAndClearsEveryPauseControl() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            OpenViewOptions(window);
            NativeMenuItem pauseItem = AllNativeMenuItems(NativeMenu.GetMenu(window)!).Single(i => i.Header == "Pause All Calculations");
            var pauseCheckbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "PauseUpdatesCheckbox");
            pauseCheckbox.IsChecked = true;
            PausedCapsule capsule = window.GraphCanvas.GetVisualDescendants().OfType<PausedCapsule>().Single();
            Assert.True(capsule.IsVisible);
            OpenViewOptions(window);
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Point centre = capsule.TranslatePoint(new Avalonia.Point(capsule.Bounds.Width / 2, capsule.Bounds.Height / 2), window)!.Value;
            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);

            Assert.False(window.GraphCanvas.Viewer.IsPaused);
            Assert.False(pauseCheckbox.IsChecked);
            Assert.False(pauseItem.IsChecked);
            Assert.False(capsule.IsVisible);
            Assert.Equal(1, solveCount);
        }

        [AvaloniaFact]
        public void MenuTogglePauseCalculations_Unpausing_SolvesExactlyOnce() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            NativeMenuItem pauseItem = AllNativeMenuItems(NativeMenu.GetMenu(window)!).Single(i => i.Header == "Pause All Calculations");
            pauseItem.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.GraphCanvas.Viewer.IsPaused);
            int solveCount = 0;
            window.GraphCanvas.Viewer.Graph.NodeValuesUpdated += (_, _) => solveCount++;

            pauseItem.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.GraphCanvas.Viewer.IsPaused);
            Assert.False(pauseItem.IsChecked);
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
            var rateOptions = window.GetVisualDescendants().OfType<PopUp>().Single(c => c.Name == "RateOptionsDropDown");
            Assert.Equal(MainWindow.RateUnitOptions[(int)ProductionGraph.RateUnit.Per1Hour], rateOptions.SelectedItem);
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

        [AvaloniaFact]
        public async Task NewGraphAsync_AfterASync_StartsWithoutResearch() {
            using var library = new LibraryFixture();
            using var shell = ShellFixture.Open();
            shell.Window.Library = library.Service;
            shell.Graph.Research = Views.Research.ResearchWindowTests.Synced();

            await shell.Window.NewGraphAsync();

            Assert.Same(ResearchState.None, shell.Graph.Research);
            GraphRow row = library.Service.Find(library.Service.OpenGraphId!.Value)!;
            Assert.DoesNotContain("\"Research\"", row.Data);
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
            window.GraphCanvas.Viewer.SetSelection([.. window.GraphCanvas.Viewer.NodeElements.Take(2)]);
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Align Selected");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

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

        [AvaloniaFact]
        public void AlignSelectionButton_EmptySelection_RealClick_DoesNotOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            int redrawCountBefore = window.GraphCanvas.RedrawRequestCount;

            RealClick(window, "Align Selected");

            Assert.Equal(redrawCountBefore, window.GraphCanvas.RedrawRequestCount);
        }

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
        public void ZoomValueLabel_TracksViewScaleAfterMouseWheelZoom() {
            var window = new MainWindow();
            window.Show();

            window.MouseWheel(new Point(750, 400), new Vector(0, 1));

            double expectedPercent = Math.Round(window.GraphCanvas.Viewport.ViewScale * 100);
            Assert.Equal(expectedPercent, window.Toolbar.ZoomStepper.Value);
        }

        [AvaloniaFact]
        public void SettingsCommand_Execute_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            window.Commands.SettingsCommand.Execute(null);

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void GraphSummaryButton_RealClick_ClosesOpenFloatingPanel() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.FloatingPanelHost.Show(new Border { Width = 50, Height = 50, Focusable = true }, new System.Drawing.Point(0, 0));
            Assert.True(window.GraphCanvas.FloatingPanelHost.IsOpen);

            RealClick(window, "Graph Summary");

            Assert.False(window.GraphCanvas.FloatingPanelHost.IsOpen);
            window.CurrentGraphSummaryWindow?.Close();
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
        public void NativeMenu_FileContainsSaveWithCmdSGesture() {
            var window = new MainWindow();

            var fileMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            var save = fileMenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Save");

            Assert.Equal(Key.S, save.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, save.Gesture.KeyModifiers);
        }

        [AvaloniaFact]
        public void NativeMenu_FileNewSaveImportAndExportAreEnabled() {
            var window = new MainWindow();

            var fileMenu = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;
            var items = fileMenu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToDictionary(i => i.Header!, i => i.IsEnabled);

            Assert.True(items["New Graph"]);
            Assert.True(items["Save"]);
            Assert.True(items["Duplicate"]);
            Assert.True(items["Rename…"]);
            Assert.True(items["Export as .fjson…"]);
            Assert.True(items["Import Graph…"]);
            Assert.True(items["Export Image…"]);
        }

        [AvaloniaFact]
        public async Task FindNode_WithTheToolbarHidden_RevealsItUntilTheFieldResigns() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenWindowWithGraphAsync();
            var settings = new AppSettings { ShowToolbar = false };
            window.Settings = settings;
            Dispatcher.UIThread.RunJobs();
            NativeMenu view = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "View").Menu!;
            NativeMenuItem showToolbar = view.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Show Toolbar");
            Assert.False(window.Toolbar.IsVisible);
            Assert.False(showToolbar.IsChecked);

            CanvasKeys.KeyDownOn(window.GraphCanvas, Key.F, KeyModifiers.Meta);

            SearchField search = window.Toolbar.SearchField;
            Assert.True(window.Toolbar.IsVisible);
            Assert.True(search.IsKeyboardFocusWithin);
            Assert.False(settings.ShowToolbar);
            Assert.False(showToolbar.IsChecked);

            search.Text = "zzz";
            CanvasKeys.KeyDownOn(search, Key.Escape);
            Assert.True(window.Toolbar.IsVisible);
            CanvasKeys.KeyDownOn(search, Key.Escape);
            Assert.False(window.Toolbar.IsVisible);
            Assert.False(settings.ShowToolbar);
        }

        [AvaloniaFact]
        public void ShowToolbarOff_AtOpen_LaysOutAt28FromTheFirstPass() {
            var window = new MainWindow { Settings = new AppSettings { ShowToolbar = false } };
            Grid titleBar = window.FindControl<Grid>("TitleBarRow")!;
            var heights = new List<double>();
            titleBar.LayoutUpdated += (_, _) => heights.Add(titleBar.Bounds.Height);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(heights);
            Assert.All(heights, h => Assert.Equal(28, h, 0.5));
            Assert.False(window.Toolbar.IsVisible);
        }

        [AvaloniaFact]
        public void HiddenToolbar_Solving_SolveStatusInTitleBarRow_RightEdgeRowWidthMinus12_CentreY14_Failed_OpensThePopover() {
            var window = new MainWindow { Settings = new AppSettings { ShowToolbar = false } };
            window.Show();
            var clock = new ManualTimeProvider();
            window.UseSolveTracker(new SolveStatusTracker(clock));
            Dispatcher.UIThread.RunJobs();
            Grid titleBar = window.FindControl<Grid>("TitleBarRow")!;
            SolveStatus status = window.TitleBarSolveStatus;
            Assert.Same(titleBar, status.GetVisualParent());
            Assert.Equal(0, status.Bounds.Width);

            window.SolveTracker.OnSolveStarted();
            clock.Advance(TimeSpan.FromMilliseconds(400));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(SolveStatusState.Solving, status.State);
            Assert.Equal(28, titleBar.Bounds.Height, 0.5);
            Assert.Equal(titleBar.Bounds.Width - 12, status.Bounds.Right, 1);
            Assert.Equal(14, status.Bounds.Y + status.Bounds.Height / 2, 0.5);
            Assert.Equal(new Size(16, 16), status.Bounds.Size);

            window.SolveTracker.OnSolveEnded([new Foreman.Models.Solver.SolveFailure([1], Foreman.Models.Solver.SolveFailureReason.Infeasible)]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SolveStatusState.Failed, status.State);
            Button triangle = status.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Triangle");
            Assert.False(triangle.IsTabStop);

            ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(triangle)).Invoke();
            Dispatcher.UIThread.RunJobs();

            PopoverFrame popover = window.GetVisualDescendants().OfType<PopoverFrame>().Single(f => f.Anchor == status);
            Assert.True(popover.IsOpen);
            Assert.Equal("Couldn\u2019t solve this graph", popover.GetVisualDescendants().OfType<StatePanel>().Single().Headline);
        }

        [AvaloniaFact]
        public void ToolbarShown_TitleBarSolveStatusHides_AndTheStepperHostsIt() {
            var window = new MainWindow();
            window.Show();
            window.SolveTracker.OnSolveEnded([new Foreman.Models.Solver.SolveFailure([1], Foreman.Models.Solver.SolveFailureReason.Infeasible)]);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.TitleBarSolveStatus.IsVisible);
            Assert.Equal(SolveStatusState.Failed, window.Toolbar.ZoomStepper.SolveStatus.State);
            Assert.Equal(16, window.Toolbar.ZoomStepper.SolveStatus.Bounds.Width);

            window.Settings = new AppSettings { ShowToolbar = false };
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.TitleBarSolveStatus.IsVisible);
            Assert.Equal(16, window.TitleBarSolveStatus.Bounds.Width);
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

        [AvaloniaFact]
        public void TitleBarRow_Background_MatchesTheToolbarRowsMaterial_InLightAndDark() {
            var app = (App)Application.Current!;

            app.ApplyTheme(ThemeMode.Light);
            Color lightStrip = ArrangedTrafficLightStrip(out Grid lightTitleBar);
            Assert.Equal(ThemeColor("Foreman.Color.TitlebarMaterial"), lightStrip);
            Assert.Equal(ThemeColor("Foreman.Color.TitlebarMaterial"), TitleBarRowColor(lightTitleBar));

            app.ApplyTheme(ThemeMode.Dark);
            Color darkStrip = ArrangedTrafficLightStrip(out Grid darkTitleBar);
            Assert.Equal(ThemeColor("Foreman.Color.TitlebarMaterial"), darkStrip);
            Assert.Equal(ThemeColor("Foreman.Color.TitlebarMaterial"), TitleBarRowColor(darkTitleBar));

            Assert.NotEqual(lightStrip, darkStrip);
        }

        [AvaloniaFact]
        public void TitleBarRow_Strip_FollowsTitlebarMaterialOpaque_UnderReduceTransparency() {
            var app = (App)Application.Current!;
            app.ApplyTheme(ThemeMode.Light);
            Color shippedMaterial = ThemeColor("Foreman.Color.TitlebarMaterial");
            Color scopedOpaque = Color.Parse("#FF34C759");
            Assert.NotEqual(shippedMaterial, scopedOpaque);
            AppearanceHub? realHub = AppearanceHub.Current;
            app.Resources["Foreman.Color.TitlebarMaterialOpaque"] = scopedOpaque;
            var reducedHub = new AppearanceHub(app, new FakeAppearance { ReduceTransparency = true });
            AppearanceHub.Current = reducedHub;
            try {
                Color reduced = ArrangedTrafficLightStrip(out Grid reducedTitleBar);
                Assert.Equal(scopedOpaque, reduced);
                Assert.Equal(scopedOpaque, TitleBarRowColor(reducedTitleBar));

                reducedHub.Dispose();
                var plainHub = new AppearanceHub(app, new FakeAppearance { ReduceTransparency = false });
                AppearanceHub.Current = plainHub;
                try {
                    Color plain = ArrangedTrafficLightStrip(out Grid plainTitleBar);
                    Assert.Equal(shippedMaterial, plain);
                    Assert.Equal(shippedMaterial, TitleBarRowColor(plainTitleBar));
                } finally {
                    plainHub.Dispose();
                }
            } finally {
                app.Resources.Remove("Foreman.Color.TitlebarMaterialOpaque");
                AppearanceHub.Current = realHub;
                realHub?.Refresh();
            }
        }

        private static Color ArrangedTrafficLightStrip(out Grid titleBar) {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Toolbar.Margin = new Thickness(TrafficLightInsetConverter.TrafficLightInsetWidth, 0, 0, 0);
            Dispatcher.UIThread.RunJobs();

            titleBar = window.FindControl<Grid>("TitleBarRow")!;
            Assert.Equal(TrafficLightInsetConverter.TrafficLightInsetWidth, window.Toolbar.Bounds.Left);
            Assert.Equal(0, titleBar.Bounds.Left);
            Assert.True(titleBar.Bounds.Width >= window.Toolbar.Bounds.Right);
            return TitleBarRowColor(titleBar);
        }

        private static Color ThemeColor(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            return Assert.IsType<Color>(value);
        }

        private static Color TitleBarRowColor(Grid titleBar) =>
            Assert.IsAssignableFrom<ISolidColorBrush>(titleBar.Background).Color;

        private sealed class FakeAppearance : IPlatformAppearance {
            private EventHandler? changed;
            public Color? AccentColor { get; set; }
            public ColorContrastPreference ContrastPreference { get; set; } = ColorContrastPreference.NoPreference;
            public bool ReduceTransparency { get; set; }
            public bool ReduceMotion { get; set; }
            public PlatformThemeVariant ThemeVariant { get; set; } = PlatformThemeVariant.Light;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        [AvaloniaFact]
        public async Task NodeDrag_SetsDirty() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            BaseNodeElement element = window.GraphCanvas.Viewer.NodeElementDictionary[id];
            element.RequestStateUpdate();
            element.PrePaint();
            window.ViewModel.IsDirty = false;
            Dispatcher.UIThread.RunJobs();

            Point canvasPoint = window.GraphCanvas.Viewport.GraphToScreen(element.Location);
            Point screenPoint = window.GraphCanvas.TranslatePoint(canvasPoint, window)!.Value;
            Point dragTo = screenPoint + new Point(40, 0);
            window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton);
            window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton);
            window.MouseMove(dragTo, RawInputModifiers.LeftMouseButton);
            window.MouseUp(dragTo, MouseButton.Left, RawInputModifiers.None);

            Assert.True(window.ViewModel.IsDirty);
        }

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
            window.ViewModel.IsDirty = false;
            window.GraphCanvas.TextPropertiesDialogStub = _ => Task.FromResult(true);

            window.GraphCanvas.Viewer.ShowAnnotationPropertiesDialog?.Invoke(text);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public void TextPropertiesSheet_EscapeReverts_AndStaysClean() {
            var window = new MainWindow();
            window.Show();
            var text = new TextAnnotationElement(new System.Drawing.Point(0, 0));
            window.GraphCanvas.Viewer.AddAnnotationElement(text);
            string originalText = text.Text;
            window.ViewModel.IsDirty = false;

            window.GraphCanvas.Viewer.ShowAnnotationPropertiesDialog?.Invoke(text);
            var sheet = Assert.IsType<TextPropertiesSheet>(SheetHost.For(window).Content);
            sheet.TextInputControl.Text = originalText + " edited live";
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(originalText, text.Text);

            CanvasKeys.KeyDownOn(sheet.TextInputControl, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(originalText, text.Text);
            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public void ShapePropertiesSheet_EscapeReverts_AndStaysClean() {
            var window = new MainWindow();
            window.Show();
            var shape = new ShapeAnnotationElement(new System.Drawing.Point(0, 0), 200, 150);
            window.GraphCanvas.Viewer.AddAnnotationElement(shape);
            int originalBorderWidth = shape.BorderWidth;
            window.ViewModel.IsDirty = false;

            window.GraphCanvas.Viewer.ShowAnnotationPropertiesDialog?.Invoke(shape);
            var sheet = Assert.IsType<ShapePropertiesSheet>(SheetHost.For(window).Content);
            sheet.BorderWidthStepperControl.Value = originalBorderWidth + 23;
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(originalBorderWidth, shape.BorderWidth);

            CanvasKeys.KeyDownOn(sheet.BorderWidthStepperControl, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(originalBorderWidth, shape.BorderWidth);
            Assert.False(window.ViewModel.IsDirty);
        }

        [AvaloniaFact]
        public void PopoverHost_IsMountedOnce_AndSpansTheWholeWindow() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var host = Assert.Single(window.GetVisualDescendants().OfType<PopoverHost>(), h => !window.GraphCanvas.IsVisualAncestorOf(h));
            var rootGrid = (Grid)window.Content!;

            Assert.Equal(rootGrid.Bounds.Width, host.Bounds.Width, 1);
            Assert.Equal(rootGrid.Bounds.Height, host.Bounds.Height, 1);
        }

        [AvaloniaFact]
        public async Task PopoverHost_WithNothingOpen_ARealClickSelectsTheNodeUnderTheCursor() {
            var window = new MainWindow();
            window.Show();
            DataCache cache = await GetCacheAsync();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId id = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            BaseNodeElement element = window.GraphCanvas.Viewer.NodeElementDictionary[id];
            element.RequestStateUpdate();
            element.PrePaint();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Point canvasPoint = window.GraphCanvas.Viewport.GraphToScreen(element.Location);
            Point screenPoint = window.GraphCanvas.TranslatePoint(canvasPoint, window)!.Value;
            Assert.Same(window.GraphCanvas, window.InputHitTest(screenPoint));

            window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Contains(element, window.GraphCanvas.Viewer.SelectedNodes);
        }

    }
}
