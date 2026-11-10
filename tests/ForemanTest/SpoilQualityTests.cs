using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Models;
using Foreman.Models.Nodes;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class SpoilQualityTests : ForemanTestBase {
        private static readonly (double next, double chain)[] FiveTiers = [(1, 0.1), (1, 0.1), (1, 0.1), (1, 0.1), (0, 0)];

        private static (DataCache Cache, QualityPrototype[] Tiers, ItemPrototype Item) NewChain(int change = 0, int? min = null, int? max = null) {
            var cache = new DataCache(filterRecipes: true);
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(cache, FiveTiers);
            tiers[4].Level = 5;
            foreach (QualityPrototype tier in tiers)
                TestDataCacheHelper.RegisterQuality(cache, tier);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            ItemPrototype item = TestDataCacheHelper.GetOrCreateItem(cache, subgroup, "spoiling");
            item.SpoilQualityChange = change;
            item.SpoilQualityMin = min is int lo ? tiers[lo] : null;
            item.SpoilQualityMax = max is int hi ? tiers[hi] : null;
            return (cache, tiers, item);
        }

        [TestMethod]
        public void Resolve_Defaults_KeepsInputQuality() {
            var (_, tiers, item) = NewChain();
            foreach (QualityPrototype tier in tiers)
                Assert.AreSame(tier, SpoilQuality.Resolve(item, tier));
        }

        [TestMethod]
        public void Resolve_ChangeMinusOne_FromRare_IsUncommon() {
            var (_, tiers, item) = NewChain(change: -1);
            Assert.AreSame(tiers[1], SpoilQuality.Resolve(item, tiers[2]));
        }

        [TestMethod]
        public void Resolve_ChangePlusTwo_FromEpic_StopsAtLegendary() {
            var (_, tiers, item) = NewChain(change: 2);
            Assert.AreSame(tiers[4], SpoilQuality.Resolve(item, tiers[3]));
        }

        [TestMethod]
        public void Resolve_ChangeMinusTwo_FromUncommon_StopsAtNormal() {
            var (_, tiers, item) = NewChain(change: -2);
            Assert.AreSame(tiers[0], SpoilQuality.Resolve(item, tiers[1]));
        }

        [TestMethod]
        public void Resolve_ChangeZeroWithBounds_ClampsIntoBounds() {
            var (_, tiers, item) = NewChain(min: 1, max: 2);
            Assert.AreSame(tiers[1], SpoilQuality.Resolve(item, tiers[0]));
            Assert.AreSame(tiers[1], SpoilQuality.Resolve(item, tiers[1]));
            Assert.AreSame(tiers[2], SpoilQuality.Resolve(item, tiers[2]));
            Assert.AreSame(tiers[2], SpoilQuality.Resolve(item, tiers[4]));
        }

        [TestMethod]
        public void Resolve_ChangePlusOne_StopsAtMax() {
            var (_, tiers, item) = NewChain(change: 1, min: 1, max: 2);
            Assert.AreSame(tiers[1], SpoilQuality.Resolve(item, tiers[0]));
            Assert.AreSame(tiers[2], SpoilQuality.Resolve(item, tiers[1]));
            Assert.AreSame(tiers[2], SpoilQuality.Resolve(item, tiers[2]));
        }

        [TestMethod]
        public void Resolve_ChangeMinusOne_StopsAtMin() {
            var (_, tiers, item) = NewChain(change: -1, min: 1, max: 3);
            Assert.AreSame(tiers[1], SpoilQuality.Resolve(item, tiers[2]));
            Assert.AreSame(tiers[1], SpoilQuality.Resolve(item, tiers[1]));
            Assert.AreSame(tiers[1], SpoilQuality.Resolve(item, tiers[0]));
        }

        [TestMethod]
        public void Resolve_ChangePlusOne_InputAlreadyPastMax_ClampsToMax() {
            var (_, tiers, item) = NewChain(change: 1, min: 1, max: 2);
            Assert.AreSame(tiers[2], SpoilQuality.Resolve(item, tiers[3]));
        }

        [TestMethod]
        public void Resolve_OnlyMinBound_LeavesTopOpen() {
            var (_, tiers, item) = NewChain(change: 1, min: 2);
            Assert.AreSame(tiers[2], SpoilQuality.Resolve(item, tiers[0]));
            Assert.AreSame(tiers[4], SpoilQuality.Resolve(item, tiers[4]));
        }

        [TestMethod]
        public void SpoilNode_ItemWithChange_OutputsResolvedQuality() {
            var (cache, tiers, item) = NewChain(change: 1);
            var subgroup = new SubgroupPrototype(cache, "§§test:spoiled-subgroup", "z");
            ItemPrototype spoiled = TestDataCacheHelper.GetOrCreateItem(cache, subgroup, "spoiled");
            item.SpoilResult = spoiled;
            spoiled.SpoilOriginsInternal.Add(item);
            item.spoilageTimes[tiers[1]] = 60;
            var graph = new ProductionGraph { DefaultAssemblerQuality = tiers[0] };

            SpoilNode node = graph.CreateSpoilNode(new ItemQualityPair(item, tiers[1]), spoiled, Point.Empty);

            Assert.AreSame(spoiled, node.OutputItem.Item);
            Assert.AreSame(tiers[2], node.OutputItem.Quality);
        }

        [TestMethod]
        public void SpoilNode_UpdateSpoilResult_UsesResolvedQuality() {
            var (cache, tiers, item) = NewChain(change: 1);
            var subgroup = new SubgroupPrototype(cache, "§§test:spoiled-subgroup", "z");
            ItemPrototype spoiled = TestDataCacheHelper.GetOrCreateItem(cache, subgroup, "spoiled");
            ItemPrototype stale = TestDataCacheHelper.GetOrCreateItem(cache, subgroup, "stale");
            item.SpoilResult = spoiled;
            spoiled.SpoilOriginsInternal.Add(item);
            item.spoilageTimes[tiers[1]] = 60;
            var graph = new ProductionGraph { DefaultAssemblerQuality = tiers[0] };
            SpoilNode node = graph.CreateSpoilNode(new ItemQualityPair(item, tiers[1]), stale, Point.Empty);

            ((SpoilNodeController)node.Controller).UpdateSpoilResult();

            Assert.AreSame(spoiled, node.OutputItem.Item);
            Assert.AreSame(tiers[2], node.OutputItem.Quality);
        }

        [TestMethod]
        public async Task SpaceAge20_SpoilNodes_OutputQualityUnchanged() {
            DataCache cache = await SpaceAgeDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            IQuality legendary = cache.Qualities["legendary"];
            var graph = new ProductionGraph { DefaultAssemblerQuality = cache.DefaultQuality };

            SpoilNode node = graph.CreateSpoilNode(new ItemQualityPair(cache.Items["nutrients"], legendary), cache.Items["spoilage"], Point.Empty);

            Assert.AreSame(cache.Items["spoilage"], node.OutputItem.Item);
            Assert.AreSame(legendary, node.OutputItem.Quality);
        }

        [TestMethod]
        public async Task SpaceAge21_Items_HaveNoSpoilQualityRules() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            Assert.IsEmpty(cache.Items.Values.Where(i => i.SpoilQualityChange != 0 || i.SpoilQualityMin is not null || i.SpoilQualityMax is not null));
        }

        [TestMethod]
        public void Item_SpoilQualityKeys_LoadAsQualitiesAndChange() {
            var (cache, tiers, loader) = NewLoader();

            loader.ProcessItem(ItemJson(i => {
                i["spoil_quality_min"] = "tier1";
                i["spoil_quality_max"] = "tier3";
                i["spoil_quality_change"] = -1;
            }), []);

            IItem item = cache.Items["shape-item"];
            Assert.AreEqual(-1, item.SpoilQualityChange);
            Assert.AreSame(tiers[1], item.SpoilQualityMin);
            Assert.AreSame(tiers[3], item.SpoilQualityMax);
        }

        [TestMethod]
        public void Item_WithoutSpoilQualityKeys_IsUnbounded() {
            var (cache, _, loader) = NewLoader();

            loader.ProcessItem(ItemJson(_ => { }), []);

            IItem item = cache.Items["shape-item"];
            Assert.AreEqual(0, item.SpoilQualityChange);
            Assert.IsNull(item.SpoilQualityMin);
            Assert.IsNull(item.SpoilQualityMax);
        }

        [TestMethod]
        public void InputFor_Defaults_IsTheOutputQuality() {
            var (_, tiers, item) = NewChain();
            Assert.AreSame(tiers[2], SpoilQuality.InputFor(item, tiers[2]));
        }

        [TestMethod]
        public void InputFor_ChangeMinusOne_OutputRare_NeedsEpicInput() {
            var (_, tiers, item) = NewChain(change: -1);
            Assert.AreSame(tiers[3], SpoilQuality.InputFor(item, tiers[2]));
        }

        [TestMethod]
        public void InputFor_BoundsExcludeOutput_IsNull() {
            var (_, tiers, item) = NewChain(min: 1, max: 2);
            Assert.IsNull(SpoilQuality.InputFor(item, tiers[3]));
        }

        [TestMethod]
        public void Item_SpoilQualityKeyNamesUnknownQuality_LeavesBoundOpen() {
            var (cache, _, loader) = NewLoader();

            loader.ProcessItem(ItemJson(i => i["spoil_quality_max"] = "nope"), []);

            Assert.IsNull(cache.Items["shape-item"].SpoilQualityMax);
        }

        private static (DataCache Cache, QualityPrototype[] Tiers, PresetDataLoader Loader) NewLoader() {
            var cache = new DataCache(filterRecipes: false);
            DataCacheStore store = TestDataCacheHelper.RequireStore(cache);
            QualityPrototype[] tiers = TestPrototypeFactory.CreateQualityChain(cache, FiveTiers);
            foreach (QualityPrototype tier in tiers)
                TestDataCacheHelper.RegisterQuality(cache, tier);
            return (cache, tiers, new PresetDataLoader(cache, store, new PresetLoadSession()));
        }

        private static JsonObject ItemJson(System.Action<JsonObject> shape) {
            var item = new JsonObject {
                ["name"] = "shape-item",
                ["localised_name"] = "Shape item",
                ["order"] = "z",
                ["stack_size"] = 50,
                ["weight"] = 1,
                ["ingredient_to_weight_coefficient"] = 1,
            };
            shape(item);
            return item;
        }
    }
}
