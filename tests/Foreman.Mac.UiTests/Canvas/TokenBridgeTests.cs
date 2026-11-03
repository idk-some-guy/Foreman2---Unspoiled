using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Styling;
using Foreman.Mac;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using Xunit;
using AvColor = Avalonia.Media.Color;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public class TokenBridgeTests {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        private static SKColor Sk(string hex) {
            AvColor color = AvColor.Parse(hex);
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        private static SKColor ResolveSk(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            var color = (AvColor)value!;
            return new SKColor(color.R, color.G, color.B, color.A);
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

        private sealed class NoAccentAppearance : IPlatformAppearance {
            private EventHandler? changed;
            public AvColor? AccentColor => null;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.NoPreference;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;
            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        [AvaloniaFact]
        public void AtOneFifty_NodeTabHeightIs33_AndNodeTabRadiusStays6() {
            App app = (App)Application.Current!;
            try {
                app.ApplyTheme(ThemeMode.Light);
                WindowScaler.SetScale(1.5);

                Assert.Equal(1.5, TokenBridge.Current.Scale);
                Assert.Equal(33f, TokenBridge.Current.NodeTabHeight);
                Assert.Equal(6f, TokenBridge.Current.NodeTabRadius);
                Assert.Equal(5f, TokenBridge.Current.FindRingDashOn);
                Assert.Equal(3f, TokenBridge.Current.FindRingDashOff);
                Assert.Equal(600, TokenBridge.Current.CanvasEmphasizedWeight);
                Assert.Equal(10f, TokenBridge.Current.NodeRadius);
                Assert.Equal(2f, TokenBridge.Current.CardShadowOffsetY);
                Assert.Equal(8f, TokenBridge.Current.CardShadowBlur);
                Assert.Equal(Sk("#12000000"), TokenBridge.Current.CardShadowColor);
                Assert.Equal(15f, TokenBridge.Current.Glyph10);
                Assert.Equal(18f, TokenBridge.Current.GlyphMicro);
                Assert.Equal(4.5f, TokenBridge.Current.NodeGapTight);
                Assert.Equal(2.1f, TokenBridge.Current.NodeBadgeStroke, 3);
                Assert.Equal((3f, 6f, 9f, 12f, 18f), (TokenBridge.Current.Space05, TokenBridge.Current.Space1, TokenBridge.Current.Space15, TokenBridge.Current.Space2, TokenBridge.Current.Space3));
                Assert.Equal(100f, TokenBridge.Current.PillRadius);
                Assert.Equal(3f, TokenBridge.Current.NodeTypeStripRadius);
                Assert.Equal(4f, TokenBridge.Current.NodeBeaconBadgeRadius);
            } finally {
                WindowScaler.SetScale(1.0);
                app.ApplyTheme(ThemeMode.System);
            }
        }

        [AvaloniaFact]
        public void ThemeFlip_RepublishesOnce_WithTheOtherAppearancesValues() {
            App app = (App)Application.Current!;
            app.InstallAppearance(new NoAccentAppearance());
            app.ApplyTheme(ThemeMode.Light);
            TokenBridge.Refresh();
            CanvasTokens light = TokenBridge.Current;
            SKColor lightAccentText = ResolveSk("Foreman.Color.AccentText");

            int changedCount = 0;
            EventHandler handler = (_, _) => changedCount++;
            TokenBridge.Changed += handler;
            try {
                app.ApplyTheme(ThemeMode.Dark);

                Assert.Equal(1, changedCount);
                CanvasTokens dark = TokenBridge.Current;
                Assert.Equal(Sk("#1D8878"), light.NodeRecipe);
                Assert.Equal(Sk("#45C4B0"), dark.NodeRecipe);
                Assert.Equal(Sk("#C86A00"), light.NodeSelected);
                Assert.Equal(Sk("#FF9F0A"), dark.NodeSelected);
                Assert.Equal(Sk("#73000000"), light.NodeTabBorder);
                Assert.Equal(Sk("#80FFFFFF"), dark.NodeTabBorder);
                Assert.Equal(0.62f, light.NodeUnavailableOpacity);
                Assert.Equal(0.62f, dark.NodeUnavailableOpacity);
                Assert.Equal(12f, light.LinkStub);
                Assert.Equal(light.LinkStub, dark.LinkStub);
                Assert.Equal(Sk("#FFFFFF"), light.TextOnWarning);
                Assert.Equal(Sk("#1A1A1A"), dark.TextOnWarning);
                Assert.Equal(light.NodeTabHeight, dark.NodeTabHeight);
                Assert.Equal(Sk("#0068DA"), lightAccentText);
                Assert.Equal(lightAccentText, light.AccentText);
                Assert.Equal(ResolveSk("Foreman.Color.AccentText"), dark.AccentText);
                Assert.Equal(1.0, light.Scale);
                Assert.Equal((100f, 100f), (light.PillRadius, dark.PillRadius));
            } finally {
                TokenBridge.Changed -= handler;
                app.InstallAppearance(null);
                app.ApplyTheme(ThemeMode.System);
                TokenBridge.Refresh();
            }
        }

        [AvaloniaFact]
        public void IncreaseContrast_NodeTabBorderTakesTheContrastValue() {
            AppearanceHub? previousCurrent = AppearanceHub.Current;
            AppearanceHub? hub = null;
            try {
                hub = new AppearanceHub((App)Application.Current!, new HighContrastAppearance());
                AppearanceHub.Current = hub;
                hub.Refresh();
                TokenBridge.Refresh();

                Assert.True(TokenBridge.Current.IncreaseContrast);
                Assert.Equal(ResolveSk("Foreman.Color.NodeTabBorderContrast"), TokenBridge.Current.NodeTabBorder);
            } finally {
                hub?.Dispose();
                AppearanceHub.Current = previousCurrent;
                TokenBridge.Refresh();
            }

            Assert.False(TokenBridge.Current.IncreaseContrast);
            Assert.Equal(ResolveSk("Foreman.Color.NodeTabBorder"), TokenBridge.Current.NodeTabBorder);
        }

        [Fact]
        public void NoPainter_ReadsARetiredCanvasKey() {
            string[] retired = ["CanvasTabBorder", "CanvasChipBorder", "CanvasErrorBorder"];
            string canvasDirectory = Path.Combine(RepoRoot, "src", "Foreman.Mac", "Canvas");

            string[] offenders = [.. Directory.EnumerateFiles(canvasDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(file => retired.Any(File.ReadAllText(file).Contains))
                .Select(file => Path.GetRelativePath(RepoRoot, file))];

            Assert.Empty(offenders);
        }
    }
}
