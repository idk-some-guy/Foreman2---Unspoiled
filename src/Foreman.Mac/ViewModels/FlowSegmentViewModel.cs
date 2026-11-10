using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Models.Nodes;
using System;

namespace Foreman.Mac.ViewModels {
    public sealed partial class FlowSegmentViewModel : ObservableObject {
        private readonly INodeViewModel node;
        private readonly GraphViewer viewer;
        private readonly BaseNodeController controller;
        private readonly PassthroughNodeController? passthroughController;
        private readonly Action? requestRedraw;
        private bool suppressWrites;

        [ObservableProperty]
        private bool isFixedRate;

        [ObservableProperty]
        private double fixedRate;

        [ObservableProperty]
        private bool isKeyNode;

        [ObservableProperty]
        private string keyNodeTitle = "";

        [ObservableProperty]
        private bool simplifyThroughput;

        public bool ShowsSimplify { get; }

        public FlowSegmentViewModel(INodeViewModel node, GraphViewer viewer, Action? requestRedraw = null) {
            this.node = node;
            this.viewer = viewer;
            this.requestRedraw = requestRedraw;
            if (viewer.Session.Editor.RequestNodeController(node.Id) is not BaseNodeController requestedController)
                throw new InvalidOperationException("Node has no controller.");
            controller = requestedController;
            passthroughController = controller as PassthroughNodeController;
            ShowsSimplify = passthroughController is not null;
            Refresh();
        }

        //suppressWrites stops Refresh's assignments from round-tripping back through the controller
        public void Refresh() {
            suppressWrites = true;
            IsFixedRate = node.RateType == RateType.Manual;
            FixedRate = IsFixedRate ? node.DesiredSetValue : node.ActualSetValue;
            IsKeyNode = node.KeyNode;
            KeyNodeTitle = node.KeyNodeTitle;
            if (node is IPassthroughNodeViewModel passthrough)
                SimplifyThroughput = passthrough.SimpleDraw;
            suppressWrites = false;
        }

        partial void OnIsFixedRateChanged(bool value) {
            if (suppressWrites)
                return;
            if (value && node.DesiredSetValue != FixedRate)
                controller.SetDesiredSetValue(FixedRate);
            RateType updated = value ? RateType.Manual : RateType.Auto;
            if (node.RateType != updated)
                controller.SetRateType(updated);
            viewer.Graph.UpdateNodeValues();
            requestRedraw?.Invoke();
        }

        partial void OnFixedRateChanged(double value) {
            if (suppressWrites)
                return;
            if (node.DesiredSetValue == value)
                return;
            controller.SetDesiredSetValue(value);
            viewer.Graph.UpdateNodeValues();
            requestRedraw?.Invoke();
        }

        partial void OnIsKeyNodeChanged(bool value) {
            if (suppressWrites)
                return;
            controller.SetKeyNode(value);
            suppressWrites = true;
            KeyNodeTitle = node.KeyNodeTitle;
            suppressWrites = false;
            requestRedraw?.Invoke();
        }

        partial void OnKeyNodeTitleChanged(string value) {
            if (suppressWrites)
                return;
            controller.SetKeyNodeTitle(value ?? "");
            requestRedraw?.Invoke();
        }

        partial void OnSimplifyThroughputChanged(bool value) {
            if (suppressWrites)
                return;
            passthroughController?.SetSimpleDraw(value);
            requestRedraw?.Invoke();
        }
    }
}
