using Foreman.Graph;
using Foreman.Models;
using System;
using System.Drawing;

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

            LinkWidth = 3f;
            UpdateCurve();
        }

        protected override Tuple<Point, Point> GetCurveEndpoints() {
            return new Tuple<Point, Point>(
                iconOnlyDraw ? SupplierElement!.Location : SupplierTab.GetConnectionPoint(),
                iconOnlyDraw ? ConsumerElement!.Location : ConsumerTab.GetConnectionPoint());
        }

        protected override Tuple<NodeDirection, NodeDirection> GetEndpointDirections() {
            return new Tuple<NodeDirection, NodeDirection>(SupplierElement!.ViewModel.NodeDirection, ConsumerElement!.ViewModel.NodeDirection);
        }
    }
}
