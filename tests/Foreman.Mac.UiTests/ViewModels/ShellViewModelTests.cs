using Foreman.Mac.ViewModels;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    public class ShellViewModelTests {
        private static readonly string[] ExpectedHits = ["new", "save"];

        private static ShellViewModel NewViewModel(List<string> hit) =>
            new(
                onNew: () => hit.Add("new"),
                onLoad: () => hit.Add("load"),
                onSave: () => hit.Add("save"),
                onSaveAs: () => hit.Add("saveAs"),
                onImport: () => hit.Add("import"),
                onExportImage: () => hit.Add("exportImage"),
                onAddItem: () => hit.Add("addItem"),
                onAddRecipe: () => hit.Add("addRecipe"),
                onAutoconnect: () => hit.Add("autoconnect"),
                onAlignSelection: () => hit.Add("alignSelection"),
                onSettings: () => hit.Add("settings"),
                onGraphSummary: () => hit.Add("graphSummary"));

        [Fact]
        public void Commands_InvokeTheirDelegates() {
            var hit = new List<string>();
            ShellViewModel vm = NewViewModel(hit);

            vm.NewCommand.Execute(null);
            vm.SaveCommand.Execute(null);

            Assert.Equal(ExpectedHits, hit);
        }
    }
}
