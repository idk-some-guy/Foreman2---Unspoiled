using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Foreman.Mac.Canvas.Elements;
using System.Collections.Generic;

namespace Foreman.Mac.Canvas.Automation {
    internal sealed class LinkAutomationPeer(CanvasAutomationPeer canvasPeer, LinkElement link) : AutomationPeer, ISelectionItemProvider {
        private AutomationPeer? parent;

        internal LinkElement Link => link;
        private GraphCanvasControl Canvas => canvasPeer.Canvas;

        public bool IsSelected => Canvas.Viewer.SelectedLinks.Contains(link);
        public ISelectionProvider? SelectionContainer => canvasPeer;

        public void Select() {
            if (!IsSelected || Canvas.Viewer.SelectedLinks.Count > 1 || Canvas.Viewer.SelectedNodes.Count > 0)
                Canvas.Viewer.SelectLink(link, additive: false);
        }

        public void AddToSelection() {
            if (!IsSelected)
                Canvas.Viewer.SelectLink(link, additive: true);
        }

        public void RemoveFromSelection() {
            if (IsSelected)
                Canvas.Viewer.SelectLink(link, additive: true);
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string? GetNameCore() => link.AutomationName;
        protected override string? GetHelpTextCore() => LinkElement.AutomationHelpText;
        protected override string GetClassNameCore() => "GraphLink";
        protected override string? GetAutomationIdCore() => "link-" + link.ViewModel.Id.Value;
        protected override string? GetAcceleratorKeyCore() => null;
        protected override string? GetAccessKeyCore() => null;
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationPeer? GetParentCore() => parent;
        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];
        protected override Rect GetBoundingRectangleCore() => canvasPeer.ScreenRect(link.CalculatedBounds);
        protected override bool IsOffscreenCore() => canvasPeer.IsOffscreenGraph(link.CalculatedBounds);
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsEnabledCore() => true;
        protected override bool IsKeyboardFocusableCore() => true;
        protected override bool HasKeyboardFocusCore() => Canvas.IsFocused && ReferenceEquals(Canvas.FocusedLink, link);

        protected override void SetFocusCore() {
            Canvas.Focus();
            if (link.SupplierElement is { } owner)
                Canvas.FocusLink(link, owner);
        }

        protected override void BringIntoViewCore() => Canvas.PanIntoView(link.CalculatedBounds, GraphCanvasControl.FindPanMargin);

        protected override bool ShowContextMenuCore() {
            Canvas.ShowContextMenuFor(link);
            return true;
        }

        protected override bool TrySetParent(AutomationPeer? newParent) {
            parent = newParent;
            return true;
        }
    }
}
