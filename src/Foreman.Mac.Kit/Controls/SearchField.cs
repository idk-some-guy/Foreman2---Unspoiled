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

        public static readonly StyledProperty<string?> MatchStatusProperty =
            AvaloniaProperty.Register<SearchField, string?>(nameof(MatchStatus));

        private const string NoMatchesText = "No matches";

        public ICommand? ClearCommand {
            get => GetValue(ClearCommandProperty);
            set => SetValue(ClearCommandProperty, value);
        }

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
        public event EventHandler? SearchCommittedBackward;

        private Button? clearButton;
        private Viewbox? leadingGlyphBox;
        private Spinner? loadingSpinner;
        private TextBlock? matchStatusText;
        private IDisposable? loadingRevealSub;
        private IDisposable? loadingHideSub;
        private IDisposable? countFloorSub;
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

        private void ApplyLoadingState() {
            if (leadingGlyphBox is null || loadingSpinner is null)
                return;

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
