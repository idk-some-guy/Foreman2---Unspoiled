using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Services;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Task 5 (contract #menubar): the full six-menu structure - every action in the 22-row table, exact
    //gestures, the conflict resolutions verbatim, and the grid-dots/inspector/undo placeholders the table
    //marks as later-plan surfaces. MainWindowShellTests.cs keeps the four Task-4-era native menu tests
    //(top-level headers, Save's gesture on both platforms, File's enabled state) - this file covers
    //everything Task 5 added on top of that.
    public class NativeMenuTests {
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

        private static NativeMenu TopMenu(MainWindow window) => NativeMenu.GetMenu(window)!;

        //A: the bold application menu is Application-scoped, not the window's own first item.
        private static NativeMenu AppMenu() => NativeMenu.GetMenu(Application.Current!)!;

        private static NativeMenu SubMenu(NativeMenu menu, string header) =>
            menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == header).Menu!;

        //NativeMenuItemSeparator is itself a NativeMenuItem (Header "-"), so every lookup below excludes it
        //explicitly before grouping/indexing by Header.
        private static IReadOnlyList<NativeMenuItem> RealItems(NativeMenu menu) =>
            [.. menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator)];

        private static NativeMenuItem ItemNamed(NativeMenu menu, string header) => RealItems(menu).Single(i => i.Header == header);

        //The gridline/rate/icon-view popover controls only join the visual tree once ViewOptionsPopover is
        //open (MainWindowShellTests.cs's own OpenViewOptions precedent) - this.FindControl resolves them
        //regardless via the compiled NameScope, which is what BuildNativeMenu itself relies on, but
        //GetVisualDescendants needs the popover open first.
        private static void OpenViewOptions(MainWindow window) {
            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        //---- top-level structure ----

        [AvaloniaFact]
        public void TopLevel_IsFileEditViewWindowHelpInOrder() {
            var window = new MainWindow();

            var headers = TopMenu(window).Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();

            Assert.Equal(["File", "Edit", "View", "Window", "Help"], headers);
        }

        //---- Foreman 2 (app) menu ----

        //A: set on Application.Current, or the ObjC side falls back to Avalonia's own "About Avalonia" app menu.
        [AvaloniaFact]
        public void AppMenu_IsSetOnApplicationWithAboutFirstAndExactlyOneQuit() {
            var window = new MainWindow();

            NativeMenu appMenu = AppMenu();

            Assert.Equal("About Foreman 2", RealItems(appMenu)[0].Header);
            Assert.Single(RealItems(appMenu), i => i.Header == "Quit Foreman 2");
        }

        //Pins both native structures exactly, item by item, including separators - other tests here only look up items by header.
        [AvaloniaFact]
        public void AppMenu_And_WindowMenu_MatchTheContractShapeExactly() {
            var window = new MainWindow();

            var appHeaders = AppMenu().Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();
            var windowHeaders = TopMenu(window).Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();

            Assert.Equal(
                ["About Foreman 2", "-", "Settings", "-", "Hide Foreman 2", "Hide Others", "Show All", "-", "Quit Foreman 2"],
                appHeaders);
            Assert.Equal(["File", "Edit", "View", "Window", "Help"], windowHeaders);
        }

        [AvaloniaFact]
        public void AppMenu_AboutIsEnabledWithNoGesture() {
            var window = new MainWindow();

            NativeMenuItem about = ItemNamed(AppMenu(), "About Foreman 2");

            Assert.True(about.IsEnabled);
            Assert.Null(about.Gesture);
        }

        //Show() makes this the ActiveWindow the app menu's action items resolve at click time (see
        //MainWindow.WindowActionItem) - the app menu itself is Application-scoped and built before any
        //window exists, so it has no window of its own to close over.
        [AvaloniaFact]
        public void AppMenu_About_ClickInvokesTheAboutDialogStub() {
            var window = new MainWindow();
            window.Show();
            bool shown = false;
            window.AboutDialogStub = () => shown = true;
            NativeMenuItem about = ItemNamed(AppMenu(), "About Foreman 2");

            about.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(shown);
        }

        //Proves the real fix: the app menu is readable on Application.Current before any MainWindow is ever
        //constructed - it's built in App.Initialize, at the point AvaloniaNativeMenuExporter's own
        //Application-target constructor actually reads it (see AppMenu_IsSetOnApplicationWithAboutFirstAndExactlyOneQuit
        //for the shape check; this test only pins the timing).
        [AvaloniaFact]
        public void AppMenu_IsPresentOnApplicationBeforeAnyMainWindowExists() {
            NativeMenu appMenu = AppMenu();

            Assert.Equal("About Foreman 2", RealItems(appMenu)[0].Header);
            Assert.Single(RealItems(appMenu), i => i.Header == "Quit Foreman 2");
        }

        [AvaloniaFact]
        public void AppMenu_SettingsHasCmdCommaGesture() {
            var window = new MainWindow();

            NativeMenuItem settings = ItemNamed(AppMenu(), "Settings");

            Assert.Equal(Key.OemComma, settings.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, settings.Gesture.KeyModifiers);
        }

        [AvaloniaFact]
        public void AppMenu_Hide_HasCmdHGestureAndHidesTheWindow() {
            var window = new MainWindow();
            window.Show();
            NativeMenuItem hide = ItemNamed(AppMenu(), "Hide Foreman 2");

            Assert.Equal(Key.H, hide.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, hide.Gesture.KeyModifiers);
            Assert.True(hide.IsEnabled);

            hide.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.IsVisible);
        }

        //Hide Others/Show All: Avalonia 12.1.2's backing implementation is internal with no public surface, so these are disabled placeholders.
        [AvaloniaFact]
        public void AppMenu_HideOthersAndShowAll_AreDisabledPlaceholders() {
            var window = new MainWindow();
            NativeMenu appMenu = AppMenu();

            NativeMenuItem hideOthers = ItemNamed(appMenu, "Hide Others");
            NativeMenuItem showAll = ItemNamed(appMenu, "Show All");

            Assert.False(hideOthers.IsEnabled);
            Assert.Equal(Key.H, hideOthers.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta | KeyModifiers.Alt, hideOthers.Gesture.KeyModifiers);
            Assert.False(showAll.IsEnabled);
        }

        [AvaloniaFact]
        public void AppMenu_QuitHasCmdQGesture() {
            var window = new MainWindow();

            NativeMenuItem quit = ItemNamed(AppMenu(), "Quit Foreman 2");

            Assert.Equal(Key.Q, quit.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, quit.Gesture.KeyModifiers);
        }

        //---- File menu ----

        [AvaloniaFact]
        public void FileMenu_ItemsAreInContractOrder() {
            var window = new MainWindow();

            var headers = RealItems(SubMenu(TopMenu(window), "File")).Select(i => i.Header).ToList();

            Assert.Equal(["New", "Open", "Save", "Save As", "Import", "Export Image", "Close"], headers);
        }

        [AvaloniaFact]
        public void FileMenu_Close_HasCmdWGestureAndIsEnabled() {
            var window = new MainWindow();

            NativeMenuItem close = ItemNamed(SubMenu(TopMenu(window), "File"), "Close");

            Assert.True(close.IsEnabled);
            Assert.Equal(Key.W, close.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, close.Gesture.KeyModifiers);
        }

        //---- Edit menu ----

        [AvaloniaFact]
        public void EditMenu_UndoRedo_AreDisabledPlaceholdersWithGestures() {
            var window = new MainWindow();
            NativeMenu editMenu = SubMenu(TopMenu(window), "Edit");

            NativeMenuItem undo = ItemNamed(editMenu, "Undo");
            NativeMenuItem redo = ItemNamed(editMenu, "Redo");

            Assert.False(undo.IsEnabled);
            Assert.Equal(Key.Z, undo.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, undo.Gesture.KeyModifiers);

            Assert.False(redo.IsEnabled);
            Assert.Equal(Key.Z, redo.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta | KeyModifiers.Shift, redo.Gesture.KeyModifiers);
        }

        //Conflict resolution 1 (contract #menubar): Cut/Copy/Paste are Command-bound so they enable/disable/
        //click correctly, but carry no Gesture - the canvas's own Cmd+C/X/V keeps working unassisted.
        [AvaloniaFact]
        public void EditMenu_CutCopyPaste_AreEnabledWithNoGesture() {
            var window = new MainWindow();
            NativeMenu editMenu = SubMenu(TopMenu(window), "Edit");

            foreach (string header in new[] { "Cut", "Copy", "Paste" }) {
                NativeMenuItem item = ItemNamed(editMenu, header);
                Assert.True(item.IsEnabled, header);
                Assert.NotNull(item.Command);
                Assert.Null(item.Gesture);
            }
        }

        //Conflict resolution 3: same gesture-less treatment as Cut/Copy/Paste, since GraphCanvasControl.
        //OnKeyUp already owns the bare Delete key.
        [AvaloniaFact]
        public void EditMenu_Delete_IsEnabledWithNoGesture() {
            var window = new MainWindow();

            NativeMenuItem delete = ItemNamed(SubMenu(TopMenu(window), "Edit"), "Delete");

            Assert.True(delete.IsEnabled);
            Assert.NotNull(delete.Command);
            Assert.Null(delete.Gesture);
        }

        [AvaloniaFact]
        public async Task EditMenu_Copy_WritesSelectionToClipboard() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.DataCache = cache;
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            window.GraphCanvas.Viewer.SetSelection([.. window.GraphCanvas.Viewer.NodeElements]);
            string? written = null;
            window.GraphCanvas.Viewer.Context.SetClipboardText = text => written = text;

            ItemNamed(SubMenu(TopMenu(window), "Edit"), "Copy").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(written);
        }

        [AvaloniaFact]
        public async Task EditMenu_Delete_DeletesSelectedNodes() {
            DataCache cache = await GetCacheAsync();
            var window = new MainWindow();
            window.DataCache = cache;
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            window.GraphCanvas.Viewer.SetSelection([.. window.GraphCanvas.Viewer.NodeElements]);

            ItemNamed(SubMenu(TopMenu(window), "Edit"), "Delete").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(window.GraphCanvas.Viewer.NodeElements);
        }

        //---- View menu: zoom ----

        [AvaloniaFact]
        public void ViewMenu_ZoomItems_HaveContractGestures() {
            var window = new MainWindow();
            NativeMenu viewMenu = SubMenu(TopMenu(window), "View");

            AssertGesture(ItemNamed(viewMenu, "Zoom In"), Key.OemPlus, KeyModifiers.Meta);
            AssertGesture(ItemNamed(viewMenu, "Zoom Out"), Key.OemMinus, KeyModifiers.Meta);
            AssertGesture(ItemNamed(viewMenu, "Actual Size"), Key.D0, KeyModifiers.Meta);
            AssertGesture(ItemNamed(viewMenu, "Fit to Graph"), Key.D0, KeyModifiers.Meta | KeyModifiers.Shift);
        }

        private static void AssertGesture(NativeMenuItem item, Key key, KeyModifiers modifiers) {
            Assert.True(item.IsEnabled, item.Header);
            Assert.Equal(key, item.Gesture!.Key);
            Assert.Equal(modifiers, item.Gesture.KeyModifiers);
        }

        [AvaloniaFact]
        public void ViewMenu_ZoomIn_IncreasesViewScale() {
            var window = new MainWindow();
            window.Show();
            float before = window.GraphCanvas.Viewport.ViewScale;

            ItemNamed(SubMenu(TopMenu(window), "View"), "Zoom In").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.GraphCanvas.Viewport.ViewScale > before);
        }

        [AvaloniaFact]
        public void ViewMenu_ActualSize_SetsViewScaleToOne() {
            var window = new MainWindow();
            window.Show();
            window.GraphCanvas.Viewport.ZoomAt(new Avalonia.Point(400, 300), zoomIn: true);
            window.GraphCanvas.Viewport.ZoomAt(new Avalonia.Point(400, 300), zoomIn: true);

            ItemNamed(SubMenu(TopMenu(window), "View"), "Actual Size").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1f, window.GraphCanvas.Viewport.ViewScale);
        }

        //---- View menu: gridlines (contract's grid-dots vocabulary renames the ITEMS, not the flags they
        //toggle - MinorGridlinesDropDown/MajorGridlinesDropDown/GridlinesCheckbox are unchanged; the dot
        //rendering itself is a later plan's job) ----

        [AvaloniaFact]
        public void ViewMenu_ShowGridlines_IsACheckboxWithOptCmdGGesture() {
            var window = new MainWindow();

            NativeMenuItem showGridlines = ItemNamed(SubMenu(TopMenu(window), "View"), "Show Gridlines");

            Assert.Equal(MenuItemToggleType.CheckBox, showGridlines.ToggleType);
            Assert.Equal(Key.G, showGridlines.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta | KeyModifiers.Alt, showGridlines.Gesture.KeyModifiers);
        }

        [AvaloniaFact]
        public void ViewMenu_ShowGridlines_ClickTogglesGridAndPopoverCheckbox() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GridlinesCheckbox");
            window.GraphCanvas.Grid.ShowGrid = false;
            checkbox.IsChecked = false;
            NativeMenuItem showGridlines = ItemNamed(SubMenu(TopMenu(window), "View"), "Show Gridlines");

            showGridlines.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.GraphCanvas.Grid.ShowGrid);
            Assert.True(checkbox.IsChecked);
        }

        [AvaloniaFact]
        public void ViewMenu_MinorAndMajorGridlines_AreTwelveItemRadioSubmenusMirroringTheDropdowns() {
            var window = new MainWindow();
            NativeMenu viewMenu = SubMenu(TopMenu(window), "View");
            string[] expectedLabels = ["None", "1", "2", "4", "8", "16", "32", "64", "128", "256", "512", "1024"];

            foreach (string submenuHeader in new[] { "Minor Gridlines", "Major Gridlines" }) {
                NativeMenu submenu = SubMenu(viewMenu, submenuHeader);
                var items = submenu.Items.OfType<NativeMenuItem>().ToList();
                Assert.Equal(expectedLabels, items.Select(i => i.Header));
                Assert.All(items, i => Assert.Equal(MenuItemToggleType.Radio, i.ToggleType));
            }
        }

        [AvaloniaFact]
        public void ViewMenu_MinorGridlines_ClickSetsTheDropdownSelection() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var dropdown = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "MinorGridlinesDropDown");
            NativeMenu minorSubmenu = SubMenu(SubMenu(TopMenu(window), "View"), "Minor Gridlines");
            NativeMenuItem eight = minorSubmenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "8");

            eight.Command!.Execute(eight.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(4, dropdown.SelectedIndex); //index 4 = "8"
        }

        //B: NativeMenuItem only recomputes IsEnabled when its Command is (re)assigned, so Command must be set after CommandParameter.
        [AvaloniaFact]
        public void RadioItems_AllReportEnabledAfterBuildNativeMenu() {
            var window = new MainWindow();
            NativeMenu viewMenu = SubMenu(TopMenu(window), "View");

            var minor = SubMenu(viewMenu, "Minor Gridlines").Items.OfType<NativeMenuItem>().ToList();
            var major = SubMenu(viewMenu, "Major Gridlines").Items.OfType<NativeMenuItem>().ToList();
            var rate = SubMenu(viewMenu, "Rate Unit").Items.OfType<NativeMenuItem>().ToList();

            Assert.Equal(12, minor.Count);
            Assert.Equal(12, major.Count);
            Assert.Equal(6, rate.Count);
            Assert.All(minor, i => Assert.True(i.IsEnabled, i.Header));
            Assert.All(major, i => Assert.True(i.IsEnabled, i.Header));
            Assert.All(rate, i => Assert.True(i.IsEnabled, i.Header));
        }

        [AvaloniaFact]
        public void ViewMenu_IconViewAndPauseAllCalculations_AreCheckboxes() {
            var window = new MainWindow();
            NativeMenu viewMenu = SubMenu(TopMenu(window), "View");

            NativeMenuItem iconView = ItemNamed(viewMenu, "Icon View");
            NativeMenuItem pause = ItemNamed(viewMenu, "Pause All Calculations");

            Assert.Equal(MenuItemToggleType.CheckBox, iconView.ToggleType);
            Assert.Null(iconView.Gesture);
            Assert.Equal(MenuItemToggleType.CheckBox, pause.ToggleType);
            Assert.Equal(Key.OemPeriod, pause.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, pause.Gesture.KeyModifiers);
        }

        [AvaloniaFact]
        public void ViewMenu_IconView_ClickTogglesTheIconsOnlyCheckbox() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconViewCheckBox");
            checkbox.IsChecked = false;

            ItemNamed(SubMenu(TopMenu(window), "View"), "Icon View").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(checkbox.IsChecked);
        }

        [AvaloniaFact]
        public void ViewMenu_RateUnit_IsASixItemRadioSubmenuMirroringTheDropdown() {
            var window = new MainWindow();

            NativeMenu rateUnitSubmenu = SubMenu(SubMenu(TopMenu(window), "View"), "Rate Unit");

            var items = rateUnitSubmenu.Items.OfType<NativeMenuItem>().ToList();
            Assert.Equal(["1 sec", "1 min", "5 min", "10 min", "30 min", "1 hour"], items.Select(i => i.Header));
            Assert.All(items, i => Assert.Equal(MenuItemToggleType.Radio, i.ToggleType));
        }

        [AvaloniaFact]
        public void ViewMenu_RateUnit_ClickSetsTheDropdownSelection() {
            var window = new MainWindow();
            window.Show();
            OpenViewOptions(window);
            var dropdown = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "RateOptionsDropDown");
            NativeMenu rateUnitSubmenu = SubMenu(SubMenu(TopMenu(window), "View"), "Rate Unit");
            NativeMenuItem fiveMin = rateUnitSubmenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "5 min");

            fiveMin.Command!.Execute(fiveMin.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, dropdown.SelectedIndex);
        }

        //Task 5: after ApplyLoadedSettings, every menu checkmark/radio-selection must reflect the settings
        //that were actually loaded, not BuildNativeMenu's construction-time snapshot (taken before Settings
        //is even assigned).
        [AvaloniaFact]
        public void ViewMenu_ChecksResyncAfterApplyLoadedSettings() {
            var window = new MainWindow();
            window.Settings = new AppSettings { MinorGridlines = 4, MajorGridlines = 7, AltGridlines = true, DefaultRateUnit = ProductionGraph.RateUnit.Per1Hour, InspectorPinned = true };

            window.ApplyLoadedSettings();

            NativeMenu viewMenu = SubMenu(TopMenu(window), "View");
            Assert.True(ItemNamed(viewMenu, "Show Gridlines").IsChecked);
            NativeMenuItem minorEight = SubMenu(viewMenu, "Minor Gridlines").Items.OfType<NativeMenuItem>().Single(i => i.Header == "8"); //index 4
            Assert.True(minorEight.IsChecked);
            NativeMenuItem majorSixtyFour = SubMenu(viewMenu, "Major Gridlines").Items.OfType<NativeMenuItem>().Single(i => i.Header == "64"); //index 7
            Assert.True(majorSixtyFour.IsChecked);
            NativeMenuItem hourRate = SubMenu(viewMenu, "Rate Unit").Items.OfType<NativeMenuItem>().Single(i => i.Header == "1 hour");
            Assert.True(hourRate.IsChecked);
            Assert.True(ItemNamed(viewMenu, "Pin Inspector").IsChecked);
            Assert.True(window.ViewModel.Inspector.IsPinned);
        }

        //Show/Pin Inspector (contract #menubar): the inspector panel itself landed in C2a Tasks 1-2, so
        //these are live toggles now - Show flips ShowOverride only, Pin flips IsPinned and persists it.
        [AvaloniaFact]
        public void ViewMenu_ShowInspector_IsCheckboxToggleThatOverridesVisibility() {
            var window = new MainWindow();
            window.Show();
            NativeMenuItem showInspector = ItemNamed(SubMenu(TopMenu(window), "View"), "Show Inspector");

            Assert.True(showInspector.IsEnabled);
            Assert.Equal(MenuItemToggleType.CheckBox, showInspector.ToggleType);
            Assert.Equal(Key.I, showInspector.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta | KeyModifiers.Alt, showInspector.Gesture.KeyModifiers);

            showInspector.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.ViewModel.Inspector.ShowOverride);
            Assert.True(showInspector.IsChecked);

            showInspector.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.ViewModel.Inspector.ShowOverride);
            Assert.False(showInspector.IsChecked);
        }

        [AvaloniaFact]
        public void ViewMenu_PinInspector_TogglesPinAndPersists() {
            var window = new MainWindow();
            window.Show();
            window.Settings = new AppSettings();
            NativeMenuItem pinInspector = ItemNamed(SubMenu(TopMenu(window), "View"), "Pin Inspector");

            Assert.True(pinInspector.IsEnabled);
            Assert.Equal(MenuItemToggleType.CheckBox, pinInspector.ToggleType);
            Assert.Equal(Key.I, pinInspector.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta | KeyModifiers.Alt | KeyModifiers.Shift, pinInspector.Gesture.KeyModifiers);

            pinInspector.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.ViewModel.Inspector.IsPinned);
            Assert.True(window.Settings.InspectorPinned);
            Assert.True(pinInspector.IsChecked);
        }

        //Level of Detail: out of phase-9 scope per the table, left disabled exactly as it was before Task 5.
        [AvaloniaFact]
        public void ViewMenu_LevelOfDetail_StaysDisabled() {
            var window = new MainWindow();

            NativeMenuItem levelOfDetail = ItemNamed(SubMenu(TopMenu(window), "View"), "Level of Detail");

            Assert.False(levelOfDetail.IsEnabled);
        }

        //---- Window menu ----

        [AvaloniaFact]
        public void WindowMenu_ItemsAreInContractOrder() {
            var window = new MainWindow();

            var headers = RealItems(SubMenu(TopMenu(window), "Window")).Select(i => i.Header).ToList();

            Assert.Equal(["Minimize", "Zoom", "Bring All to Front"], headers);
        }

        [AvaloniaFact]
        public void WindowMenu_Minimize_HasCmdMGestureAndMinimizesTheWindow() {
            var window = new MainWindow();
            window.Show();
            NativeMenuItem minimize = ItemNamed(SubMenu(TopMenu(window), "Window"), "Minimize");

            Assert.Equal(Key.M, minimize.Gesture!.Key);
            Assert.Equal(KeyModifiers.Meta, minimize.Gesture.KeyModifiers);

            minimize.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.Minimized, window.WindowState);
        }

        [AvaloniaFact]
        public void WindowMenu_Zoom_TogglesMaximizedState() {
            var window = new MainWindow();
            window.Show();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            NativeMenuItem zoom = ItemNamed(SubMenu(TopMenu(window), "Window"), "Zoom");
            Assert.Null(zoom.Gesture);

            zoom.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Normal, window.WindowState);

            zoom.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }

        [AvaloniaFact]
        public void WindowMenu_BringAllToFront_IsEnabledWithNoGesture() {
            var window = new MainWindow();

            NativeMenuItem bringToFront = ItemNamed(SubMenu(TopMenu(window), "Window"), "Bring All to Front");

            Assert.True(bringToFront.IsEnabled);
            Assert.Null(bringToFront.Gesture);
        }

        //---- C1: menu dispatch deferral ----

        //C1: Execute must return before the wrapped action runs, and the action must only run once the dispatcher is pumped.
        [AvaloniaFact]
        public void MenuCommand_ExecuteDefersTheWrappedActionToTheDispatcher() {
            var window = new MainWindow();
            window.Show();
            bool shown = false;
            window.AboutDialogStub = () => shown = true;
            NativeMenuItem about = ItemNamed(AppMenu(), "About Foreman 2");

            about.Command!.Execute(null);
            Assert.False(shown);

            Dispatcher.UIThread.RunJobs();
            Assert.True(shown);
        }
    }
}
