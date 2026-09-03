using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Models;
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
        //Resolved fresh in the constructor below against the app's active theme variant, ahead of
        //ViewModel.Refresh(nodes) - the row records' Foreground initializers read this field at construction.
        internal static IBrush RowForegroundBrush = new ImmutableSolidColorBrush(Color.FromRgb(0, 0, 0));
        private static readonly Regex ComparerRegex = new(@"\d+", RegexOptions.Compiled);

        internal GraphSummaryViewModel ViewModel { get; } = new GraphSummaryViewModel();

        private readonly List<BuildingRow> unfilteredAssemblerList;
        private readonly List<BuildingRow> unfilteredMinerList;
        private readonly List<BuildingRow> unfilteredPowerList;
        private readonly List<BeaconRow> unfilteredBeaconList;
        private readonly List<ItemRow> unfilteredItemsList;
        private readonly List<ItemRow> unfilteredFluidsList;
        private readonly List<KeyNodeRow> unfilteredKeyNodesList;

        private readonly Dictionary<ListBox, int> lastSortOrder = [];

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

            RowForegroundBrush = ThemeResources.ResolveBrush("Foreman.Color.StatusForeground", Color.FromRgb(0, 0, 0));
            ViewModel.Refresh(nodes);
            unfilteredAssemblerList = [.. ViewModel.Assemblers];
            unfilteredMinerList = [.. ViewModel.Miners];
            unfilteredPowerList = [.. ViewModel.PowerBuildings];
            unfilteredBeaconList = [.. ViewModel.Beacons];
            unfilteredItemsList = [.. ViewModel.Items];
            unfilteredFluidsList = [.. ViewModel.Fluids];
            unfilteredKeyNodesList = [.. ViewModel.KeyNodes];

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

            WindowScaler.Track(this);
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

        //-------------------------------------------------------------------------------------------------------Filter functions

        private void UpdateFilteredBuildingLists() {
            string filter = buildingsFilterTextBox.Text ?? "";
            assemblerListView.ItemsSource = GraphSummaryViewModel.FilterBuildingRows(unfilteredAssemblerList, filter);
            minerListView.ItemsSource = GraphSummaryViewModel.FilterBuildingRows(unfilteredMinerList, filter);
            powerListView.ItemsSource = GraphSummaryViewModel.FilterBuildingRows(unfilteredPowerList, filter);
            beaconListView.ItemsSource = GraphSummaryViewModel.FilterBeaconRows(unfilteredBeaconList, filter);
        }

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
                GraphSummaryViewModel.RowContainsFilter([r.Name, r.InText, r.InULText, r.OutText, r.OutULText, r.OverprodText, r.ProducedText, r.ConsumedText], filter) &&
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

    internal sealed class BuildingRow(object tag, string rowKey, Bitmap? icon, IBrush background, string countText, string name, string powerText, double powerValue, string powerBText, double powerBValue) {
        public object Tag { get; } = tag;
        public string RowKey { get; } = rowKey;
        public Bitmap? Icon { get; } = icon;
        public IBrush Background { get; } = background;
        public IBrush Foreground { get; } = GraphSummaryWindow.RowForegroundBrush;
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
        public IBrush Foreground { get; } = GraphSummaryWindow.RowForegroundBrush;
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
        public IBrush Foreground { get; } = GraphSummaryWindow.RowForegroundBrush;
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
}
