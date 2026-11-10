using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Serialization;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class PlantTowerTests : ForemanTestBase {
        private static readonly EffectRange Wide = new(-1000, 1000);
        private static readonly EffectLimits WideLimits = new(Wide, Wide, Wide, Wide, new EffectRange(0, 1000));

        private sealed record Rig(GraphSessionTestHelper.TestContext Context, ProductionGraph Graph, PlantNode Node, PlantProcessPrototype Process, IItem Crop, AssemblerPrototype Tower, ModulePrototype Module);

        private static Rig BuildRig(ModuleEffects effects, EffectLimits? limits = null, int slots = 4, bool withTower = true) {
            GraphSessionTestHelper.TestContext ctx = GraphSessionTestHelper.CreateContext();
            PlantProcessPrototype process = GraphSessionTestHelper.CreatePlantProcess(ctx, "seed", "crop");
            IItem crop = process.ProductList[0];
            process.ProductSetInternal[crop] = 50;
            TestDataCacheHelper.RequireStore(ctx.Cache).PlantProcesses[process.Name] = process;
            AssemblerPrototype tower = TestPrototypeFactory.CreateTestTower(ctx.Cache, slots, limits ?? WideLimits);
            ModulePrototype module = TestPrototypeFactory.CreateTestModule(ctx.Cache, effects);
            tower.ModulesInternal.Add(module);
            TestDataCacheHelper.RegisterAssembler(ctx.Cache, tower);
            TestDataCacheHelper.RegisterModule(ctx.Cache, module);
            ProductionGraph graph = ctx.NewGraph();
            PlantNode node = graph.CreatePlantNode(process, ctx.Quality, Point.Empty);
            if (withTower)
                Controller(node).SetTower(new AssemblerQualityPair(tower, ctx.Quality));
            return new Rig(ctx, graph, node, process, crop, tower, module);
        }

        private static PlantNodeController Controller(PlantNode node) => (PlantNodeController)node.Controller;

        private static void AddModules(Rig rig, int count) {
            for (int i = 0; i < count; i++)
                Controller(rig.Node).AddTowerModule(new ModuleQualityPair(rig.Module, rig.Context.Quality));
        }

        private static double Yield(Rig rig) => rig.Node.outputRateFor(new ItemQualityPair(rig.Crop, rig.Context.Quality));

        private static ModuleEffects Productivity(double value) => new(0, 0, value, 0, 0);

        [TestMethod]
        public async Task Node_PlantWithoutTower_SolvesAsToday() {
            DataCache cache = await SpaceAgeDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            IPlantProcess process = cache.Items["yumako-seed"].PlantResult!;
            ProductionGraph graph = new() { DefaultAssemblerQuality = cache.DefaultQuality };
            PlantNode node = graph.CreatePlantNode(process, cache.DefaultQuality!, Point.Empty);

            Assert.IsNull(node.Tower);
            Assert.IsEmpty(node.TowerModules);
            foreach (IItem product in process.ProductList)
                Assert.AreEqual(process.ProductSet[product], node.outputRateFor(new ItemQualityPair(product, cache.DefaultQuality!)), 0);
        }

        [TestMethod]
        public void Node_PlantWithTowerProductivity_ScalesYield() {
            Rig rig = BuildRig(Productivity(0.1));
            AddModules(rig, 4);

            Assert.AreEqual(70, Yield(rig), 1e-9);
        }

        [TestMethod]
        public void Node_PlantTowerProductivity_IsCappedByTowerLimits() {
            Rig rig = BuildRig(Productivity(0.1), new EffectLimits(Wide, Wide, new EffectRange(0, 0.25), Wide, new EffectRange(0, 1000)));
            AddModules(rig, 4);

            Assert.AreEqual(62.5, Yield(rig), 1e-9);
        }

        [TestMethod]
        public void Node_PlantTowerNegativeProductivity_NeverShrinksYield() {
            Rig rig = BuildRig(Productivity(-0.1));
            AddModules(rig, 4);

            Assert.AreEqual(50, Yield(rig), 0);
        }

        [TestMethod]
        public void Node_PlantWithTowerQualityModule_HarvestStaysAtPlantQuality() {
            Rig rig = BuildRig(new ModuleEffects(0, 0, 0, 0, 1));
            AddModules(rig, 4);
            rig.Node.UpdateState();

            Assert.AreEqual(50, Yield(rig), 0);
            ItemQualityPair only = Assert.ContainsSingle(rig.Node.Outputs);
            Assert.AreSame(rig.Context.Quality, only.Quality);
            Assert.AreEqual(PlantNode.Errors.Clean, rig.Node.ErrorSet);
        }

        [TestMethod]
        public void Node_PlantTowerModulesOverSlots_IsError() {
            Rig rig = BuildRig(Productivity(0.1), slots: 2);
            AddModules(rig, 2);
            rig.Node.UpdateState();
            Assert.AreEqual(PlantNode.Errors.Clean, rig.Node.ErrorSet);

            AddModules(rig, 1);
            rig.Node.UpdateState();
            Assert.AreEqual(PlantNode.Errors.TowerModuleLimitExceeded, rig.Node.ErrorSet & PlantNode.Errors.TowerModuleLimitExceeded);
            Assert.AreEqual(NodeState.Error, rig.Node.State);
        }

        [TestMethod]
        public void Node_PlantTowerAtRare_GetsNoSlotBonus() {
            Rig rig = BuildRig(Productivity(0.1), slots: 2);
            QualityPrototype[] chain = TestPrototypeFactory.CreateQualityChain(rig.Context.Cache, (0.1, 0.1), (0.1, 0.1), (0, 0));
            Controller(rig.Node).SetTower(new AssemblerQualityPair(rig.Tower, chain[2]));
            AddModules(rig, 2);
            rig.Node.UpdateState();
            Assert.AreEqual(PlantNode.Errors.Clean, rig.Node.ErrorSet & PlantNode.Errors.TowerModuleLimitExceeded);

            AddModules(rig, 1);
            rig.Node.UpdateState();
            Assert.AreEqual(PlantNode.Errors.TowerModuleLimitExceeded, rig.Node.ErrorSet & PlantNode.Errors.TowerModuleLimitExceeded);
        }

        [TestMethod]
        public void Node_PlantSwappingToSmallerTower_TrimsModulesToItsSlots() {
            Rig rig = BuildRig(Productivity(0.1));
            AddModules(rig, 4);
            AssemblerPrototype smaller = TestPrototypeFactory.CreateTestTower(rig.Context.Cache, 2, WideLimits);
            smaller.ModulesInternal.Add(rig.Module);

            Controller(rig.Node).SetTower(new AssemblerQualityPair(smaller, rig.Context.Quality));

            Assert.HasCount(2, rig.Node.TowerModules);
            Assert.AreEqual(PlantNode.Errors.Clean, rig.Node.ErrorSet);
        }

        [TestMethod]
        public void Node_PlantSwappingToTowerWithoutTheModule_DropsIt() {
            Rig rig = BuildRig(Productivity(0.1));
            AddModules(rig, 2);
            AssemblerPrototype other = TestPrototypeFactory.CreateTestTower(rig.Context.Cache, 4, WideLimits);

            Controller(rig.Node).SetTower(new AssemblerQualityPair(other, rig.Context.Quality));

            Assert.IsEmpty(rig.Node.TowerModules);
        }

        [TestMethod]
        public void Node_PlantTowerModuleErrors_OfferFixingTheModules() {
            Rig rig = BuildRig(Productivity(0.1), slots: 2);
            AddModules(rig, 3);

            Dictionary<string, Action> resolutions = rig.Node.Controller.GetErrorResolutions();
            Assert.IsFalse(resolutions.ContainsKey("Remove tower"));
            resolutions["Fix tower modules"]();

            Assert.HasCount(2, rig.Node.TowerModules);
            Assert.AreEqual(PlantNode.Errors.Clean, rig.Node.ErrorSet);
        }

        [TestMethod]
        public void Node_PlantTowerBaseProductivity_AppliesWithoutModules() {
            Rig rig = BuildRig(Productivity(0.1));
            rig.Tower.BaseProductivityBonus = 0.5;

            Assert.AreEqual(75, Yield(rig), 1e-9);
        }

        [TestMethod]
        public void Node_PlantTowerBaseProductivity_IsCappedByTowerLimits() {
            Rig rig = BuildRig(Productivity(0.1), new EffectLimits(Wide, Wide, new EffectRange(0, 0.6), Wide, new EffectRange(0, 1000)));
            rig.Tower.BaseProductivityBonus = 0.5;
            AddModules(rig, 4);

            Assert.AreEqual(80, Yield(rig), 1e-9);
        }

        [TestMethod]
        public void Node_PlantTowerErrors_UseTheRecipeNodeWording() {
            Rig rig = BuildRig(Productivity(0.1), slots: 1);
            AddModules(rig, 2);

            CollectionAssert.Contains(rig.Node.GetErrors(), "> Tower has too many modules (2/1)!");
        }

        [TestMethod]
        public void Node_PlantRemovingTower_ClearsItsModules() {
            Rig rig = BuildRig(Productivity(0.1));
            AddModules(rig, 4);

            Controller(rig.Node).SetTower(null);

            Assert.IsNull(rig.Node.Tower);
            Assert.IsEmpty(rig.Node.TowerModules);
            Assert.AreEqual(50, Yield(rig), 0);
        }

        [TestMethod]
        public void Node_PlantMissingTower_OffersRemovingTheTower() {
            Rig rig = BuildRig(Productivity(0.1), withTower: false);
            var missing = new AssemblerPrototype(rig.Context.Cache, "gone-tower", "gone-tower", EntityType.AgriculturalTower, EnergySource.Electric, true);
            Controller(rig.Node).SetTower(new AssemblerQualityPair(missing, rig.Context.Quality));

            Dictionary<string, Action> resolutions = rig.Node.Controller.GetErrorResolutions();
            resolutions["Remove tower"]();

            Assert.IsNull(rig.Node.Tower);
            Assert.AreEqual(PlantNode.Errors.Clean, rig.Node.ErrorSet);
        }

        [TestMethod]
        public void GraphSave_PlantTower_RoundTripsWithoutFormatBump() {
            Rig rig = BuildRig(Productivity(0.1), withTower: false);
            var rare = new QualityPrototype(rig.Context.Cache, "rare", "Rare", "c") { Level = 2 };
            TestDataCacheHelper.RequireStore(rig.Context.Cache).Qualities[rare.Name] = rare;
            Controller(rig.Node).SetTower(new AssemblerQualityPair(rig.Tower, rare));
            AddModules(rig, 3);

            string json = GraphSaveCodec.WriteProductionGraphToString(rig.Graph);
            Assert.AreEqual(9, (int)JsonNode.Parse(json)!["Version"]!);
            foreach (BaseNode node in rig.Graph.Nodes.ToList())
                rig.Graph.DeleteNode(node);
            var batch = rig.Graph.InsertNodesFromFragment(rig.Context.Cache, json, applySolverSettings: true);

            PlantNode loaded = (PlantNode)Assert.ContainsSingle(batch.NewNodes);
            Assert.AreEqual(new AssemblerQualityPair(rig.Tower, rare), loaded.Tower);
            Assert.HasCount(3, loaded.TowerModules);
            Assert.IsTrue(loaded.TowerModules.All(m => ReferenceEquals(m.Module, rig.Module) && m.Quality == rig.Context.Quality));
            Assert.AreEqual(PlantNode.Errors.Clean, loaded.ErrorSet);
            Assert.AreEqual(65, loaded.outputRateFor(new ItemQualityPair(rig.Crop, rig.Context.Quality)), 1e-9);
        }

        [TestMethod]
        public void GraphSave_OldPlantNode_LoadsWithNoTower() {
            Rig rig = BuildRig(Productivity(0.1), withTower: false);

            string json = GraphSaveCodec.WriteProductionGraphToString(rig.Graph);
            JsonObject written = (JsonObject)JsonNode.Parse(json)!["Nodes"]!.AsArray()[0]!;
            Assert.IsFalse(written.ContainsKey("Assembler"));
            Assert.IsFalse(written.ContainsKey("AssemblerQuality"));
            Assert.IsFalse(written.ContainsKey("AssemblerModules"));
            foreach (BaseNode node in rig.Graph.Nodes.ToList())
                rig.Graph.DeleteNode(node);
            var batch = rig.Graph.InsertNodesFromFragment(rig.Context.Cache, json, applySolverSettings: true);

            PlantNode loaded = (PlantNode)Assert.ContainsSingle(batch.NewNodes);
            Assert.IsNull(loaded.Tower);
            Assert.IsEmpty(loaded.TowerModules);
            Assert.AreEqual(PlantNode.Errors.Clean, loaded.ErrorSet);
        }

        [TestMethod]
        public void GraphSave_PlantTower_MissingTowerFlagsTowerIsMissing() {
            Rig rig = BuildRig(Productivity(0.1));
            AddModules(rig, 1);
            string json = GraphSaveCodec.WriteProductionGraphToString(rig.Graph);
            JsonNode document = JsonNode.Parse(json)!;
            Rename(document, rig.Tower.Name, "gone-tower");
            foreach (BaseNode node in rig.Graph.Nodes.ToList())
                rig.Graph.DeleteNode(node);

            var batch = rig.Graph.InsertNodesFromFragment(rig.Context.Cache, document.ToJsonString(), applySolverSettings: true);

            PlantNode loaded = (PlantNode)Assert.ContainsSingle(batch.NewNodes);
            Assert.IsNotNull(loaded.Tower);
            Assert.IsTrue(loaded.Tower!.Value.Assembler.IsMissing);
            Assert.AreEqual(PlantNode.Errors.TowerIsMissing, loaded.ErrorSet & PlantNode.Errors.TowerIsMissing);
        }

        [TestMethod]
        public void GraphSave_PlantTower_MissingModuleFlagsTowerModuleIsMissing() {
            Rig rig = BuildRig(Productivity(0.1));
            AddModules(rig, 1);
            JsonNode document = JsonNode.Parse(GraphSaveCodec.WriteProductionGraphToString(rig.Graph))!;
            Rename(document, rig.Module.Name, "gone-module");
            foreach (BaseNode node in rig.Graph.Nodes.ToList())
                rig.Graph.DeleteNode(node);

            var batch = rig.Graph.InsertNodesFromFragment(rig.Context.Cache, document.ToJsonString(), applySolverSettings: true);

            PlantNode loaded = (PlantNode)Assert.ContainsSingle(batch.NewNodes);
            Assert.HasCount(1, loaded.TowerModules);
            Assert.AreEqual(PlantNode.Errors.TowerModuleIsMissing, loaded.ErrorSet & PlantNode.Errors.TowerModuleIsMissing);
        }

        private static void Rename(JsonNode? node, string from, string to) {
            switch (node) {
                case JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).ToList()) {
                        if (obj[key] is JsonValue v && v.TryGetValue(out string? text) && text == from)
                            obj[key] = to;
                        else
                            Rename(obj[key], from, to);
                    }
                    break;
                case JsonArray array:
                    for (int i = 0; i < array.Count; i++) {
                        if (array[i] is JsonValue v && v.TryGetValue(out string? text) && text == from)
                            array[i] = to;
                        else
                            Rename(array[i], from, to);
                    }
                    break;
            }
        }
    }
}
