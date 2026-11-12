using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Research;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Inspector {
    public class RecipeSegmentResearchTests {
        private const string MinerRecipe = "§§r:e:iron-ore";

        private static ResearchState WithRecipe(string recipe, double bonus) =>
            new(null, new Dictionary<ResearchField, double>(), new Dictionary<ResearchField, double>(),
                new Dictionary<string, double> { [recipe] = bonus }, new Dictionary<string, double>(), []);

        private static ResearchState WithMining(double bonus) =>
            new(null, new Dictionary<ResearchField, double> { [ResearchField.MiningProductivity] = bonus }, new Dictionary<ResearchField, double>(),
                new Dictionary<string, double>(), new Dictionary<string, double>(), []);

        private static (ShellFixture Shell, RecipeSegmentView View, IRecipeNodeViewModel Node) Open(string recipe) {
            ShellFixture shell = ShellFixture.Open();
            DataCache cache = shell.Window.DataCache!;
            NodeId id = shell.Window.GraphCanvas.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(cache.Recipes[recipe], cache.DefaultQuality!), new Point(0, 0));
            shell.Graph.UpdateNodeValues();
            Assert.True(shell.Window.GraphCanvas.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node));
            InspectorTestSupport.Select(shell.Window, node!);
            Dispatcher.UIThread.RunJobs();
            return (shell, shell.Window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!, (IRecipeNodeViewModel)node!);
        }

        private static FormRow Row(RecipeSegmentView view) => view.FindControl<FormRow>("ExtraProductivityRow")!;

        private static Stepper ExtraStepper(RecipeSegmentView view) => view.FindControl<Stepper>("ExtraProductivityValue")!;

        private static void Research(ShellFixture shell, ResearchState state) {
            shell.Graph.Research = state;
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void Inspector_ExtraProd_ResearchNote() {
            (ShellFixture shell, RecipeSegmentView view, IRecipeNodeViewModel node) = Open(MinerRecipe);
            using (shell) {
                Assert.Null(Row(view).Note);

                Research(shell, WithMining(0.2));
                Assert.Equal("Research adds 20 %. This value adds on top.", Row(view).Note);

                Research(shell, ResearchState.None);
                Assert.Null(Row(view).Note);
            }

            (ShellFixture gears, RecipeSegmentView gearView, IRecipeNodeViewModel gearNode) = Open("iron-gear-wheel");
            using (gears) {
                Assert.Null(Row(gearView).Note);

                Research(gears, WithRecipe(gearNode.BaseRecipe.Recipe!.Name, 0.3));
                Assert.Equal("Research adds 30 %. This value adds on top.", Row(gearView).Note);
                Assert.Equal(Row(gearView).Note, AutomationProperties.GetHelpText(ExtraStepper(gearView)));

                Research(gears, ResearchState.None);
                Assert.Null(Row(gearView).Note);
                Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(ExtraStepper(gearView))));
            }
        }

        [AvaloniaFact]
        public void Inspector_ExtraProd_ResearchNote_AtTheRecipeLimit() {
            (ShellFixture shell, RecipeSegmentView view, IRecipeNodeViewModel node) = Open("iron-gear-wheel");
            using (shell) {
                var recipe = (RecipePrototype)node.BaseRecipe.Recipe!;
                double original = recipe.MaxProductivityBonus;
                try {
                    recipe.MaxProductivityBonus = 3;
                    shell.Graph.Research = WithRecipe(recipe.Name, 0.3);
                    Assert.IsType<RecipeNodeController>(shell.Window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(node.Id)).SetExtraProductivityBonus(3);
                    shell.Graph.UpdateNodeValues();
                    Dispatcher.UIThread.RunJobs();

                    Assert.Equal("Research adds 30 %. The total stops at the recipe’s limit of 300 %.", Row(view).Note);
                } finally {
                    recipe.MaxProductivityBonus = original;
                }
            }
        }

        [AvaloniaFact]
        public void Inspector_ExtraProd_ResearchNote_ZeroCapRecipeAddsOnTop() {
            (ShellFixture shell, RecipeSegmentView view, IRecipeNodeViewModel node) = Open("iron-gear-wheel");
            using (shell) {
                var recipe = (RecipePrototype)node.BaseRecipe.Recipe!;
                double original = recipe.MaxProductivityBonus;
                try {
                    recipe.MaxProductivityBonus = 0;
                    Research(shell, WithRecipe(recipe.Name, 0.3));

                    Assert.Equal("Research adds 30 %. This value adds on top.", Row(view).Note);
                } finally {
                    recipe.MaxProductivityBonus = original;
                }
            }
        }

        [AvaloniaFact]
        public void Inspector_ExtraProd_ResearchNote_VoiceOver() {
            (ShellFixture shell, RecipeSegmentView view, IRecipeNodeViewModel node) = Open("iron-gear-wheel");
            using (shell) {
                shell.Graph.EnableExtraProductivityForNonMiners = true;
                Research(shell, WithRecipe(node.BaseRecipe.Recipe!.Name, 0.3));
                ExtraStepper(view).Value = 10;
                Dispatcher.UIThread.RunJobs();

                Stepper stepper = ExtraStepper(view);
                Assert.Equal("Extra productivity", AutomationProperties.GetName(stepper));
                Assert.Equal("10 percent", ControlAutomationPeer.CreatePeerForElement(stepper).GetProvider<IValueProvider>()?.Value);
                Assert.Equal("Research adds 30 %. This value adds on top.", AutomationProperties.GetHelpText(stepper));
            }
        }

        [AvaloniaFact]
        public void Inspector_ExtraProd_ZeroResearchEntryShowsNoNote() {
            (ShellFixture shell, RecipeSegmentView view, IRecipeNodeViewModel node) = Open("iron-gear-wheel");
            using (shell) {
                shell.Graph.EnableExtraProductivityForNonMiners = false;
                Research(shell, WithRecipe(node.BaseRecipe.Recipe!.Name, 0));

                Assert.False(view.ViewModel!.ShowsExtraProductivity);
                Assert.Null(view.ViewModel.ExtraProductivityNote);
            }
        }

        [AvaloniaFact]
        public void Inspector_ExtraProdRow_ShownWhenResearchApplies() {
            (ShellFixture shell, RecipeSegmentView view, IRecipeNodeViewModel node) = Open("iron-gear-wheel");
            using (shell) {
                shell.Graph.EnableExtraProductivityForNonMiners = false;
                Assert.False(node.BaseRecipe.Recipe!.HasProductivityResearch);
                Research(shell, ResearchState.None);
                Assert.False(Row(view).IsVisible);

                Research(shell, WithRecipe(node.BaseRecipe.Recipe.Name, 0.3));

                Assert.True(Row(view).IsVisible);
                Assert.Equal("Research adds 30 %. This value adds on top.", Row(view).Note);
            }
        }
    }
}
