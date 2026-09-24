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
        public async Task Settings_ThemeFlip_KeepsItsHubSubscription_AndNoSubscriberLeakAfterClose() {
            App app = CurrentApp;
            int baseline = app.Hub.SubscriberCount;
            DataCache cache = await GetCacheAsync();

            var window = new SettingsWindow(new SettingsWindow.SettingsWindowOptions(cache) {
                Presets = [new Preset(VanillaPresetName, true, true)],
            });
            window.Show();
            Assert.True(app.Hub.SubscriberCount > baseline);

            int subscribed = app.Hub.SubscriberCount;

            app.ApplyTheme(ThemeMode.Dark);

            Assert.Equal(subscribed, app.Hub.SubscriberCount);

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
            int shown = app.Hub.SubscriberCount;
            Assert.True(shown > baseline);

            var sidebar = window.FindControl<Border>("Sidebar")!;
            Color beforeColor = ColorOf(sidebar.Background!);

            app.ApplyTheme(ThemeMode.Dark);

            Assert.NotEqual(beforeColor, ColorOf(sidebar.Background!));

            //Close hides this window; the kit grid still in its tree keeps its own subscription.
            window.Close();
            Assert.Equal(shown - 1, app.Hub.SubscriberCount);
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
