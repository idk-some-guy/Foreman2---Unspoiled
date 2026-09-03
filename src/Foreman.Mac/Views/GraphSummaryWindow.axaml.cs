using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    //Ports Forms/GraphSummaryForm.cs(+.Designer.cs) (reference §6): a read-only report over the whole
    //current graph (both constructor overloads collapse to the node-list one below, matching upstream -
    //MainForm always passes the whole session, never a subset).
    public partial class GraphSummaryWindow : Window {
        internal sealed class BuildingRow(object tag, string rowKey, Bitmap? icon, IBrush background, string countText, string name, string powerText, double powerValue, string powerBText, double powerBValue) {
            public object Tag { get; } = tag;
            public string RowKey { get; } = rowKey;
            public Bitmap? Icon { get; } = icon;
            public IBrush Background { get; } = background;
            public IBrush Foreground { get; } = RowForegroundBrush;
            public string CountText { get; } = countText;
            public string Name { get; } = name;
            public string PowerText { get; } = powerText;
            public double PowerValue { get; } = powerValue;
            public string PowerBText { get; } = powerBText;
            public double PowerBValue { get; } = powerBValue;
        }

        internal sealed class BeaconRow(object tag, string rowKey, Bitmap? icon, IBrush background, string countText, string name, string powerBText, double powerBValue) {
            public object Tag { get; } = tag;
            public string RowKey { get; } = rowKey;
            public Bitmap? Icon { get; } = icon;
            public IBrush Background { get; } = background;
            public IBrush Foreground { get; } = RowForegroundBrush;
            public string CountText { get; } = countText;
            public string Name { get; } = name;
            public string PowerBText { get; } = powerBText;
            public double PowerBValue { get; } = powerBValue;
        }

        internal sealed class ItemRow(object tag, string rowKey, Bitmap? icon, IBrush background, string name,
                string inText, double inValue, string inULText, double inULValue, string outText, double outValue,
                string outULText, double outULValue, string overprodText, double overprodValue, string producedText, double producedValue, string consumedText, double consumedValue) {
            public object Tag { get; } = tag;
            public string RowKey { get; } = rowKey;
            public Bitmap? Icon { get; } = icon;
            public IBrush Background { get; } = background;
            public IBrush Foreground { get; } = RowForegroundBrush;
            public string Name { get; } = name;
            public string InText { get; } = inText;
            public double InValue { get; } = inValue;
            public string InULText { get; } = inULText;
            public double InULValue { get; } = inULValue;
            public string OutText { get; } = outText;
            public double OutValue { get; } = outValue;
            public string OutULText { get; } = outULText;
            public double OutULValue { get; } = outULValue;
            public string OverprodText { get; } = overprodText;
            public double OverprodValue { get; } = overprodValue;
            public string ProducedText { get; } = producedText;
            public double ProducedValue { get; } = producedValue;
            public string ConsumedText { get; } = consumedText;
            public double ConsumedValue { get; } = consumedValue;
        }

        internal sealed class KeyNodeRow(object tag, string rowKey, Bitmap? icon, string typeText, string detailsText, string titleText, string throughputText, double throughputValue, string factoriesText, double factoriesValue) {
            public object Tag { get; } = tag;
            public string RowKey { get; } = rowKey;
            public Bitmap? Icon { get; } = icon;
            public string TypeText { get; } = typeText;
            public string DetailsText { get; } = detailsText;
            public string TitleText { get; } = titleText;
            public string ThroughputText { get; } = throughputText;
            public double ThroughputValue { get; } = throughputValue;
            public string FactoriesText { get; } = factoriesText;
            public double FactoriesValue { get; } = factoriesValue;
        }

        private sealed class ItemCounter(double i, double iu, double o, double ou, double oo, double p, double c) {
            public double Input { get; set; } = i;
            public double InputUnlinked { get; set; } = iu;
            public double Output { get; set; } = o;
            public double OutputUnlinked { get; set; } = ou;
            public double OutputOverflow { get; set; } = oo;
            public double Production { get; set; } = p;
            public double Consumption { get; set; } = c;
        }

        private static readonly IBrush AvailableRowBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        private static readonly IBrush UnavailableRowBrush = new SolidColorBrush(Color.FromRgb(255, 192, 203));

        //Both row backgrounds above are light regardless of app theme, so the row text needs an explicit
        //dark Foreground too - the live Fluent dark theme's default TextBlock foreground is white, which is
        //invisible on either background otherwise.
        private static readonly IBrush RowForegroundBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0));
        private static readonly Regex ComparerRegex = new(@"\d+", RegexOptions.Compiled);

        private readonly List<BuildingRow> unfilteredAssemblerList = [];
        private readonly List<BuildingRow> unfilteredMinerList = [];
        private readonly List<BuildingRow> unfilteredPowerList = [];
        private readonly List<BeaconRow> unfilteredBeaconList = [];
        private readonly List<ItemRow> unfilteredItemsList = [];
        private readonly List<ItemRow> unfilteredFluidsList = [];
        private readonly List<KeyNodeRow> unfilteredKeyNodesList = [];

        private readonly Dictionary<ListBox, int> lastSortOrder = [];
        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

        private readonly string rateString;

        private readonly TabControl mainTabControl;
        private readonly TabItem itemsFluidsTabItem;

        private readonly TextBox buildingsFilterTextBox;
        private readonly TextBlock buildingCountLabel;
        private readonly TextBlock beaconCountLabel;
        private readonly TextBlock powerConsumptionLabel;
        private readonly TextBlock powerProductionLabel;
        private readonly TextBlock powerNetLabel;
        private readonly ListBox assemblerListView;
        private readonly ListBox minerListView;
        private readonly ListBox powerListView;
        private readonly ListBox beaconListView;
        private readonly Button buildingsExportButton;

        private readonly TextBox itemsFilterTextBox;
        private readonly CheckBox inputFilterCheckBox;
        private readonly CheckBox inputUnlinkedFilterCheckBox;
        private readonly CheckBox outputFilterCheckBox;
        private readonly CheckBox outputUnlinkedFilterCheckBox;
        private readonly CheckBox outputOverproducedFilterCheckBox;
        private readonly CheckBox productionFilterCheckBox;
        private readonly CheckBox consumptionFilterCheckBox;
        private readonly ListBox itemsListView;
        private readonly ListBox fluidsListView;
        private readonly Button itemsExportButton;

        private readonly TextBox keyNodesFilterTextBox;
        private readonly CheckBox supplierNodeFilterCheckBox;
        private readonly CheckBox consumerNodeFilterCheckBox;
        private readonly CheckBox passthroughNodeFilterCheckBox;
        private readonly CheckBox recipeNodeFilterCheckBox;
        private readonly ListBox keyNodesListView;
        private readonly Button keyNodesExportButton;

        //Test-only seam: lets a test supply the export target path (or null for "cancelled") without a real
        //modal SaveFileDialog (same convention as SettingsWindow's DeleteConfirmationStub).
        internal Func<Task<string?>>? SaveFilePathStub { get; set; }
        internal string? LastCsvWritten { get; private set; }

        public GraphSummaryWindow() : this([], "second") {
        }

        public GraphSummaryWindow(IProductionGraphSession session, string rateString) : this(session.View.Nodes, rateString) {
        }

        public GraphSummaryWindow(IEnumerable<INodeViewModel> nodes, string rateString) {
            InitializeComponent();
            this.rateString = rateString;

            mainTabControl = this.FindControl<TabControl>("MainTabControl")!;
            itemsFluidsTabItem = this.FindControl<TabItem>("ItemsFluidsTabItem")!;

            buildingsFilterTextBox = this.FindControl<TextBox>("BuildingsFilterTextBox")!;
            buildingCountLabel = this.FindControl<TextBlock>("BuildingCountLabel")!;
            beaconCountLabel = this.FindControl<TextBlock>("BeaconCountLabel")!;
            powerConsumptionLabel = this.FindControl<TextBlock>("PowerConsumptionLabel")!;
            powerProductionLabel = this.FindControl<TextBlock>("PowerProductionLabel")!;
            powerNetLabel = this.FindControl<TextBlock>("PowerNetLabel")!;
            assemblerListView = this.FindControl<ListBox>("AssemblerListView")!;
            minerListView = this.FindControl<ListBox>("MinerListView")!;
            powerListView = this.FindControl<ListBox>("PowerListView")!;
            beaconListView = this.FindControl<ListBox>("BeaconListView")!;
            buildingsExportButton = this.FindControl<Button>("BuildingsExportButton")!;

            itemsFilterTextBox = this.FindControl<TextBox>("ItemsFilterTextBox")!;
            inputFilterCheckBox = this.FindControl<CheckBox>("InputFilterCheckBox")!;
            inputUnlinkedFilterCheckBox = this.FindControl<CheckBox>("InputUnlinkedFilterCheckBox")!;
            outputFilterCheckBox = this.FindControl<CheckBox>("OutputFilterCheckBox")!;
            outputUnlinkedFilterCheckBox = this.FindControl<CheckBox>("OutputUnlinkedFilterCheckBox")!;
            outputOverproducedFilterCheckBox = this.FindControl<CheckBox>("OutputOverproducedFilterCheckBox")!;
            productionFilterCheckBox = this.FindControl<CheckBox>("ProductionFilterCheckBox")!;
            consumptionFilterCheckBox = this.FindControl<CheckBox>("ConsumptionFilterCheckBox")!;
            itemsListView = this.FindControl<ListBox>("ItemsListView")!;
            fluidsListView = this.FindControl<ListBox>("FluidsListView")!;
            itemsExportButton = this.FindControl<Button>("ItemsExportButton")!;

            keyNodesFilterTextBox = this.FindControl<TextBox>("KeyNodesFilterTextBox")!;
            supplierNodeFilterCheckBox = this.FindControl<CheckBox>("SupplierNodeFilterCheckBox")!;
            consumerNodeFilterCheckBox = this.FindControl<CheckBox>("ConsumerNodeFilterCheckBox")!;
            passthroughNodeFilterCheckBox = this.FindControl<CheckBox>("PassthroughNodeFilterCheckBox")!;
            recipeNodeFilterCheckBox = this.FindControl<CheckBox>("RecipeNodeFilterCheckBox")!;
            keyNodesListView = this.FindControl<ListBox>("KeyNodesListView")!;
            keyNodesExportButton = this.FindControl<Button>("KeyNodesExportButton")!;

            lastSortOrder[assemblerListView] = 2;
            lastSortOrder[minerListView] = 2;
            lastSortOrder[powerListView] = 2;
            lastSortOrder[beaconListView] = 2;
            lastSortOrder[itemsListView] = 1;
            lastSortOrder[fluidsListView] = 1;
            lastSortOrder[keyNodesListView] = 1;

            itemsFluidsTabItem.Header = "Items/Fluids" + " ( per " + rateString + ")";

            IEnumerable<IRecipeNodeViewModel> recipeNodes = [.. nodes.OfType<IRecipeNodeViewModel>()];
            List<INodeViewModel> nodeList = [.. nodes];

            LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType == EntityType.Assembler), unfilteredAssemblerList);
            LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType is EntityType.Miner or EntityType.OffshorePump), unfilteredMinerList);
            LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor), unfilteredPowerList);

            LoadUnfilteredBeaconList(recipeNodes.Where(r => r.SelectedBeacon));

            LoadUnfilteredItemLists(nodeList, fluids: false, unfilteredItemsList);
            LoadUnfilteredItemLists(nodeList, fluids: true, unfilteredFluidsList);

            LoadUnfilteredKeyNodesList(nodeList.Where(n => n.KeyNode));

            double buildingTotal = recipeNodes.Sum(n => Math.Ceiling(n.ActualSetValue));
            double beaconTotal = recipeNodes.Sum(n => n.GetTotalBeacons());
            buildingCountLabel.Text = "#Buildings: " + GraphicsStuff.DoubleToString(buildingTotal);
            beaconCountLabel.Text = "#Beacons: " + GraphicsStuff.DoubleToString(beaconTotal);

            double powerConsumption = recipeNodes.Sum(n => n.GetTotalAssemblerElectricalConsumption() + n.GetTotalBeaconElectricalConsumption());
            double powerProduction = recipeNodes.Sum(n => n.GetTotalGeneratorElectricalProduction());
            powerConsumptionLabel.Text = "Power Consumption: " + GraphicsStuff.DoubleToEnergy(powerConsumption, "W");
            powerProductionLabel.Text = "Power Production: " + GraphicsStuff.DoubleToEnergy(powerProduction, "W");
            if (powerConsumption > 0 && powerProduction > 0) {
                powerNetLabel.IsVisible = true;
                powerNetLabel.Text = "Net Power: " + GraphicsStuff.DoubleToEnergy(powerProduction - powerConsumption, "W");
            } else {
                powerNetLabel.IsVisible = false;
            }

            UpdateFilteredBuildingLists();
            UpdateFilteredItemsLists();
            UpdateFilteredKeyNodesList();

            buildingsFilterTextBox.TextChanging += (_, _) => UpdateFilteredBuildingLists();
            itemsFilterTextBox.TextChanging += (_, _) => UpdateFilteredItemsLists();
            foreach (CheckBox checkBox in ItemFilterCheckBoxes)
                checkBox.IsCheckedChanged += (_, _) => UpdateFilteredItemsLists();
            keyNodesFilterTextBox.TextChanging += (_, _) => UpdateFilteredKeyNodesList();
            foreach (CheckBox checkBox in KeyNodeFilterCheckBoxes)
                checkBox.IsCheckedChanged += (_, _) => UpdateFilteredKeyNodesList();

            buildingsExportButton.Click += (_, _) => Async.Fire(ExportBuildingsCsvAsync(), nameof(ExportBuildingsCsvAsync));
            itemsExportButton.Click += (_, _) => Async.Fire(ExportItemsCsvAsync(), nameof(ExportItemsCsvAsync));
            keyNodesExportButton.Click += (_, _) => Async.Fire(ExportKeyNodesCsvAsync(), nameof(ExportKeyNodesCsvAsync));

            WireColumnHeaders();
        }

        //One clickable header Button per upstream ColumnHeader (reference §6): each wires straight to the
        //matching sort method below rather than a generic column-click event, since Avalonia's ListBox has
        //no built-in column-header concept to hook into.
        private void WireColumnHeaders() {
            for (int column = 0; column < 4; column++) {
                int c = column;
                WireHeader("AssemblerHeader" + c, () => SortBuildingColumn(unfilteredAssemblerList, assemblerListView, c));
                WireHeader("MinerHeader" + c, () => SortBuildingColumn(unfilteredMinerList, minerListView, c));
                WireHeader("PowerHeader" + c, () => SortBuildingColumn(unfilteredPowerList, powerListView, c));
            }
            for (int column = 0; column < 3; column++) {
                int c = column;
                WireHeader("BeaconHeader" + c, () => SortBeaconColumn(c));
            }
            for (int column = 0; column < 8; column++) {
                int c = column;
                WireHeader("ItemsHeader" + c, () => SortItemColumn(unfilteredItemsList, itemsListView, c));
                WireHeader("FluidsHeader" + c, () => SortItemColumn(unfilteredFluidsList, fluidsListView, c));
            }
            for (int column = 0; column < 5; column++) {
                int c = column;
                WireHeader("KeyNodesHeader" + c, () => SortKeyNodesColumn(c));
            }
        }

        private void WireHeader(string name, Action sort) => this.FindControl<Button>(name)!.Click += (_, _) => sort();

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private IEnumerable<CheckBox> ItemFilterCheckBoxes {
            get {
                yield return inputFilterCheckBox;
                yield return inputUnlinkedFilterCheckBox;
                yield return outputFilterCheckBox;
                yield return outputUnlinkedFilterCheckBox;
                yield return outputOverproducedFilterCheckBox;
                yield return productionFilterCheckBox;
                yield return consumptionFilterCheckBox;
            }
        }

        private IEnumerable<CheckBox> KeyNodeFilterCheckBoxes {
            get {
                yield return supplierNodeFilterCheckBox;
                yield return consumerNodeFilterCheckBox;
                yield return passthroughNodeFilterCheckBox;
                yield return recipeNodeFilterCheckBox;
            }
        }

        //-------------------------------------------------------------------------------------------------------Initial list initialization

        //Ports LoadUnfilteredSelectedAssemblerList (reference §6, GraphSummaryForm.cs:134-165).
        private void LoadUnfilteredSelectedAssemblerList(IEnumerable<IRecipeNodeViewModel> origin, List<BuildingRow> destination) {
            var buildingCounters = new Dictionary<AssemblerQualityPair, int>();
            var buildingElectricalPower = new Dictionary<AssemblerQualityPair, (double Assembler, double Beacon)>();

            foreach (IRecipeNodeViewModel rnode in origin) {
                if (!buildingCounters.ContainsKey(rnode.SelectedAssembler)) {
                    buildingCounters[rnode.SelectedAssembler] = 0;
                    buildingElectricalPower[rnode.SelectedAssembler] = (0, 0);
                }
                buildingCounters[rnode.SelectedAssembler] += (int)Math.Ceiling(rnode.ActualSetValue);
                (double assemblerPower, double beaconPower) = buildingElectricalPower[rnode.SelectedAssembler];
                buildingElectricalPower[rnode.SelectedAssembler] = (
                    assemblerPower + rnode.GetTotalGeneratorElectricalProduction() + rnode.GetTotalAssemblerElectricalConsumption(),
                    beaconPower + rnode.GetTotalBeaconElectricalConsumption());
            }

            foreach (AssemblerQualityPair assembler in buildingCounters.Keys.OrderByDescending(a => a.Assembler.Available).ThenBy(a => a.Assembler.FriendlyName, StringComparer.Ordinal).ThenBy(a => a.Quality.Level).ThenBy(a => a.Quality.FriendlyName, StringComparer.Ordinal)) {
                (double assemblerPower, double beaconPower) = buildingElectricalPower[assembler];
                int count = buildingCounters[assembler];
                destination.Add(new BuildingRow(
                    assembler,
                    assembler.Assembler.Name + ":" + assembler.Quality.Name,
                    GetOrBakeIcon(assembler.Icon),
                    assembler.Assembler.Available ? AvailableRowBrush : UnavailableRowBrush,
                    count >= 10000000 ? count.ToString("0.##e0", DisplayCulture.Format) : count.ToString("N0", DisplayCulture.Format),
                    assembler.FriendlyName ?? "",
                    assemblerPower == 0 ? "-" : GraphicsStuff.DoubleToEnergy(assemblerPower, "W"), assemblerPower,
                    beaconPower == 0 ? "-" : GraphicsStuff.DoubleToEnergy(beaconPower, "W"), beaconPower));
            }
        }

        //Ports LoadUnfilteredBeaconList (reference §6, GraphSummaryForm.cs:167-214).
        private void LoadUnfilteredBeaconList(IEnumerable<IRecipeNodeViewModel> origin) {
            var beaconCounters = new Dictionary<BeaconQualityPair, int>();

            foreach (IRecipeNodeViewModel rnode in origin) {
                if (!rnode.SelectedBeacon)
                    continue;
                beaconCounters.TryAdd(rnode.SelectedBeacon, 0);
                beaconCounters[rnode.SelectedBeacon] += rnode.GetTotalBeacons();
            }

            IEnumerable<BeaconQualityPair> sortedBeacons = beaconCounters.Keys
                .OrderByDescending(b => b.Beacon!.Available)
                .ThenBy(b => b.Beacon!.FriendlyName, StringComparer.Ordinal)
                .ThenBy(b => b.Quality!.Level)
                .ThenBy(b => b.Quality!.FriendlyName, StringComparer.Ordinal);

            foreach (BeaconQualityPair beacon in sortedBeacons) {
                int count = beaconCounters[beacon];
                double beaconPowerConsumption = count * (beacon.Beacon!.GetEnergyConsumption(beacon.Quality!) + beacon.Beacon!.GetEnergyDrain());
                unfilteredBeaconList.Add(new BeaconRow(
                    beacon,
                    beacon.Beacon!.Name + ":" + beacon.Quality!.Name,
                    GetOrBakeIcon(beacon.Icon),
                    beacon.Beacon!.Available ? AvailableRowBrush : UnavailableRowBrush,
                    count.ToString(DisplayCulture.Format),
                    beacon.FriendlyName ?? "",
                    count == 0 ? "-" : GraphicsStuff.DoubleToEnergy(beaconPowerConsumption, "W"), beaconPowerConsumption));
            }
        }

        //Ports LoadUnfilteredItemLists (reference §6, GraphSummaryForm.cs:216-299): unlinked recipe inputs
        //go to In(x link), linked ones to Consumed; recipe outputs always add to Produced, unlinked ones
        //also to Out(x link), overproduced ones add the overflow to Overprod. - these three checks are
        //independent (a linked output can still overproduce). Suppliers add to In, consumers add to Out.
        private void LoadUnfilteredItemLists(IEnumerable<INodeViewModel> nodes, bool fluids, List<ItemRow> destination) {
            var itemCounters = new Dictionary<ItemQualityPair, ItemCounter>();

            ItemCounter CounterFor(ItemQualityPair pair) {
                if (!itemCounters.TryGetValue(pair, out ItemCounter? counter)) {
                    counter = new ItemCounter(0, 0, 0, 0, 0, 0, 0);
                    itemCounters[pair] = counter;
                }
                return counter;
            }

            foreach (INodeViewModel node in nodes) {
                if (node is IRecipeNodeViewModel recipeNode) {
                    foreach (ItemQualityPair input in recipeNode.Inputs.Where(i => fluids.Equals(i.Item is IFluid))) {
                        double consumeRate = recipeNode.GetConsumeRate(input);
                        if (consumeRate > 0) {
                            ItemCounter counter = CounterFor(input);
                            if (!recipeNode.InputLinks.Any(l => l.Item == input))
                                counter.InputUnlinked += consumeRate;
                            else
                                counter.Consumption += consumeRate;
                        }
                    }

                    foreach (ItemQualityPair output in recipeNode.Outputs.Where(i => fluids.Equals(i.Item is IFluid))) {
                        double supplyRate = recipeNode.GetSupplyRate(output);
                        if (supplyRate <= 0)
                            continue;

                        bool isOverProduced = recipeNode.IsOverproducing(output);
                        double supplyUsedRate = isOverProduced ? recipeNode.GetSupplyUsedRate(output) : supplyRate;

                        ItemCounter counter = CounterFor(output);
                        if (!recipeNode.OutputLinks.Any(l => l.Item == output))
                            counter.OutputUnlinked += supplyRate;
                        counter.Production += supplyRate;
                        if (isOverProduced)
                            counter.OutputOverflow += supplyRate - supplyUsedRate;
                    }
                } else if (node is ISupplierNodeViewModel sNode && fluids.Equals(sNode.SuppliedItem.Item is IFluid)) {
                    CounterFor(sNode.SuppliedItem).Input += sNode.ActualRate;
                } else if (node is IConsumerNodeViewModel cNode && fluids.Equals(cNode.ConsumedItem.Item is IFluid)) {
                    CounterFor(cNode.ConsumedItem).Output += cNode.ActualRate;
                }
            }

            IEnumerable<ItemQualityPair> sortedItems = itemCounters.Keys
                .OrderBy(p => p.Item!.FriendlyName, StringComparer.Ordinal)
                .ThenBy(p => p.Quality!.Level)
                .ThenBy(p => p.Quality!.FriendlyName, StringComparer.Ordinal);

            foreach (ItemQualityPair item in sortedItems) {
                ItemCounter counter = itemCounters[item];
                destination.Add(new ItemRow(
                    item,
                    item.Item!.Name + ":" + item.Quality!.Name,
                    GetOrBakeIcon(item.Icon),
                    item.Item!.Available ? AvailableRowBrush : UnavailableRowBrush,
                    item.FriendlyName ?? "",
                    counter.Input == 0 ? "-" : GraphicsStuff.DoubleToString(counter.Input), counter.Input,
                    counter.InputUnlinked == 0 ? "-" : GraphicsStuff.DoubleToString(counter.InputUnlinked), counter.InputUnlinked,
                    counter.Output == 0 ? "-" : GraphicsStuff.DoubleToString(counter.Output), counter.Output,
                    counter.OutputUnlinked == 0 ? "-" : GraphicsStuff.DoubleToString(counter.OutputUnlinked), counter.OutputUnlinked,
                    counter.OutputOverflow == 0 ? "-" : GraphicsStuff.DoubleToString(counter.OutputOverflow), counter.OutputOverflow,
                    counter.Production == 0 ? "-" : GraphicsStuff.DoubleToString(counter.Production), counter.Production,
                    counter.Consumption == 0 ? "-" : GraphicsStuff.DoubleToString(counter.Consumption), counter.Consumption));
            }
        }

        //Ports LoadUnfilteredKeyNodesList (reference §6, GraphSummaryForm.cs:301-358): recipe-node rows show
        //Throughput="-"/Factories=ActualSetValue, every other node type shows the reverse.
        private void LoadUnfilteredKeyNodesList(IEnumerable<INodeViewModel> origin) {
            foreach (INodeViewModel node in origin) {
                SKBitmap? icon;
                string? nodeText;
                string nodeType;
                if (node is IConsumerNodeViewModel cNode) {
                    icon = cNode.ConsumedItem.Icon;
                    nodeText = cNode.ConsumedItem.FriendlyName;
                    nodeType = "Consumer";
                } else if (node is ISupplierNodeViewModel sNode) {
                    icon = sNode.SuppliedItem.Icon;
                    nodeText = sNode.SuppliedItem.FriendlyName;
                    nodeType = "Supplier";
                } else if (node is IPassthroughNodeViewModel pNode) {
                    icon = pNode.PassthroughItem.Icon;
                    nodeText = pNode.PassthroughItem.FriendlyName;
                    nodeType = "Passthrough";
                } else if (node is IRecipeNodeViewModel rNode) {
                    icon = rNode.BaseRecipe.Icon;
                    nodeText = rNode.BaseRecipe.FriendlyName;
                    nodeType = "Recipe";
                } else if (node is ISpoilNodeViewModel spNode) {
                    icon = spNode.InputItem.Icon;
                    nodeText = spNode.InputItem.FriendlyName + " spoiling";
                    nodeType = "Spoil";
                } else if (node is IPlantNodeViewModel plNode) {
                    icon = plNode.Seed.Icon;
                    nodeText = plNode.Seed.FriendlyName + " planting";
                    nodeType = "Plant";
                } else
                    continue;

                (string throughputText, double throughputValue, string factoriesText, double factoriesValue) = node is IRecipeNodeViewModel rrNode
                    ? ("-", 0.0, GraphicsStuff.DoubleToString(rrNode.ActualSetValue), rrNode.ActualSetValue)
                    : (GraphicsStuff.DoubleToString(node.ActualRate), node.ActualRate, "-", 0.0);

                unfilteredKeyNodesList.Add(new KeyNodeRow(node, nodeText ?? "", GetOrBakeIcon(icon), nodeType, nodeText ?? "", node.KeyNodeTitle, throughputText, throughputValue, factoriesText, factoriesValue));
            }
        }

        //-------------------------------------------------------------------------------------------------------Filter functions

        //Ports ListViewItemRowContainsFilter (reference §6, GraphSummaryForm.cs:362-376): summary rows carry
        //value-type tags, not IDataObjectBase, so the filter matches visible cell text instead.
        private static bool RowContainsFilter(IEnumerable<string> cells, string filterLower) =>
            string.IsNullOrEmpty(filterLower) || cells.Any(c => c.Contains(filterLower, StringComparison.OrdinalIgnoreCase));

        private void UpdateFilteredBuildingLists() {
            string filter = buildingsFilterTextBox.Text ?? "";
            assemblerListView.ItemsSource = FilterBuildingRows(unfilteredAssemblerList, filter);
            minerListView.ItemsSource = FilterBuildingRows(unfilteredMinerList, filter);
            powerListView.ItemsSource = FilterBuildingRows(unfilteredPowerList, filter);
            beaconListView.ItemsSource = FilterBeaconRows(unfilteredBeaconList, filter);
        }

        private static List<BuildingRow> FilterBuildingRows(List<BuildingRow> unfiltered, string filter) =>
            [.. unfiltered.Where(r => RowContainsFilter([r.CountText, r.Name, r.PowerText, r.PowerBText], filter))];

        private static List<BeaconRow> FilterBeaconRows(List<BeaconRow> unfiltered, string filter) =>
            [.. unfiltered.Where(r => RowContainsFilter([r.CountText, r.Name, r.PowerBText], filter))];

        //Ports UpdateFilteredItemsList (reference §6, GraphSummaryForm.cs:398-432).
        private void UpdateFilteredItemsLists() {
            itemsListView.ItemsSource = FilterItemRows(unfilteredItemsList);
            fluidsListView.ItemsSource = FilterItemRows(unfilteredFluidsList);
        }

        private List<ItemRow> FilterItemRows(List<ItemRow> unfiltered) {
            string filter = itemsFilterTextBox.Text ?? "";
            bool includeInputs = inputFilterCheckBox.IsChecked == true;
            bool includeInputUnlinked = inputUnlinkedFilterCheckBox.IsChecked == true;
            bool includeOutputs = outputFilterCheckBox.IsChecked == true;
            bool includeOutputsUnlinked = outputUnlinkedFilterCheckBox.IsChecked == true;
            bool includeOutputsOverflow = outputOverproducedFilterCheckBox.IsChecked == true;
            bool includeProduced = productionFilterCheckBox.IsChecked == true;
            bool includeConsumed = consumptionFilterCheckBox.IsChecked == true;

            return [.. unfiltered.Where(r =>
                RowContainsFilter([r.Name, r.InText, r.InULText, r.OutText, r.OutULText, r.OverprodText, r.ProducedText, r.ConsumedText], filter) &&
                ((includeInputs && r.InText != "-") ||
                 (includeInputUnlinked && r.InULText != "-") ||
                 (includeOutputs && r.OutText != "-") ||
                 (includeOutputsUnlinked && r.OutULText != "-") ||
                 (includeOutputsOverflow && r.OverprodText != "-") ||
                 (includeProduced && r.ProducedText != "-") ||
                 (includeConsumed && r.ConsumedText != "-")))];
        }

        //Ports UpdateFilteredKeyNodesList (reference §6, GraphSummaryForm.cs:434-456).
        private void UpdateFilteredKeyNodesList() {
            string filter = keyNodesFilterTextBox.Text ?? "";
            bool includeSuppliers = supplierNodeFilterCheckBox.IsChecked == true;
            bool includeConsumers = consumerNodeFilterCheckBox.IsChecked == true;
            bool includePassthrough = passthroughNodeFilterCheckBox.IsChecked == true;
            bool includeRecipe = recipeNodeFilterCheckBox.IsChecked == true;

            keyNodesListView.ItemsSource = unfilteredKeyNodesList.Where(r =>
                (string.IsNullOrEmpty(filter) || r.TypeText.Contains(filter, StringComparison.OrdinalIgnoreCase) || r.DetailsText.Contains(filter, StringComparison.OrdinalIgnoreCase) || r.TitleText.Contains(filter, StringComparison.OrdinalIgnoreCase)) &&
                ((includeSuppliers && r.Tag is ISupplierNodeViewModel) ||
                 (includeConsumers && r.Tag is IConsumerNodeViewModel) ||
                 (includePassthrough && r.Tag is IPassthroughNodeViewModel) ||
                 (includeRecipe && r.Tag is IRecipeNodeViewModel))).ToList();
        }

        //-------------------------------------------------------------------------------------------------------Column sort functions

        //Ports BuildingListView_ColumnSort (reference §6, GraphSummaryForm.cs:485-511): column 0 (#) parses
        //its own text as the sort key, column 1 (Name) is a string compare, columns 2+ use the numeric tag.
        internal void SortBuildingColumn(List<BuildingRow> unfiltered, ListBox owner, int column) {
            int reverse = lastSortOrder[owner] == column + 1 ? -1 : 1;
            lastSortOrder[owner] = reverse * (column + 1);

            unfiltered.Sort((a, b) => {
                int result = column switch {
                    0 => -double.Parse(a.CountText, DisplayCulture.Format).CompareTo(double.Parse(b.CountText, DisplayCulture.Format)),
                    1 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                    2 => -a.PowerValue.CompareTo(b.PowerValue),
                    _ => -a.PowerBValue.CompareTo(b.PowerBValue),
                };
                if (result == 0)
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (result == 0)
                    result = string.Compare(a.RowKey, b.RowKey, StringComparison.Ordinal);
                return result * reverse;
            });
            UpdateFilteredBuildingLists();
        }

        internal void SortBeaconColumn(int column) {
            int reverse = lastSortOrder[beaconListView] == column + 1 ? -1 : 1;
            lastSortOrder[beaconListView] = reverse * (column + 1);

            unfilteredBeaconList.Sort((a, b) => {
                int result = column switch {
                    0 => -double.Parse(a.CountText, DisplayCulture.Format).CompareTo(double.Parse(b.CountText, DisplayCulture.Format)),
                    1 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                    _ => -a.PowerBValue.CompareTo(b.PowerBValue),
                };
                if (result == 0)
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (result == 0)
                    result = string.Compare(a.RowKey, b.RowKey, StringComparison.Ordinal);
                return result * reverse;
            });
            UpdateFilteredBuildingLists();
        }

        //Ports ItemListView_ColumnSort (reference §6, GraphSummaryForm.cs:516-536).
        internal void SortItemColumn(List<ItemRow> unfiltered, ListBox owner, int column) {
            int reverse = lastSortOrder[owner] == column + 1 ? -1 : 1;
            lastSortOrder[owner] = reverse * (column + 1);

            unfiltered.Sort((a, b) => {
                int result = column == 0
                    ? string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                    : -ItemColumnValue(a, column).CompareTo(ItemColumnValue(b, column));
                if (result == 0)
                    result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (result == 0)
                    result = string.Compare(a.RowKey, b.RowKey, StringComparison.Ordinal);
                return result * reverse;
            });
            UpdateFilteredItemsLists();
        }

        private static double ItemColumnValue(ItemRow row, int column) => column switch {
            1 => row.InValue,
            2 => row.InULValue,
            3 => row.OutValue,
            4 => row.OutULValue,
            5 => row.OverprodValue,
            6 => row.ProducedValue,
            _ => row.ConsumedValue,
        };

        //Ports KeyNodesListView_ColumnClick (reference §6, GraphSummaryForm.cs:538-578): column 2 (Node
        //Title) natural-sorts digit runs so "Node 2" sorts before "Node 10".
        internal void SortKeyNodesColumn(int column) {
            const int maxDigits = 20;
            var naturalSortCache = new Dictionary<string, string>();
            string NaturalKey(string s) {
                if (!naturalSortCache.TryGetValue(s, out string? key)) {
                    key = ComparerRegex.Replace(s.ToLowerInvariant(), m => m.Value.PadLeft(maxDigits, '0'));
                    naturalSortCache[s] = key;
                }
                return key;
            }
            int NaturalCompare(string a, string b) => string.Compare(NaturalKey(a), NaturalKey(b), StringComparison.Ordinal);

            int reverse = lastSortOrder[keyNodesListView] == column + 1 ? -1 : 1;
            lastSortOrder[keyNodesListView] = reverse * (column + 1);

            unfilteredKeyNodesList.Sort((a, b) => {
                int result = column switch {
                    2 => NaturalCompare(a.TitleText, b.TitleText),
                    0 => string.Compare(a.TypeText, b.TypeText, StringComparison.OrdinalIgnoreCase),
                    1 => string.Compare(a.DetailsText, b.DetailsText, StringComparison.OrdinalIgnoreCase),
                    3 => -a.ThroughputValue.CompareTo(b.ThroughputValue),
                    _ => -a.FactoriesValue.CompareTo(b.FactoriesValue),
                };
                if (result == 0 && column != 2)
                    result = NaturalCompare(a.TitleText, b.TitleText);
                if (result == 0 && column != 0)
                    result = string.Compare(a.TypeText, b.TypeText, StringComparison.OrdinalIgnoreCase);
                if (result == 0 && column != 1)
                    result = string.Compare(a.DetailsText, b.DetailsText, StringComparison.OrdinalIgnoreCase);
                if (result == 0 && a.Tag is INodeViewModel nodeA && b.Tag is INodeViewModel nodeB)
                    result = nodeA.Id.Value.CompareTo(nodeB.Id.Value);
                return result * reverse;
            });
            UpdateFilteredKeyNodesList();
        }

        //-------------------------------------------------------------------------------------------------------Export CSV

        private static readonly string[] BuildingsExportAssemblerHeader = ["#", "Assembler", "Electrical power consumed by assemblers (in W)", "Electrical power consumed by beacons (in W)"];
        private static readonly string[] BuildingsExportMinerHeader = ["#", "Miner", "Electrical power consumed by assemblers (in W)", "Electrical power consumed by beacons (in W)"];
        private static readonly string[] BuildingsExportPowerHeader = ["#", "Power Building", "Electrical power generated (in W)", "Electrical power consumed (in W)"];
        private static readonly string[] BuildingsExportBeaconHeader = ["#", "Beacon", "Electrical power consumed by beacons (in W)"];

        //Ports ExportCSV (reference §6, GraphSummaryForm.cs:615-648): dumps the currently FILTERED rows of
        //each sub-list, not the unfiltered set - the filtered lists reassigned onto ItemsSource above are
        //exactly what's on screen, so we read straight off there.
        internal Task ExportBuildingsCsvAsync() => ExportCsvAsync(
            [BuildingRowsAsCells(assemblerListView), BuildingRowsAsCells(minerListView), BuildingRowsAsCells(powerListView), BeaconRowsAsCells()],
            [BuildingsExportAssemblerHeader, BuildingsExportMinerHeader, BuildingsExportPowerHeader, BuildingsExportBeaconHeader]);

        internal Task ExportItemsCsvAsync() => ExportCsvAsync(
            [ItemRowsAsCells(itemsListView), ItemRowsAsCells(fluidsListView)],
            [
                ["Item", "Input (per " + rateString + ")", "Input through un-linked recipe ingredients (per " + rateString + ")", "Output (per " + rateString + ")", "Output through un-linked recipe products (per " + rateString + ")", "Output through overproduction (per " + rateString + ")", "Produced by recipe nodes (per " + rateString + ")", "Consumed by recipe nodes (per " + rateString + ")"],
                ["Fluid", "Input (per " + rateString + ")", "Input through un-linked recipe ingredients (per " + rateString + ")", "Output (per " + rateString + ")", "Output through un-linked recipe products (per " + rateString + ")", "Output through overproduction (per " + rateString + ")", "Produced by recipe nodes (per " + rateString + ")", "Consumed by recipe nodes (per " + rateString + ")"],
            ]);

        internal Task ExportKeyNodesCsvAsync() => ExportCsvAsync(
            [KeyNodeRowsAsCells()],
            [["Node Type", "Node Details (item / recipe name)", "Node Title", "Throughput (for non-recipe nodes) (per " + rateString + ")", "Building Count (for recipe nodes)"]]);

        private static List<string[]> BuildingRowsAsCells(ListBox owner) =>
            [.. (owner.ItemsSource as IEnumerable<BuildingRow> ?? []).Select(r => new[] { r.CountText, r.Name, r.PowerText, r.PowerBText })];

        private List<string[]> BeaconRowsAsCells() =>
            [.. (beaconListView.ItemsSource as IEnumerable<BeaconRow> ?? []).Select(r => new[] { r.CountText, r.Name, r.PowerBText })];

        private static List<string[]> ItemRowsAsCells(ListBox owner) =>
            [.. (owner.ItemsSource as IEnumerable<ItemRow> ?? []).Select(r => new[] { r.Name, r.InText, r.InULText, r.OutText, r.OutULText, r.OverprodText, r.ProducedText, r.ConsumedText })];

        private List<string[]> KeyNodeRowsAsCells() =>
            [.. (keyNodesListView.ItemsSource as IEnumerable<KeyNodeRow> ?? []).Select(r => new[] { r.TypeText, r.DetailsText, r.TitleText, r.ThroughputText, r.FactoriesText })];

        private async Task ExportCsvAsync(List<string[]>[] rowLists, string[][] columnNames) {
            string? path = await (SaveFilePathStub?.Invoke() ?? RealPickSaveFilePathAsync()).ConfigureAwait(true);
            if (path is null)
                return;

            var csvLines = new List<string[]>();
            for (int i = 0; i < rowLists.Length; i++) {
                csvLines.Add(columnNames[i]);
                foreach (string[] row in rowLists[i])
                    csvLines.Add([.. row.Select(cell => cell.Replace(",", "").Replace("\n", "; ").Replace("\t", ""))]);
                csvLines.Add([""]);
            }
            if (csvLines.Count > 0)
                csvLines.RemoveAt(csvLines.Count - 1);

            var csvBuilder = new StringBuilder();
            foreach (string[] line in csvLines)
                csvBuilder.AppendLine(string.Join(",", line));

            Utf8File.WriteAllText(path, csvBuilder.ToString());
            LastCsvWritten = csvBuilder.ToString();
        }

        private async Task<string?> RealPickSaveFilePathAsync() {
            if (StorageProvider is not IStorageProvider storage)
                return null;

            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = "Export CSV",
                SuggestedFileName = "foreman data.csv",
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            }).ConfigureAwait(true);
            return file?.Path.LocalPath;
        }

        //-------------------------------------------------------------------------------------------------------Icon baking

        //Dedups baked icons by source SKBitmap reference (same intent as SettingsWindow.GetOrBakeIcon).
        private Bitmap? GetOrBakeIcon(SKBitmap? icon) {
            if (icon is null)
                return null;
            if (!bakedIconCache.TryGetValue(icon, out Bitmap? baked)) {
                baked = BakeIcon(icon);
                bakedIconCache[icon] = baked;
            }
            return baked;
        }

        private static Bitmap BakeIcon(SKBitmap icon) {
            const int size = 24;
            using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var paint = new SKPaint { IsAntialias = true };
            surface.Canvas.DrawBitmap(icon, new SKRect(0, 0, size, size), paint);
            using SKPixmap pixmap = surface.PeekPixels();
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixmap.Info.Width, pixmap.Info.Height), new Vector(96, 96), pixmap.RowBytes);
        }

        //-------------------------------------------------------------------------------------------------------Test-only seams

        internal TabControl MainTabControlControl => mainTabControl;
        internal TabItem ItemsFluidsTabItemControl => itemsFluidsTabItem;

        internal TextBox BuildingsFilterTextBoxControl => buildingsFilterTextBox;
        internal TextBlock BuildingCountLabelControl => buildingCountLabel;
        internal TextBlock BeaconCountLabelControl => beaconCountLabel;
        internal TextBlock PowerConsumptionLabelControl => powerConsumptionLabel;
        internal TextBlock PowerProductionLabelControl => powerProductionLabel;
        internal TextBlock PowerNetLabelControl => powerNetLabel;
        internal ListBox AssemblerListViewControl => assemblerListView;
        internal ListBox MinerListViewControl => minerListView;
        internal ListBox PowerListViewControl => powerListView;
        internal ListBox BeaconListViewControl => beaconListView;

        internal TextBox ItemsFilterTextBoxControl => itemsFilterTextBox;
        internal CheckBox InputFilterCheckBoxControl => inputFilterCheckBox;
        internal CheckBox InputUnlinkedFilterCheckBoxControl => inputUnlinkedFilterCheckBox;
        internal CheckBox OutputFilterCheckBoxControl => outputFilterCheckBox;
        internal CheckBox OutputUnlinkedFilterCheckBoxControl => outputUnlinkedFilterCheckBox;
        internal CheckBox OutputOverproducedFilterCheckBoxControl => outputOverproducedFilterCheckBox;
        internal CheckBox ProductionFilterCheckBoxControl => productionFilterCheckBox;
        internal CheckBox ConsumptionFilterCheckBoxControl => consumptionFilterCheckBox;
        internal ListBox ItemsListViewControl => itemsListView;
        internal ListBox FluidsListViewControl => fluidsListView;

        internal TextBox KeyNodesFilterTextBoxControl => keyNodesFilterTextBox;
        internal CheckBox SupplierNodeFilterCheckBoxControl => supplierNodeFilterCheckBox;
        internal CheckBox ConsumerNodeFilterCheckBoxControl => consumerNodeFilterCheckBox;
        internal CheckBox PassthroughNodeFilterCheckBoxControl => passthroughNodeFilterCheckBox;
        internal CheckBox RecipeNodeFilterCheckBoxControl => recipeNodeFilterCheckBox;
        internal ListBox KeyNodesListViewControl => keyNodesListView;

        internal List<BuildingRow> UnfilteredAssemblerList => unfilteredAssemblerList;
        internal List<BuildingRow> UnfilteredMinerList => unfilteredMinerList;
        internal List<BuildingRow> UnfilteredPowerList => unfilteredPowerList;
        internal List<BeaconRow> UnfilteredBeaconList => unfilteredBeaconList;
        internal List<ItemRow> UnfilteredItemsList => unfilteredItemsList;
        internal List<ItemRow> UnfilteredFluidsList => unfilteredFluidsList;
        internal List<KeyNodeRow> UnfilteredKeyNodesList => unfilteredKeyNodesList;

        internal void SimulateHeaderClick(string name) => this.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        internal void SimulateExportBuildingsClick() => Async.Fire(ExportBuildingsCsvAsync(), nameof(ExportBuildingsCsvAsync));
        internal void SimulateExportItemsClick() => Async.Fire(ExportItemsCsvAsync(), nameof(ExportItemsCsvAsync));
        internal void SimulateExportKeyNodesClick() => Async.Fire(ExportKeyNodesCsvAsync(), nameof(ExportKeyNodesCsvAsync));
    }
}
