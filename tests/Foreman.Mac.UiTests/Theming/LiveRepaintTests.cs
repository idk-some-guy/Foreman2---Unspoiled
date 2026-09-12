using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Theming {
    //Opens Settings, a sheet, the comparator, and the browser, flips the theme through the app-wide
    //AppearanceHub, confirms each repaints, and confirms none of them are still subscribed once closed.
    //GraphThemeTests/DarkModeTests already cover MainWindow's own canvas repaint and the System-mode
    //platform-follow path; this file covers the rest of the surface list.
    public class LiveRepaintTests : IDisposable {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                sharedCache ??= await LoadVanillaAsync();
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        private static async Task<DataCache> LoadVanillaAsync() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
            return cache;
        }

        private static App CurrentApp => (App)Application.Current!;

        public LiveRepaintTests() => CurrentApp.ApplyTheme(ThemeMode.Light);

        public void Dispose() {
            CurrentApp.ApplyTheme(ThemeMode.Light);
            GC.SuppressFinalize(this);
        }

        private static Color ColorOf(IBrush brush) => Assert.IsType<ImmutableSolidColorBrush>(brush).Color;

        //--- Settings ---------------------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task Settings_ThemeFlip_EnabledObjectsRowRepaints_AndNoSubscriberLeakAfterClose() {
            App app = CurrentApp;
            int baseline = app.Hub.SubscriberCount;
            DataCache cache = await GetCacheAsync();

            var window = new SettingsWindow(new SettingsWindow.SettingsWindowOptions(cache) {
                Presets = [new Preset(VanillaPresetName, true, true)],
            });
            window.Show();
            Assert.True(app.Hub.SubscriberCount > baseline);

            EnabledObjectsListItem before = window.ViewModel.RecipeObjects.First(r => r.DataObject.Available);
            Color beforeColor = ColorOf(before.RowBackground);

            app.ApplyTheme(ThemeMode.Dark);

            EnabledObjectsListItem after = window.ViewModel.RecipeObjects.First(r => r.DataObject.Available);
            Assert.NotEqual(beforeColor, ColorOf(after.RowBackground));

            window.Close();
            Assert.Equal(baseline, app.Hub.SubscriberCount);
        }

        //--- A sheet (GraphSummarySheet) -------------------------------------------------------------------

        private sealed class GraphFixture {
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
        }

        private static GraphFixture NewGraphFixture(DataCache cache) {
            var graph = new ProductionGraph { DefaultAssemblerQuality = cache.DefaultQuality };
            var session = new ProductionGraphSession(graph);
            session.Attach();
            return new GraphFixture { Graph = graph, Session = session };
        }

        //GraphSummarySheet no longer self-attaches - SheetHost is the one subscriber, forwarding to whatever
        //it's showing (SheetHost.cs's ctor comment). Hosting it for real here, rather than in a bare Window,
        //is what exercises that path and catches a regression back to the double-refresh bug.
        [AvaloniaFact]
        public async Task GraphSummarySheet_ThemeFlip_ItemRowRepaints_ExactlyOnce_WhenHosted() {
            App app = CurrentApp;
            DataCache cache = await GetCacheAsync();
            IItem item = cache.Items.Values.First(i => i.Available);
            GraphFixture fx = NewGraphFixture(cache);
            fx.Graph.CreateSupplierNode(new ItemQualityPair(item, cache.DefaultQuality!), System.Drawing.Point.Empty);
            fx.Graph.UpdateNodeValues();

            int preBaseline = app.Hub.SubscriberCount;
            var owner = new Window { Width = 900, Height = 700 };
            owner.Show();
            SheetHost host = SheetHost.For(owner);
            Assert.True(app.Hub.SubscriberCount > preBaseline);
            int baseline = app.Hub.SubscriberCount;

            var sheet = new GraphSummarySheet(fx.Session, "second");
            _ = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline, app.Hub.SubscriberCount);

            ItemRow before = sheet.UnfilteredItemsList.Concat(sheet.UnfilteredFluidsList).First();
            Color beforeColor = ColorOf(before.Background);
            int callsBefore = sheet.RebuildRowsCallCount;

            app.ApplyTheme(ThemeMode.Dark);

            Assert.Equal(callsBefore + 1, sheet.RebuildRowsCallCount);
            ItemRow after = sheet.UnfilteredItemsList.Concat(sheet.UnfilteredFluidsList).First();
            Assert.NotEqual(beforeColor, ColorOf(after.Background));

            owner.Close();
            Assert.Equal(preBaseline, app.Hub.SubscriberCount);
        }

        //--- The comparator (PresetComparatorWindow) --------------------------------------------------------

        private static DataCache NewEmptyCache() => new(filterRecipes: false);

        private static SubgroupPrototype NewSubgroup(DataCache cache) {
            var group = new GroupPrototype(cache, "g", "G", "a");
            var subgroup = new SubgroupPrototype(cache, "sg", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            return subgroup;
        }

        //ProcessObjects reads DataCache.Items, which only DataCacheStore.Items (internal) actually backs -
        //constructing an ItemPrototype alone never registers it there (same reflection seam
        //CatalogFixture.Store uses elsewhere in this test project).
        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        [AvaloniaFact]
        public async Task Comparator_ThemeFlip_LeftOnlyRowRepaints_AndNoSubscriberLeakAfterClose() {
            App app = CurrentApp;
            var leftPreset = new Preset("Left-only", true, true);
            var rightPreset = new Preset("Right-only", true, true);

            DataCache leftCache = NewEmptyCache();
            SubgroupPrototype subgroup = NewSubgroup(leftCache);
            var soloItem = new ItemPrototype(leftCache, "solo-item", "Solo Item", subgroup, "a") { Available = true };
            Store(leftCache).Items[soloItem.Name] = soloItem;
            DataCache rightCache = NewEmptyCache();

            int baseline = app.Hub.SubscriberCount;
            var window = new PresetComparatorWindow([leftPreset, rightPreset]) {
                LoadCacheStub = preset => Task.FromResult<DataCache?>(preset.Name == leftPreset.Name ? leftCache : rightCache),
            };
            window.Show();
            Assert.True(app.Hub.SubscriberCount > baseline);

            await window.SimulateProcessPresetsClickAsync();
            Dispatcher.UIThread.RunJobs();
            //Comparing lands on the Mods tab (index 0) by default - the solo item only shows up once the
            //Items tab (index 1) is selected, which re-runs RebuildTab against unfilteredItemTabObjects. A
            //real click through the input pipeline, not a direct SelectedIndex set, is what
            //PresetComparatorWindowTests' own tab-switch coverage relies on for this exact control.
            TabItem itemsTab = window.ComparisonTabControlControl.Items.OfType<TabItem>().ElementAt(1);
            Point tabScreen = itemsTab.TranslatePoint(new Point(itemsTab.Bounds.Width / 2, itemsTab.Bounds.Height / 2), window)!.Value;
            window.MouseDown(tabScreen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(tabScreen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            ComparatorRow before = Assert.Single(window.ViewModel.LeftOnlyRows);
            Color beforeColor = ColorOf(before.Background);

            app.ApplyTheme(ThemeMode.Dark);

            ComparatorRow after = Assert.Single(window.ViewModel.LeftOnlyRows);
            Assert.NotEqual(beforeColor, ColorOf(after.Background));

            window.Close();
            Assert.Equal(baseline, app.Hub.SubscriberCount);
        }

        //--- The browser (AddBrowserWindow) -----------------------------------------------------------------

        [AvaloniaFact]
        public void AddBrowserWindow_ThemeFlip_DynamicResourceBoundSidebarRepaints_AndNoSubscriberLeakAfterClose() {
            App app = CurrentApp;
            CatalogFixture fx = CatalogFixture.WithGroups();
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);

            int baseline = app.Hub.SubscriberCount;
            var window = new AddBrowserWindow { DataContext = vm };
            window.Show();
            Assert.True(app.Hub.SubscriberCount > baseline);

            var sidebar = window.FindControl<Border>("Sidebar")!;
            Color beforeColor = ColorOf(sidebar.Background!);

            app.ApplyTheme(ThemeMode.Dark);

            Assert.NotEqual(beforeColor, ColorOf(sidebar.Background!));

            window.Close();
            Assert.Equal(baseline, app.Hub.SubscriberCount);
        }

        //MainWindow owns browserIconCache and hands AddBrowserViewModel a bake delegate closed over it -
        //RefreshTheme must dispose+clear that cache and rebuild the viewmodel so a bound cell's icon is a
        //freshly baked Bitmap instance, not the stale one from before the flip.
        [AvaloniaFact]
        public void AddBrowserWindow_ThemeFlip_BoundCellIconIsRebaked() {
            using ShellFixture shell = ShellFixture.Open();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Items));
            Dispatcher.UIThread.RunJobs();

            var vm = (AddBrowserViewModel)shell.Window.BrowserWindow!.DataContext!;
            BrowserCell before = vm.Sections.SelectMany(s => s.Cells).First(c => c.Icon is not null);
            IImage iconBefore = before.Icon!;

            CurrentApp.ApplyTheme(ThemeMode.Dark);
            Dispatcher.UIThread.RunJobs();

            BrowserCell after = vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Equals(before.Candidate));
            Assert.NotSame(iconBefore, after.Icon);
        }

        //--- MainWindow's own hub subscription --------------------------------------------------------------

        [AvaloniaFact]
        public void MainWindow_Close_UnsubscribesFromTheAppWideHub() {
            App app = CurrentApp;
            int baseline = app.Hub.SubscriberCount;

            var window = new MainWindow();
            window.Show();
            Assert.True(app.Hub.SubscriberCount > baseline);

            window.Close();
            Assert.Equal(baseline, app.Hub.SubscriberCount);
        }
    }
}
