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

        public ICommand? ClearCommand {
            get => GetValue(ClearCommandProperty);
            set => SetValue(ClearCommandProperty, value);
        }

        public event EventHandler? MoveDownRequested;
        public event EventHandler? SearchCommitted;

        private Button? clearButton;

        static SearchField() {
            PlaceholderTextProperty.OverrideDefaultValue<SearchField>("Search");
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (clearButton is not null)
                clearButton.Click -= OnClearClicked;
            clearButton = e.NameScope.Find<Button>("PART_Clear");
            if (clearButton is not null)
                clearButton.Click += OnClearClicked;
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
