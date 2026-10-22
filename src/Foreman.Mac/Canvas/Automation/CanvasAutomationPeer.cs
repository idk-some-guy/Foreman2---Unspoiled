using Avalonia;
using AvaloniaPoint = Avalonia.Point;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Foreman.Mac.Canvas.Elements;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Canvas.Automation {
    internal sealed class CanvasAutomationPeer(GraphCanvasControl canvas) : ControlAutomationPeer(canvas), ISelectionProvider {
        private readonly Dictionary<BaseNodeElement, NodeAutomationPeer> nodePeers = [];
        private readonly Dictionary<LinkElement, LinkAutomationPeer> linkPeers = [];

        internal GraphCanvasControl Canvas => canvas;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string? GetNameCore() => "Graph canvas";
        protected override string GetClassNameCore() => "GraphCanvas";

        protected override string? GetHelpTextCore() {
            int nodes = canvas.NodeElements.Count;
            int links = canvas.LinkElements.Count;
            return $"{nodes} {(nodes == 1 ? "node" : "nodes")}, {links} {(links == 1 ? "link" : "links")}{canvas.Viewer.PausedValueSuffix}";
        }

        public bool CanSelectMultiple => true;
        public bool IsSelectionRequired => false;

        public IReadOnlyList<AutomationPeer> GetSelection() =>
            [.. GetOrCreateChildrenCore().OfType<NodeAutomationPeer>().Where(p => p.IsSelected), .. GetOrCreateChildrenCore().OfType<LinkAutomationPeer>().Where(p => p.IsSelected)];

        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() {
            List<BaseNodeElement> nodes = CanvasTraversal.ReadingOrder(canvas.NodeElements);
            List<LinkElement> links = CanvasTraversal.LinkOrder(nodes, canvas.LinkElements);
            foreach (BaseNodeElement stale in nodePeers.Keys.Except(nodes).ToList())
                nodePeers.Remove(stale);
            foreach (LinkElement stale in linkPeers.Keys.Except(links).ToList())
                linkPeers.Remove(stale);

            var children = new List<AutomationPeer>(nodes.Count + links.Count);
            foreach (BaseNodeElement node in nodes) {
                if (!nodePeers.TryGetValue(node, out NodeAutomationPeer? peer))
                    nodePeers[node] = peer = new NodeAutomationPeer(this, node);
                children.Add(peer);
            }
            foreach (LinkElement link in links) {
                if (!linkPeers.TryGetValue(link, out LinkAutomationPeer? peer))
                    linkPeers[link] = peer = new LinkAutomationPeer(this, link);
                children.Add(peer);
            }
            if (canvas.EmptyState.IsVisible)
                children.Add(CreatePeerForElement(canvas.EmptyState));
            if (canvas.PausedCapsuleButton.IsVisible)
                children.Add(CreatePeerForElement(canvas.PausedCapsuleButton));
            return children;
        }

        internal void NotifyChildrenChanged() => RaiseChildrenChangedEvent();

        internal Rect ScreenRect(System.Drawing.Rectangle graphBounds) {
            AvaloniaPoint topLeft = canvas.Viewport.GraphToScreen(graphBounds.Location);
            AvaloniaPoint bottomRight = canvas.Viewport.GraphToScreen(new System.Drawing.Point(graphBounds.Right, graphBounds.Bottom));
            Rect origin = GetBoundingRectangle();
            return new Rect(origin.X + topLeft.X, origin.Y + topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
        }

        internal bool IsOffscreenGraph(System.Drawing.Rectangle graphBounds) {
            AvaloniaPoint topLeft = canvas.Viewport.GraphToScreen(graphBounds.Location);
            AvaloniaPoint bottomRight = canvas.Viewport.GraphToScreen(new System.Drawing.Point(graphBounds.Right, graphBounds.Bottom));
            return bottomRight.X < 0 || bottomRight.Y < 0 || topLeft.X > canvas.Viewport.Width || topLeft.Y > canvas.Viewport.Height;
        }
    }
}
