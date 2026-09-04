using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.Views;
using Foreman.Mac.Views;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AvColor = Avalonia.Media.Color;

namespace Foreman.Mac.UiTests.Theming {
    public class DarkModeTests {
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

        //Fake for ThemeService's IPlatformColorSource seam (internal to Foreman.Mac.Services) - Avalonia's
        //own IPlatformSettings can't be implemented by test code, which is exactly why that seam exists.
        private sealed class FakeColorSource(bool isDark) : IPlatformColorSource {
            private EventHandler? changed;

            public bool IsDark { get; private set; } = isDark;
            public int SubscriberCount { get; private set; }

            public event EventHandler? Changed {
                add { changed += value; SubscriberCount++; }
                remove { changed -= value; SubscriberCount--; }
            }

            public void SetDark(bool isDark) {
                IsDark = isDark;
                changed?.Invoke(this, EventArgs.Empty);
            }
        }

        //--- ThemeService ---------------------------------------------------------------------------------

        [AvaloniaFact]
        public void Apply_Dark_ForcesRequestedThemeVariantDark() {
            var service = new ThemeService((App)Application.Current!);

            service.Apply(ThemeMode.Dark);

            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            Assert.True(service.IsDark);
        }

        [AvaloniaFact]
        public void Apply_Light_ForcesRequestedThemeVariantLight() {
            var service = new ThemeService((App)Application.Current!);

            service.Apply(ThemeMode.Light);

            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            Assert.False(service.IsDark);
        }

        [AvaloniaFact]
        public void Apply_System_SetsDefaultAndFollowsStubbedPlatformValueLive() {
            var colorSource = new FakeColorSource(isDark: true);
            var service = new ThemeService((App)Application.Current!, colorSource);

            service.Apply(ThemeMode.System);

            Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
            Assert.True(service.IsDark);
            Assert.Equal(1, colorSource.SubscriberCount);

            colorSource.SetDark(false);

            Assert.False(service.IsDark);
        }

        [AvaloniaFact]
        public void Apply_AwayFromSystem_UnsubscribesThePreviousPlatformSettings() {
            var colorSource = new FakeColorSource(isDark: true);
            var service = new ThemeService((App)Application.Current!, colorSource);
            service.Apply(ThemeMode.System);

            service.Apply(ThemeMode.Dark);

            Assert.Equal(0, colorSource.SubscriberCount);
        }

        [AvaloniaFact]
        public void Dispose_UnsubscribesFromPlatformSettings() {
            var colorSource = new FakeColorSource(isDark: true);
            var service = new ThemeService((App)Application.Current!, colorSource);
            service.Apply(ThemeMode.System);

            service.Dispose();

            Assert.Equal(0, colorSource.SubscriberCount);
        }

        //I-2 (final fix wave): ThemeService.IsDark used to be write-only - nothing consumed a live OS
        //appearance flip while System was active, so the Skia-painted canvas (no DynamicResource of its
        //own) never followed it. MainWindow.LiveThemeService now wires the new Changed event through to
        //the same canvas theme seed ApplyLoadedSettings uses (ApplyCanvasTheme, which reads
        //Application.Current.ActualThemeVariant - Avalonia itself guards IPlatformSettings against test
        //doubles, so a real OS flip is forced the same way this file's other tests fake an "ambient dark
        //system": a direct RequestedThemeVariant assignment). colorSource.SetDark is the actual trigger
        //under test - it's the one thing that fires ThemeService.Changed and, before this fix, was the one
        //thing MainWindow never listened for.
        [AvaloniaFact]
        public void OsColorFlip_InSystemMode_ChangesCanvasBackgroundColor() {
            var colorSource = new FakeColorSource(isDark: false);
            var themeService = new ThemeService((App)Application.Current!, colorSource);
            themeService.Apply(ThemeMode.System);

            var window = new MainWindow { LiveThemeService = themeService, Settings = new AppSettings { FlagDarkMode = ThemeMode.System } };
            window.Show();
            window.ApplyLoadedSettings();
            SKColor before = window.GraphCanvas.Viewer.BackgroundColor;

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            colorSource.SetDark(true);

            SKColor after = window.GraphCanvas.Viewer.BackgroundColor;
            Assert.NotEqual(before, after);
        }

        //--- GraphSummary rows ------------------------------------------------------------------------------

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
        }

        private static Fixture NewFixture(DataCache cache) {
            var graph = new ProductionGraph { DefaultAssemblerQuality = cache.DefaultQuality };
            var session = new ProductionGraphSession(graph);
            session.Attach();
            return new Fixture { Cache = cache, Graph = graph, Session = session };
        }

        private static GraphSummaryWindow NewSummaryWindowWithSupplierRow(Fixture fx, IItem item) {
            fx.Graph.CreateSupplierNode(new ItemQualityPair(item, fx.Cache.DefaultQuality!), System.Drawing.Point.Empty);
            fx.Graph.UpdateNodeValues();
            return new GraphSummaryWindow(fx.Session, "second");
        }

        [AvaloniaFact]
        public async Task GraphSummaryRow_Dark_UsesDarkStatusTokens() {
            ((App)Application.Current!).ApplyTheme(ThemeMode.Dark);
            var fx = NewFixture(await GetCacheAsync());
            IItem item = fx.Cache.Items.Values.First(i => i.Available);

            var window = NewSummaryWindowWithSupplierRow(fx, item);
            ItemRow row = window.UnfilteredItemsList.Concat(window.UnfilteredFluidsList).First();

            Assert.Equal(AvColor.Parse("#3D3D40"), Assert.IsType<ImmutableSolidColorBrush>(row.Background).Color);
            Assert.Equal(AvColor.Parse("#F2F2F2"), Assert.IsType<ImmutableSolidColorBrush>(row.Foreground).Color);
        }

        [AvaloniaFact]
        public async Task GraphSummaryRow_Light_UsesLightStatusTokens() {
            ((App)Application.Current!).ApplyTheme(ThemeMode.Light);
            var fx = NewFixture(await GetCacheAsync());
            IItem item = fx.Cache.Items.Values.First(i => i.Available);

            var window = NewSummaryWindowWithSupplierRow(fx, item);
            ItemRow row = window.UnfilteredItemsList.Concat(window.UnfilteredFluidsList).First();

            Assert.Equal(AvColor.Parse("#FFFFFF"), Assert.IsType<ImmutableSolidColorBrush>(row.Background).Color);
            Assert.Equal(AvColor.Parse("#000000"), Assert.IsType<ImmutableSolidColorBrush>(row.Foreground).Color);
        }

        [AvaloniaTheory]
        [InlineData(ThemeMode.Light)]
        [InlineData(ThemeMode.Dark)]
        public async Task GraphSummaryRows_ContrastAssert_BothVariants(ThemeMode mode) {
            ((App)Application.Current!).ApplyTheme(mode);
            var fx = NewFixture(await GetCacheAsync());
            IItem available = fx.Cache.Items.Values.First(i => i.Available);
            var window = NewSummaryWindowWithSupplierRow(fx, available);

            ItemRow availableRow = window.UnfilteredItemsList.Concat(window.UnfilteredFluidsList).First();
            ContrastAssert.Readable(availableRow.Foreground, availableRow.Background);

            IItem? unavailable = fx.Cache.Items.Values.FirstOrDefault(i => !i.Available);
            if (unavailable is not null) {
                var unavailableFx = NewFixture(await GetCacheAsync());
                var unavailableWindow = NewSummaryWindowWithSupplierRow(unavailableFx, unavailable);
                ItemRow unavailableRow = unavailableWindow.UnfilteredItemsList.Concat(unavailableWindow.UnfilteredFluidsList).First();
                ContrastAssert.Readable(unavailableRow.Foreground, unavailableRow.Background);
            }
        }

        //--- Enabled Objects list ---------------------------------------------------------------------------

        private static SettingsWindow.SettingsWindowOptions NewOptions(DataCache cache) =>
            new(cache) { Presets = [new Preset(VanillaPresetName, true, true)] };

        [AvaloniaFact]
        public async Task EnabledObjectsRow_Dark_UsesDarkStatusTokens() {
            ((App)Application.Current!).ApplyTheme(ThemeMode.Dark);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync()));

            EnabledObjectsListItem row = window.ViewModel.RecipeObjects.First(r => r.DataObject.Available);

            Assert.Equal(AvColor.Parse("#3D3D40"), Assert.IsType<ImmutableSolidColorBrush>(row.RowBackground).Color);
            Assert.Equal(AvColor.Parse("#F2F2F2"), Assert.IsType<ImmutableSolidColorBrush>(row.Foreground).Color);
        }

        [AvaloniaFact]
        public async Task EnabledObjectsRow_Light_UsesLightStatusTokens() {
            ((App)Application.Current!).ApplyTheme(ThemeMode.Light);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync()));

            EnabledObjectsListItem row = window.ViewModel.RecipeObjects.First(r => r.DataObject.Available);

            Assert.Equal(AvColor.Parse("#FFFFFF"), Assert.IsType<ImmutableSolidColorBrush>(row.RowBackground).Color);
            Assert.Equal(AvColor.Parse("#000000"), Assert.IsType<ImmutableSolidColorBrush>(row.Foreground).Color);
        }

        [AvaloniaTheory]
        [InlineData(ThemeMode.Light)]
        [InlineData(ThemeMode.Dark)]
        public async Task EnabledObjectsRows_ContrastAssert_BothVariants(ThemeMode mode) {
            ((App)Application.Current!).ApplyTheme(mode);
            var window = new SettingsWindow(NewOptions(await GetCacheAsync()));

            EnabledObjectsListItem availableRow = window.ViewModel.RecipeObjects.First(r => r.DataObject.Available);
            ContrastAssert.Readable(availableRow.Foreground, availableRow.RowBackground);

            EnabledObjectsListItem? unavailableRow = window.ViewModel.RecipeObjects.FirstOrDefault(r => !r.DataObject.Available);
            if (unavailableRow is not null)
                ContrastAssert.Readable(unavailableRow.Foreground, unavailableRow.RowBackground);
        }
    }
}
