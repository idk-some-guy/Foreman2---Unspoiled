using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;
using System.Linq;

namespace Foreman.Mac.Views.QuickAdd {
    public sealed partial class QuickAddPalette : UserControl, IPanelInitialFocus, IPanelContentResize {
        private readonly TextBox searchBox;
        private readonly ItemsControl results;

        public QuickAddPalette(QuickAddViewModel vm) {
            InitializeComponent();
            DataContext = vm;
            searchBox = this.FindControl<TextBox>("SearchBox")!;
            results = this.FindControl<ItemsControl>("Results")!;
            searchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(Button.ClickEvent, OnRowClick);
            vm.RowsChanged += (_, _) => ContentResized?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler<AddCandidate>? PickCommitted;
        public event EventHandler<string>? BrowseRequested;
        public event EventHandler? CloseRequested;
        public event EventHandler? ContentResized;

        private QuickAddViewModel Vm => (QuickAddViewModel)DataContext!;

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnSearchKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Down:
                    Vm.MoveHighlight(+1);
                    BringHighlightIntoView();
                    e.Handled = true;
                    break;
                case Key.Up:
                    Vm.MoveHighlight(-1);
                    BringHighlightIntoView();
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

        //The ScrollViewer caps at 256px, well under the row count a search can return - keeps the
        //highlight on-screen as arrow keys move it past what's currently visible. Adjusts Offset
        //directly rather than Control.BringIntoView() - that routed event is a no-op in some windowing
        //states this control doesn't control.
        private void BringHighlightIntoView() {
            if (Vm.HighlightedIndex < 0 || results.ContainerFromIndex(Vm.HighlightedIndex) is not Control container)
                return;
            if (results.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not ScrollViewer scrollViewer)
                return;
            if (container.TranslatePoint(new Point(0, 0), scrollViewer) is not Point topLeft)
                return;

            double bottom = topLeft.Y + container.Bounds.Height;
            double viewportHeight = scrollViewer.Viewport.Height;
            if (topLeft.Y < 0)
                scrollViewer.Offset += new Vector(0, topLeft.Y);
            else if (bottom > viewportHeight)
                scrollViewer.Offset += new Vector(0, bottom - viewportHeight);
        }

        private void OnRowClick(object? sender, RoutedEventArgs e) {
            if (e.Source is Button { DataContext: QuickAddRow row })
                PickCommitted?.Invoke(this, row.Candidate);
        }

        public void FocusInitialControl() => searchBox.Focus();
    }
}
