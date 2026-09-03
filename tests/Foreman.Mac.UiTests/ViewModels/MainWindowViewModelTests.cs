using Foreman;
using Foreman.Mac.ViewModels;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    public class MainWindowViewModelTests {
        [Fact]
        public void Constructor_DefaultsTitleToAppName() {
            var vm = new MainWindowViewModel();

            Assert.Equal("Foreman 2", vm.WindowTitle);
        }

        //Exact current format, MainWindow.axaml.cs:661-662 (UpdateTitleBar): "{appName} ({presetName}) - {path}".
        [Fact]
        public void Title_TracksFileName() {
            var vm = new MainWindowViewModel();

            vm.CurrentFileName = "base.fjson";

            Assert.Equal("Foreman 2 () - base.fjson", vm.WindowTitle);
        }

        [Fact]
        public void Title_NoFileName_ShowsUntitled() {
            var vm = new MainWindowViewModel();

            vm.PresetName = "Some Preset";

            Assert.Equal("Foreman 2 (Some Preset) - Untitled", vm.WindowTitle);
        }

        [Fact]
        public void Title_PresetAndFileNameBothSet_MatchesSaveLoadFormat() {
            var vm = new MainWindowViewModel();

            vm.PresetName = "Some Preset";
            vm.CurrentFileName = "/tmp/flowchart.fjson";

            Assert.Equal("Foreman 2 (Some Preset) - /tmp/flowchart.fjson", vm.WindowTitle);
        }

        [Fact]
        public void VersionLabel_ReturnsAppVersionVersionedDisplay() {
            var vm = new MainWindowViewModel();

            Assert.Equal(AppVersion.VersionedDisplay, vm.VersionLabel);
        }
    }
}
