using Foreman.Models;
using SkiaSharp;
using System;
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
            LinkWidth = 3f;
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

            if (supplierOrigin != endpoints.Item1 || consumerOrigin != endpoints.Item2 || supplierDirection != endpointDirections.Item1 || consumerDirection != endpointDirections.Item2) {
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
            }
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

            SKColor color = ToSkColor(Item.Item.AverageColor);
            bool drawArrow = Context.ArrowsOnLinks && !Context.DynamicLinkWidth && !iconOnlyDraw;
            Point arrowTangentAnchor = consumerOrigin;

            SKPath path = GraphicsStuff.RentPath();
            switch (Type) {
                case LineType.Simple:
                    path.MoveTo(supplierOrigin.X, supplierOrigin.Y);
                    CubicTo(path, supplierPull, consumerPull, consumerOrigin);
                    arrowTangentAnchor = consumerPull;
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
                    arrowTangentAnchor = midUD;
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
                    arrowTangentAnchor = pullN8;
                    break;
            }

            SKPaint strokePaint = GraphicsStuff.RentStrokePaint(color, LinkWidth, SKStrokeCap.Round);
            canvas.DrawPath(path, strokePaint);

            if (drawArrow)
                GraphicsStuff.DrawArrowHead(canvas, consumerOrigin, arrowTangentAnchor, color, LinkWidth, ArrowCapWidthMultiplier, ArrowCapHeightMultiplier);
        }

        private static void CubicTo(SKPath path, Point c1, Point c2, Point p3) {
            path.CubicTo(c1.X, c1.Y, c2.X, c2.Y, p3.X, p3.Y);
        }

        private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);
    }
}
