using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Windows.Input;

namespace Foreman.Mac.Kit.Controls {
    public class SearchField : TextBox {
        protected override Type StyleKeyOverride => typeof(SearchField);

        public static readonly StyledProperty<ICommand?> ClearCommandProperty =
            AvaloniaProperty.Register<SearchField, ICommand?>(nameof(ClearCommand));

        public static readonly StyledProperty<bool> IsLoadingProperty =
            AvaloniaProperty.Register<SearchField, bool>(nameof(IsLoading));

        //Find node's "N of M"/"No matches" readout; null hides the slot, same as an empty string.
        public static readonly StyledProperty<string?> MatchStatusProperty =
            AvaloniaProperty.Register<SearchField, string?>(nameof(MatchStatus));

        private const string NoMatchesText = "No matches";

        public ICommand? ClearCommand {
            get => GetValue(ClearCommandProperty);
            set => SetValue(ClearCommandProperty, value);
        }

        //Grace period before the leading glyph swaps for a spinner (Motion.StatePanelDelay) - a fast load never flashes it.
        public bool IsLoading {
            get => GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        public string? MatchStatus {
            get => GetValue(MatchStatusProperty);
            set => SetValue(MatchStatusProperty, value);
        }

        public event EventHandler? MoveDownRequested;
        public event EventHandler? SearchCommitted;
        //Shift+Return - the backward twin of SearchCommitted (⇧⌘G's Return equivalent).
        public event EventHandler? SearchCommittedBackward;

        private Button? clearButton;
        private Viewbox? leadingGlyphBox;
        private Spinner? loadingSpinner;
        private TextBlock? matchStatusText;
        private IDisposable? loadingRevealSub;
        private IDisposable? loadingHideSub;
        private IDisposable? countFloorSub;
        //Foreman.Metrics.SearchFieldCountFloor's own resolved value; the ruled 168 fallback until the observable delivers.
        private double countFloor = 168;

        static SearchField() {
            PlaceholderTextProperty.OverrideDefaultValue<SearchField>("Search");
            IsLoadingProperty.Changed.AddClassHandler<SearchField>((f, _) => f.ApplyLoadingState());
            MatchStatusProperty.Changed.AddClassHandler<SearchField>((f, _) => f.ApplyMatchStatus());
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (clearButton is not null)
                clearButton.Click -= OnClearClicked;
            clearButton = e.NameScope.Find<Button>("PART_Clear");
            if (clearButton is not null)
                clearButton.Click += OnClearClicked;

            leadingGlyphBox = e.NameScope.Find<Viewbox>("PART_LeadingGlyphBox");
            loadingSpinner = e.NameScope.Find<Spinner>("PART_LoadingSpinner");
            matchStatusText = e.NameScope.Find<TextBlock>("PART_MatchStatus");
            ApplyLoadingState();
            ApplyMatchStatus();
        }

        protected override Size ArrangeOverride(Size finalSize) {
            Size result = base.ArrangeOverride(finalSize);
            bool hidden = finalSize.Width < countFloor;
            if (hidden != PseudoClasses.Contains(":match-status-hidden")) {
                PseudoClasses.Set(":match-status-hidden", hidden);
                ApplyMatchStatus();
            }
            return result;
        }

        //Only the drawn slot respects the count floor - AXHelp and the tooltip carry the status at every width (ruling §04/§05).
        private void ApplyMatchStatus() {
            PseudoClasses.Set(":match-status-none", MatchStatus == NoMatchesText);
            AutomationProperties.SetHelpText(this, MatchStatus);
            ToolTip.SetTip(this, MatchStatus);
            if (matchStatusText is null)
                return;
            matchStatusText.Text = MatchStatus;
            matchStatusText.IsVisible = !string.IsNullOrEmpty(MatchStatus) && !PseudoClasses.Contains(":match-status-hidden");
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            countFloorSub = this.GetResourceObservable("Foreman.Metrics.SearchFieldCountFloor", ToCountFloor)
                .Subscribe(new ResourceSinks.DoubleSink(v => { countFloor = v; InvalidateArrange(); }, countFloor));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            loadingRevealSub?.Dispose();
            loadingRevealSub = null;
            loadingHideSub?.Dispose();
            loadingHideSub = null;
            countFloorSub?.Dispose();
            countFloorSub = null;
        }

        private static object ToCountFloor(object? value) => value is double d ? d : 168.0;

        //Delayed reveal via a held-then-animated Opacity transition (ZoomStepper.cs's idiom), never a wall-clock timer:
        //IsLoading clearing before the grace elapses cancels the pending reveal without a flash.
        private void ApplyLoadingState() {
            if (leadingGlyphBox is null || loadingSpinner is null)
                return;

            //Fresh Transitions every call (ZoomStepper's ApplyStateVisuals idiom) cancels whatever fade was already in flight.
            loadingRevealSub?.Dispose();
            loadingRevealSub = null;
            loadingHideSub?.Dispose();
            loadingHideSub = null;

            if (IsLoading) {
                loadingSpinner.Transitions = Motion.Transition(TransitionKind.CrossFade, Motion.TabLabelCrossFadeDuration, delay: Motion.StatePanelDelay);
                loadingRevealSub = loadingSpinner.GetObservable(Visual.OpacityProperty).Subscribe(new OpacitySink(v => {
                    if (v <= 0)
                        return;
                    loadingSpinner.IsVisible = true;
                    leadingGlyphBox.IsVisible = false;
                    loadingRevealSub?.Dispose();
                    loadingRevealSub = null;
                }));
                loadingSpinner.Opacity = 1;
            } else {
                loadingSpinner.Transitions = Motion.Transition(TransitionKind.CrossFade, Motion.TabLabelCrossFadeDuration);
                if (loadingSpinner.Opacity <= 0) {
                    loadingSpinner.Opacity = 0;
                    loadingSpinner.IsVisible = false;
                    leadingGlyphBox.IsVisible = true;
                } else {
                    loadingSpinner.Opacity = 0;
                    loadingHideSub = loadingSpinner.GetObservable(Visual.OpacityProperty).Subscribe(new OpacitySink(v => {
                        if (v > 0)
                            return;
                        loadingSpinner.IsVisible = false;
                        leadingGlyphBox.IsVisible = true;
                        loadingHideSub?.Dispose();
                        loadingHideSub = null;
                    }));
                }
            }
        }

        private sealed class OpacitySink(Action<double> onNext) : IObserver<double> {
            public void OnNext(double value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            switch (e.Key) {
                case Key.Escape:
                    PerformClearOrResignFocus();
                    e.Handled = true;
                    return;
                case Key.Down:
                    MoveDownRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                case Key.Return:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        SearchCommittedBackward?.Invoke(this, EventArgs.Empty);
                    else
                        SearchCommitted?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
            }
            base.OnKeyDown(e);
        }

        private void OnClearClicked(object? sender, RoutedEventArgs e) => PerformClear();

        //Esc on a non-empty field clears it without moving focus; on an already-empty field it resigns instead.
        private void PerformClearOrResignFocus() {
            if (!string.IsNullOrEmpty(Text)) {
                PerformClear();
                return;
            }

            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null, NavigationMethod.Unspecified);
        }

        private void PerformClear() {
            Clear();
            if (ClearCommand?.CanExecute(null) == true)
                ClearCommand.Execute(null);
        }
    }
}
