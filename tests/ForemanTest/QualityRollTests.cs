using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Models;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class QualityRollTests : ForemanTestBase {
        private static readonly string[] TierNames = ["normal", "uncommon", "rare", "epic", "legendary"];

        private static async Task<(RecipeNode Node, DataCache Cache, IQuality Normal)> BuildNodeAsync(string preset, IModule? module = null, int moduleCount = 4) {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(preset).ConfigureAwait(false);
            GraphSessionTestHelper.TestContext ctx = GraphSessionTestHelper.CreateContext(cache);
            ProductionGraph graph = ctx.NewGraph();
            RecipeNode node = graph.CreateRecipeNode(new RecipeQualityPair(cache.Recipes["electronic-circuit"], ctx.Quality), Point.Empty);
            node.SelectedAssembler = new AssemblerQualityPair(cache.Assemblers["assembling-machine-3"], ctx.Quality);
            node.MaxQualitySteps = 5;
            module ??= cache.Modules["quality-module-3"];
            for (int i = 0; i < moduleCount; i++)
                node.AssemblerModulesAdd(new ModuleQualityPair(module, ctx.Quality));
            return (node, cache, ctx.Quality);
        }

        private static double[] OutputRates(RecipeNode node, DataCache cache) {
            IItem item = cache.Items["electronic-circuit"];
            return [.. TierNames.Select(name => {
                var pair = new ItemQualityPair(item, cache.Qualities[name]);
                return node.Outputs.Contains(pair) ? node.outputRateFor(pair) : 0;
            })];
        }

        private static double[] Shares(double[] rates) {
            double total = rates.Sum();
            return [.. rates.Select(r => r / total)];
        }

        private static void AssertShares(double[] expected, double[] actual) {
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], actual[i], 1e-12, TierNames[i]);
        }

        private static void AssertChances(double[] expected, IReadOnlyList<(IQuality Quality, double Chance)> actual, IQuality[] tiers, int firstTier) {
            Assert.HasCount(expected.Length, actual);
            for (int i = 0; i < expected.Length; i++) {
                Assert.AreSame(tiers[firstTier + i], actual[i].Quality);
                Assert.AreEqual(expected[i], actual[i].Chance, 1e-12, "tier " + (firstTier + i));
            }
        }

        private static readonly (double next, double chain)[] Uniform = [(1, 0.1), (1, 0.1), (1, 0.1), (1, 0.1), (0, 0)];

        [TestMethod]
        public void Distribute_ZeroEffect_ReturnsNoUpgrades() {
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(new DataCache(true), Uniform);
            Assert.IsEmpty(QualityRoll.Distribute(tiers[0], 0, 5));
        }

        [TestMethod]
        public void Distribute_EffectAboveOne_FirstChanceCapsAtOneButChainUsesUncappedP() {
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(new DataCache(true), Uniform);
            AssertChances([1, 0.15, 0.015, 0.0015], QualityRoll.Distribute(tiers[0], 1.5, 5), tiers, 1);
            AssertChances([1, 1, 0.12, 0.012], QualityRoll.Distribute(tiers[0], 12, 5), tiers, 1);
        }

        [TestMethod]
        public void Distribute_ChainFallsBackToNext_WhenNoChainProbability() {
            var cache = new DataCache(true);
            var normal = new QualityPrototype(cache, "normal", "Normal", "a") { NextProbability = 0.5 };
            var uncommon = new QualityPrototype(cache, "uncommon", "Uncommon", "b") { NextProbability = 0.4 };
            var rare = new QualityPrototype(cache, "rare", "Rare", "c");
            normal.NextQuality = uncommon;
            uncommon.NextQuality = rare;

            Assert.AreEqual(0.4, uncommon.ChainProbability, 0);
            AssertChances([0.1, 0.1 * 0.4], QualityRoll.Distribute(normal, 0.2, 5), [normal, uncommon, rare], 1);
        }

        [TestMethod]
        public void QualityChain_NextTierDisabled_KeepsShareAtLastEnabledTier() {
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(new DataCache(true), Uniform);
            tiers[2].Enabled = false;
            AssertChances([0.5], QualityRoll.Distribute(tiers[0], 0.5, 5), tiers, 1);

            tiers[2].Enabled = true;
            tiers[2].Available = false;
            AssertChances([0.5], QualityRoll.Distribute(tiers[0], 0.5, 5), tiers, 1);
        }

        [TestMethod]
        public void Distribute_MaxStepsTwo_StopsAtFirstUpgrade() {
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(new DataCache(true), Uniform);
            AssertChances([0.5], QualityRoll.Distribute(tiers[0], 0.5, 2), tiers, 1);
            Assert.IsEmpty(QualityRoll.Distribute(tiers[0], 0.5, 1));
        }

        [TestMethod]
        public void Distribute_FromUncommon_UsesUncommonNextThenChain() {
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(new DataCache(true), [(0.5, 0.2), (0.6, 0.3), (0.7, 0.4), (0.8, 0.5), (0, 0)]);
            AssertChances([0.24, 0.24 * 0.4, 0.24 * 0.4 * 0.5], QualityRoll.Distribute(tiers[1], 0.4, 5), tiers, 2);
        }

        [TestMethod]
        public void Distribute_ExplicitZeroChainOnReachedTier_StopsAfterFirstUpgrade() {
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(new DataCache(true), [(1, 0.1), (1, 0), (1, 0.1), (1, 0.1), (0, 0)]);
            AssertChances([0.5], QualityRoll.Distribute(tiers[0], 0.5, 5), tiers, 1);
        }

        [TestMethod]
        public void Distribute_ChainProbabilityDiffersPerTier_UsesTheTierJustReached() {
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(new DataCache(true), [(1, 0.5), (1, 0.2), (1, 0.1), (1, 0.05), (0, 0)]);
            AssertChances([0.5, 0.5 * 0.2, 0.5 * 0.2 * 0.1, 0.5 * 0.2 * 0.1 * 0.05], QualityRoll.Distribute(tiers[0], 0.5, 5), tiers, 1);
        }

        [TestMethod]
        public async Task SpaceAge21_Qualities_ReadChainProbabilityFromPreset() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            foreach (string name in TierNames.Take(4)) {
                Assert.AreEqual(1, cache.Qualities[name].NextProbability, 0, name);
                Assert.AreEqual(0.1, cache.Qualities[name].ChainProbability, 0, name);
            }
            Assert.AreEqual(0, cache.Qualities["legendary"].ChainProbability, 0);
        }

        [TestMethod]
        public async Task SpaceAge20_Qualities_ChainProbabilityFallsBackToNextProbability() {
            DataCache cache = await SpaceAgeDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            foreach (IQuality quality in cache.Qualities.Values)
                Assert.AreEqual(quality.NextProbability, quality.ChainProbability, 0, quality.Name);
        }

        [TestMethod]
        public async Task SpaceAge20_FourQualityModule3_OutputsMatchCurrentChain() {
            (RecipeNode node, DataCache cache, _) = await BuildNodeAsync(SpaceAgeDataCacheFixture.PresetName).ConfigureAwait(false);
            double[] expected = [0.9, 0.09, 0.009000000000000001, 0.0009000000000000002, 0.00010000000000000003];
            double[] actual = OutputRates(node, cache);
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], actual[i], 0, TierNames[i]);
        }

        [TestMethod]
        public async Task SpaceAge21_FourQualityModule3_LegendaryShareFollowsChainProbability() {
            (RecipeNode node, DataCache cache, IQuality normal) = await BuildNodeAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            Assert.AreEqual(0.1, 4 * cache.Modules["quality-module-3"].GetQualityBonus(normal), 1e-12);

            AssertShares([0.9, 0.09, 0.009000000000000001, 0.0009000000000000002, 0.00010000000000000002], Shares(OutputRates(node, cache)));
        }

        [TestMethod]
        public async Task Node_QualityEffectAboveOne_BaseTierDroppedAndSharesMatchRollChances() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            var strong = new ModulePrototype(cache, "quality-module-3", "Quality module 3") { QualityBonus = 1.5 };
            (RecipeNode node, _, _) = await BuildNodeAsync(PresetDataCacheFixture.SpaceAge21, strong, 1).ConfigureAwait(false);
            AssertShares([0, 0.85, 0.135, 0.013500000000000002, 0.0015000000000000005], Shares(OutputRates(node, cache)));

            var stronger = new ModulePrototype(cache, "quality-module-3", "Quality module 3") { QualityBonus = 12 };
            (RecipeNode node12, _, _) = await BuildNodeAsync(PresetDataCacheFixture.SpaceAge21, stronger, 1).ConfigureAwait(false);
            AssertShares([0, 0, 0.8800000000000001, 0.10800000000000001, 0.012000000000000004], Shares(OutputRates(node12, cache)));
        }
    }
}
