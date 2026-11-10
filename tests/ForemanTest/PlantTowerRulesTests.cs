using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Models;
using Foreman.Models.Nodes;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class PlantTowerRulesTests : ForemanTestBase {
        private static readonly EffectRange Wide = new(-1000, 1000);
        private static readonly EffectLimits WideLimits = new(Wide, Wide, Wide, Wide, new EffectRange(0, 1000));

        private sealed record Rig(GraphSessionTestHelper.TestContext Context, AssemblerPrototype Tower, ModulePrototype Productivity, ModulePrototype Quality, ModulePrototype Speed);

        private static Rig BuildRig(int slots = 4) {
            GraphSessionTestHelper.TestContext ctx = GraphSessionTestHelper.CreateContext();
            AssemblerPrototype tower = TestPrototypeFactory.CreateTestTower(ctx.Cache, slots, WideLimits);
            ModulePrototype productivity = TestPrototypeFactory.CreateTestModule(ctx.Cache, "§§test:productivity", "Productivity module", new ModuleEffects(0, 0, 0.1, 0, 0));
            ModulePrototype quality = TestPrototypeFactory.CreateTestModule(ctx.Cache, "§§test:quality", "Quality module", new ModuleEffects(0, 0, 0, 0, 0.1));
            ModulePrototype speed = TestPrototypeFactory.CreateTestModule(ctx.Cache, "§§test:speed", "Speed module", new ModuleEffects(0, 0.2, 0, 0, 0));
            foreach (ModulePrototype module in new[] { productivity, quality, speed }) {
                tower.ModulesInternal.Add(module);
                TestDataCacheHelper.RegisterModule(ctx.Cache, module);
            }
            TestDataCacheHelper.RegisterAssembler(ctx.Cache, tower);
            return new Rig(ctx, tower, productivity, quality, speed);
        }

        private static (PlantNode Node, PlantNodeController Controller) BuildNode(Rig rig) {
            PlantProcessPrototype process = GraphSessionTestHelper.CreatePlantProcess(rig.Context, "seed", "crop");
            TestDataCacheHelper.RequireStore(rig.Context.Cache).PlantProcesses[process.Name] = process;
            PlantNode node = rig.Context.NewGraph().CreatePlantNode(process, rig.Context.Quality, Point.Empty);
            var controller = (PlantNodeController)node.Controller;
            controller.SetTower(new AssemblerQualityPair(rig.Tower, rig.Context.Quality));
            return (node, controller);
        }

        [TestMethod]
        public async Task IsChoosableTower_VanillaTowerWithoutSlots_IsFalse() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            IAssembler tower = cache.Assemblers["agricultural-tower"];

            Assert.AreEqual(EntityType.AgriculturalTower, tower.EntityType);
            Assert.IsFalse(PlantTowerRules.IsChoosableTower(tower));
            Assert.AreEqual(0, PlantTowerRules.TowerOptions(cache).Count);
        }

        [TestMethod]
        public void IsChoosableTower_SlottedTower_IsTrue() {
            Rig rig = BuildRig();

            Assert.IsTrue(PlantTowerRules.IsChoosableTower(rig.Tower));
            Assert.IsFalse(PlantTowerRules.IsChoosableTower(TestPrototypeFactory.CreateTestAssembler(rig.Context.Cache)));
            Assert.IsFalse(PlantTowerRules.IsChoosableTower(TestPrototypeFactory.CreateTestTower(rig.Context.Cache, 0, WideLimits)));
        }

        [TestMethod]
        public void TowerOptions_DisabledTower_IsLeftOut() {
            Rig rig = BuildRig();
            Assert.AreEqual(1, PlantTowerRules.TowerOptions(rig.Context.Cache).Count);

            rig.Tower.Enabled = false;

            Assert.AreEqual(0, PlantTowerRules.TowerOptions(rig.Context.Cache).Count);
        }

        [TestMethod]
        public void TowerOptions_AreOrderedByName() {
            Rig rig = BuildRig();
            var second = new AssemblerPrototype(rig.Context.Cache, "§§test:aardvark", "Aardvark tower", EntityType.AgriculturalTower, EnergySource.Electric, false) { ModuleSlots = 2 };
            TestDataCacheHelper.RegisterAssembler(rig.Context.Cache, second);

            CollectionAssert.AreEqual(new[] { "Aardvark tower", "Test Tower" }, PlantTowerRules.TowerOptions(rig.Context.Cache).Select(t => t.FriendlyName).ToArray());
        }

        [TestMethod]
        public void ModuleOptions_LeavesOutModulesWithoutProductivity() {
            Rig rig = BuildRig();

            IReadOnlyList<IModule> options = PlantTowerRules.ModuleOptions(rig.Tower, rig.Context.Quality);

            CollectionAssert.AreEqual(new[] { rig.Productivity }, options.ToArray());
        }

        [TestMethod]
        public void ModuleOptions_DisabledModule_IsLeftOut() {
            Rig rig = BuildRig();
            rig.Productivity.Enabled = false;

            Assert.AreEqual(0, PlantTowerRules.ModuleOptions(rig.Tower, rig.Context.Quality).Count);
        }

        [TestMethod]
        public void HasHiddenModules_TowerAcceptsOtherModules_IsTrue() {
            Rig rig = BuildRig();

            Assert.IsTrue(PlantTowerRules.HasHiddenModules(rig.Tower, rig.Context.Quality));

            rig.Quality.Enabled = false;
            rig.Speed.Enabled = false;

            Assert.IsFalse(PlantTowerRules.HasHiddenModules(rig.Tower, rig.Context.Quality));
        }

        [TestMethod]
        public void RemoveTowerModule_RemovesThatIndex() {
            Rig rig = BuildRig();
            (PlantNode node, PlantNodeController controller) = BuildNode(rig);
            var normal = rig.Context.Quality;
            controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Speed, normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Quality, normal));

            controller.RemoveTowerModule(1);

            CollectionAssert.AreEqual(new IModule[] { rig.Productivity, rig.Quality }, node.TowerModules.Select(m => m.Module).ToArray());
        }

        [TestMethod]
        public void RemoveTowerModule_IndexOutOfRange_ChangesNothing() {
            Rig rig = BuildRig();
            (PlantNode node, PlantNodeController controller) = BuildNode(rig);
            controller.AddTowerModule(new ModuleQualityPair(rig.Productivity, rig.Context.Quality));

            controller.RemoveTowerModule(3);
            controller.RemoveTowerModule(-1);

            Assert.AreEqual(1, node.TowerModules.Count);
        }

        [TestMethod]
        public void ReplaceTowerModule_KeepsOtherSlots() {
            Rig rig = BuildRig();
            (PlantNode node, PlantNodeController controller) = BuildNode(rig);
            var normal = rig.Context.Quality;
            controller.AddTowerModule(new ModuleQualityPair(rig.Speed, normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Speed, normal));
            controller.AddTowerModule(new ModuleQualityPair(rig.Quality, normal));

            controller.ReplaceTowerModule(1, new ModuleQualityPair(rig.Productivity, normal));

            CollectionAssert.AreEqual(new IModule[] { rig.Speed, rig.Productivity, rig.Quality }, node.TowerModules.Select(m => m.Module).ToArray());
        }

        [TestMethod]
        public void ReplaceTowerModule_IndexOutOfRange_ChangesNothing() {
            Rig rig = BuildRig();
            (PlantNode node, PlantNodeController controller) = BuildNode(rig);
            controller.AddTowerModule(new ModuleQualityPair(rig.Speed, rig.Context.Quality));

            controller.ReplaceTowerModule(2, new ModuleQualityPair(rig.Productivity, rig.Context.Quality));

            Assert.AreEqual(rig.Speed, node.TowerModules[0].Module);
        }
    }
}
