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
