using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Foreman.Mac.Views {
    //MainWindow keeps one instance alive for the app's lifetime; closing here hides rather than destroys it.
    public sealed partial class AddBrowserWindow : Window {
        //Row metrics mirroring the "foreman-browser-group" markup/style - kept in sync by hand.
        private const double GroupRowIconWidth = 16;
        private const double GroupRowIconGap = 8;
        private const double GroupRowHorizontalPadding = 12;

        private readonly RadioButton itemsSegment;
        private readonly RadioButton recipesSegment;
        private readonly Button qualityButton;
        private readonly Border sidebar;
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

            itemsSegment.IsCheckedChanged += (_, _) => { if (itemsSegment.IsChecked == true && Vm is AddBrowserViewModel vm) vm.Segment = BrowserSegment.Items; };
            recipesSegment.IsCheckedChanged += (_, _) => { if (recipesSegment.IsChecked == true && Vm is AddBrowserViewModel vm) vm.Segment = BrowserSegment.Recipes; };

            AddHandler(Button.ClickEvent, OnCellOrGroupClicked);
            //handledEventsToo: Button's own OnKeyDown consumes Enter to raise its Click before this ever sees it.
            AddHandler(KeyDownEvent, OnCellKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            DoubleTapped += OnCellDoubleTapped;
            DataContextChanged += OnDataContextChanged;
            //rows realise after DataContext is set; recheck tooltips once layout catches up
            LayoutUpdated += (_, _) => UpdateSidebarWidth();

            WindowScaler.Track(this);
        }

        private AddBrowserViewModel? Vm => DataContext as AddBrowserViewModel;

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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

        //Sidebar sizes to its widest group row, floored at BrowserSidebar and capped at BrowserSidebarMax.
        private void UpdateSidebarWidth() {
            if (Vm is not AddBrowserViewModel vm)
                return;
            double floor = Resource<double>("Foreman.Size.BrowserSidebar");
            double cap = Resource<double>("Foreman.Size.BrowserSidebarMax");
            double rowFontSize = Resource<double>("Foreman.Type.Browser.Row.FontSize");
            var typeface = new Typeface(FontFamily.Default);
            //Icon + gap + row padding + the Sidebar Border's own padding (the previous formula's bug).
            double chrome = GroupRowIconWidth + GroupRowIconGap + GroupRowHorizontalPadding + sidebar.Padding.Left + sidebar.Padding.Right;

            double widest = 0;
            foreach (BrowserGroup group in vm.Groups)
                widest = Math.Max(widest, chrome + MeasureText(group.Name, typeface, rowFontSize).Width);

            sidebar.Width = Math.Min(cap, Math.Max(floor, widest));
            RefreshGroupTooltips(typeface, rowFontSize, chrome);
        }

        //Only a label that actually needs trimming gets a tooltip.
        private void RefreshGroupTooltips(Typeface typeface, double rowFontSize, double chrome) {
            double availableTextWidth = sidebar.Width - chrome;
            foreach (TextBlock label in this.GetVisualDescendants().OfType<TextBlock>()) {
                if (label.DataContext is not BrowserGroup group)
                    continue;
                bool trimmed = MeasureText(group.Name, typeface, rowFontSize).Width > availableTextWidth;
                ToolTip.SetTip(label, trimmed ? group.Name : null);
            }
        }

        private static FormattedText MeasureText(string text, Typeface typeface, double fontSize) =>
            new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black);

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
