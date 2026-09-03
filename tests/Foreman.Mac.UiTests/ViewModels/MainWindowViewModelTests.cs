using Foreman;
using Foreman.Mac.ViewModels;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    public class MainWindowViewModelTests {
        private static ShellViewModel NewShell() =>
            new(
                onNew: () => { },
                onLoad: () => { },
                onSave: () => { },
                onSaveAs: () => { },
                onImport: () => { },
                onExportImage: () => { },
                onAddItem: () => { },
                onAddRecipe: () => { },
                onAutoconnect: () => { },
                onAlignSelection: () => { },
                onSettings: () => { },
                onGraphSummary: () => { });

        [Fact]
        public void Constructor_ExposesShellAndDefaultsTitleToAppName() {
            var vm = new MainWindowViewModel(NewShell());

            Assert.Equal("Foreman 2", vm.WindowTitle);
        }

        //Exact current format, MainWindow.axaml.cs:661-662 (UpdateTitleBar): "{appName} ({presetName}) - {path}".
        [Fact]
        public void Title_TracksDirtyAndFileName() {
            var vm = new MainWindowViewModel(NewShell());

            vm.CurrentFileName = "base.fjson";
            vm.IsDirty = true;

            Assert.Equal("Foreman 2 () - base.fjson", vm.WindowTitle);
        }

        [Fact]
        public void Title_NoFileName_ShowsUntitled() {
            var vm = new MainWindowViewModel(NewShell());

            vm.PresetName = "Some Preset";

            Assert.Equal("Foreman 2 (Some Preset) - Untitled", vm.WindowTitle);
        }

        [Fact]
        public void Title_PresetAndFileNameBothSet_MatchesSaveLoadFormat() {
            var vm = new MainWindowViewModel(NewShell());

            vm.PresetName = "Some Preset";
            vm.CurrentFileName = "/tmp/flowchart.fjson";

            Assert.Equal("Foreman 2 (Some Preset) - /tmp/flowchart.fjson", vm.WindowTitle);
        }

        [Fact]
        public void VersionLabel_ReturnsAppVersionVersionedDisplay() {
            var vm = new MainWindowViewModel(NewShell());

            Assert.Equal(AppVersion.VersionedDisplay, vm.VersionLabel);
        }

        [Fact]
        public void Shell_ReturnsSameInstancePassedToConstructor() {
            ShellViewModel shell = NewShell();

            var vm = new MainWindowViewModel(shell);

            Assert.Same(shell, vm.Shell);
        }
    }
}
