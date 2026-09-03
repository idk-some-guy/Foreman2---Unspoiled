using Foreman;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace ForemanTest {
    // Pins GLOP's behavior at the shipped ProductionGraph defaults (LowPriorityPower = 4, per
    // upstream's own comment intent of "10,000 is a nice value here"). No existing test exercises
    // this: SolverTest.cs's cases never touch LowPriorityPower, and GraphOptionsSolverFieldsTest.cs's
    // two LowPriorityPower cases deliberately use small exponents. These cases must keep passing
    // against GLOP; a failure here is a shipped-default regression, not a test bug.
    [TestClass]
    public class SolverDefaultsTest : ForemanTestBase {
        [TestMethod]
        public void DefaultLowPriorityPower_FeedsFiniteMultiplier() {
            var graph = new ProductionGraph();

            Assert.AreEqual(4, graph.LowPriorityPower);
            Assert.AreEqual(1e4, Math.Pow(10, graph.LowPriorityPower));
        }

        [TestMethod]
        public void DefaultPriority_PrefersNormalRecipeOverLowPriority() {
            var builder = GraphBuilder.Create();
            // Low-priority recipe is strictly cheaper on factory count (Time 1 vs Time 15), but at the
            // Infinity default the solver must still shun it entirely in favor of the normal recipe.
            var lowPriority = builder.Recipe("cheap-low-priority").Input("OreCheap", 1).Output("Widget", 1).SetLowPriority(true).SetTime(1);
            var normal = builder.Recipe("normal").Input("OreExpensive", 1).Output("Widget", 1).SetTime(15);
            var consumer = builder.Consumer("Widget").Target(10);
            builder.Link(builder.Supply("OreCheap"), lowPriority, consumer);
            builder.Link(builder.Supply("OreExpensive"), normal, consumer);

            var data = builder.Build();
            data.Solve();

            AssertFloatsAreEqual(10, data.RecipeRate("normal"));
            AssertFloatsAreEqual(0, data.RecipeRate("cheap-low-priority"));
        }

        [TestMethod]
        public void DefaultPriority_UsesLowPriorityWhenOnlyOption() {
            var builder = GraphBuilder.Create();
            var onlyOption = builder.Recipe("only-option").Input("Ore", 1).Output("Widget", 1).SetLowPriority(true);
            var consumer = builder.Consumer("Widget").Target(10);
            builder.Link(builder.Supply("Ore"), onlyOption, consumer);

            var data = builder.Build();
            data.Solve();

            // The large finite coefficient on the only feasible path must not produce NaN, zero, or
            // an unbounded solve.
            AssertFloatsAreEqual(10, data.RecipeRate("only-option"));
            AssertFloatsAreEqual(10, data.SupplyRate("Ore"));
        }

        [TestMethod]
        public void DefaultPriority_CyclicGraphStillSolves() {
            // Mirrors SolverTest.cs's TestCycle exactly; defaults were already in place there too, but
            // this pins it under the corpus that specifically targets the shipped solver defaults.
            var builder = GraphBuilder.Create();

            var fixer = builder.Recipe("fixer").Input("Broken", 5).Output("Fixed", 5);
            var breaker = builder.Recipe("breaker").Input("Fixed", 1).Output("Broken", 1).SetTarget(10);
            builder.Link(fixer, breaker);
            builder.Link(breaker, fixer);

            var data = builder.Build();
            data.Solve();

            AssertFloatsAreEqual(2, data.RecipeRate("fixer"));
        }

        private static void AssertFloatsAreEqual(double expected, double actual) =>
            Assert.AreEqual(expected, actual, 0.0001);
    }
}
