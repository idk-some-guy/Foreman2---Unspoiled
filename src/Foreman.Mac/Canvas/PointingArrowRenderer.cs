using Foreman;
using SkiaSharp;
using System;
using System.Drawing;
using System.Linq;
using AvaloniaPoint = Avalonia.Point;

namespace Foreman.Mac.Canvas {
    //Ports ProductionGraphView/PointingArrowRenderer.cs in full: screen-space guide arrows pointing off-screen
    //toward error/warning/disconnected/over-under-supplied nodes. GDI+'s LineCap.ArrowAnchor has no Skia
    //equivalent (same gap as BaseLinkElement's arrow cap - reference §7), so the arrowhead is drawn with the
    //same GraphicsStuff.DrawArrowHead triangle helper rather than a pen end-cap.
    public sealed class PointingArrowRenderer(Viewport viewport) {
        private enum Border { Top, Bottom, Left, Right }

        public bool ShowErrorArrows { get; set; }
        public bool ShowWarningArrows { get; set; }
        public bool ShowDisconnectedArrows { get; set; }
        public bool ShowOUNodeArrows { get; set; }

        private const int ArrowScale = 8;
        private const int Padding = 10;
        private const float ArrowCapWidthMultiplier = 3f;
        private const float ArrowCapHeightMultiplier = 3f;

        private static readonly SKColor ErrorColor = new(0x8B, 0x00, 0x00); //DarkRed
        private static readonly SKColor WarningColor = new(0xFF, 0x8C, 0x00); //DarkOrange
        private static readonly SKColor DisconnectedColor = new(0xDA, 0xA5, 0x20); //Goldenrod
        private static readonly SKColor OUNodeColor = new(0xDA, 0xA5, 0x20); //Goldenrod

        private readonly Viewport viewport = viewport;

        public void Paint(SKCanvas canvas, ProductionGraph graph) {
            if (ShowErrorArrows)
                foreach (BaseNode node in graph.Nodes.Where(n => n.State == NodeState.Error))
                    DrawArrow(canvas, ToScreenPoint(node.Location), ErrorColor);
            if (ShowWarningArrows)
                foreach (BaseNode node in graph.Nodes.Where(n => n.State == NodeState.Warning))
                    DrawArrow(canvas, ToScreenPoint(node.Location), WarningColor);
            if (ShowDisconnectedArrows)
                foreach (BaseNode node in graph.Nodes.Where(n => n.State == NodeState.MissingLink))
                    DrawArrow(canvas, ToScreenPoint(node.Location), DisconnectedColor);
            if (ShowOUNodeArrows)
                foreach (BaseNode node in graph.Nodes.Where(n => n.IsOverproducing() || n.ManualRateNotMet()))
                    DrawArrow(canvas, ToScreenPoint(node.Location), OUNodeColor);
        }

        private Point ToScreenPoint(Point graphPoint) {
            AvaloniaPoint screenPoint = viewport.GraphToScreen(graphPoint);
            return new Point((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y));
        }

        private void DrawArrow(SKCanvas canvas, Point nodeOrigin, SKColor color) {
            int width = (int)viewport.Width;
            int height = (int)viewport.Height;

            if (nodeOrigin.X > -Padding && nodeOrigin.X < width + Padding && nodeOrigin.Y > -Padding && nodeOrigin.Y < height + Padding) //roughly 'in bounds'
                return;

            var center = new Point(width / 2, height / 2);
            Point borderPoint;

            if (nodeOrigin.Y < Padding) {
                borderPoint = IntersectionPoint(nodeOrigin, center, Padding, horizontal: true);
                if (borderPoint.X >= Padding && borderPoint.X <= width - Padding) { //within the top segment of the border
                    DrawArrowSegment(canvas, center, borderPoint, ArrowScale * 4, color);
                    return;
                }
            }

            if (nodeOrigin.Y > height - Padding) {
                borderPoint = IntersectionPoint(nodeOrigin, center, height - Padding, horizontal: true);
                if (borderPoint.X >= Padding && borderPoint.X <= width - Padding) { //within the bottom segment of the border
                    DrawArrowSegment(canvas, center, borderPoint, ArrowScale * 4, color);
                    return;
                }
            }

            if (nodeOrigin.X < Padding) {
                borderPoint = IntersectionPoint(nodeOrigin, center, Padding, horizontal: false);
                if (borderPoint.Y >= Padding && borderPoint.Y <= height - Padding) { //within the left segment of the border
                    DrawArrowSegment(canvas, center, borderPoint, ArrowScale * 4, color);
                    return;
                }
            }

            if (nodeOrigin.X > width - Padding) {
                borderPoint = IntersectionPoint(nodeOrigin, center, width - Padding, horizontal: false);
                if (borderPoint.Y >= Padding && borderPoint.Y <= height - Padding) { //within the right segment of the border
                    DrawArrowSegment(canvas, center, borderPoint, ArrowScale * 4, color);
                    return;
                }
            }
            //if we are here, then there was no need to paint the arrow (within borders). Due to previous checks this shouldnt happen though.
        }

        private static void DrawArrowSegment(SKCanvas canvas, Point origin, Point endpoint, float length, SKColor color) {
            var sizedVector = new SizeF(origin.X - endpoint.X, origin.Y - endpoint.Y);
            float vectorLength = (float)Math.Sqrt((sizedVector.Width * sizedVector.Width) + (sizedVector.Height * sizedVector.Height));
            if (vectorLength < 0.001f)
                return;
            sizedVector = new SizeF(sizedVector.Width * length / vectorLength, sizedVector.Height * length / vectorLength);
            Point lineOrigin = Point.Add(endpoint, sizedVector.ToSize());

            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = ArrowScale, StrokeCap = SKStrokeCap.Square, IsAntialias = true };
            canvas.DrawLine(lineOrigin.X, lineOrigin.Y, endpoint.X, endpoint.Y, paint);
            GraphicsStuff.DrawArrowHead(canvas, endpoint, lineOrigin, color, ArrowScale, ArrowCapWidthMultiplier, ArrowCapHeightMultiplier);
        }

        private static Point IntersectionPoint(Point a, Point b, int c, bool horizontal) //c is x if vertical line, and y if horizontal line
        {
            return horizontal
                ? new Point(a.X + ((b.X - a.X) * (c - a.Y) / (b.Y - a.Y)), c)
                : new Point(c, a.Y + ((b.Y - a.Y) * (c - a.X) / (b.X - a.X)));
        }
    }
}
