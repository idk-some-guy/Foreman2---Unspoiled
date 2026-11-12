using Foreman.Mac.ViewModels;
using Foreman.Models;
using Foreman.Models.Research;
using System;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    public class ResearchViewModelTests {
        private const double Awkward = 0.1 + 0.2;

        private static ResearchViewModel Synced(ProductionGraph graph) {
            var values = new Dictionary<ResearchField, double> { [ResearchField.LabSpeed] = Awkward };
            var recipes = new Dictionary<string, double> { ["steel-plate"] = Awkward };
            graph.Research = new ResearchState(new ResearchSource("Megabase", "/tmp/Megabase.zip", 1, DateTime.UtcNow), values, values, recipes, recipes, []);
            return new ResearchViewModel(graph);
        }

        [Fact]
        public void SetField_EditedAwayAndTypedBackToTheSyncedDisplay_IsNoLongerHandEdited() {
            var graph = new ProductionGraph();
            ResearchViewModel vm = Synced(graph);

            vm.SetField(ResearchField.LabSpeed, 50);
            Assert.True(graph.Research.IsHandEdited(ResearchField.LabSpeed));
            vm.SetField(ResearchField.LabSpeed, 30);

            Assert.False(graph.Research.IsHandEdited(ResearchField.LabSpeed));
            Assert.Equal(Awkward, graph.Research.Get(ResearchField.LabSpeed));
        }

        [Fact]
        public void SetRecipe_EditedAwayAndTypedBackToTheSyncedDisplay_IsNoLongerHandEdited() {
            var graph = new ProductionGraph();
            ResearchViewModel vm = Synced(graph);

            vm.SetRecipe("steel-plate", 50);
            Assert.True(graph.Research.IsRecipeHandEdited("steel-plate"));
            vm.SetRecipe("steel-plate", 30);

            Assert.False(graph.Research.IsRecipeHandEdited("steel-plate"));
            Assert.Equal(Awkward, graph.Research.RecipeProductivity["steel-plate"]);
        }
    }
}
