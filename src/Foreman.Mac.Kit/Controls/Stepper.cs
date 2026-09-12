using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Foreman.Mac.Kit.Controls {
    public class Stepper : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(Stepper);

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<Stepper, double>(nameof(Value), coerce: CoerceValue);

        public static readonly StyledProperty<double> MinimumProperty =
            AvaloniaProperty.Register<Stepper, double>(nameof(Minimum), 0);

        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<Stepper, double>(nameof(Maximum), 100);

        public static readonly StyledProperty<double> IncrementProperty =
            AvaloniaProperty.Register<Stepper, double>(nameof(Increment), 1);

        public static readonly StyledProperty<string?> UnitProperty =
            AvaloniaProperty.Register<Stepper, string?>(nameof(Unit));

        public static readonly StyledProperty<int> DecimalsProperty =
            AvaloniaProperty.Register<Stepper, int>(nameof(Decimals), 0);

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

        public double Increment {
            get => GetValue(IncrementProperty);
            set => SetValue(IncrementProperty, value);
        }

        public string? Unit {
            get => GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public int Decimals {
            get => GetValue(DecimalsProperty);
            set => SetValue(DecimalsProperty, value);
        }

        private TextBox? valueBox;
        private Button? upButton;
        private Button? downButton;
        private TextBlock? unitText;
        private Grid? columns;
        private readonly List<IDisposable> subs = [];

        static Stepper() {
            ValueProperty.Changed.AddClassHandler<Stepper>((s, _) => s.RefreshValueDisplay());
            MinimumProperty.Changed.AddClassHandler<Stepper>((s, _) => s.CoerceValue(ValueProperty));
            MaximumProperty.Changed.AddClassHandler<Stepper>((s, _) => s.CoerceValue(ValueProperty));
            UnitProperty.Changed.AddClassHandler<Stepper>((s, _) => s.RefreshUnitDisplay());
            DecimalsProperty.Changed.AddClassHandler<Stepper>((s, _) => s.RefreshValueDisplay());
        }

        private static double CoerceValue(AvaloniaObject sender, double value) =>
            Math.Clamp(value, ((Stepper)sender).Minimum, ((Stepper)sender).Maximum);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);

            if (valueBox is not null) {
                valueBox.RemoveHandler(InputElement.KeyDownEvent, OnValueBoxKeyDown);
                valueBox.LostFocus -= OnValueBoxLostFocus;
            }
            if (upButton is not null)
                upButton.Click -= OnUpClicked;
            if (downButton is not null)
                downButton.Click -= OnDownClicked;

            valueBox = e.NameScope.Find<TextBox>("PART_ValueBox");
            upButton = e.NameScope.Find<Button>("PART_Up");
            downButton = e.NameScope.Find<Button>("PART_Down");
            unitText = e.NameScope.Find<TextBlock>("PART_Unit");
            columns = e.NameScope.Find<Grid>("PART_Columns");

            if (valueBox is not null) {
                //Tunnel, ahead of TextBox's own bubble-phase Up/Down caret movement, so a single-line box never eats our step keys.
                valueBox.AddHandler(InputElement.KeyDownEvent, OnValueBoxKeyDown, RoutingStrategies.Tunnel);
                valueBox.LostFocus += OnValueBoxLostFocus;
            }
            if (upButton is not null)
                upButton.Click += OnUpClicked;
            if (downButton is not null)
                downButton.Click += OnDownClicked;

            RefreshValueDisplay();
            RefreshUnitDisplay();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        //ColumnDefinition.Width is a GridLength, so we convert the raw double resource ourselves rather than bind it in XAML.
        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Size.StepperFieldWidth").Subscribe(new Sink<double>(v => {
                if (columns is not null)
                    columns.ColumnDefinitions[0].Width = new GridLength(v);
            }, 40)));
            subs.Add(this.GetResourceObservable("Foreman.Size.StepperArrowColumn").Subscribe(new Sink<double>(v => {
                if (columns is not null)
                    columns.ColumnDefinitions[2].Width = new GridLength(v);
            }, 15)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void OnUpClicked(object? sender, RoutedEventArgs e) => Step(Increment);

        private void OnDownClicked(object? sender, RoutedEventArgs e) => Step(-Increment);

        private void OnValueBoxLostFocus(object? sender, RoutedEventArgs e) => CommitText();

        private void OnValueBoxKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Up:
                    Step(StepAmount(e.KeyModifiers));
                    e.Handled = true;
                    break;
                case Key.Down:
                    Step(-StepAmount(e.KeyModifiers));
                    e.Handled = true;
                    break;
                case Key.Enter:
                    CommitText();
                    e.Handled = true;
                    break;
            }
        }

        private double StepAmount(KeyModifiers modifiers) =>
            modifiers.HasFlag(KeyModifiers.Shift) ? Increment * 10 : Increment;

        private void Step(double delta) => Value += delta;

        private void CommitText() {
            if (valueBox is null)
                return;

            if (double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                Value = parsed;
            else
                RefreshValueDisplay();
        }

        private void RefreshValueDisplay() {
            if (valueBox is not null)
                valueBox.Text = Value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
        }

        private void RefreshUnitDisplay() {
            if (unitText is null)
                return;
            unitText.Text = Unit;
            unitText.IsVisible = !string.IsNullOrEmpty(Unit);
        }

        private sealed class Sink<T> : IObserver<object?> {
            private readonly Action<T> onNext;
            private readonly T fallback;
            private T current;

            public Sink(Action<T> onNext, T fallback) {
                this.onNext = onNext;
                this.fallback = fallback;
                current = fallback;
            }

            public void OnNext(object? value) {
                T next = value is T t ? t : fallback;
                if (EqualityComparer<T>.Default.Equals(current, next))
                    return;
                current = next;
                onNext(next);
            }

            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
