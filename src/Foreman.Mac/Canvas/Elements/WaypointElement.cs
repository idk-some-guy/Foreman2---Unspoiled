using Foreman.Mac.Canvas.KitSkia;
using SkiaSharp;
using System;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    public sealed class WaypointElement(LinkElement link, int index, Point location) : GraphElement {
        public LinkElement Link { get; } = link;
        public int Index { get; } = index;

        public override Point Location { get; set; } = location;
        public override int X { get => Location.X; set => Location = new Point(value, Location.Y); }
        public override int Y { get => Location.Y; set => Location = new Point(Location.X, value); }

        public bool IsSelected => Link.SelectedWaypointIndex == Index;

        public Rectangle RenderBounds => Square(TokenBridge.Current.WaypointDiameter);

        public Rectangle HitBounds => Square(TokenBridge.Current.WaypointHitTarget);

        private Rectangle Square(float size) {
            int half = (int)Math.Round(size / 2f);
            return new Rectangle(Location.X - half, Location.Y - half, half * 2, half * 2);
        }

        public override bool ContainsPoint(Point graphPoint) => Link.AreWaypointsShown && HitBounds.Contains(graphPoint);

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) { }

        public void DrawMarker(SKCanvas canvas, CanvasTokens tokens) {
            float radius = tokens.WaypointDiameter / 2f;
            var centre = new SKPoint(Location.X, Location.Y);
            canvas.DrawCircle(centre, radius, GraphicsStuff.RentFillPaint(IsSelected ? tokens.Accent : tokens.WindowBackground));
            canvas.DrawCircle(centre, radius - 1f, GraphicsStuff.RentStrokePaint(tokens.Accent, 2f));
        }

        public static Point DragTarget(Point graphPoint, GridManager grid) {
            Point target = grid.AlignToGrid(graphPoint);
            if (!grid.LockDragToAxis)
                return target;

            Point travelled = Point.Subtract(graphPoint, (Size)grid.DragOrigin);
            return Math.Abs(travelled.X) > Math.Abs(travelled.Y) ? new Point(target.X, grid.DragOrigin.Y) : new Point(grid.DragOrigin.X, target.Y);
        }
    }
}
