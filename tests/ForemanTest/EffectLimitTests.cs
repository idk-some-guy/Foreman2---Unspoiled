using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Models;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class EffectLimitTests : ForemanTestBase {
        private static readonly EffectRange Wide = new(-1000, 1000);

        private static EffectLimits Limits(EffectRange? speed = null, EffectRange? productivity = null, EffectRange? quality = null) =>
            new(Wide, speed ?? Wide, productivity ?? Wide, Wide, quality ?? new EffectRange(0, 1000));

        private static (RecipeNode Node, GraphSessionTestHelper.TestContext Context) BuildSyntheticNode(EffectLimits limits, double maxProductivity = 1000) {
            GraphSessionTestHelper.TestContext ctx = GraphSessionTestHelper.CreateContext();
            var recipe = new RecipePrototype(ctx.Cache, "limit-recipe", "limit-recipe", ctx.Subgroup, "z") { MaxProductivityBonus = maxProductivity };
            TestPrototypeFactory.SetRecipeTime(recipe, 1);
            TestPrototypeFactory.LinkRecipeAndAssembler(recipe, TestPrototypeFactory.CreateTestAssembler(ctx.Cache, limits));
            RecipeNode node = ctx.NewGraph().CreateRecipeNode(new RecipeQualityPair(recipe, ctx.Quality), Point.Empty);
            return (node, ctx);
        }

        private static void AddModules(RecipeNode node, GraphSessionTestHelper.TestContext ctx, ModuleEffects effects, int count) {
            ModulePrototype module = TestPrototypeFactory.CreateTestModule(ctx.Cache, effects);
            for (int i = 0; i < count; i++)
                node.AssemblerModulesAdd(new ModuleQualityPair(module, ctx.Quality));
        }

        private static ModuleEffects Only(double speed = 0, double productivity = 0, double quality = 0) => new(0, speed, productivity, 0, quality);

        private static RecipeNode BuildPresetNode(DataCache cache, GraphSessionTestHelper.TestContext ctx, string module, int count) {
            RecipeNode node = ctx.NewGraph().CreateRecipeNode(new RecipeQualityPair(cache.Recipes["electronic-circuit"], ctx.Quality), Point.Empty);
            node.SelectedAssembler = new AssemblerQualityPair(cache.Assemblers["assembling-machine-3"], ctx.Quality);
            for (int i = 0; i < count; i++)
                node.AssemblerModulesAdd(new ModuleQualityPair(cache.Modules[module], ctx.Quality));
            return node;
        }

        private static void AssertEffects(RecipeNode node, double speed, double consumption, double pollution, double quality, double productivity) {
            Assert.AreEqual(speed, node.GetSpeedMultiplier(), 0, "speed");
            Assert.AreEqual(consumption, node.GetConsumptionMultiplier(), 0, "consumption");
            Assert.AreEqual(pollution, node.GetPollutionMultiplier(), 0, "pollution");
            Assert.AreEqual(quality, node.GetQualityMultiplier(), 0, "quality");
            Assert.AreEqual(productivity, node.GetProductivityBonus(), 0, "productivity");
        }

        [TestMethod]
        public async Task SpaceAge20_ExtremeModules_MultipliersUnchanged() {
            DataCache cache = await SpaceAgeDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            GraphSessionTestHelper.TestContext ctx = GraphSessionTestHelper.CreateContext(cache);

            RecipeNode beaconed = BuildPresetNode(cache, ctx, "efficiency-module-3", 4);
            beaconed.SelectedBeacon = new BeaconQualityPair(cache.Beacons["beacon"], ctx.Quality);
            beaconed.BeaconCount = 8;
            for (int i = 0; i < 2; i++)
                beaconed.BeaconModulesAdd(new ModuleQualityPair(cache.Modules["speed-module-3"], ctx.Quality));
            AssertEffects(beaconed, 5.242, 4.9388, 1, 0, 0);

            AssertEffects(BuildPresetNode(cache, ctx, "efficiency-module-3", 4), 1, (double)0.2f, 1, 0, 0);
            AssertEffects(BuildPresetNode(cache, ctx, "speed-module-3", 4), 3, 3.8, 1, 0, 0);

            RecipeNode productive = BuildPresetNode(cache, ctx, "productivity-module-3", 4);
            productive.ExtraProductivityBonus = 0.3;
            AssertEffects(productive, 0.3999999999999999, 4.2, 1.4000000000000004, 0, 0.7);
        }

        [TestMethod]
        public void EffectLimits_SpeedHighBound_CapsMultiplier() {
            (RecipeNode node, var ctx) = BuildSyntheticNode(Limits(speed: new EffectRange(-0.8, 0.5)));
            AddModules(node, ctx, Only(speed: 0.5), 4);
            Assert.AreEqual(1.5, node.GetSpeedMultiplier(), 1e-12);
        }

        [TestMethod]
        public void EffectLimits_SpeedLowBound_FloorsMultiplierAtOnePlusLow() {
            (RecipeNode node, var ctx) = BuildSyntheticNode(Limits(speed: new EffectRange(-0.5, 1000)));
            AddModules(node, ctx, Only(speed: -0.3), 4);
            Assert.AreEqual(0.5, node.GetSpeedMultiplier(), 1e-12);
        }

        [TestMethod]
        public void EffectLimits_ProductivityLowNegative_FinalBonusFloorsAtZero() {
            (RecipeNode node, var ctx) = BuildSyntheticNode(Limits(productivity: new EffectRange(-0.8, 1000)));
            node.ExtraProductivityBonus = 0.3;
            AddModules(node, ctx, Only(productivity: -0.5), 1);
            Assert.AreEqual(0, node.GetProductivityBonus(), 0);
        }

        [TestMethod]
        public void EffectLimits_ProductivityLowBound_ClampsBeforeResearchBonus() {
            (RecipeNode node, var ctx) = BuildSyntheticNode(Limits(productivity: new EffectRange(-0.2, 1000)));
            node.ExtraProductivityBonus = 0.5;
            AddModules(node, ctx, Only(productivity: -0.5), 1);
            Assert.AreEqual(0.3, node.GetProductivityBonus(), 1e-12);
        }

        [TestMethod]
        public void EffectLimits_ProductivityHighBound_AppliesBeforeResearchBonus() {
            (RecipeNode node, var ctx) = BuildSyntheticNode(Limits(productivity: new EffectRange(-0.8, 0.1)));
            node.ExtraProductivityBonus = 0.3;
            AddModules(node, ctx, Only(productivity: 0.1), 4);
            Assert.AreEqual(0.4, node.GetProductivityBonus(), 1e-12);
        }

        [TestMethod]
        public void EffectLimits_ProductivityResult_StillCappedAtRecipeMaximum() {
            (RecipeNode node, var ctx) = BuildSyntheticNode(Limits(), maxProductivity: 0.25);
            AddModules(node, ctx, Only(productivity: 0.1), 4);
            Assert.AreEqual(0.25, node.GetProductivityBonus(), 0);
        }

        [TestMethod]
        public void EffectLimits_QualityHighBound_CapsEffect() {
            (RecipeNode node, var ctx) = BuildSyntheticNode(Limits(quality: new EffectRange(0, 0.1)));
            AddModules(node, ctx, Only(quality: 0.1), 4);
            Assert.AreEqual(0.1, node.GetQualityMultiplier(), 1e-12);
        }

        [TestMethod]
        public void EffectLimits_QualityNegativeLow_StillFloorsAtZero() {
            (RecipeNode node, var ctx) = BuildSyntheticNode(Limits(quality: new EffectRange(-0.5, 1000)));
            AddModules(node, ctx, Only(quality: -0.1), 3);
            Assert.AreEqual(0, node.GetQualityMultiplier(), 0);
        }

        [TestMethod]
        public async Task SpaceAge21_DefaultLimits_FloorConsumptionAtOnePlusLow() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            GraphSessionTestHelper.TestContext ctx = GraphSessionTestHelper.CreateContext(cache);
            RecipeNode node = BuildPresetNode(cache, ctx, "efficiency-module-3", 4);
            Assert.AreEqual(1 + -0.8, node.GetConsumptionMultiplier(), 0);
        }

        [TestMethod]
        public async Task SpaceAge21_Assembler3_LimitsEqualExported() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            string path = Path.Combine(PresetDataCacheFixture.PresetsDirectory, PresetDataCacheFixture.SpaceAge21 + ".pjson");
            JsonNode exported = JsonNode.Parse(File.ReadAllText(path))!["entities"]!.AsArray()
                .First(e => e!["name"]!.GetValue<string>() == "assembling-machine-3")!["effect_limits"]!;
            EffectLimits actual = ((IAssembler)cache.Assemblers["assembling-machine-3"]).EffectLimits;

            void Check(string key, EffectRange range) {
                Assert.AreEqual(exported[key]!["low"]!.GetValue<double>(), range.Low, 0, key + " low");
                Assert.AreEqual(exported[key]!["high"]!.GetValue<double>(), range.High, 0, key + " high");
            }
            Check("consumption", actual.Consumption);
            Check("speed", actual.Speed);
            Check("productivity", actual.Productivity);
            Check("pollution", actual.Pollution);
            Check("quality", actual.Quality);
            Assert.IsFalse(actual.IsLegacy);
        }

        [TestMethod]
        public async Task EffectLimits_AbsentKey_IsLegacySingleton() {
            DataCache cache = await SpaceAgeDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            Assert.AreSame(EffectLimits.Legacy, cache.Assemblers["assembling-machine-3"].EffectLimits);
            Assert.IsTrue(cache.Assemblers["assembling-machine-3"].EffectLimits.IsLegacy);

            EffectLimits copy = EffectLimits.Legacy with { };
            Assert.AreEqual(EffectLimits.Legacy, copy);
            Assert.IsFalse(copy.IsLegacy);
        }

        private static EffectLimits LoadLimits(JsonObject effectLimits) {
            var cache = new DataCache(filterRecipes: false);
            DataCacheStore store = TestDataCacheHelper.RequireStore(cache);
            new EntityDataLoader(cache, store, new PresetLoadSession()).ProcessEntity(new JsonObject {
                ["name"] = "limit-machine",
                ["localised_name"] = "Limit machine",
                ["type"] = "assembling-machine",
                ["fuel_type"] = "electricity",
                ["uses_module_effects"] = true,
                ["effect_limits"] = effectLimits,
            }, []);
            return cache.Assemblers["limit-machine"].EffectLimits;
        }

        [TestMethod]
        public void EffectLimits_PartialKey_MissingBoundsTakeApiDefaults() {
            EffectLimits limits = LoadLimits(new JsonObject { ["speed"] = new JsonObject { ["low"] = -0.5 } });

            Assert.IsFalse(limits.IsLegacy);
            Assert.AreEqual(new EffectRange(-0.5, 1000), limits.Speed);
            Assert.AreEqual(new EffectRange(-0.8, 1000), limits.Consumption);
            Assert.AreEqual(new EffectRange(-0.8, 1000), limits.Productivity);
            Assert.AreEqual(new EffectRange(-0.8, 1000), limits.Pollution);
            Assert.AreEqual(new EffectRange(0, 1000), limits.Quality);
        }

        [TestMethod]
        public void EffectLimits_LowAboveHigh_IsSwappedAndMultiplierDoesNotThrow() {
            EffectLimits limits = LoadLimits(new JsonObject { ["speed"] = new JsonObject { ["low"] = 2.0, ["high"] = 1.0 } });
            Assert.AreEqual(new EffectRange(1, 2), limits.Speed);

            (RecipeNode node, _) = BuildSyntheticNode(limits);
            Assert.AreEqual(2, node.GetSpeedMultiplier(), 0);
        }
    }
}
