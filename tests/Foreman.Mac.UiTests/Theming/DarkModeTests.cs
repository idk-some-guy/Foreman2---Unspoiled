using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.Views;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
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

        //Bare fake for AppearanceHub's own seam (Foreman.Mac.Kit.Appearance.IPlatformAppearance) - this
        //file only needs a hub to exist so the System-mode variant flip below has somewhere to route
        //through (AppearanceHub.Current), not to exercise accent/contrast/motion behavior.
        private sealed class FakeAppearance : IPlatformAppearance {
            private EventHandler? changed;

            public AvColor? AccentColor => null;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.NoPreference;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;

            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        //--- ThemeService ---------------------------------------------------------------------------------

        [AvaloniaFact]
        public void Apply_Dark_ForcesRequestedThemeVariantDark() {
            var service = new ThemeService((App)Application.Current!);

            service.Apply(ThemeMode.Dark);

            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Dark, Application.Current!.ActualThemeVariant);
        }

        [AvaloniaFact]
        public void Apply_Light_ForcesRequestedThemeVariantLight() {
            var service = new ThemeService((App)Application.Current!);

            service.Apply(ThemeMode.Light);

            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            Assert.Equal(ThemeVariant.Light, Application.Current!.ActualThemeVariant);
        }

        //ThemeService has no live-follow logic of its own - System hands RequestedThemeVariant to
        //Avalonia's built-in OS-follow, and the headless platform can't be driven to prove that follow
        //(GraphThemeTests hits the same wall: its System-mode variant leg is a direct assignment, not a
        //raised platform event). So this stays a ThemeService unit test: Default is the whole contract.
        [AvaloniaFact]
        public void Apply_System_SetsRequestedThemeVariantToDefault() {
            var service = new ThemeService((App)Application.Current!);

            service.Apply(ThemeMode.System);

            Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
        }

        //MainWindow subscribes to AppearanceHub like every other window (IThemeAware.RefreshTheme ->
        //ApplyCanvasTheme, which reads Application.Current.ActualThemeVariant): GraphCanvas's Skia surface
        //has no DynamicResource of its own to follow a live OS flip, and the hub is its single notification
        //path. Avalonia guards IPlatformSettings against test doubles, so the flip is forced the same way
        //this file's other tests fake an "ambient dark system" - a direct RequestedThemeVariant
        //assignment, which the hub hears on its own ActualThemeVariantChanged hook.
        [AvaloniaFact]
        public void OsColorFlip_InSystemMode_ChangesCanvasBackgroundColor() {
            AppearanceHub? previousCurrent = AppearanceHub.Current;
            ThemeVariant? previousVariant = Application.Current!.RequestedThemeVariant;
            var hub = new AppearanceHub((App)Application.Current!, new FakeAppearance());
            try {
                AppearanceHub.Current = hub;
                Application.Current!.RequestedThemeVariant = ThemeVariant.Default;

                var window = new MainWindow { Settings = new AppSettings { FlagDarkMode = ThemeMode.System } };
                window.Show();
                window.ApplyLoadedSettings();
                SKColor before = window.GraphCanvas.Viewer.BackgroundColor;

                Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

                Assert.NotEqual(before, window.GraphCanvas.Viewer.BackgroundColor);
            } finally {
                hub.Dispose();
                AppearanceHub.Current = previousCurrent;
                Application.Current!.RequestedThemeVariant = previousVariant;
            }
        }
    }
}
