using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Foreman.Mac;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.ViewModels;
using System;

namespace Foreman.Mac.Views.Inspector {
    public partial class FlowSegmentView : UserControl {
        private readonly Segmented rateMode;
        private readonly Stepper rateValue;
        private readonly PopUp rateUnitPopUp;
        private GraphViewer? boundViewer;

        public FlowSegmentViewModel? ViewModel { get; private set; }

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
