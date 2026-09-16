using Avalonia;
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

        public ICommand? ClearCommand {
            get => GetValue(ClearCommandProperty);
            set => SetValue(ClearCommandProperty, value);
        }

        //Grace period before the leading glyph swaps for a spinner (Motion.StatePanelDelay) - a fast load never flashes it.
        public bool IsLoading {
            get => GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        public event EventHandler? MoveDownRequested;
        public event EventHandler? SearchCommitted;

        private Button? clearButton;
        private Viewbox? leadingGlyphBox;
        private Spinner? loadingSpinner;
        private IDisposable? loadingRevealSub;
        private IDisposable? loadingHideSub;

        static SearchField() {
            PlaceholderTextProperty.OverrideDefaultValue<SearchField>("Search");
            IsLoadingProperty.Changed.AddClassHandler<SearchField>((f, _) => f.ApplyLoadingState());
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
            ApplyLoadingState();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            loadingRevealSub?.Dispose();
            loadingRevealSub = null;
            loadingHideSub?.Dispose();
            loadingHideSub = null;
        }

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
