using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;

namespace Foreman.Mac.Views.QuickAdd {
    public sealed partial class QuickAddPalette : UserControl, IPanelInitialFocus, IViewportFittable {
        private readonly TextBox searchBox;

        public QuickAddPalette(QuickAddViewModel vm) {
            InitializeComponent();
            DataContext = vm;
            searchBox = this.FindControl<TextBox>("SearchBox")!;
            searchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(Button.ClickEvent, OnRowClick);
        }

        public event EventHandler<AddCandidate>? PickCommitted;
        public event EventHandler<string>? BrowseRequested;
        public event EventHandler? CloseRequested;

        private QuickAddViewModel Vm => (QuickAddViewModel)DataContext!;

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnSearchKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Down:
                    Vm.MoveHighlight(+1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    Vm.MoveHighlight(-1);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
                case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                    BrowseRequested?.Invoke(this, Vm.Query);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (Vm.Commit() is { } candidate)
                        PickCommitted?.Invoke(this, candidate);
                    e.Handled = true;
                    break;
            }
        }

        private void OnRowClick(object? sender, RoutedEventArgs e) {
            if (e.Source is Button { DataContext: QuickAddRow row })
                PickCommitted?.Invoke(this, row.Candidate);
        }

        public void FocusInitialControl() => searchBox.Focus();

        public void FitToViewport(double maxWidth, double maxHeight) { }
    }
}
