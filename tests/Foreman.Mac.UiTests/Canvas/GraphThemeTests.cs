using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Styling;
using Foreman.Mac;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using System;
using Xunit;
using AvColor = Avalonia.Media.Color;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public class GraphThemeTests {
        private const string MovedTokenKey = "Foreman.Color.NodeRecipe";
        private static readonly AvColor MovedTokenValue = AvColor.FromRgb(0x01, 0x02, 0x03);

        private static void StageMovedCanvasToken() {
            TokenBridge.Refresh();
            Application.Current!.Resources[MovedTokenKey] = MovedTokenValue;
        }

        private static void ClearMovedCanvasToken() {
            Application.Current!.Resources.Remove(MovedTokenKey);
            TokenBridge.Refresh();
        }

        [AvaloniaFact]
        public void TokenBridge_ResolvedTwiceOntoTheSameValues_RaisesChangedOnce() {
            StageMovedCanvasToken();

            int changedCount = 0;
            EventHandler handler = (_, _) => changedCount++;
            TokenBridge.Changed += handler;
            try {
                TokenBridge.Refresh();
                TokenBridge.Refresh();

                Assert.Equal(1, changedCount);
            } finally {
                TokenBridge.Changed -= handler;
                ClearMovedCanvasToken();
            }
        }

        [AvaloniaFact]
        public void SetScale_RefreshesTheTokenBridgeExactlyOnce() {
            if (WindowScaler.CurrentScale != 1.0)
                WindowScaler.SetScale(1.0);

            int changedCount = 0;
            EventHandler handler = (_, _) => changedCount++;
            TokenBridge.Changed += handler;
            try {
                WindowScaler.SetScale(1.5);

                Assert.Equal(1, changedCount);
            } finally {
                TokenBridge.Changed -= handler;
                WindowScaler.SetScale(1.0);
            }
        }

        private sealed class HighContrastAppearance : IPlatformAppearance {
            private EventHandler? changed;
            public AvColor? AccentColor => null;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.High;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        private sealed class RaisableAppearance : IPlatformAppearance {
            private EventHandler? changed;
            public AvColor? AccentColor => null;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.NoPreference;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
            public void Raise() => changed?.Invoke(this, EventArgs.Empty);
        }

        [AvaloniaFact]
        public void AppearanceEvent_InSystemMode_RefreshesTheAppsHubAndBridgeExactlyOnce() {
            App app = (App)Application.Current!;
            ThemeVariant? previousVariant = app.RequestedThemeVariant;
            var appearance = new RaisableAppearance();
            app.InstallAppearance(appearance);
            app.ApplyTheme(ThemeMode.System);
            StageMovedCanvasToken();

            int hubChangedCount = 0;
            int bridgeChangedCount = 0;
            EventHandler hubHandler = (_, _) => hubChangedCount++;
            EventHandler bridgeHandler = (_, _) => bridgeChangedCount++;
            app.Hub.Changed += hubHandler;
            TokenBridge.Changed += bridgeHandler;
            try {
                appearance.Raise();

                Assert.Equal(1, hubChangedCount);
                Assert.Equal(1, bridgeChangedCount);
            } finally {
                TokenBridge.Changed -= bridgeHandler;
                app.Hub.Changed -= hubHandler;
                ClearMovedCanvasToken();
                app.InstallAppearance(null);
                app.RequestedThemeVariant = previousVariant;
            }
        }

        //Avalonia guards IPlatformSettings against test doubles, so the variant leg is a direct assignment.
        [AvaloniaFact]
        public void SystemModeVariantFlip_RefreshesTheHubTwiceAndRepublishesTheBridgeOnce() {
            App app = (App)Application.Current!;
            ThemeVariant? previousVariant = app.RequestedThemeVariant;
            var appearance = new RaisableAppearance();
            app.InstallAppearance(appearance);
            app.ApplyTheme(ThemeMode.System);
            StageMovedCanvasToken();

            int hubChangedCount = 0;
            int bridgeChangedCount = 0;
            EventHandler hubHandler = (_, _) => hubChangedCount++;
            EventHandler bridgeHandler = (_, _) => bridgeChangedCount++;
            app.Hub.Changed += hubHandler;
            TokenBridge.Changed += bridgeHandler;
            try {
                app.RequestedThemeVariant = ThemeVariant.Dark;
                appearance.Raise();

                Assert.Equal(2, hubChangedCount);
                Assert.Equal(1, bridgeChangedCount);
            } finally {
                TokenBridge.Changed -= bridgeHandler;
                app.Hub.Changed -= hubHandler;
                ClearMovedCanvasToken();
                app.InstallAppearance(null);
                app.RequestedThemeVariant = previousVariant;
            }
        }

        [AvaloniaFact]
        public void ApplyThemeDark_RefreshesHubAndBridgeExactlyOnce() {
            App app = (App)Application.Current!;
            AppearanceHub realHub = app.Hub;
            app.ApplyTheme(ThemeMode.Light);
            StageMovedCanvasToken();

            int hubChangedCount = 0;
            int bridgeChangedCount = 0;
            EventHandler hubHandler = (_, _) => hubChangedCount++;
            EventHandler bridgeHandler = (_, _) => bridgeChangedCount++;
            realHub.Changed += hubHandler;
            TokenBridge.Changed += bridgeHandler;
            try {
                app.ApplyTheme(ThemeMode.Dark);

                Assert.Equal(1, hubChangedCount);
                Assert.Equal(1, bridgeChangedCount);
            } finally {
                TokenBridge.Changed -= bridgeHandler;
                realHub.Changed -= hubHandler;
                ClearMovedCanvasToken();
                app.ApplyTheme(ThemeMode.System);
            }
        }

        [AvaloniaFact]
        public void HighContrastHub_DisposeInFinally_ClearsOverridesEvenWhenTheGuardedAssertionFails() {
            AppearanceHub? previousCurrent = AppearanceHub.Current;
            AppearanceHub? hub = null;
            try {
                try {
                    hub = new AppearanceHub((App)Application.Current!, new HighContrastAppearance());
                    AppearanceHub.Current = hub;
                    hub.Refresh();

                    Assert.Fail("forces the guarded block to fail, mirroring a broken assertion");
                } finally {
                    hub?.Dispose();
                }
            } catch (Exception) {
                //The throw is the point: the assertions after the try prove Dispose() ran from the finally.
            } finally {
                AppearanceHub.Current = previousCurrent;
            }

            Application.Current!.TryGetResource("Foreman.FocusRing.Width", Application.Current.ActualThemeVariant, out object? width);
            Assert.Equal(3.0, width);
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.Border"));
            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Opacity.SplitGripIdle"));
        }
    }
}
