using Foreman.Mac.Services;
using SkiaSharp;
using System;
using AvColor = Avalonia.Media.Color;

namespace Foreman.Mac.Canvas.KitSkia {
    //Canvas token snapshot: metrics plus the three former literal border hexes (#696969/#B8860B/#8B0000)
    //as tokens; the rest of the painters' colours migrate later, when K5 owns Canvas/Elements/* itself.
    public sealed record CanvasTokens(double Scale, double NodePadding, double TypeFontSize, SKColor TabBorder, SKColor ChipBorder, SKColor ErrorBorder);

    public static class TokenBridge {
        private const double BaseNodePadding = 8.0;
        private const double BaseTypeFontSize = 10.0;

        private static readonly SKColor FallbackTabBorder = new(0x69, 0x69, 0x69);
        private static readonly SKColor FallbackChipBorder = new(0xB8, 0x86, 0x0B);
        private static readonly SKColor FallbackErrorBorder = new(0x8B, 0x00, 0x00);

        public static CanvasTokens Current { get; private set; } =
            new(1.0, BaseNodePadding, BaseTypeFontSize, FallbackTabBorder, FallbackChipBorder, FallbackErrorBorder);

        //Painters subscribe to re-request a redraw; GraphCanvasControl does.
        public static event EventHandler? Changed;

        //AppearanceHub.Changed is the only caller; painters read Current instead of resolving
        //Application.Current themselves. One OS flip refreshes the hub twice (platform appearance and
        //ActualThemeVariantChanged), so the record comparison below keeps that to a single republish.
        public static void Refresh() {
            double scale = WindowScaler.CurrentScale;
            CanvasTokens tokens = new(
                scale,
                BaseNodePadding * scale,
                BaseTypeFontSize * scale,
                ResolveBorderColor("Foreman.Color.CanvasTabBorder", FallbackTabBorder),
                ResolveBorderColor("Foreman.Color.CanvasChipBorder", FallbackChipBorder),
                ResolveBorderColor("Foreman.Color.CanvasErrorBorder", FallbackErrorBorder));
            if (tokens == Current)
                return;

            Current = tokens;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static SKColor ResolveBorderColor(string key, SKColor fallback) {
            AvColor avFallback = new(fallback.Alpha, fallback.Red, fallback.Green, fallback.Blue);
            AvColor color = ThemeResources.ResolveColor(key, avFallback);
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        public static float Scaled(float baseValue) => (float)(baseValue * Current.Scale);

        public static int ScaledInt(int baseValue) => (int)Math.Round(baseValue * Current.Scale);
    }
}
