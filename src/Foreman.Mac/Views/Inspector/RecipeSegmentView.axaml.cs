using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.ViewModels;
using System;
using System.ComponentModel;

namespace Foreman.Mac.Views.Inspector {
    //The inspector's Rate/Assembler/Fuel/Modules/Beacon groups for a recipe node. RateAuto isn't
    //bound in XAML - its RadioButton grouping with RateFixed already flips RateFixed.IsChecked (and so
    //RecipeSegmentViewModel.IsFixedRate, via its own TwoWay binding) whenever Auto gets picked instead -
    //same pattern as FlowSegmentView.
    public partial class RecipeSegmentView : UserControl {
        private readonly RadioButton rateAuto;
        private readonly PickGrid assemblerGrid;
        private readonly PickGrid fuelGrid;
        private readonly PickGrid equippedGrid;
        private readonly PickGrid moduleOptionsGrid;
        private readonly PickGrid beaconOptionsGrid;
        private readonly PickGrid equippedBeaconGrid;
        private readonly PickGrid beaconModuleOptionsGrid;
        private readonly Button clearBeaconButton;
        private readonly InspectorGroup beaconGroup;
        private GraphViewer? boundViewer;
        private bool lastHasBeacon;

        public RecipeSegmentViewModel? ViewModel { get; private set; }

        public RecipeSegmentView() {
            InitializeComponent();
            rateAuto = this.FindControl<RadioButton>("RateAuto")!;
            assemblerGrid = this.FindControl<PickGrid>("AssemblerGrid")!;
            fuelGrid = this.FindControl<PickGrid>("FuelGrid")!;
            equippedGrid = this.FindControl<PickGrid>("EquippedGrid")!;
            moduleOptionsGrid = this.FindControl<PickGrid>("ModuleOptionsGrid")!;
            beaconOptionsGrid = this.FindControl<PickGrid>("BeaconOptions")!;
            equippedBeaconGrid = this.FindControl<PickGrid>("EquippedBeaconGrid")!;
            beaconModuleOptionsGrid = this.FindControl<PickGrid>("BeaconModuleOptionsGrid")!;
            clearBeaconButton = this.FindControl<Button>("ClearBeaconButton")!;
            beaconGroup = this.FindControl<InspectorGroup>("BeaconGroup")!;
            rateAuto.IsCheckedChanged += RateAuto_IsCheckedChanged;
            assemblerGrid.ItemActivated += (_, item) => ViewModel?.PickAssembler(item);
            fuelGrid.ItemActivated += (_, item) => ViewModel?.PickFuel(item);
            equippedGrid.ItemActivated += (_, item) => ViewModel?.RemoveEquippedModule(item);
            moduleOptionsGrid.ItemActivated += (_, item) => ViewModel?.AddModule(item);
            beaconOptionsGrid.ItemActivated += (_, item) => ViewModel?.PickBeacon(item);
            equippedBeaconGrid.ItemActivated += (_, item) => ViewModel?.RemoveEquippedBeaconModule(item);
            beaconModuleOptionsGrid.ItemActivated += (_, item) => ViewModel?.AddBeaconModule(item);
            clearBeaconButton.Click += (_, _) => ViewModel?.ClearBeacon();
            DetachedFromVisualTree += (_, _) => Detach();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal void Bind(IRecipeNodeViewModel node, GraphViewer viewer, DataCache cache, Action? requestRedraw = null) {
            Detach();
            boundViewer = viewer;
            ViewModel = new RecipeSegmentViewModel(node, viewer, cache, requestRedraw);
            DataContext = ViewModel;
            rateAuto.IsChecked = !ViewModel.IsFixedRate;
            lastHasBeacon = ViewModel.HasBeacon;
            beaconGroup.IsExpanded = lastHasBeacon;
            ViewModel.PropertyChanged += OnRecipeViewModelPropertyChanged;
            viewer.Session.NodeValuesUpdated += OnNodeValuesUpdated;
        }

        internal void Refresh() {
            ViewModel?.Refresh();
            if (ViewModel is not null)
                rateAuto.IsChecked = !ViewModel.IsFixedRate;
        }

        private void RateAuto_IsCheckedChanged(object? sender, RoutedEventArgs e) {
            if (ViewModel is not null && rateAuto.IsChecked == true)
                ViewModel.IsFixedRate = false;
        }

        private void OnNodeValuesUpdated(object? sender, EventArgs e) => Refresh();

        //HasBeacon flipping is the only trigger for a code-driven IsExpanded change - a plain re-solve with
        //the beacon unchanged must not re-collapse or re-expand a group the user set by hand (fix round: a
        //live {Binding HasBeacon} used to re-push on every RefreshComputed, undoing a manual collapse).
        private void OnRecipeViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(RecipeSegmentViewModel.HasBeacon) || ViewModel is null || ViewModel.HasBeacon == lastHasBeacon)
                return;
            lastHasBeacon = ViewModel.HasBeacon;
            beaconGroup.IsExpanded = lastHasBeacon;
        }

        private void Detach() {
            if (boundViewer is not null)
                boundViewer.Session.NodeValuesUpdated -= OnNodeValuesUpdated;
            boundViewer = null;
            if (ViewModel is not null)
                ViewModel.PropertyChanged -= OnRecipeViewModelPropertyChanged;
        }
    }
}
