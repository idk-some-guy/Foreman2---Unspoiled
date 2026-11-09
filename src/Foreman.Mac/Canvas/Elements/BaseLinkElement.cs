using Foreman.Mac.Canvas.KitSkia;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/BaseLinkElement.cs: the three bezier path shapes (Simple/UShape/
    //NShape), visibility bounds, and the direction-arrow cap. GDI+'s AdjustableArrowCap has no Skia
    //equivalent, so the arrowhead is drawn as an explicit filled triangle (GraphicsStuff.DrawArrowHead)
    //instead of a pen end-cap; the underlying line is not inset for it (upstream's cap replaces the last
    //few pixels of stroke) since both are drawn in the same color and the seam isn't visible.
    public abstract class BaseLinkElement : GraphElement {
        public enum LineType { Simple, UShape, NShape }

        public BaseNodeElement? SupplierElement { get; protected set; }
        public BaseNodeElement? ConsumerElement { get; protected set; }
        public virtual ItemQualityPair Item { get; protected set; }

        private Point consumerOrigin, supplierOrigin;
        private NodeDirection consumerDirection, supplierDirection;

        public LineType Type { get; private set; }

        private Point consumerPull, supplierPull; //for basic links
        private Point midUA, midUB, midUC, midUD, pullU1, pullU2, pullU3, pullU4; //for U shape links
        private Point midNA, midNB, midNC, midND, midNE, midNF, pullN1, pullN2, pullN3, pullN4, pullN5, pullN6, pullN7, pullN8; //for N shape links

        public float LinkWidth { get; set; }

        protected virtual LinkRouting Routing => LinkRouting.Rounded;
        protected virtual IReadOnlyList<Point> RouteWaypoints => [];
        protected virtual float StrokeOpacity => 1f;
        protected virtual bool IsHaloShown => false;
        protected virtual bool IsCasingShown => false;
        protected virtual bool IsUndersupplied => false;
        protected virtual void DrawWaypoints(SKCanvas canvas, CanvasTokens tokens) { }

        private LinkRouting routedRouting;
        private IReadOnlyList<Point> routedWaypoints = [];
        private double routedScale;
        private float routedCorner;
        private SKRect routedSupplierBounds, routedConsumerBounds;
        private List<SKPoint>? orthogonalPoints;
        private List<RouteSegment>? orthogonalSegments;
        private List<CubicSegment>? waypointCubics;
        private List<SKPoint>? flattened;
        private SKPoint arrowAnchor;
        private bool hasCustomRoute;

        public Rectangle CalculatedBounds { get; private set; }

        protected bool iconOnlyDraw { get; set; }
        private const int circlePull = 100;
        private const float ArrowCapWidthMultiplier = 4f;
        private const float ArrowCapHeightMultiplier = 3f;

        protected NodeElementContext Context { get; }

        public override Point Location {
            get => new(); //link elements are always considered to be located at 0,0 graph to simplify things, with their connection points being in graph-coordinates (no need for local transforms)
            set { }
        }
        public override int X { get => 0; set { } }
        public override int Y { get => 0; set { } }

        //parent is null for every link element except a passthrough-bus slave DraggedLinkElement, which
        //chains onto its master (reference §3's UpdateSlaveLinks) so Dispose/SubElements cascade together.
        protected BaseLinkElement(NodeElementContext context, GraphElement? parent = null) : base(parent) {
            Context = context;
            LinkWidth = TokenBridge.Current.LinkWidth;
        }

        public override void UpdateVisibility(Rectangle graphZone, int xborder = 0, int yborder = 0) {
            //NOTE: link element works in graph coordinates throughout (Location is 0,0), so we dont need graph-to-local conversions.
            UpdateCurve();
            Visible =
                        CalculatedBounds.X + CalculatedBounds.Width > graphZone.X - xborder &&
                        CalculatedBounds.X < graphZone.X + graphZone.Width + xborder &&
                        CalculatedBounds.Y + CalculatedBounds.Height > graphZone.Y - yborder &&
                        CalculatedBounds.Y < graphZone.Y + graphZone.Height + yborder;
        }

        protected abstract Tuple<Point, Point>? GetCurveEndpoints(); //supplier,consumer
        protected abstract Tuple<NodeDirection, NodeDirection>? GetEndpointDirections(); //supplier,consumer

        protected void UpdateCurve() //updates all points & boundaries (important for occluding objects outside view)
        {
            Tuple<Point, Point>? endpoints = GetCurveEndpoints();
            Tuple<NodeDirection, NodeDirection>? endpointDirections = GetEndpointDirections();

            if (endpoints == null || endpointDirections == null)
                return;

            CanvasTokens tokens = TokenBridge.Current;
            LinkRouting routing = Routing;
            IReadOnlyList<Point> waypoints = RouteWaypoints;
            float corner = routing == LinkRouting.Orthogonal ? Math.Max(tokens.LinkCornerRadius, LinkWidth) : 0f;
            SKRect supplierBounds = NodeBounds(SupplierElement, endpoints.Item1), consumerBounds = NodeBounds(ConsumerElement, endpoints.Item2);
            if (supplierBounds != routedSupplierBounds || consumerBounds != routedConsumerBounds || supplierOrigin != endpoints.Item1 || consumerOrigin != endpoints.Item2 || supplierDirection != endpointDirections.Item1 || consumerDirection != endpointDirections.Item2
                || routing != routedRouting || !ReferenceEquals(waypoints, routedWaypoints) || tokens.Scale != routedScale || corner != routedCorner) {
                routedRouting = routing;
                routedWaypoints = waypoints;
                routedScale = tokens.Scale;
                routedCorner = corner;
                routedSupplierBounds = supplierBounds;
                routedConsumerBounds = consumerBounds;
                flattened = null;
                supplierOrigin = endpoints.Item1;
                supplierDirection = endpointDirections.Item1;
                consumerOrigin = endpoints.Item2;
                consumerDirection = endpointDirections.Item2;

                Type = (supplierDirection != consumerDirection) ? LineType.UShape :
                    ((supplierDirection == NodeDirection.Up && consumerOrigin.Y > supplierOrigin.Y) || (supplierDirection == NodeDirection.Down && consumerOrigin.Y < supplierOrigin.Y)) ? LineType.NShape : LineType.Simple;

                switch (Type) {
                    case LineType.Simple: //supplier and consumer directions are same, link direction is regular
                        if (supplierDirection == NodeDirection.Up) {
                            supplierPull = new Point(supplierOrigin.X, supplierOrigin.Y - Math.Max((supplierOrigin.Y - consumerOrigin.Y) / 2, 20));
                            consumerPull = new Point(consumerOrigin.X, consumerOrigin.Y + Math.Max((supplierOrigin.Y - consumerOrigin.Y) / 2, 20));
                        } else {
                            supplierPull = new Point(supplierOrigin.X, supplierOrigin.Y + Math.Max((consumerOrigin.Y - supplierOrigin.Y) / 2, 20));
                            consumerPull = new Point(consumerOrigin.X, consumerOrigin.Y - Math.Max((consumerOrigin.Y - supplierOrigin.Y) / 2, 20));
                        }

                        CalculatedBounds = new Rectangle(
                            Math.Min(supplierOrigin.X, consumerOrigin.X),
                            Math.Min(supplierOrigin.Y, consumerOrigin.Y),
                            Math.Abs(supplierOrigin.X - consumerOrigin.X),
                            Math.Abs(supplierOrigin.Y - consumerOrigin.Y));

                        break;
                    case LineType.UShape: //supplier and consumer directions are different

                        int xOffset = Math.Min(circlePull * 2, Math.Abs(consumerOrigin.X - supplierOrigin.X)) * Math.Sign(consumerOrigin.X - supplierOrigin.X) / 2;
                        if (supplierDirection == NodeDirection.Up) {
                            midUA = new Point(supplierOrigin.X, Math.Min(supplierOrigin.Y, consumerOrigin.Y));
                            midUB = new Point(midUA.X + xOffset, midUA.Y - circlePull);
                            midUD = new Point(consumerOrigin.X, midUA.Y);
                            midUC = new Point(midUD.X - xOffset, midUB.Y);

                            pullU1 = new Point(supplierOrigin.X, midUA.Y - (circlePull / 2));
                            pullU2 = new Point(supplierOrigin.X + (xOffset / 2), midUB.Y);
                            pullU3 = new Point(consumerOrigin.X - (xOffset / 2), midUB.Y);
                            pullU4 = new Point(consumerOrigin.X, midUD.Y - (circlePull / 2));
                        } else {
                            midUA = new Point(supplierOrigin.X, Math.Max(supplierOrigin.Y, consumerOrigin.Y));
                            midUB = new Point(midUA.X + xOffset, midUA.Y + circlePull);
                            midUD = new Point(consumerOrigin.X, midUA.Y);
                            midUC = new Point(midUD.X - xOffset, midUB.Y);

                            pullU1 = new Point(supplierOrigin.X, midUA.Y + (circlePull / 2));
                            pullU2 = new Point(supplierOrigin.X + (xOffset / 2), midUB.Y);
                            pullU3 = new Point(consumerOrigin.X - (xOffset / 2), midUB.Y);
                            pullU4 = new Point(consumerOrigin.X, midUD.Y + (circlePull / 2));
                        }

                        CalculatedBounds = new Rectangle(
                            Math.Min(supplierOrigin.X, consumerOrigin.X),
                            Math.Min(supplierOrigin.Y, consumerOrigin.Y) - (supplierDirection == NodeDirection.Up ? circlePull : 0),
                            Math.Abs(supplierOrigin.X - consumerOrigin.X),
                            Math.Abs(supplierOrigin.Y - consumerOrigin.Y) + circlePull);
                        break;
                    case LineType.NShape: //supplier and consumer directions are same, but the link direction is wrong
                        int midX = Math.Abs(supplierOrigin.X - consumerOrigin.X) > 2 * circlePull ? (supplierOrigin.X + consumerOrigin.X) / 2 : supplierOrigin.X > consumerOrigin.X ? supplierOrigin.X + (int)(circlePull * 1.5) : supplierOrigin.X - (int)(circlePull * 1.5);
                        int xOffsetA = Math.Min(circlePull * 2, Math.Abs(supplierOrigin.X - midX)) * Math.Sign(midX - supplierOrigin.X) / 2;
                        int xOffsetB = Math.Min(circlePull * 2, Math.Abs(midX - consumerOrigin.X)) * Math.Sign(consumerOrigin.X - midX) / 2;

                        midNC = new Point(midX, supplierOrigin.Y);
                        midND = new Point(midX, consumerOrigin.Y);

                        if (supplierDirection == NodeDirection.Up) {
                            midNA = new Point(supplierOrigin.X + xOffsetA, supplierOrigin.Y - circlePull);
                            midNB = new Point(midNC.X - xOffsetA, midNA.Y);

                            midNE = new Point(midND.X + xOffsetB, consumerOrigin.Y + circlePull);
                            midNF = new Point(consumerOrigin.X - xOffsetB, midNE.Y);

                            pullN1 = new Point(supplierOrigin.X, supplierOrigin.Y - (circlePull / 2));
                            pullN2 = new Point(supplierOrigin.X + (xOffsetA / 2), midNA.Y);
                            pullN3 = new Point(midNC.X - (xOffsetA / 2), midNA.Y);
                            pullN4 = new Point(midNC.X, pullN1.Y);
                            pullN5 = new Point(midNC.X, consumerOrigin.Y + (circlePull / 2));
                            pullN6 = new Point(midNC.X + (xOffsetB / 2), midNE.Y);
                            pullN7 = new Point(consumerOrigin.X - (xOffsetB / 2), midNE.Y);
                            pullN8 = new Point(consumerOrigin.X, pullN5.Y);
                        } else {
                            midNA = new Point(supplierOrigin.X + xOffsetA, supplierOrigin.Y + circlePull);
                            midNB = new Point(midNC.X - xOffsetA, midNA.Y);

                            midNE = new Point(midND.X + xOffsetB, consumerOrigin.Y - circlePull);
                            midNF = new Point(consumerOrigin.X - xOffsetB, midNE.Y);

                            pullN1 = new Point(supplierOrigin.X, supplierOrigin.Y + (circlePull / 2));
                            pullN2 = new Point(supplierOrigin.X + (xOffsetA / 2), midNA.Y);
                            pullN3 = new Point(midNC.X - (xOffsetA / 2), midNA.Y);
                            pullN4 = new Point(midNC.X, pullN1.Y);
                            pullN5 = new Point(midNC.X, consumerOrigin.Y - (circlePull / 2));
                            pullN6 = new Point(midNC.X + (xOffsetB / 2), midNE.Y);
                            pullN7 = new Point(consumerOrigin.X - (xOffsetB / 2), midNE.Y);
                            pullN8 = new Point(consumerOrigin.X, pullN5.Y);
                        }

                        CalculatedBounds = new Rectangle(
                            Math.Min(Math.Min(midX, supplierOrigin.X), consumerOrigin.X),
                            Math.Min(supplierOrigin.Y, consumerOrigin.Y) - circlePull,
                            Math.Max(Math.Max(midX, supplierOrigin.X), consumerOrigin.X) - Math.Min(Math.Min(midX, supplierOrigin.X), consumerOrigin.X),
                            Math.Abs(supplierOrigin.Y - consumerOrigin.Y) + (2 * circlePull));
                        break;
                }

                ComputeCustomRoute(corner);
            }
        }

        private SKRect NodeBounds(BaseNodeElement? node, Point fallback) {
            if (node is null)
                return SKRect.Create(fallback.X, fallback.Y, 0, 0);
            float width = iconOnlyDraw ? Context.IconsDrawSize : node.Width, height = iconOnlyDraw ? Context.IconsDrawSize : node.Height;
            return SKRect.Create(node.Location.X - (width / 2f), node.Location.Y - (height / 2f), width, height);
        }

        private void ComputeCustomRoute(float corner) {
            orthogonalPoints = null;
            orthogonalSegments = null;
            waypointCubics = null;
            hasCustomRoute = routedRouting == LinkRouting.Orthogonal || routedWaypoints.Count > 0;
            if (!hasCustomRoute)
                return;

            IEnumerable<SKPoint> extent;
            if (routedRouting == LinkRouting.Orthogonal) {
                orthogonalPoints = LinkRouter.OrthogonalPoints(supplierOrigin, supplierDirection, consumerOrigin, consumerDirection, routedWaypoints, TokenBridge.Current.LinkStub, routedSupplierBounds, routedConsumerBounds);
                orthogonalSegments = LinkRouter.OrthogonalSegments(orthogonalPoints, corner);
                arrowAnchor = orthogonalPoints[^2];
                extent = orthogonalPoints;
            } else {
                waypointCubics = LinkRouter.CatmullRom(supplierOrigin, supplierDirection, consumerOrigin, consumerDirection, routedWaypoints);
                arrowAnchor = waypointCubics[^1].C2;
                var hull = new List<SKPoint> { new(supplierOrigin.X, supplierOrigin.Y) };
                foreach (CubicSegment cubic in waypointCubics) {
                    hull.Add(cubic.C1);
                    hull.Add(cubic.C2);
                    hull.Add(cubic.To);
                }
                extent = hull;
            }

            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            foreach (SKPoint point in extent) {
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
            CalculatedBounds = new Rectangle((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right - left), (int)Math.Ceiling(bottom - top));
        }

        public IReadOnlyList<SKPoint> Polyline {
            get {
                UpdateCurve();
                SKPath path = GraphicsStuff.RentPath();
                return flattened ??= LinkRouter.Flatten(FillPath(path, out _));
            }
        }

        public IReadOnlyList<RouteSegment> PathSegments() {
            UpdateCurve();
            if (orthogonalSegments is not null)
                return orthogonalSegments;

            IReadOnlyList<SKPoint> line = Polyline;
            var segments = new List<RouteSegment>(Math.Max(line.Count - 1, 0));
            for (int i = 0; i < line.Count - 1; i++)
                segments.Add(new RouteSegment(line[i], line[i + 1], 0));
            return segments;
        }

        private SKPath FillPath(SKPath path, out SKPoint arrowTangentAnchor) {
            if (orthogonalPoints is not null) {
                arrowTangentAnchor = arrowAnchor;
                return LinkRouter.ToPath(path, orthogonalPoints, routedCorner);
            }
            if (waypointCubics is not null) {
                arrowTangentAnchor = arrowAnchor;
                return LinkRouter.ToPath(path, new SKPoint(supplierOrigin.X, supplierOrigin.Y), waypointCubics);
            }

            arrowTangentAnchor = new SKPoint(consumerOrigin.X, consumerOrigin.Y);
            switch (Type) {
                case LineType.Simple:
                    path.MoveTo(supplierOrigin.X, supplierOrigin.Y);
                    CubicTo(path, supplierPull, consumerPull, consumerOrigin);
                    arrowTangentAnchor = new SKPoint(consumerPull.X, consumerPull.Y);
                    break;
                case LineType.UShape:
                    //The 1st/3rd/5th segments below have c1==p0 and c2==p3 - a cubic with both control points
                    //collapsed onto its endpoints is geometrically just the straight line between them, so
                    //they're emitted as LineTo. This is required, not just an optimization: a leading or
                    //trailing segment whose 4 control points all coincide gives Skia's stroker an undefined
                    //start/end tangent, and it silently skips the round cap there (verified empirically -
                    //GDI+'s DrawBeziers doesn't have this failure mode, so upstream draws all 5 as beziers).
                    path.MoveTo(supplierOrigin.X, supplierOrigin.Y);
                    path.LineTo(midUA.X, midUA.Y);
                    CubicTo(path, pullU1, pullU2, midUB);
                    path.LineTo(midUC.X, midUC.Y);
                    CubicTo(path, pullU3, pullU4, midUD);
                    path.LineTo(consumerOrigin.X, consumerOrigin.Y);
                    arrowTangentAnchor = new SKPoint(midUD.X, midUD.Y);
                    break;
                case LineType.NShape:
                    //2nd/4th/6th segments are the same collapsed-control case as UShape above.
                    path.MoveTo(supplierOrigin.X, supplierOrigin.Y);
                    CubicTo(path, pullN1, pullN2, midNA);
                    path.LineTo(midNB.X, midNB.Y);
                    CubicTo(path, pullN3, pullN4, midNC);
                    path.LineTo(midND.X, midND.Y);
                    CubicTo(path, pullN5, pullN6, midNE);
                    path.LineTo(midNF.X, midNF.Y);
                    CubicTo(path, pullN7, pullN8, consumerOrigin);
                    arrowTangentAnchor = new SKPoint(pullN8.X, pullN8.Y);
                    break;
            }
            return path;
        }

        public override bool ContainsPoint(Point graphPoint) {
            return false;
        }

        public override void Dispose() {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
            if (Item.Item is null)
                return;
            iconOnlyDraw = (style == NodeDrawingStyle.IconsOnly);
            UpdateCurve();

            CanvasTokens tokens = TokenBridge.Current;
            bool undersupplied = IsUndersupplied;
            SKColor color = StrokeColor(tokens, undersupplied);
            bool drawArrow = Context.ArrowsOnLinks && !Context.DynamicLinkWidth && !iconOnlyDraw;

            SKPath path = GraphicsStuff.RentPath();
            FillPath(path, out SKPoint arrowTangentAnchor);

            if (IsHaloShown)
                canvas.DrawPath(path, ConfigureHalo(GraphicsStuff.RentStrokePaint(color, LinkWidth, SKStrokeCap.Round), color));
            if (IsCasingShown)
                canvas.DrawPath(path, ConfigureCasing(GraphicsStuff.RentStrokePaint(color, LinkWidth, SKStrokeCap.Round), tokens));

            SKPaint strokePaint = ConfigureStroke(GraphicsStuff.RentStrokePaint(color, LinkWidth, SKStrokeCap.Round), color, undersupplied);
            canvas.DrawPath(path, strokePaint);
            strokePaint.PathEffect = null;

            if (drawArrow)
                GraphicsStuff.DrawArrowHead(canvas, consumerOrigin, new Point((int)arrowTangentAnchor.X, (int)arrowTangentAnchor.Y), WithOpacity(color), LinkWidth, ArrowCapWidthMultiplier, ArrowCapHeightMultiplier);

            DrawWaypoints(canvas, tokens);
        }

        private static readonly SKPathEffect UndersuppliedDash = SKPathEffect.CreateDash([6f, 4f], 0f);

        private SKColor StrokeColor(CanvasTokens tokens, bool undersupplied) =>
            undersupplied ? tokens.Error : ToSkColor(Item.Item!.AverageColor);

        private SKColor WithOpacity(SKColor color) => StrokeOpacity >= 1f ? color : color.WithAlpha((byte)Math.Round(color.Alpha * StrokeOpacity, MidpointRounding.AwayFromZero));

        private SKPaint ConfigureStroke(SKPaint paint, SKColor color, bool undersupplied) {
            paint.Color = WithOpacity(color);
            paint.StrokeWidth = LinkWidth;
            paint.PathEffect = undersupplied ? UndersuppliedDash : null;
            return paint;
        }

        private SKPaint ConfigureHalo(SKPaint paint, SKColor color) {
            paint.Color = color.WithAlpha((byte)Math.Round(color.Alpha * 0.3f, MidpointRounding.AwayFromZero));
            paint.StrokeWidth = LinkWidth + 4f;
            paint.PathEffect = null;
            return paint;
        }

        private SKPaint ConfigureCasing(SKPaint paint, CanvasTokens tokens) {
            paint.Color = tokens.NodeSelected;
            paint.StrokeWidth = LinkWidth + 4f;
            paint.PathEffect = null;
            return paint;
        }

        public SKPaint StrokePaint => ConfigureStroke(NewStrokePaint(), StrokeColor(TokenBridge.Current, IsUndersupplied), IsUndersupplied);

        public SKPaint CasingPaint => ConfigureCasing(NewStrokePaint(), TokenBridge.Current);

        public SKPaint HaloPaint => ConfigureHalo(NewStrokePaint(), StrokeColor(TokenBridge.Current, IsUndersupplied));

        private static SKPaint NewStrokePaint() => new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };

        private static void CubicTo(SKPath path, Point c1, Point c2, Point p3) {
            path.CubicTo(c1.X, c1.Y, c2.X, c2.Y, p3.X, p3.Y);
        }

        private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);
    }
}
