using SkiaSharp;
using System;
using System.Drawing;

namespace Foreman.Mac.Canvas {
    //Ports ProductionGraphView/Annotations/TextAnnotationLayout.cs. MeasureBoxForText takes an SKTypeface +
    //float size instead of upstream's GDI Font (Graphics.MeasureString on a scratch Bitmap), using
    //SKFont.MeasureText/Metrics for the same natural-size-plus-padding box; ComputeResizeFontSize and
    //NearlyEqualFontSize are pure math and port verbatim.
    public static class TextAnnotationLayout {
        public const int DefaultPadding = 16;
        public const int MinBoxWidth = 60;
        public const int MinBoxHeight = 30;
        public const float MinFontSizePt = 6f;
        public const float MaxFontSizePt = 288f;

        public static Size MeasureBoxForText(
            string text,
            SKTypeface typeface,
            float fontSize,
            int padding = DefaultPadding,
            int minWidth = MinBoxWidth,
            int minHeight = MinBoxHeight) {
            if (string.IsNullOrEmpty(text))
                return new Size(minWidth, minHeight);

            using var font = new SKFont(typeface, fontSize);
            float width = font.MeasureText(text);
            SKFontMetrics metrics = font.Metrics;
            float height = metrics.Descent - metrics.Ascent;
            return new Size(
                Math.Max(minWidth, (int)Math.Ceiling(width) + padding),
                Math.Max(minHeight, (int)Math.Ceiling(height) + padding));
        }

        public static float ComputeResizeFontSize(
            float startFontSizePt,
            int startWidth,
            int startHeight,
            int newWidth,
            int newHeight) {
            if (startWidth <= 0 || startHeight <= 0)
                return startFontSizePt;

            float scale = (float)Math.Sqrt(
                (newWidth / (double)startWidth) * (newHeight / (double)startHeight));
            return Math.Clamp(startFontSizePt * scale, MinFontSizePt, MaxFontSizePt);
        }

        public static bool NearlyEqualFontSize(float a, float b) =>
            Math.Abs(a - b) < 0.05f;
    }
}
