using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    //The full six-menu structure (contract #menubar): every action in the 22-row table, exact
    //gestures, the conflict resolutions verbatim, and the grid-dots/inspector/undo placeholders the table
    //marks as later-plan surfaces. MainWindowShellTests.cs keeps the four older native menu tests
    //(top-level headers, Save's gesture on both platforms, File's enabled state) - this file covers
    //everything beyond that baseline.
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

        private static IEnumerable<NativeMenuItem> AllItems(NativeMenu menu) {
            foreach (NativeMenuItem item in RealItems(menu)) {
                yield return item;
                if (item.Menu is NativeMenu submenu)
                    foreach (NativeMenuItem nested in AllItems(submenu))
                        yield return nested;
            }
        }

        //The gridline/rate/icon-view popover controls only join the visual tree once ViewOptionsPopover is
        //open (MainWindowShellTests.cs's own OpenViewOptions precedent) - this.FindControl resolves them
        //regardless via the compiled NameScope, which is what BuildNativeMenu itself relies on, but
        //GetVisualDescendants needs the popover open first.
        private static void OpenViewOptions(MainWindow window) {
            window.ViewModel.ToggleViewOptionsCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        //---- Foreman 2 (app) menu ----

        //Pins both native structures exactly, item by item, including separators - other tests here only look up items by header.
        [AvaloniaFact]
        public void AppMenu_And_WindowMenu_MatchTheContractShapeExactly() {
            var window = new MainWindow();

            var appHeaders = AppMenu().Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();
            var windowHeaders = TopMenu(window).Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();

            Assert.Equal(
                ["About Foreman 2", "-", "Settings…", "-", "Hide Foreman 2", "Hide Others", "Show All", "-", "Quit Foreman 2"],
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

        //Proves the real fix: the app menu is readable on Application.Current before any MainWindow is ever
        //constructed - it's built in App.Initialize, at the point AvaloniaNativeMenuExporter's own
        //Application-target constructor actually reads it (AppMenu_And_WindowMenu_MatchTheContractShapeExactly
        //pins the full shape).
        [AvaloniaFact]
        public void AppMenu_IsPresentOnApplicationBeforeAnyMainWindowExists() {
            NativeMenu appMenu = AppMenu();

            Assert.Equal("About Foreman 2", RealItems(appMenu)[0].Header);
            Assert.Single(RealItems(appMenu), i => i.Header == "Quit Foreman 2");
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

            Assert.Equal(["New Graph", "Open…", "Open Recent", "Save", "Save As…", "Import Graph…", "Export Image…", "Close"], headers);
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
            var dropdown = window.GetVisualDescendants().OfType<PopUp>().Single(c => c.Name == "MinorGridlinesDropDown");
            NativeMenu minorSubmenu = SubMenu(SubMenu(TopMenu(window), "View"), "Minor Gridlines");
            NativeMenuItem eight = minorSubmenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "8");

            eight.Command!.Execute(eight.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("8", dropdown.SelectedItem);
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
        public void ViewMenu_IconView_IsACheckboxWithNoGesture() {
            var window = new MainWindow();

            NativeMenuItem iconView = ItemNamed(SubMenu(TopMenu(window), "View"), "Icon View");

            Assert.Equal(MenuItemToggleType.CheckBox, iconView.ToggleType);
            Assert.Null(iconView.Gesture);
        }

        //§14 ruling 4: ⌘. is command-only cancel-in-flight, so no menu item may claim it.
        [AvaloniaFact]
        public void PauseAllCalculations_IsBoundToOptCmdP() {
            var window = new MainWindow();

            NativeMenuItem pause = ItemNamed(SubMenu(TopMenu(window), "View"), "Pause All Calculations");

            Assert.Equal(MenuItemToggleType.CheckBox, pause.ToggleType);
            Assert.Equal(new KeyGesture(Key.P, KeyModifiers.Meta | KeyModifiers.Alt), pause.Gesture);
            Assert.DoesNotContain(AllItems(TopMenu(window)), i => i.Gesture?.Key == Key.OemPeriod);
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

        //2026-09-17 building-picker ruling: one MainWindow method serves the inspector's own Rate unit row,
        //View Options' pop-up and the View menu radios - a pick from any of the three mirrors into the other
        //two, saves immediately, and costs exactly one solve, with one Save per pick.
        [AvaloniaFact]
        public async Task RateUnitPickedFromAnySurface_PersistsTheDefaultThroughSettingsService() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenWindowWithGraphAsync();
            var settingsService = new SettingsService(NewTempDir());
            window.SettingsService = settingsService;
            window.Settings = new AppSettings();
            Foreman.Graph.INodeViewModel node = window.GraphCanvas.Viewer.Session.View.Nodes.Single();
            Inspector.InspectorTestSupport.Select(window, node);

            var recipeView = window.Inspector.FindControl<Foreman.Mac.Views.Inspector.RecipeSegmentView>("RecipeSegment")!;
            PopUp inspectorRateUnit = recipeView.FindControl<PopUp>("RateUnitPopUp")!;
            OpenViewOptions(window);
            PopUp dropdown = window.GetVisualDescendants().OfType<PopUp>().Single(c => c.Name == "RateOptionsDropDown");
            NativeMenu rateUnitSubmenu = SubMenu(SubMenu(TopMenu(window), "View"), "Rate Unit");
            int solves = 0;
            window.GraphCanvas.Viewer.Session.NodeValuesUpdated += (_, _) => solves++;

            //Surface 1: the inspector's own Rate unit pop-up.
            inspectorRateUnit.Command!.Execute("per 5 minutes");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ProductionGraph.RateUnit.Per5Min, window.GraphCanvas.Viewer.Graph.SelectedRateUnit);
            Assert.Equal(ProductionGraph.RateUnit.Per5Min, ((AppSettings)window.Settings!).DefaultRateUnit);
            Assert.Equal("per 5 minutes", dropdown.SelectedItem);
            Assert.True(rateUnitSubmenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "5 min").IsChecked);
            Assert.Equal(1, settingsService.SaveCallCount);
            Assert.Equal(1, solves);

            //Surface 2: View Options' own pop-up - mirrors back into the inspector and the menu radio.
            dropdown.Command!.Execute("per 10 minutes");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ProductionGraph.RateUnit.Per10Min, window.GraphCanvas.Viewer.Graph.SelectedRateUnit);
            Assert.Equal(ProductionGraph.RateUnit.Per10Min, ((AppSettings)window.Settings!).DefaultRateUnit);
            Assert.Equal("per 10 minutes", inspectorRateUnit.SelectedItem);
            Assert.True(rateUnitSubmenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "10 min").IsChecked);
            Assert.Equal(2, settingsService.SaveCallCount);
            Assert.Equal(2, solves);

            //Surface 3: the View menu's own radio - mirrors back into the inspector and the dropdown.
            NativeMenuItem oneHour = rateUnitSubmenu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "1 hour");
            oneHour.Command!.Execute(oneHour.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ProductionGraph.RateUnit.Per1Hour, window.GraphCanvas.Viewer.Graph.SelectedRateUnit);
            Assert.Equal(ProductionGraph.RateUnit.Per1Hour, ((AppSettings)window.Settings!).DefaultRateUnit);
            Assert.Equal("per hour", inspectorRateUnit.SelectedItem);
            Assert.Equal("per hour", dropdown.SelectedItem);
            Assert.Equal(3, settingsService.SaveCallCount);
            Assert.Equal(3, solves);
        }

        //The reverse direction: a pick made on View Options must reach a bound Flow segment's own Rate unit
        //row too, not just the Recipe segment RateUnitPickedFromAnySurface covers.
        [AvaloniaFact]
        public async Task RateUnitPickedFromViewOptions_MirrorsIntoTheFlowSegmentsOwnPopUp() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            Foreman.Graph.NodeId supplierId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            window.GraphCanvas.Viewer.Session.View.TryGetNode(supplierId, out Foreman.Graph.INodeViewModel? supplier);
            Dispatcher.UIThread.RunJobs();
            Inspector.InspectorTestSupport.Select(window, supplier!);

            var flowView = window.Inspector.FindControl<Foreman.Mac.Views.Inspector.FlowSegmentView>("FlowSegment")!;
            PopUp flowRateUnit = flowView.FindControl<PopUp>("RateUnitPopUp")!;

            OpenViewOptions(window);
            PopUp dropdown = window.GetVisualDescendants().OfType<PopUp>().Single(c => c.Name == "RateOptionsDropDown");
            dropdown.Command!.Execute("per 30 minutes");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("per 30 minutes", flowRateUnit.SelectedItem);
        }

        private static string NewTempDir() {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "foreman-mac-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }

        //After ApplyLoadedSettings, every menu checkmark/radio-selection must reflect the settings
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

        //Show/Pin Inspector (contract #menubar): the inspector panel itself already exists, so
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

        [AvaloniaFact]
        public void ViewMenu_ItemsAreInContractOrder_WithShowToolbarAndADisabledCustomizeToolbar() {
            var window = new MainWindow();
            NativeMenu view = SubMenu(TopMenu(window), "View");

            Assert.Equal(
                ["Zoom In", "Zoom Out", "Actual Size", "Fit to Graph", "-",
                    "Show Gridlines", "Minor Gridlines", "Major Gridlines", "Rate Unit", "Link Routing", "Icon View", "Pause All Calculations", "-",
                    "Show Inspector", "Pin Inspector", "-",
                    "Show Toolbar", "Customize Toolbar\u2026", "Enter Full Screen"],
                view.Items.OfType<NativeMenuItem>().Select(i => i.Header));
            NativeMenuItem showToolbar = ItemNamed(view, "Show Toolbar");
            Assert.Equal(MenuItemToggleType.CheckBox, showToolbar.ToggleType);
            Assert.True(showToolbar.IsChecked);
            Assert.True(showToolbar.IsEnabled);
            AssertGesture(showToolbar, Key.T, KeyModifiers.Meta | KeyModifiers.Alt);
            NativeMenuItem customize = ItemNamed(view, "Customize Toolbar\u2026");
            Assert.False(customize.IsEnabled);
            Assert.Null(customize.Gesture);
        }

        //---- Window menu ----

        [AvaloniaFact]
        public void WindowMenu_ItemsAreInContractOrder() {
            var window = new MainWindow();

            var headers = RealItems(SubMenu(TopMenu(window), "Window")).Select(i => i.Header).ToList();

            Assert.Equal(["Minimize", "Zoom", "Welcome to Foreman", "Preset Comparator", "Bring All to Front"], headers);
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

        //---- selection, Find, window commands, ⌘. and the no-document state (v2 §05, §14 ruling 4, A25) ----

        [AvaloniaFact]
        public async Task EditMenu_SelectAllAndDeselectAll_HaveCmdAAndShiftCmdA_AndActOnTheCanvasSelection() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenWindowWithGraphAsync();
            DataCache cache = window.DataCache!;
            window.GraphCanvas.Viewer.Graph.CreateSupplierNode(new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!), new System.Drawing.Point(300, 0));
            Dispatcher.UIThread.RunJobs();
            NativeMenu editMenu = SubMenu(TopMenu(window), "Edit");
            NativeMenuItem selectAll = ItemNamed(editMenu, "Select All");
            NativeMenuItem deselectAll = ItemNamed(editMenu, "Deselect All");

            AssertGesture(selectAll, Key.A, KeyModifiers.Meta);
            AssertGesture(deselectAll, Key.A, KeyModifiers.Meta | KeyModifiers.Shift);

            window.GraphCanvas.Focus();
            System.Drawing.Point offset = window.GraphCanvas.Viewport.ViewOffset;
            var cmdA = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Meta };
            window.GraphCanvas.RaiseEvent(cmdA);
            Dispatcher.UIThread.RunJobs();
            Assert.False(cmdA.Handled);
            Assert.Equal(offset, window.GraphCanvas.Viewport.ViewOffset);

            selectAll.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, window.GraphCanvas.Viewer.SelectedNodes.Count);

            deselectAll.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.GraphCanvas.Viewer.SelectedNodes);
        }

        //A menu key equivalent reaches the menu even with a text field focused, so ⌘A must stay the field's own select-all there.
        [AvaloniaFact]
        public async Task EditMenu_SelectAll_WithATextFieldFocused_SelectsItsTextInsteadOfTheCanvas() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenWindowWithGraphAsync();
            SearchField search = window.Toolbar.SearchField;
            search.Text = "zzz";
            search.Focus();
            search.SelectionStart = search.SelectionEnd = 0;
            Dispatcher.UIThread.RunJobs();

            ItemNamed(SubMenu(TopMenu(window), "Edit"), "Select All").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, search.SelectionStart);
            Assert.Equal(3, search.SelectionEnd);
            Assert.Empty(window.GraphCanvas.Viewer.SelectedNodes);
        }

        [AvaloniaFact]
        public async Task EditMenu_Find_IsAFourItemSubmenu_WhoseItemsDriveTheToolbarSearch() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();
            DataCache cache = window.DataCache!;
            var pair = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            NodeId firstId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(0, 0));
            NodeId secondId = window.GraphCanvas.Viewer.Session.Editor.CreateSupplierNode(pair, new System.Drawing.Point(200, 0));
            foreach ((NodeId id, string title) in new[] { (firstId, "Iron Feed A"), (secondId, "Iron Feed B") }) {
                var controller = window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(id)!;
                controller.SetKeyNode(true);
                controller.SetKeyNodeTitle(title);
            }
            Dispatcher.UIThread.RunJobs();
            NativeMenu find = SubMenu(SubMenu(TopMenu(window), "Edit"), "Find");

            Assert.Equal(["Find Node…", "Find Next", "Find Previous", "-", "Select Next Node"], find.Items.OfType<NativeMenuItem>().Select(i => i.Header));
            AssertGesture(ItemNamed(find, "Find Node…"), Key.F, KeyModifiers.Meta);
            AssertGesture(ItemNamed(find, "Find Next"), Key.G, KeyModifiers.Meta);
            AssertGesture(ItemNamed(find, "Find Previous"), Key.G, KeyModifiers.Meta | KeyModifiers.Shift);
            //Canvas traversal (A14) lands with the canvas; a bare Tab key equivalent would take Tab from every text field.
            NativeMenuItem selectNext = ItemNamed(find, "Select Next Node");
            Assert.False(selectNext.IsEnabled);
            Assert.Null(selectNext.Gesture);

            ItemNamed(find, "Find Node…").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.Toolbar.SearchField.IsFocused);

            window.Toolbar.SearchField.Text = "Iron Feed";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(firstId, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);

            ItemNamed(find, "Find Next").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(secondId, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);

            ItemNamed(find, "Find Previous").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(firstId, Assert.Single(window.GraphCanvas.Viewer.SelectedNodes).ViewModel.Id);
        }

        //Neither the graph library (A3) nor the welcome window exists yet, so both items ship disabled rather than absent.
        [AvaloniaFact]
        public void OpenRecentAndWelcome_ShipDisabled_UntilTheGraphLibraryLands() {
            var window = new MainWindow();

            NativeMenuItem openRecent = ItemNamed(SubMenu(TopMenu(window), "File"), "Open Recent");
            NativeMenuItem welcome = ItemNamed(SubMenu(TopMenu(window), "Window"), "Welcome to Foreman");

            Assert.False(openRecent.IsEnabled);
            Assert.Empty(openRecent.Menu!.Items);
            Assert.Null(openRecent.Gesture);
            Assert.False(welcome.IsEnabled);
            Assert.Equal(new KeyGesture(Key.W, KeyModifiers.Meta | KeyModifiers.Shift), welcome.Gesture);
        }

        //Per-link routing and waypoints land with the canvas (A10); the submenu mirrors a selected link, and none can be selected yet.
        [AvaloniaFact]
        public void ViewMenu_LinkRouting_IsARadioSubmenuWithClearWaypoints_DisabledUntilLinksAreSelectable() {
            var window = new MainWindow();

            NativeMenu linkRouting = SubMenu(SubMenu(TopMenu(window), "View"), "Link Routing");

            Assert.Equal(["Rounded", "Orthogonal", "-", "Clear Waypoints"], linkRouting.Items.OfType<NativeMenuItem>().Select(i => i.Header));
            Assert.Equal(MenuItemToggleType.Radio, ItemNamed(linkRouting, "Rounded").ToggleType);
            Assert.Equal(MenuItemToggleType.Radio, ItemNamed(linkRouting, "Orthogonal").ToggleType);
            Assert.All(RealItems(linkRouting), i => Assert.False(i.IsEnabled, i.Header));
        }

        [AvaloniaFact]
        public void ViewMenu_EnterFullScreen_HasCtrlCmdF_AndTogglesBackToThePriorState() {
            var window = new MainWindow();
            window.Show();
            NativeMenuItem fullScreen = ItemNamed(SubMenu(TopMenu(window), "View"), "Enter Full Screen");

            AssertGesture(fullScreen, Key.F, KeyModifiers.Meta | KeyModifiers.Control);

            fullScreen.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            fullScreen.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }

        //Settings' Compare Presets… and this item share one rule: fewer than two presets disables both.
        [AvaloniaFact]
        public void WindowMenu_PresetComparator_NeedsTwoPresets_AndOpensTheComparatorOnTheResolvedPresets() {
            var window = new MainWindow();
            window.Show();
            var settings = new AppSettings { CurrentPresetName = PresetResolver.DefaultPresetName };
            var presetResolver = new PresetResolver(NewTempDir(), NewTempDir());
            window.Settings = settings;
            window.PresetResolver = presetResolver;
            IReadOnlyList<Preset>? shown = null;
            window.PresetComparatorDialogStub = presets => shown = presets;
            NativeMenu windowMenu = SubMenu(TopMenu(window), "Window");
            NativeMenuItem comparator = ItemNamed(windowMenu, "Preset Comparator");
            Assert.Null(comparator.Gesture);

            OpenMenu(windowMenu);
            Assert.False(comparator.IsEnabled);
            comparator.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(shown);

            settings.CurrentPresetName = "Other";
            OpenMenu(windowMenu);
            Assert.True(comparator.IsEnabled);

            comparator.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(shown);
            Assert.Equal(presetResolver.BuildPresetList(settings.CurrentPresetName).Select(p => p.Name), shown!.Select(p => p.Name));
        }

        //The native exporter raises Opening through an internal bridge interface; headless has no exporter, so we call it the same way.
        private static void OpenMenu(NativeMenu menu) {
            Type bridge = typeof(NativeMenu).GetInterfaces().Single(i => i.Name == "INativeMenuExporterEventsImplBridge");
            bridge.GetMethod("RaiseOpening")!.Invoke(menu, null);
            Dispatcher.UIThread.RunJobs();
        }

        //---- Help menu (mac-ui-designer ruling "Help menu items", 2026-09-24) ----

        private const string RepoUrl = "https://github.com/idk-some-guy/Foreman2---Unspoiled";

        private sealed class RecordingLauncher(bool result) : Avalonia.Platform.Storage.ILauncher {
            public List<Uri> Launched { get; } = [];
            public Task<bool> LaunchUriAsync(Uri uri) { Launched.Add(uri); return Task.FromResult(result); }
            public Task<bool> LaunchFileAsync(Avalonia.Platform.Storage.IStorageItem storageItem) => Task.FromResult(false);
        }

        [AvaloniaFact]
        public async Task HelpMenu_FourItemsOpenTheirPagesInTheBrowser_AndAFailedOpenOffersCopyLink() {
            var window = new MainWindow();
            window.Show();
            window.IsDocumentOpen = false;
            var launcher = new RecordingLauncher(result: true);
            window.LauncherOverride = launcher;
            NativeMenu help = SubMenu(TopMenu(window), "Help");

            Assert.Equal(["Foreman 2 Help", "Keyboard Shortcuts", "-", "Foreman 2 on GitHub", "Report an Issue\u2026"], help.Items.OfType<NativeMenuItem>().Select(i => i.Header));
            Assert.All(RealItems(help), i => Assert.True(i.IsEnabled, i.Header));
            AssertGesture(ItemNamed(help, "Foreman 2 Help"), Key.OemQuestion, KeyModifiers.Meta | KeyModifiers.Shift);
            Assert.All(RealItems(help).Skip(1), i => Assert.Null(i.Gesture));
            Assert.DoesNotContain(AllItems(TopMenu(window)), i => i.Header!.Contains("Git repo"));

            string[] expected = [RepoUrl + "#readme", RepoUrl + "/blob/main/docs/keyboard-shortcuts.md", RepoUrl];
            foreach ((NativeMenuItem item, string url) in RealItems(help).Take(3).Zip(expected)) {
                launcher.Launched.Clear();
                item.Command!.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(url, Assert.Single(launcher.Launched).AbsoluteUri);
            }

            launcher.Launched.Clear();
            ItemNamed(help, "Report an Issue\u2026").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Uri issue = Assert.Single(launcher.Launched);
            Assert.Equal("/idk-some-guy/Foreman2---Unspoiled/issues/new", issue.AbsolutePath);
            Assert.StartsWith("?body=", issue.Query);
            Assert.StartsWith("Foreman 2 ", Uri.UnescapeDataString(issue.Query["?body=".Length..]));

            window.LauncherOverride = new RecordingLauncher(result: false);
            ItemNamed(help, "Foreman 2 on GitHub").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            AlertSheet alert = Assert.IsType<AlertSheet>(SheetHost.For(window).Content);
            Assert.Equal("Couldn\u2019t open your web browser.", alert.FrameControl.Headline);
            Assert.Equal(RepoUrl, alert.FrameControl.InformativeText);
            Assert.Equal(["Copy Link", "OK"], new[] { alert.YesButtonControl, alert.NoButtonControl }.Select(b => (string)b.Content!));
            Assert.Same(alert.YesButtonControl, alert.DefaultButton);

            alert.YesButtonControl.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(RepoUrl, await window.Clipboard!.TryGetTextAsync());
            Assert.False(SheetHost.For(window).IsOpen);
        }

        //§14 ruling 4: a running solve first, then the frontmost popover or radial (Escape's own order), then the frontmost sheet.
        [AvaloniaFact]
        public async Task CmdPeriod_WithASolveInFlight_CancelsTheSolve_AndOtherwiseDismissesTheFrontmostSheet() {
            MainWindow window = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();
            OpenViewOptions(window);
            window.GraphCanvas.AddItemAsync(new System.Drawing.Point(300, 200));
            Dispatcher.UIThread.RunJobs();
            var alert = new AlertSheet("Probe", "message", AlertButtons.YesNo);
            Task<ConfirmChoice> result = SheetHost.For(window).ShowAsync(alert);
            Dispatcher.UIThread.RunJobs();
            window.Toolbar.ZoomStepper.IsSolving = true;
            PopoverFrame viewOptions = window.GetVisualDescendants().OfType<PopoverFrame>().Single(f => f.Name == "ViewOptionsPopover");
            Assert.True(viewOptions.IsOpen);
            Assert.NotNull(window.GraphCanvas.OpenPalette);

            CanvasKeys.KeyDownOn(alert.YesButtonControl, Key.OemPeriod, KeyModifiers.Meta);
            Assert.False(window.Toolbar.ZoomStepper.IsSolving);
            Assert.True(viewOptions.IsOpen);
            Assert.False(result.IsCompleted);

            CanvasKeys.KeyDownOn(alert.YesButtonControl, Key.OemPeriod, KeyModifiers.Meta);
            Assert.False(viewOptions.IsOpen);
            Assert.NotNull(window.GraphCanvas.OpenPalette);
            Assert.False(result.IsCompleted);

            CanvasKeys.KeyDownOn(alert.YesButtonControl, Key.OemPeriod, KeyModifiers.Meta);
            Assert.Null(window.GraphCanvas.OpenPalette);
            Assert.False(result.IsCompleted);

            CanvasKeys.KeyDownOn(alert.YesButtonControl, Key.OemPeriod, KeyModifiers.Meta);
            Assert.True(result.IsCompleted);
            Assert.Equal(ConfirmChoice.No, await result);
        }

        //A25: with no document the ruled items disable, and leaving that state restores each item's own prior enablement.
        [AvaloniaFact]
        public void NoDocument_DisablesTheRuledItems_AndLeavesWindowWelcomeLive() {
            var window = new MainWindow();
            NativeMenu file = SubMenu(TopMenu(window), "File");
            NativeMenu edit = SubMenu(TopMenu(window), "Edit");
            NativeMenu view = SubMenu(TopMenu(window), "View");
            NativeMenu windowMenu = SubMenu(TopMenu(window), "Window");
            string[] canvasHalf = ["Zoom In", "Zoom Out", "Actual Size", "Fit to Graph", "Show Gridlines", "Minor Gridlines", "Major Gridlines",
                "Rate Unit", "Link Routing", "Icon View", "Pause All Calculations", "Show Inspector", "Pin Inspector"];
            bool welcomeBefore = ItemNamed(windowMenu, "Welcome to Foreman").IsEnabled;
            bool comparatorBefore = ItemNamed(windowMenu, "Preset Comparator").IsEnabled;

            window.IsDocumentOpen = false;

            foreach (string header in new[] { "Save", "Save As\u2026", "Export Image\u2026", "Close" })
                Assert.False(ItemNamed(file, header).IsEnabled, header);
            foreach (string header in new[] { "New Graph", "Open\u2026", "Import Graph\u2026" })
                Assert.True(ItemNamed(file, header).IsEnabled, header);
            Assert.All(AllItems(edit), i => Assert.False(i.IsEnabled, i.Header));
            foreach (string header in canvasHalf)
                Assert.False(ItemNamed(view, header).IsEnabled, header);
            Assert.True(ItemNamed(view, "Enter Full Screen").IsEnabled);
            Assert.True(ItemNamed(view, "Show Toolbar").IsEnabled);
            Assert.Equal(welcomeBefore, ItemNamed(windowMenu, "Welcome to Foreman").IsEnabled);
            Assert.Equal(comparatorBefore, ItemNamed(windowMenu, "Preset Comparator").IsEnabled);
            Assert.All(RealItems(windowMenu).Where(i => i.Header is not ("Welcome to Foreman" or "Preset Comparator")), i => Assert.True(i.IsEnabled, i.Header));

            window.IsDocumentOpen = true;

            Assert.True(ItemNamed(file, "Save").IsEnabled);
            Assert.True(ItemNamed(edit, "Select All").IsEnabled);
            Assert.False(ItemNamed(edit, "Undo").IsEnabled);
            Assert.True(ItemNamed(view, "Zoom In").IsEnabled);
            Assert.False(ItemNamed(view, "Customize Toolbar\u2026").IsEnabled);
            Assert.All(RealItems(SubMenu(view, "Link Routing")), i => Assert.False(i.IsEnabled, i.Header));
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
