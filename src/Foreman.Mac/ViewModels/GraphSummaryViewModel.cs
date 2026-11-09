using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Views;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    internal sealed partial class GraphSummaryViewModel : ObservableObject {
        private sealed class ItemCounter(double i, double iu, double o, double ou, double oo, double p, double c) {
            public double Input { get; set; } = i;
            public double InputUnlinked { get; set; } = iu;
            public double Output { get; set; } = o;
            public double OutputUnlinked { get; set; } = ou;
            public double OutputOverflow { get; set; } = oo;
            public double Production { get; set; } = p;
            public double Consumption { get; set; } = c;
            public bool Stale { get; set; }
        }

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

        public double BuildingTotal { get; private set; }
        public double BeaconTotal { get; private set; }
        public double PowerConsumption { get; private set; }
        public double PowerProduction { get; private set; }

        public void Refresh(IEnumerable<INodeViewModel> nodes, ProductionGraph.RateUnit unit = ProductionGraph.RateUnit.Per1Sec) {
            List<IRecipeNodeViewModel> recipeNodes = [.. nodes.OfType<IRecipeNodeViewModel>()];
            List<INodeViewModel> nodeList = [.. nodes];

            Assemblers = LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType == EntityType.Assembler));
            Miners = LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType is EntityType.Miner or EntityType.OffshorePump));
            PowerBuildings = LoadUnfilteredSelectedAssemblerList(recipeNodes.Where(r => r.SelectedAssembler.Assembler.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor));

            Beacons = LoadUnfilteredBeaconList(recipeNodes.Where(r => r.SelectedBeacon));

            Items = LoadUnfilteredItemLists(nodeList, fluids: false, unit);
            Fluids = LoadUnfilteredItemLists(nodeList, fluids: true, unit);

            KeyNodes = LoadUnfilteredKeyNodesList(nodeList.Where(n => n.KeyNode), unit);

            BuildingTotal = recipeNodes.Sum(n => Math.Ceiling(n.ActualSetValue));
            BeaconTotal = recipeNodes.Sum(n => n.GetTotalBeacons());
            PowerConsumption = recipeNodes.Sum(n => n.GetTotalAssemblerElectricalConsumption() + n.GetTotalBeaconElectricalConsumption());
            PowerProduction = recipeNodes.Sum(n => n.GetTotalGeneratorElectricalProduction());
        }

        private List<BuildingRow> LoadUnfilteredSelectedAssemblerList(IEnumerable<IRecipeNodeViewModel> origin) {
            var buildingCounters = new Dictionary<AssemblerQualityPair, int>();
            var buildingElectricalPower = new Dictionary<AssemblerQualityPair, (double Assembler, double Beacon)>();
            HashSet<AssemblerQualityPair> staleBuildings = [];

            foreach (IRecipeNodeViewModel rnode in origin) {
                if (!buildingCounters.ContainsKey(rnode.SelectedAssembler)) {
                    buildingCounters[rnode.SelectedAssembler] = 0;
                    buildingElectricalPower[rnode.SelectedAssembler] = (0, 0);
                }
                if (rnode.SolveState == NodeSolveState.Stale)
                    staleBuildings.Add(rnode.SelectedAssembler);
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
                    count,
                    count >= 10000000 ? count.ToString("0.##e0", DisplayCulture.Format) : count.ToString("N0", DisplayCulture.Format),
                    assembler.FriendlyName ?? "",
                    SummaryTables.Power(assemblerPower), assemblerPower,
                    SummaryTables.Power(beaconPower), beaconPower,
                    staleBuildings.Contains(assembler)));
            }
            return destination;
        }

        private List<BeaconRow> LoadUnfilteredBeaconList(IEnumerable<IRecipeNodeViewModel> origin) {
            var beaconCounters = new Dictionary<BeaconQualityPair, int>();
            HashSet<BeaconQualityPair> staleBeacons = [];

            foreach (IRecipeNodeViewModel rnode in origin) {
                if (!rnode.SelectedBeacon)
                    continue;
                if (rnode.SolveState == NodeSolveState.Stale)
                    staleBeacons.Add(rnode.SelectedBeacon);
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
                    count,
                    count.ToString(DisplayCulture.Format),
                    beacon.FriendlyName ?? "",
                    SummaryTables.Power(beaconPowerConsumption), beaconPowerConsumption,
                    staleBeacons.Contains(beacon)));
            }
            return destination;
        }

        private List<ItemRow> LoadUnfilteredItemLists(IEnumerable<INodeViewModel> nodes, bool fluids, ProductionGraph.RateUnit unit) {
            var itemCounters = new Dictionary<ItemQualityPair, ItemCounter>();

            ItemCounter CounterFor(ItemQualityPair pair) {
                if (!itemCounters.TryGetValue(pair, out ItemCounter? counter)) {
                    counter = new ItemCounter(0, 0, 0, 0, 0, 0, 0);
                    itemCounters[pair] = counter;
                }
                return counter;
            }

            foreach (INodeViewModel node in nodes) {
                bool stale = node.SolveState == NodeSolveState.Stale;
                if (node is IRecipeNodeViewModel recipeNode) {
                    foreach (ItemQualityPair input in recipeNode.Inputs.Where(i => fluids.Equals(i.Item is IFluid))) {
                        double consumeRate = recipeNode.GetConsumeRate(input);
                        if (consumeRate > 0) {
                            ItemCounter counter = CounterFor(input);
                            counter.Stale |= stale;
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
                        counter.Stale |= stale;
                        if (!recipeNode.OutputLinks.Any(l => l.Item == output))
                            counter.OutputUnlinked += supplyRate;
                        counter.Production += supplyRate;
                        if (isOverProduced)
                            counter.OutputOverflow += supplyRate - supplyUsedRate;
                    }
                } else if (node is ISupplierNodeViewModel sNode && fluids.Equals(sNode.SuppliedItem.Item is IFluid)) {
                    ItemCounter counter = CounterFor(sNode.SuppliedItem);
                    counter.Input += sNode.ActualRate;
                    counter.Stale |= stale;
                } else if (node is IConsumerNodeViewModel cNode && fluids.Equals(cNode.ConsumedItem.Item is IFluid)) {
                    ItemCounter counter = CounterFor(cNode.ConsumedItem);
                    counter.Output += cNode.ActualRate;
                    counter.Stale |= stale;
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
                    item.FriendlyName ?? "",
                    SummaryTables.FormatRate(counter.Input, unit), counter.Input,
                    SummaryTables.FormatRate(counter.InputUnlinked, unit), counter.InputUnlinked,
                    SummaryTables.FormatRate(counter.Output, unit), counter.Output,
                    SummaryTables.FormatRate(counter.OutputUnlinked, unit), counter.OutputUnlinked,
                    SummaryTables.FormatRate(counter.OutputOverflow, unit), counter.OutputOverflow,
                    SummaryTables.FormatRate(counter.Production, unit), counter.Production,
                    SummaryTables.FormatRate(counter.Consumption, unit), counter.Consumption,
                    counter.Stale));
            }
            return destination;
        }

        private List<KeyNodeRow> LoadUnfilteredKeyNodesList(IEnumerable<INodeViewModel> origin, ProductionGraph.RateUnit unit) {
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
                    ? (SummaryTables.Zero, 0.0, SummaryTables.FormatCount(rrNode.ActualSetValue), rrNode.ActualSetValue)
                    : (SummaryTables.FormatRate(node.ActualRate, unit), node.ActualRate, SummaryTables.Zero, 0.0);

                destination.Add(new KeyNodeRow(node, nodeText ?? "", GetOrBakeIcon(icon), nodeType, nodeText ?? "", node.KeyNodeTitle, throughputText, throughputValue, factoriesText, factoriesValue, node.SolveState == NodeSolveState.Stale));
            }
            return destination;
        }

        private Bitmap? GetOrBakeIcon(SKBitmap? icon) => icon is null ? null : IconBaker.GetOrBake(bakedIconCache, icon, 24);
    }
}
