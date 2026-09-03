using Foreman;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas {
    //Ports Controls/GraphicsStuff.cs: rounded-rect fill/stroke (GraphicsPath's 4-arc construction collapses
    //to SKCanvas.DrawRoundRect), the shrink-to-fit DrawText loop, and the number-formatting helpers.
    //BuildingQuantityToText moved here from Elements/GraphElement.cs (its only home upstream) since it's a
    //plain formatting helper with no element-tree dependency, and this task doesn't port GraphElement's
    //subclasses that call it yet.
    public enum TextHorizontalAlign { Near, Center }
    public enum TextVerticalAlign { Near, Center, Far }

    public static class GraphicsStuff {
        private const float MinFontSize = 1f;

        //DrawText/DrawStringAtPoint run on two real threads - the Avalonia compositor's render thread for
        //the live canvas and the UI thread for ImageExportWindow's direct viewer.Paint() call - so a shared
        //static paint/font would reintroduce the cross-thread mutation hazard the phase-7 Task 2 review
        //flagged in GridManager. [ThreadStatic] gives each thread its own instance with no locking, and
        //every call fully overwrites color/typeface/size before drawing, so reuse never leaks a stale value
        //across calls on the same thread.
        [ThreadStatic]
        private static SKPaint? threadPaint;
        [ThreadStatic]
        private static SKFont? threadFont;

        private static SKPaint RentPaint(SKColor color) {
            SKPaint paint = threadPaint ??= new SKPaint { IsAntialias = true };
            paint.Color = color;
            return paint;
        }

        private static SKFont RentFont(SKTypeface typeface, float size) {
            SKFont font = threadFont ??= new SKFont();
            font.Typeface = typeface;
            font.Size = size;
            return font;
        }

        //Same per-thread scratch pattern as RentPaint/RentFont above, exposed for GraphElement.Draw()
        //implementations that used to hold their own mutate-reset SKPaint/SKPath as per-instance fields
        //(final-review C1): those fields raced whenever the same element instance painted on two threads
        //at once - the compositor's render thread for the live canvas and the UI thread for
        //ImageExportWindow's direct Paint() call both walk the same shared GraphViewer's element tree.
        //Separate ThreadStatic slots from threadPaint/threadFont above so a Draw() that also calls
        //DrawText/DrawStringAtPoint doesn't have the two uses stomp on each other mid-call.
        [ThreadStatic]
        private static SKPaint? threadStrokePaint;
        [ThreadStatic]
        private static SKPaint? threadFillPaint;
        [ThreadStatic]
        private static SKPath? threadPath;

        public static SKPaint RentStrokePaint(SKColor color, float strokeWidth, SKStrokeCap cap = SKStrokeCap.Butt) {
            SKPaint paint = threadStrokePaint ??= new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
            paint.Color = color;
            paint.StrokeWidth = strokeWidth;
            paint.StrokeCap = cap;
            return paint;
        }

        public static SKPaint RentFillPaint(SKColor color) {
            SKPaint paint = threadFillPaint ??= new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            paint.Color = color;
            return paint;
        }

        public static SKPath RentPath() {
            SKPath path = threadPath ??= new SKPath();
            path.Reset();
            return path;
        }

        public static int DrawText(SKCanvas canvas, SKColor textColor, SKTypeface typeface, float baseFontSize, Rectangle textbox, string text,
            TextHorizontalAlign horizontalAlign = TextHorizontalAlign.Center, TextVerticalAlign verticalAlign = TextVerticalAlign.Center, bool singleLine = false) {
            SKPaint paint = RentPaint(textColor);
            SKFont font = RentFont(typeface, baseFontSize);

            if (singleLine) {
                float width = font.MeasureText(text);
                while (width > textbox.Width && font.Size > MinFontSize) {
                    font.Size -= 0.5f;
                    width = font.MeasureText(text);
                }

                DrawSingleLine(canvas, text, font, paint, textbox, horizontalAlign, verticalAlign);
                return (int)width;
            }

            List<string> lines = WrapLines(text, font, textbox.Width);
            float height = LinesHeight(font, lines.Count);
            while (height > textbox.Height && font.Size > MinFontSize) {
                font.Size -= 0.5f;
                lines = WrapLines(text, font, textbox.Width);
                height = LinesHeight(font, lines.Count);
            }

            float maxWidth = 0;
            foreach (string line in lines)
                maxWidth = Math.Max(maxWidth, font.MeasureText(line));

            DrawLines(canvas, lines, font, paint, textbox, horizontalAlign, verticalAlign);
            return (int)maxWidth;
        }

        //Ports Graphics.DrawString(text, font, brush, x, y)'s literal point-anchored semantics (no bounding
        //box, no shrink-to-fit, "\n" splits into top-anchored lines at the font's own line height) - used by
        //AssemblerElement/BeaconElement's module-tally and stat-readout text, which upstream draws this way
        //rather than through a StringFormat-bound box.
        public static void DrawStringAtPoint(SKCanvas canvas, SKColor color, SKTypeface typeface, float fontSize, string text, float x, float y) {
            SKPaint paint = RentPaint(color);
            SKFont font = RentFont(typeface, fontSize);
            SKFontMetrics metrics = font.Metrics;
            float lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
            float baseline = y - metrics.Ascent;
            foreach (string line in text.Split('\n')) {
                canvas.DrawText(line, x, baseline, font, paint);
                baseline += lineHeight;
            }
        }

        private static void DrawSingleLine(SKCanvas canvas, string text, SKFont font, SKPaint paint, Rectangle textbox, TextHorizontalAlign horizontalAlign, TextVerticalAlign verticalAlign) {
            SKFontMetrics metrics = font.Metrics;
            float baseline = VerticalBaseline(textbox, metrics.Descent - metrics.Ascent, metrics.Ascent, verticalAlign);
            canvas.DrawText(text, HorizontalX(textbox, horizontalAlign), baseline, ToSkAlign(horizontalAlign), font, paint);
        }

        private static void DrawLines(SKCanvas canvas, List<string> lines, SKFont font, SKPaint paint, Rectangle textbox, TextHorizontalAlign horizontalAlign, TextVerticalAlign verticalAlign) {
            SKFontMetrics metrics = font.Metrics;
            float lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
            float totalHeight = lineHeight * lines.Count;
            float y = VerticalBaseline(textbox, totalHeight, metrics.Ascent, verticalAlign);

            float x = HorizontalX(textbox, horizontalAlign);
            SKTextAlign skAlign = ToSkAlign(horizontalAlign);
            foreach (string line in lines) {
                canvas.DrawText(line, x, y, skAlign, font, paint);
                y += lineHeight;
            }
        }

        private static float HorizontalX(Rectangle textbox, TextHorizontalAlign horizontalAlign) =>
            horizontalAlign == TextHorizontalAlign.Center ? textbox.Left + textbox.Width / 2f : textbox.Left;

        //Near leaves all slack below the text (flush top), Far leaves it all above (flush bottom), Center
        //splits it evenly - contentHeight is the single line's (Descent-Ascent) or the multi-line block's
        //total height, whichever DrawSingleLine/DrawLines is placing.
        private static float VerticalBaseline(Rectangle textbox, float contentHeight, float ascent, TextVerticalAlign verticalAlign) =>
            verticalAlign switch {
                TextVerticalAlign.Center => textbox.Top + (textbox.Height - contentHeight) / 2 - ascent,
                TextVerticalAlign.Far => textbox.Top + (textbox.Height - contentHeight) - ascent,
                _ => textbox.Top - ascent,
            };

        private static SKTextAlign ToSkAlign(TextHorizontalAlign horizontalAlign) =>
            horizontalAlign == TextHorizontalAlign.Center ? SKTextAlign.Center : SKTextAlign.Left;

        private static float LinesHeight(SKFont font, int lineCount) {
            SKFontMetrics metrics = font.Metrics;
            return (metrics.Descent - metrics.Ascent + metrics.Leading) * lineCount;
        }

        private static List<string> WrapLines(string text, SKFont font, int maxWidth) {
            var lines = new List<string>();
            string[] words = text.Split(' ');
            var currentLine = "";

            foreach (string word in words) {
                string candidate = currentLine.Length == 0 ? word : currentLine + " " + word;
                if (currentLine.Length > 0 && font.MeasureText(candidate) > maxWidth) {
                    lines.Add(currentLine);
                    currentLine = word;
                } else {
                    currentLine = candidate;
                }
            }

            if (currentLine.Length > 0 || lines.Count == 0)
                lines.Add(currentLine);

            return lines;
        }

        public static void DrawRoundRect(SKCanvas canvas, float x, float y, float width, float height, float radius, SKPaint pen) {
            canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), radius, radius, pen);
        }

        public static void FillRoundRect(SKCanvas canvas, float x, float y, float width, float height, float radius, SKPaint brush) {
            canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), radius, radius, brush);
        }

        public static void FillRoundRectTLFlag(SKCanvas canvas, float x, float y, float width, float height, float radius, SKPaint brush) {
            float left = x;
            float top = y;
            float bottom = y + height;
            float right = x + width;

            using var path = new SKPath();
            path.ArcTo(new SKRect(left, top, left + 2 * radius, top + 2 * radius), 180f, 90f, forceMoveTo: true);
            path.LineTo(right, top);
            path.LineTo(left, bottom);
            path.Close();

            canvas.DrawPath(path, brush);
        }

        //Approximates GDI+'s AdjustableArrowCap/LineCap.ArrowAnchor line-end arrows, neither of which Skia
        //has a built-in equivalent for (reference §7): a filled triangle at the tip, oriented along the
        //tangentAnchor->tip direction, sized as multiples of the pen width (matching AdjustableArrowCap's
        //own width/height-as-pen-width-multiples convention).
        public static void DrawArrowHead(SKCanvas canvas, Point tip, Point tangentAnchor, SKColor color, float penWidth, float widthMultiplier, float heightMultiplier) {
            double dx = tip.X - tangentAnchor.X;
            double dy = tip.Y - tangentAnchor.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.001)
                return;

            double ux = dx / length, uy = dy / length;
            double px = -uy, py = ux;
            float arrowLength = heightMultiplier * penWidth;
            float halfWidth = widthMultiplier * penWidth / 2f;

            var baseCenter = new SKPoint((float)(tip.X - (ux * arrowLength)), (float)(tip.Y - (uy * arrowLength)));
            var baseLeft = new SKPoint((float)(baseCenter.X + (px * halfWidth)), (float)(baseCenter.Y + (py * halfWidth)));
            var baseRight = new SKPoint((float)(baseCenter.X - (px * halfWidth)), (float)(baseCenter.Y - (py * halfWidth)));

            using var path = new SKPath();
            path.MoveTo(tip.X, tip.Y);
            path.LineTo(baseLeft);
            path.LineTo(baseRight);
            path.Close();

            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, paint);
        }

        public static string DoubleToString(double value) {
            return Math.Abs(value) >= 100000
                ? value.ToString("0.00e0", DisplayCulture.Format)
                : Math.Abs(value) >= 10000
                ? value.ToString("0", DisplayCulture.Format)
                : Math.Abs(value) >= 100
                ? value.ToString("0.#", DisplayCulture.Format)
                : Math.Abs(value) >= 10
                ? value.ToString("0.##", DisplayCulture.Format)
                : Math.Abs(value) >= 0.1
                ? value.ToString("0.###", DisplayCulture.Format)
                : Math.Abs(value) != 0 ? value.ToString("0.######", DisplayCulture.Format) : "0";
        }

        public static string DoubleToEnergy(double value, string unit) {
            return Math.Abs(value) >= 1000000000000
                ? (value / 1000000000000).ToString("0.##", DisplayCulture.Format) + " P" + unit
                : Math.Abs(value) >= 1000000000
                ? (value / 1000000000).ToString("0.##", DisplayCulture.Format) + " G" + unit
                : Math.Abs(value) >= 1000000
                ? (value / 1000000).ToString("0.##", DisplayCulture.Format) + " M" + unit
                : Math.Abs(value) >= 1000
                ? (value / 1000).ToString("0.##", DisplayCulture.Format) + " K" + unit
                : value.ToString("0.##", DisplayCulture.Format) + " " + unit;
        }

        public static string BuildingQuantityToText(double quantity, bool roundAssemblerCount) {
            if (quantity >= 10000)
                return quantity.ToString("0.##e0", DisplayCulture.Format);
            if (roundAssemblerCount)
                return Math.Ceiling(quantity).ToString("0", DisplayCulture.Format);
            if (quantity >= 0.1)
                return quantity.ToString("0.#", DisplayCulture.Format);
            return quantity != 0 ? "<0.1" : "0";
        }
    }
}
