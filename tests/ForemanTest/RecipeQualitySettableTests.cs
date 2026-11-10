using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Models.Nodes;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class RecipeQualitySettableTests : ForemanTestBase {
        private const string Lock = "> Selected recipe can only be crafted at Normal quality.";

        private static Task<DataCache> SpaceAge21() => PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21);

        private static ProductionGraph NewGraph(DataCache cache) => new() { DefaultAssemblerQuality = cache.DefaultQuality };

        private static RecipeNode AddNode(ProductionGraph graph, DataCache cache, string recipe, string quality) =>
            graph.CreateRecipeNode(new RecipeQualityPair(cache.Recipes[recipe], cache.Qualities[quality]), Point.Empty);

        private static RecipeNodeController Controller(RecipeNode node) => (RecipeNodeController)node.Controller;

        private static ItemQualityPair Fluid(DataCache cache, string name) => new(cache.Items[name], cache.DefaultQuality!);

        [TestMethod]
        public async Task Recipe_CanSetQualityAbsent_IsTrue() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync("Factorio 2.0 Space Age").ConfigureAwait(false);
            Assert.IsTrue(cache.Recipes.Values.All(r => r.CanSetQuality));
        }

        [TestMethod]
        public async Task Node_PresetWithoutTheKey_NeverWarnsAboutQuality() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync("Factorio 2.0 Space Age").ConfigureAwait(false);
            ProductionGraph graph = NewGraph(cache);
            RecipeNode node = AddNode(graph, cache, "sulfur", "uncommon");
            node.UpdateState();

            Assert.DoesNotContain(w => w.Contains("can only be crafted"), node.GetWarnings());
            Assert.AreEqual(0, Controller(node).GetWarningResolutions().Keys.Count(k => k.StartsWith("Switch to ", StringComparison.Ordinal) && k.EndsWith(" quality", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task SpaceAge21_RecipeWithCanSetQualityFalse_LoadsFalse() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            Assert.IsFalse(cache.Recipes["sulfur"].CanSetQuality);
            Assert.IsFalse(cache.Recipes["advanced-oil-processing"].CanSetQuality);
            Assert.IsTrue(cache.Recipes["iron-gear-wheel"].CanSetQuality);
            Assert.AreEqual(23, cache.Recipes.Values.Count(r => !r.CanSetQuality));
        }

        [TestMethod]
        public async Task Vanilla21_RecipesWithCanSetQualityFalse_AreCounted() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.Vanilla21).ConfigureAwait(false);
            Assert.AreEqual(10, cache.Recipes.Values.Count(r => !r.CanSetQuality));
        }

        [TestMethod]
        public async Task Node_UnsettableRecipeAtUncommon_Warns() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            RecipeNode node = AddNode(NewGraph(cache), cache, "sulfur", "uncommon");
            node.UpdateState();

            Assert.AreEqual(NodeState.Warning, node.State);
            Assert.AreNotEqual(RecipeNode.Warnings.Clean, node.WarningSet & RecipeNode.Warnings.RecipeQualityCannotBeSet);
        }

        [TestMethod]
        public async Task Node_UnsettableRecipeAtNormal_DoesNotWarn() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            RecipeNode node = AddNode(NewGraph(cache), cache, "sulfur", "normal");
            node.UpdateState();

            Assert.AreEqual(RecipeNode.Warnings.Clean, node.WarningSet & RecipeNode.Warnings.RecipeQualityCannotBeSet);
            Assert.DoesNotContain(Lock, node.GetWarnings());
        }

        [TestMethod]
        public async Task Node_SettableRecipeAtUncommon_DoesNotWarn() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            RecipeNode node = AddNode(NewGraph(cache), cache, "iron-gear-wheel", "uncommon");
            node.UpdateState();

            Assert.AreEqual(RecipeNode.Warnings.Clean, node.WarningSet & RecipeNode.Warnings.RecipeQualityCannotBeSet);
        }

        private static (double NodeRate, double[] LinkRates, bool Warned) SolveSulfur(DataCache cache) {
            ProductionGraph graph = NewGraph(cache);
            RecipeNode node = AddNode(graph, cache, "sulfur", "uncommon");
            foreach (string fluid in new[] { "water", "petroleum-gas" })
                graph.CreateLink(graph.CreateSupplierNode(Fluid(cache, fluid), Point.Empty), node, Fluid(cache, fluid));
            var sulfur = new ItemQualityPair(cache.Items["sulfur"], cache.Qualities["uncommon"]);
            ConsumerNode consumer = graph.CreateConsumerNode(sulfur, Point.Empty);
            consumer.RateType = RateType.Manual;
            consumer.DesiredRatePerSec = 1;
            graph.CreateLink(node, consumer, sulfur);

            graph.UpdateNodeValues();

            return (node.ActualRatePerSec, [.. graph.NodeLinks.Select(l => l.ThroughputPerSec)],
                (node.WarningSet & RecipeNode.Warnings.RecipeQualityCannotBeSet) != 0);
        }

        [TestMethod]
        public async Task Node_UnsettableRecipe_SolveRatesUnchanged() {
            DataCache locked = await SpaceAge21().ConfigureAwait(false);
            var settable = new DataCache(filterRecipes: true);
            await settable.LoadAllData(new Preset(PresetDataCacheFixture.SpaceAge21, true, true), NullProgress.Instance, loadIcons: false).ConfigureAwait(false);
            ((RecipePrototype)settable.Recipes["sulfur"]).CanSetQuality = true;

            (double lockedRate, double[] lockedLinks, bool lockedWarned) = SolveSulfur(locked);
            (double settableRate, double[] settableLinks, bool settableWarned) = SolveSulfur(settable);

            Assert.IsTrue(lockedWarned);
            Assert.IsFalse(settableWarned);
            Assert.IsGreaterThan(0, lockedRate);
            Assert.AreEqual(settableRate, lockedRate, 0);
            CollectionAssert.AreEqual(settableLinks, lockedLinks);
        }

        [TestMethod]
        public async Task Node_UnsettableRecipeAboveLowest_WarningTextVerbatim() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            RecipeNode node = AddNode(NewGraph(cache), cache, "sulfur", "uncommon");
            node.UpdateState();
            List<string> warnings = node.GetWarnings();

            Assert.AreEqual(1, warnings.Count(w => w == Lock));
            int at = warnings.IndexOf(Lock);
            for (int i = 0; i < warnings.Count; i++) {
                if (warnings[i].Contains("recipe is", StringComparison.Ordinal))
                    Assert.IsLessThan(at, i);
                if (warnings[i].Contains("assembler", StringComparison.Ordinal))
                    Assert.IsGreaterThan(at, i);
            }
        }

        [TestMethod]
        public async Task Node_UnsettableRecipe_LowestFollowsEnabledSet() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset(PresetDataCacheFixture.SpaceAge21, true, true), NullProgress.Instance, loadIcons: false).ConfigureAwait(false);
            ((QualityPrototype)cache.Qualities["normal"]).Enabled = false;
            var graph = new ProductionGraph { DefaultAssemblerQuality = cache.Qualities["uncommon"] };

            RecipeNode atUncommon = AddNode(graph, cache, "sulfur", "uncommon");
            atUncommon.UpdateState();
            Assert.DoesNotContain(w => w.Contains("can only be crafted", StringComparison.Ordinal), atUncommon.GetWarnings());

            RecipeNode atRare = AddNode(graph, cache, "sulfur", "rare");
            atRare.UpdateState();
            Assert.Contains("> Selected recipe can only be crafted at Uncommon quality.", atRare.GetWarnings());
        }

        [TestMethod]
        public async Task Resolutions_UnsettableAboveLowest_SwitchIsLast() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            RecipeNode node = AddNode(NewGraph(cache), cache, "sulfur", "uncommon");
            node.UpdateState();
            List<string> keys = [.. Controller(node).GetWarningResolutions().Keys];

            Assert.Contains("Switch to Normal quality", keys);
            Assert.AreEqual("Switch to Normal quality", keys[^1]);
        }

        [TestMethod]
        public async Task Resolutions_SettableRecipe_HasNoSwitch() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            RecipeNode node = AddNode(NewGraph(cache), cache, "iron-gear-wheel", "uncommon");
            node.UpdateState();

            Assert.DoesNotContain("Switch to Normal quality", Controller(node).GetWarningResolutions().Keys);
        }

        [TestMethod]
        public async Task Resolutions_UnsettableAtLowest_HasNoSwitch() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            RecipeNode node = AddNode(NewGraph(cache), cache, "sulfur", "normal");
            node.UpdateState();

            Assert.DoesNotContain("Switch to Normal quality", Controller(node).GetWarningResolutions().Keys);
        }

        [TestMethod]
        public async Task Switch_FluidOnlyRecipe_KeepsEveryLinkAndSetting() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            ProductionGraph graph = NewGraph(cache);
            RecipeNode node = AddNode(graph, cache, "advanced-oil-processing", "uncommon");
            RecipeNodeController controller = Controller(node);

            IModule module = cache.Modules["speed-module"];
            var moduleQuality = new ModuleQualityPair(module, cache.Qualities["rare"]);
            node.AssemblerModulesAdd(moduleQuality);
            node.AssemblerModulesAdd(moduleQuality);
            node.SelectedBeacon = new BeaconQualityPair(cache.Beacons["beacon"], cache.Qualities["uncommon"]);
            node.BeaconModulesAdd(moduleQuality);
            node.BeaconCount = 4;
            node.BeaconsPerAssembler = 2;
            node.BeaconsConst = 1;
            node.NeighbourCount = 2;
            node.ExtraProductivityBonus = 0.25;
            node.LowPriority = true;
            node.RateType = RateType.Manual;
            node.DesiredSetValue = 3;
            node.KeyNode = true;
            node.KeyNodeTitle = "Refinery";
            node.NodeDirection = NodeDirection.Down;
            node.Location = new Point(40, 80);
            AssemblerQualityPair assembler = node.SelectedAssembler;

            (string Item, LinkRouting Routing)[] inputs = [("water", LinkRouting.Orthogonal), ("crude-oil", LinkRouting.Rounded)];
            (string Item, LinkRouting Routing)[] outputs = [("heavy-oil", LinkRouting.Rounded), ("light-oil", LinkRouting.Orthogonal), ("petroleum-gas", LinkRouting.Rounded)];
            foreach ((string item, LinkRouting routing) in inputs)
                graph.CreateLink(graph.CreateSupplierNode(Fluid(cache, item), Point.Empty), node, Fluid(cache, item), routing);
            Point[] waypoints = [new Point(10, 20), new Point(30, 40)];
            node.InputLinks.Single(l => l.Item == Fluid(cache, "water")).Waypoints = waypoints;
            foreach ((string item, LinkRouting routing) in outputs)
                graph.CreateLink(node, graph.CreateConsumerNode(Fluid(cache, item), Point.Empty), Fluid(cache, item), routing);

            NodeReplacement replacement = controller.SwitchToLowestQuality();
            RecipeNode copy = replacement.New;

            Assert.AreSame(cache.DefaultQuality, copy.BaseRecipe.Quality);
            Assert.AreSame(node.BaseRecipe.Recipe, copy.BaseRecipe.Recipe);
            Assert.AreEqual(0, replacement.RemovedLinkCount);
            Assert.DoesNotContain(node, graph.Nodes);
            Assert.Contains(copy, graph.Nodes);
            Assert.AreEqual(5, copy.InputLinks.Count + copy.OutputLinks.Count);
            foreach ((string item, LinkRouting routing) in inputs)
                Assert.AreEqual(routing, copy.InputLinks.Single(l => l.Item == Fluid(cache, item)).Routing);
            foreach ((string item, LinkRouting routing) in outputs)
                Assert.AreEqual(routing, copy.OutputLinks.Single(l => l.Item == Fluid(cache, item)).Routing);

            CollectionAssert.AreEqual(waypoints, copy.InputLinks.Single(l => l.Item == Fluid(cache, "water")).Waypoints.ToList());

            Assert.AreEqual(assembler, copy.SelectedAssembler);
            CollectionAssert.AreEqual(node.AssemblerModules.ToList(), copy.AssemblerModules.ToList());
            CollectionAssert.AreEqual(node.BeaconModules.ToList(), copy.BeaconModules.ToList());
            Assert.AreEqual(node.SelectedBeacon, copy.SelectedBeacon);
            Assert.AreEqual(node.Fuel, copy.Fuel);
            Assert.AreEqual(4, copy.BeaconCount);
            Assert.AreEqual(2, copy.BeaconsPerAssembler);
            Assert.AreEqual(1, copy.BeaconsConst);
            Assert.AreEqual(2, copy.NeighbourCount);
            Assert.AreEqual(0.25, copy.ExtraProductivityBonus);
            Assert.IsTrue(copy.LowPriority);
            Assert.AreEqual(RateType.Manual, copy.RateType);
            Assert.AreEqual(3, copy.DesiredSetValue);
            Assert.IsTrue(copy.KeyNode);
            Assert.AreEqual("Refinery", copy.KeyNodeTitle);
            Assert.AreEqual(NodeDirection.Down, copy.NodeDirection);
            Assert.AreEqual(new Point(40, 80), copy.Location);

            Assert.DoesNotContain(w => w.Contains("can only be crafted", StringComparison.Ordinal), copy.GetWarnings());
        }

        [TestMethod]
        public async Task Switch_ItemProduct_DropsLinksOfChangedItems() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            ProductionGraph graph = NewGraph(cache);
            RecipeNode node = AddNode(graph, cache, "sulfur", "uncommon");

            var sulfur = new ItemQualityPair(cache.Items["sulfur"], cache.Qualities["uncommon"]);
            graph.CreateLink(node, graph.CreateConsumerNode(sulfur, Point.Empty), sulfur);
            foreach (string fluid in new[] { "water", "petroleum-gas" })
                graph.CreateLink(graph.CreateSupplierNode(Fluid(cache, fluid), Point.Empty), node, Fluid(cache, fluid));

            NodeReplacement replacement = Controller(node).SwitchToLowestQuality();

            Assert.AreEqual(1, replacement.RemovedLinkCount);
            Assert.IsEmpty(replacement.New.OutputLinks);
            Assert.HasCount(2, replacement.New.InputLinks);
            Assert.AreEqual(2, graph.NodeLinks.Count());
        }

        [TestMethod]
        public async Task Switch_RaisesNodeReplacedOnceWithBothNodes() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            ProductionGraph graph = NewGraph(cache);
            RecipeNode node = AddNode(graph, cache, "sulfur", "uncommon");
            var raised = new List<NodeReplacedEventArgs>();
            graph.NodeReplaced += (_, e) => raised.Add(e);

            NodeReplacement replacement = Controller(node).SwitchToLowestQuality();

            Assert.HasCount(1, raised);
            Assert.AreSame(node, raised[0].Old);
            Assert.AreSame(replacement.New, raised[0].New);
            Assert.AreEqual(0, raised[0].RemovedLinkCount);
        }

        [TestMethod]
        public async Task Switch_SessionReportsBothNodeIdsAndTheRemovedLinkCount() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            ProductionGraph graph = NewGraph(cache);
            ProductionGraphSession session = GraphSessionTestHelper.AttachSession(graph);
            RecipeNode node = AddNode(graph, cache, "sulfur", "uncommon");
            var sulfur = new ItemQualityPair(cache.Items["sulfur"], cache.Qualities["uncommon"]);
            graph.CreateLink(node, graph.CreateConsumerNode(sulfur, Point.Empty), sulfur);
            var raised = new List<NodeViewModelReplacedEventArgs>();
            session.NodeViewModelReplaced += (_, e) => raised.Add(e);

            NodeReplacement replacement = Controller(node).SwitchToLowestQuality();

            Assert.HasCount(1, raised);
            Assert.AreEqual(node.NodeID, raised[0].Old.Value);
            Assert.AreEqual(replacement.New.NodeID, raised[0].New.Value);
            Assert.AreEqual(1, raised[0].RemovedLinkCount);
            Assert.IsTrue(session.View.TryGetNode(raised[0].New, out _));
            Assert.IsFalse(session.View.TryGetNode(raised[0].Old, out _));
        }

        [TestMethod]
        public async Task PlacementQuality_Unsettable_ReturnsLowest() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            IQuality placed = RecipeQualityLock.PlacementQuality(cache.Recipes["sulfur"], cache.Qualities["rare"], cache);

            Assert.AreSame(cache.Qualities["normal"], placed);
        }

        [TestMethod]
        public async Task PlacementQuality_Settable_ReturnsRequested() {
            DataCache cache = await SpaceAge21().ConfigureAwait(false);
            IQuality placed = RecipeQualityLock.PlacementQuality(cache.Recipes["iron-gear-wheel"], cache.Qualities["rare"], cache);

            Assert.AreSame(cache.Qualities["rare"], placed);
        }
    }
}
