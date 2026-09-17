using System;
using System.Drawing;
using AvaloniaPoint = Avalonia.Point;

namespace Foreman.Mac.Canvas {
    //Ports ProductionGraphViewer's viewport fields/transforms (ViewOffset/ViewScale/ScreenToGraph/GraphToScreen/
    //UpdateGraphBounds/MouseWheel zoom/view-drag pan) as a standalone class so render and hit-testing share one
    //implementation instead of two hand-computed copies.
    public sealed class Viewport {
        public const float MinViewScale = 0.01f;
        public const float MaxViewScale = 2f;
        public const float ZoomStepFactor = 1.1f;

        public double Width { get; private set; }
        public double Height { get; private set; }
        public Point ViewOffset { get; internal set; }
        public float ViewScale { get; internal set; }
        public Rectangle VisibleGraphBounds { get; private set; }

        public Viewport(double width = 0, double height = 0) {
            Width = width;
            Height = height;
            ViewOffset = new Point(0, 0);
            ViewScale = 1f;
            UpdateGraphBounds(Rectangle.Empty);
        }

        public void SetSize(double width, double height, Rectangle? limitToGraphBounds = null) {
            Width = width;
            Height = height;
            UpdateGraphBounds(limitToGraphBounds);
        }

        public Point ScreenToGraph(AvaloniaPoint point) {
            return new Point(
                Convert.ToInt32(((point.X - Width / 2) / ViewScale) - ViewOffset.X),
                Convert.ToInt32(((point.Y - Height / 2) / ViewScale) - ViewOffset.Y));
        }

        public AvaloniaPoint GraphToScreen(Point point) {
            return new AvaloniaPoint(
                ((point.X + ViewOffset.X) * ViewScale) + Width / 2,
                ((point.Y + ViewOffset.Y) * ViewScale) + Height / 2);
        }

        public void ZoomAt(AvaloniaPoint screenPoint, bool zoomIn, Rectangle? limitToGraphBounds = null) {
            Point oldZoomCenter = ScreenToGraph(screenPoint);

            ViewScale = zoomIn ? ViewScale * ZoomStepFactor : ViewScale / ZoomStepFactor;
            ViewScale = Math.Max(ViewScale, MinViewScale);
            ViewScale = Math.Min(ViewScale, MaxViewScale);

            Point newZoomCenter = ScreenToGraph(screenPoint);
            ViewOffset = new Point(ViewOffset.X + newZoomCenter.X - oldZoomCenter.X, ViewOffset.Y + newZoomCenter.Y - oldZoomCenter.Y);

            UpdateGraphBounds(limitToGraphBounds);
        }

        //View menu's Actual Size (contract #menubar): same anchor-preserving math as ZoomAt, but jumps to an
        //exact scale instead of stepping by ZoomStepFactor.
        public void SetAbsoluteZoom(AvaloniaPoint anchorScreenPoint, float scale, Rectangle? limitToGraphBounds = null) {
            Point oldAnchor = ScreenToGraph(anchorScreenPoint);

            ViewScale = Math.Clamp(scale, MinViewScale, MaxViewScale);

            Point newAnchor = ScreenToGraph(anchorScreenPoint);
            ViewOffset = new Point(ViewOffset.X + newAnchor.X - oldAnchor.X, ViewOffset.Y + newAnchor.Y - oldAnchor.Y);

            UpdateGraphBounds(limitToGraphBounds);
        }

        //View menu's Fit to Graph (contract #menubar): scales so the given bounds fit inside the viewport
        //and centers on them. An empty graph (zero-size bounds) leaves the viewport untouched, same as an
        //empty-bounds UpdateGraphBounds call.
        public void FitToBounds(Rectangle bounds, Rectangle? limitToGraphBounds = null) {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            ViewScale = Math.Clamp((float)Math.Min(Width / bounds.Width, Height / bounds.Height), MinViewScale, MaxViewScale);
            ViewOffset = new Point(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2));

            UpdateGraphBounds(limitToGraphBounds);
        }

        //limitToGraphBounds ports upstream's UpdateGraphBounds(limitView) split (reference §2's carried
        //note, ProductionGraphViewer.cs:1058): a mouse-drag pan hard-limits ViewOffset to stay over the
        //graph unless an object is being dragged at the same time, in which case upstream passes
        //MouseDownElement == null - the caller here passes the graph bounds to clamp to, or null to skip
        //the clamp the same way. Every other UpdateGraphBounds caller (construction, resize, zoom, pan,
        //WASD, paste, post-load) always passes the graph's actual bounds, matching upstream's own
        //default-clamps callers; PanTo is the only one that ever passes null, and only while an object is
        //mid-drag.
        public void PanTo(AvaloniaPoint currentScreenPoint, Point dragOriginGraphPoint, Rectangle? limitToGraphBounds = null) {
            Point graphLocation = ScreenToGraph(currentScreenPoint);
            ViewOffset = new Point(ViewOffset.X + graphLocation.X - dragOriginGraphPoint.X, ViewOffset.Y + graphLocation.Y - dragOriginGraphPoint.Y);
            UpdateGraphBounds(limitToGraphBounds);
        }

        //margin is in screen points, converted to graph units by ViewScale; ViewScale itself never changes.
        public void PanIntoView(Rectangle nodeBounds, double margin = 0) {
            if (PanIntoViewTarget(nodeBounds, margin) is not Point target)
                return;

            ViewOffset = target;
            UpdateGraphBounds();
        }

        //The ViewOffset that brings nodeBounds inside the inset rect, or null while it already is.
        public Point? PanIntoViewTarget(Rectangle nodeBounds, double margin = 0) {
            int marginGraph = ViewScale > 0 ? (int)Math.Round(margin / ViewScale) : (int)margin;
            Rectangle inset = Rectangle.Inflate(VisibleGraphBounds, -marginGraph, -marginGraph);
            int dx = 0, dy = 0;
            if (nodeBounds.Left < inset.Left)
                dx = inset.Left - nodeBounds.Left;
            else if (nodeBounds.Right > inset.Right)
                dx = inset.Right - nodeBounds.Right;
            if (nodeBounds.Top < inset.Top)
                dy = inset.Top - nodeBounds.Top;
            else if (nodeBounds.Bottom > inset.Bottom)
                dy = inset.Bottom - nodeBounds.Bottom;
            return dx == 0 && dy == 0 ? null : new Point(ViewOffset.X + dx, ViewOffset.Y + dy);
        }

        public void UpdateGraphBounds(Rectangle? limitToGraphBounds = null) {
            if (limitToGraphBounds is Rectangle bounds) {
                if (bounds.Width == 0 || bounds.Height == 0) {
                    ViewOffset = new Point(0, 0);
                } else {
                    Point screenCentre = ScreenToGraph(new AvaloniaPoint(Width / 2, Height / 2));
                    int newX = ViewOffset.X;
                    int newY = ViewOffset.Y;
                    if (screenCentre.X < bounds.X)
                        newX -= bounds.X - screenCentre.X;
                    if (screenCentre.Y < bounds.Y)
                        newY -= bounds.Y - screenCentre.Y;
                    if (screenCentre.X > bounds.X + bounds.Width)
                        newX -= bounds.X + bounds.Width - screenCentre.X;
                    if (screenCentre.Y > bounds.Y + bounds.Height)
                        newY -= bounds.Y + bounds.Height - screenCentre.Y;
                    ViewOffset = new Point(newX, newY);
                }
            }

            VisibleGraphBounds = new Rectangle(
                (int)(-Width / (2 * ViewScale) - ViewOffset.X),
                (int)(-Height / (2 * ViewScale) - ViewOffset.Y),
                (int)(Width / ViewScale),
                (int)(Height / ViewScale));
        }
    }
}
