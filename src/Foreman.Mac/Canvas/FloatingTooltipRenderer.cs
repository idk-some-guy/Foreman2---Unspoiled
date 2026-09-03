using Foreman.Mac.Canvas.Elements;
using SkiaSharp;
using System;
using System.Drawing;
using AvaloniaSize = Avalonia.Size;

namespace Foreman.Mac.Canvas {
    //Ports ProductionGraphView/FloatingTooltipRenderer.cs's plain-text/custom-draw Paint path (reference §8
    //step 11). FloatingTooltipControl's floating-panel half (pinned edit-panel tooltips, P5-panel) isn't
    //ported, so there's no floatingTooltipControls dictionary or paintAll/showOverride distinction to keep -
    //this only ever draws one live-hover TooltipInfo at a time, the shape GraphElement.GetToolTips returns.
    public static class FloatingTooltipRenderer {
        private const int Border = 2;
        private const int TextPadding = 2;
        private const int ArrowSize = 10;
        private const float TextFontSize = 10f;

        private static readonly SKTypeface TextTypeface = SKTypeface.Default;
        private static readonly SKColor TextColor = SKColors.White;
        private static readonly SKPaint BackgroundPaint = new() { Color = new SKColor(65, 65, 65), Style = SKPaintStyle.Fill, IsAntialias = true };
        private static readonly SKPaint BorderPaint = new() { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };

        public static void Draw(SKCanvas canvas, TooltipInfo tooltip) {
            var screenArrowPoint = new Point((int)tooltip.ScreenLocation.X, (int)tooltip.ScreenLocation.Y);
            SKSize? measuredText = tooltip.Text is string text ? MeasureTextBlock(text) : null;

            Size size = measuredText is SKSize measured ? new Size((int)measured.Width + (TextPadding * 2), (int)measured.Height + (TextPadding * 2))
                : tooltip.ScreenSize is AvaloniaSize screenSize ? new Size((int)screenSize.Width, (int)screenSize.Height)
                : Size.Empty;

            Rectangle bounds = tooltip.Direction == Direction.None
                ? new Rectangle(screenArrowPoint, size)
                : GetTooltipScreenBounds(screenArrowPoint, size, tooltip.Direction);

            DrawArrow(canvas, screenArrowPoint, tooltip.Direction);
            GraphicsStuff.FillRoundRect(canvas, bounds.X - Border, bounds.Y - Border, bounds.Width + (Border * 2), bounds.Height + (Border * 2), 3, BorderPaint);
            GraphicsStuff.FillRoundRect(canvas, bounds.X, bounds.Y, bounds.Width, bounds.Height, 3, BackgroundPaint);

            if (tooltip.Text is string body && measuredText is SKSize textSize) {
                float topY = bounds.Y + TextPadding - 1 + (bounds.Height / 2f) - (textSize.Height / 2f);
                GraphicsStuff.DrawStringAtPoint(canvas, TextColor, TextTypeface, TextFontSize, body, bounds.X + TextPadding, topY);
            }

            tooltip.CustomDraw?.Invoke(canvas, bounds.Location);
        }

        private static void DrawArrow(SKCanvas canvas, Point screenArrowPoint, Direction direction) {
            if (direction == Direction.None)
                return;

            Point p1, p2;
            switch (direction) {
                case Direction.Down:
                    p1 = new Point(screenArrowPoint.X - (ArrowSize / 2), screenArrowPoint.Y - ArrowSize);
                    p2 = new Point(screenArrowPoint.X + (ArrowSize / 2), screenArrowPoint.Y - ArrowSize);
                    break;
                case Direction.Left:
                    p1 = new Point(screenArrowPoint.X + ArrowSize, screenArrowPoint.Y - (ArrowSize / 2));
                    p2 = new Point(screenArrowPoint.X + ArrowSize, screenArrowPoint.Y + (ArrowSize / 2));
                    break;
                case Direction.Up:
                    p1 = new Point(screenArrowPoint.X - (ArrowSize / 2), screenArrowPoint.Y + ArrowSize);
                    p2 = new Point(screenArrowPoint.X + (ArrowSize / 2), screenArrowPoint.Y + ArrowSize);
                    break;
                default:
                    p1 = new Point(screenArrowPoint.X - ArrowSize, screenArrowPoint.Y - (ArrowSize / 2));
                    p2 = new Point(screenArrowPoint.X - ArrowSize, screenArrowPoint.Y + (ArrowSize / 2));
                    break;
            }

            using var path = new SKPath();
            path.MoveTo(screenArrowPoint.X, screenArrowPoint.Y);
            path.LineTo(p1.X, p1.Y);
            path.LineTo(p2.X, p2.Y);
            path.Close();
            canvas.DrawPath(path, BackgroundPaint);
        }

        public static Rectangle GetTooltipScreenBounds(Point screenArrowPoint, Size size, Direction direction) {
            Point centreOffset = direction switch {
                Direction.Down => new Point(0, -ArrowSize - (size.Height / 2)),
                Direction.Left => new Point(ArrowSize + (size.Width / 2), 0),
                Direction.Up => new Point(0, ArrowSize + (size.Height / 2)),
                Direction.Right => new Point(-ArrowSize - (size.Width / 2), 0),
                _ => Point.Empty
            };
            return new Rectangle(
                screenArrowPoint.X + centreOffset.X - (size.Width / 2),
                screenArrowPoint.Y + centreOffset.Y - (size.Height / 2),
                size.Width, size.Height);
        }

        private static SKSize MeasureTextBlock(string text) {
            using var font = new SKFont(TextTypeface, TextFontSize);
            string[] lines = text.Split('\n');
            float maxWidth = 0;
            foreach (string line in lines)
                maxWidth = Math.Max(maxWidth, font.MeasureText(line));
            SKFontMetrics metrics = font.Metrics;
            float lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
            return new SKSize(maxWidth, lineHeight * lines.Length);
        }
    }
}
