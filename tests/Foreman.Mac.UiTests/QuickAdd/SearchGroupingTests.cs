using Avalonia.Headless.XUnit;
using Foreman.Mac.Canvas;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using System.Linq;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Jozef, live: two similarly named recipes turned up side by side under one "RESULTS" heading, the
    //second truncated to look identical to the first. A search now groups its matches one section per
    //subgroup, titled "Group · Subgroup", and every cell carries its full name and kind in a ToolTip.
    public sealed class SearchGroupingTests {
        [AvaloniaFact]
        public void SearchingSulfuricOnSpaceAge_GroupsMatchesBySubgroup_InsteadOfOneResultsSection() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "sulfuric"));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.ShowUnavailable = true;

            //"Sulfuric acid" itself (fluid-recipes) and "Carbon" (raw-material, an ingredient-match on the
            //acid it consumes) would have shared one "Results" heading before this fix.
            BrowserSection? fluidRecipes = vm.Sections.FirstOrDefault(s => s.Cells.Any(c => c.Candidate.Entry.Name == "sulfuric-acid"));
            BrowserSection? rawMaterial = vm.Sections.FirstOrDefault(s => s.Cells.Any(c => c.Candidate.Entry.Name == "carbon"));

            Assert.NotNull(fluidRecipes);
            Assert.NotNull(rawMaterial);
            Assert.NotSame(fluidRecipes, rawMaterial);
            Assert.NotEqual(fluidRecipes!.Heading, rawMaterial!.Heading);
            Assert.Contains("FLUID RECIPES", fluidRecipes.Heading);
            Assert.Contains("RAW MATERIAL", rawMaterial.Heading);
        }

        [AvaloniaFact]
        public void EveryCellsToolTip_CarriesItsFullNameAndKind() {
            using ShellFixture shell = ShellFixture.OpenWithSpaceAge();
            shell.Window.OpenBrowser(new AddRequest(new DrawingPoint(0, 0), AddScope.Recipes, SeedText: "sulfuric"));
            AddBrowserViewModel vm = shell.BrowserVm;
            vm.ShowUnavailable = true;

            BrowserCell cell = vm.Sections.SelectMany(s => s.Cells).First(c => c.Candidate.Entry.Name == "sulfuric-acid");

            Assert.Equal($"{cell.Candidate.Name} (recipe)", cell.TooltipText);
        }
    }
}
