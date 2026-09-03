using Foreman.Mac.Canvas;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/AnnotationElement.cs in full. P3 ported the read-only slice (bounds,
    //Draw helpers, ToSaveData/FromSaveData); this task adds the P4 half: 8-handle resize, drag, mouse
    //routing, the right-click menu, lasso-edge intersection, and the now-real selection/handle drawing.
    //Context replaces upstream's constructor-injected graphViewer field (Task 3 dropped it since annotations
    //are built by static factories with no viewer in scope) - GraphViewer.AddAnnotationElement assigns it once
    //the element joins the live tree, the same way NodeElementContext is threaded onto BaseNodeElement.
    public abstract class AnnotationElement : GraphElement {
        public bool IsSelected { get; set; }
        public AnnotationElementContext? Context { get; set; }

        private const float EdgeHitScreenPx = 8f;
        private const float HandleDrawScreenPx = 5f;
        private const float HandleHitScreenPx = 10f;
        private const int MinAnnotationSize = 30;

        private static readonly SKColor SelectionHighlightColor = new(80, 160, 255, 220);
        private const float SelectionHighlightScreenPx = 2.5f;

        //Fixed color, safe as a shared static across every viewer.
        private static readonly SKPaint HandleFillPaint = new() { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };

        //Per-instance mutate-reset paints: StrokeWidth tracks ViewScale per frame, so not shareable statics.
        private readonly SKPaint _selectionHighlightPaint = new() { Color = SelectionHighlightColor, Style = SKPaintStyle.Stroke, IsAntialias = true };
        private readonly SKPaint _handleBorderPaint = new() { Color = new SKColor(60, 100, 200, 255), Style = SKPaintStyle.Stroke, IsAntialias = true };

        protected enum HandleType {
            None,
            TopLeft, TopCenter, TopRight,
            MiddleLeft, MiddleRight,
            BottomLeft, BottomCenter, BottomRight
        }

        private static readonly HandleType[] AllHandles = [
            HandleType.TopLeft, HandleType.TopCenter, HandleType.TopRight,
            HandleType.MiddleLeft, HandleType.MiddleRight,
            HandleType.BottomLeft, HandleType.BottomCenter, HandleType.BottomRight
        ];

        private Point _dragStartMouseLocation;
        private Point _dragStartElementLocation;
        private bool _dragStarted;
        private HandleType _activeHandle = HandleType.None;
        private int _dragStartWidth;
        private int _dragStartHeight;

        public bool IsResizing => _activeHandle != HandleType.None;
        protected int ResizeStartWidth => _dragStartWidth;
        protected int ResizeStartHeight => _dragStartHeight;

        protected AnnotationElement(Point graphLocation, int width, int height) : base(parent: null) {
            X = graphLocation.X;
            Y = graphLocation.Y;
            Width = width;
            Height = height;
        }

        private float ViewScale => Context?.ViewScale?.Invoke() ?? 1f;

        protected Rectangle GetGraphRect() {
            Point topLeft = LocalToGraph(new Point(-Width / 2, -Height / 2));
            return new Rectangle(topLeft.X, topLeft.Y, Width, Height);
        }

        //Ports the base ContainsPoint override (reference §6): handles win first when selected, then the full
        //interior when selected, otherwise only the edge band - clicking a selected annotation's hollow
        //center falls through to rubber-band selection, matching upstream.
        public override bool ContainsPoint(Point graphPoint) {
            if (!Visible)
                return false;
            if (IsSelected && GetHandleAtPoint(graphPoint) != HandleType.None)
                return true;

            Point local = GraphToLocal(graphPoint);
            if (!Bounds.Contains(local))
                return false;
            if (IsSelected)
                return true;

            float edge = EdgeHitScreenPx / ViewScale;
            int halfW = Width / 2;
            int halfH = Height / 2;
            return local.X <= -halfW + edge || local.X >= halfW - edge
                || local.Y <= -halfH + edge || local.Y >= halfH - edge;
        }

        public bool ContainsPointFull(Point graphPoint) => Visible && Bounds.Contains(GraphToLocal(graphPoint));

        public bool PickAtPoint(Point graphPoint) => IsSelected ? ContainsPoint(graphPoint) : ContainsPointFull(graphPoint);

        public void ForceVisible() => Visible = true;

        public override void Dispose() {
            _selectionHighlightPaint.Dispose();
            _handleBorderPaint.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        //----------------------------------------------Mouse handling (reference §6)

        public void MouseDown(Point graphPoint) {
            _dragStartMouseLocation = graphPoint;
            _dragStartElementLocation = new Point(X, Y);
            _dragStartWidth = Width;
            _dragStartHeight = Height;
            _dragStarted = false;
            _activeHandle = IsSelected ? GetHandleAtPoint(graphPoint) : HandleType.None;
            OnAfterMouseDown();
        }

        //Hook for TextAnnotationElement to snapshot its pre-resize font size (reference §6's
        //_resizeStartFontSize) without needing MouseDown itself to be virtual.
        protected virtual void OnAfterMouseDown() { }

        //First post-threshold call only arms the drag (matches BaseNodeElement.Dragged's same pattern), so
        //there's no jump on the threshold frame.
        public void Dragged(Point graphPoint) {
            if (!_dragStarted) {
                _dragStarted = true;
                return;
            }

            if (_activeHandle == HandleType.None) {
                Point offset = Point.Subtract(graphPoint, (Size)_dragStartMouseLocation);
                X = _dragStartElementLocation.X + offset.X;
                Y = _dragStartElementLocation.Y + offset.Y;
            } else {
                ApplyResize(graphPoint);
            }
        }

        public void CancelMouseCapture() {
            _dragStarted = false;
            _activeHandle = HandleType.None;
        }

        //Ports MouseUpAction's right-click body (reference §4f): Properties always present, then either
        //"Delete selection" (this annotation selected alongside other nodes/annotations) or a lone "Delete".
        public List<MenuEntry> BuildRightClickMenu() {
            var entries = new List<MenuEntry> {
                MenuEntry.Item("Properties", () => Context?.ShowPropertiesDialog?.Invoke(this)),
                MenuEntry.Divider
            };

            bool inSelection = Context is { } ctx && ctx.SelectedAnnotations.Contains(this)
                && (ctx.SelectedNodeCount() > 0 || ctx.SelectedAnnotations.Count > 1);
            entries.Add(inSelection
                ? MenuEntry.Item("Delete selection", () => Context?.TryDeleteSelection?.Invoke())
                : MenuEntry.Item("Delete", () => Context?.RemoveAnnotationElement?.Invoke(this)));

            return entries;
        }

        //----------------------------------------------Resize math (reference §6, verbatim from upstream)

        private void ApplyResize(Point mouseGraph) {
            int dx = mouseGraph.X - _dragStartMouseLocation.X;
            int dy = mouseGraph.Y - _dragStartMouseLocation.Y;

            int startLeft = _dragStartElementLocation.X - _dragStartWidth / 2;
            int startRight = _dragStartElementLocation.X + _dragStartWidth / 2;
            int startTop = _dragStartElementLocation.Y - _dragStartHeight / 2;
            int startBottom = _dragStartElementLocation.Y + _dragStartHeight / 2;

            int newLeft = startLeft;
            int newRight = startRight;
            int newTop = startTop;
            int newBottom = startBottom;

            switch (_activeHandle) {
                case HandleType.TopLeft: newLeft = startLeft + dx; newTop = startTop + dy; break;
                case HandleType.TopCenter: newTop = startTop + dy; break;
                case HandleType.TopRight: newRight = startRight + dx; newTop = startTop + dy; break;
                case HandleType.MiddleLeft: newLeft = startLeft + dx; break;
                case HandleType.MiddleRight: newRight = startRight + dx; break;
                case HandleType.BottomLeft: newLeft = startLeft + dx; newBottom = startBottom + dy; break;
                case HandleType.BottomCenter: newBottom = startBottom + dy; break;
                case HandleType.BottomRight: newRight = startRight + dx; newBottom = startBottom + dy; break;
            }

            if (newRight - newLeft < MinAnnotationSize) {
                bool movingLeft = _activeHandle is HandleType.TopLeft or HandleType.MiddleLeft or HandleType.BottomLeft;
                if (movingLeft)
                    newLeft = newRight - MinAnnotationSize;
                else
                    newRight = newLeft + MinAnnotationSize;
            }

            if (newBottom - newTop < MinAnnotationSize) {
                bool movingTop = _activeHandle is HandleType.TopLeft or HandleType.TopCenter or HandleType.TopRight;
                if (movingTop)
                    newTop = newBottom - MinAnnotationSize;
                else
                    newBottom = newTop + MinAnnotationSize;
            }

            Width = newRight - newLeft;
            Height = newBottom - newTop;
            X = newLeft + Width / 2;
            Y = newTop + Height / 2;
            OnResized();
        }

        protected virtual void OnResized() { }

        private int GetHandleDrawHalfSize() {
            float elementCap = Math.Min(Width, Height) / 5f;
            return (int)Math.Max(3f, Math.Min(HandleDrawScreenPx / ViewScale, Math.Max(elementCap, 4f)));
        }

        private int GetHandleHitHalfSize() {
            float elementCap = Math.Min(Width, Height) / 4f;
            return (int)Math.Max(5f, Math.Min(HandleHitScreenPx / ViewScale, Math.Max(elementCap, 6f)));
        }

        private Rectangle GetHandleRect(HandleType handle, int half) {
            int size = half * 2;
            int cx = X, cy = Y;
            int left = cx - Width / 2;
            int right = cx + Width / 2;
            int top = cy - Height / 2;
            int bottom = cy + Height / 2;

            return handle switch {
                HandleType.TopLeft => new Rectangle(left - half, top - half, size, size),
                HandleType.TopCenter => new Rectangle(cx - half, top - half, size, size),
                HandleType.TopRight => new Rectangle(right - half, top - half, size, size),
                HandleType.MiddleLeft => new Rectangle(left - half, cy - half, size, size),
                HandleType.MiddleRight => new Rectangle(right - half, cy - half, size, size),
                HandleType.BottomLeft => new Rectangle(left - half, bottom - half, size, size),
                HandleType.BottomCenter => new Rectangle(cx - half, bottom - half, size, size),
                HandleType.BottomRight => new Rectangle(right - half, bottom - half, size, size),
                _ => Rectangle.Empty
            };
        }

        protected HandleType GetHandleAtPoint(Point graphPoint) {
            if (!IsSelected)
                return HandleType.None;

            int hitHalf = GetHandleHitHalfSize();
            foreach (HandleType handle in AllHandles)
                if (GetHandleRect(handle, hitHalf).Contains(graphPoint))
                    return handle;
            return HandleType.None;
        }

        //----------------------------------------------Drawing helpers for subclasses

        //Real now (was no-op'd for the P3 read-only path): a translucent blue rectangle around graphRect,
        //stroke width compensated for zoom so it stays a constant screen thickness.
        protected void DrawSelectionHighlight(SKCanvas canvas, Rectangle graphRect) {
            if (!IsSelected)
                return;

            float pw = SelectionHighlightScreenPx / ViewScale;
            _selectionHighlightPaint.StrokeWidth = pw;
            canvas.DrawRect(SKRect.Create(graphRect.X - pw, graphRect.Y - pw, graphRect.Width + (pw * 2), graphRect.Height + (pw * 2)), _selectionHighlightPaint);
        }

        //Ports DrawResizeHandles (reference §6): 8 white squares with a blue border, call at the end of each
        //subclass Draw() so they sit on top of the shape/text.
        protected void DrawResizeHandles(SKCanvas canvas) {
            if (!IsSelected)
                return;

            //Ports Color.FromArgb(60,100,200) verbatim (reference §6's DrawResizeHandles) - that upstream call
            //is the 3-arg RGB overload (R=60,G=100,B=200, blue), not alpha-first; _handleBorderPaint carries
            //that fixed color already, only StrokeWidth needs refreshing here.
            _handleBorderPaint.StrokeWidth = Math.Max(0.5f, 1f / ViewScale);

            int drawHalf = GetHandleDrawHalfSize();
            foreach (HandleType handle in AllHandles) {
                Rectangle r = GetHandleRect(handle, drawHalf);
                var skRect = SKRect.Create(r.X, r.Y, r.Width, r.Height);
                canvas.DrawRect(skRect, HandleFillPaint);
                canvas.DrawRect(skRect, _handleBorderPaint);
            }
        }

        //----------------------------------------------Lasso / selection support (reference §1/§6)

        //True when the lasso overlaps this annotation's edge without being entirely contained inside it - a
        //lasso drawn wholly inside a shape doesn't select it.
        public bool LassoIntersectsEdge(Rectangle lasso) {
            int annLeft = X - Width / 2;
            int annRight = X + Width / 2;
            int annTop = Y - Height / 2;
            int annBottom = Y + Height / 2;

            bool overlaps = lasso.Right > annLeft && lasso.Left < annRight && lasso.Bottom > annTop && lasso.Top < annBottom;
            if (!overlaps)
                return false;

            bool lassoInsideAnnotation = lasso.Left >= annLeft && lasso.Right <= annRight && lasso.Top >= annTop && lasso.Bottom <= annBottom;
            return !lassoInsideAnnotation;
        }

        //----------------------------------------------Abstract interface

        public abstract AnnotationSaveData ToSaveData();

        public static AnnotationElement FromSaveData(AnnotationSaveData data) => data switch {
            TextAnnotationSaveData text => TextAnnotationElement.FromSaveData(text),
            ShapeAnnotationSaveData shape => ShapeAnnotationElement.FromSaveData(shape),
            _ => throw new InvalidOperationException("Unknown annotation type in save: " + data.Type)
        };

        protected static ColorSaveData ColorToSave(SKColor c) => new(c.Alpha, c.Red, c.Green, c.Blue);

        protected static SKColor ColorFromSave(ColorSaveData c) => new(c.R, c.G, c.B, c.A);

        //AppSettings persists annotation-style defaults as .NET Color.ToArgb() ints (reference §6's
        //SaveDefaults writing to Properties.Settings.Default) - these convert to/from SKColor for the
        //Text/Shape subclasses' static defaults.
        protected static SKColor ArgbIntToColor(int argb) =>
            new((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF), (byte)((argb >> 24) & 0xFF));

        protected static int ColorToArgbInt(SKColor c) =>
            unchecked((c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue);
    }
}
