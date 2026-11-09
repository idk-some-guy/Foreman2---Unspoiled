using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    public class InspectorGroup : HeaderedContentControl {
        protected override Type StyleKeyOverride => typeof(InspectorGroup);

        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<InspectorGroup, bool>(nameof(IsExpanded), true);

        public bool IsExpanded {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        private Border? headerRow;
        private Path? triangle;
        private ContentPresenter? contentHost;
        private bool bodyRealized;
        private double inset = 4;
        private double groupGap = 12;
        private EventHandler<PointerPressedEventArgs>? headerPressed;
        private EventHandler<KeyEventArgs>? headerKeyDown;
        private readonly List<IDisposable> subs = [];

        static InspectorGroup() {
            IsExpandedProperty.Changed.AddClassHandler<InspectorGroup>((g, _) => g.ApplyExpansion());
            ContentProperty.Changed.AddClassHandler<InspectorGroup>((g, _) => g.ApplyContent());
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new InspectorGroupAutomationPeer(this);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            if (this.GetVisualAncestors().OfType<InspectorGroup>().Any())
                throw new InvalidOperationException("InspectorGroup: one nesting level only - a group may not contain another group.");
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (headerRow is not null) {
                if (headerPressed is not null)
                    headerRow.PointerPressed -= headerPressed;
                if (headerKeyDown is not null)
                    headerRow.KeyDown -= headerKeyDown;
            }

            headerRow = e.NameScope.Find<Border>("PART_HeaderRow");
            triangle = e.NameScope.Find<Path>("PART_Triangle");
            contentHost = e.NameScope.Find<ContentPresenter>("PART_ContentHost");

            if (headerRow is not null) {
                headerPressed = (_, args) => { if (IsEnabled && args.GetCurrentPoint(headerRow).Properties.IsLeftButtonPressed) IsExpanded = !IsExpanded; };
                headerKeyDown = (_, args) => HandleHeaderKey(args);
                headerRow.PointerPressed += headerPressed;
                headerRow.KeyDown += headerKeyDown;
            }

            ApplyRotation();
            ApplyExpansion();
            ApplyInset();
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Space.Space1").Subscribe(new ResourceSinks.DoubleSink(v => {
                inset = v;
                ApplyInset();
            }, inset)));
            subs.Add(this.GetResourceObservable("Foreman.Space.Space3").Subscribe(new ResourceSinks.DoubleSink(v => {
                groupGap = v;
                ApplyInset();
            }, groupGap)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void HandleHeaderKey(KeyEventArgs e) {
            switch (e.Key) {
                case Key.Space:
                case Key.Enter:
                    IsExpanded = !IsExpanded;
                    e.Handled = true;
                    break;
                case Key.Left:
                    IsExpanded = false;
                    e.Handled = true;
                    break;
                case Key.Right:
                    IsExpanded = true;
                    e.Handled = true;
                    break;
                case Key.Up:
                    FocusSibling(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    FocusSibling(1);
                    e.Handled = true;
                    break;
            }
        }

        private void FocusSibling(int direction) {
            if (this.GetVisualParent() is not Control parent)
                return;
            List<InspectorGroup> groups = [.. parent.GetVisualChildren().OfType<InspectorGroup>()];
            int index = groups.IndexOf(this);
            if (index < 0)
                return;
            int next = index + direction;
            if (next < 0 || next >= groups.Count)
                return;
            groups[next].headerRow?.Focus(NavigationMethod.Directional);
        }

        private void ApplyRotation() {
            if (triangle is null)
                return;
            triangle.RenderTransform = TransformOperations.Parse(IsExpanded ? "rotate(90deg)" : "rotate(0deg)");
        }

        private void ApplyExpansion() {
            ApplyRotation();
            if (contentHost is null)
                return;
            if (IsExpanded) {
                if (!bodyRealized) {
                    SetContent(Content);
                    bodyRealized = true;
                }
                contentHost.IsVisible = true;
            } else {
                contentHost.IsVisible = false;
            }
        }

        private void ApplyContent() {
            if (contentHost is not null && bodyRealized)
                SetContent(Content);
        }

        private void SetContent(object? content) {
            if (contentHost!.Content is FormGroup outgoing)
                outgoing.TrailingGap = null;
            contentHost.Content = content;
            if (content is FormGroup incoming)
                incoming.TrailingGap = 0;
        }

        private void ApplyInset() {
            if (contentHost is null)
                return;
            contentHost.Margin = new Thickness(inset, inset, 0, groupGap);
        }

        private sealed class InspectorGroupAutomationPeer(InspectorGroup owner) : ControlAutomationPeer(owner), IExpandCollapseProvider {
            private InspectorGroup Group => (InspectorGroup)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string GetClassNameCore() => "InspectorGroup";

            ExpandCollapseState IExpandCollapseProvider.ExpandCollapseState =>
                Group.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

            bool IExpandCollapseProvider.ShowsMenu => false;

            void IExpandCollapseProvider.Expand() => Group.IsExpanded = true;
            void IExpandCollapseProvider.Collapse() => Group.IsExpanded = false;
        }
    }
}
