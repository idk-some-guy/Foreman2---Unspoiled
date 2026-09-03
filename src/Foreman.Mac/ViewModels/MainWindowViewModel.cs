using CommunityToolkit.Mvvm.ComponentModel;

namespace Foreman.Mac.ViewModels {
    //Window-chrome state split off MainWindow.axaml.cs: title/dirty/filename/preset/version label. Canvas
    //interaction, save/load orchestration, and dialog plumbing stay in the code-behind.
    public sealed partial class MainWindowViewModel : ObservableObject {
        private const string AppName = "Foreman 2";

        [ObservableProperty]
        private string windowTitle = AppName;

        [ObservableProperty]
        private bool isDirty;

        [ObservableProperty]
        private string? currentFileName;

        [ObservableProperty]
        private string? presetName;

        public string VersionLabel { get; } = AppVersion.VersionedDisplay;

        public ShellViewModel Shell { get; }

        public MainWindowViewModel(ShellViewModel shell) {
            Shell = shell;
        }

        partial void OnIsDirtyChanged(bool value) => RecomputeTitle();

        partial void OnCurrentFileNameChanged(string? value) => RecomputeTitle();

        partial void OnPresetNameChanged(string? value) => RecomputeTitle();

        //Ports MainWindow.axaml.cs's UpdateTitleBar format verbatim (reference: upstream MainForm.cs:188,240,263,444).
        private void RecomputeTitle() =>
            WindowTitle = string.Create(DisplayCulture.Format, $"{AppName} ({PresetName ?? ""}) - {CurrentFileName ?? "Untitled"}");
    }
}
