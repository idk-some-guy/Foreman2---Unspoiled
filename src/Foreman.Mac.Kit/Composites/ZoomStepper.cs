using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace Foreman.Mac.Kit.Composites {
    public class ZoomStepper : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(ZoomStepper);

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<ZoomStepper, double>(nameof(Value), 100, coerce: CoerceValue);

        public static readonly StyledProperty<ICommand?> ActualSizeCommandProperty =
            AvaloniaProperty.Register<ZoomStepper, ICommand?>(nameof(ActualSizeCommand));

        public static readonly StyledProperty<ICommand?> ZoomInCommandProperty =
            AvaloniaProperty.Register<ZoomStepper, ICommand?>(nameof(ZoomInCommand));

        public static readonly StyledProperty<ICommand?> ZoomOutCommandProperty =
            AvaloniaProperty.Register<ZoomStepper, ICommand?>(nameof(ZoomOutCommand));

        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public ICommand? ActualSizeCommand { get => GetValue(ActualSizeCommandProperty); set => SetValue(ActualSizeCommandProperty, value); }
        public ICommand? ZoomInCommand { get => GetValue(ZoomInCommandProperty); set => SetValue(ZoomInCommandProperty, value); }
        public ICommand? ZoomOutCommand { get => GetValue(ZoomOutCommandProperty); set => SetValue(ZoomOutCommandProperty, value); }

        public SolveStatus SolveStatus { get; } = new() { Name = "PART_SolveStatus" };

        private ZoomStepperReadout? readout;
        private Button? minusButton;
        private Button? plusButton;
        private TextBlock? valueText;
        private Panel? solveStatusHost;

        static ZoomStepper() {
            ValueProperty.Changed.AddClassHandler<ZoomStepper>((c, _) => c.ApplyValueText());
        }

        public ZoomStepper() {
            SolveStatus.PropertyChanged += (_, e) => {
                if (e.Property == SolveStatus.StateProperty)
                    ApplyState();
            };
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (readout is not null)
                readout.Click -= OnReadoutClick;
            readout = e.NameScope.Find<ZoomStepperReadout>("PART_Readout");
            minusButton = e.NameScope.Find<Button>("PART_Minus");
            plusButton = e.NameScope.Find<Button>("PART_Plus");
            valueText = e.NameScope.Find<TextBlock>("PART_Value");
            solveStatusHost = e.NameScope.Find<Panel>("PART_SolveStatusHost");

            if (readout is not null)
                readout.Click += OnReadoutClick;
            HostSolveStatus();
            ApplyValueText();
            ApplyState();
        }

        //Theme resolves from this control's scope once templated, never at construction.
        private void HostSolveStatus() {
            if (SolveStatus.Theme is null && this.TryFindResource("Kit.SolveStatus", null, out object? theme) && theme is Avalonia.Styling.ControlTheme controlTheme)
                SolveStatus.Theme = controlTheme;
            if (SolveStatus.Parent is Panel previous && !ReferenceEquals(previous, solveStatusHost))
                previous.Children.Remove(SolveStatus);
            if (solveStatusHost is not null && !solveStatusHost.Children.Contains(SolveStatus))
                solveStatusHost.Children.Add(SolveStatus);
        }

        private void ApplyState() {
            if (valueText is not null)
                valueText.IsVisible = SolveStatus.State == SolveStatusState.Idle;
            ApplyAccessibleText();
        }

        private void ApplyValueText() {
            if (valueText is not null)
                valueText.Text = FormatPercent(Value);
            ApplyAccessibleText();
        }

        private void ApplyAccessibleText() {
            if (readout is null)
                return;
            string percent = FormatPercent(Value);
            string text = SolveStatus.State switch {
                SolveStatusState.Solving => $"Solving — zoom {percent}",
                SolveStatusState.Failed => $"Solve failed — zoom {percent}",
                _ => "Actual Size (⌘0)",
            };
            ToolTip.SetTip(readout, text);
            AutomationProperties.SetName(readout, text);
            readout.AccessibleValue = FormatPercentWords(Value);
        }

        private static string FormatPercent(double value) =>
            string.Format(CultureInfo.InvariantCulture, "{0} %", (int)Math.Round(value, MidpointRounding.AwayFromZero));

        //The addendum's VoiceOver row spells the unit out ("140 percent") rather than using the "%" sign FormatPercent shows sighted users.
        private static string FormatPercentWords(double value) =>
            string.Format(CultureInfo.InvariantCulture, "{0} percent", (int)Math.Round(value, MidpointRounding.AwayFromZero));

        //Roving order for the row's peer tree (composites addendum §08 VoiceOver row): Zoom Out, the readout, Zoom In.
        internal IEnumerable<Control> AutomationChildren() {
            if (minusButton is not null)
                yield return minusButton;
            if (readout is not null)
                yield return readout;
            if (plusButton is not null)
                yield return plusButton;
        }

        private static double CoerceValue(AvaloniaObject sender, double value) => Math.Clamp(value, 25, 400);

        private void OnReadoutClick(object? sender, RoutedEventArgs e) {
            if (SolveStatus.State == SolveStatusState.Failed) {
                SolveStatus.IsErrorPopoverOpen = true;
                return;
            }
            if (ActualSizeCommand is { } command && command.CanExecute(null))
                command.Execute(null);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ZoomStepperAutomationPeer(this);

        private sealed class ZoomStepperAutomationPeer(ZoomStepper owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string GetNameCore() => "Zoom";
            protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
                [.. owner.AutomationChildren().Select(ControlAutomationPeer.CreatePeerForElement)];
        }
    }

    //A named Button subclass so the readout's own automation peer can expose a value pattern - a stock Button's peer has none.
    public class ZoomStepperReadout : Button {
        public static readonly StyledProperty<string?> AccessibleValueProperty =
            AvaloniaProperty.Register<ZoomStepperReadout, string?>(nameof(AccessibleValue));

        public string? AccessibleValue {
            get => GetValue(AccessibleValueProperty);
            set => SetValue(AccessibleValueProperty, value);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ZoomStepperReadoutAutomationPeer(this);

        private sealed class ZoomStepperReadoutAutomationPeer(ZoomStepperReadout owner) : ControlAutomationPeer(owner), IValueProvider {
            bool IValueProvider.IsReadOnly => true;
            string? IValueProvider.Value => owner.AccessibleValue;
            void IValueProvider.SetValue(string? value) { }
        }
    }
}
