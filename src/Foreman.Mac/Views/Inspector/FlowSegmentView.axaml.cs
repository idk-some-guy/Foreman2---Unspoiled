using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Foreman;
using Foreman.Mac;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.ViewModels;
using System;

namespace Foreman.Mac.Views.Inspector {
    //Docked replacement for EditFlowPanel: rate + node groups for every non-recipe node. RateMode
    //isn't bound in XAML - Kit.Segmented has no data-bindable SelectedItem, so its SelectedIndex is synced
    //by hand both ways here, same shape as the RadioButton pair it replaces.
    public partial class FlowSegmentView : UserControl {
        private readonly Segmented rateMode;
        private readonly Stepper rateValue;
        private readonly PopUp rateUnitPopUp;
        private GraphViewer? boundViewer;

        public FlowSegmentViewModel? ViewModel { get; private set; }

        //Rate unit is a graph-level setting shared with View Options and the View menu radios - see
        //RecipeSegmentView's own RateUnitChanged for the mirror this feeds.
        public event EventHandler<ProductionGraph.RateUnit>? RateUnitChanged;

        public FlowSegmentView() {
            InitializeComponent();
            rateMode = this.FindControl<Segmented>("RateMode")!;
            rateMode.Items.Add("Auto");
            rateMode.Items.Add("Fixed");
            rateValue = this.FindControl<Stepper>("RateValue")!;
            rateUnitPopUp = this.FindControl<PopUp>("RateUnitPopUp")!;
            foreach (string option in MainWindow.RateUnitOptions)
                rateUnitPopUp.Items.Add(option);
            rateMode.PropertyChanged += RateMode_PropertyChanged;
            rateUnitPopUp.Command = new RelayCommand<string>(PickRateUnitByName);
            DetachedFromVisualTree += (_, _) => Detach();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal void Bind(INodeViewModel node, GraphViewer viewer, Action? requestRedraw = null) {
            Detach();
            boundViewer = viewer;
            ViewModel = new FlowSegmentViewModel(node, viewer, requestRedraw);
            DataContext = ViewModel;
            rateValue.Maximum = node.MaxDesiredSetValue * viewer.Graph.GetRateMultipler();
            rateMode.SelectedIndex = ViewModel.IsFixedRate ? 1 : 0;
            rateUnitPopUp.SelectedItem = MainWindow.RateUnitOptions[(int)viewer.Graph.SelectedRateUnit];
            viewer.Session.NodeValuesUpdated += OnNodeValuesUpdated;
        }

        //Re-reads the bound node (NodeValuesUpdated, or a rate-unit change forwarded from InspectorView.
        //RefreshSegment) without waiting for the node to change - IsFixedRate's own TwoWay binding already
        //resyncs the stepper, but RateMode isn't bound (see the class comment) so it needs its own nudge here.
        internal void Refresh() {
            ViewModel?.Refresh();
            if (ViewModel is null)
                return;
            rateMode.SelectedIndex = ViewModel.IsFixedRate ? 1 : 0;
            if (boundViewer is not null)
                rateUnitPopUp.SelectedItem = MainWindow.RateUnitOptions[(int)boundViewer.Graph.SelectedRateUnit];
        }

        private void RateMode_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property == Segmented.SelectedIndexProperty && ViewModel is not null)
                ViewModel.IsFixedRate = rateMode.SelectedIndex == 1;
        }

        //Reports the pick and writes nothing - see RecipeSegmentView's own PickRateUnitByName.
        private void PickRateUnitByName(string? name) {
            if (boundViewer is null || name is null)
                return;
            int index = Array.IndexOf(MainWindow.RateUnitOptions, name);
            if (index < 0)
                return;
            RateUnitChanged?.Invoke(this, (ProductionGraph.RateUnit)index);
        }

        private void OnNodeValuesUpdated(object? sender, EventArgs e) => Refresh();

        private void Detach() {
            if (boundViewer is not null)
                boundViewer.Session.NodeValuesUpdated -= OnNodeValuesUpdated;
            boundViewer = null;
        }
    }
}
