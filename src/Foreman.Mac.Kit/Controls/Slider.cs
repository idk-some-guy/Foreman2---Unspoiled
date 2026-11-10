using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Foreman.Mac.Kit.Controls {
    public class Slider : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(Slider);

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<Slider, double>(nameof(Value), coerce: CoerceValue);

        public static readonly StyledProperty<double> MinimumProperty =
            AvaloniaProperty.Register<Slider, double>(nameof(Minimum), 0);

        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<Slider, double>(nameof(Maximum), 100);

        public static readonly StyledProperty<double> StepProperty =
            AvaloniaProperty.Register<Slider, double>(nameof(Step), 0);

        public static readonly StyledProperty<bool> ShowValueProperty =
            AvaloniaProperty.Register<Slider, bool>(nameof(ShowValue));

        public static readonly StyledProperty<string?> UnitProperty =
            AvaloniaProperty.Register<Slider, string?>(nameof(Unit));

        public static readonly StyledProperty<string?> SpokenUnitProperty =
            AvaloniaProperty.Register<Slider, string?>(nameof(SpokenUnit));

        public static readonly StyledProperty<double> PageStepProperty =
            AvaloniaProperty.Register<Slider, double>(nameof(PageStep), double.NaN);

        public double Value {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Minimum {
            get => GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum {
            get => GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double Step {
            get => GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        public bool ShowValue {
            get => GetValue(ShowValueProperty);
            set => SetValue(ShowValueProperty, value);
        }

        public string? Unit {
            get => GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public string? SpokenUnit {
            get => GetValue(SpokenUnitProperty);
            set => SetValue(SpokenUnitProperty, value);
        }

        public double PageStep {
            get => GetValue(PageStepProperty);
            set => SetValue(PageStepProperty, value);
        }

        public bool IsDiscrete => Step > 0;

        public int StepCount => IsDiscrete && Maximum > Minimum ? (int)Math.Round((Maximum - Minimum) / Step) : 0;

        internal int TickRebuilds { get; private set; }

        private Border? track;
        private Border? fill;
        private Border? thumb;
        private Canvas? ticks;
        private TextBlock? valueLabel;

        private bool hovering;
        private bool pressed;
        private bool dragging;
        private double dragStartValue;
        private IPointer? capturedPointer;

        private EventHandler? trackLayoutUpdated;
        private EventHandler? ticksLayoutUpdated;
        private Size lastTrackSize;
        private Size lastTicksSize;
        private double tickHeight = 4;
        private double tickGap = 5;
        private double thumbNormal = 14;
        private double thumbHover = 15;
        private double thumbPressed = 16;
        private IBrush tickBrush = Brushes.Transparent;
        private readonly List<IDisposable> subs = [];

        static Slider() {
            ValueProperty.Changed.AddClassHandler<Slider>((s, _) => { s.Reposition(); s.RefreshValueLabel(); });
            MinimumProperty.Changed.AddClassHandler<Slider>((s, _) => { s.CoerceValue(ValueProperty); s.Reposition(); });
            MaximumProperty.Changed.AddClassHandler<Slider>((s, _) => { s.CoerceValue(ValueProperty); s.Reposition(); });
            StepProperty.Changed.AddClassHandler<Slider>((s, _) => { s.RebuildTicks(); s.Reposition(); });
            ShowValueProperty.Changed.AddClassHandler<Slider>((s, _) => s.RefreshValueLabel());
            UnitProperty.Changed.AddClassHandler<Slider>((s, _) => s.RefreshValueLabel());
        }

        private static double CoerceValue(AvaloniaObject sender, double value) =>
            Math.Clamp(value, ((Slider)sender).Minimum, ((Slider)sender).Maximum);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (track is not null && trackLayoutUpdated is not null)
                track.LayoutUpdated -= trackLayoutUpdated;
            if (ticks is not null && ticksLayoutUpdated is not null)
                ticks.LayoutUpdated -= ticksLayoutUpdated;

            track = e.NameScope.Find<Border>("PART_Track");
            fill = e.NameScope.Find<Border>("PART_Fill");
            thumb = e.NameScope.Find<Border>("PART_Thumb");
            ticks = e.NameScope.Find<Canvas>("PART_Ticks");
            valueLabel = e.NameScope.Find<TextBlock>("PART_ValueLabel");

            if (track is not null) {
                trackLayoutUpdated = (_, _) => {
                    if (track.Bounds.Size == lastTrackSize)
                        return;
                    lastTrackSize = track.Bounds.Size;
                    RebuildTicks();
                    Reposition();
                };
                track.LayoutUpdated += trackLayoutUpdated;
            }

            if (ticks is not null) {
                ticksLayoutUpdated = (_, _) => {
                    if (ticks.Bounds.Size == lastTicksSize)
                        return;
                    lastTicksSize = ticks.Bounds.Size;
                    TickRebuilds++;
                    RebuildTicks();
                };
                ticks.LayoutUpdated += ticksLayoutUpdated;
            }

            ApplyThumbSize();
            RebuildTicks();
            Reposition();
            RefreshValueLabel();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Size.SliderTickHeight").Subscribe(new ResourceSinks.DoubleSink(v => { tickHeight = v; RebuildTicks(); }, tickHeight)));
            subs.Add(this.GetResourceObservable("Foreman.Size.SliderTickGap").Subscribe(new ResourceSinks.DoubleSink(v => { tickGap = v; RebuildTicks(); }, tickGap)));
            subs.Add(this.GetResourceObservable("Foreman.Size.SliderThumbNormal").Subscribe(new ResourceSinks.DoubleSink(v => { thumbNormal = v; ApplyThumbSize(); }, thumbNormal)));
            subs.Add(this.GetResourceObservable("Foreman.Size.SliderThumbHover").Subscribe(new ResourceSinks.DoubleSink(v => { thumbHover = v; ApplyThumbSize(); }, thumbHover)));
            subs.Add(this.GetResourceObservable("Foreman.Size.SliderThumbPressed").Subscribe(new ResourceSinks.DoubleSink(v => { thumbPressed = v; ApplyThumbSize(); }, thumbPressed)));
            subs.Add(this.GetResourceObservable("Foreman.Color.Border").Subscribe(new ResourceSinks.BrushSink(v => { tickBrush = v; RebuildTicks(); }, tickBrush)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private double TargetThumbSize() => pressed ? thumbPressed : hovering ? thumbHover : thumbNormal;

        private void ApplyThumbSize() {
            if (thumb is null)
                return;
            double size = TargetThumbSize();
            thumb.Width = size;
            thumb.Height = size;
            Reposition();
        }

        private void Reposition() {
            if (track is null || thumb is null)
                return;

            double range = Maximum - Minimum;
            double fraction = range > 0 ? Math.Clamp((Value - Minimum) / range, 0, 1) : 0;
            double trackWidth = track.Bounds.Width;

            if (fill is not null)
                fill.Width = trackWidth * fraction;

            double x = trackWidth * fraction - thumb.Width / 2;
            thumb.RenderTransform = TransformOperations.Parse($"translateX({x.ToString(CultureInfo.InvariantCulture)}px)");
        }

        private void RebuildTicks() {
            if (ticks is null)
                return;

            ticks.Children.Clear();
            int count = StepCount;
            if (!IsDiscrete || count < 1 || count > 11)
                return;

            double width = ticks.Bounds.Width;
            double centerY = ticks.Bounds.Height / 2;
            for (int i = 0; i <= count; i++) {
                double x = width * i / count;
                var mark = new Rectangle { Width = 1, Height = tickHeight, Fill = tickBrush };
                Canvas.SetLeft(mark, x - 0.5);
                Canvas.SetTop(mark, centerY + tickGap);
                ticks.Children.Add(mark);
            }
        }

        private void RefreshValueLabel() {
            if (valueLabel is null)
                return;
            valueLabel.IsVisible = ShowValue;
            valueLabel.Text = ShowValue ? FormatValue() : null;
        }

        private string RoundedValue => Math.Round(Value).ToString(CultureInfo.InvariantCulture);

        private string FormatValue() => string.IsNullOrEmpty(Unit) ? RoundedValue : $"{RoundedValue}\u202F{Unit}";

        protected override void OnPointerEntered(PointerEventArgs e) {
            base.OnPointerEntered(e);
            hovering = true;
            ApplyThumbSize();
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            hovering = false;
            ApplyThumbSize();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!IsEnabled || track is null)
                return;

            pressed = true;
            dragging = true;
            dragStartValue = Value;
            thumb?.Focus();
            ApplyThumbSize();
            capturedPointer = e.Pointer;
            e.Pointer.Capture(this);
            UpdateValueFromPointer(e.GetPosition(track));
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            if (dragging && track is not null)
                UpdateValueFromPointer(e.GetPosition(track));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (!dragging)
                return;
            dragging = false;
            pressed = false;
            ApplyThumbSize();
            e.Pointer.Capture(null);
            capturedPointer = null;
        }

        private void UpdateValueFromPointer(Point positionInTrack) {
            if (track is null || track.Bounds.Width <= 0)
                return;

            double fraction = Math.Clamp(positionInTrack.X / track.Bounds.Width, 0, 1);
            double raw = Minimum + fraction * (Maximum - Minimum);
            Value = IsDiscrete ? SnapToStep(raw) : raw;
        }

        private double SnapToStep(double raw) => Minimum + Math.Round((raw - Minimum) / Step) * Step;

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            double range = Maximum - Minimum;

            switch (e.Key) {
                case Key.Left:
                case Key.Down:
                    Value -= StepFor(e.KeyModifiers, range);
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.Up:
                    Value += StepFor(e.KeyModifiers, range);
                    e.Handled = true;
                    break;
                case Key.Home:
                    Value = Minimum;
                    e.Handled = true;
                    break;
                case Key.End:
                    Value = Maximum;
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    Value += PageAmount(range);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    Value -= PageAmount(range);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (dragging) {
                        Value = dragStartValue;
                        dragging = false;
                        pressed = false;
                        ApplyThumbSize();
                        capturedPointer?.Capture(null);
                        capturedPointer = null;
                        e.Handled = true;
                    }
                    break;
            }
        }

        private double ArrowStep(double range) => IsDiscrete ? Step : range * 0.01;

        private double PageAmount(double range) => double.IsNaN(PageStep) ? range * 0.25 : PageStep;

        private double StepFor(KeyModifiers modifiers, double range) {
            if (modifiers.HasFlag(KeyModifiers.Shift))
                return range * 0.10;
            if (modifiers.HasFlag(KeyModifiers.Alt))
                return range * 0.001;
            return ArrowStep(range);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new SliderAutomationPeer(this);

        private sealed class SliderAutomationPeer : ControlAutomationPeer, IRangeValueProvider, IValueProvider {
            public SliderAutomationPeer(Slider owner) : base(owner) {
                owner.PropertyChanged += OwnerPropertyChanged;
            }

            private Slider Slider => (Slider)Owner;
            private double Range => Slider.Maximum - Slider.Minimum;

            private void OwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
                if (e.Property == ValueProperty)
                    RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, e.OldValue, e.NewValue);
                else if (e.Property == MinimumProperty)
                    RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MinimumProperty, e.OldValue, e.NewValue);
                else if (e.Property == MaximumProperty)
                    RaisePropertyChangedEvent(RangeValuePatternIdentifiers.MaximumProperty, e.OldValue, e.NewValue);
            }

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

            protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => null;
            protected override bool HasKeyboardFocusCore() => Slider.IsKeyboardFocusWithin;
            protected override bool IsKeyboardFocusableCore() => Slider.thumb?.Focusable == true;
            protected override void SetFocusCore() => Slider.thumb?.Focus();

            bool IRangeValueProvider.IsReadOnly => !Slider.IsEnabled;
            double IRangeValueProvider.Minimum => Slider.Minimum;
            double IRangeValueProvider.Maximum => Slider.Maximum;
            double IRangeValueProvider.Value => Slider.Value;
            double IRangeValueProvider.SmallChange => Slider.ArrowStep(Range);
            double IRangeValueProvider.LargeChange => Slider.PageAmount(Range);
            void IRangeValueProvider.SetValue(double value) => Slider.SetCurrentValue(ValueProperty, value);

            bool IValueProvider.IsReadOnly => true;
            string? IValueProvider.Value => SpokenValue.Describe(Slider.RoundedValue, Slider.Unit, Slider.SpokenUnit);
            void IValueProvider.SetValue(string? value) { }
        }

    }

    internal sealed class SliderThumb : Border {
        protected override Type StyleKeyOverride => typeof(Border);

        protected override AutomationPeer OnCreateAutomationPeer() =>
            TemplatedParent is Control owner ? ControlAutomationPeer.CreatePeerForElement(owner) : base.OnCreateAutomationPeer();
    }
}
