using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    public enum SheetWidthClass { Alert, Standard, Wide }

    public class SheetFrame : ContentControl {
        protected override Type StyleKeyOverride => typeof(SheetFrame);

        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<SheetFrame, string?>(nameof(Title));

        public static readonly StyledProperty<SheetWidthClass> WidthClassProperty =
            AvaloniaProperty.Register<SheetFrame, SheetWidthClass>(nameof(WidthClass), SheetWidthClass.Standard);

        public static readonly StyledProperty<ButtonRow?> ButtonsProperty =
            AvaloniaProperty.Register<SheetFrame, ButtonRow?>(nameof(Buttons));

        public static readonly StyledProperty<bool> IsOpenProperty =
            AvaloniaProperty.Register<SheetFrame, bool>(nameof(IsOpen));

        public static readonly StyledProperty<bool> IsBusyProperty =
            AvaloniaProperty.Register<SheetFrame, bool>(nameof(IsBusy));

        public static readonly StyledProperty<string?> BusyTextProperty =
            AvaloniaProperty.Register<SheetFrame, string?>(nameof(BusyText));

        public static readonly StyledProperty<object?> StateContentProperty =
            AvaloniaProperty.Register<SheetFrame, object?>(nameof(StateContent));

        public static readonly StyledProperty<double> TopInsetProperty =
            AvaloniaProperty.Register<SheetFrame, double>(nameof(TopInset));

        public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public SheetWidthClass WidthClass { get => GetValue(WidthClassProperty); set => SetValue(WidthClassProperty, value); }
        public ButtonRow? Buttons { get => GetValue(ButtonsProperty); set => SetValue(ButtonsProperty, value); }
        public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
        public bool IsBusy { get => GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
        public string? BusyText { get => GetValue(BusyTextProperty); set => SetValue(BusyTextProperty, value); }
        public object? StateContent { get => GetValue(StateContentProperty); set => SetValue(StateContentProperty, value); }
        public double TopInset { get => GetValue(TopInsetProperty); set => SetValue(TopInsetProperty, value); }

        public event EventHandler? Closed;
        public event EventHandler? Cancelled;

        private Border? panel;
        private Scrim? scrim;
        private TextBlock? titleText;
        private ContentPresenter? contentHost;
        private ContentPresenter? stateHost;
        private Grid? footer;
        private ButtonRow? buttonRow;
        private InputElement? focusToRestore;
        private IDisposable? widthBinding;
        private readonly List<IDisposable> tokenSubs = [];

        private double contentSpace5 = 24;
        private double titleBottomSpace3 = 12;
        private double footerTopSpace4 = 16;
        private double slide = 12;
        private bool closedInPlace;

        private bool? priorDefaultEnabled;

        static SheetFrame() {
            WidthClassProperty.Changed.AddClassHandler<SheetFrame>((f, _) => f.ApplyWidth());
            ButtonsProperty.Changed.AddClassHandler<SheetFrame>((f, e) => f.ApplyButtons(e.OldValue as ButtonRow));
            IsOpenProperty.Changed.AddClassHandler<SheetFrame>((f, e) => f.ApplyOpenState((bool)e.NewValue!));
            IsBusyProperty.Changed.AddClassHandler<SheetFrame>((f, _) => f.ApplyBusy());
            BusyTextProperty.Changed.AddClassHandler<SheetFrame>((f, _) => f.ApplyBusy());
            StateContentProperty.Changed.AddClassHandler<SheetFrame>((f, _) => f.ApplyStateContent());
            TopInsetProperty.Changed.AddClassHandler<SheetFrame>((f, _) => f.ApplyTopInset());
            TitleProperty.Changed.AddClassHandler<SheetFrame>((f, _) => f.ApplyTitle());
        }

        public SheetFrame() {
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            IsHitTestVisible = IsOpen;
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            panel = e.NameScope.Find<Border>("PART_Panel");
            scrim = e.NameScope.Find<Scrim>("PART_Scrim");
            titleText = e.NameScope.Find<TextBlock>("PART_Title");
            contentHost = e.NameScope.Find<ContentPresenter>("PART_ContentHost");
            stateHost = e.NameScope.Find<ContentPresenter>("PART_StateHost");
            footer = e.NameScope.Find<Grid>("PART_Footer");
            ApplyWidth();
            ApplyButtons(null);
            ApplyStateContent();
            ApplyBusy();
            ApplyTopInset();
            ApplyTitle();
            ApplyContentMargin();
            ApplyTitleSpacing();
            ApplyFooterSpacing();
            closedInPlace = Motion.ReduceMotion;
            SnapPanel(IsOpen || closedInPlace ? 0 : -slide);
            PseudoClasses.Set(":open", IsOpen);
            IsHitTestVisible = IsOpen;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
            ApplyWidth();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
            widthBinding?.Dispose();
            widthBinding = null;
        }

        private void Subscribe() {
            Unsubscribe();
            tokenSubs.Add(this.GetResourceObservable("Foreman.Space.Space5").Subscribe(new ResourceSinks.DoubleSink(v => {
                contentSpace5 = v;
                ApplyContentMargin();
            }, contentSpace5)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Space.Space3").Subscribe(new ResourceSinks.DoubleSink(v => {
                titleBottomSpace3 = v;
                ApplyTitleSpacing();
            }, titleBottomSpace3)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Space.Space4").Subscribe(new ResourceSinks.DoubleSink(v => {
                footerTopSpace4 = v;
                ApplyFooterSpacing();
            }, footerTopSpace4)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Metrics.SheetSlide").Subscribe(new ResourceSinks.DoubleSink(v => {
                slide = v;
                if (!IsOpen && !closedInPlace)
                    SnapPanel(-slide);
            }, slide)));
        }

        private static TransformOperations TranslateY(double y) =>
            TransformOperations.Parse($"translateY({y.ToString(CultureInfo.InvariantCulture)}px)");

        private void SnapPanel(double y) {
            if (panel is null)
                return;
            var transitions = panel.Transitions;
            panel.Transitions = null;
            panel.RenderTransform = TranslateY(y);
            panel.Transitions = transitions;
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in tokenSubs)
                sub.Dispose();
            tokenSubs.Clear();
        }

        private void ApplyContentMargin() {
            if (panel is not null)
                panel.Padding = new Thickness(Math.Max(0, contentSpace5 - 4));
        }

        private void ApplyTitleSpacing() {
            if (titleText is not null)
                titleText.Margin = new Thickness(0, 0, 0, titleBottomSpace3);
        }

        private void ApplyFooterSpacing() {
            if (footer is not null)
                footer.Margin = new Thickness(0, footerTopSpace4, 0, 0);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new SheetFrameAutomationPeer(this);

        private sealed class SheetFrameAutomationPeer(SheetFrame owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Window;
        }

        private void ApplyTopInset() {
            if (panel is not null)
                panel.Margin = new Thickness(0, TopInset, 0, 0);
        }

        private void ApplyTitle() {
            if (titleText is not null)
                titleText.IsVisible = !string.IsNullOrEmpty(Title);
            AutomationProperties.SetName(this, Title);
        }

        private void ApplyWidth() {
            if (panel is null)
                return;
            string key = WidthClass switch {
                SheetWidthClass.Alert => "Foreman.Metrics.SheetWidthAlert",
                SheetWidthClass.Wide => "Foreman.Metrics.SheetWidthWide",
                _ => "Foreman.Metrics.SheetWidthStandard",
            };
            widthBinding?.Dispose();
            widthBinding = panel.Bind(Layoutable.WidthProperty, this.GetResourceObservable(key));
        }

        private void ApplyButtons(ButtonRow? old) {
            ButtonRow? previouslyWired = old ?? buttonRow;
            if (previouslyWired is not null) {
                previouslyWired.DefaultInvoked -= OnDefaultInvoked;
                previouslyWired.CancelInvoked -= OnCancelInvoked;
                if (priorDefaultEnabled is { } prior && previouslyWired.ResolveDefaultButton() is { } outgoingDefault) {
                    outgoingDefault.IsEnabled = prior;
                    priorDefaultEnabled = null;
                }
            }
            buttonRow = Buttons;
            if (buttonRow is not null) {
                buttonRow.DefaultInvoked += OnDefaultInvoked;
                buttonRow.CancelInvoked += OnCancelInvoked;
            }
            ApplyBusy();
        }

        private void OnDefaultInvoked(object? sender, ButtonRowRole role) {
            if (IsBusy)
                return;
            if (role == ButtonRowRole.Cancel)
                Cancelled?.Invoke(this, EventArgs.Empty);
            else
                Closed?.Invoke(this, EventArgs.Empty);
        }

        private void OnCancelInvoked(object? sender, EventArgs e) => Cancelled?.Invoke(this, EventArgs.Empty);

        private void ApplyStateContent() {
            if (contentHost is null || stateHost is null)
                return;
            bool showState = StateContent is not null;
            stateHost.IsVisible = showState;
            contentHost.IsVisible = !showState;
        }

        private void ApplyBusy() {
            if (buttonRow?.ResolveDefaultButton() is not { } def)
                return;
            if (IsBusy) {
                priorDefaultEnabled ??= def.IsEnabled;
                def.IsEnabled = false;
            } else if (priorDefaultEnabled is { } prior) {
                def.IsEnabled = prior;
                priorDefaultEnabled = null;
            }
        }

        //Layout maths (MaxWidth clamp) lives here, never a LayoutUpdated handler writing it back.
        protected override Size ArrangeOverride(Size finalSize) {
            if (panel is not null)
                panel.MaxWidth = Math.Max(0, finalSize.Width - 24);
            return base.ArrangeOverride(finalSize);
        }

        //Reduce Motion is read at each open and close, so a live toggle takes effect on the next one; under it the panel never moves.
        private void ApplyOpenState(bool open) {
            TimeSpan duration = open ? Motion.SheetOpenDuration : Motion.SheetCloseDuration;
            Avalonia.Animation.Easings.Easing easing = open ? Motion.EntranceEasing : Motion.LeavingEasing;
            bool reduce = Motion.ReduceMotion;
            if (panel is not null) {
                if (open && !reduce)
                    SnapPanel(-slide);
                panel.Transitions = Motion.Transition(TransitionKind.SlideFade, duration, easing);
                panel.RenderTransform = TranslateY(open || reduce ? 0 : -slide);
            }
            closedInPlace = !open && reduce;
            if (scrim is not null)
                scrim.Transitions = Motion.Transition(TransitionKind.Fade, duration, easing);

            PseudoClasses.Set(":open", open);
            IsHitTestVisible = open;
            if (open) {
                focusToRestore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as InputElement;
                //Deferred: content isn't realized into the visual tree until its template applies.
                Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Loaded);
            } else {
                focusToRestore?.Focus();
                focusToRestore = null;
            }
        }

        private void FocusFirst() {
            if (contentHost is null)
                return;
            InputElement? target = contentHost.GetVisualDescendants().OfType<InputElement>()
                .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled);
            target?.Focus();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (!IsOpen)
                return;
            switch (e.Key) {
                case Key.Enter:
                    if (e.Source is TextBox { AcceptsReturn: true })
                        return;
                    if (buttonRow?.TryInvokeDefault() == true)
                        e.Handled = true;
                    break;
                case Key.Escape:
                    e.Handled = true;
                    if (buttonRow?.TryInvokeCancel() != true)
                        Cancelled?.Invoke(this, EventArgs.Empty);
                    break;
                case Key.OemPeriod when e.KeyModifiers == KeyModifiers.Meta:
                    e.Handled = true;
                    if (buttonRow?.TryInvokeCancel() != true)
                        Cancelled?.Invoke(this, EventArgs.Empty);
                    break;
                case Key.Back when e.KeyModifiers == KeyModifiers.Meta:
                    if (buttonRow?.TryInvokeDestructive() == true)
                        e.Handled = true;
                    break;
                case Key.Tab:
                    TrapTab(e);
                    break;
            }
        }

        private void TrapTab(KeyEventArgs e) {
            InputElement[] focusables = [.. this.GetVisualDescendants().OfType<InputElement>().Where(c => c.Focusable && c.IsEffectivelyEnabled)];
            if (focusables.Length == 0)
                return;
            InputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as InputElement;
            int index = focused is null ? -1 : Array.IndexOf(focusables, focused);
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (shift && index == 0) {
                focusables[^1].Focus(NavigationMethod.Tab);
                e.Handled = true;
            } else if (!shift && index == focusables.Length - 1) {
                focusables[0].Focus(NavigationMethod.Tab);
                e.Handled = true;
            }
        }
    }
}
