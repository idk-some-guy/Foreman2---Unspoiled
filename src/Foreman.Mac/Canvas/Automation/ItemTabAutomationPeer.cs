using Avalonia;
using Avalonia.Automation.Peers;
using Foreman.Mac.Canvas.Elements;
using System.Collections.Generic;
using System.Drawing;

namespace Foreman.Mac.Canvas.Automation {
    internal sealed class ItemTabAutomationPeer(CanvasAutomationPeer canvasPeer, NodeAutomationPeer nodePeer, ItemTabElement tab) : AutomationPeer {
        internal ItemTabElement Tab => tab;

        private Rectangle GraphBounds {
            get {
                SkiaSharp.SKRect rect = tab.TabGeometry().Rect;
                return Rectangle.FromLTRB((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom);
            }
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string? GetNameCore() => tab.AutomationName;
        protected override string? GetHelpTextCore() => null;
        protected override string GetClassNameCore() => "GraphNodeTab";
        protected override string? GetAutomationIdCore() => $"node-{nodePeer.Node.ViewModel.Id.Value}-tab-{(tab.LinkType == LinkType.Output ? "output" : "input")}-{tab.Item.Item?.Name}";
        protected override string? GetAcceleratorKeyCore() => null;
        protected override string? GetAccessKeyCore() => null;
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override AutomationPeer? GetParentCore() => nodePeer;
        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];
        protected override Rect GetBoundingRectangleCore() => canvasPeer.ScreenRect(GraphBounds);
        protected override bool IsOffscreenCore() => canvasPeer.IsOffscreenGraph(GraphBounds);
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsEnabledCore() => true;
        protected override bool IsKeyboardFocusableCore() => false;
        protected override bool HasKeyboardFocusCore() => false;
        protected override void SetFocusCore() { }
        protected override void BringIntoViewCore() => nodePeer.BringIntoView();
        protected override bool ShowContextMenuCore() => false;

        protected override bool TrySetParent(AutomationPeer? newParent) => true;
    }
}
