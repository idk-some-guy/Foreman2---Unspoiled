using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Models;
using Foreman.Models.Nodes;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ForemanTest {
    //Proves the 4 new AppSettings solver fields (QualitySteps/LowPriorityPower/PullConsumerNodes/
    //PullConsumerNodesPower) reach the exact math upstream feeds them into: GraphOptimisation.cs:28
    //(Math.Pow(10, LowPriorityPower), Math.Pow(10, PullOutputNodesPower)) and RecipeNode.cs's quality-tier
    //loop (MaxQualitySteps). Each case is hand-derived from that math, not just "does the number change".
    [TestClass]
    public class GraphOptionsSolverFieldsTest : ForemanTestBase {
        //factoryObjectiveCoefficient is a fixed 1e-2 (ProductionSolver.cs:62's io-ratio ctor), so a
        //recipe's objective weight is 0.01 * FactoryRate() * (LowPriority ? 10^LowPriorityPower : 1),
        //where FactoryRate() = Time / Speed (TestAssembler's speed is 1, so FactoryRate == Time here).
        [TestMethod]
        public void LowPriorityPower_BelowCrossoverExponent_PrefersTheLowPriorityRecipe() {
            var builder = GraphBuilder.Create();
            var cheap = builder.Recipe("cheap").Input("OreCheap", 1).Output("Widget", 1).SetLowPriority(true).SetTime(1);
            var expensive = builder.Recipe("expensive").Input("OreExpensive", 1).Output("Widget", 1).SetTime(15);
            var consumer = builder.Consumer("Widget").Target(10);
            builder.Link(builder.Supply("OreCheap"), cheap, consumer);
            builder.Link(builder.Supply("OreExpensive"), expensive, consumer);
            var data = builder.Build();

            //cost(cheap) = 0.01*1*10^1 = 0.1 < cost(expensive) = 0.01*15*1 = 0.15 -> solver fully prefers cheap.
            data.Graph.LowPriorityPower = 1;
            data.Solve();

            AssertFloatsAreEqual(10, data.RecipeRate("cheap"));
            AssertFloatsAreEqual(0, data.RecipeRate("expensive"));
        }

        [TestMethod]
        public void LowPriorityPower_AboveCrossoverExponent_SwitchesToTheHighPriorityRecipe() {
            var builder = GraphBuilder.Create();
            var cheap = builder.Recipe("cheap").Input("OreCheap", 1).Output("Widget", 1).SetLowPriority(true).SetTime(1);
            var expensive = builder.Recipe("expensive").Input("OreExpensive", 1).Output("Widget", 1).SetTime(15);
            var consumer = builder.Consumer("Widget").Target(10);
            builder.Link(builder.Supply("OreCheap"), cheap, consumer);
            builder.Link(builder.Supply("OreExpensive"), expensive, consumer);
            var data = builder.Build();

            //cost(cheap) = 0.01*1*10^2 = 1.0 > cost(expensive) = 0.01*15*1 = 0.15 -> solver switches entirely.
            data.Graph.LowPriorityPower = 2;
            data.Solve();

            AssertFloatsAreEqual(0, data.RecipeRate("cheap"));
            AssertFloatsAreEqual(10, data.RecipeRate("expensive"));
        }

        //A Time=2000, no-input recipe feeding a free (Auto-rate) consumer: factory cost per unit output is
        //0.01*2000 = 20; the "maximize output nodes" reward per unit is 10^PullConsumerNodesPower (only
        //applied when PullConsumerNodes is true - GraphOptimisation.cs:28 / ProductionSolver.cs:66). With no
        //reward, or a reward below the factory cost, the trivial 0-production solution is optimal.
        [TestMethod]
        public void PullConsumerNodes_False_LeavesConsumerAtZeroRegardlessOfPower() {
            var builder = GraphBuilder.Create();
            var recipe = builder.Recipe("expensive-recipe").Output("Widget", 1).SetTime(2000);
            var consumer = builder.Consumer("Widget");
            builder.Link(recipe, consumer);
            var data = builder.Build();

            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(NewTempDir());
            ErrorLogging.ClearLog();

            data.Graph.PullOutputNodes = false;
            data.Graph.PullOutputNodesPower = 5; //max power, but the reward term is zeroed while the flag is off
            data.Solve();

            AssertFloatsAreEqual(0, data.ConsumedRate("Widget"));
            AssertFloatsAreEqual(0, data.RecipeRate("expensive-recipe"));
            //Distinguishes a correct bounded zero from an unbounded-LP failure that also happens to report 0.
            string log = File.Exists(ErrorLogging.LogFilePath) ? File.ReadAllText(ErrorLogging.LogFilePath) : "";
            Assert.DoesNotContain("Solver failed to find a solution", log);
        }

        [TestMethod]
        public void PullConsumerNodes_TrueWithRewardBelowFactoryCost_StaysBoundedAtZero() {
            var builder = GraphBuilder.Create();
            var recipe = builder.Recipe("expensive-recipe").Output("Widget", 1).SetTime(2000);
            var consumer = builder.Consumer("Widget");
            builder.Link(recipe, consumer);
            var data = builder.Build();

            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(NewTempDir());
            ErrorLogging.ClearLog();

            //reward = 10^1 = 10 < factory cost 20 -> bounded optimal solution, no solver failure logged.
            data.Graph.PullOutputNodes = true;
            data.Graph.PullOutputNodesPower = 1;
            data.Solve();

            AssertFloatsAreEqual(0, data.ConsumedRate("Widget"));
            string log = File.Exists(ErrorLogging.LogFilePath) ? File.ReadAllText(ErrorLogging.LogFilePath) : "";
            Assert.DoesNotContain("Solver failed to find a solution", log);
        }

        [TestMethod]
        public void PullConsumerNodes_TrueWithRewardAboveFactoryCost_BecomesUnboundedAndLogsSolverFailure() {
            var builder = GraphBuilder.Create();
            var recipe = builder.Recipe("expensive-recipe").Output("Widget", 1).SetTime(2000);
            var consumer = builder.Consumer("Widget");
            builder.Link(recipe, consumer);
            var data = builder.Build();

            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(NewTempDir());
            ErrorLogging.ClearLog();

            //reward = 10^2 = 100 > factory cost 20 -> the LP is unbounded (no ceiling on output), matching
            //ProductionSolver's own "can result in an unbound solution" comment for this option.
            data.Graph.PullOutputNodes = true;
            data.Graph.PullOutputNodesPower = 2;
            data.Solve();

            Assert.Contains("Solver failed to find a solution", File.ReadAllText(ErrorLogging.LogFilePath));
        }

        //RecipeNode.cs's quality-tier loop (reference RecipeNode.cs:335-349): with a quality-bonus module
        //giving a 30% chance and a chain normal(100%)->uncommon(50%)->rare, the tier split is exactly
        //derivable per MaxQualitySteps: currentStep starts at 1 (base tier already added), and the loop adds
        //one more tier per remaining step while currentStep < MaxQualitySteps.
        [TestMethod]
        public void QualitySteps_CapsHowManyQualityTiersARecipeCanProduce() {
            var builder = GraphBuilder.Create();
            var recipeBuilder = builder.Recipe("quality-recipe").Output("Widget", 1).SetTarget(10);
            var data = builder.Build();

            var normal = (QualityPrototype)data.Quality;
            var uncommon = new QualityPrototype(data.Cache, "uncommon", "Uncommon", "b") { NextProbability = 0.5 };
            var rare = new QualityPrototype(data.Cache, "rare", "Rare", "c");
            normal.NextQuality = uncommon;
            normal.NextProbability = 1.0;
            uncommon.NextQuality = rare;

            var qualityModule = new ModulePrototype(data.Cache, "quality-module", "Quality Module") { QualityBonus = 0.3 };
            var node = (RecipeNode)recipeBuilder.BuiltNode;
            node.AssemblerModulesAdd(new ModuleQualityPair(qualityModule, normal));

            IItem widget = node.Outputs.First(o => ReferenceEquals(o.Quality, normal)).Item!;

            data.Graph.MaxQualitySteps = 1;
            data.Solve();
            AssertFloatsAreEqual(1.0, node.GetSupplyRate(new ItemQualityPair(widget, normal)) / node.ActualRate);
            Assert.IsFalse(node.Outputs.Any(o => ReferenceEquals(o.Quality, uncommon)));

            data.Graph.MaxQualitySteps = 2;
            data.Solve();
            AssertFloatsAreEqual(0.7, node.GetSupplyRate(new ItemQualityPair(widget, normal)) / node.ActualRate);
            AssertFloatsAreEqual(0.3, node.GetSupplyRate(new ItemQualityPair(widget, uncommon)) / node.ActualRate);
            Assert.IsFalse(node.Outputs.Any(o => ReferenceEquals(o.Quality, rare)));

            data.Graph.MaxQualitySteps = 3;
            data.Solve();
            AssertFloatsAreEqual(0.7, node.GetSupplyRate(new ItemQualityPair(widget, normal)) / node.ActualRate);
            AssertFloatsAreEqual(0.15, node.GetSupplyRate(new ItemQualityPair(widget, uncommon)) / node.ActualRate);
            AssertFloatsAreEqual(0.15, node.GetSupplyRate(new ItemQualityPair(widget, rare)) / node.ActualRate);
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void AssertFloatsAreEqual(double expected, double actual) =>
            Assert.AreEqual(expected, actual, 0.0001);
    }
}
