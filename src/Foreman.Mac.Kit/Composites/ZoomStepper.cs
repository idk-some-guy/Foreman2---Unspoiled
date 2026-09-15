using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Globalization;
using System.Windows.Input;
using SpinnerControl = Foreman.Mac.Kit.Controls.Spinner;

namespace Foreman.Mac.Kit.Composites {
    //Error beats Solving beats Normal when both flags are set at once (defensive - the host should never do this).
    public enum ZoomStepperState { Normal, Solving, Error }

    //Toolbar's zoom readout (K3 composites addendum §08): a number, or a spinner mid-solve, or an alert that
    //opens the stepper's own error popover, carrying host-supplied content - never more than one of the three at a time.
    public class ZoomStepper : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(ZoomStepper);

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<ZoomStepper, double>(nameof(Value), 100, coerce: CoerceValue);

        public static readonly StyledProperty<bool> IsSolvingProperty =
            AvaloniaProperty.Register<ZoomStepper, bool>(nameof(IsSolving));

        public static readonly StyledProperty<bool> HasSolveErrorProperty =
            AvaloniaProperty.Register<ZoomStepper, bool>(nameof(HasSolveError));

        public static readonly StyledProperty<ICommand?> ActualSizeCommandProperty =
            AvaloniaProperty.Register<ZoomStepper, ICommand?>(nameof(ActualSizeCommand));

        public static readonly StyledProperty<ICommand?> ZoomInCommandProperty =
            AvaloniaProperty.Register<ZoomStepper, ICommand?>(nameof(ZoomInCommand));

        public static readonly StyledProperty<ICommand?> ZoomOutCommandProperty =
            AvaloniaProperty.Register<ZoomStepper, ICommand?>(nameof(ZoomOutCommand));

        //Content for the stepper's own error PopoverFrame, carrying a host-supplied Kit.StatePanel.error.
        public static readonly StyledProperty<object?> ErrorPopoverContentProperty =
            AvaloniaProperty.Register<ZoomStepper, object?>(nameof(ErrorPopoverContent));

        public static readonly StyledProperty<bool> IsErrorPopoverOpenProperty =
            AvaloniaProperty.Register<ZoomStepper, bool>(nameof(IsErrorPopoverOpen));

        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public bool IsSolving { get => GetValue(IsSolvingProperty); set => SetValue(IsSolvingProperty, value); }
        public bool HasSolveError { get => GetValue(HasSolveErrorProperty); set => SetValue(HasSolveErrorProperty, value); }
        public ICommand? ActualSizeCommand { get => GetValue(ActualSizeCommandProperty); set => SetValue(ActualSizeCommandProperty, value); }
        public ICommand? ZoomInCommand { get => GetValue(ZoomInCommandProperty); set => SetValue(ZoomInCommandProperty, value); }
        public ICommand? ZoomOutCommand { get => GetValue(ZoomOutCommandProperty); set => SetValue(ZoomOutCommandProperty, value); }
        public object? ErrorPopoverContent { get => GetValue(ErrorPopoverContentProperty); set => SetValue(ErrorPopoverContentProperty, value); }
        public bool IsErrorPopoverOpen { get => GetValue(IsErrorPopoverOpenProperty); set => SetValue(IsErrorPopoverOpenProperty, value); }

        //Raised when the popover is dismissed (Escape, outside press) and the readout returns to the number; HasSolveError is not touched, the host owns clearing it.
        public event EventHandler? ErrorDismissed;

        public ZoomStepperState State =>
            HasSolveError ? ZoomStepperState.Error : IsSolving ? ZoomStepperState.Solving : ZoomStepperState.Normal;

        //What the readout actually shows: a dismissed error still holds HasSolveError until the host clears it, so it displays like Normal until then.
        private ZoomStepperState DisplayState =>
            appliedState == ZoomStepperState.Error && errorDismissed ? ZoomStepperState.Normal : appliedState;

        private Button? readout;
        private TextBlock? valueText;
        private SpinnerControl? spinner;
        private Path? errorGlyph;
        private PopoverFrame? errorPopover;
        private PopoverHost? popoverHost;
        private IDisposable? spinnerHideSub;
        private bool templateApplied;
        private bool errorDismissed;
        private ZoomStepperState appliedState = ZoomStepperState.Normal;

        static ZoomStepper() {
            ValueProperty.Changed.AddClassHandler<ZoomStepper>((c, _) => c.ApplyValueText());
            IsSolvingProperty.Changed.AddClassHandler<ZoomStepper>((c, _) => c.ApplyState());
            HasSolveErrorProperty.Changed.AddClassHandler<ZoomStepper>((c, _) => c.ApplyState());
            IsErrorPopoverOpenProperty.Changed.AddClassHandler<ZoomStepper>((c, e) => c.ApplyErrorPopoverOpen((bool)e.NewValue!));
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            readout = e.NameScope.Find<Button>("PART_Readout");
            valueText = e.NameScope.Find<TextBlock>("PART_Value");
            spinner = e.NameScope.Find<SpinnerControl>("PART_Spinner");
            errorGlyph = e.NameScope.Find<Path>("PART_ErrorGlyph");

            if (readout is not null)
                readout.Click += OnReadoutClick;

            templateApplied = true;
            appliedState = State;
            ApplyValueText();
            ApplyStateVisuals();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            if (errorPopover is { } popover) {
                popover.Closed -= OnErrorPopoverClosed;
                popoverHost?.Remove(popover);
                errorPopover = null;
                popoverHost = null;
            }
        }

        private void ApplyState() {
            if (!templateApplied)
                return;
            ZoomStepperState next = State;
            if (appliedState == next)
                return;
            appliedState = next;
            errorDismissed = false;
            ApplyStateVisuals();
            if (next != ZoomStepperState.Error)
                IsErrorPopoverOpen = false;
        }

        //Fresh Transitions every state change (StatePanel's ApplyAppear idiom) - a fast solve that flips Solving off before the grace elapses discards the pending fade-in without a flash.
        private void ApplyStateVisuals() {
            ZoomStepperState display = DisplayState;
            bool solving = display == ZoomStepperState.Solving;
            bool error = display == ZoomStepperState.Error;
            bool normal = display == ZoomStepperState.Normal;

            PseudoClasses.Set(":solving", solving);
            PseudoClasses.Set(":error", error);

            TimeSpan delay = solving ? Motion.StatePanelDelay : TimeSpan.Zero;
            Avalonia.Animation.Transitions fade = Motion.Transition(TransitionKind.CrossFade, Motion.TabLabelCrossFadeDuration, delay: delay);

            if (valueText is not null) {
                valueText.Transitions = fade;
                valueText.Opacity = normal ? 1 : 0;
            }
            if (spinner is not null) {
                spinner.Transitions = fade;
                if (solving) {
                    spinnerHideSub?.Dispose();
                    spinnerHideSub = null;
                    spinner.IsVisible = true;
                    spinner.Opacity = 1;
                } else if (spinner.Opacity <= 0) {
                    spinnerHideSub?.Dispose();
                    spinnerHideSub = null;
                    spinner.Opacity = 0;
                    spinner.IsVisible = false;
                } else {
                    spinner.Opacity = 0;
                    spinnerHideSub?.Dispose();
                    spinnerHideSub = spinner.GetObservable(Visual.OpacityProperty).Subscribe(new OpacitySink(v => {
                        if (v > 0)
                            return;
                        spinner.IsVisible = false;
                        spinnerHideSub?.Dispose();
                        spinnerHideSub = null;
                    }));
                }
            }
            if (errorGlyph is not null) {
                errorGlyph.Transitions = fade;
                errorGlyph.Opacity = error ? 1 : 0;
            }

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
            string text = DisplayState switch {
                ZoomStepperState.Solving => $"Solving — zoom {percent}",
                ZoomStepperState.Error => $"Zoom {percent} — solve failed",
                _ => "Actual Size (⌘0)",
            };
            ToolTip.SetTip(readout, text);
            AutomationProperties.SetName(readout, text);
            AutomationProperties.SetHelpText(readout, percent);
        }

        private static string FormatPercent(double value) =>
            string.Format(CultureInfo.InvariantCulture, "{0} %", (int)Math.Round(value, MidpointRounding.AwayFromZero));

        private static double CoerceValue(AvaloniaObject sender, double value) => Math.Clamp(value, 25, 400);

        private void OnReadoutClick(object? sender, RoutedEventArgs e) {
            if (DisplayState == ZoomStepperState.Error) {
                IsErrorPopoverOpen = true;
                return;
            }
            if (ActualSizeCommand is { } command && command.CanExecute(null))
                command.Execute(null);
        }

        //IsErrorPopoverOpen is the single open/close path - setting it directly raises the same events this handler does.
        private void ApplyErrorPopoverOpen(bool open) {
            if (open)
                OpenErrorPopover();
            else if (errorPopover is { IsOpen: true } popover)
                popover.IsOpen = false;
        }

        //Dismissal (Escape, outside press, or the host clearing HasSolveError) returns the display to the number; HasSolveError itself stays host-owned.
        private void OnErrorPopoverClosed(object? sender, EventArgs e) {
            IsErrorPopoverOpen = false;
            errorDismissed = true;
            ApplyStateVisuals();
            ErrorDismissed?.Invoke(this, EventArgs.Empty);
        }

        //Built once per host attach; PopoverHost defers the actual Surface add when a layout pass is mid-flight (e.g. opened from TemplateApplied).
        private void OpenErrorPopover() {
            if (errorPopover is { } existing) {
                existing.Content = ErrorPopoverContent;
                existing.IsOpen = true;
                return;
            }
            if (readout is null) {
                IsErrorPopoverOpen = false;
                return;
            }
            if (this.FindAncestorOfType<PopoverHost>() is not { } host)
                throw new InvalidOperationException("ZoomStepper requires a PopoverHost ancestor to open its error popover.");
            if (!this.TryFindResource("Kit.PopoverFrame", null, out object? themeObj) || themeObj is not Avalonia.Styling.ControlTheme theme) {
                IsErrorPopoverOpen = false;
                return;
            }

            PopoverFrame popover = new() { Theme = theme, Anchor = readout };
            popover.Closed += OnErrorPopoverClosed;
            errorPopover = popover;
            popoverHost = host;
            host.Add(popover, () => {
                popover.Content = ErrorPopoverContent;
                popover.IsOpen = true;
            });
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ZoomStepperAutomationPeer(this);

        private sealed class ZoomStepperAutomationPeer(ZoomStepper owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string GetNameCore() => "Zoom";
        }

        private sealed class OpacitySink(Action<double> onNext) : IObserver<double> {
            public void OnNext(double value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
