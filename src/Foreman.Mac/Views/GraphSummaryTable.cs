using Avalonia.Media.Imaging;
using Foreman.Graph;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Foreman.Mac.Views {
    internal enum SummaryTableId { Assemblers, Miners, Power, Beacons, Items, Fluids, KeyNodes }

    internal enum SummarySegment { Buildings, ItemsAndFluids, KeyNodes }

    internal sealed record SummaryColumn(string Header, bool Numeric);

    internal sealed class SummaryRow {
        public required string Key { get; init; }
        public required object Tag { get; init; }
        public Bitmap? Icon { get; init; }
        public required string[] Cells { get; init; }
        public string SearchText { get; init; } = "";
        public required double[] Values { get; init; }
        public RowStatus Status { get; init; }
        public bool IsStale { get; init; }
    }

    internal sealed class SummarySort(int column, bool ascending) {
        public int Column { get; } = column;
        public bool Ascending { get; } = ascending;
    }

    internal static class SummaryTables {
        public const string Zero = "-";

        public static readonly string[] ItemKinds = ["Input", "Unlinked input", "Output", "Unlinked output", "Overproduced", "Produced", "Consumed"];
        public static readonly string[] KeyNodeKinds = ["Supplier nodes", "Consumer nodes", "Passthrough nodes", "Recipe nodes"];

        private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

        private static readonly SummaryColumn[] AssemblerColumns = [new("Count", true), new("Name", false), new("Power", true), new("Beacon power", true)];
        private static readonly SummaryColumn[] PowerColumns = [new("Count", true), new("Name", false), new("Generated", true), new("Consumed", true)];
        private static readonly SummaryColumn[] BeaconColumns = [new("Count", true), new("Name", false), new("Power", true)];
        private static readonly SummaryColumn[] ItemColumns = [new("Item", false), .. ItemKinds.Select(k => new SummaryColumn(k, true))];
        private static readonly SummaryColumn[] FluidColumns = [new("Fluid", false), .. ItemKinds.Select(k => new SummaryColumn(k, true))];
        private static readonly SummaryColumn[] KeyNodeColumns = [new("Type", false), new("Details", false), new("Title", false), new("Throughput", true), new("Buildings", true)];

        public static IReadOnlyList<SummaryColumn> Columns(SummaryTableId id) => id switch {
            SummaryTableId.Assemblers or SummaryTableId.Miners => AssemblerColumns,
            SummaryTableId.Power => PowerColumns,
            SummaryTableId.Beacons => BeaconColumns,
            SummaryTableId.Items => ItemColumns,
            SummaryTableId.Fluids => FluidColumns,
            _ => KeyNodeColumns,
        };

        public static int NameColumn(SummaryTableId id) => id switch {
            SummaryTableId.Items or SummaryTableId.Fluids => 0,
            SummaryTableId.KeyNodes => 1,
            _ => 1,
        };

        public static int DefaultSortColumn(SummaryTableId id) => Columns(id).Select((c, i) => (c, i)).First(x => x.c.Numeric).i;

        public static SummarySort DefaultSort(SummaryTableId id) => new(DefaultSortColumn(id), false);

        public static SummarySort Toggle(SummaryTableId id, SummarySort? current, int column) =>
            current is not null && current.Column == column
                ? new SummarySort(column, !current.Ascending)
                : new SummarySort(column, !Columns(id)[column].Numeric);

        public static IReadOnlyList<SummaryTableId> Tables(SummarySegment segment) => segment switch {
            SummarySegment.Buildings => [SummaryTableId.Assemblers, SummaryTableId.Miners, SummaryTableId.Power, SummaryTableId.Beacons],
            SummarySegment.ItemsAndFluids => [SummaryTableId.Items, SummaryTableId.Fluids],
            _ => [SummaryTableId.KeyNodes],
        };

        public static SummaryRow FromBuilding(BuildingRow r) => new() {
            Key = r.RowKey, Tag = r.Tag, Icon = r.Icon, IsStale = r.IsStale,
            SearchText = r.Name, Cells = [r.CountText, r.Name, r.PowerText, r.PowerBText],
            Values = [r.CountValue, 0, r.PowerValue, r.PowerBValue],
        };

        public static SummaryRow FromBeacon(BeaconRow r) => new() {
            Key = r.RowKey, Tag = r.Tag, Icon = r.Icon, IsStale = r.IsStale,
            SearchText = r.Name, Cells = [r.CountText, r.Name, r.PowerBText],
            Values = [r.CountValue, 0, r.PowerBValue],
        };

        public static SummaryRow FromItem(ItemRow r) => new() {
            Key = r.RowKey, Tag = r.Tag, Icon = r.Icon, IsStale = r.IsStale, Status = r.Status,
            SearchText = r.Name, Cells = [r.Name, r.InText, r.InULText, r.OutText, r.OutULText, r.OverprodText, r.ProducedText, r.ConsumedText],
            Values = [0, r.InValue, r.InULValue, r.OutValue, r.OutULValue, r.OverprodValue, r.ProducedValue, r.ConsumedValue],
        };

        public static SummaryRow FromKeyNode(KeyNodeRow r) => new() {
            Key = "n" + ((INodeViewModel)r.Tag).Id.Value, Tag = r.Tag, Icon = r.Icon, IsStale = r.IsStale,
            SearchText = r.DetailsText, Cells = [r.TypeText, r.DetailsText, r.TitleText, r.ThroughputText, r.FactoriesText],
            Values = [0, 0, 0, r.ThroughputValue, r.FactoriesValue],
        };

        public static List<SummaryRow> Build(SummaryTableId id, GraphSummaryViewModel vm) => id switch {
            SummaryTableId.Assemblers => [.. vm.Assemblers.Select(FromBuilding)],
            SummaryTableId.Miners => [.. vm.Miners.Select(FromBuilding)],
            SummaryTableId.Power => [.. vm.PowerBuildings.Select(FromBuilding)],
            SummaryTableId.Beacons => [.. vm.Beacons.Select(FromBeacon)],
            SummaryTableId.Items => [.. vm.Items.Select(FromItem)],
            SummaryTableId.Fluids => [.. vm.Fluids.Select(FromItem)],
            _ => [.. vm.KeyNodes.Select(FromKeyNode)],
        };

        public static void Sort(List<SummaryRow> rows, SummaryTableId id, SummarySort sort) {
            int direction = sort.Ascending ? 1 : -1;
            bool numeric = Columns(id)[sort.Column].Numeric;
            rows.Sort((a, b) => {
                int result = numeric
                    ? a.Values[sort.Column].CompareTo(b.Values[sort.Column])
                    : Compare(id, sort.Column, a, b);
                result *= direction;
                if (result == 0)
                    result = TieBreak(id, sort.Column, a, b);
                return result;
            });
        }

        private static int Compare(SummaryTableId id, int column, SummaryRow a, SummaryRow b) =>
            id == SummaryTableId.KeyNodes && column == 2
                ? NaturalCompare(a.Cells[2], b.Cells[2])
                : string.Compare(a.Cells[column], b.Cells[column], StringComparison.OrdinalIgnoreCase);

        private static int TieBreak(SummaryTableId id, int column, SummaryRow a, SummaryRow b) {
            int result = 0;
            if (id == SummaryTableId.KeyNodes) {
                if (column != 2)
                    result = NaturalCompare(a.Cells[2], b.Cells[2]);
                if (result == 0 && column != 0)
                    result = string.Compare(a.Cells[0], b.Cells[0], StringComparison.OrdinalIgnoreCase);
                if (result == 0 && column != 1)
                    result = string.Compare(a.Cells[1], b.Cells[1], StringComparison.OrdinalIgnoreCase);
                if (result == 0 && a.Tag is INodeViewModel nodeA && b.Tag is INodeViewModel nodeB)
                    result = nodeA.Id.Value.CompareTo(nodeB.Id.Value);
                return result;
            }
            int name = NameColumn(id);
            result = string.Compare(a.Cells[name], b.Cells[name], StringComparison.OrdinalIgnoreCase);
            return result != 0 ? result : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
        }

        private static int NaturalCompare(string a, string b) =>
            string.Compare(NaturalKey(a), NaturalKey(b), StringComparison.Ordinal);

        private static string NaturalKey(string s) =>
            Digits.Replace(s.ToLowerInvariant(), m => m.Value.PadLeft(20, '0'));

        public static bool MatchesQuery(SummaryTableId id, SummaryRow row, string query) {
            if (string.IsNullOrEmpty(query))
                return true;
            int searched = id == SummaryTableId.KeyNodes ? 3 : row.Cells.Length;
            return row.Cells.Take(searched).Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        public static bool PassesKinds(SummaryTableId id, SummaryRow row, bool[] kinds) {
            if (id is SummaryTableId.Items or SummaryTableId.Fluids) {
                for (int i = 0; i < ItemKinds.Length; i++)
                    if (kinds[i] && row.Cells[i + 1] != Zero)
                        return true;
                return false;
            }
            if (id == SummaryTableId.KeyNodes)
                return (kinds[0] && row.Tag is ISupplierNodeViewModel)
                    || (kinds[1] && row.Tag is IConsumerNodeViewModel)
                    || (kinds[2] && row.Tag is IPassthroughNodeViewModel)
                    || (kinds[3] && row.Tag is IRecipeNodeViewModel);
            return true;
        }

        public static string[] CsvHeader(SummaryTableId id, string rate) => id switch {
            SummaryTableId.Assemblers => ["#", "Assembler", "Electrical power consumed by assemblers (in W)", "Electrical power consumed by beacons (in W)"],
            SummaryTableId.Miners => ["#", "Miner", "Electrical power consumed by assemblers (in W)", "Electrical power consumed by beacons (in W)"],
            SummaryTableId.Power => ["#", "Power Building", "Electrical power generated (in W)", "Electrical power consumed (in W)"],
            SummaryTableId.Beacons => ["#", "Beacon", "Electrical power consumed by beacons (in W)"],
            SummaryTableId.Items or SummaryTableId.Fluids => [id == SummaryTableId.Items ? "Item" : "Fluid", $"Input (per {rate})", $"Input through un-linked recipe ingredients (per {rate})", $"Output (per {rate})", $"Output through un-linked recipe products (per {rate})", $"Output through overproduction (per {rate})", $"Produced by recipe nodes (per {rate})", $"Consumed by recipe nodes (per {rate})"],
            _ => ["Node Type", "Node Details (item / recipe name)", "Node Title", $"Throughput (for non-recipe nodes) (per {rate})", "Building Count (for recipe nodes)"],
        };

        public static string AutomationName(SummaryTableId id, SummaryRow row, string rate) {
            IReadOnlyList<SummaryColumn> columns = Columns(id);
            int name = NameColumn(id);
            List<string> parts = [row.Cells[name]];
            for (int i = 0; i < columns.Count; i++) {
                if (i == name || row.Cells[i] == Zero)
                    continue;
                string header = columns[i].Header.ToLowerInvariant();
                bool perRate = id is SummaryTableId.Items or SummaryTableId.Fluids || id == SummaryTableId.KeyNodes && i == 3;
                parts.Add(perRate ? $"{header} {row.Cells[i]} per {rate}" : $"{header} {row.Cells[i]}");
            }
            if (row.Status == RowStatus.Error)
                parts.Add("unlinked");
            return string.Join(", ", parts);
        }
    }
}
