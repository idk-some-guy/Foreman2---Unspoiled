using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Views;
using Foreman.Models;
using Foreman.Models.Nodes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Fixture numbers throughout are pinned via RateType.Manual + SetDesiredSetValue on a node with no
    //other constraint in its connected component: ProductionSolver.AddTarget adds a hard equality
    //(nodeVar + errorVar == desiredRate) with a heavily-weighted error term, so an isolated Manual node's
    //ActualRatePerSec lands on its target exactly - for a RecipeNode this makes ActualSetValue equal the
    //pinned factory count exactly too (RecipeNode.ActualSetValue's Time/Speed factors cancel against the
    //same factors in DesiredRatePerSec). That gives every count/beacon fixture below an exact, hand-checkable
    //expected value without needing to know a specific assembler's speed or a recipe's timing.
    public class GraphSummaryWindowTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string AssemblerRecipeName = "iron-gear-wheel";
        private const string MinerRecipeName = "§§r:e:iron-ore";
        private const string GeneratorRecipeName = "§§r:g:steam:100>NaN";

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

        private static ISupplierNodeViewModel CreateSupplierNode(Fixture fx, string itemName) {
            IItem item = fx.Cache.Items[itemName];
            fx.Graph.CreateSupplierNode(new ItemQualityPair(item, fx.Cache.DefaultQuality!), Point.Empty);
            return fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Last();
        }

        private static IConsumerNodeViewModel CreateConsumerNode(Fixture fx, string itemName) {
            IItem item = fx.Cache.Items[itemName];
            fx.Graph.CreateConsumerNode(new ItemQualityPair(item, fx.Cache.DefaultQuality!), Point.Empty);
            return fx.Session.View.Nodes.OfType<IConsumerNodeViewModel>().Last();
        }

        private static RecipeNodeController RecipeControllerFor(Fixture fx, IRecipeNodeViewModel vm) {
            fx.Session.TryGetDomainNode(vm.Id, out BaseNode? node);
            return (RecipeNodeController)fx.Graph.RequestNodeController(node!)!;
        }

        private static BaseNodeController NodeControllerFor(Fixture fx, INodeViewModel vm) {
            fx.Session.TryGetDomainNode(vm.Id, out BaseNode? node);
            return fx.Graph.RequestNodeController(node!)!;
        }

        private static void PinFactories(Fixture fx, IRecipeNodeViewModel vm, double factories) {
            RecipeNodeController controller = RecipeControllerFor(fx, vm);
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(factories);
        }

        private static void PinRate(Fixture fx, INodeViewModel vm, double rate) {
            BaseNodeController controller = NodeControllerFor(fx, vm);
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(rate);
        }

        private static void SetBeaconConst(Fixture fx, IRecipeNodeViewModel vm, double constCount) {
            RecipeNodeController controller = RecipeControllerFor(fx, vm);
            controller.SetBeacon(new BeaconQualityPair(fx.Cache.Beacons["beacon"], fx.Cache.DefaultQuality!));
            controller.SetBeaconsCont(constCount);
        }

        private static void Link(Fixture fx, INodeViewModel supplier, INodeViewModel consumer, ItemQualityPair item) {
            fx.Session.TryGetDomainNode(supplier.Id, out BaseNode? s);
            fx.Session.TryGetDomainNode(consumer.Id, out BaseNode? c);
            fx.Graph.CreateLink(s!, c!, item);
        }

        private static GraphSummaryWindow NewWindow(Fixture fx, string rateString = "second") => new(fx.Session, rateString);

        //--- Tab shell ---------------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task MainTabControl_HasBuildingsItemsFluidsKeyNodesInOrder() {
            var fx = NewFixture(await GetCacheAsync());
            var window = NewWindow(fx);

            var headers = window.MainTabControlControl.Items.OfType<TabItem>().Select(t => t.Header?.ToString()).ToList();

            Assert.Equal(3, headers.Count);
            Assert.Equal("Buildings", headers[0]);
            Assert.StartsWith("Items/Fluids", headers[1]);
            Assert.Equal("Key Nodes", headers[2]);
        }

        [AvaloniaFact]
        public async Task ItemsFluidsTabItem_HeaderHasRateSuffix() {
            var fx = NewFixture(await GetCacheAsync());
            var window = NewWindow(fx, "minute");

            Assert.Equal("Items/Fluids ( per minute)", window.ItemsFluidsTabItemControl.Header);
        }

        //Phase5b hands-on gate (Finding 1): Building/Beacon/Item rows bind Background (White for
        //available, Pink for unavailable) but never bound Foreground, same white-on-white trap as
        //SettingsWindow's Enabled Objects rows. KeyNodeRow rows have no per-row Background at all (they
        //inherit the window's own dark background), so they're deliberately left off this check.
        [AvaloniaFact]
        public async Task SummaryRows_ForegroundIsReadableAgainstRowBackground() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 1);
            SetBeaconConst(fx, assemblerNode, 1);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);

            GraphSummaryWindow.BuildingRow buildingRow = Assert.Single(window.UnfilteredAssemblerList);
            ContrastAssert.Readable(buildingRow.Foreground, buildingRow.Background);

            GraphSummaryWindow.BeaconRow beaconRow = Assert.Single(window.UnfilteredBeaconList);
            ContrastAssert.Readable(beaconRow.Foreground, beaconRow.Background);

            GraphSummaryWindow.ItemRow itemRow = window.UnfilteredItemsList.First();
            ContrastAssert.Readable(itemRow.Foreground, itemRow.Background);
        }

        //Regression: the Fluent theme's default ListBoxItem carries generous Padding/MinHeight, leaving
        //upstream's dense WinForms rows (20px icon, minimal chrome) looking sparse and hard to scan.
        [AvaloniaFact]
        public async Task SummaryRows_AreDenseLikeUpstream() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 1);
            fx.Graph.UpdateNodeValues();
            var window = NewWindow(fx);
            window.Show();

            ListBoxItem realizedRow = window.AssemblerListViewControl.GetVisualDescendants().OfType<ListBoxItem>().First();
            Assert.True(realizedRow.Bounds.Height <= 28, $"Expected a dense row (<=28px), got {realizedRow.Bounds.Height}px.");
        }

        //--- Building/beacon header totals: two-pass independence ---------------------------------------

        [AvaloniaFact]
        public async Task BuildingCountHeader_SumsCeilingAcrossAllSubTabs_IndependentlyOfPerRowGroups() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 2.3);
            IRecipeNodeViewModel minerNode = CreateRecipeNode(fx, MinerRecipeName);
            PinFactories(fx, minerNode, 1.4);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);

            //Header: Ceiling(2.3) + Ceiling(1.4) = 3 + 2 = 5, summed over every recipe node regardless of
            //sub-tab. A merged pass that only read the active sub-tab's rows would show 3, not 5.
            Assert.Equal("#Buildings: 5", window.BuildingCountLabelControl.Text);

            //Rows: each sub-tab's own count is its own group's ceiling only, not the header total.
            Assert.Equal("3", Assert.Single(window.UnfilteredAssemblerList).CountText);
            Assert.Equal("2", Assert.Single(window.UnfilteredMinerList).CountText);
        }

        [AvaloniaFact]
        public async Task BeaconCountHeader_SumsGetTotalBeaconsAcrossAllRecipeNodes() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 1);
            SetBeaconConst(fx, assemblerNode, 5);
            IRecipeNodeViewModel minerNode = CreateRecipeNode(fx, MinerRecipeName);
            PinFactories(fx, minerNode, 1);
            SetBeaconConst(fx, minerNode, 3);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);

            Assert.Equal("#Beacons: 8", window.BeaconCountLabelControl.Text);
        }

        //--- Power header totals -------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task PowerConsumptionHeader_MatchesAssemblerAndBeaconElectricalConsumption_NetHiddenWithNoProduction() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 2);
            fx.Graph.UpdateNodeValues();
            double expectedConsumption = assemblerNode.GetTotalAssemblerElectricalConsumption() + assemblerNode.GetTotalBeaconElectricalConsumption();

            var window = NewWindow(fx);

            Assert.Equal("Power Consumption: " + GraphicsStuff.DoubleToEnergy(expectedConsumption, "W"), window.PowerConsumptionLabelControl.Text);
            Assert.Equal("Power Production: " + GraphicsStuff.DoubleToEnergy(0, "W"), window.PowerProductionLabelControl.Text);
            Assert.False(window.PowerNetLabelControl.IsVisible);
        }

        [AvaloniaFact]
        public async Task PowerNetLabel_VisibleAndCorrect_WhenBothConsumptionAndProductionNonzero() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 2);
            IRecipeNodeViewModel generatorNode = CreateRecipeNode(fx, GeneratorRecipeName);
            PinFactories(fx, generatorNode, 3);
            fx.Graph.UpdateNodeValues();
            double expectedConsumption = assemblerNode.GetTotalAssemblerElectricalConsumption();
            double expectedProduction = generatorNode.GetTotalGeneratorElectricalProduction();
            Assert.True(expectedProduction > 0, "fixture sanity: generator must actually produce power");

            var window = NewWindow(fx);

            Assert.True(window.PowerNetLabelControl.IsVisible);
            Assert.Equal("Net Power: " + GraphicsStuff.DoubleToEnergy(expectedProduction - expectedConsumption, "W"), window.PowerNetLabelControl.Text);
        }

        //--- Per-row beacon power -------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task BeaconRow_PowerEqualsCountTimesEnergyConsumptionPlusDrain() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 1);
            SetBeaconConst(fx, assemblerNode, 4);
            fx.Graph.UpdateNodeValues();

            IBeacon beacon = fx.Cache.Beacons["beacon"];
            IQuality quality = fx.Cache.DefaultQuality!;
            double expectedPower = 4 * (beacon.GetEnergyConsumption(quality) + beacon.GetEnergyDrain());

            var window = NewWindow(fx);

            GraphSummaryWindow.BeaconRow row = Assert.Single(window.UnfilteredBeaconList);
            Assert.Equal("4", row.CountText);
            Assert.Equal(expectedPower, row.PowerBValue, precision: 6);
        }

        //--- Items/Fluids: routing rules -------------------------------------------------------------------

        [AvaloniaFact]
        public async Task ItemRow_StandaloneRecipe_UnlinkedInputAndOutput_BothRouteToUnlinkedAndOutputFullyOverproduces() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 2);
            fx.Graph.UpdateNodeValues();

            ItemQualityPair inputPair = new(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!);
            ItemQualityPair outputPair = new(fx.Cache.Items["iron-gear-wheel"], fx.Cache.DefaultQuality!);
            double expectedConsume = node.GetConsumeRate(inputPair);
            double expectedSupply = node.GetSupplyRate(outputPair);
            Assert.True(expectedConsume > 0 && expectedSupply > 0, "fixture sanity: recipe must actually flow");

            var window = NewWindow(fx);

            GraphSummaryWindow.ItemRow inputRow = window.UnfilteredItemsList.Single(r => r.Name == "Iron Plate" || r.Name.Contains("Plate", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("-", inputRow.InText);
            Assert.NotEqual("-", inputRow.InULText);
            Assert.Equal("-", inputRow.ConsumedText);
            Assert.Equal(expectedConsume, inputRow.InULValue, precision: 6);

            GraphSummaryWindow.ItemRow outputRow = window.UnfilteredItemsList.Single(r => r.Name.Contains("Gear", StringComparison.OrdinalIgnoreCase));
            //Unlinked (no output link at all) AND overproduced (supplyUsedRate is 0 with no link consuming
            //it) are independent flags upstream - both fire together here, not one or the other.
            Assert.NotEqual("-", outputRow.OutULText);
            Assert.NotEqual("-", outputRow.OverprodText);
            Assert.Equal(expectedSupply, outputRow.OutULValue, precision: 6);
            Assert.Equal(expectedSupply, outputRow.OverprodValue, precision: 6);
            Assert.Equal(expectedSupply, outputRow.ProducedValue, precision: 6);
        }

        [AvaloniaFact]
        public async Task ItemRow_LinkedInput_RoutesToConsumedNotUnlinked_AndSupplierAddsToIn() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipeNode = CreateRecipeNode(fx, AssemblerRecipeName);
            ItemQualityPair inputPair = new(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!);
            ISupplierNodeViewModel supplier = CreateSupplierNode(fx, "iron-plate");
            Link(fx, supplier, recipeNode, inputPair);
            PinFactories(fx, recipeNode, 2);
            PinRate(fx, supplier, 999);
            fx.Graph.UpdateNodeValues();
            double expectedConsume = recipeNode.GetConsumeRate(inputPair);

            var window = NewWindow(fx);

            GraphSummaryWindow.ItemRow inputRow = window.UnfilteredItemsList.Single(r => r.Name.Contains("Plate", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("-", inputRow.InULText);
            Assert.NotEqual("-", inputRow.ConsumedText);
            Assert.Equal(expectedConsume, inputRow.ConsumedValue, precision: 6);
            Assert.NotEqual("-", inputRow.InText);
            Assert.Equal(supplier.ActualRate, inputRow.InValue, precision: 6);
        }

        [AvaloniaFact]
        public async Task ItemRow_LinkedOutputBelowConsumerDemand_StaysLinkedButStillOverproduces() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipeNode = CreateRecipeNode(fx, AssemblerRecipeName);
            ItemQualityPair outputPair = new(fx.Cache.Items["iron-gear-wheel"], fx.Cache.DefaultQuality!);
            IConsumerNodeViewModel consumer = CreateConsumerNode(fx, "iron-gear-wheel");
            Link(fx, recipeNode, consumer, outputPair);
            PinFactories(fx, recipeNode, 2);
            fx.Graph.UpdateNodeValues();

            double naturalSupply = recipeNode.GetSupplyRate(outputPair);
            double consumerTarget = naturalSupply / 2;
            PinRate(fx, consumer, consumerTarget);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);

            GraphSummaryWindow.ItemRow outputRow = window.UnfilteredItemsList.Single(r => r.Name.Contains("Gear", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("-", outputRow.OutULText);
            Assert.NotEqual("-", outputRow.OverprodText);
            //recipeNode.ActualSetValue stays pinned at 2 factories regardless of the consumer's own pinned
            //target, so production never throttles down to meet demand - the overflow is exactly the gap
            //between what the recipe makes and what the linked consumer actually pulls.
            Assert.Equal(naturalSupply - consumerTarget, outputRow.OverprodValue, precision: 6);
        }

        [AvaloniaFact]
        public async Task ItemsFilterCheckBoxes_AllSevenDefaultChecked() {
            var fx = NewFixture(await GetCacheAsync());
            var window = NewWindow(fx);

            Assert.True(window.InputFilterCheckBoxControl.IsChecked);
            Assert.True(window.InputUnlinkedFilterCheckBoxControl.IsChecked);
            Assert.True(window.OutputFilterCheckBoxControl.IsChecked);
            Assert.True(window.OutputUnlinkedFilterCheckBoxControl.IsChecked);
            Assert.True(window.OutputOverproducedFilterCheckBoxControl.IsChecked);
            Assert.True(window.ProductionFilterCheckBoxControl.IsChecked);
            Assert.True(window.ConsumptionFilterCheckBoxControl.IsChecked);
        }

        [AvaloniaFact]
        public async Task ItemsFilter_UncheckingAllButProduction_HidesRowsWithNoProduction() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipeNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, recipeNode, 1);
            fx.Graph.UpdateNodeValues();
            var window = NewWindow(fx);

            window.InputFilterCheckBoxControl.IsChecked = false;
            window.InputUnlinkedFilterCheckBoxControl.IsChecked = false;
            window.OutputFilterCheckBoxControl.IsChecked = false;
            window.OutputUnlinkedFilterCheckBoxControl.IsChecked = false;
            window.OutputOverproducedFilterCheckBoxControl.IsChecked = false;
            window.ConsumptionFilterCheckBoxControl.IsChecked = false;

            var visible = ((IEnumerable<GraphSummaryWindow.ItemRow>)window.ItemsListViewControl.ItemsSource!).ToList();

            Assert.All(visible, r => Assert.NotEqual("-", r.ProducedText));
            Assert.DoesNotContain(visible, r => r.Name.Contains("Plate", StringComparison.OrdinalIgnoreCase));
        }

        //--- Key Nodes -------------------------------------------------------------------------------------

        [AvaloniaFact]
        public async Task KeyNodes_OnlyKeyNodeTrueRows_AndFourTypeFiltersDefaultChecked() {
            var fx = NewFixture(await GetCacheAsync());
            ISupplierNodeViewModel keySupplier = CreateSupplierNode(fx, "iron-plate");
            ISupplierNodeViewModel plainSupplier = CreateSupplierNode(fx, "copper-plate");
            NodeControllerFor(fx, keySupplier).SetKeyNode(true);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);

            Assert.True(window.SupplierNodeFilterCheckBoxControl.IsChecked);
            Assert.True(window.ConsumerNodeFilterCheckBoxControl.IsChecked);
            Assert.True(window.PassthroughNodeFilterCheckBoxControl.IsChecked);
            Assert.True(window.RecipeNodeFilterCheckBoxControl.IsChecked);

            GraphSummaryWindow.KeyNodeRow row = Assert.Single(window.UnfilteredKeyNodesList);
            Assert.Same(keySupplier, row.Tag);
            Assert.DoesNotContain(window.UnfilteredKeyNodesList, r => ReferenceEquals(r.Tag, plainSupplier));
        }

        [AvaloniaFact]
        public async Task KeyNodesRow_RecipeNode_ThroughputDash_FactoriesActualSetValue() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipeNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, recipeNode, 3.5);
            RecipeControllerFor(fx, recipeNode).SetKeyNode(true);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);

            GraphSummaryWindow.KeyNodeRow row = Assert.Single(window.UnfilteredKeyNodesList);
            Assert.Equal("-", row.ThroughputText);
            Assert.Equal(3.5, row.FactoriesValue, precision: 6);
        }

        [AvaloniaFact]
        public async Task KeyNodesRow_SupplierNode_ThroughputActualRate_FactoriesDash() {
            var fx = NewFixture(await GetCacheAsync());
            ISupplierNodeViewModel supplier = CreateSupplierNode(fx, "iron-plate");
            NodeControllerFor(fx, supplier).SetKeyNode(true);
            PinRate(fx, supplier, 7.5);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);

            GraphSummaryWindow.KeyNodeRow row = Assert.Single(window.UnfilteredKeyNodesList);
            Assert.Equal("-", row.FactoriesText);
            Assert.Equal(7.5, row.ThroughputValue, precision: 6);
        }

        [AvaloniaFact]
        public async Task KeyNodesTitleColumn_NaturalSort_Node2BeforeNode10() {
            var fx = NewFixture(await GetCacheAsync());
            ISupplierNodeViewModel a = CreateSupplierNode(fx, "iron-plate");
            ISupplierNodeViewModel b = CreateSupplierNode(fx, "copper-plate");
            NodeControllerFor(fx, a).SetKeyNode(true);
            NodeControllerFor(fx, a).SetKeyNodeTitle("Node 10");
            NodeControllerFor(fx, b).SetKeyNode(true);
            NodeControllerFor(fx, b).SetKeyNodeTitle("Node 2");
            fx.Graph.UpdateNodeValues();
            var window = NewWindow(fx);

            window.SortKeyNodesColumn(2);

            Assert.Equal(["Node 2", "Node 10"], window.UnfilteredKeyNodesList.Select(r => r.TitleText));
        }

        [AvaloniaFact]
        public async Task BuildingColumnSort_DefaultsToNumericDescending() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel small = CreateRecipeNode(fx, MinerRecipeName);
            PinFactories(fx, small, 1);
            IRecipeNodeViewModel big = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, big, 9);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);
            var combined = new List<GraphSummaryWindow.BuildingRow>();
            combined.AddRange(window.UnfilteredAssemblerList);
            combined.AddRange(window.UnfilteredMinerList);
            var owner = window.AssemblerListViewControl;

            window.SortBuildingColumn(combined, owner, 0);

            Assert.Equal(["9", "1"], combined.Select(r => r.CountText));
        }

        //--- CSV export respects filters -------------------------------------------------------------------

        [AvaloniaFact]
        public async Task ExportBuildingsCsv_DumpsCurrentlyFilteredRows_NotFullSet() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 1);
            IRecipeNodeViewModel minerNode = CreateRecipeNode(fx, MinerRecipeName);
            PinFactories(fx, minerNode, 1);
            fx.Graph.UpdateNodeValues();
            var window = NewWindow(fx);

            //Filtering to the assembler-only friendly name hides the miner row from the Assemblers list
            //before export runs.
            window.BuildingsFilterTextBoxControl.Text = window.UnfilteredAssemblerList[0].Name;

            string? capturedPath = null;
            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
            window.SaveFilePathStub = () => {
                capturedPath = tempPath;
                return Task.FromResult<string?>(tempPath);
            };

            await window.ExportBuildingsCsvAsync().ConfigureAwait(true);

            Assert.NotNull(capturedPath);
            string csv = await File.ReadAllTextAsync(tempPath).ConfigureAwait(true);
            File.Delete(tempPath);
            Assert.Contains(window.UnfilteredAssemblerList[0].Name.Replace(" ", ""), csv.Replace(" ", ""));
            Assert.DoesNotContain(window.UnfilteredMinerList[0].Name.Replace(" ", ""), csv.Replace(" ", ""));
        }
    }

    //Ports GraphSummaryButton_Click (reference upstream MainForm.cs:564-569): MainForm always passes the
    //whole session, never a node subset - the launcher's stub seam lets us confirm that without a real
    //modal ShowDialog.
    public class GraphSummaryWindowLauncherTests {
        private static Foreman.DataCaching.DataCache MinimalCache() {
            var cache = new Foreman.DataCaching.DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            System.Reflection.FieldInfo field = typeof(Foreman.DataCaching.DataCache).GetField("_store", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var store = (Foreman.DataCaching.Loading.DataCacheStore)field.GetValue(cache)!;
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;
            return cache;
        }

        [AvaloniaFact]
        public void OpenGraphSummaryAsync_PassesWholeSessionToDialog() {
            var window = new MainWindow();
            window.Show();
            window.DataCache = MinimalCache();

            IProductionGraphSession? captured = null;
            window.GraphSummaryDialogStub = session => captured = session;

            _ = window.OpenGraphSummaryAsync();

            Assert.Same(window.GraphCanvas.Viewer.Session, captured);
        }
    }
}
