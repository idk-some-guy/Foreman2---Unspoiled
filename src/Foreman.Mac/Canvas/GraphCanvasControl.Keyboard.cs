using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Foreman.Mac.Canvas.Automation;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Composites;
using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaPoint = Avalonia.Point;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.Canvas {
    public sealed partial class GraphCanvasControl {
        public BaseNodeElement? FocusedNode { get; private set; }
        public LinkElement? FocusedLink { get; private set; }

        internal event EventHandler<string>? Announced;
        internal event EventHandler<BaseNodeElement>? InspectorRequested;

        internal static double FindPanMargin {
            get {
                Application app = Application.Current!;
                return app.TryGetResource("Foreman.Metrics.FindPanMargin", app.ActualThemeVariant, out object? value) && value is double margin
                    ? margin
                    : throw new InvalidOperationException("Foreman.Metrics.FindPanMargin is not defined.");
            }
        }

        internal StatePanel EmptyState => emptyState;
        internal Button PausedCapsuleButton => pausedCapsule;

        private readonly StatePanel emptyState;
        private BaseNodeElement? linkOwner;

        protected override AutomationPeer OnCreateAutomationPeer() => new CanvasAutomationPeer(this);

        private StatePanel BuildEmptyState() {
            var addItem = new Button { Content = "Add Item…", Theme = LooseKitTheme("Kit.Button") };
            var addRecipe = new Button { Content = "Add Recipe…", Theme = LooseKitTheme("Kit.Button") };
            ButtonRow.SetRole(addRecipe, ButtonRowRole.Default);
            addItem.Click += (_, _) => AddItemAsync(ViewportCentre());
            addRecipe.Click += (_, _) => AddRecipeAsync(ViewportCentre());
            return new StatePanel {
                Theme = LooseKitTheme("Kit.StatePanel"),
                Shape = StatePanelShape.Empty,
                Headline = "This graph is empty",
                Sentence = "Add an item or a recipe to begin, or double-click anywhere on the canvas.",
                Buttons = new ButtonRow { Theme = LooseKitTheme("Kit.ButtonRow"), Items = { addItem, addRecipe } },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
        }

        private static Avalonia.Styling.ControlTheme? LooseKitTheme(string key) {
            object? theme = null;
            Application.Current?.TryGetResource(key, null, out theme);
            return theme as Avalonia.Styling.ControlTheme;
        }

        private DrawingPoint ViewportCentre() => Viewport.ScreenToGraph(new AvaloniaPoint(Viewport.Width / 2, Viewport.Height / 2));

        private void OnElementsChanged(object? sender, EventArgs e) {
            emptyState.IsVisible = NodeElements.Count == 0;
            IsTabStop = NodeElements.Count > 0;
            ValidateFocus();
            RequestRedraw();
            (ControlAutomationPeer.FromElement(this) as CanvasAutomationPeer)?.NotifyChildrenChanged();
        }

        internal void Announce(string text) => Announced?.Invoke(this, text);

        private BaseNodeElement? ValidFocusedNode() {
            if (FocusedNode is not null && !NodeElements.Contains(FocusedNode))
                FocusedNode = null;
            return FocusedNode;
        }

        private void ValidateFocus() {
            BaseNodeElement? node = ValidFocusedNode();
            if (FocusedLink is not null && !LinkElements.Contains(FocusedLink)) {
                FocusedLink = null;
                if (node is not null)
                    node.ShowsKeyboardFocus = IsKeyboardFocusWithin;
            }
            if (node is null)
                linkOwner = null;
        }

        internal void FocusNode(BaseNodeElement node, bool showRing = true) {
            ClearFocusMarks();
            FocusedNode = node;
            linkOwner = null;
            node.ShowsKeyboardFocus = showRing;
            RequestRedraw();
        }

        internal void FocusLink(LinkElement link, BaseNodeElement owner) {
            ClearFocusMarks();
            FocusedNode = owner;
            linkOwner = owner;
            FocusedLink = link;
            link.IsFocused = true;
            Viewer.SelectLink(link, additive: false);
            RequestRedraw();
        }

        private void ClearFocusMarks() {
            if (FocusedNode is not null)
                FocusedNode.ShowsKeyboardFocus = false;
            if (FocusedLink is not null)
                FocusedLink.IsFocused = false;
            FocusedLink = null;
        }

        internal void HideKeyboardFocus() {
            foreach (BaseNodeElement node in NodeElements)
                node.ShowsKeyboardFocus = false;
            if (FocusedLink is not null)
                FocusedLink.IsFocused = false;
            FocusedLink = null;
            linkOwner = null;
        }

        internal void SelectAndFocus(BaseNodeElement node) {
            Viewer.SetSelection([node]);
            FocusNode(node);
            PanIntoView(CanvasTraversal.WorldBounds(node), FindPanMargin);
        }

        internal void RestoreKeyboardFocus() => Focus(NavigationMethod.Tab);

        protected override void OnGotFocus(FocusChangedEventArgs e) {
            base.OnGotFocus(e);
            if (!ReferenceEquals(e.Source, this) || e.NavigationMethod is not (NavigationMethod.Tab or NavigationMethod.Directional) || NodeElements.Count == 0)
                return;
            BaseNodeElement node = ValidFocusedNode() ?? CanvasTraversal.ReadingOrder(NodeElements)[0];
            FocusNode(node);
            PanIntoView(CanvasTraversal.WorldBounds(node), FindPanMargin);
        }

        protected override void OnLostFocus(FocusChangedEventArgs e) {
            base.OnLostFocus(e);
            if (!IsKeyboardFocusWithin)
                HideKeyboardFocus();
        }

        private bool HandleNavigationKey(KeyEventArgs e) {
            KeyModifiers modifiers = e.KeyModifiers;
            bool shift = modifiers.HasFlag(KeyModifiers.Shift);
            bool alt = modifiers.HasFlag(KeyModifiers.Alt);
            bool command = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
            if (command)
                return false;

            if (e.Key == Key.Escape && !alt)
                return EscapeFocus();
            if (!ReferenceEquals(e.Source, this))
                return false;
            if (NodeElements.Count == 0)
                return false;

            switch (e.Key) {
                case Key.Tab when alt:
                    StepLink(!shift);
                    return true;
                case Key.Tab:
                    StepNode(!shift);
                    return true;
                case Key.Left when alt: MoveSpatial(Direction.Left, shift); return true;
                case Key.Right when alt: MoveSpatial(Direction.Right, shift); return true;
                case Key.Up when alt: MoveSpatial(Direction.Up, shift); return true;
                case Key.Down when alt: MoveSpatial(Direction.Down, shift); return true;
                case Key.Return or Key.Enter when !alt && !shift:
                    return InvokeFocusedNode();
                default:
                    return false;
            }
        }

        private BaseNodeElement? Anchor() =>
            ValidFocusedNode() ?? CanvasTraversal.ReadingOrder(NodeElements).FirstOrDefault(Viewer.SelectedNodes.Contains);

        internal void StepNode(bool forward) {
            List<BaseNodeElement> order = CanvasTraversal.ReadingOrder(NodeElements);
            if (order.Count == 0)
                return;
            BaseNodeElement? current = Anchor();
            int index = current is null ? -1 : order.IndexOf(current);
            int next;
            bool wrapped = false;
            if (index < 0)
                next = forward ? 0 : order.Count - 1;
            else {
                next = index + (forward ? 1 : -1);
                wrapped = next < 0 || next >= order.Count;
                next = (next + order.Count) % order.Count;
            }
            SelectAndFocus(order[next]);
            if (wrapped)
                Announce(forward ? "Wrapped to first node." : "Wrapped to last node.");
        }

        internal void MoveSpatial(Direction direction, bool extend) {
            if (Anchor() is not BaseNodeElement from)
                return;
            if (CanvasTraversal.Spatial(from, direction, NodeElements) is not BaseNodeElement target)
                return;
            Viewer.SetSelection(extend ? [.. Viewer.SelectedNodes, from, target] : [target]);
            FocusNode(target);
            PanIntoView(CanvasTraversal.WorldBounds(target), FindPanMargin);
        }

        private void StepLink(bool forward) {
            BaseNodeElement? owner = linkOwner ?? ValidFocusedNode();
            if (owner is null)
                return;
            List<LinkElement> links = CanvasTraversal.LinksOf(owner, LinkElements);
            if (links.Count == 0)
                return;
            int index = FocusedLink is null ? -1 : links.IndexOf(FocusedLink);
            int next = index < 0 ? (forward ? 0 : links.Count - 1) : (index + (forward ? 1 : -1) + links.Count) % links.Count;
            FocusLink(links[next], owner);
        }

        private bool EscapeFocus() {
            if (FocusedLink is not null && linkOwner is BaseNodeElement owner && NodeElements.Contains(owner)) {
                SelectAndFocus(owner);
                return true;
            }
            bool hadState = Viewer.SelectedNodes.Count > 0 || Viewer.SelectedLinks.Count > 0 || Viewer.SelectedAnnotations.Count > 0 || NodeElements.Any(n => n.ShowsKeyboardFocus);
            if (!hadState)
                return false;
            Viewer.ClearSelection();
            HideKeyboardFocus();
            RequestRedraw();
            return true;
        }

        private bool InvokeFocusedNode() {
            if (Anchor() is not BaseNodeElement node)
                return false;
            InvokeNode(node);
            return true;
        }

        internal void InvokeNode(BaseNodeElement node) {
            if (!Viewer.SelectedNodes.Contains(node))
                Viewer.SetSelection([node]);
            InspectorRequested?.Invoke(this, node);
        }

        internal void ShowContextMenuFor(BaseNodeElement node) => ShowContextMenu(node.MouseUpRight(node.Location));

        internal void ShowContextMenuFor(LinkElement link) {
            if (!Viewer.SelectedLinks.Contains(link))
                Viewer.SelectLink(link, additive: false);
            ShowContextMenu(BuildLinkMenu(link, link.CalculatedBounds.Location));
        }
    }
}
