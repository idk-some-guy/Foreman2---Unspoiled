using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Kit.Composites {
    public class SplitDivider : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(SplitDivider);

        public const double DefaultValue = 300;
        public const double SmallStep = 8;
        public const double LargeStep = 24;

        public static readonly StyledProperty<double> MinimumProperty =
            AvaloniaProperty.Register<SplitDivider, double>(nameof(Minimum), 260);

        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<SplitDivider, double>(nameof(Maximum), 380);

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<SplitDivider, double>(nameof(Value), DefaultValue, coerce: CoerceValue);

        public double Minimum {
            get => GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum {
            get => GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double Value {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public event EventHandler<double>? ValueChanged;

        private bool isDragging;
        private double dragStartPointerX;
        private double dragStartValue;
        private IPointer? capturedPointer;
        private CancellationTokenSource? resetCancellation;

        private static readonly Cursor LeftSideCursor = new(StandardCursorType.LeftSide);
        private static readonly Cursor RightSideCursor = new(StandardCursorType.RightSide);
        private static readonly Cursor SizeWestEastCursor = new(StandardCursorType.SizeWestEast);

        static SplitDivider() {
            MinimumProperty.Changed.AddClassHandler<SplitDivider>((d, _) => { d.CoerceValue(ValueProperty); d.ApplyCursor(); });
            MaximumProperty.Changed.AddClassHandler<SplitDivider>((d, _) => { d.CoerceValue(ValueProperty); d.ApplyCursor(); });
            ValueProperty.Changed.AddClassHandler<SplitDivider>((d, e) => {
                d.ApplyCursor();
                d.ValueChanged?.Invoke(d, (double)e.NewValue!);
            });
        }

        public SplitDivider() {
            Focusable = true;
            ApplyCursor();
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new SplitDividerAutomationPeer(this);

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            resetCancellation?.Cancel();
            resetCancellation = null;
            if (isDragging)
                EndDrag();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            resetCancellation?.Cancel();
            resetCancellation = null;

            Focus(NavigationMethod.Pointer);
            if (e.ClickCount >= 2) {
                ResetToDefault();
                e.Handled = true;
                return;
            }

            isDragging = true;
            dragStartPointerX = e.GetCurrentPoint(null).Position.X;
            dragStartValue = Value;
            PseudoClasses.Set(":dragging", true);
            capturedPointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            if (!isDragging)
                return;
            double currentX = e.GetCurrentPoint(null).Position.X;
            SetCurrentValue(ValueProperty, dragStartValue + (currentX - dragStartPointerX));
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (!isDragging)
                return;
            EndDrag();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (isDragging) {
                if (e.Key == Key.Escape) {
                    SetCurrentValue(ValueProperty, dragStartValue);
                    EndDrag();
                    e.Handled = true;
                }
                return;
            }

            resetCancellation?.Cancel();
            resetCancellation = null;

            switch (e.Key) {
                case Key.Left:
                    SetCurrentValue(ValueProperty, Value - (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? LargeStep : SmallStep));
                    e.Handled = true;
                    break;
                case Key.Right:
                    SetCurrentValue(ValueProperty, Value + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? LargeStep : SmallStep));
                    e.Handled = true;
                    break;
                case Key.Home:
                    SetCurrentValue(ValueProperty, Minimum);
                    e.Handled = true;
                    break;
                case Key.End:
                    SetCurrentValue(ValueProperty, Maximum);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    ResetToDefault();
                    e.Handled = true;
                    break;
            }
        }

        private void EndDrag() {
            isDragging = false;
            PseudoClasses.Set(":dragging", false);
            capturedPointer?.Capture(null);
            capturedPointer = null;
        }

        private void ResetToDefault() {
            double target = Math.Clamp(DefaultValue, Minimum, Maximum);
            if (Motion.ReduceMotion || VisualRoot is null) {
                SetCurrentValue(ValueProperty, target);
                return;
            }

            double start = Value;
            resetCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            resetCancellation = cancellation;
            _ = RunReset(start, target, cancellation.Token);
        }

        private async Task RunReset(double start, double target, CancellationToken token) {
            var reset = new Animation {
                Duration = Motion.InspectorReflowDuration,
                Easing = Motion.EntranceEasing,
                Children = {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ValueProperty, start) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ValueProperty, target) } },
                },
            };
            try {
                await reset.RunAsync(this, token);
            } catch (OperationCanceledException) {
                return;
            }

            //Animation.RunAsync does not leave the last keyframe applied once it completes - pin the real value.
            if (!token.IsCancellationRequested)
                SetCurrentValue(ValueProperty, target);
        }

        private void ApplyCursor() {
            Cursor target = Value >= Maximum ? LeftSideCursor : Value <= Minimum ? RightSideCursor : SizeWestEastCursor;
            if (!ReferenceEquals(Cursor, target))
                Cursor = target;
        }

        private static double CoerceValue(AvaloniaObject sender, double value) {
            var divider = (SplitDivider)sender;
            return Math.Clamp(value, divider.Minimum, divider.Maximum);
        }

        private sealed class SplitDividerAutomationPeer(SplitDivider owner) : ControlAutomationPeer(owner), IRangeValueProvider {
            private SplitDivider Divider => (SplitDivider)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
            protected override string GetClassNameCore() => "Splitter";

            bool IRangeValueProvider.IsReadOnly => false;
            double IRangeValueProvider.Minimum => Divider.Minimum;
            double IRangeValueProvider.Maximum => Divider.Maximum;
            double IRangeValueProvider.Value => Divider.Value;
            double IRangeValueProvider.SmallChange => SmallStep;
            double IRangeValueProvider.LargeChange => LargeStep;

            void IRangeValueProvider.SetValue(double value) => Divider.SetCurrentValue(ValueProperty, value);
        }
    }
}
