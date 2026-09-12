using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Foreman.Mac.Views {
    //MainWindow keeps one instance alive for the app's lifetime; closing here hides rather than destroys it.
    public sealed partial class AddBrowserWindow : Window, IThemeAware {
        private readonly RadioButton itemsSegment;
        private readonly RadioButton recipesSegment;
        private readonly Button qualityButton;
        private readonly Border sidebar;
        private readonly ItemsControl groupsList;
        private AddBrowserViewModel? subscribedVm;
        private int lastPointerClickCount = 1;

        //Parameterless so the XAML runtime loader can find it (AVLN3001) - the caller assigns DataContext
        //separately and OnDataContextChanged below picks it up.
        public AddBrowserWindow() {
            InitializeComponent();

            itemsSegment = this.FindControl<RadioButton>("ItemsSegment")!;
            recipesSegment = this.FindControl<RadioButton>("RecipesSegment")!;
            qualityButton = this.FindControl<Button>("QualityButton")!;
            sidebar = this.FindControl<Border>("Sidebar")!;
            groupsList = this.FindControl<ItemsControl>("GroupsList")!;

            itemsSegment.IsCheckedChanged += (_, _) => { if (itemsSegment.IsChecked == true && Vm is AddBrowserViewModel vm) vm.Segment = BrowserSegment.Items; };
            recipesSegment.IsCheckedChanged += (_, _) => { if (recipesSegment.IsChecked == true && Vm is AddBrowserViewModel vm) vm.Segment = BrowserSegment.Recipes; };

            AddHandler(Button.ClickEvent, OnCellOrGroupClicked);
            //handledEventsToo: Button's own OnKeyDown consumes Enter to raise its Click before this ever sees it.
            AddHandler(KeyDownEvent, OnCellKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            DoubleTapped += OnCellDoubleTapped;
            DataContextChanged += OnDataContextChanged;
            groupsList.ContainerPrepared += OnGroupContainerPrepared;

            WindowScaler.Track(this);

            //IsVisible, not Closed, is this window's real open/shut signal (MainWindow hides rather than
            //closes it); Closed below is just the backstop for a standalone instance.
            PropertyChanged += OnIsVisibleChanged;
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        private AddBrowserViewModel? Vm => DataContext as AddBrowserViewModel;

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnIsVisibleChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property != IsVisibleProperty)
                return;
            if (IsVisible)
                AppearanceHub.Current?.Subscribe(this);
            else
                AppearanceHub.Current?.Unsubscribe(this);
        }

        //Cell/group chrome is entirely DynamicResource-bound already (free on a theme flip); the sidebar's
        //measured width is the one thing this window computes in code, off live text metrics that a scale
        //change can move.
        public void RefreshTheme() => UpdateSidebarWidth();

        private void OnDataContextChanged(object? sender, EventArgs e) {
            if (subscribedVm is not null) {
                subscribedVm.PropertyChanged -= OnVmPropertyChanged;
                ((INotifyCollectionChanged)subscribedVm.Groups).CollectionChanged -= OnGroupsChanged;
            }
            subscribedVm = Vm;
            if (subscribedVm is not null) {
                SyncSegmentToggle(subscribedVm);
                subscribedVm.PropertyChanged += OnVmPropertyChanged;
                ((INotifyCollectionChanged)subscribedVm.Groups).CollectionChanged += OnGroupsChanged;
                UpdateSidebarWidth();
            }
        }

        private void OnGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSidebarWidth();

        //A prepared container's own content (the label inside) isn't realised until it's loaded.
        private void OnGroupContainerPrepared(object? sender, ContainerPreparedEventArgs e) {
            if (e.Container.IsLoaded) {
                UpdateSidebarWidth();
                return;
            }
            e.Container.Loaded += OnGroupContainerLoaded;
        }

        //A cell starts life carrying .entering (Opacity 0 per the style in Foreman.Styles.axaml) and sheds
        //it a frame later, with its own stagger Delay, so cells fade in top-down instead of all at once.
        //Wired to the Button's own Loaded, not the parent ItemsControl's ContainerPrepared - the latter
        //fires against a throwaway instance TransitioningContentControl discards before the real swap.
        private void OnCellLoaded(object? sender, RoutedEventArgs e) {
            if (sender is not Control cell || cell.DataContext is not BrowserCell data)
                return;
            int index = cell.FindAncestorOfType<ItemsControl>() is ItemsControl { ItemsSource: IEnumerable<BrowserCell> siblings }
                ? IndexOf(siblings, data)
                : 0;
            cell.Classes.Add("entering");
            cell.Transitions = EntranceTransitions(Motion.StaggerDelay(index));
            Dispatcher.UIThread.Post(() => cell.Classes.Remove("entering"), DispatcherPriority.Loaded);
        }

        //Not IndexFromContainer - it isn't reliable here (see OnCellLoaded above).
        private static int IndexOf(IEnumerable<BrowserCell> cells, BrowserCell target) {
            int index = 0;
            foreach (BrowserCell cell in cells) {
                if (cell.Equals(target))
                    return index;
                index++;
            }
            return 0;
        }

        private static Transitions EntranceTransitions(TimeSpan delay) =>
            Motion.Transition(TransitionKind.SlideFade, Motion.EntranceDuration, Motion.EntranceEasing, delay);

        private void OnGroupContainerLoaded(object? sender, RoutedEventArgs e) {
            ((Control)sender!).Loaded -= OnGroupContainerLoaded;
            UpdateSidebarWidth();
        }

        //Gate fix 7, item 5: measures the realised rows themselves (Measure(Size.Infinity), the same probe
        //FloatingPanelHost uses to fit its own content) instead of a hand-rolled icon/gap/padding sum - that
        //sum was wrong against the real font metrics and let a wide preset group name clip past the sidebar's
        //right edge. Sizes to the widest row, floored at BrowserSidebar and capped at BrowserSidebarMax.
        private void UpdateSidebarWidth() {
            if (Vm is null)
                return;
            double floor = Resource<double>("Foreman.Size.BrowserSidebar");
            double cap = Resource<double>("Foreman.Size.BrowserSidebarMax");

            double widestRow = 0;
            foreach (Button row in GroupRows())
                widestRow = Math.Max(widestRow, MeasuredWidth(row));

            double width = Math.Min(cap, Math.Max(floor, widestRow + sidebar.Padding.Left + sidebar.Padding.Right));
            if (sidebar.Width != width)
                sidebar.Width = width;
            RefreshGroupTooltips(width);
        }

        //Only a label that actually needs trimming gets a tooltip; SetTip is skipped when the decision
        //for that row hasn't changed, so a container-realisation pass over rows already tipped is a no-op.
        private void RefreshGroupTooltips(double sidebarWidth) {
            double availableRowWidth = sidebarWidth - sidebar.Padding.Left - sidebar.Padding.Right;
            foreach (Button row in GroupRows()) {
                if (row.DataContext is not BrowserGroup group)
                    continue;
                TextBlock? label = row.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                if (label is null)
                    continue;
                bool trimmed = MeasuredWidth(row) > availableRowWidth + 0.5;
                string? desired = trimmed ? group.Name : null;
                if (ToolTip.GetTip(label) as string != desired)
                    ToolTip.SetTip(label, desired);
            }
        }

        private IEnumerable<Button> GroupRows() =>
            this.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-group"));

        private static double MeasuredWidth(Button row) {
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return row.DesiredSize.Width;
        }

        private static T Resource<T>(string key) {
            Application app = Application.Current!;
            return app.TryGetResource(key, app.ActualThemeVariant, out object? value) && value is T typed
                ? typed
                : throw new InvalidOperationException($"AddBrowserWindow: resource '{key}' not found.");
        }

        private void SyncSegmentToggle(AddBrowserViewModel vm) {
            itemsSegment.IsChecked = vm.Segment == BrowserSegment.Items;
            recipesSegment.IsChecked = vm.Segment == BrowserSegment.Recipes;
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (Vm is not AddBrowserViewModel vm)
                return;
            switch (e.PropertyName) {
                case nameof(AddBrowserViewModel.Segment):
                    SyncSegmentToggle(vm);
                    break;
                case nameof(AddBrowserViewModel.SelectedCell):
                    SyncSelectionClasses(vm);
                    break;
            }
        }

        //A plain cell click doesn't rebuild Sections, so the template's IsSelected binding won't move on its own.
        private void SyncSelectionClasses(AddBrowserViewModel vm) {
            var selected = vm.SelectedCell?.Candidate;
            foreach (Button button in this.GetVisualDescendants().OfType<Button>())
                if (button.Classes.Contains("foreman-browser-cell") && button.DataContext is BrowserCell cell)
                    button.Classes.Set("selected", selected is not null && cell.Candidate.Equals(selected));
        }

        //Stashed for OnCellOrGroupClicked below - Button.ClickEvent itself carries no ClickCount.
        private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e) => lastPointerClickCount = e.ClickCount;

        private void OnCellOrGroupClicked(object? sender, RoutedEventArgs e) {
            if (Vm is not AddBrowserViewModel vm)
                return;
            switch (e.Source) {
                case Button { DataContext: BrowserCell cell } when vm.Segment == BrowserSegment.Recipes && cell.Candidate.Kind == AddKind.Recipe:
                    vm.SelectCell(cell);
                    if (lastPointerClickCount == 1)
                        vm.PlaceSelectedRecipe();
                    break;
                case Button { DataContext: BrowserCell cell }:
                    vm.SelectCell(cell);
                    break;
                case Button { DataContext: BrowserGroup group }:
                    vm.SelectGroup(group.Group);
                    break;
            }
        }

        private void OnQualityItemClick(object? sender, RoutedEventArgs e) {
            if (Vm is AddBrowserViewModel vm && sender is Button { DataContext: IQuality quality }) {
                vm.Quality = quality;
                qualityButton.Flyout?.Hide();
            }
        }

        private void OnSourceClick(object? sender, RoutedEventArgs e) => Vm?.Place(NodeType.Supplier);
        private void OnPassthroughClick(object? sender, RoutedEventArgs e) => Vm?.Place(NodeType.Passthrough);
        private void OnOutputClick(object? sender, RoutedEventArgs e) => Vm?.Place(NodeType.Consumer);
        private void OnSpoilClick(object? sender, RoutedEventArgs e) => Vm?.Place(NodeType.Spoil, NodeDirection.Up);
        private void OnUnspoilClick(object? sender, RoutedEventArgs e) => Vm?.Place(NodeType.Spoil, NodeDirection.Down);
        private void OnPlantClick(object? sender, RoutedEventArgs e) => Vm?.Place(NodeType.Plant, NodeDirection.Up);
        private void OnUnplantClick(object? sender, RoutedEventArgs e) => Vm?.Place(NodeType.Plant, NodeDirection.Down);

        private void OnClearChipClick(object? sender, RoutedEventArgs e) => Vm?.ClearChip();

        //A Recipes cell already placed on its first press - ignore the DoubleTapped that follows.
        private void OnCellDoubleTapped(object? sender, TappedEventArgs e) {
            if (Vm is not AddBrowserViewModel vm || e.Source is not Button { DataContext: BrowserCell cell })
                return;
            if (vm.Segment == BrowserSegment.Recipes && cell.Candidate.Kind == AddKind.Recipe)
                return;
            vm.SelectCell(cell);
            vm.PlaceDefault();
        }

        //Enter always places, regardless of any preceding click.
        private void OnCellKeyDown(object? sender, KeyEventArgs e) {
            if (Vm is not AddBrowserViewModel vm || e.Key != Key.Enter || e.Source is not Button { DataContext: BrowserCell cell })
                return;
            e.Handled = true;
            vm.SelectCell(cell);
            if (vm.Segment == BrowserSegment.Recipes && cell.Candidate.Kind == AddKind.Recipe)
                vm.PlaceSelectedRecipe();
            else
                vm.PlaceDefault();
        }

        //⌘W/Esc both hide rather than close - MainWindow owns one instance for the app's lifetime.
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))) {
                e.Handled = true;
                Hide();
                return;
            }
            base.OnKeyDown(e);
        }

        //Owner-close and app shutdown cascade a Closing here first - only a direct close of this window
        //(the red button, Cmd+W's own Hide() already short-circuits before this) should cancel-and-hide.
        protected override void OnClosing(WindowClosingEventArgs e) {
            if (e.CloseReason == WindowCloseReason.WindowClosing) {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            BeginMoveDrag(e);
        }
    }
}
