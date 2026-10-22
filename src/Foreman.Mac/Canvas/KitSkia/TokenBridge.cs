using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using SkiaSharp;
using System;
using System.Globalization;
using AvColor = Avalonia.Media.Color;

namespace Foreman.Mac.Canvas.KitSkia {
    public sealed record CanvasTokens(
        double Scale, bool IncreaseContrast,
        SKColor NodeRecipe, SKColor NodeSupplier, SKColor NodeConsumer, SKColor NodePassthrough,
        SKColor NodeTabBorder, SKColor NodeSelected, SKColor FindMatchRing, SKColor FocusRing,
        SKColor Accent, SKColor AccentSoft, SKColor Warning, SKColor Error, SKColor TextOnAccent, SKColor TextOnWarning,
        SKColor Text, SKColor TextSecondary, SKColor ControlBackground, SKColor ControlBackgroundHover,
        SKColor WellBackground, SKColor BorderStrong, SKColor Border, SKColor CanvasBackground, SKColor PopoverMaterial,
        SKColor CardShadowColor, float CardShadowOffsetY, float CardShadowBlur,
        float NodeUnavailableOpacity, float CanvasFontSize, float CalloutFontSize, float EmphasizedBodyFontSize, int CanvasEmphasizedWeight,
        float NodeIcon, float NodeBuildingIcon, float NodeTabIcon,
        float NodeInsetTop, float NodeInsetSide, float NodeInsetBottom, float NodeTypeStrip, float NodeTypeStripInset,
        float NodeTabHeight, float NodeTabMinWidth, float NodeTabOverhang, float NodeBadgeHeight, float NodePip, float NodeGapTight,
        float LinkWidth, float LinkWidthMax, float LinkHitWidth, float WaypointDiameter, float WaypointHitTarget,
        float NodeRadius, float NodeTabRadius, float LinkCornerRadius, float CanvasMinTextSize, float GridMinScreenSpacing,
        float FindRingDashOn, float FindRingDashOff, SKColor AccentText, float PillRadius,
        float Glyph10, float GlyphMicro, float Space05, float Space1, float Space15, float Space2, float Space3, float Space5,
        float NodeTypeStripRadius, float NodeBeaconBadgeRadius, float NodeBadgeStroke, float FocusRingWidth);

    public static class TokenBridge {
        private static CanvasTokens? current;

        //An empty record from before the tokens load is never kept, so the first real resolve raises Changed.
        public static CanvasTokens Current {
            get {
                if (current is null)
                    Refresh();
                return current ?? Resolve();
            }
        }

        public static event EventHandler? Changed;

        //AppearanceHub.Changed is the only caller. One OS flip refreshes the hub twice (platform appearance and
        //ActualThemeVariantChanged), so the record comparison keeps that to a single republish.
        public static void Refresh() {
            if (Resource("Foreman.Color.CanvasBackground") is null)
                return;

            CanvasTokens tokens = Resolve();
            if (tokens == current)
                return;

            current = tokens;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static CanvasTokens Resolve() {
            BoxShadow cardShadow = Resource("Foreman.Shadow.Card") is BoxShadows { Count: > 0 } shadows ? shadows[0] : default;
            (float dashOn, float dashOff) = ParseDash(Resource("Foreman.Metrics.FindRingDash") as string);

            double scale = WindowScaler.CurrentScale;
            return new CanvasTokens(
                scale, AppearanceHub.Current?.IncreaseContrast ?? false,
                ColorOf("Foreman.Color.NodeRecipe"), ColorOf("Foreman.Color.NodeSupplier"), ColorOf("Foreman.Color.NodeConsumer"), ColorOf("Foreman.Color.NodePassthrough"),
                ColorOf("Foreman.Color.NodeTabBorder"), ColorOf("Foreman.Color.NodeSelected"), ColorOf("Foreman.Color.FindMatchRing"), ColorOf("Foreman.Color.FocusRing"),
                ColorOf("Foreman.Color.Accent"), ColorOf("Foreman.Color.AccentSoft"), ColorOf("Foreman.Color.Warning"), ColorOf("Foreman.Color.Error"),
                ColorOf("Foreman.Color.TextOnAccent"), ColorOf("Foreman.Color.TextOnWarning"),
                ColorOf("Foreman.Color.Text"), ColorOf("Foreman.Color.TextSecondary"), ColorOf("Foreman.Color.ControlBackground"), ColorOf("Foreman.Color.ControlBackgroundHover"),
                ColorOf("Foreman.Color.WellBackground"), ColorOf("Foreman.Color.BorderStrong"), ColorOf("Foreman.Color.Border"), ColorOf("Foreman.Color.CanvasBackground"),
                ColorOf("Foreman.Color.PopoverMaterial"),
                ToSk(cardShadow.Color), (float)cardShadow.OffsetY, (float)cardShadow.Blur,
                Number("Foreman.Opacity.NodeUnavailable"), Number("Foreman.Type.Canvas.FontSize"), Number("Foreman.Type.Callout.FontSize"),
                Number("Foreman.Type.EmphasizedBody.FontSize"), Resource("Foreman.Type.Canvas.Emphasized.FontWeight") is FontWeight weight ? (int)weight : 0,
                Number("Foreman.Size.NodeIcon"), Number("Foreman.Size.NodeBuildingIcon"), Number("Foreman.Size.NodeTabIcon"),
                Number("Foreman.Size.NodeInsetTop"), Number("Foreman.Size.NodeInsetSide"), Number("Foreman.Size.NodeInsetBottom"),
                Number("Foreman.Size.NodeTypeStrip"), Number("Foreman.Size.NodeTypeStripInset"),
                Number("Foreman.Size.NodeTabHeight"), Number("Foreman.Size.NodeTabMinWidth"), Number("Foreman.Size.NodeTabOverhang"),
                Number("Foreman.Size.NodeBadgeHeight"), Number("Foreman.Size.NodePip"), Number("Foreman.Size.NodeGapTight"),
                Number("Foreman.Size.LinkWidth"), Number("Foreman.Size.LinkWidthMax"), Number("Foreman.Size.LinkHitWidth"),
                Number("Foreman.Size.WaypointDiameter"), Number("Foreman.Size.WaypointHitTarget"),
                Radius("Foreman.Radius.Node"), Radius("Foreman.Radius.NodeTab"), Radius("Foreman.Radius.LinkCorner"),
                Number("Foreman.Metrics.CanvasMinTextSize"), Number("Foreman.Metrics.GridMinScreenSpacing"),
                dashOn, dashOff, ColorOf("Foreman.Color.AccentText"), Radius("Foreman.Radius.Pill"),
                Number("Foreman.Size.Glyph10"), Number("Foreman.Size.GlyphMicro"),
                Space("Foreman.Space.Space05", scale), Space("Foreman.Space.Space1", scale), Space("Foreman.Space.Space15", scale), Space("Foreman.Space.Space2", scale),
                Space("Foreman.Space.Space3", scale), Space("Foreman.Space.Space5", scale),
                Radius("Foreman.Radius.NodeTypeStrip"), Radius("Foreman.Radius.NodeBeaconBadge"),
                Number("Foreman.Size.NodeBadgeStroke"), Number("Foreman.FocusRing.Width"));
        }

        //Off the UI thread ActualThemeVariant throws, so we resolve against Light there, as ThemeResources does.
        private static object? Resource(string key) {
            Application? app = Application.Current;
            if (app is null)
                return null;
            ThemeVariant variant = Dispatcher.UIThread.CheckAccess() ? app.ActualThemeVariant : ThemeVariant.Light;
            return app.TryGetResource(key, variant, out object? value) ? value : null;
        }

        private static SKColor ColorOf(string key) => ToSk(ThemeResources.ResolveColor(key, default));

        private static SKColor ToSk(AvColor color) => new(color.R, color.G, color.B, color.A);

        private static float Number(string key) => Resource(key) is double value ? (float)value : 0f;

        //The Space ladder stays unscaled for Thickness bindings, so the canvas multiplies it here.
        private static float Space(string key, double scale) => (float)(Number(key) * scale);

        private static float Radius(string key) => Resource(key) is CornerRadius radius ? (float)radius.TopLeft : 0f;

        private static (float On, float Off) ParseDash(string? dash) {
            string[] parts = dash?.Split(',') ?? [];
            return parts.Length == 2
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float on)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float off)
                ? (on, off)
                : (0f, 0f);
        }

        public static float Scaled(float baseValue) => (float)(baseValue * Current.Scale);

        public static int ScaledInt(int baseValue) => (int)Math.Round(baseValue * Current.Scale);
    }
}
