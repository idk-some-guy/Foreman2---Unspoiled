using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System.ComponentModel;
using System.Linq;

namespace Foreman.Mac.Views {
    //MainWindow keeps one instance alive for the app's lifetime; closing here hides rather than destroys it.
    public sealed partial class AddBrowserWindow : Window {
        private readonly RadioButton itemsSegment;
        private readonly RadioButton recipesSegment;
        private readonly Button qualityButton;

        public AddBrowserWindow(AddBrowserViewModel vm) {
            InitializeComponent();
            DataContext = vm;

            itemsSegment = this.FindControl<RadioButton>("ItemsSegment")!;
            recipesSegment = this.FindControl<RadioButton>("RecipesSegment")!;
            qualityButton = this.FindControl<Button>("QualityButton")!;

            SyncSegmentToggle();
            itemsSegment.IsCheckedChanged += (_, _) => { if (itemsSegment.IsChecked == true) Vm.Segment = BrowserSegment.Items; };
            recipesSegment.IsCheckedChanged += (_, _) => { if (recipesSegment.IsChecked == true) Vm.Segment = BrowserSegment.Recipes; };

            AddHandler(Button.ClickEvent, OnCellOrGroupClicked);
            //handledEventsToo: Button's own OnKeyDown consumes Enter to raise its Click before this ever sees it.
            AddHandler(KeyDownEvent, OnCellKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
            DoubleTapped += OnCellDoubleTapped;
            vm.PropertyChanged += OnVmPropertyChanged;

            WindowScaler.Track(this);
        }

        private AddBrowserViewModel Vm => (AddBrowserViewModel)DataContext!;

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void SyncSegmentToggle() {
            itemsSegment.IsChecked = Vm.Segment == BrowserSegment.Items;
            recipesSegment.IsChecked = Vm.Segment == BrowserSegment.Recipes;
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case nameof(AddBrowserViewModel.Segment):
                    SyncSegmentToggle();
                    break;
                case nameof(AddBrowserViewModel.SelectedCell):
                    SyncSelectionClasses();
                    break;
            }
        }

        //A plain cell click doesn't rebuild Sections, so the template's IsSelected binding won't move on its own.
        private void SyncSelectionClasses() {
            var selected = Vm.SelectedCell?.Candidate;
            foreach (Button button in this.GetVisualDescendants().OfType<Button>())
                if (button.Classes.Contains("foreman-browser-cell") && button.DataContext is BrowserCell cell)
                    button.Classes.Set("selected", selected is not null && cell.Candidate.Equals(selected));
        }

        //Button.ClickEvent bubbles, so one handler here catches every cell and every group-sidebar button.
        private void OnCellOrGroupClicked(object? sender, RoutedEventArgs e) {
            switch (e.Source) {
                case Button { DataContext: BrowserCell cell }:
                    Vm.SelectCell(cell);
                    break;
                case Button { DataContext: BrowserGroup group }:
                    Vm.SelectGroup(group.Group);
                    break;
            }
        }

        private void OnQualityItemClick(object? sender, RoutedEventArgs e) {
            if (sender is Button { DataContext: IQuality quality }) {
                Vm.Quality = quality;
                qualityButton.Flyout?.Hide();
            }
        }

        private void OnSourceClick(object? sender, RoutedEventArgs e) => Vm.Place(NodeType.Supplier);
        private void OnPassthroughClick(object? sender, RoutedEventArgs e) => Vm.Place(NodeType.Passthrough);
        private void OnOutputClick(object? sender, RoutedEventArgs e) => Vm.Place(NodeType.Consumer);
        private void OnSpoilClick(object? sender, RoutedEventArgs e) => Vm.Place(NodeType.Spoil, NodeDirection.Up);
        private void OnUnspoilClick(object? sender, RoutedEventArgs e) => Vm.Place(NodeType.Spoil, NodeDirection.Down);
        private void OnPlantClick(object? sender, RoutedEventArgs e) => Vm.Place(NodeType.Plant, NodeDirection.Up);
        private void OnUnplantClick(object? sender, RoutedEventArgs e) => Vm.Place(NodeType.Plant, NodeDirection.Down);

        private void OnClearChipClick(object? sender, RoutedEventArgs e) => Vm.ClearChip();

        //Places a cell directly on double-click/Enter; Vm.PlaceDefault() picks the action.
        private void OnCellDoubleTapped(object? sender, TappedEventArgs e) {
            if (e.Source is Button { DataContext: BrowserCell cell })
                PlaceFromCell(cell);
        }

        private void OnCellKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key != Key.Enter || e.Source is not Button { DataContext: BrowserCell cell })
                return;
            e.Handled = true;
            PlaceFromCell(cell);
        }

        private void PlaceFromCell(BrowserCell cell) {
            Vm.SelectCell(cell);
            Vm.PlaceDefault();
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

        protected override void OnClosing(WindowClosingEventArgs e) {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            BeginMoveDrag(e);
        }
    }
}
