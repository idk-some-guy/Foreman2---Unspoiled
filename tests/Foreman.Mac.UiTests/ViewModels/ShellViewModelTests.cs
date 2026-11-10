using Foreman.Mac.ViewModels;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    public class ShellViewModelTests {
        private static readonly string[] ExpectedHits = ["new", "save", "syncResearch", "syncResearchFromAnotherSave"];

        private static ShellViewModel NewViewModel(List<string> hit) =>
            new(
                onNew: () => hit.Add("new"),
                onLoad: () => hit.Add("load"),
                onSave: () => hit.Add("save"),
                onImport: () => hit.Add("import"),
                onExportImage: () => hit.Add("exportImage"),
                onAddItem: () => hit.Add("addItem"),
                onAddRecipe: () => hit.Add("addRecipe"),
                onAutoconnect: () => hit.Add("autoconnect"),
                onAlignSelection: () => hit.Add("alignSelection"),
                onSettings: () => hit.Add("settings"),
                onGraphSummary: () => hit.Add("graphSummary"),
                onSyncResearch: () => hit.Add("syncResearch"),
                onSyncResearchFromAnotherSave: () => hit.Add("syncResearchFromAnotherSave"));

        [Fact]
        public void Commands_InvokeTheirDelegates() {
            var hit = new List<string>();
            ShellViewModel vm = NewViewModel(hit);

            vm.NewCommand.Execute(null);
            vm.SaveCommand.Execute(null);
            vm.SyncResearchCommand.Execute(null);
            vm.SyncResearchFromAnotherSaveCommand.Execute(null);

            Assert.Equal(ExpectedHits, hit);
        }
    }
}
