using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Composites {
    public enum PopoverEdge { Bottom, Top, Right, Left }

    //Where the frame landed and how its tail should sit - computed by the pure ResolvePlacement.
    public readonly record struct PopoverPlacementResult(PopoverEdge Edge, Point FramePosition, double TailOffset, Point RenderOrigin);

    //Translucent popover chrome with a tail that tracks its anchor; requires a PopoverHost ancestor to clamp against and to close it on an outside press.
    public class PopoverFrame : ContentControl {
        protected override Type StyleKeyOverride => typeof(PopoverFrame);

        public static readonly StyledProperty<Control?> AnchorProperty =
            AvaloniaProperty.Register<PopoverFrame, Control?>(nameof(Anchor));

        public static readonly StyledProperty<bool> IsOpenProperty =
            AvaloniaProperty.Register<PopoverFrame, bool>(nameof(IsOpen));

        public static readonly DirectProperty<PopoverFrame, PopoverEdge> ResolvedPlacementProperty =
            AvaloniaProperty.RegisterDirect<PopoverFrame, PopoverEdge>(nameof(ResolvedPlacement), o => o.resolvedPlacement);

        //NaN sizes the panel to its content; a consumer that wants a fixed width (e.g. View Options) sets this.
        public static readonly StyledProperty<double> PanelWidthProperty =
            AvaloniaProperty.Register<PopoverFrame, double>(nameof(PanelWidth), double.NaN);

        public Control? Anchor { get => GetValue(AnchorProperty); set => SetValue(AnchorProperty, value); }
        public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
        public double PanelWidth { get => GetValue(PanelWidthProperty); set => SetValue(PanelWidthProperty, value); }
        public PopoverEdge ResolvedPlacement => resolvedPlacement;

        public event EventHandler? Closed;

        //Gap and corner clamp are pointer-sized constants, not named tokens - only the tail's 12pt square got a key.
        private const double Gap = 6;
        private const double TailClamp = 26;

        private Border? panel;
        private Border? tail;
        private PopoverEdge resolvedPlacement = PopoverEdge.Bottom;
        private double tailSize = 12;
        private readonly List<IDisposable> tokenSubs = [];

        static PopoverFrame() {
            FocusableProperty.OverrideDefaultValue<PopoverFrame>(true);
            IsOpenProperty.Changed.AddClassHandler<PopoverFrame>((p, e) => p.ApplyOpenState((bool)e.NewValue!));
            ContentProperty.Changed.AddClassHandler<PopoverFrame>((p, _) => p.ApplyAccessibleName());
        }

        public PopoverFrame() {
            AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            //An unopened frame must not swallow clicks meant for whatever sits behind it.
            IsHitTestVisible = IsOpen;
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            panel = e.NameScope.Find<Border>("PART_Panel");
            tail = e.NameScope.Find<Border>("PART_Tail");
            PseudoClasses.Set(":open", IsOpen);
            IsHitTestVisible = IsOpen;
            ApplyTailGeometry();
            ApplyAccessibleName();
            UpdatePlacement();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Unsubscribe();
            tokenSubs.Add(this.GetResourceObservable("Foreman.Size.PopoverTail").Subscribe(new ResourceSinks.DoubleSink(v => {
                tailSize = v;
                ApplyTailGeometry();
                UpdatePlacement();
            }, tailSize)));
            if (Anchor is { } anchor && this.FindAncestorOfType<PopoverHost>() is { } host) {
                tokenSubs.Add(anchor.GetObservable(Visual.BoundsProperty).Subscribe(new ChangeObserver<Rect>(_ => UpdatePlacement())));
                tokenSubs.Add(host.GetObservable(Visual.BoundsProperty).Subscribe(new ChangeObserver<Rect>(_ => UpdatePlacement())));
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
        }

        //The tail's static footprint depends only on the PopoverTail token, never on where the frame lands.
        private void ApplyTailGeometry() {
            if (tail is null || panel is null)
                return;
            double half = tailSize / 2;
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

        //Driven by the anchor's and the host's own Bounds, never by this control's arrange pass.
        private void UpdatePlacement() {
            if (panel is null || Anchor is not { } anchor || this.FindAncestorOfType<PopoverHost>() is not { } host)
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
            //Only the two edges facing away from the body are stroked, so the tail's border never doubles up with the panel's seam.
            tail.BorderThickness = placement.Edge switch {
                PopoverEdge.Bottom => new Thickness(1, 1, 0, 0),
                PopoverEdge.Top => new Thickness(0, 0, 1, 1),
                PopoverEdge.Right => new Thickness(1, 0, 0, 1),
                _ => new Thickness(0, 1, 1, 0),
            };
            //Panel-local geometry offset by the shared half-tail margin - the tail's unrotated top-left cancels the panel's own (half, half) offset.
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

            TimeSpan duration = open ? Motion.PopoverDuration : Motion.RadialSweepCloseDuration;
            Avalonia.Animation.Easings.Easing easing = open ? Motion.RadialOpenEasing : Motion.LeavingEasing;
            if (panel is not null)
                panel.Transitions = Motion.Transition(TransitionKind.Scale, duration, easing);
            if (tail is not null)
                tail.Transitions = Motion.Transition(TransitionKind.Fade, duration, easing);

            PseudoClasses.Set(":open", open);
            IsHitTestVisible = open;
            if (open)
                //Posted, not inline - the frame has not been through its first arrange yet, so Focus() silently no-ops here.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (IsOpen) Focus(); });
            else {
                Anchor?.Focus();
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        internal void Close() => IsOpen = false;

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (!IsOpen || e.Key != Key.Escape)
                return;
            e.Handled = true;
            Close();
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

        //Preferred edge below the anchor, flipping to above then trailing then leading, and only then clamping - the frame never leaves the host.
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
