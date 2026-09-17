using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.ViewModels;
using System;

namespace Foreman.Mac.Views.Inspector {
    //Docked replacement for EditFlowPanel (Task 4): rate + node groups for every non-recipe node. RateMode
    //isn't bound in XAML - Kit.Segmented has no data-bindable SelectedItem, so its SelectedIndex is synced
    //by hand both ways here, same shape as the RadioButton pair it replaces.
    public partial class FlowSegmentView : UserControl {
        private readonly Segmented rateMode;
        private readonly Stepper rateValue;
        private GraphViewer? boundViewer;

        public FlowSegmentViewModel? ViewModel { get; private set; }

        public FlowSegmentView() {
            InitializeComponent();
            rateMode = this.FindControl<Segmented>("RateMode")!;
            rateMode.Items.Add("Auto");
            rateMode.Items.Add("Fixed");
            rateValue = this.FindControl<Stepper>("RateValue")!;
            rateMode.PropertyChanged += RateMode_PropertyChanged;
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
            viewer.Session.NodeValuesUpdated += OnNodeValuesUpdated;
        }

        //Re-reads the bound node (NodeValuesUpdated, or a rate-unit change forwarded from InspectorView.
        //RefreshSegment) without waiting for the node to change - IsFixedRate's own TwoWay binding already
        //resyncs the stepper, but RateMode isn't bound (see the class comment) so it needs its own nudge here.
        internal void Refresh() {
            ViewModel?.Refresh();
            if (ViewModel is not null)
                rateMode.SelectedIndex = ViewModel.IsFixedRate ? 1 : 0;
        }

        private void RateMode_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property == Segmented.SelectedIndexProperty && ViewModel is not null)
                ViewModel.IsFixedRate = rateMode.SelectedIndex == 1;
        }

        private void OnNodeValuesUpdated(object? sender, EventArgs e) => Refresh();

        private void Detach() {
            if (boundViewer is not null)
                boundViewer.Session.NodeValuesUpdated -= OnNodeValuesUpdated;
            boundViewer = null;
        }
    }
}
