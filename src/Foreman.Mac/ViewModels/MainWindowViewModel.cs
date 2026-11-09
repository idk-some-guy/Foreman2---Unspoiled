using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Foreman.Mac.ViewModels {
    public sealed partial class MainWindowViewModel : ObservableObject {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DocumentTitle))]
        [NotifyPropertyChangedFor(nameof(WindowTitle))]
        private string? documentName;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditedSuffix))]
        [NotifyPropertyChangedFor(nameof(WindowTitle))]
        private bool isDirty;

        [ObservableProperty]
        private bool isViewOptionsOpen;

        public string VersionLabel { get; } = AppVersion.VersionedDisplay;

        public InspectorViewModel Inspector { get; } = new();

        public bool IsInspectorVisible => Inspector.IsVisible;

        public bool IsInspectorPinned {
            get => Inspector.IsPinned;
            set => Inspector.IsPinned = value;
        }

        public string DocumentTitle => string.IsNullOrEmpty(DocumentName) ? "Untitled" : DocumentName;

        public string EditedSuffix => IsDirty ? " — Edited" : "";

        public string WindowTitle => DocumentTitle + EditedSuffix;

        public IRelayCommand ToggleViewOptionsCommand { get; }

        public MainWindowViewModel() {
            ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsOpen = !IsViewOptionsOpen);
            Inspector.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(InspectorViewModel.IsVisible))
                    OnPropertyChanged(nameof(IsInspectorVisible));
                else if (e.PropertyName == nameof(InspectorViewModel.IsPinned))
                    OnPropertyChanged(nameof(IsInspectorPinned));
            };
        }
    }
}
