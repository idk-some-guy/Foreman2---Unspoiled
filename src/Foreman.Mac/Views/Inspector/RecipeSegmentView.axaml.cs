using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foreman.DataCaching;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;

namespace Foreman.Mac.Views.Inspector {
    //Docked replacement for EditRecipePanel's Rate/Assembler/Fuel groups (Task 5; Modules/Beacon/quality
    //follow in Task 6). RateAuto isn't bound in XAML - its RadioButton grouping with RateFixed already flips
    //RateFixed.IsChecked (and so RecipeSegmentViewModel.IsFixedRate, via its own TwoWay binding) whenever
    //Auto gets picked instead - same pattern as FlowSegmentView.
    public partial class RecipeSegmentView : UserControl {
        private readonly RadioButton rateAuto;
        private readonly PickGrid assemblerGrid;
        private readonly PickGrid fuelGrid;
        private GraphViewer? boundViewer;

        public RecipeSegmentViewModel? ViewModel { get; private set; }

        public RecipeSegmentView() {
            InitializeComponent();
            rateAuto = this.FindControl<RadioButton>("RateAuto")!;
            assemblerGrid = this.FindControl<PickGrid>("AssemblerGrid")!;
            fuelGrid = this.FindControl<PickGrid>("FuelGrid")!;
            rateAuto.IsCheckedChanged += RateAuto_IsCheckedChanged;
            assemblerGrid.ItemActivated += (_, item) => ViewModel?.PickAssembler(item);
            fuelGrid.ItemActivated += (_, item) => ViewModel?.PickFuel(item);
            DetachedFromVisualTree += (_, _) => Detach();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal void Bind(IRecipeNodeViewModel node, GraphViewer viewer, DataCache cache, AppSettings settings, Action? requestRedraw = null) {
            Detach();
            boundViewer = viewer;
            ViewModel = new RecipeSegmentViewModel(node, viewer, cache, settings, requestRedraw);
            DataContext = ViewModel;
            rateAuto.IsChecked = !ViewModel.IsFixedRate;
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

        private void Detach() {
            if (boundViewer is not null)
                boundViewer.Session.NodeValuesUpdated -= OnNodeValuesUpdated;
            boundViewer = null;
        }
    }
}
