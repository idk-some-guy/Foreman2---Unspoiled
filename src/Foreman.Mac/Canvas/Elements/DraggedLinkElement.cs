using Avalonia.Input;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Models;
using System;
using System.Drawing;
using System.Linq;

namespace Foreman.Mac.Canvas.Elements {
    //Ports ProductionGraphView/Elements/DraggedLinkElement.cs (reference §3): the in-flight link-drag ghost.
    //Reuses BaseLinkElement's bezier Draw/curve math unchanged - only the endpoint resolution (one end bound
    //to a live tab, the other tracking the cursor) and the drag lifecycle (start/track/end/cancel, plus the
    //Cmd+drag passthrough-bus fan-out) are new.
    //
    //Holds a GraphCanvasControl reference rather than NodeElementContext alone (unlike every other P3/P4
    //element in this file): upstream's single ProductionGraphViewer class owns everything a dragged link
    //needs (GetNodeAtPoint, SelectedNodes, Session, Grid, Graph, StartLinkDrag/DisposeLinkDrag/
    //AddPassthroughNodesFromSelection); this port splits that across GraphViewer (session/graph state) and
    //GraphCanvasControl (drag lifecycle, MouseDownElement, modifier tracking), and this element needs facilities
    //from both, so it takes the control that owns the split rather than threading a dozen extra context fields.
    public sealed class DraggedLinkElement : BaseLinkElement {
        public LinkType StartConnectionType { get; }
        public Point EndpointLocation { get; set; }

        private readonly GraphCanvasControl canvas;
        private readonly BaseNodeElement originElement;
        private bool dragEnded;
        private Point lastGraphPoint;

        public DraggedLinkElement(GraphCanvasControl canvas, BaseNodeElement startNode, LinkType startConnectionType, ItemQualityPair item, DraggedLinkElement? masterLink = null)
            : base(canvas.Viewer.Context, masterLink) {
            this.canvas = canvas;
            originElement = startNode;
            if (startConnectionType == LinkType.Input)
                ConsumerElement = startNode;
            else
                SupplierElement = startNode;
            StartConnectionType = startConnectionType;
            Item = item;
        }

        public override void UpdateVisibility(Rectangle graphZone, int xborder = 0, int yborder = 0) => Visible = true;

        public override void PrePaint() {
            UpdateSlaveLinks();
            foreach (DraggedLinkElement slaveLink in SubElements.OfType<DraggedLinkElement>())
                slaveLink.LinkWidth = LinkWidth;
        }

        protected override Tuple<Point, Point>? GetCurveEndpoints() {
            if (dragEnded)
                return null; //freezes the last curve for one frame before disposal

            Point supplierPoint = EndpointLocation;
            Point consumerPoint = EndpointLocation;
            if (SupplierElement is not null)
                supplierPoint = iconOnlyDraw ? SupplierElement.Location : SupplierElement.GetOutputLineItemTab(Item).GetConnectionPoint();
            if (ConsumerElement is not null)
                consumerPoint = iconOnlyDraw ? ConsumerElement.Location : ConsumerElement.GetInputLineItemTab(Item).GetConnectionPoint();

            return new Tuple<Point, Point>(supplierPoint, consumerPoint);
        }

        protected override Tuple<NodeDirection, NodeDirection>? GetEndpointDirections() {
            if (SupplierElement is null) {
                if (ConsumerElement is null)
                    return new Tuple<NodeDirection, NodeDirection>(canvas.Viewer.Graph.DefaultNodeDirection, canvas.Viewer.Graph.DefaultNodeDirection);

                //null-forgiving: a DraggedLinkElement's own GetEndpointDirections never returns null (every
                //branch below produces a Tuple); the base class only declares it nullable for its other,
                //genuinely-optional overrides (LinkElement pre-tab-resolution).
                if (Parent is DraggedLinkElement masterLink) {
                    Tuple<NodeDirection, NodeDirection> masterDirections = masterLink.GetEndpointDirections()!;
                    return masterDirections.Item2 == ConsumerElement.ViewModel.NodeDirection
                        ? masterDirections
                        : new Tuple<NodeDirection, NodeDirection>(masterDirections.Item1 == NodeDirection.Up ? NodeDirection.Down : NodeDirection.Up, ConsumerElement.ViewModel.NodeDirection);
                }

                if (!canvas.SmartNodeDirection)
                    return new Tuple<NodeDirection, NodeDirection>(canvas.Viewer.Graph.DefaultNodeDirection, ConsumerElement.ViewModel.NodeDirection);

                Point consumerPoint = iconOnlyDraw ? ConsumerElement.Location : ConsumerElement.GetInputLineItemTab(Item).GetConnectionPoint();
                return (ConsumerElement.ViewModel.NodeDirection == NodeDirection.Up && consumerPoint.Y > EndpointLocation.Y) || (ConsumerElement.ViewModel.NodeDirection == NodeDirection.Down && consumerPoint.Y < EndpointLocation.Y)
                    ? new Tuple<NodeDirection, NodeDirection>(ConsumerElement.ViewModel.NodeDirection == NodeDirection.Up ? NodeDirection.Down : NodeDirection.Up, ConsumerElement.ViewModel.NodeDirection)
                    : new Tuple<NodeDirection, NodeDirection>(ConsumerElement.ViewModel.NodeDirection, ConsumerElement.ViewModel.NodeDirection);
            }
            if (ConsumerElement is null) {
                if (Parent is DraggedLinkElement masterLinkOut) {
                    Tuple<NodeDirection, NodeDirection> masterDirections = masterLinkOut.GetEndpointDirections()!;
                    return masterDirections.Item1 == SupplierElement.ViewModel.NodeDirection
                        ? masterDirections
                        : new Tuple<NodeDirection, NodeDirection>(SupplierElement.ViewModel.NodeDirection, masterDirections.Item2 == NodeDirection.Up ? NodeDirection.Down : NodeDirection.Up);
                }

                if (!canvas.SmartNodeDirection)
                    return new Tuple<NodeDirection, NodeDirection>(SupplierElement.ViewModel.NodeDirection, canvas.Viewer.Graph.DefaultNodeDirection);

                Point supplierPoint = iconOnlyDraw ? SupplierElement.Location : SupplierElement.GetOutputLineItemTab(Item).GetConnectionPoint();
                return (SupplierElement.ViewModel.NodeDirection == NodeDirection.Up && supplierPoint.Y < EndpointLocation.Y) || (SupplierElement.ViewModel.NodeDirection == NodeDirection.Down && supplierPoint.Y > EndpointLocation.Y)
                    ? new Tuple<NodeDirection, NodeDirection>(SupplierElement.ViewModel.NodeDirection, SupplierElement.ViewModel.NodeDirection == NodeDirection.Up ? NodeDirection.Down : NodeDirection.Up)
                    : new Tuple<NodeDirection, NodeDirection>(SupplierElement.ViewModel.NodeDirection, SupplierElement.ViewModel.NodeDirection);
            }

            return new Tuple<NodeDirection, NodeDirection>(SupplierElement.ViewModel.NodeDirection, ConsumerElement.ViewModel.NodeDirection);
        }

        //Ports EndDrag's three outcomes (reference §3): (a) both ends bound -> real link, (b) unresolved with
        //slave links present -> passthrough-bus drop, (c) unresolved with no slaves -> the P5-chooser stub
        //hook (Task 7 wires the real chooser; records the request and disposes). A "both null" MouseUp/
        //MouseDown is unreachable through this port's own routing (upstream guards it with Trace.Fail), so it
        //falls back to a plain cancel instead of asserting.
        private void EndDrag(Point graphPoint) {
            dragEnded = true;

            if (SupplierElement is not null && ConsumerElement is not null) {
                canvas.Viewer.Session.Editor.CreateLink(SupplierElement.ViewModel.Id, ConsumerElement.ViewModel.Id, Item);
                canvas.Viewer.Graph.UpdateNodeValues();
                canvas.DisposeLinkDrag();
                canvas.RequestRedraw();
            } else if (SubElements.Any(e => e is DraggedLinkElement)) {
                canvas.AddPassthroughNodesFromSelection(StartConnectionType, (Size)Point.Subtract(EndpointLocation, (Size)originElement.Location));
            } else if (StartConnectionType == LinkType.Input && SupplierElement is null) {
                DropUnresolvedEnd(new NewNodeLinkDragRequest(NewNodeType.Supplier, Item, canvas.Viewport.GraphToScreen(graphPoint), EndpointLocation, originElement, ResolvedNewNodeDirection()));
            } else if (StartConnectionType == LinkType.Output && ConsumerElement is null) {
                DropUnresolvedEnd(new NewNodeLinkDragRequest(NewNodeType.Consumer, Item, canvas.Viewport.GraphToScreen(graphPoint), EndpointLocation, originElement, ResolvedNewNodeDirection()));
            } else {
                canvas.DisposeLinkDrag();
            }
        }

        //Ports AddNewNode's "(Control.ModifierKeys & Keys.Control) == Keys.Control" branch (reference §10,
        //upstream lines 227-232): Cmd held at release bypasses the chooser entirely and drops a passthrough
        //node directly, wired to the drag's origin - the same Cmd this port already maps for the slave-link
        //bus fan-out (upstream overloads Ctrl for both purposes off the same DraggedLinkElement).
        private void DropUnresolvedEnd(NewNodeLinkDragRequest request) {
            if (canvas.IsPassthroughBusModifierHeld)
                canvas.CreatePassthroughFromLinkDrag(request);
            else
                canvas.RequestNewNodeFromLinkDrag(request);
        }

        //Ports AddNewNode's SmartNodeDirection branch for the "a link drag is actively in progress" case
        //(reference §8): originElement and this drag are both always present at this call site (EndDrag only
        //reaches the new-node outcome mid-drag), so upstream's originElement==null and draggedLinkElement==null
        //fallbacks never apply here - this collapses to just the two live branches.
        private NodeDirection ResolvedNewNodeDirection() =>
            !canvas.SmartNodeDirection
                ? canvas.Viewer.Graph.DefaultNodeDirection
                : Type != LineType.UShape
                    ? originElement.ViewModel.NodeDirection
                    : originElement.ViewModel.NodeDirection == NodeDirection.Up ? NodeDirection.Down : NodeDirection.Up;

        public void MouseDown(Point graphPoint, MouseButton button) {
            if (button == MouseButton.Left)
                EndDrag(graphPoint);
            else if (button == MouseButton.Right) { //cancel drag-link, no confirmation
                canvas.DisposeLinkDrag();
                canvas.RequestRedraw(); //OnPointerPressed has no trailing invalidate on this path (it's left-button-gated)
            }
        }

        public void MouseUp(Point graphPoint, MouseButton button) {
            if (button == MouseButton.Left)
                EndDrag(graphPoint);
        }

        public void MouseMoved(Point graphPoint) {
            lastGraphPoint = graphPoint;
            if (dragEnded)
                return;

            BaseNodeElement? mousedElement = canvas.GetNodeAtPoint(graphPoint);
            if (mousedElement is not null) {
                if (StartConnectionType == LinkType.Input && mousedElement.ViewModel.Outputs.Contains(Item))
                    SupplierElement = mousedElement;
                else if (StartConnectionType == LinkType.Output && mousedElement.ViewModel.Inputs.Contains(Item))
                    ConsumerElement = mousedElement;

                //a possible connection was just found above, but fails the item temperature check -> break it
                if (SupplierElement is not null && ConsumerElement is not null &&
                    !LinkChecker.IsPossibleConnection(Item, SupplierElement.ViewModel, ConsumerElement.ViewModel, canvas.Viewer.Session)) {
                    if (StartConnectionType == LinkType.Input)
                        SupplierElement = null;
                    else
                        ConsumerElement = null;
                }

                if (SupplierElement is not null && ConsumerElement is not null)
                    foreach (DraggedLinkElement link in SubElements.OfType<DraggedLinkElement>().ToList())
                        link.Dispose();
            } else { //no node under the mouse: break any previously established connection
                if (StartConnectionType == LinkType.Input)
                    SupplierElement = null;
                else
                    ConsumerElement = null;
            }
            UpdateEndpoint();
        }

        //Ports UpdateSlaveLinks' Ctrl+multi-passthrough fan-out (reference §3), upstream Cmd-mapped per this
        //task's plan: fires only while unresolved, Cmd held, no slaves yet, the origin is a passthrough node,
        //and every selected node (including the origin) is a passthrough. Drops the dead
        //"SubElements.Contains(dle) ? null : dle" guard upstream carries here - dle is always in SubElements
        //immediately after construction (its own ctor chain adds it), so that branch never actually disposed
        //anything; this just constructs the slave directly.
        private void UpdateSlaveLinks() {
            if (SupplierElement is not null && ConsumerElement is not null)
                return;

            bool hasSlaves = SubElements.Any(e => e is DraggedLinkElement);
            if (canvas.IsPassthroughBusModifierHeld && !hasSlaves && originElement is PassthroughNodeElement &&
                canvas.Viewer.SelectedNodes.Count > 1 && canvas.Viewer.SelectedNodes.Contains(originElement) &&
                canvas.Viewer.SelectedNodes.All(n => n is PassthroughNodeElement)) {
                foreach (PassthroughNodeElement node in canvas.Viewer.SelectedNodes.OfType<PassthroughNodeElement>().Where(n => n != originElement))
                    _ = new DraggedLinkElement(canvas, node, StartConnectionType, ((IPassthroughNodeViewModel)node.ViewModel).PassthroughItem, this);
            } else if (!canvas.IsPassthroughBusModifierHeld) {
                foreach (DraggedLinkElement link in SubElements.OfType<DraggedLinkElement>().ToList())
                    link.Dispose();
            }
            UpdateEndpoint();
        }

        //lastGraphPoint (updated on every MouseMoved) stands in for upstream's live
        //graphViewer.ScreenToGraph(PointToClient(Cursor.Position)) read - Avalonia has no global cursor-
        //position query, so the endpoint tracks the pointer only across move events rather than every single
        //render frame regardless of movement (every move already triggers the invalidate that leads here).
        private void UpdateEndpoint() {
            EndpointLocation = lastGraphPoint;
            if (canvas.Grid.ShowGrid && canvas.Grid.CurrentGridUnit > 0)
                EndpointLocation = canvas.Grid.AlignToGrid(EndpointLocation);

            foreach (DraggedLinkElement slaveLink in SubElements.OfType<DraggedLinkElement>()) {
                BaseNodeElement? anchor = StartConnectionType == LinkType.Input ? slaveLink.ConsumerElement : slaveLink.SupplierElement;
                if (anchor is not null)
                    slaveLink.EndpointLocation = Point.Add(anchor.Location, (Size)Point.Subtract(EndpointLocation, (Size)originElement.Location));
            }
        }
    }
}
