using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Concurrent;

namespace Foreman.Mac.Canvas.Elements {
    public readonly record struct NodeCardGeometry(SKRect Rect, float Radius, SKColor Fill, float StrokeWidth, SKColor Stroke, bool HasShadow);
    public readonly record struct TabGeometry(SKRect Rect, float Radius, float StrokeWidth, SKColor Stroke, SKColor Fill, float OverhangOutside);

    internal enum NodeTextRole { KeyTitle, Name, Count, Detail, Chip, ExtraProductivity, TabRate }
    internal readonly record struct NodeText(NodeTextRole Role, string Text, SKRect Rect, float Size, int Weight, SKColor Color);

    internal enum StatusItemKind { Beacon, Pip, ExtraProductivity }
    internal readonly record struct StatusItem(StatusItemKind Kind, SKRect Rect);

    internal readonly record struct LabelState(string Text, string? From, double Progress);

    //Count and rate text cross-fade on the canvas's shared label clock; a change that lands under Reduce Motion swaps.
    internal sealed class LabelFade {
        private int generation;

        public string Text { get; private set; } = "";
        private string? from;

        public void Set(string text, NodeElementContext context) {
            if (text == Text)
                return;
            from = Motion.ReduceMotion || Text.Length == 0 ? null : Text;
            Text = text;
            generation = context.LabelFadeGeneration;
        }

        public LabelState State(NodeElementContext context) {
            if (from is not null && (generation != context.LabelFadeGeneration || context.LabelFadeProgress >= 1))
                from = null;
            return from is null ? new LabelState(Text, null, 1) : new LabelState(Text, from, context.LabelFadeProgress);
        }
    }

    //Draws the node card family (§01) from CanvasTokens; sizes arrive already multiplied, radii and strokes stay fixed.
    internal static class NodeCardPainter {
        //§01 metrics with no §12 key or Space rung, in points at 100 %.
        internal const float KeyLineHeight = 12, BuildingRowHeight = 20, SubRowHeight = 16;
        internal const float CardStrokeWidth = 1, TabStrokeWidth = 1.5f;
        private const float GlyphStrokeWidth = 1.5f, BadgeGlyphStrokeWidth = 1.4f, GlyphGrid = 20;

        private static readonly ConcurrentDictionary<int, SKTypeface> Typefaces = new();
        private static readonly string[] RateSuffixes = ["/s", "/min", "/5\u00A0min", "/10\u00A0min", "/30\u00A0min", "/h"];
        private static readonly string[] ScaleLetters = ["", "k", "M", "G"];
        private static volatile ShadowFilter? shadowFilter;

        private sealed record ShadowFilter(float Blur, SKMaskFilter Filter);

        //The live canvas paints on the render thread and image export on the UI thread, so scratch objects are per thread.
        [ThreadStatic] private static SKFont? threadFont;
        [ThreadStatic] private static SKPaint? threadTextPaint;
        [ThreadStatic] private static SKPaint? threadFillPaint;
        [ThreadStatic] private static SKPaint? threadStrokePaint;
        [ThreadStatic] private static SKPaint? threadShadowPaint;
        [ThreadStatic] private static SKRoundRect? threadRoundRect;
        [ThreadStatic] private static SKPoint[]? threadStripRadii;

        internal static float DetailRowTop(CanvasTokens tokens) => tokens.NodeInsetTop + tokens.NodeIcon + tokens.Space2;

        internal static float StatusRowTop(CanvasTokens tokens) => DetailRowTop(tokens) + tokens.NodeBuildingIcon + tokens.Space3;

        internal static SKTypeface Typeface(int weight) =>
            Typefaces.GetOrAdd(weight, w => SKTypeface.FromFamilyName(null, new SKFontStyle(w, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)) ?? SKTypeface.Default);

        private static SKFont Font(float size, int weight) {
            SKFont font = threadFont ??= new SKFont { Subpixel = true };
            font.Typeface = Typeface(weight);
            font.Size = size;
            return font;
        }

        private static SKPaint FillPaint(SKColor color) {
            SKPaint paint = threadFillPaint ??= new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            paint.Color = color;
            return paint;
        }

        private static SKPaint StrokePaint(SKColor color, float width) {
            SKPaint paint = threadStrokePaint ??= new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            paint.Color = color;
            paint.StrokeWidth = width;
            return paint;
        }

        internal static float Measure(string text, float size, int weight) => Font(size, weight).MeasureText(text);

        //One line with a trailing ellipsis, as CharacterEllipsis trims.
        internal static string Fit(string text, float maxWidth, float size, int weight, out bool truncated) {
            SKFont font = Font(size, weight);
            truncated = font.MeasureText(text) > maxWidth;
            if (!truncated)
                return text;
            int length = text.Length;
            while (length > 0 && font.MeasureText(string.Concat(text.AsSpan(0, length), "…")) > maxWidth)
                length--;
            return string.Concat(text.AsSpan(0, length).TrimEnd(), "…");
        }

        //The canvas number: up to 3 decimals below 1, 2 below 10, 1 below 100, whole to 9999, then k/M/G on the same rule.
        internal static string Number(double value) {
            double magnitude = Math.Abs(value);
            if (magnitude == 0)
                return "0";
            if (magnitude < 0.001)
                return "<" + 0.001.ToString("0.###", DisplayCulture.Format);
            int letter = 0;
            double mantissa = RoundForCanvas(magnitude);
            while (mantissa >= 10000 || (letter > 0 && mantissa >= 1000)) {
                if (letter == ScaleLetters.Length - 1)
                    break;
                magnitude /= 1000;
                letter++;
                mantissa = RoundForCanvas(magnitude);
            }
            return (value < 0 ? "-" : "") + mantissa.ToString("0.###", DisplayCulture.Format) + ScaleLetters[letter];
        }

        private static double RoundForCanvas(double magnitude) =>
            Math.Round(magnitude, magnitude < 1 ? 3 : magnitude < 10 ? 2 : magnitude < 100 ? 1 : 0, MidpointRounding.AwayFromZero);

        internal static string Rate(double value, ProductionGraph.RateUnit unit) => Number(value) + RateSuffixes[(int)unit];

        internal static string Count(double quantity, bool round) {
            double shown = round ? Math.Ceiling(quantity) : Math.Round(quantity, 2, MidpointRounding.AwayFromZero);
            return shown >= 10000 ? Number(shown) : shown.ToString(round ? "0" : "0.00", DisplayCulture.Format);
        }

        internal static void DrawCard(SKCanvas canvas, NodeCardGeometry card, CanvasTokens tokens) {
            if (card.HasShadow) {
                SKPaint shadow = threadShadowPaint ??= new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
                shadow.Color = tokens.CardShadowColor;
                shadow.MaskFilter = ShadowMask(tokens.CardShadowBlur);
                SKRect offset = card.Rect;
                offset.Offset(0, tokens.CardShadowOffsetY);
                canvas.DrawRoundRect(offset, card.Radius, card.Radius, shadow);
            }
            canvas.DrawRoundRect(card.Rect, card.Radius, card.Radius, FillPaint(card.Fill));
            StrokeInside(canvas, card.Rect, card.Radius, card.StrokeWidth, card.Stroke);
        }

        private static SKMaskFilter ShadowMask(float blur) {
            ShadowFilter? cached = shadowFilter;
            if (cached is not null && cached.Blur == blur)
                return cached.Filter;
            var created = new ShadowFilter(blur, SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur / 2));
            shadowFilter = created;
            return created.Filter;
        }

        //A card or tab border sits inside its bounds, as a CSS border-box does.
        internal static void StrokeInside(SKCanvas canvas, SKRect rect, float radius, float width, SKColor color) {
            SKRect inner = SKRect.Inflate(rect, -width / 2, -width / 2);
            float innerRadius = Math.Max(0, radius - (width / 2));
            canvas.DrawRoundRect(inner, innerRadius, innerRadius, StrokePaint(color, width));
        }

        internal static void DrawStrip(SKCanvas canvas, SKRect strip, SKColor color, float radius) {
            if (strip.IsEmpty)
                return;
            SKPoint[] radii = threadStripRadii ??= new SKPoint[4];
            radii[1] = radii[2] = new SKPoint(radius, radius);
            SKRoundRect roundRect = threadRoundRect ??= new SKRoundRect();
            roundRect.SetRectRadii(strip, radii);
            canvas.DrawRoundRect(roundRect, FillPaint(color));
        }

        internal static void DrawChip(SKCanvas canvas, SKRect chip, CanvasTokens tokens) {
            float radius = Math.Min(tokens.PillRadius, chip.Height / 2);
            canvas.DrawRoundRect(chip, radius, radius, FillPaint(tokens.AccentSoft));
        }

        internal static void DrawRoundedBox(SKCanvas canvas, SKRect box, float radius, SKColor fill, float strokeWidth, SKColor stroke) {
            canvas.DrawRoundRect(box, radius, radius, FillPaint(fill));
            StrokeInside(canvas, box, radius, strokeWidth, stroke);
        }

        internal static void DrawPip(SKCanvas canvas, SKRect pip, SKColor color) => canvas.DrawOval(pip, FillPaint(color));

        //Glyph geometry scales with its box; the stroke keeps the kit's fixed width at every size.
        internal static void DrawGlyph(SKCanvas canvas, string key, SKRect box, SKColor color, bool badge = false) {
            float scale = box.Width / GlyphGrid;
            float stroke = badge ? BadgeGlyphStrokeWidth : GlyphStrokeWidth;
            canvas.Save();
            canvas.Translate(box.Left, box.Top);
            canvas.Scale(scale);
            canvas.DrawPath(GlyphPaths.Get(key), StrokePaint(color, stroke / scale));
            canvas.Restore();
        }

        internal static void FillGlyph(SKCanvas canvas, string key, SKRect box, SKColor color) {
            float scale = box.Width / GlyphGrid;
            canvas.Save();
            canvas.Translate(box.Left, box.Top);
            canvas.Scale(scale);
            canvas.DrawPath(GlyphPaths.Get(key), FillPaint(color));
            canvas.Restore();
        }

        internal static void DrawIcon(SKCanvas canvas, SKBitmap? icon, SKRect rect) {
            if (icon is not null)
                canvas.DrawBitmap(icon, rect);
        }

        internal static void DrawText(SKCanvas canvas, in NodeText text, float dx, float dy, string? content = null, float opacity = 1) {
            SKFont font = Font(text.Size, text.Weight);
            SKPaint paint = threadTextPaint ??= new SKPaint { IsAntialias = true };
            paint.Color = opacity >= 1 ? text.Color : text.Color.WithAlpha((byte)Math.Round(text.Color.Alpha * Math.Clamp(opacity, 0, 1)));
            SKFontMetrics metrics = font.Metrics;
            float baseline = text.Rect.MidY + dy - ((metrics.Ascent + metrics.Descent) / 2);
            canvas.DrawText(content ?? text.Text, text.Rect.Left + dx, baseline, SKTextAlign.Left, font, paint);
        }

        internal static void DrawLabel(SKCanvas canvas, in NodeText text, float dx, float dy, LabelState state) {
            if (state.From is null) {
                DrawText(canvas, text, dx, dy);
                return;
            }
            DrawText(canvas, text, dx, dy, state.From, (float)(1 - state.Progress));
            DrawText(canvas, text, dx, dy, state.Text, (float)state.Progress);
        }
    }
}
