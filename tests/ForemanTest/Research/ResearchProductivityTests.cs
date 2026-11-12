using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Models.Research;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ForemanTest.Research {
    [TestClass]
    public class ResearchProductivityTests : ForemanTestBase {
        private static readonly EffectRange Wide = new(-1000, 1000);

        private static EffectLimits Limits(EffectRange? productivity = null) =>
            new(Wide, Wide, productivity ?? Wide, Wide, new EffectRange(0, 1000));

        private static (RecipeNode Node, GraphSessionTestHelper.TestContext Context) BuildNode(EffectLimits limits, double maxProductivity = 1000, bool miner = false) {
            GraphSessionTestHelper.TestContext ctx = GraphSessionTestHelper.CreateContext();
            var recipe = new RecipePrototype(ctx.Cache, "research-recipe", "research-recipe", ctx.Subgroup, "z") { MaxProductivityBonus = maxProductivity };
            TestPrototypeFactory.SetRecipeTime(recipe, 1);
            AssemblerPrototype assembler = TestPrototypeFactory.CreateTestAssembler(ctx.Cache, limits);
            if (miner) {
                assembler = new AssemblerPrototype(ctx.Cache, "§§test:miner", "Test Miner", EntityType.Miner, EnergySource.Electric, false) {
                    EffectLimits = limits,
                    ModuleSlots = 4,
                    AllowModules = true,
                };
                foreach (IQuality quality in ctx.Cache.Qualities.Values)
                    assembler.SpeedInternal[quality] = 1;
            }
            TestPrototypeFactory.LinkRecipeAndAssembler(recipe, assembler);
            RecipeNode node = ctx.NewGraph().CreateRecipeNode(new RecipeQualityPair(recipe, ctx.Quality), Point.Empty);
            return (node, ctx);
        }

        private static void AddProductivityModules(RecipeNode node, GraphSessionTestHelper.TestContext ctx, double productivity, int count) {
            ModulePrototype module = TestPrototypeFactory.CreateTestModule(ctx.Cache, new ModuleEffects(0, 0, productivity, 0, 0));
            for (int i = 0; i < count; i++)
                node.AssemblerModulesAdd(new ModuleQualityPair(module, ctx.Quality));
        }

        private static void SetRecipeResearch(RecipeNode node, double bonus) =>
            node.MyGraph.Research = ResearchState.None.WithRecipe(node.BaseRecipe.Recipe!.Name, bonus);

        private static void SetMiningResearch(RecipeNode node, double bonus) =>
            node.MyGraph.Research = ResearchState.None.WithValue(ResearchField.MiningProductivity, bonus);

        [TestMethod]
        public void GetProductivityBonus_RecipeResearch_AddsAfterEffectClamp() {
            (RecipeNode node, var ctx) = BuildNode(Limits(new EffectRange(-0.8, 0.1)));
            SetRecipeResearch(node, 0.3);
            AddProductivityModules(node, ctx, 0.1, 4);

            Assert.AreEqual(0.4, node.GetProductivityBonus(), 1e-12);
        }

        [TestMethod]
        public void GetProductivityBonus_ResearchAndExtra_Stack() {
            (RecipeNode node, _) = BuildNode(Limits());
            node.ExtraProductivityBonus = 0.1;
            SetRecipeResearch(node, 0.2);

            Assert.AreEqual(0.3, node.GetProductivityBonus(), 1e-12);
        }

        [TestMethod]
        public void GetProductivityBonus_ResearchPlusExtra_CappedAtRecipeMaximum() {
            (RecipeNode node, _) = BuildNode(Limits(), maxProductivity: 0.25);
            node.ExtraProductivityBonus = 0.1;
            SetRecipeResearch(node, 0.2);

            Assert.AreEqual(0.25, node.GetProductivityBonus(), 1e-12);
            Assert.IsTrue(node.ProductivityAtRecipeLimit);
            Assert.AreEqual(0.25, node.MaxProductivityBonus, 0);
        }

        [TestMethod]
        public void GetProductivityBonus_MiningResearch_OnMiner_AddsUncapped() {
            (RecipeNode node, _) = BuildNode(Limits(), maxProductivity: 0.25, miner: true);
            node.ExtraProductivityBonus = 0.1;
            SetMiningResearch(node, 1.0);

            Assert.AreEqual(1.1, node.GetProductivityBonus(), 1e-12);
            Assert.AreEqual(1.0, node.ResearchProductivityBonus, 1e-12);
        }

        [TestMethod]
        public void GetProductivityBonus_MiningResearch_OnAssembler_IsIgnored() {
            (RecipeNode node, _) = BuildNode(Limits());
            SetMiningResearch(node, 1.0);

            Assert.AreEqual(0, node.GetProductivityBonus(), 0);
            Assert.AreEqual(0, node.ResearchProductivityBonus, 0);
        }

        [TestMethod]
        public void GetProductivityBonus_ResearchForUnknownRecipe_IsIgnored() {
            (RecipeNode node, _) = BuildNode(Limits());
            node.ExtraProductivityBonus = 0.1;
            node.MyGraph.Research = ResearchState.None.WithRecipe("not-in-preset", 5);

            Assert.AreEqual(0.1, node.GetProductivityBonus(), 1e-12);
            Assert.IsFalse(node.HasResearchProductivity);
        }

        [TestMethod]
        public void HasResearchProductivity_EntryOrMiningOnMiner() {
            (RecipeNode assembler, _) = BuildNode(Limits());
            SetRecipeResearch(assembler, 0);
            Assert.IsFalse(assembler.HasResearchProductivity);

            SetRecipeResearch(assembler, 0.1);
            Assert.IsTrue(assembler.HasResearchProductivity);

            (RecipeNode miner, _) = BuildNode(Limits(), miner: true);
            SetMiningResearch(miner, 0.2);
            Assert.IsTrue(miner.HasResearchProductivity);

            SetMiningResearch(miner, 0);
            Assert.IsFalse(miner.HasResearchProductivity);
        }

        [TestMethod]
        public void GetProductivityBonus_LegacyLimits_IncludesResearch() {
            (RecipeNode node, var ctx) = BuildNode(EffectLimits.Legacy);
            SetRecipeResearch(node, 0.3);
            AddProductivityModules(node, ctx, 0.1, 2);
            Assert.AreEqual(0.5, node.GetProductivityBonus(), 1e-12);

            (RecipeNode capped, var cappedCtx) = BuildNode(EffectLimits.Legacy, maxProductivity: 0.4);
            SetRecipeResearch(capped, 0.3);
            AddProductivityModules(capped, cappedCtx, 0.1, 2);
            Assert.AreEqual(0.4, capped.GetProductivityBonus(), 1e-12);
            Assert.IsTrue(capped.ProductivityAtRecipeLimit);
        }

        [TestMethod]
        public void ResearchChange_ResolvesWithNewProductivity() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            data.Solve();
            SupplierNode ore = data.Graph.Nodes.OfType<SupplierNode>().Single();
            Assert.AreEqual(10, ore.ActualRate, 1e-9);
            RecipeNode recipe = data.Graph.Nodes.OfType<RecipeNode>().Single();
            var prototype = (RecipePrototype)recipe.BaseRecipe.Recipe!;
            foreach (IItem product in prototype.ProductPSetInternal.Keys.ToList())
                prototype.ProductPSetInternal[product] = prototype.ProductSet[product];
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = SolveWiring.Wire(data.Graph);

            data.Graph.Research = ResearchState.None.WithRecipe(recipe.BaseRecipe.Recipe!.Name, 0.5);
            worker.RunAll();
            ctx.RunAll();

            CollectionAssert.AreEqual(new[] { "started", "ended:Applied" }, events);
            Assert.AreEqual(10 / 1.5, ore.ActualRate, 1e-9);
        }
    }
}
