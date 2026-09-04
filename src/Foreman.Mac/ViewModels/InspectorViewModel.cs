using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.Graph;
using System.Collections.Generic;

namespace Foreman.Mac.ViewModels {
    public enum InspectorSegment { Recipe, Flow }

    public sealed partial class InspectorViewModel : ObservableObject {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVisible))]
        private bool isPinned;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVisible))]
        private bool showOverride;

        public IReadOnlyList<INodeViewModel> Selection { get; private set; } = [];
        public INodeViewModel? Node => Selection.Count == 1 ? Selection[0] : null;
        public bool HasNode => Node is not null;
        public InspectorSegment Segment => Node is IRecipeNodeViewModel ? InspectorSegment.Recipe : InspectorSegment.Flow;
        public string EmptyStateText => Selection.Count > 1 ? $"{Selection.Count} nodes selected" : "No selection";
        public bool IsVisible => IsPinned || ShowOverride || Selection.Count > 0;

        public void SetSelection(IReadOnlyList<INodeViewModel> nodes) {
            bool wasVisible = IsVisible;
            Selection = nodes;
            OnPropertyChanged(nameof(Selection));
            OnPropertyChanged(nameof(Node));
            OnPropertyChanged(nameof(HasNode));
            OnPropertyChanged(nameof(Segment));
            OnPropertyChanged(nameof(EmptyStateText));
            if (wasVisible != IsVisible)
                OnPropertyChanged(nameof(IsVisible));
        }
    }
}
