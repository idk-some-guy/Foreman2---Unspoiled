using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Foreman.Mac.ViewModels {
    //Window-chrome state split off MainWindow.axaml.cs: title/filename/version label, and the
    //toolbar's View Options popover open/closed state. Canvas interaction, save/load orchestration, and
    //dialog plumbing stay in the code-behind.
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

        //Canvas selection lands here via SetSelection; Inspector.IsVisible folds pin/override/selection
        //into one flag the sidebar binds against.
        public InspectorViewModel Inspector { get; } = new();

        public bool IsInspectorVisible => Inspector.IsVisible;

        public bool IsInspectorPinned {
            get => Inspector.IsPinned;
            set => Inspector.IsPinned = value;
        }

        //The app name moved to the menu bar, and the titlebar now shows only the document (contract
        //#toolbar's "Iron Plate Line — Edited"). DocumentName is the library graph's name.
        public string DocumentTitle => string.IsNullOrEmpty(DocumentName) ? "Untitled" : DocumentName;

        //The em dash and its leading space are the suffix's own spacing (contract's ".dot { margin-left:
        //4px }") - Avalonia's Run has no Margin, so the space travels with the text instead.
        public string EditedSuffix => IsDirty ? " — Edited" : "";

        //Also the OS window title (Mission Control / Dock) via MainWindow.axaml's Title binding.
        public string WindowTitle => DocumentTitle + EditedSuffix;

        //Folds the old GridLinesGroupBox/ProductionGroupBox into the contract's #toolbar View Options
        //button (ShellViewModel has no command for it - it toggles chrome, not a document/canvas action).
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
