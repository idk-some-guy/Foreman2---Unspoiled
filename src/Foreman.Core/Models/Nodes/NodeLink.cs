namespace Foreman.Models.Nodes {
    public class NodeLink {
        private readonly NodeLinkController controller;
        public NodeLinkController Controller { get { return controller; } }

        public ItemQualityPair Item { get; private set; }
        public double ThroughputPerSec { get; internal set; }
        public double Throughput { get { return ThroughputPerSec * MyGraph.GetRateMultipler(); } }
        public bool IsValid { get; private set; }
        public LinkRouting Routing { get; internal set; }
        public System.Collections.Generic.IReadOnlyList<System.Drawing.Point> Waypoints { get; internal set; } = [];

        public ProductionGraph MyGraph { get; }
        public BaseNode SupplierNode { get; }
        public BaseNode ConsumerNode { get; }
        internal NodeLink(ProductionGraph myGraph, BaseNode supplier, BaseNode consumer, ItemQualityPair item, LinkRouting routing) {
            MyGraph = myGraph;
            Routing = routing;
            SupplierNode = supplier;
            ConsumerNode = consumer;
            Item = item;

            controller = new NodeLinkController(this);

            IsValid = LinkChecker.IsPossibleConnection(Item, SupplierNode, ConsumerNode); //only need to check once -> item & recipe temperatures cant change.
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "NodeLink for {0} ({1}) connecting {1} -> {2}", Item.Item?.Name, Item.Quality?.Name, SupplierNode.NodeID, ConsumerNode.NodeID);
    }

    public class NodeLinkController {
        private readonly NodeLink MyLink;

        internal NodeLinkController(NodeLink link) { MyLink = link; }

        public void Delete() => MyLink.MyGraph.DeleteLink(MyLink);

        public void SetRouting(LinkRouting routing) {
            if (MyLink.Routing == routing)
                return;
            MyLink.Routing = routing;
            MyLink.MyGraph.NotifyNodeMutated();
        }

        public void SetWaypoints(System.Collections.Generic.IReadOnlyList<System.Drawing.Point> waypoints) {
            if (System.Linq.Enumerable.SequenceEqual(MyLink.Waypoints, waypoints))
                return;
            MyLink.Waypoints = [.. waypoints];
            MyLink.MyGraph.NotifyNodeMutated();
        }
        public override string ToString() { return "C: " + MyLink.ToString(); }
    }
}
