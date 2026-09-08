using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Mac.Themes;
using Foreman.Mac.ViewModels;
using System;
using System.Linq;

namespace Foreman.Mac.Views.QuickAdd {
    public sealed partial class QuickAddPalette : UserControl, IPanelInitialFocus, IPanelContentResize {
        private readonly TextBox searchBox;
        private readonly ItemsControl results;
        private QuickAddViewModel? subscribedVm;

        //Parameterless so the XAML runtime loader can find it (AVLN3001) - the caller assigns DataContext
        //separately and OnDataContextChanged below picks it up.
        public QuickAddPalette() {
            InitializeComponent();
            searchBox = this.FindControl<TextBox>("SearchBox")!;
            results = this.FindControl<ItemsControl>("Results")!;
            searchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(Button.ClickEvent, OnRowClick);
            DataContextChanged += OnDataContextChanged;
            results.ContainerPrepared += OnRowContainerPrepared;
        }

        public event EventHandler<AddCandidate>? PickCommitted;
        public event EventHandler<string>? BrowseRequested;
        public event EventHandler? CloseRequested;
        public event EventHandler? ContentResized;

        private QuickAddViewModel? Vm => DataContext as QuickAddViewModel;

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnDataContextChanged(object? sender, EventArgs e) {
            if (subscribedVm is not null)
                subscribedVm.RowsChanged -= OnVmRowsChanged;
            subscribedVm = Vm;
            if (subscribedVm is not null)
                subscribedVm.RowsChanged += OnVmRowsChanged;
        }

        private void OnVmRowsChanged(object? sender, EventArgs e) => ContentResized?.Invoke(this, EventArgs.Empty);

        private void OnSearchKeyDown(object? sender, KeyEventArgs e) {
            if (Vm is not QuickAddViewModel vm)
                return;
            switch (e.Key) {
                case Key.Down:
                    vm.MoveHighlight(+1);
                    BringHighlightIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.MoveHighlight(-1);
                    BringHighlightIntoView(vm);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
                case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                    BrowseRequested?.Invoke(this, vm.Query);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (vm.Commit() is { } candidate)
                        PickCommitted?.Invoke(this, candidate);
                    e.Handled = true;
                    break;
            }
        }

        //The ScrollViewer caps at 256px, well under the row count a search can return - keeps the
        //highlight on-screen as arrow keys move it past what's currently visible. Adjusts Offset
        //directly rather than Control.BringIntoView() - that routed event is a no-op in some windowing
        //states this control doesn't control.
        private void BringHighlightIntoView(QuickAddViewModel vm) {
            if (vm.HighlightedIndex < 0 || results.ContainerFromIndex(vm.HighlightedIndex) is not Control container)
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

        //A row starts life carrying .entering (Opacity 0, translated up 4px per the style in
        //Foreman.Styles.axaml) and sheds it a frame later, so the base style's own Transitions carry it
        //into place instead of the row just appearing. Gate fix 11: the Transitions are overridden
        //per-row here so a later row's entrance is delayed by its own stagger.
        private void OnRowContainerPrepared(object? sender, ContainerPreparedEventArgs e) {
            e.Container.Classes.Add("entering");
            e.Container.Transitions = EntranceTransitions(Motion.StaggerDelay(e.Index));
            Dispatcher.UIThread.Post(() => e.Container.Classes.Remove("entering"), DispatcherPriority.Loaded);
        }

        private static Transitions EntranceTransitions(TimeSpan delay) =>
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = Motion.EntranceDuration, Easing = Motion.EntranceEasing, Delay = delay },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Motion.EntranceDuration, Easing = Motion.EntranceEasing, Delay = delay },
        ];

        private void OnRowClick(object? sender, RoutedEventArgs e) {
            if (e.Source is Button { DataContext: QuickAddRow row })
                PickCommitted?.Invoke(this, row.Candidate);
        }

        public void FocusInitialControl() => searchBox.Focus();
    }
}
