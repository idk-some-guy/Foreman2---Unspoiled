using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaSize = Avalonia.Size;

namespace Foreman.Mac.Canvas.Elements {
    public enum NodeDrawingStyle { Regular, PrintStyle, Simple, IconsOnly }

    //Ports the Direction/TooltipInfo pair from ProductionGraphView/FloatingTooltipControl.cs (upstream keeps
    //them with the floating-panel host; we keep them here since GetToolTips is the only P3 consumer and the
    //panel host itself is P5-panel, out of scope). ScreenSize/CustomDraw carry the recipe tooltip's
    //RecipePainter callback; the offset CustomDraw receives is the tooltip bubble's screen-space top-left.
    public enum Direction { Up, Down, Left, Right, None }

    public readonly record struct TooltipInfo(AvaloniaPoint ScreenLocation, Direction Direction, string? Text, AvaloniaSize? ScreenSize = null, Action<SKCanvas, Point>? CustomDraw = null);

    //Ports ProductionGraphView/Elements/GraphElement.cs: bounds/location, parent-relative coordinate
    //conversion, and the Paint/Draw split. RightClickMenu and the mouse-drag virtuals are P4 and stay out.
    public abstract class GraphElement : IDisposable {
        public List<GraphElement> SubElements { get; }

        public Rectangle Bounds => new(-Width / 2, -Height / 2, Width, Height);
        public virtual int Width { get; set; }
        public virtual int Height { get; set; }
        public Size Size {
            get => new(Width, Height);
            set { Width = value.Width; Height = value.Height; }
        }

        public virtual int X { get; set; }
        public virtual int Y { get; set; }
        public virtual Point Location {
            get => new(X, Y);
            set { X = value.X; Y = value.Y; }
        }

        public virtual bool Visible { get; protected set; }
        protected GraphElement? Parent { get; }

        protected GraphElement(GraphElement? parent = null) {
            Parent = parent;
            Parent?.SubElements.Add(this);
            SubElements = [];
            Visible = true;
        }

        public Point GraphToLocal(Point graphPoint) {
            return Parent == null
                ? Point.Subtract(graphPoint, (Size)Location)
                : Point.Subtract(Parent.GraphToLocal(graphPoint), (Size)Location);
        }

        public Point LocalToGraph(Point localPoint) {
            return Parent == null ? Point.Add(localPoint, (Size)Location) : Point.Add(Parent.LocalToGraph(localPoint), (Size)Location);
        }

        public bool IntersectsWithZone(Rectangle graphZone, int xborder, int yborder) {
            Point localZoneOrigin = GraphToLocal(graphZone.Location);
            return Width / 2 > localZoneOrigin.X - xborder &&
                   -(Width / 2) < localZoneOrigin.X + graphZone.Width + xborder &&
                   Height / 2 > localZoneOrigin.Y - yborder &&
                   -(Height / 2) < localZoneOrigin.Y + graphZone.Height + yborder;
        }

        public virtual void UpdateVisibility(Rectangle graphZone, int xborder = 0, int yborder = 0) {
            Visible = IntersectsWithZone(graphZone, xborder, yborder);
        }

        public virtual bool ContainsPoint(Point graphPoint) {
            return Visible && Bounds.Contains(GraphToLocal(graphPoint));
        }

        public virtual void PrePaint() { }

        public virtual List<TooltipInfo> GetToolTips(Point graphPoint) => [];

        //Ports GraphElement.Dispose's subelement cascade + parent detachment (upstream lines 105-116).
        //RightClickMenu disposal is P4 and stays out - there's no such resource on this port's base class.
        public virtual void Dispose() {
            foreach (GraphElement element in SubElements.ToArray())
                element.Dispose();
            SubElements.Clear();
            Parent?.SubElements.Remove(this);
            GC.SuppressFinalize(this);
        }

        public void Paint(SKCanvas canvas, NodeDrawingStyle style) {
            if (!Visible)
                return;

            Draw(canvas, style);
            foreach (GraphElement element in SubElements)
                element.Paint(canvas, style);
        }

        protected abstract void Draw(SKCanvas canvas, NodeDrawingStyle style);
    }
}
