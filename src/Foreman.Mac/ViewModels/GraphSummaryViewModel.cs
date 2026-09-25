using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    //Splits GraphSummaryWindow.axaml.cs's row-assembly off into its own VM: every
    //BuildingRow/BeaconRow/ItemRow/KeyNodeRow the window shows starts life here, rebuilt from scratch by
    //Refresh. Filter/sort UI plumbing, header-total labels, and CSV export all read window control state
    //directly and stay in the code-behind; RowContainsFilter/FilterBuildingRows/FilterBeaconRows moved over
    //too since they were already pure rows-in/rows-out helpers with no window dependency.
    internal sealed partial class GraphSummaryViewModel : ObservableObject {
        private sealed class ItemCounter(double i, double iu, double o, double ou, double oo, double p, double c) {
            public double Input { get; set; } = i;
            public double InputUnlinked { get; set; } = iu;
            public double Output { get; set; } = o;
            public double OutputUnlinked { get; set; } = ou;
            public double OutputOverflow { get; set; } = oo;
            public double Production { get; set; } = p;
            public double Consumption { get; set; } = c;
        }

        //Resolved fresh at the top of every Refresh() call (below) against the app's active theme variant -
        //not readonly, since a light-only literal here would silently break dark mode.
        private static IBrush AvailableRowBrush = new ImmutableSolidColorBrush(Color.FromRgb(255, 255, 255));
        private static IBrush UnavailableRowBrush = new ImmutableSolidColorBrush(Color.FromRgb(255, 192, 203));

        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

        [ObservableProperty]
        private IReadOnlyList<BuildingRow> assemblers = [];

        [ObservableProperty]
        private IReadOnlyList<BuildingRow> miners = [];

        [ObservableProperty]
        private IReadOnlyList<BuildingRow> powerBuildings = [];

        [ObservableProperty]
        private IReadOnlyList<BeaconRow> beacons = [];

        [ObservableProperty]
        private IReadOnlyList<ItemRow> items = [];

        [ObservableProperty]
        private IReadOnlyList<ItemRow> fluids = [];

        [ObservableProperty]
        private IReadOnlyList<KeyNodeRow> keyNodes = [];

        //Ports the constructor's four LoadUnfiltered* calls (reference GraphSummaryWindow.axaml.cs).
        //Every property is reassigned to a brand-new list on every call. GraphSummaryWindow never
        //binds to these properties; it copies them into its own fields and rebuilds fresh filtered lists
        //before each ItemsSource assignment, which is where the reference-equality short-circuit lives.
        public void Refresh(IEnumerable<INodeViewModel> nodes) {
            AvailableRowBrush = ThemeResources.ResolveBrush("Foreman.Color.ControlBackground", Color.FromRgb(255, 255, 255));
            UnavailableRowBrush = ThemeResources.ResolveBrush("Foreman.Color.StatusWarningBackground", Color.FromRgb(255, 192, 203));

            List<IRecipeNodeViewModel> recipeNodes = [.. nodes.OfType<IRecipeNodeViewModel>()];
            List<INodeViewModel> nodeList = [.. nodes];

            Assemblers = LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType == EntityType.Assembler));
            Miners = LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType is EntityType.Miner or EntityType.OffshorePump));
            PowerBuildings = LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor));

            Beacons = LoadUnfilteredBeaconList(recipeNodes.Where(r => r.SelectedBeacon));

            Items = LoadUnfilteredItemLists(nodeList, fluids: false);
            Fluids = LoadUnfilteredItemLists(nodeList, fluids: true);

            KeyNodes = LoadUnfilteredKeyNodesList(nodeList.Where(n => n.KeyNode));
        }

        //Ports LoadUnfilteredSelectedAssemblerList (reference upstream GraphSummaryForm.cs:134-165).
        private List<BuildingRow> LoadUnfilteredSelectedAssemblerList(IEnumerable<IRecipeNodeViewModel> origin) {
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

            var destination = new List<BuildingRow>();
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
            return destination;
        }

        //Ports LoadUnfilteredBeaconList (reference upstream GraphSummaryForm.cs:167-214).
        private List<BeaconRow> LoadUnfilteredBeaconList(IEnumerable<IRecipeNodeViewModel> origin) {
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

            var destination = new List<BeaconRow>();
            foreach (BeaconQualityPair beacon in sortedBeacons) {
                int count = beaconCounters[beacon];
                double beaconPowerConsumption = count * (beacon.Beacon!.GetEnergyConsumption(beacon.Quality!) + beacon.Beacon!.GetEnergyDrain());
                destination.Add(new BeaconRow(
                    beacon,
                    beacon.Beacon!.Name + ":" + beacon.Quality!.Name,
                    GetOrBakeIcon(beacon.Icon),
                    beacon.Beacon!.Available ? AvailableRowBrush : UnavailableRowBrush,
                    count.ToString(DisplayCulture.Format),
                    beacon.FriendlyName ?? "",
                    count == 0 ? "-" : GraphicsStuff.DoubleToEnergy(beaconPowerConsumption, "W"), beaconPowerConsumption));
            }
            return destination;
        }

        //Ports LoadUnfilteredItemLists (reference upstream GraphSummaryForm.cs:216-299): unlinked recipe
        //inputs go to In(x link), linked ones to Consumed; recipe outputs always add to Produced, unlinked
        //ones also to Out(x link), overproduced ones add the overflow to Overprod - these three checks are
        //independent (a linked output can still overproduce). Suppliers add to In, consumers add to Out.
        private List<ItemRow> LoadUnfilteredItemLists(IEnumerable<INodeViewModel> nodes, bool fluids) {
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

            var destination = new List<ItemRow>();
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
            return destination;
        }

        //Ports LoadUnfilteredKeyNodesList (reference upstream GraphSummaryForm.cs:301-358): recipe-node rows
        //show Throughput="-"/Factories=ActualSetValue, every other node type shows the reverse.
        private List<KeyNodeRow> LoadUnfilteredKeyNodesList(IEnumerable<INodeViewModel> origin) {
            var destination = new List<KeyNodeRow>();
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

                destination.Add(new KeyNodeRow(node, nodeText ?? "", GetOrBakeIcon(icon), nodeType, nodeText ?? "", node.KeyNodeTitle, throughputText, throughputValue, factoriesText, factoriesValue));
            }
            return destination;
        }

        //Dedups baked icons by source SKBitmap reference (same intent as SettingsWindow.GetOrBakeIcon).
        private Bitmap? GetOrBakeIcon(SKBitmap? icon) => icon is null ? null : IconBaker.GetOrBake(bakedIconCache, icon, 24);

        //-------------------------------------------------------------------------------------------------------Row filters

        //Ports ListViewItemRowContainsFilter (reference upstream GraphSummaryForm.cs:362-376): summary rows
        //carry value-type tags, not IDataObjectBase, so the filter matches visible cell text instead.
        internal static bool RowContainsFilter(IEnumerable<string> cells, string filterLower) =>
            string.IsNullOrEmpty(filterLower) || cells.Any(c => c.Contains(filterLower, StringComparison.OrdinalIgnoreCase));

        internal static List<BuildingRow> FilterBuildingRows(List<BuildingRow> unfiltered, string filter) =>
            [.. unfiltered.Where(r => RowContainsFilter([r.CountText, r.Name, r.PowerText, r.PowerBText], filter))];

        internal static List<BeaconRow> FilterBeaconRows(List<BeaconRow> unfiltered, string filter) =>
            [.. unfiltered.Where(r => RowContainsFilter([r.CountText, r.Name, r.PowerBText], filter))];
    }
}
