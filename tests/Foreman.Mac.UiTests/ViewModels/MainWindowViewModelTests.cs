using Foreman;
using Foreman.Mac.ViewModels;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    public class MainWindowViewModelTests {
        //Task 8 (contract #toolbar's document title "Iron Plate Line — Edited"): the app name lives in the
        //menu bar now, not the titlebar - an untitled document shows just "Untitled".
        [Fact]
        public void Constructor_DefaultsDocumentTitleToUntitled() {
            var vm = new MainWindowViewModel();

            Assert.Equal("Untitled", vm.DocumentTitle);
            Assert.Equal("", vm.EditedSuffix);
            Assert.Equal("Untitled", vm.WindowTitle);
        }

        [Fact]
        public void DocumentTitle_TracksFileName_ExtensionStripped() {
            var vm = new MainWindowViewModel();

            vm.CurrentFileName = "Iron Plate Line.fjson";

            Assert.Equal("Iron Plate Line", vm.DocumentTitle);
        }

        [Fact]
        public void DocumentTitle_FullPath_UsesFileNameOnly() {
            var vm = new MainWindowViewModel();

            vm.CurrentFileName = "/tmp/flowchart.fjson";

            Assert.Equal("flowchart", vm.DocumentTitle);
        }

        [Fact]
        public void DocumentTitle_NoFileName_ShowsUntitled() {
            var vm = new MainWindowViewModel();

            vm.CurrentFileName = null;

            Assert.Equal("Untitled", vm.DocumentTitle);
        }

        [Fact]
        public void EditedSuffix_WhenDirty_ShowsEmDashEdited() {
            var vm = new MainWindowViewModel();

            vm.IsDirty = true;

            Assert.Equal(" — Edited", vm.EditedSuffix);
        }

        [Fact]
        public void EditedSuffix_WhenClean_IsEmpty() {
            var vm = new MainWindowViewModel();

            vm.IsDirty = false;

            Assert.Equal("", vm.EditedSuffix);
        }

        [Fact]
        public void WindowTitle_CombinesDocumentTitleAndEditedSuffix_TracksBothLive() {
            var vm = new MainWindowViewModel();

            vm.CurrentFileName = "Iron Plate Line.fjson";
            Assert.Equal("Iron Plate Line", vm.WindowTitle);

            vm.IsDirty = true;
            Assert.Equal("Iron Plate Line — Edited", vm.WindowTitle);

            vm.IsDirty = false;
            Assert.Equal("Iron Plate Line", vm.WindowTitle);
        }

        [Fact]
        public void VersionLabel_ReturnsAppVersionVersionedDisplay() {
            var vm = new MainWindowViewModel();

            Assert.Equal(AppVersion.VersionedDisplay, vm.VersionLabel);
        }

        //Task 4: the View Options popover (contract #toolbar, Gridlines + Graph Options fold) is chrome
        //state, same category as WindowTitle - closed by default so the canvas gets the full window on boot.
        [Fact]
        public void IsViewOptionsOpen_DefaultsToFalse() {
            var vm = new MainWindowViewModel();

            Assert.False(vm.IsViewOptionsOpen);
        }

        [Fact]
        public void ToggleViewOptionsCommand_Execute_TogglesIsViewOptionsOpen() {
            var vm = new MainWindowViewModel();

            vm.ToggleViewOptionsCommand.Execute(null);
            Assert.True(vm.IsViewOptionsOpen);

            vm.ToggleViewOptionsCommand.Execute(null);
            Assert.False(vm.IsViewOptionsOpen);
        }
    }
}
