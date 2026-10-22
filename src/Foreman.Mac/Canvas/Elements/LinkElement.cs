using Foreman.Graph;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/LinkElement.cs in full: a link bound to a live INodeLinkViewModel,
    //resolving its endpoints from the connected nodes' item tabs. supplierElement/consumerElement are
    //non-nullable here (upstream's null guard, backed by Trace.Fail, is redundant under this port's nullable
    //reference types - the compiler already rejects a null argument).
    public sealed class LinkElement : BaseLinkElement {
        public INodeLinkViewModel ViewModel { get; }
        public override ItemQualityPair Item { get => ViewModel.Item; protected set { } }

        public ItemTabElement SupplierTab { get; }
        public ItemTabElement ConsumerTab { get; }

        public bool IsHovered { get; set; }
        public bool IsSelected { get; set; }
        public bool IsFocused { get; set; }
        public int SelectedWaypointIndex { get; set; } = -1;

        public bool AreWaypointsShown => IsHovered || IsSelected || IsFocused;

        private WaypointElement[] waypointElements = [];

        public LinkElement(NodeElementContext context, INodeLinkViewModel viewModel, BaseNodeElement supplierElement, BaseNodeElement consumerElement) : base(context) {
            ViewModel = viewModel;
            SupplierElement = supplierElement;
            ConsumerElement = consumerElement;

            //GetOutputLineItemTab/GetInputLineItemTab throw (via .First) rather than return null, so this
            //check mirrors upstream's own dead code rather than guarding a reachable failure.
            ItemTabElement? supplierTab = supplierElement.GetOutputLineItemTab(Item);
            ItemTabElement? consumerTab = consumerElement.GetInputLineItemTab(Item);
            if (supplierTab is null || consumerTab is null)
                throw new InvalidOperationException($"Link element being created with one of the elements ({supplierElement}, {consumerElement}) not having the required item ({Item})!");
            SupplierTab = supplierTab;
            ConsumerTab = consumerTab;

            UpdateCurve();
        }

        protected override LinkRouting Routing => ViewModel.Routing;
        public IReadOnlyList<Point>? WaypointPreview { get; set; }
        public IReadOnlyList<Point> EffectiveWaypoints => WaypointPreview ?? ViewModel.Waypoints;
        protected override IReadOnlyList<Point> RouteWaypoints => EffectiveWaypoints;

        public void PreviewWaypoint(int index, Point to) {
            IReadOnlyList<Point> current = EffectiveWaypoints;
            if (index < 0 || index >= current.Count || current[index] == to)
                return;
            List<Point> waypoints = [.. current];
            waypoints[index] = to;
            WaypointPreview = waypoints;
        }
        protected override bool IsHaloShown => IsHovered;
        protected override bool IsCasingShown => IsSelected || IsFocused;
        protected override bool IsUndersupplied => ConsumerElement?.FlowUndersupplied == true;

        protected override Tuple<Point, Point> GetCurveEndpoints() {
            return new Tuple<Point, Point>(
                iconOnlyDraw ? SupplierElement!.Location : SupplierTab.GetConnectionPoint(),
                iconOnlyDraw ? ConsumerElement!.Location : ConsumerTab.GetConnectionPoint());
        }

        protected override Tuple<NodeDirection, NodeDirection> GetEndpointDirections() {
            return new Tuple<NodeDirection, NodeDirection>(SupplierElement!.ViewModel.NodeDirection, ConsumerElement!.ViewModel.NodeDirection);
        }

        public IReadOnlyList<WaypointElement> WaypointElements {
            get {
                SyncWaypoints();
                return waypointElements;
            }
        }

        private void SyncWaypoints() {
            IReadOnlyList<Point> points = EffectiveWaypoints;
            if (waypointElements.Length != points.Count) {
                foreach (WaypointElement stale in waypointElements)
                    stale.Dispose();
                waypointElements = [.. points.Select((point, index) => new WaypointElement(this, index, point))];
                if (SelectedWaypointIndex >= points.Count)
                    SelectedWaypointIndex = -1;
                return;
            }

            for (int i = 0; i < waypointElements.Length; i++) {
                if (waypointElements[i].Location != points[i])
                    waypointElements[i].Location = points[i];
            }
        }

        public WaypointElement? WaypointAt(Point graphPoint) =>
            AreWaypointsShown ? WaypointElements.LastOrDefault(waypoint => waypoint.ContainsPoint(graphPoint)) : null;

        protected override void DrawWaypoints(SKCanvas canvas, CanvasTokens tokens) {
            if (!AreWaypointsShown)
                return;
            foreach (WaypointElement waypoint in WaypointElements)
                waypoint.DrawMarker(canvas, tokens);
        }

        public float HitBand => Math.Max(TokenBridge.Current.LinkHitWidth, LinkWidth + 4f);

        public bool HitTest(Point graphPoint) => HitTest(new PointF(graphPoint.X, graphPoint.Y));

        public bool HitTest(PointF graphPoint) {
            UpdateCurve();
            float half = HitBand / 2f;
            Rectangle bounds = CalculatedBounds;
            if (graphPoint.X < bounds.Left - half || graphPoint.X > bounds.Right + half || graphPoint.Y < bounds.Top - half || graphPoint.Y > bounds.Bottom + half)
                return false;

            return LinkRouter.DistanceToPolyline(Polyline, new SKPoint(graphPoint.X, graphPoint.Y)) <= half;
        }

        //The nearest whole graph point to the path, with the index it takes in the waypoint order.
        public Point NearestPointOnPath(PointF graphPoint, out int insertIndex) {
            IReadOnlyList<SKPoint> line = Polyline;
            var target = new SKPoint(graphPoint.X, graphPoint.Y);
            (SKPoint nearest, float along) = LinkRouter.NearestOnPolyline(line, target);

            Point best = new((int)Math.Round(nearest.X), (int)Math.Round(nearest.Y));
            float bestDistance = float.MaxValue;
            foreach (int x in new[] { (int)Math.Floor(nearest.X), (int)Math.Ceiling(nearest.X) }) {
                foreach (int y in new[] { (int)Math.Floor(nearest.Y), (int)Math.Ceiling(nearest.Y) }) {
                    float distance = LinkRouter.DistanceToPolyline(line, new SKPoint(x, y));
                    if (distance < bestDistance) {
                        bestDistance = distance;
                        best = new Point(x, y);
                    }
                }
            }

            insertIndex = ViewModel.Waypoints.Count(waypoint => LinkRouter.AlongLength(line, new SKPoint(waypoint.X, waypoint.Y)) <= along);
            return best;
        }

        public string AutomationName {
            get {
                string unit = NodeCardPainter.UnitWord(Context.RateUnit);
                string name = $"{Item.FriendlyName} link, {SupplierElement!.NodeName} to {ConsumerElement!.NodeName}, {NodeCardPainter.Number(ViewModel.Throughput)} per {unit}, {ViewModel.Routing}";
                int waypoints = ViewModel.Waypoints.Count;
                if (waypoints > 0)
                    name += waypoints == 1 ? ", 1 waypoint" : $", {waypoints} waypoints";
                return IsUndersupplied ? name + ", short of supply" : name;
            }
        }

        public static string AutomationHelpText => "Actions: Delete, Rounded, Orthogonal, Clear Waypoints";

        public override void Dispose() {
            foreach (WaypointElement waypoint in waypointElements)
                waypoint.Dispose();
            waypointElements = [];
            base.Dispose();
        }
    }
}
