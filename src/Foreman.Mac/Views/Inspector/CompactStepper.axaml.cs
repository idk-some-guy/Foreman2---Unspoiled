using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Foreman;
using System;
using System.Globalization;

namespace Foreman.Mac.Views.Inspector {
    //Compact 22px/52-field/15x11-arrow stepper (Task 9, contract §03 CSS 439-450): NumericUpDown's own
    //Fluent template can't reach this shape without retemplating internals this port doesn't control, so
    //this owns its own TextBox+arrow-button template - Value/Minimum/Maximum/Increment/FormatString mirror
    //the NumericUpDown properties it replaces, so every call site's binding survives unchanged.
    public sealed partial class CompactStepper : UserControl {
        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<CompactStepper, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay, coerce: CoerceValue);

        public static readonly StyledProperty<double> MinimumProperty =
            AvaloniaProperty.Register<CompactStepper, double>(nameof(Minimum), defaultValue: 0);

        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<CompactStepper, double>(nameof(Maximum), defaultValue: 100);

        public static readonly StyledProperty<double> IncrementProperty =
            AvaloniaProperty.Register<CompactStepper, double>(nameof(Increment), defaultValue: 1);

        public static readonly StyledProperty<string> FormatStringProperty =
            AvaloniaProperty.Register<CompactStepper, string>(nameof(FormatString), defaultValue: "0.##");

        private readonly TextBox valueBox;

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

        public string FormatString {
            get => GetValue(FormatStringProperty);
            set => SetValue(FormatStringProperty, value);
        }

        //Read by RecipeSegmentModulesBeaconTests' BeaconCountInput_WholeValue_DisplaysBareInteger - the
        //live formatted mirror of Value, same as NumericUpDown.Text.
        public string Text => valueBox.Text ?? "";

        public event EventHandler<double>? ValueChanged;

        public CompactStepper() {
            InitializeComponent();
            valueBox = this.FindControl<TextBox>("ValueBox")!;
            this.FindControl<Button>("IncrementButton")!.Click += (_, _) => Value += Increment;
            this.FindControl<Button>("DecrementButton")!.Click += (_, _) => Value -= Increment;
            valueBox.AddHandler(KeyDownEvent, OnValueBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            valueBox.LostFocus += (_, _) => CommitText();
            RefreshText();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private static double CoerceValue(AvaloniaObject sender, double value) =>
            sender is CompactStepper stepper ? Math.Clamp(value, stepper.Minimum, stepper.Maximum) : value;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ValueProperty) {
                RefreshText();
                ValueChanged?.Invoke(this, Value);
            } else if (change.Property == FormatStringProperty)
                RefreshText();
        }

        private void RefreshText() {
            if (valueBox is null)
                return;
            valueBox.Text = Value.ToString(FormatString, DisplayCulture.Format);
        }

        private void OnValueBoxKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key != Key.Enter)
                return;
            CommitText();
            e.Handled = true;
        }

        //NumberStyles.Any, not Float - N2/N4 render a thousands separator that Float can't parse back.
        private void CommitText() {
            if (double.TryParse(valueBox.Text, NumberStyles.Any, DisplayCulture.Format, out double parsed))
                Value = parsed;
            else
                RefreshText();
        }
    }
}
