using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Foreman.Mac.Kit.Composites {
    public enum PopoverEdge { Bottom, Top, Right, Left }

    public enum PopoverFrameMode { Anchored, Flush }

    public readonly record struct PopoverPlacementResult(PopoverEdge Edge, Point FramePosition, double TailOffset, Point RenderOrigin);

    public class PopoverFrame : ContentControl {
        protected override Type StyleKeyOverride => typeof(PopoverFrame);

        public static readonly StyledProperty<Control?> AnchorProperty =
            AvaloniaProperty.Register<PopoverFrame, Control?>(nameof(Anchor));

        public static readonly StyledProperty<bool> IsOpenProperty =
            AvaloniaProperty.Register<PopoverFrame, bool>(nameof(IsOpen));

        public static readonly DirectProperty<PopoverFrame, PopoverEdge> ResolvedPlacementProperty =
            AvaloniaProperty.RegisterDirect<PopoverFrame, PopoverEdge>(nameof(ResolvedPlacement), o => o.resolvedPlacement);

        public static readonly StyledProperty<double> PanelWidthProperty =
            AvaloniaProperty.Register<PopoverFrame, double>(nameof(PanelWidth), double.NaN);

        public static readonly StyledProperty<PopoverFrameMode> ModeProperty =
            AvaloniaProperty.Register<PopoverFrame, PopoverFrameMode>(nameof(Mode));

        public static readonly StyledProperty<Point> AnchorPointProperty =
            AvaloniaProperty.Register<PopoverFrame, Point>(nameof(AnchorPoint));

        public Control? Anchor { get => GetValue(AnchorProperty); set => SetValue(AnchorProperty, value); }
        public PopoverFrameMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
        public Point AnchorPoint { get => GetValue(AnchorPointProperty); set => SetValue(AnchorPointProperty, value); }
        public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
        public double PanelWidth { get => GetValue(PanelWidthProperty); set => SetValue(PanelWidthProperty, value); }
        public PopoverEdge ResolvedPlacement => resolvedPlacement;

        public event EventHandler? Closed;
        public event EventHandler? Cancelled;

        private const double Gap = 6;
        private const double TailClamp = 26;

        private Border? panel;
        private Border? tail;
        private Border? contentClip;
        private PopoverEdge resolvedPlacement = PopoverEdge.Bottom;
        private double tailSize = 12;
        private readonly List<IDisposable> tokenSubs = [];
        private IDisposable? panelSizeSub;

        static PopoverFrame() {
            IsOpenProperty.Changed.AddClassHandler<PopoverFrame>((p, e) => p.ApplyOpenState((bool)e.NewValue!));
            ModeProperty.Changed.AddClassHandler<PopoverFrame>((p, _) => p.ApplyMode());
            AnchorPointProperty.Changed.AddClassHandler<PopoverFrame>((p, _) => p.UpdatePlacement());
            ContentProperty.Changed.AddClassHandler<PopoverFrame>((p, _) => p.ApplyAccessibleName());
        }

        public PopoverFrame() {
            AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            IsHitTestVisible = IsOpen;
            ApplyNavigation(IsOpen);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            panel = e.NameScope.Find<Border>("PART_Panel");
            tail = e.NameScope.Find<Border>("PART_Tail");
            contentClip = e.NameScope.Find<Border>("PART_ContentClip");
            PseudoClasses.Set(":open", IsOpen);
            IsHitTestVisible = IsOpen;
            if (panel is not null) {
                panel.Transitions = null;
                panel.RenderTransform = PanelScale(IsOpen);
            }
            ApplyMode();
            ApplyAccessibleName();
            SubscribePanelSize();
            UpdatePlacement();
        }

        private bool IsFlush => Mode == PopoverFrameMode.Flush;

        //ClipToBounds clips a Border's own drawing too, so the clip sits inside the shadow-carrying panel.
        private void ApplyMode() {
            if (panel is null)
                return;
            PseudoClasses.Set(":flush", IsFlush);
            if (contentClip is not null)
                contentClip.ClipToBounds = IsFlush;
            if (tail is not null)
                tail.IsVisible = !IsFlush;
            ApplyTailGeometry();
            UpdatePlacement();
        }

        private void SubscribePanelSize() {
            panelSizeSub?.Dispose();
            panelSizeSub = panel?.GetObservable(Visual.BoundsProperty).Subscribe(new ChangeObserver<Rect>(_ => UpdatePlacement()));
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Unsubscribe();
            tokenSubs.Add(this.GetResourceObservable("Foreman.Size.PopoverTail").Subscribe(new ResourceSinks.DoubleSink(v => {
                tailSize = v;
                ApplyTailGeometry();
                UpdatePlacement();
            }, tailSize)));
            if (this.FindAncestorOfType<PopoverHost>() is { } host && (Anchor is not null || IsFlush)) {
                if (Anchor is { } anchor)
                    tokenSubs.Add(anchor.GetObservable(Visual.BoundsProperty).Subscribe(new ChangeObserver<Rect>(_ => UpdatePlacement())));
                tokenSubs.Add(host.GetObservable(Visual.BoundsProperty).Subscribe(new ChangeObserver<Rect>(_ => UpdatePlacement())));
                SubscribePanelSize();
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in tokenSubs)
                sub.Dispose();
            tokenSubs.Clear();
            panelSizeSub?.Dispose();
            panelSizeSub = null;
        }

        private void ApplyTailGeometry() {
            if (tail is null || panel is null)
                return;
            double half = IsFlush ? 0 : tailSize / 2;
            panel.Margin = new Thickness(half);
            tail.Width = tailSize;
            tail.Height = tailSize;
            tail.HorizontalAlignment = HorizontalAlignment.Left;
            tail.VerticalAlignment = VerticalAlignment.Top;
        }

        private void ApplyAccessibleName() {
            string? name = Content switch {
                FormGroup group => group.Title,
                Control c => Avalonia.Automation.AutomationProperties.GetName(c),
                _ => null,
            };
            Avalonia.Automation.AutomationProperties.SetName(this, name);
        }

        private void UpdatePlacement() {
            if (panel is null || this.FindAncestorOfType<PopoverHost>() is not { } host)
                return;
            if (IsFlush) {
                Point position = ResolvePointPlacement(AnchorPoint, host.Bounds.Size, panel.DesiredSize);
                Canvas.SetLeft(this, position.X);
                Canvas.SetTop(this, position.Y);
                panel.RenderTransformOrigin = new RelativePoint(AnchorPoint - position, RelativeUnit.Absolute);
                return;
            }
            if (Anchor is not { } anchor)
                return;
            Rect anchorInParent = new(anchor.TranslatePoint(new Point(0, 0), host) ?? default, anchor.Bounds.Size);
            PopoverPlacementResult placement = ResolvePlacement(anchorInParent, host.Bounds.Size, panel.DesiredSize, Gap, TailClamp);
            Canvas.SetLeft(this, placement.FramePosition.X);
            Canvas.SetTop(this, placement.FramePosition.Y);
            double half = tailSize / 2;
            panel.RenderTransformOrigin = new RelativePoint(placement.RenderOrigin - new Point(half, half), RelativeUnit.Absolute);
            SetAndRaise(ResolvedPlacementProperty, ref resolvedPlacement, placement.Edge);
            ApplyTailPlacement(placement);
        }

        private void ApplyTailPlacement(PopoverPlacementResult placement) {
            if (tail is null || panel is null)
                return;
            tail.BorderThickness = placement.Edge switch {
                PopoverEdge.Bottom => new Thickness(1, 1, 0, 0),
                PopoverEdge.Top => new Thickness(0, 0, 1, 1),
                PopoverEdge.Right => new Thickness(1, 0, 0, 1),
                _ => new Thickness(0, 1, 1, 0),
            };
            tail.Margin = placement.Edge switch {
                PopoverEdge.Bottom => new Thickness(placement.TailOffset, 0, 0, 0),
                PopoverEdge.Top => new Thickness(placement.TailOffset, panel.DesiredSize.Height, 0, 0),
                PopoverEdge.Right => new Thickness(0, placement.TailOffset, 0, 0),
                _ => new Thickness(panel.DesiredSize.Width, placement.TailOffset, 0, 0),
            };
        }

        private void ApplyOpenState(bool open) {
            if (open && this.FindAncestorOfType<PopoverHost>() is null)
                throw new InvalidOperationException("PopoverFrame requires a PopoverHost ancestor to open.");

            TimeSpan duration = open ? Motion.PopoverDuration : Motion.PopoverCloseDuration;
            Avalonia.Animation.Easings.Easing easing = open ? Motion.EntranceEasing : Motion.LeavingEasing;
            if (panel is not null) {
                panel.Transitions = Motion.Transition(TransitionKind.Scale, duration, easing);
                panel.RenderTransform = PanelScale(open);
            }
            if (tail is not null)
                tail.Transitions = Motion.Transition(TransitionKind.Fade, duration, easing);

            PseudoClasses.Set(":open", open);
            IsHitTestVisible = open;
            ApplyNavigation(open);
            if (open)
                //Posted, not inline - the frame has not been through its first arrange yet, so Focus() silently no-ops here.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (IsOpen && !IsKeyboardFocusWithin) Focus(); });
            else {
                Anchor?.Focus();
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        private static TransformOperations PanelScale(bool open) {
            double scale = open || Motion.ReduceMotion ? 1 : Motion.PopoverOpenScale;
            return TransformOperations.Parse($"scale({scale.ToString(CultureInfo.InvariantCulture)})");
        }

        private void ApplyNavigation(bool open) {
            Focusable = open;
            KeyboardNavigation.SetIsTabStop(this, open);
            KeyboardNavigation.SetTabNavigation(this, open ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
        }

        internal void Close() => IsOpen = false;

        internal void Cancel() {
            Cancelled?.Invoke(this, EventArgs.Empty);
            Close();
        }

        internal static bool IsCancelKey(KeyEventArgs e) =>
            e.Key == Key.Escape || (e.Key == Key.OemPeriod && e.KeyModifiers == KeyModifiers.Meta);

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (!IsOpen || !IsCancelKey(e))
                return;
            e.Handled = true;
            Cancel();
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new PopoverFrameAutomationPeer(this);

        private sealed class PopoverFrameAutomationPeer(PopoverFrame owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
            protected override string GetLocalizedControlTypeCore() => "popover";
        }

        private sealed class ChangeObserver<T>(Action<T> onNext) : IObserver<T> {
            public void OnNext(T value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        internal static Point ResolvePointPlacement(Point point, Size host, Size frame) {
            double x = point.X + frame.Width <= host.Width ? point.X : point.X - frame.Width;
            double y = point.Y + frame.Height <= host.Height ? point.Y : point.Y - frame.Height;
            return new Point(
                Math.Clamp(x, 0, Math.Max(0, host.Width - frame.Width)),
                Math.Clamp(y, 0, Math.Max(0, host.Height - frame.Height)));
        }

        internal static PopoverPlacementResult ResolvePlacement(Rect anchor, Size window, Size popover, double gap, double tailClamp) {
            double centerX = anchor.X + anchor.Width / 2;
            double centerY = anchor.Y + anchor.Height / 2;

            Rect Below() => new(centerX - popover.Width / 2, anchor.Bottom + gap, popover.Width, popover.Height);
            Rect Above() => new(centerX - popover.Width / 2, anchor.Y - gap - popover.Height, popover.Width, popover.Height);
            Rect Trailing() => new(anchor.Right + gap, centerY - popover.Height / 2, popover.Width, popover.Height);
            Rect Leading() => new(anchor.X - gap - popover.Width, centerY - popover.Height / 2, popover.Width, popover.Height);

            bool Fits(Rect r) => r.X >= 0 && r.Y >= 0 && r.Right <= window.Width && r.Bottom <= window.Height;

            (PopoverEdge edge, Rect rect) chosen =
                Fits(Below()) ? (PopoverEdge.Bottom, Below()) :
                Fits(Above()) ? (PopoverEdge.Top, Above()) :
                Fits(Trailing()) ? (PopoverEdge.Right, Trailing()) :
                Fits(Leading()) ? (PopoverEdge.Left, Leading()) :
                (PopoverEdge.Bottom, Below());

            double clampedX = Math.Clamp(chosen.rect.X, 0, Math.Max(0, window.Width - popover.Width));
            double clampedY = Math.Clamp(chosen.rect.Y, 0, Math.Max(0, window.Height - popover.Height));
            Rect frame = new(clampedX, clampedY, popover.Width, popover.Height);

            double tailOffset;
            if (chosen.edge is PopoverEdge.Bottom or PopoverEdge.Top)
                tailOffset = Math.Clamp(centerX - frame.X, tailClamp, Math.Max(tailClamp, frame.Width - tailClamp));
            else
                tailOffset = Math.Clamp(centerY - frame.Y, tailClamp, Math.Max(tailClamp, frame.Height - tailClamp));

            Point renderOrigin = new(centerX - frame.X, centerY - frame.Y);
            return new PopoverPlacementResult(chosen.edge, frame.Position, tailOffset, renderOrigin);
        }
    }
}
