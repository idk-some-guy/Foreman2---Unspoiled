using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.ViewModels {
    //GraphSummaryWindow.axaml.cs's row-assembly moved into GraphSummaryViewModel. Fixture helpers
    //below are the same solved-graph recipe used by Views/GraphSummaryWindowTests.cs (RateType.Manual +
    //SetDesiredSetValue pins an isolated node's rate exactly - see that file's header comment for why every
    //count/beacon expectation below is exact and hand-checkable).
    public class GraphSummaryViewModelTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string AssemblerRecipeName = "iron-gear-wheel";
        private const string MinerRecipeName = "§§r:e:iron-ore";

        private static readonly SemaphoreSlim CacheGate = new(1, 1);
        private static DataCache? sharedCache;

        private static async Task<DataCache> GetCacheAsync() {
            if (sharedCache is not null)
                return sharedCache;
            await CacheGate.WaitAsync();
            try {
                if (sharedCache is null) {
                    var cache = new DataCache(filterRecipes: true);
                    await cache.LoadAllData(new Preset(VanillaPresetName, true, true), new Progress<KeyValuePair<int, string>>());
                    sharedCache = cache;
                }
            } finally {
                CacheGate.Release();
            }
            return sharedCache;
        }

        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
        }

        private static Fixture NewFixture(DataCache cache) {
            var graph = new ProductionGraph { DefaultAssemblerQuality = cache.DefaultQuality };
            var session = new ProductionGraphSession(graph);
            session.Attach();
            return new Fixture { Cache = cache, Graph = graph, Session = session };
        }

        private static IRecipeNodeViewModel CreateRecipeNode(Fixture fx, string recipeName) {
            IRecipe recipe = fx.Cache.Recipes[recipeName];
            fx.Graph.CreateRecipeNode(new RecipeQualityPair(recipe, fx.Cache.DefaultQuality!), Point.Empty);
            return fx.Session.View.Nodes.OfType<IRecipeNodeViewModel>().Last();
        }

        private static RecipeNodeController RecipeControllerFor(Fixture fx, IRecipeNodeViewModel vm) {
            fx.Session.TryGetDomainNode(vm.Id, out BaseNode? node);
            return (RecipeNodeController)fx.Graph.RequestNodeController(node!)!;
        }

        private static void PinFactories(Fixture fx, IRecipeNodeViewModel vm, double factories) {
            RecipeNodeController controller = RecipeControllerFor(fx, vm);
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(factories);
        }

        private static void SetBeaconConst(Fixture fx, IRecipeNodeViewModel vm, double constCount) {
            RecipeNodeController controller = RecipeControllerFor(fx, vm);
            controller.SetBeacon(new BeaconQualityPair(fx.Cache.Beacons["beacon"], fx.Cache.DefaultQuality!));
            controller.SetBeaconsCont(constCount);
        }

        [AvaloniaFact]
        public async Task Refresh_PopulatesAllSevenRowLists_WithRowCountsAndKnownCellValues() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 2);
            SetBeaconConst(fx, assemblerNode, 4);
            RecipeControllerFor(fx, assemblerNode).SetKeyNode(true);
            IRecipeNodeViewModel minerNode = CreateRecipeNode(fx, MinerRecipeName);
            PinFactories(fx, minerNode, 1);
            fx.Graph.UpdateNodeValues();

            IBeacon beacon = fx.Cache.Beacons["beacon"];
            IQuality quality = fx.Cache.DefaultQuality!;
            double expectedBeaconPower = 4 * (beacon.GetEnergyConsumption(quality) + beacon.GetEnergyDrain());
            ItemQualityPair gearPair = new(fx.Cache.Items["iron-gear-wheel"], quality);
            double expectedGearSupply = assemblerNode.GetSupplyRate(gearPair);

            var vm = new GraphSummaryViewModel();

            vm.Refresh(fx.Session.View.Nodes);

            Assert.Equal("2", Assert.Single(vm.Assemblers).CountText);
            Assert.Equal("1", Assert.Single(vm.Miners).CountText);
            Assert.Empty(vm.PowerBuildings);

            BeaconRow beaconRow = Assert.Single(vm.Beacons);
            Assert.Equal("4", beaconRow.CountText);
            Assert.Equal(expectedBeaconPower, beaconRow.PowerBValue, precision: 6);

            //Assembler contributes iron-plate (unlinked input) and iron-gear-wheel (unlinked, overproduced
            //output); the miner contributes iron-ore (unlinked, overproduced output) - three item rows, no
            //fluids.
            Assert.Equal(3, vm.Items.Count);
            Assert.Empty(vm.Fluids);
            ItemRow gearRow = vm.Items.Single(r => r.Name.Contains("Gear", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expectedGearSupply, gearRow.OutULValue, precision: 6);

            KeyNodeRow keyNodeRow = Assert.Single(vm.KeyNodes);
            Assert.Equal("0", keyNodeRow.ThroughputText);
            Assert.Equal(2, keyNodeRow.FactoriesValue, precision: 6);
        }
    }
}
