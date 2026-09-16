using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Styling;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using SkiaSharp;
using System;
using System.Drawing;
using Xunit;
using AvColor = Avalonia.Media.Color;

namespace Foreman.Mac.UiTests.Canvas {
    //Ports MainForm.SetDarkMode/SetLightMode's canvas half (upstream MainForm.cs:36-46): the
    //ProductionGraphViewer's own BackColor swap, ported here as GraphViewer.BackgroundColor since this
    //control has no WinForms BackColor to inherit from.
    [Collection(ScaleTestGroup.Name)]
    public class GraphThemeTests {
        private const int Size = 100;

        private static SKColor SamplePixel(SKSurface surface, int x, int y) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(x, y);
        }

        //Phase 7 task 2 (perf-packaging-reference.md §1b): pins GraphViewer.cs:432/458's per-frame `new
        //SKPaint` call sites' exact pixel output so converting them to cached paints (a fixed static one for
        //PaintPausedBorder, a mutate-reset per-viewer one for the lasso's ViewScale-dependent stroke width)
        //can't drift either.

        [Fact]
        public void PaintPausedBorder_DrawsFixedRedStrokeAlongTheEdge() {
            var viewer = new GraphViewer(new Viewport(Size, Size), new GridManager());

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size));
            viewer.PaintPausedBorder(surface.Canvas);

            Assert.Equal(new SKColor(255, 80, 80), SamplePixel(surface, 1, Size / 2));
        }

        [Fact]
        public void Paint_ActiveSelectionZone_DrawsLassoStrokeInPlace() {
            var viewer = new GraphViewer(new Viewport(Size, Size), new GridManager());
            viewer.Viewport.ViewScale = 1f;
            viewer.SelectionZone = new Rectangle(10, 10, 50, 50);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size));
            viewer.Paint(surface.Canvas);

            Assert.Equal(new SKColor(100, 100, 200), SamplePixel(surface, Size / 2 + 10, Size / 2 + 35));
        }

        //Task 2: the clear color is now Foreman.Color.CanvasBackground (#1E1E20 dark / #FAFAFA light),
        //not the pre-token literal (23,23,23)/white - see task-2-report.md's light-value-equivalence table.
        //Every other test in the suite that touches Application.Current's theme resources runs under
        //[AvaloniaFact], which blocks until Avalonia.Headless's HeadlessUnitTestSession singleton has set
        //Application.Current up; these two ran under plain [Fact] instead, so whether Foreman.Color.
        //CanvasBackground resolved to the real token or ResolveBackgroundColor's fallback constant depended
        //on whether some other AvaloniaFact test happened to have already initialized the process-wide
        //Application first - a race that "moves" as the suite's parallel scheduling changes, not a leaked
        //RequestedThemeVariant (no test forces a variant and fails to restore it; the value read back here
        //is passed in explicitly, per call, to ResolveColor's variant parameter).
        [AvaloniaFact]
        public void ApplyTheme_Dark_PaintClearsToDarkCanvasBackgroundToken() {
            var viewer = new GraphViewer(new Viewport(Size, Size), new GridManager());
            viewer.ApplyTheme(dark: true);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size));
            viewer.Paint(surface.Canvas);

            Assert.Equal(new SKColor(0x1E, 0x1E, 0x20), SamplePixel(surface, 5, 5));
        }

        [AvaloniaFact]
        public void ApplyTheme_Light_PaintClearsToLightCanvasBackgroundToken() {
            var viewer = new GraphViewer(new Viewport(Size, Size), new GridManager());
            viewer.ApplyTheme(dark: true);
            viewer.ApplyTheme(dark: false);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size));
            viewer.Paint(surface.Canvas);

            Assert.Equal(new SKColor(0xFA, 0xFA, 0xFA), SamplePixel(surface, 5, 5));
        }

        //TabBorder/ChipBorder/ErrorBorder carry the painters' three former literal hexes as tokens -
        //assertions below read the live resource dictionary and pin the exact contract hex, so a drifted
        //token file fails here instead of silently keeping the old fallback.
        private static SKColor ResolveCanvasBorderToken(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            var color = (AvColor)value!;
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        private static SKColor ResolveCanvasBorderToken(string key, ThemeVariant variant) {
            Application.Current!.TryGetResource(key, variant, out object? value);
            var color = (AvColor)value!;
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        private static void AssertBridgeMatchesTokens() {
            Assert.Equal(ResolveCanvasBorderToken("Foreman.Color.CanvasTabBorder"), TokenBridge.Current.TabBorder);
            Assert.Equal(ResolveCanvasBorderToken("Foreman.Color.CanvasChipBorder"), TokenBridge.Current.ChipBorder);
            Assert.Equal(ResolveCanvasBorderToken("Foreman.Color.CanvasErrorBorder"), TokenBridge.Current.ErrorBorder);
        }

        //Contract hexes, hardcoded here rather than read back from the dictionary - AssertBridgeMatchesTokens
        //already proves the bridge tracks the dictionary; this proves the dictionary itself still holds
        //the spec's exact values.
        private static readonly SKColor ContractTabBorder = new(0x69, 0x69, 0x69);
        private static readonly SKColor ContractChipBorder = new(0xB8, 0x86, 0x0B);
        private static readonly SKColor ContractErrorBorder = new(0x8B, 0x00, 0x00);

        private static void AssertTokensMatchContractLiterals() {
            Assert.Equal(ContractTabBorder, TokenBridge.Current.TabBorder);
            Assert.Equal(ContractChipBorder, TokenBridge.Current.ChipBorder);
            Assert.Equal(ContractErrorBorder, TokenBridge.Current.ErrorBorder);
        }

        private const string MovedTokenKey = "Foreman.Color.CanvasTabBorder";
        private static readonly AvColor MovedTokenValue = AvColor.FromRgb(0x01, 0x02, 0x03);

        //Both dictionaries carry the same canvas border hexes, so no appearance change moves one on its
        //own; staging an override the bridge has not resolved yet puts it in the state one that does would.
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
        public void CanvasBorderTokens_LightAndDarkDictionaries_MatchContractLiterals() {
            Assert.Equal(ContractTabBorder, ResolveCanvasBorderToken("Foreman.Color.CanvasTabBorder", ThemeVariant.Light));
            Assert.Equal(ContractChipBorder, ResolveCanvasBorderToken("Foreman.Color.CanvasChipBorder", ThemeVariant.Light));
            Assert.Equal(ContractErrorBorder, ResolveCanvasBorderToken("Foreman.Color.CanvasErrorBorder", ThemeVariant.Light));

            Assert.Equal(ContractTabBorder, ResolveCanvasBorderToken("Foreman.Color.CanvasTabBorder", ThemeVariant.Dark));
            Assert.Equal(ContractChipBorder, ResolveCanvasBorderToken("Foreman.Color.CanvasChipBorder", ThemeVariant.Dark));
            Assert.Equal(ContractErrorBorder, ResolveCanvasBorderToken("Foreman.Color.CanvasErrorBorder", ThemeVariant.Dark));
        }

        [AvaloniaFact]
        public void TokenBridge_AtDefaultScale_CanvasBorderColorsMatchTheirTokens() {
            TokenBridge.Refresh();

            AssertBridgeMatchesTokens();
            AssertTokensMatchContractLiterals();
        }

        [AvaloniaFact]
        public void TokenBridge_AtOneFifty_CanvasBorderColorsStayPinnedToTheirTokens() {
            try {
                WindowScaler.SetScale(1.5);

                AssertBridgeMatchesTokens();
                AssertTokensMatchContractLiterals();
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        //App wires WindowScaler.Rescaled to AppearanceHub.Refresh() only; Hub.Changed is TokenBridge's one
        //trigger, so a single SetScale must resolve the bridge exactly once, not once per hook.
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

        //Bare fake for AppearanceHub's own seam, matching DarkModeTests.FakeAppearance - only
        //ContrastPreference matters here, the rest is inert plumbing the hub still needs to construct.
        private sealed class HighContrastAppearance : IPlatformAppearance {
            private EventHandler? changed;
            public AvColor? AccentColor => null;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.High;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        //Raisable fake for AppearanceHub's own seam - PlatformAppearance's half of the shared OS event.
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

        //A System-mode OS colour event raises the platform appearance leg, the app's only hook onto it.
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

        //A System-mode OS flip raises the platform appearance event and ActualThemeVariantChanged, so the
        //hub refreshes on both legs; the bridge resolves the same values the second time and stays quiet.
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

        //An explicit theme change reaches the app's own hub through ActualThemeVariantChanged once and
        //nothing else - the companion case to the OS-event collapse above, on wiring no test replaced.
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
        public void TokenBridge_UnderIncreaseContrast_CanvasBorderColorsStayPinnedToTheirTokens() {
            AppearanceHub? previousCurrent = AppearanceHub.Current;
            AppearanceHub? hub = null;
            try {
                hub = new AppearanceHub((App)Application.Current!, new HighContrastAppearance());
                AppearanceHub.Current = hub;
                hub.Refresh();
                TokenBridge.Refresh();

                AssertBridgeMatchesTokens();
                AssertTokensMatchContractLiterals();
            } finally {
                hub?.Dispose();
                AppearanceHub.Current = previousCurrent;
            }
        }

        //AppearanceHub.Dispose() clears Increase Contrast's resource overrides - FocusRing.Width back at
        //3.0, Border and SplitGripIdle absent - even when the guarded block throws. Moving hub?.Dispose()
        //out of this try's finally (so it never runs on the throw below) fails every assertion here.
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
                // expected: proves Dispose() still ran even though the guarded block threw
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
