using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.ViewModels;
using System;

namespace Foreman.Mac.Views.Inspector {
    //Docked replacement for EditFlowPanel (Task 4): rate + node groups for every non-recipe node. RateAuto
    //isn't bound in XAML - its RadioButton grouping with RateFixed already flips RateFixed.IsChecked (and
    //so FlowSegmentViewModel.IsFixedRate, via its own TwoWay binding) whenever Auto gets picked instead.
    public partial class FlowSegmentView : UserControl {
        private readonly RadioButton rateAuto;
        private readonly NumericUpDown rateValue;
        private GraphViewer? boundViewer;

        public FlowSegmentViewModel? ViewModel { get; private set; }

        public FlowSegmentView() {
            InitializeComponent();
            rateAuto = this.FindControl<RadioButton>("RateAuto")!;
            rateValue = this.FindControl<NumericUpDown>("RateValue")!;
            rateAuto.IsCheckedChanged += RateAuto_IsCheckedChanged;
            DetachedFromVisualTree += (_, _) => Detach();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal void Bind(INodeViewModel node, GraphViewer viewer, Action? requestRedraw = null) {
            Detach();
            boundViewer = viewer;
            ViewModel = new FlowSegmentViewModel(node, viewer, requestRedraw);
            DataContext = ViewModel;
            rateValue.Maximum = (decimal)(node.MaxDesiredSetValue * viewer.Graph.GetRateMultipler());
            rateAuto.IsChecked = !ViewModel.IsFixedRate;
            viewer.Session.NodeValuesUpdated += OnNodeValuesUpdated;
        }

        //Re-reads the bound node (NodeValuesUpdated, or a rate-unit change forwarded from InspectorView.
        //RefreshSegment) without waiting for the node to change - IsFixedRate's own TwoWay binding already
        //resyncs RateFixed, but RateAuto isn't bound (see the class comment) so it needs its own nudge here.
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
