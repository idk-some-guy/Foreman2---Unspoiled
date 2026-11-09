using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Foreman.Mac.Canvas.Elements;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Canvas.Automation {
    internal sealed class NodeAutomationPeer(CanvasAutomationPeer canvasPeer, BaseNodeElement node) : AutomationPeer, ISelectionItemProvider, IInvokeProvider {
        private AutomationPeer? parent;
        private readonly Dictionary<ItemTabElement, ItemTabAutomationPeer> tabPeers = [];

        internal BaseNodeElement Node => node;
        private GraphCanvasControl Canvas => canvasPeer.Canvas;

        public bool IsSelected => Canvas.Viewer.SelectedNodes.Contains(node);
        public ISelectionProvider? SelectionContainer => canvasPeer;

        public void Select() => Canvas.Viewer.SetSelection([node]);

        public void AddToSelection() => Canvas.Viewer.SetSelection([.. Canvas.Viewer.SelectedNodes, node]);

        public void RemoveFromSelection() => Canvas.Viewer.SetSelection(Canvas.Viewer.SelectedNodes.Where(n => !ReferenceEquals(n, node)));

        public void Invoke() => Canvas.InvokeNode(node);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string? GetNameCore() => node.AutomationName;
        protected override string? GetHelpTextCore() => node.AutomationHelpText;
        protected override string GetClassNameCore() => "GraphNode";
        protected override string? GetAutomationIdCore() => "node-" + node.ViewModel.Id.Value;
        protected override string? GetAcceleratorKeyCore() => null;
        protected override string? GetAccessKeyCore() => null;
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationPeer? GetParentCore() => parent;

        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() {
            IReadOnlyList<ItemTabElement> tabs = node.ItemTabs;
            foreach (ItemTabElement stale in tabPeers.Keys.Except(tabs).ToList())
                tabPeers.Remove(stale);

            var children = new List<AutomationPeer>(tabs.Count);
            foreach (ItemTabElement tab in tabs) {
                if (!tabPeers.TryGetValue(tab, out ItemTabAutomationPeer? peer))
                    tabPeers[tab] = peer = new ItemTabAutomationPeer(canvasPeer, this, tab);
                children.Add(peer);
            }
            return children;
        }
        protected override Rect GetBoundingRectangleCore() => canvasPeer.ScreenRect(CanvasTraversal.WorldBounds(node));
        protected override bool IsOffscreenCore() => canvasPeer.IsOffscreenGraph(CanvasTraversal.WorldBounds(node));
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsEnabledCore() => true;
        protected override bool IsKeyboardFocusableCore() => true;
        protected override bool HasKeyboardFocusCore() => Canvas.IsFocused && ReferenceEquals(Canvas.FocusedNode, node) && Canvas.FocusedLink is null;

        protected override void SetFocusCore() {
            Canvas.Focus();
            Canvas.FocusNode(node);
            BringIntoViewCore();
        }

        protected override void BringIntoViewCore() => Canvas.PanIntoView(CanvasTraversal.WorldBounds(node), GraphCanvasControl.FindPanMargin);

        protected override bool ShowContextMenuCore() {
            Canvas.ShowContextMenuFor(node);
            return true;
        }

        protected override bool TrySetParent(AutomationPeer? newParent) {
            parent = newParent;
            return true;
        }
    }
}
