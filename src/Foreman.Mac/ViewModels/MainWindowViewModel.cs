using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Foreman.Mac.ViewModels {
    //Window-chrome state split off MainWindow.axaml.cs: title/filename/preset/version label, and (task 4)
    //the toolbar's View Options popover open/closed state. Canvas interaction, save/load orchestration, and
    //dialog plumbing stay in the code-behind.
    public sealed partial class MainWindowViewModel : ObservableObject {
        private const string AppName = "Foreman 2";

        [ObservableProperty]
        private string windowTitle = AppName;

        [ObservableProperty]
        private string? currentFileName;

        [ObservableProperty]
        private string? presetName;

        [ObservableProperty]
        private bool isViewOptionsOpen;

        //Task 7 (contract #toolbar's Save .on dirty-tint state): a cheap UI hint set on any graph mutation
        //event and cleared on save/load/new - MainWindow.axaml.cs wires both ends. The authoritative dirty
        //gate stays TestGraphSavedStatusAsync's content-hash compare; this flag never feeds it, so it may
        //disagree with the hash in either direction without risking a silent data-losing close.
        [ObservableProperty]
        private bool isDirty;

        public string VersionLabel { get; } = AppVersion.VersionedDisplay;

        //Folds the old GridLinesGroupBox/ProductionGroupBox into the contract's #toolbar View Options
        //button (ShellViewModel has no command for it - it toggles chrome, not a document/canvas action).
        public IRelayCommand ToggleViewOptionsCommand { get; }

        public MainWindowViewModel() {
            ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsOpen = !IsViewOptionsOpen);
        }

        partial void OnCurrentFileNameChanged(string? value) => RecomputeTitle();

        partial void OnPresetNameChanged(string? value) => RecomputeTitle();

        //Ports MainWindow.axaml.cs's UpdateTitleBar format verbatim (reference: upstream MainForm.cs:188,240,263,444).
        private void RecomputeTitle() =>
            WindowTitle = string.Create(DisplayCulture.Format, $"{AppName} ({PresetName ?? ""}) - {CurrentFileName ?? "Untitled"}");
    }
}
