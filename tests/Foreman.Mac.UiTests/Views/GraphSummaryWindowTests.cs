using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
using Foreman.Models.Solver;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
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
using Point = System.Drawing.Point;

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

        private static GraphSummaryWindow NewWindow(Fixture fx, ProductionGraph.RateUnit? unit = null) {
            if (unit is { } selected)
                fx.Graph.SelectedRateUnit = selected;
            return GraphSummaryWindow.Create(fx.Session, new GraphSummaryHost { Unit = () => fx.Graph.SelectedRateUnit });
        }

        private static GraphSummaryWindow ShownWindow(Fixture fx, GraphSummaryHost? host = null) {
            GraphSummaryWindow window = GraphSummaryWindow.Create(fx.Session, host);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static Avalonia.Media.Color Token(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value), key);
            return Assert.IsType<Avalonia.Media.Color>(value);
        }

        private static void Layout(GraphSummaryWindow window) {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static ListBoxItem ContainerOf(GraphSummaryWindow window, Func<SummaryRow, bool> match) {
            Layout(window);
            return window.TableControl.GetVisualDescendants().OfType<ListBoxItem>().Single(c => c.Content is SummaryRow row && match(row));
        }

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
            Assert.Equal("#Buildings: 5", window.BuildingCountText);

            //Rows: each sub-tab's own count is its own group's ceiling only, not the header total.
            Assert.Equal("3", Assert.Single(window.ViewModel.Assemblers).CountText);
            Assert.Equal("2", Assert.Single(window.ViewModel.Miners).CountText);
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

            Assert.Equal("#Beacons: 8", window.BeaconCountText);
        }

        [AvaloniaFact]
        public async Task PowerConsumptionHeader_MatchesAssemblerAndBeaconElectricalConsumption_NetHiddenWithNoProduction() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assemblerNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assemblerNode, 2);
            fx.Graph.UpdateNodeValues();
            double expectedConsumption = assemblerNode.GetTotalAssemblerElectricalConsumption() + assemblerNode.GetTotalBeaconElectricalConsumption();

            var window = NewWindow(fx);

            Assert.Equal("Power Consumption: " + GraphicsStuff.DoubleToEnergy(expectedConsumption, "W"), window.PowerConsumptionText);
            Assert.Equal("Power Production: " + GraphicsStuff.DoubleToEnergy(0, "W"), window.PowerProductionText);
            Assert.Null(window.PowerNetText);
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

            Assert.Equal("Net Power: " + GraphicsStuff.DoubleToEnergy(expectedProduction - expectedConsumption, "W"), window.PowerNetText);
        }

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

            BeaconRow row = Assert.Single(window.ViewModel.Beacons);
            Assert.Equal("4", row.CountText);
            Assert.Equal(expectedPower, row.PowerBValue, precision: 6);
        }

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

            ItemRow inputRow = window.ViewModel.Items.Single(r => r.Name == "Iron Plate" || r.Name.Contains("Plate", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(SummaryTables.Zero, inputRow.InText);
            Assert.NotEqual(SummaryTables.Zero, inputRow.InULText);
            Assert.Equal(SummaryTables.Zero, inputRow.ConsumedText);
            Assert.Equal(expectedConsume, inputRow.InULValue, precision: 6);

            ItemRow outputRow = window.ViewModel.Items.Single(r => r.Name.Contains("Gear", StringComparison.OrdinalIgnoreCase));
            //Unlinked (no output link at all) AND overproduced (supplyUsedRate is 0 with no link consuming
            //it) are independent flags upstream - both fire together here, not one or the other.
            Assert.NotEqual(SummaryTables.Zero, outputRow.OutULText);
            Assert.NotEqual(SummaryTables.Zero, outputRow.OverprodText);
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

            ItemRow inputRow = window.ViewModel.Items.Single(r => r.Name.Contains("Plate", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(SummaryTables.Zero, inputRow.InULText);
            Assert.NotEqual(SummaryTables.Zero, inputRow.ConsumedText);
            Assert.Equal(expectedConsume, inputRow.ConsumedValue, precision: 6);
            Assert.NotEqual(SummaryTables.Zero, inputRow.InText);
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

            ItemRow outputRow = window.ViewModel.Items.Single(r => r.Name.Contains("Gear", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(SummaryTables.Zero, outputRow.OutULText);
            Assert.NotEqual(SummaryTables.Zero, outputRow.OverprodText);
            //recipeNode.ActualSetValue stays pinned at 2 factories regardless of the consumer's own pinned
            //target, so production never throttles down to meet demand - the overflow is exactly the gap
            //between what the recipe makes and what the linked consumer actually pulls.
            Assert.Equal(naturalSupply - consumerTarget, outputRow.OverprodValue, precision: 6);
        }

        [AvaloniaFact]
        public async Task KeyNodesRow_RecipeNode_ThroughputDash_FactoriesActualSetValue() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipeNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, recipeNode, 3.5);
            RecipeControllerFor(fx, recipeNode).SetKeyNode(true);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx);

            KeyNodeRow row = Assert.Single(window.ViewModel.KeyNodes);
            Assert.Equal(SummaryTables.Zero, row.ThroughputText);
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

            KeyNodeRow row = Assert.Single(window.ViewModel.KeyNodes);
            Assert.Equal(SummaryTables.Zero, row.FactoriesText);
            Assert.Equal(7.5, row.ThroughputValue, precision: 6);
            Assert.Equal(SummaryTables.FormatRate(row.ThroughputValue, ProductionGraph.RateUnit.Per1Sec), row.ThroughputText);
            Assert.EndsWith("/s", row.ThroughputText);
        }

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
            window.FilterBoxControl.Text = window.ViewModel.Assemblers[0].Name;

            string? capturedPath = null;
            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
            window.SaveFilePathStub = () => {
                capturedPath = tempPath;
                return Task.FromResult<string?>(tempPath);
            };

            await window.ExportCsvAsync().ConfigureAwait(true);

            Assert.NotNull(capturedPath);
            string csv = await File.ReadAllTextAsync(tempPath).ConfigureAwait(true);
            File.Delete(tempPath);
            Assert.Contains(window.ViewModel.Assemblers[0].Name.Replace(" ", ""), csv.Replace(" ", ""));
            Assert.DoesNotContain(window.ViewModel.Miners[0].Name.Replace(" ", ""), csv.Replace(" ", ""));
        }

        [AvaloniaFact]
        public async Task ItemsFilter_UncheckingAllButProduced_HidesRowsWithNoProduction_AndShowReadsKindsOfSeven() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipeNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, recipeNode, 1);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);

            window.SelectSegment(1);

            Assert.Equal("Show", window.ShowButtonControl.DisplayLabel);
            Assert.Equal("Show, 7 of 7 kinds", AutomationProperties.GetName(window.ShowButtonControl));
            Assert.Equal(["Input", "Unlinked input", "Output", "Unlinked output", "Overproduced", "Produced", "Consumed"], window.ShowButtonControl.Items.Select(i => i.Text));
            Assert.Equal("Show All", window.ShowButtonControl.ResetLabel);

            foreach (int kind in new[] { 0, 1, 2, 3, 4, 6 })
                window.SetKind(kind, false);

            Assert.Equal("Show", window.ShowButtonControl.DisplayLabel);
            Assert.Equal("Show, 1 of 7 kinds", AutomationProperties.GetName(window.ShowButtonControl));
            Assert.All(window.Shown, r => Assert.NotEqual(SummaryTables.Zero, r.Cells[6]));
            Assert.DoesNotContain(window.Shown, r => r.Cells[0].Contains("Plate", StringComparison.OrdinalIgnoreCase));
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
            window.SelectSegment(2);

            window.SortBy(2);

            Assert.Equal(["Node 2", "Node 10"], window.Rows(SummaryTableId.KeyNodes).Select(r => r.Cells[2]));
        }

        [AvaloniaFact]
        public async Task Command_OpensAWindowTitledGraphSummaryDashName_780By560_Min700By560TimesStep_Resizable_NoSheetLayer_NoScrim_SecondInvocationActivatesTheSameInstance() {
            MainWindow main = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();

            main.Commands.GraphSummaryCommand.Execute(null);

            GraphSummaryWindow summary = Assert.IsType<GraphSummaryWindow>(main.CurrentGraphSummaryWindow);
            Assert.True(summary.IsVisible);
            Assert.Equal("Graph Summary — " + main.ViewModel.DocumentTitle, summary.Title);
            Assert.Equal((780, 560), (summary.Width, summary.Height));
            Assert.Equal(700 * WindowScaler.CurrentScale, summary.MinWidth, 3);
            Assert.Equal(560 * WindowScaler.CurrentScale, summary.MinHeight, 3);
            Assert.True(summary.CanResize);
            Assert.True(double.IsPositiveInfinity(summary.MaxWidth) && double.IsPositiveInfinity(summary.MaxHeight));
            Assert.False(SheetHost.For(main).IsOpen);
            Assert.Same(main, summary.Owner);

            main.Commands.GraphSummaryCommand.Execute(null);

            Assert.Same(summary, main.CurrentGraphSummaryWindow);
            Assert.Single(main.OwnedWindows.OfType<GraphSummaryWindow>());

            summary.Close();
            Assert.Null(main.CurrentGraphSummaryWindow);
            main.Commands.GraphSummaryCommand.Execute(null);
            Assert.NotSame(summary, main.CurrentGraphSummaryWindow);
            main.CurrentGraphSummaryWindow!.Close();
        }

        [AvaloniaFact]
        public async Task TwoSegmentedControls_InOrder_NoTabControl_NoCheckBox_NoCloseOrColonLabels() {
            var window = ShownWindow(NewFixture(await GetCacheAsync()));

            Segmented[] segments = [.. window.GetVisualDescendants().OfType<Segmented>()];
            Assert.Equal(2, segments.Length);
            Assert.Same(window.TopSegmentControl, segments[0]);
            Assert.Equal(["Buildings", "Items and fluids", "Key nodes"], segments[0].Items);
            Assert.Equal(["Assemblers", "Miners", "Power", "Beacons"], segments[1].Items);
            Assert.Empty(window.GetVisualDescendants().OfType<TabControl>());
            Assert.Empty(window.GetVisualDescendants().OfType<CheckBox>());
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Close");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.EndsWith(':') == true);
            Assert.False(window.ShowButtonControl.IsVisible);

            window.SelectSegment(1);
            Assert.Equal(["Items", "Fluids"], window.SubSegmentControl.Items);
            Assert.True(window.SubSegmentControl.IsVisible && window.ShowButtonControl.IsVisible);

            window.SelectSegment(2);
            Assert.False(window.SubSegmentControl.IsVisible);
            Assert.True(window.ShowButtonControl.IsVisible);
            Assert.Equal(["Supplier nodes", "Consumer nodes", "Passthrough nodes", "Recipe nodes"], window.ShowButtonControl.Items.Select(i => i.Text));

            Assert.Equal(("Filter", 200d, 140d), (window.FilterBoxControl.PlaceholderText, window.FilterBoxControl.Width, window.FilterBoxControl.MinWidth));
            Assert.Equal("Export CSV…", window.ExportButtonControl.Content);
            Assert.NotNull(window.TableControl.Theme);

            int rebuilds = window.RebuildCount;
            window.ShowButtonControl.IsOpen = true;
            AnimationClock.Settle(300);
            window.ShowButtonControl.IsOpen = false;
            AnimationClock.Settle(300);
            Assert.Equal(rebuilds, window.RebuildCount);
            Assert.True(window.EmptyPanel.Bounds.Height > 0);
        }

        [AvaloniaFact]
        public async Task StubbedSolveChange_UpdatesProduced_WindowStaysOpen_SortAndSelectionSurvive() {
            var fx = NewFixture(await GetCacheAsync());
            foreach (IItem item in fx.Cache.Items.Values.Where(i => i.Available && i is not IFluid).Take(40))
                PinRate(fx, CreateSupplierNode(fx, item.Name), 5);
            IRecipeNodeViewModel gear = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, gear, 1);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            window.SortBy(6);
            window.SortBy(6);
            SummaryRow before = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Gear", StringComparison.OrdinalIgnoreCase));
            window.SelectRow(before.Key);
            window.ScrollTableTo(200);
            double offset = window.TableScrollOffset;
            Assert.True(offset > 0, "fixture sanity: the table must scroll");
            int rebuilds = window.RebuildCount;

            PinFactories(fx, gear, 3);
            fx.Graph.UpdateNodeValues();

            Assert.True(window.RebuildCount > rebuilds);
            SummaryRow after = window.Rows(SummaryTableId.Items).Single(r => r.Key == before.Key);
            Assert.True(after.Values[6] > before.Values[6]);
            Assert.NotEqual(before.Cells[6], after.Cells[6]);
            Assert.True(window.IsVisible);
            Assert.Equal((6, true), (window.SortOf(SummaryTableId.Items).Column, window.SortOf(SummaryTableId.Items).Ascending));
            Assert.Equal(before.Key, window.SelectedKey);
            Assert.Equal(offset, window.TableScrollOffset, 1);
        }

        [AvaloniaFact]
        public async Task UnlinkedInputRow_IsErrorSoft_NoBrushResolvesAStatusKey() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 1);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);

            SummaryRow input = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Plate", StringComparison.OrdinalIgnoreCase));
            SummaryRow output = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Gear", StringComparison.OrdinalIgnoreCase));
            Assert.Equal((RowStatus.Error, RowStatus.Warning), (input.Status, output.Status));

            Assert.Equal(Token("Foreman.Color.ErrorSoft"), ((ISolidColorBrush)ContainerOf(window, r => r.Key == input.Key).Background!).Color);
            Assert.Equal(Token("Foreman.Color.WarningSoft"), ((ISolidColorBrush)ContainerOf(window, r => r.Key == output.Key).Background!).Color);

            window.SelectRow(input.Key);
            Assert.Equal(Token("Foreman.Color.AccentSoft"), ((ISolidColorBrush)ContainerOf(window, r => r.Key == input.Key).Background!).Color);

            foreach (string key in new[] { "Foreman.Color.Status" + "Foreground", "Foreman.Color.Status" + "WarningBackground" })
                Assert.False(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out _), key);
        }

        [AvaloniaFact]
        public async Task StatusRows_GlyphsReadAgainstTheirPaintedFill_InBothAppearances() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 1);
            fx.Graph.UpdateNodeValues();
            var tracker = new SolveStatusTracker(TimeProvider.System);
            var window = ShownWindow(fx, new GraphSummaryHost { Tracker = () => tracker });
            window.SelectSegment(1);
            SummaryRow input = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Plate", StringComparison.OrdinalIgnoreCase));
            SummaryRow output = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Gear", StringComparison.OrdinalIgnoreCase));
            Assert.Equal((RowStatus.Error, RowStatus.Warning), (input.Status, output.Status));

            var figures = new List<string>();
            var app = (App)Application.Current!;
            try {
                foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark }) {
                    app.ApplyTheme(mode);
                    Layout(window);
                    Avalonia.Media.Color secondary = Token("Foreman.Color.TextSecondary");
                    foreach ((SummaryRow row, string label) in new[] { (input, "Error"), (output, "Warning") }) {
                        double primary = double.MaxValue;
                        double weak = double.MaxValue;
                        foreach (bool stale in new[] { false, true }) {
                            if (stale)
                                tracker.OnSolveEnded([new SolveFailure([], SolveFailureReason.Infeasible)]);
                            ListBoxItem container = ContainerOf(window, r => r.Key == row.Key);
                            Avalonia.Media.Color fill = Over(((ISolidColorBrush)container.Background!).Color, Token("Foreman.Color.WellBackground"));
                            TextBlock[] texts = [.. container.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text))];
                            Assert.NotEmpty(texts);
                            foreach (TextBlock text in texts) {
                                Avalonia.Media.Color foreground = ((ISolidColorBrush)text.Foreground!).Color;
                                double ratio = ContrastAssert.ContrastRatio(Over(foreground, fill), fill);
                                if (foreground == secondary)
                                    weak = Math.Min(weak, ratio);
                                else
                                    primary = Math.Min(primary, ratio);
                            }
                        }
                        figures.Add($"{mode} {label} primary {primary:0.00} secondary {weak:0.00}");
                        Assert.True(primary >= 4.5, $"{mode} {label} primary text {primary:0.00}");
                        Assert.True(weak >= 4.0, $"{mode} {label} secondary text {weak:0.00}");
                        tracker.OnSolveStarted();
                        tracker.OnSolveEnded([]);
                    }
                }
            } finally {
                app.ApplyTheme(ThemeMode.Light);
            }

            string scratch = Environment.GetEnvironmentVariable("FOREMAN_TEST_OUT") is { Length: > 0 } configured ? configured : Path.GetTempPath();
            Directory.CreateDirectory(scratch);
            File.WriteAllLines(Path.Combine(scratch, "graph-summary-contrast.txt"), figures);
        }

        private static Avalonia.Media.Color Over(Avalonia.Media.Color top, Avalonia.Media.Color ground) {
            double a = top.A / 255.0;
            byte Mix(byte t, byte g) => (byte)Math.Round((t * a) + (g * (1 - a)));
            return Avalonia.Media.Color.FromRgb(Mix(top.R, ground.R), Mix(top.G, ground.G), Mix(top.B, ground.B));
        }

        [AvaloniaFact]
        public async Task StaleRows_ReadSecondaryItalic_WhileTheSolveFailed() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 1);
            fx.Graph.UpdateNodeValues();
            var tracker = new SolveStatusTracker(TimeProvider.System);
            var window = ShownWindow(fx, new GraphSummaryHost { Tracker = () => tracker });
            window.SelectSegment(1);
            SummaryRow gear = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Gear", StringComparison.OrdinalIgnoreCase));
            TextBlock Produced() => (TextBlock)ContainerOf(window, r => r.Key == gear.Key).GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 8).Children[6];

            Assert.Equal(FontStyle.Normal, Produced().FontStyle);
            Assert.Equal(Token("Foreman.Color.Text"), ((ISolidColorBrush)Produced().Foreground!).Color);

            tracker.OnSolveEnded([new SolveFailure([], SolveFailureReason.Infeasible)]);

            Assert.Equal(FontStyle.Italic, Produced().FontStyle);
            Assert.Equal(Token("Foreman.Color.TextSecondaryOnSoft"), ((ISolidColorBrush)Produced().Foreground!).Color);
            Assert.Equal(gear.Cells[6], Produced().Text);

            tracker.OnSolveEnded([]);

            Assert.Equal(FontStyle.Normal, Produced().FontStyle);
        }

        [AvaloniaFact]
        public async Task EmptyGraph_EmptyPanelRuledStrings_AddRecipe_FilterZzz_NoResultsClearFilterClears() {
            var fx = NewFixture(await GetCacheAsync());
            int added = 0;
            var window = ShownWindow(fx, new GraphSummaryHost { AddRecipe = () => added++ });

            Assert.True(window.EmptyPanel.IsVisible);
            Assert.Equal(StatePanelShape.Empty, window.EmptyPanel.Shape);
            Assert.Equal("Nothing to summarize", window.EmptyPanel.Headline);
            Assert.Equal("Add nodes to the graph and their totals appear here.", window.EmptyPanel.Sentence);
            Assert.False(window.TableControl.IsVisible);
            Layout(window);
            Button addRecipe = Assert.Single(window.EmptyPanel.Buttons!.Items.OfType<Button>());
            Assert.Equal("Add Recipe…", addRecipe.Content);
            RealClick.On(addRecipe);
            Assert.Equal(1, added);

            PinRate(fx, CreateSupplierNode(fx, "iron-plate"), 5);
            fx.Graph.UpdateNodeValues();
            Assert.False(window.EmptyPanel.IsVisible);
            Assert.True(window.TableControl.IsVisible);

            window.SelectSegment(1);
            window.FilterBoxControl.Text = "zzz";
            Assert.True(window.NoMatchPanel.IsVisible);
            Assert.Equal(StatePanelShape.NoResults, window.NoMatchPanel.Shape);
            Assert.Equal("No matches for “zzz”", window.NoMatchPanel.Headline);
            Assert.Equal("Clear the filter or choose another category.", window.NoMatchPanel.Sentence);
            Layout(window);
            Button clear = Assert.Single(window.NoMatchPanel.Buttons!.Items.OfType<Button>());
            Assert.Equal("Clear Filter", clear.Content);
            RealClick.On(clear);
            Assert.Equal("", window.FilterBoxControl.Text);
            Assert.False(window.NoMatchPanel.IsVisible);

            for (int kind = 0; kind < 7; kind++)
                window.SetKind(kind, false);
            Assert.True(window.HiddenPanel.IsVisible);
            Assert.Equal("Nothing to show", window.HiddenPanel.Headline);
            Assert.Equal("Every row is hidden by the Show menu.", window.HiddenPanel.Sentence);
            Layout(window);
            Button showAll = Assert.Single(window.HiddenPanel.Buttons!.Items.OfType<Button>());
            Assert.Equal("Show All", showAll.Content);
            RealClick.On(showAll);
            Assert.All(window.ShowButtonControl.Items, i => Assert.True(i.IsChecked));
            Assert.False(window.HiddenPanel.IsVisible);
        }

        [AvaloniaFact]
        public async Task HeaderClick_SortsAndReverses_TableIsATable_RowsNameTheirCells() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 1);
            ISupplierNodeViewModel ironSupplier = CreateSupplierNode(fx, "iron-plate");
            Link(fx, ironSupplier, node, new ItemQualityPair(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!));
            PinRate(fx, ironSupplier, 5);
            IRecipeNodeViewModel cableNode = CreateRecipeNode(fx, "copper-cable");
            PinFactories(fx, cableNode, 1);
            ISupplierNodeViewModel copperSupplier = CreateSupplierNode(fx, "copper-plate");
            Link(fx, copperSupplier, cableNode, new ItemQualityPair(fx.Cache.Items["copper-plate"], fx.Cache.DefaultQuality!));
            PinRate(fx, copperSupplier, 3);
            double[] rates = [9, 1, 10, 100, 25, 3, 11, 2, 50, 8, 12, 200];
            IEnumerable<string> recipes = fx.Cache.Recipes.Values
                .Where(r => !r.Name.StartsWith('§') && r.ProductList is [IItem and not IFluid])
                .DistinctBy(r => r.ProductList[0])
                .Select(r => r.Name);
            foreach ((string recipe, double rate) in recipes.Zip(rates))
                PinFactories(fx, CreateRecipeNode(fx, recipe), rate);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            Layout(window);
            double[] Produced() => [.. window.Shown.Select(r => r.Values[6]).Where(v => v > 0)];
            Assert.True(Produced().Length >= 10, $"{Produced().Length}");
            Assert.NotEqual([.. Produced().OrderBy(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal)], [.. Produced().Order()]);
            Assert.Equal((1, false), (window.SortOf(SummaryTableId.Items).Column, window.SortOf(SummaryTableId.Items).Ascending));
            Assert.Equal((0, false), (window.SortOf(SummaryTableId.Assemblers).Column, window.SortOf(SummaryTableId.Assemblers).Ascending));
            Assert.Equal((3, false), (window.SortOf(SummaryTableId.KeyNodes).Column, window.SortOf(SummaryTableId.KeyNodes).Ascending));
            double[] defaultColumn = [.. window.Shown.Select(r => r.Values[1])];
            Assert.Equal(defaultColumn.OrderByDescending(v => v), defaultColumn);
            Assert.True(defaultColumn.Where(v => v != 0).Distinct().Count() >= 2, string.Join(",", defaultColumn));
            window.Width = 1500;
            Layout(window);
            Control Header(int i) => window.HeaderRowControl.Children.OfType<Border>().Single(b => b.Name == "SummaryHeader" + i);

            RealClick.On(Header(6));
            Assert.Equal((6, false), (window.SortOf(SummaryTableId.Items).Column, window.SortOf(SummaryTableId.Items).Ascending));
            Assert.Equal("Produced, sorted descending", AutomationProperties.GetName(Header(6)));
            Assert.Contains("▾", ((TextBlock)((Border)Header(6)).Child!).Text);
            Assert.Equal(Produced().OrderByDescending(v => v), Produced());

            RealClick.On(Header(6));
            Assert.True(window.SortOf(SummaryTableId.Items).Ascending);
            Assert.Equal("Produced, sorted ascending", AutomationProperties.GetName(Header(6)));
            Assert.Contains("▴", ((TextBlock)((Border)Header(6)).Child!).Text);
            Assert.Equal(Produced().Order(), Produced());

            Assert.Equal(AutomationControlType.Table, ControlAutomationPeer.CreatePeerForElement(window.TableControl).GetAutomationControlType());
            SummaryRow gear = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Gear", StringComparison.OrdinalIgnoreCase));
            string name = AutomationProperties.GetName(ContainerOf(window, r => r.Key == gear.Key))!;
            Assert.StartsWith(gear.Cells[0] + ", ", name);
            Assert.Contains($"produced {NodeCardPainter.Number(gear.Values[6])} per second", name);
        }

        [AvaloniaFact]
        public async Task ItemsTable_EveryKindOn_ListsAnAllZeroRow_AndTurningAKindOffHidesIt() {
            var fx = NewFixture(await GetCacheAsync());
            CreateSupplierNode(fx, "iron-plate");
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            Layout(window);

            SummaryRow row = Assert.Single(window.Rows(SummaryTableId.Items));
            Assert.All(row.Cells.Skip(1), c => Assert.Equal(SummaryTables.Zero, c));
            Assert.Equal([row.Key], window.Shown.Select(r => r.Key));
            Assert.True(window.TableControl.IsVisible);
            Assert.False(window.EmptyPanel.IsVisible);
            Assert.False(window.HiddenPanel.IsVisible);
            Assert.False(window.NoMatchPanel.IsVisible);
            Assert.False(window.ShowButtonControl.Menu.Items[^1].IsEnabled);
            Assert.Equal("Show All", window.ShowButtonControl.Menu.Items[^1].Text);

            window.ShowButtonControl.Menu.Items[0].Activate!.Invoke();

            Assert.Empty(window.Shown);
            Assert.True(window.HiddenPanel.IsVisible);
            Assert.False(window.EmptyPanel.IsVisible);
            Assert.True(window.ShowButtonControl.Menu.Items[^1].IsEnabled);
        }

        [AvaloniaFact]
        public async Task KeyNodesTable_SpoilAndPlantKeyNodes_ListUnderRecipeNodes() {
            var fx = NewFixture(await GetCacheAsync());
            fx.Graph.CreateSpoilNode(new ItemQualityPair(fx.Cache.Items["iron-plate"], fx.Cache.DefaultQuality!), fx.Cache.Items["copper-plate"], Point.Empty);
            var process = new PlantProcessPrototype(fx.Cache, "plant-summary-test");
            process.InternalOneWayAddProduct((ItemPrototype)fx.Cache.Items["copper-plate"], 1);
            process.Seed = (ItemPrototype)fx.Cache.Items["iron-plate"];
            fx.Graph.CreatePlantNode(process, fx.Cache.DefaultQuality!, Point.Empty);
            foreach (INodeViewModel node in fx.Session.View.Nodes.Where(n => n is ISpoilNodeViewModel or IPlantNodeViewModel))
                NodeControllerFor(fx, node).SetKeyNode(true);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(2);
            Layout(window);

            Assert.Equal(2, window.Shown.Count);
            Assert.False(window.EmptyPanel.IsVisible);
            foreach (SummaryRow row in window.Rows(SummaryTableId.KeyNodes)) {
                Assert.True(SummaryTables.PassesKinds(SummaryTableId.KeyNodes, row, [false, false, false, true]));
                Assert.False(SummaryTables.PassesKinds(SummaryTableId.KeyNodes, row, [true, true, true, false]));
            }

            window.ShowButtonControl.Menu.Items[3].Activate!.Invoke();
            Assert.Empty(window.Shown);
            Assert.True(window.HiddenPanel.IsVisible);
            Assert.True(window.ShowButtonControl.Menu.Items[^1].IsEnabled);
        }

        [Fact]
        public void FormatRate_ReadsTheCanvasNumberWithItsUnitSuffix_AndAZeroReadsBare() {
            Assert.Equal("45/s", SummaryTables.FormatRate(45, ProductionGraph.RateUnit.Per1Sec));
            Assert.Equal("3.5/min", SummaryTables.FormatRate(3.5, ProductionGraph.RateUnit.Per1Min));
            Assert.Equal("12/h", SummaryTables.FormatRate(12, ProductionGraph.RateUnit.Per1Hour));
            Assert.Equal("0", SummaryTables.FormatRate(0, ProductionGraph.RateUnit.Per1Sec));
            Assert.Equal("0", SummaryTables.Zero);
        }

        [AvaloniaFact]
        public async Task Cells_ReadZeroAsZero_RatesWithTheirUnit_AndCountsAsBareIntegers() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 2);
            fx.Graph.UpdateNodeValues();

            var window = NewWindow(fx, ProductionGraph.RateUnit.Per1Min);

            ItemRow input = window.ViewModel.Items.Single(r => r.Name.Contains("Plate", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("0", input.InText);
            Assert.Equal(SummaryTables.FormatRate(input.InULValue, ProductionGraph.RateUnit.Per1Min), input.InULText);
            Assert.EndsWith("/min", input.InULText);
            BuildingRow building = Assert.Single(window.ViewModel.Assemblers);
            Assert.Equal("2", building.CountText);
            Assert.DoesNotContain("-", building.CountText);
            Assert.Equal("0", building.PowerBText);
        }

        [AvaloniaFact]
        public async Task Footer_ReadsThePerSegmentTotals_OnTheBuildingsSegmentOnly_AtTheRuledGeometry() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel assembler = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, assembler, 2);
            SetBeaconConst(fx, assembler, 3);
            IRecipeNodeViewModel miner = CreateRecipeNode(fx, MinerRecipeName);
            PinFactories(fx, miner, 1);
            IRecipeNodeViewModel generator = CreateRecipeNode(fx, GeneratorRecipeName);
            PinFactories(fx, generator, 3);
            SetBeaconConst(fx, generator, 2);
            fx.Graph.UpdateNodeValues();
            double generated = generator.GetTotalGeneratorElectricalProduction() + generator.GetTotalAssemblerElectricalConsumption();
            double consumed = generator.GetTotalBeaconElectricalConsumption();
            Assert.True(generated > 0 && consumed > 0 && generated != consumed, $"{generated} {consumed}");
            var window = ShownWindow(fx);
            Layout(window);

            Assert.True(window.FooterBar.IsVisible);
            Assert.Equal($"2 buildings · {GraphicsStuff.DoubleToEnergy(assembler.GetTotalAssemblerElectricalConsumption(), "W")} · {GraphicsStuff.DoubleToEnergy(assembler.GetTotalBeaconElectricalConsumption(), "W")} from beacons", window.FooterLabel.Text);

            window.FilterBoxControl.Text = "zzz";
            Assert.Equal($"2 buildings · {GraphicsStuff.DoubleToEnergy(assembler.GetTotalAssemblerElectricalConsumption(), "W")} · {GraphicsStuff.DoubleToEnergy(assembler.GetTotalBeaconElectricalConsumption(), "W")} from beacons", window.FooterLabel.Text);
            window.FilterBoxControl.Text = "";

            window.SelectSubSegment(1);
            Assert.Equal($"1 buildings · {GraphicsStuff.DoubleToEnergy(miner.GetTotalAssemblerElectricalConsumption(), "W")} · {GraphicsStuff.DoubleToEnergy(miner.GetTotalBeaconElectricalConsumption(), "W")} from beacons", window.FooterLabel.Text);

            window.SelectSubSegment(2);
            Assert.Equal($"{GraphicsStuff.DoubleToEnergy(generated, "W")} generated · {GraphicsStuff.DoubleToEnergy(consumed, "W")} consumed", window.FooterLabel.Text);

            window.SelectSubSegment(3);
            double beaconCount = window.Rows(SummaryTableId.Beacons).Sum(r => r.Values[0]);
            double beaconPower = window.Rows(SummaryTableId.Beacons).Sum(r => r.Values[2]);
            Assert.Equal($"{beaconCount.ToString("N0", DisplayCulture.Format)} beacons · {GraphicsStuff.DoubleToEnergy(beaconPower, "W")}", window.FooterLabel.Text);

            Assert.Equal(0, window.FindControl<Grid>("TableArea")!.Margin.Bottom);
            Application.Current!.TryGetResource("Foreman.Size.ControlHeightLarge", Application.Current.ActualThemeVariant, out object? height);
            Application.Current.TryGetResource("Foreman.Type.Caption.FontSize", Application.Current.ActualThemeVariant, out object? caption);
            Assert.Equal(24, (double)height!);
            Assert.Equal(24, window.FooterBar.Bounds.Height);
            Assert.Equal(new Thickness(20, 0), window.FooterBar.Padding);
            Assert.Equal(new Thickness(0, 1, 0, 0), window.FooterBar.BorderThickness);
            Assert.Equal(Token("Foreman.Color.Border"), ((ISolidColorBrush)window.FooterBar.BorderBrush!).Color);
            Assert.Equal(Token("Foreman.Color.WindowBackground"), ((ISolidColorBrush)window.FooterBar.Background!).Color);
            Assert.Equal((double)caption!, window.FooterLabel.FontSize);
            Assert.Equal(Token("Foreman.Color.TextSecondary"), ((ISolidColorBrush)window.FooterLabel.Foreground!).Color);
            Assert.Equal(Avalonia.Media.FontFeatureCollection.Parse("tnum"), window.FooterLabel.FontFeatures);

            window.SelectSegment(1);
            Assert.False(window.FooterBar.IsVisible);
            Assert.Equal(20, window.FindControl<Grid>("TableArea")!.Margin.Bottom);
            window.SelectSegment(2);
            Assert.False(window.FooterBar.IsVisible);
            window.SelectSegment(0);
            Assert.True(window.FooterBar.IsVisible);
        }

        [AvaloniaFact]
        public async Task TheFooter_IsReadAfterTheTable() {
            var fx = NewFixture(await GetCacheAsync());
            PinFactories(fx, CreateRecipeNode(fx, AssemblerRecipeName), 2);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            Layout(window);

            IReadOnlyList<AutomationPeer> peers = Flatten(ControlAutomationPeer.CreatePeerForElement(window));
            int footer = IndexOf(peers, p => p.GetName() == window.FooterLabel.Text);
            int table = IndexOf(peers, p => p.GetAutomationControlType() == AutomationControlType.Table);
            Assert.True(table >= 0 && footer > table, $"{table} {footer}");
        }

        private static int IndexOf(IReadOnlyList<AutomationPeer> peers, Func<AutomationPeer, bool> match) {
            for (int i = 0; i < peers.Count; i++)
                if (match(peers[i]))
                    return i;
            return -1;
        }

        private static List<AutomationPeer> Flatten(AutomationPeer root) {
            var list = new List<AutomationPeer>();
            void Walk(AutomationPeer peer) {
                list.Add(peer);
                foreach (AutomationPeer child in peer.GetChildren())
                    Walk(child);
            }
            Walk(root);
            return list;
        }

        [AvaloniaFact]
        public async Task ExportCsv_WritesBareNumbersUnderThePerRateHeaders() {
            var fx = NewFixture(await GetCacheAsync());
            PinFactories(fx, CreateRecipeNode(fx, AssemblerRecipeName), 2);
            ISupplierNodeViewModel supplier = CreateSupplierNode(fx, "copper-plate");
            NodeControllerFor(fx, supplier).SetKeyNode(true);
            PinRate(fx, supplier, 7.5);
            fx.Graph.UpdateNodeValues();
            var window = NewWindow(fx, ProductionGraph.RateUnit.Per1Min);
            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
            window.SaveFilePathStub = () => Task.FromResult<string?>(tempPath);
            window.SelectSegment(1);
            await window.ExportCsvAsync().ConfigureAwait(true);
            string[] itemLines = (await File.ReadAllTextAsync(tempPath).ConfigureAwait(true)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            window.SelectSegment(2);
            await window.ExportCsvAsync().ConfigureAwait(true);
            string[] keyLines = (await File.ReadAllTextAsync(tempPath).ConfigureAwait(true)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            File.Delete(tempPath);

            Assert.Contains("(per 1 min)", itemLines[0]);
            ItemRow plate = window.ViewModel.Items.Single(r => r.Name.Contains("Iron plate", StringComparison.OrdinalIgnoreCase));
            Assert.NotEqual(0, plate.InULValue);
            string line = itemLines.Single(l => l.StartsWith(plate.Name.Replace(",", ""), StringComparison.Ordinal));
            string[] cells = line.TrimEnd('\r').Split(',');
            Assert.Equal(GraphicsStuff.DoubleToString(plate.InULValue), cells[2]);
            Assert.All(cells.Skip(1), c => Assert.DoesNotContain("/", c));
            Assert.Equal("0", cells[1]);
            Assert.Contains("(per 1 min)", keyLines[0]);
            string[] keyCells = keyLines[1].TrimEnd('\r').Split(',');
            Assert.Equal(GraphicsStuff.DoubleToString(window.ViewModel.KeyNodes.Single().ThroughputValue), keyCells[3]);
            Assert.NotEqual("0", keyCells[3]);
            Assert.DoesNotContain("/", keyCells[3]);
        }

        [AvaloniaFact]
        public async Task SecondaryCells_OnSoftRows_UseTheStrongerSecondary_AndReadAtTheRuledContrast() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 1);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            SummaryRow input = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Plate", StringComparison.OrdinalIgnoreCase));
            SummaryRow output = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Gear", StringComparison.OrdinalIgnoreCase));
            var app = (App)Application.Current!;
            try {
                foreach ((ThemeMode mode, string hex) in new[] { (ThemeMode.Light, "#A0000000"), (ThemeMode.Dark, "#A0FFFFFF") }) {
                    app.ApplyTheme(mode);
                    Layout(window);
                    Assert.Equal(Avalonia.Media.Color.Parse(hex), Token("Foreman.Color.TextSecondaryOnSoft"));
                    foreach (SummaryRow row in new[] { input, output }) {
                        ListBoxItem container = ContainerOf(window, r => r.Key == row.Key);
                        Avalonia.Media.Color fill = Over(((ISolidColorBrush)container.Background!).Color, Token("Foreman.Color.WellBackground"));
                        TextBlock[] zeros = [.. container.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == SummaryTables.Zero)];
                        Assert.NotEmpty(zeros);
                        foreach (TextBlock zero in zeros) {
                            Avalonia.Media.Color foreground = ((ISolidColorBrush)zero.Foreground!).Color;
                            Assert.Equal(Token("Foreman.Color.TextSecondaryOnSoft"), foreground);
                            double ratio = ContrastAssert.ContrastRatio(Over(foreground, fill), fill);
                            Assert.True(ratio >= 4.5, $"{mode} {row.Key} {ratio:0.00}");
                        }
                    }

                    window.SelectRow(input.Key);
                    Layout(window);
                    TextBlock selectedZero = ContainerOf(window, r => r.Key == input.Key).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == SummaryTables.Zero);
                    Assert.Equal(Token("Foreman.Color.TextSecondary"), ((ISolidColorBrush)selectedZero.Foreground!).Color);
                    window.TableControl.SelectedItem = null;
                }
            } finally {
                app.ApplyTheme(ThemeMode.Light);
            }
        }

        [AvaloniaFact]
        public async Task RowAutomationName_EndsWithTheStatusWords_InTheRuledOrder() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipeNode = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, recipeNode, 2);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            SummaryRow input = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Plate", StringComparison.OrdinalIgnoreCase));
            SummaryRow output = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Gear", StringComparison.OrdinalIgnoreCase));

            string Name(SummaryRow r) => AutomationProperties.GetName(ContainerOf(window, x => x.Key == r.Key))!;

            Assert.EndsWith(", unlinked input", Name(input));
            Assert.EndsWith(", unlinked output, overproduced", Name(output));
            Assert.DoesNotContain("unlinked input", Name(output));
        }

        [AvaloniaFact]
        public async Task FirstOpen_DefaultSizeScalesWithTheStep_AndARestoredSizeComesBackAtThatStep() {
            var fx = NewFixture(await GetCacheAsync());
            WindowScaler.SetScale(1.25);
            try {
                var settings = new AppSettings();
                GraphSummaryWindow first = ShownWindow(fx, new GraphSummaryHost { Settings = settings });
                Assert.Equal((975, 700), (first.Width, first.Height));
                first.Width = 1100;
                first.Height = 800;
                Dispatcher.UIThread.RunJobs();
                first.Close();

                GraphSummaryWindow second = ShownWindow(fx, new GraphSummaryHost { Settings = settings });
                Assert.Equal(1100, second.Width, 1);
                Assert.Equal(800, second.Height, 1);
                second.Close();
            } finally {
                WindowScaler.SetScale(1.0);
            }
        }

        [AvaloniaFact]
        public async Task TypingAPrefixInTheItemsTable_SelectsTheMatchingRow() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 1);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            Layout(window);
            SummaryRow plate = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].StartsWith("Iron plate", StringComparison.OrdinalIgnoreCase));
            Assert.True(window.TableControl.IsTextSearchEnabled);
            window.TableControl.Focus();

            CanvasKeys.TextInputOn(window.TableControl, "iron p");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(plate.Key, window.SelectedKey);
        }

        [AvaloniaFact]
        public async Task TabOrder_TopSegment_SecondSegment_Show_Filter_Export_Table() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 1);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            window.TableControl.SelectedIndex = 0;
            Layout(window);
            Control[] expected = [window.TopSegmentControl, window.SubSegmentControl, window.ShowButtonControl, window.FilterBoxControl, window.ExportButtonControl, window.TableControl];

            window.TopSegmentControl.Focus(NavigationMethod.Tab);
            Assert.True(expected[0].IsKeyboardFocusWithin);
            for (int i = 1; i < expected.Length; i++) {
                CanvasKeys.KeyDownOn((Control)window.FocusManager!.GetFocusedElement()!, Key.Tab);
                Assert.True(expected[i].IsKeyboardFocusWithin, $"Tab {i} should reach {expected[i].GetType().Name}, reached {window.FocusManager.GetFocusedElement()}");
            }
        }

        [AvaloniaFact]
        public async Task RowAutomationName_SaysUnlinkedInputOnlyForAnUnlinkedInput_AndOverproducedForOverproductionAlone() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipeNode = CreateRecipeNode(fx, AssemblerRecipeName);
            ItemQualityPair outputPair = new(fx.Cache.Items["iron-gear-wheel"], fx.Cache.DefaultQuality!);
            IConsumerNodeViewModel consumer = CreateConsumerNode(fx, "iron-gear-wheel");
            Link(fx, recipeNode, consumer, outputPair);
            PinFactories(fx, recipeNode, 2);
            fx.Graph.UpdateNodeValues();
            PinRate(fx, consumer, recipeNode.GetSupplyRate(outputPair) / 2);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            SummaryRow input = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Plate", StringComparison.OrdinalIgnoreCase));
            SummaryRow output = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Gear", StringComparison.OrdinalIgnoreCase));
            Assert.Equal((RowStatus.Error, RowStatus.Warning), (input.Status, output.Status));

            string Name(SummaryRow r) => AutomationProperties.GetName(ContainerOf(window, x => x.Key == r.Key))!;

            Assert.EndsWith(", unlinked input", Name(input));
            Assert.EndsWith(", overproduced", Name(output));
            Assert.DoesNotContain("unlinked", Name(output));
        }

        [AvaloniaFact]
        public async Task ItemRow_NodeSolveStateStale_ReadsSecondaryItalic_WithTheTrackerIdle_AndACurrentRowStaysRoman() {
            var fx = NewFixture(await GetCacheAsync());
            ISupplierNodeViewModel stale = CreateSupplierNode(fx, "iron-plate");
            ISupplierNodeViewModel current = CreateSupplierNode(fx, "copper-plate");
            PinRate(fx, stale, 5);
            PinRate(fx, current, 5);
            fx.Graph.UpdateNodeValues();
            fx.Session.TryGetDomainNode(stale.Id, out BaseNode? domain);
            domain!.SolveState = NodeSolveState.Stale;
            var window = ShownWindow(fx, new GraphSummaryHost { Tracker = () => new SolveStatusTracker(TimeProvider.System) });
            window.SelectSegment(1);
            SummaryRow staleRow = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Iron", StringComparison.OrdinalIgnoreCase));
            SummaryRow currentRow = window.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Copper", StringComparison.OrdinalIgnoreCase));
            Assert.Equal((true, false), (staleRow.IsStale, currentRow.IsStale));
            TextBlock Input(SummaryRow r) => (TextBlock)ContainerOf(window, x => x.Key == r.Key).GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 8).Children[1];

            Assert.Equal(FontStyle.Italic, Input(staleRow).FontStyle);
            Assert.Equal(Token("Foreman.Color.TextSecondary"), ((ISolidColorBrush)Input(staleRow).Foreground!).Color);
            Assert.Equal(FontStyle.Normal, Input(currentRow).FontStyle);
            Assert.Equal(Token("Foreman.Color.Text"), ((ISolidColorBrush)Input(currentRow).Foreground!).Color);
        }

        [AvaloniaFact]
        public async Task KeyNodesShowMenu_UntickingRecipeNodes_HidesARecipeKeyNode_AndKeepsASupplierKeyNode() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel recipe = CreateRecipeNode(fx, AssemblerRecipeName);
            ISupplierNodeViewModel supplier = CreateSupplierNode(fx, "iron-plate");
            NodeControllerFor(fx, recipe).SetKeyNode(true);
            NodeControllerFor(fx, supplier).SetKeyNode(true);
            fx.Graph.UpdateNodeValues();
            var window = ShownWindow(fx);
            window.SelectSegment(2);
            Assert.Equal(2, window.Shown.Count);

            window.SetKind(3, false);

            SummaryRow only = Assert.Single(window.Shown);
            Assert.Same(supplier, only.Tag);
            Assert.False(window.HiddenPanel.IsVisible);
        }

        [AvaloniaFact]
        public async Task Reveal_AssemblersMinersBeaconsAndKeyNodes_ReachExactlyTheNodesUsingThatRow() {
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel gearA = CreateRecipeNode(fx, AssemblerRecipeName);
            IRecipeNodeViewModel gearB = CreateRecipeNode(fx, AssemblerRecipeName);
            IRecipeNodeViewModel miner = CreateRecipeNode(fx, MinerRecipeName);
            ISupplierNodeViewModel supplier = CreateSupplierNode(fx, "iron-plate");
            PinFactories(fx, gearA, 1);
            PinFactories(fx, gearB, 1);
            PinFactories(fx, miner, 1);
            SetBeaconConst(fx, gearA, 2);
            NodeControllerFor(fx, supplier).SetKeyNode(true);
            fx.Graph.UpdateNodeValues();
            IReadOnlyList<INodeViewModel>? revealed = null;
            var window = ShownWindow(fx, new GraphSummaryHost { Reveal = nodes => revealed = nodes });

            void Reveals(int segment, int sub, INodeViewModel[] expected) {
                window.SelectSegment(segment);
                if (segment == 0)
                    window.SelectSubSegment(sub);
                revealed = null;
                window.SelectRow(Assert.Single(window.Shown).Key);
                window.RevealSelected();
                Assert.Equal(expected.Select(n => n.Id.Value).Order(), revealed!.Select(n => n.Id.Value).Order());
            }

            Reveals(0, 0, [gearA, gearB]);
            Reveals(0, 1, [miner]);
            Reveals(0, 3, [gearA]);
            Reveals(2, 0, [supplier]);
        }

        [Fact]
        public void Placement_PrefersTheRightThenTheLeftThenCascades() {
            var screen = new PixelRect(0, 0, 1920, 1080);
            var size = new PixelSize(780, 560);

            Assert.Equal(new PixelPoint(912, 40), GraphSummaryPlacement.Resolve(new PixelRect(100, 40, 800, 600), size, screen, 1));
            Assert.Equal(new PixelPoint(208, 40), GraphSummaryPlacement.Resolve(new PixelRect(1000, 40, 800, 600), size, screen, 1));
            Assert.Equal(new PixelPoint(28, 28), GraphSummaryPlacement.Resolve(new PixelRect(0, 0, 1800, 900), size, screen, 1));
            Assert.Equal(new PixelPoint(812, 0), GraphSummaryPlacement.Resolve(new PixelRect(0, 0, 800, 600), size, null, 1));
            Assert.Equal(new PixelPoint(1624, 0), GraphSummaryPlacement.Resolve(new PixelRect(0, 0, 1600, 1200), new PixelSize(1560, 1120), null, 2));
        }

        [AvaloniaFact]
        public async Task CmdOneToThree_TopSegment_OptCmdOneToFour_Second_EscapeClearsThenCloses() {
            var window = ShownWindow(NewFixture(await GetCacheAsync()));

            CanvasKeys.KeyDownOn(window, Key.D2, KeyModifiers.Meta);
            Assert.Equal(1, window.SegmentIndex);
            CanvasKeys.KeyDownOn(window, Key.D3, KeyModifiers.Meta);
            Assert.Equal(2, window.SegmentIndex);
            CanvasKeys.KeyDownOn(window, Key.D1, KeyModifiers.Meta);
            Assert.Equal(0, window.SegmentIndex);
            CanvasKeys.KeyDownOn(window, Key.D4, KeyModifiers.Meta | KeyModifiers.Alt);
            Assert.Equal(3, window.SubSegmentControl.SelectedIndex);
            Assert.Equal(SummaryTableId.Beacons, window.CurrentTable);
            CanvasKeys.KeyDownOn(window, Key.D2, KeyModifiers.Meta | KeyModifiers.Alt);
            Assert.Equal(SummaryTableId.Miners, window.CurrentTable);

            CanvasKeys.KeyDownOn(window, Key.F, KeyModifiers.Meta);
            Assert.True(window.FilterBoxControl.IsFocused);

            window.FilterBoxControl.Text = "gear";
            CanvasKeys.KeyDownOn(window, Key.Escape);
            Assert.Equal("", window.FilterBoxControl.Text);
            Assert.True(window.IsVisible);
            CanvasKeys.KeyDownOn(window, Key.Escape);
            Assert.False(window.IsVisible);

            var second = ShownWindow(NewFixture(await GetCacheAsync()));
            CanvasKeys.KeyDownOn(second, Key.W, KeyModifiers.Meta);
            Assert.False(second.IsVisible);
        }

        [AvaloniaFact]
        public async Task DoubleClickRow_SelectsMatchingNodes_PansAndBringsTheGraphWindowForward() {
            MainWindow main = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();
            var editor = main.GraphCanvas.Viewer.Session.Editor;
            DataCache cache = main.DataCache!;
            var iron = new ItemQualityPair(cache.Items["iron-plate"], cache.DefaultQuality!);
            var copper = new ItemQualityPair(cache.Items["copper-plate"], cache.DefaultQuality!);
            NodeId ironSupplier = editor.CreateSupplierNode(iron, new Point(0, 0));
            NodeId copperSupplier = editor.CreateSupplierNode(copper, new Point(-900, -600));
            NodeId ironConsumer = editor.CreateConsumerNode(iron, new Point(400, 250));
            foreach (NodeId id in new[] { ironSupplier, copperSupplier, ironConsumer }) {
                var controller = (BaseNodeController)editor.RequestNodeController(id)!;
                controller.SetRateType(RateType.Manual);
                controller.SetDesiredSetValue(5);
            }
            Dispatcher.UIThread.RunJobs();
            main.GraphCanvas.Viewer.Graph.UpdateNodeValues();
            Viewport viewport = main.GraphCanvas.Viewport;
            viewport.ViewOffset = new Point(-20000, -20000);
            viewport.UpdateGraphBounds();
            main.ShowGraphSummary();
            GraphSummaryWindow summary = main.CurrentGraphSummaryWindow!;
            summary.SelectSegment(1);
            SummaryRow ironRow = summary.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Iron", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, summary.NodesFor(SummaryTableId.Items, ironRow).Count);

            Motion.ReduceMotion = true;
            try {
                ListBoxItem container = ContainerOf(summary, r => r.Key == ironRow.Key);
                RealClick.DoubleClick(container);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ironRow.Key, summary.SelectedKey);
                Assert.Equal([ironSupplier, ironConsumer], main.GraphCanvas.Viewer.SelectedNodes.Select(n => n.ViewModel.Id).OrderBy(i => i.Value));
                Assert.DoesNotContain(copperSupplier, main.GraphCanvas.Viewer.SelectedNodes.Select(n => n.ViewModel.Id));
                Rectangle group = Rectangle.Union(
                    CanvasTraversal.WorldBounds(main.GraphCanvas.Viewer.NodeElementDictionary[ironSupplier]),
                    CanvasTraversal.WorldBounds(main.GraphCanvas.Viewer.NodeElementDictionary[ironConsumer]));
                int margin = (int)Math.Round(GraphCanvasControl.FindPanMargin / viewport.ViewScale);
                Assert.True(viewport.VisibleGraphBounds.Contains(Rectangle.Inflate(group, margin, margin)));

                SummaryRow copperRow = summary.Rows(SummaryTableId.Items).Single(r => r.Cells[0].Contains("Copper", StringComparison.OrdinalIgnoreCase));
                summary.SelectRow(copperRow.Key);
                CanvasKeys.KeyDownOn(summary.TableControl, Key.Enter);
                Assert.Equal([copperSupplier], main.GraphCanvas.Viewer.SelectedNodes.Select(n => n.ViewModel.Id));
            } finally {
                Motion.ReduceMotion = false;
                summary.Close();
            }
        }

        [AvaloniaFact]
        public async Task SizePositionAndLastSegment_AreRemembered() {
            MainWindow main = await Inspector.InspectorTestSupport.OpenEmptyWindowAsync();
            main.Settings = new AppSettings();
            main.ShowGraphSummary();
            GraphSummaryWindow first = main.CurrentGraphSummaryWindow!;
            first.Width = 900;
            first.Height = 640;
            first.Position = new PixelPoint(120, 80);
            first.SelectSegment(2);
            Dispatcher.UIThread.RunJobs();
            first.Close();

            AppSettings settings = main.Settings;
            Assert.Equal(2, settings.GraphSummarySegment);
            Assert.Equal(900, settings.GraphSummaryWidth!.Value * WindowScaler.CurrentScale, 1);
            Assert.Equal(640, settings.GraphSummaryHeight!.Value * WindowScaler.CurrentScale, 1);
            Assert.Equal((120d, 80d), (settings.GraphSummaryLeft!.Value, settings.GraphSummaryTop!.Value));

            main.ShowGraphSummary();
            GraphSummaryWindow second = main.CurrentGraphSummaryWindow!;
            Assert.NotSame(first, second);
            Assert.Equal((900, 640), (second.Width, second.Height));
            Assert.Equal(new PixelPoint(120, 80), second.Position);
            Assert.Equal(2, second.SegmentIndex);
            Assert.Equal(2, second.TopSegmentControl.SelectedIndex);
            second.Close();
        }

        [AvaloniaFact]
        public async Task ThemeFlip_RepaintsTheOpenWindowsRows_AndTheHubSubscriptionEndsWithTheWindow() {
            var app = (App)Application.Current!;
            app.ApplyTheme(ThemeMode.Light);
            var fx = NewFixture(await GetCacheAsync());
            IRecipeNodeViewModel node = CreateRecipeNode(fx, AssemblerRecipeName);
            PinFactories(fx, node, 1);
            fx.Graph.UpdateNodeValues();
            int baseline = app.Hub.SubscriberCount;
            var window = ShownWindow(fx);
            window.SelectSegment(1);
            Assert.Equal(baseline + 1, app.Hub.SubscriberCount);
            SummaryRow input = window.Rows(SummaryTableId.Items).Single(r => r.Status == RowStatus.Error);
            Avalonia.Media.Color Background() => ((ISolidColorBrush)ContainerOf(window, r => r.Key == input.Key).Background!).Color;
            Avalonia.Media.Color light = Background();

            try {
                app.ApplyTheme(ThemeMode.Dark);

                Assert.NotEqual(light, Background());
                Assert.Equal(Token("Foreman.Color.ErrorSoft"), Background());
            } finally {
                app.ApplyTheme(ThemeMode.Light);
            }

            window.Close();
            Assert.Equal(baseline, app.Hub.SubscriberCount);
        }
    }
}
