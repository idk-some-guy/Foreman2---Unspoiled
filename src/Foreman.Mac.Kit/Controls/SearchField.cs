using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        private IDisposable? loadingDelayHandle;

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
            loadingDelayHandle?.Dispose();
            loadingDelayHandle = null;
        }

        private void ApplyLoadingState() {
            loadingDelayHandle?.Dispose();
            loadingDelayHandle = null;
            if (IsLoading)
                loadingDelayHandle = DispatcherTimer.RunOnce(ShowLoadingSpinner, Motion.StatePanelDelay);
            else
                SetLoadingVisual(false);
        }

        private void ShowLoadingSpinner() {
            loadingDelayHandle = null;
            if (IsLoading)
                SetLoadingVisual(true);
        }

        private void SetLoadingVisual(bool loading) {
            if (leadingGlyphBox is not null)
                leadingGlyphBox.IsVisible = !loading;
            if (loadingSpinner is not null)
                loadingSpinner.IsVisible = loading;
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
