using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    //Ports Forms/PresetComparatorForm.cs (reference docs/panels-reference.md §6): a side-by-side diff of two
    //presets' data caches, opened from Settings' Compare Presets…. The eight categories are headers only; the
    //three groups below swap their dataset per selected tab (unfilteredSelectedTabObjects).
    //
    //Divergence (docs/upstream-divergences.md): the preset list comes from the caller's already-resolved
    //List<Preset> instead of upstream's own Presets-directory rescan in LoadPresetOptions. Hover tooltips
    //beyond the row's name aren't ported.
    public partial class PresetComparatorWindow : Window, IThemeAware {
        internal const int LeftOnly = 0;
        internal const int Left = 1;
        internal const int Right = 2;
        internal const int RightOnly = 3;

        internal enum RowSimilarity { Equal, CloseEnough, Different }

        private static readonly (string One, string Many)[] CountNouns = [
            ("mod", "mods"), ("item", "items"), ("recipe", "recipes"), ("assembler", "assemblers"),
            ("miner", "miners"), ("power building", "power buildings"), ("beacon", "beacons"), ("module", "modules"),
        ];

        private static readonly string[] TabNames = ["Mods", "Items", "Recipes", "Assemblers", "Miners", "Power", "Beacons", "Modules"];
        private static readonly string[] TabGlyphKeys = [
            "Kit.Glyph.Category.Mods.18", "Kit.Glyph.Category.Items.18", "Kit.Glyph.Category.Recipes.18", "Kit.Glyph.Category.Assemblers.18",
            "Kit.Glyph.Category.Miners.18", "Kit.Glyph.Category.Power.18", "Kit.Glyph.Beacon.18", "Kit.Glyph.Category.Modules.18",
        ];
        private const int BeaconsTab = 6;
        private const string EmptySentence = "Pick a preset on each side, then click Compare. Each preset takes a few seconds to load.";
        private const string SamePresetSentence = "Both sides name the same preset. Choose a different one on either side.";

        internal PresetComparatorViewModel ViewModel { get; } = new PresetComparatorViewModel();

        private readonly IReadOnlyList<Preset> presets;
        private readonly PopUp leftPresetPopUp;
        private readonly PopUp rightPresetPopUp;
        private readonly Button compareButton;
        private readonly TabStrip comparisonTabStrip;
        private readonly SearchField filterField;
        private readonly CheckBox showUnavailableCheckBox;
        private readonly CheckBox hideIdenticalCheckBox;
        private readonly CheckBox hideSimilarCheckBox;
        private readonly ListBox leftOnlyList;
        private readonly ListBox bothList;
        private readonly ListBox rightOnlyList;
        private readonly TextBlock leftOnlyHeading;
        private readonly TextBlock rightOnlyHeading;
        private readonly TextBlock leftOnlyCount;
        private readonly TextBlock bothCount;
        private readonly TextBlock rightOnlyCount;
        private readonly TextBlock leftOnlyNoResults;
        private readonly TextBlock bothNoResults;
        private readonly TextBlock rightOnlyNoResults;
        private readonly Grid groupsGrid;
        private readonly DockPanel leftOnlyGroup;
        private readonly DockPanel bothGroup;
        private readonly DockPanel rightOnlyGroup;
        private readonly StatePanel emptyPanel;

        private readonly List<object>[] unfilteredModTabObjects = [[], [], [], []];
        private readonly List<object>[] unfilteredItemTabObjects = [[], [], [], []];
        private readonly List<object>[] unfilteredRecipeTabObjects = [[], [], [], []];
        private readonly List<object>[] unfilteredAssemblerTabObjects = [[], [], [], []];
        private readonly List<object>[] unfilteredMinerTabObjects = [[], [], [], []];
        private readonly List<object>[] unfilteredPowerTabObjects = [[], [], [], []];
        private readonly List<object>[] unfilteredBeaconTabObjects = [[], [], [], []];
        private readonly List<object>[] unfilteredModuleTabObjects = [[], [], [], []];
        private readonly List<object>[][] tabSet;
        private List<object>[] unfilteredSelectedTabObjects;

        private bool compared;
        private bool loading;
        private readonly WindowMinSizeScale minSizeScale;

        internal DataCache? LeftCache { get; private set; }
        internal DataCache? RightCache { get; private set; }

        internal Func<Preset, Task<DataLoadResult>>? LoadCacheStub { get; set; }

        internal static bool CanCompare(IReadOnlyCollection<Preset>? presets) => presets is { Count: >= 2 };

        public PresetComparatorWindow() : this([]) {
        }

        public PresetComparatorWindow(IReadOnlyList<Preset> presets) {
            InitializeComponent();
            this.presets = presets;

            leftPresetPopUp = this.FindControl<PopUp>("LeftPresetPopUp")!;
            rightPresetPopUp = this.FindControl<PopUp>("RightPresetPopUp")!;
            compareButton = this.FindControl<Button>("CompareButton")!;
            comparisonTabStrip = this.FindControl<TabStrip>("ComparisonTabStrip")!;
            filterField = this.FindControl<SearchField>("FilterField")!;
            showUnavailableCheckBox = this.FindControl<CheckBox>("ShowUnavailableCheckBox")!;
            hideIdenticalCheckBox = this.FindControl<CheckBox>("HideIdenticalCheckBox")!;
            hideSimilarCheckBox = this.FindControl<CheckBox>("HideSimilarCheckBox")!;
            leftOnlyList = this.FindControl<ListBox>("LeftOnlyList")!;
            bothList = this.FindControl<ListBox>("BothList")!;
            rightOnlyList = this.FindControl<ListBox>("RightOnlyList")!;
            leftOnlyHeading = this.FindControl<TextBlock>("LeftOnlyHeading")!;
            rightOnlyHeading = this.FindControl<TextBlock>("RightOnlyHeading")!;
            leftOnlyCount = this.FindControl<TextBlock>("LeftOnlyCount")!;
            bothCount = this.FindControl<TextBlock>("BothCount")!;
            rightOnlyCount = this.FindControl<TextBlock>("RightOnlyCount")!;
            leftOnlyNoResults = this.FindControl<TextBlock>("LeftOnlyNoResults")!;
            bothNoResults = this.FindControl<TextBlock>("BothNoResults")!;
            rightOnlyNoResults = this.FindControl<TextBlock>("RightOnlyNoResults")!;
            groupsGrid = this.FindControl<Grid>("GroupsGrid")!;
            leftOnlyGroup = this.FindControl<DockPanel>("LeftOnlyGroup")!;
            bothGroup = this.FindControl<DockPanel>("BothGroup")!;
            rightOnlyGroup = this.FindControl<DockPanel>("RightOnlyGroup")!;
            emptyPanel = this.FindControl<StatePanel>("EmptyPanel")!;

            for (int i = 0; i < TabNames.Length; i++)
                comparisonTabStrip.Items.Add(new TabStripItem {
                    Text = TabNames[i],
                    Glyph = (Geometry)this.FindResource(TabGlyphKeys[i])!,
                    GlyphFill = i == BeaconsTab ? (Geometry)this.FindResource("Kit.Glyph.Beacon.Fill.18")! : null,
                    ToolTip = $"{TabNames[i]} \u2014 \u2318{i + 1}",
                });
            comparisonTabStrip.SelectedIndex = 1;

            foreach (Preset preset in presets) {
                leftPresetPopUp.Items.Add(preset.Name);
                rightPresetPopUp.Items.Add(preset.Name);
            }
            if (presets.Count >= 2) {
                leftPresetPopUp.SelectedItem = presets[0].Name;
                rightPresetPopUp.SelectedItem = presets[1].Name;
            }
            ToolTip.SetTip(leftPresetPopUp, leftPresetPopUp.SelectedItem);
            ToolTip.SetTip(rightPresetPopUp, rightPresetPopUp.SelectedItem);

            tabSet = [
                unfilteredModTabObjects,
                unfilteredItemTabObjects,
                unfilteredRecipeTabObjects,
                unfilteredAssemblerTabObjects,
                unfilteredMinerTabObjects,
                unfilteredPowerTabObjects,
                unfilteredBeaconTabObjects,
                unfilteredModuleTabObjects,
            ];
            unfilteredSelectedTabObjects = tabSet[comparisonTabStrip.SelectedIndex];

            compareButton.Click += (_, _) => Async.Fire(CompareAsync(), nameof(CompareAsync));
            leftPresetPopUp.PropertyChanged += (_, e) => { if (e.Property == PopUp.SelectedItemProperty) OnPresetChoiceChanged(leftPresetPopUp); };
            rightPresetPopUp.PropertyChanged += (_, e) => { if (e.Property == PopUp.SelectedItemProperty) OnPresetChoiceChanged(rightPresetPopUp); };
            AddHandler(KeyDownEvent, OnPresetComparatorKeyDown, RoutingStrategies.Tunnel);
            comparisonTabStrip.SelectionChanged += (_, _) => {
                unfilteredSelectedTabObjects = tabSet[comparisonTabStrip.SelectedIndex];
                UpdateUnfilteredRows();
                UpdateFilteredLists();
            };
            filterField.TextChanged += (_, _) => UpdateFilteredLists();
            hideIdenticalCheckBox.IsCheckedChanged += (_, _) => UpdateFilteredLists();
            hideSimilarCheckBox.IsCheckedChanged += (_, _) => UpdateFilteredLists();
            showUnavailableCheckBox.IsCheckedChanged += (_, _) => UpdateFilteredLists();
            bothList.ContainerPrepared += (_, e) => ApplyMatchClass(e.Container);
            bothList.ContainerIndexChanged += (_, e) => ApplyMatchClass(e.Container);
            leftOnlyList.ContainerPrepared += (_, e) => NameRow(e.Container);
            rightOnlyList.ContainerPrepared += (_, e) => NameRow(e.Container);
            leftOnlyList.ContainerIndexChanged += (_, e) => NameRow(e.Container);
            rightOnlyList.ContainerIndexChanged += (_, e) => NameRow(e.Container);

            foreach (DockPanel group in new[] { leftOnlyGroup, bothGroup, rightOnlyGroup }) {
                AutomationProperties.SetControlTypeOverride(group, AutomationControlType.Group);
                AutomationProperties.SetIsControlElementOverride(group, true);
            }

            Closed += (_, _) => {
                if (!compared)
                    return;
                compared = false;
                ClearAllLists();
                LeftCache?.Clear();
                LeftCache = null;
                RightCache?.Clear();
                RightCache = null;
                GC.Collect();
            };

            UpdateCompareState();
            minSizeScale = new WindowMinSizeScale(this);

            AppearanceHub.Current?.Subscribe(this);
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void RefreshTheme() => minSizeScale.Apply();

        private static void ApplyMatchClass(Control container) {
            var pair = container.DataContext as ComparatorPairRow;
            container.Classes.Set("similar", pair?.Similarity == RowSimilarity.CloseEnough);
            container.Classes.Set("different", pair?.Similarity == RowSimilarity.Different);
            AutomationProperties.SetName(container, pair?.SpokenName);
        }

        private static void NameRow(Control container) =>
            AutomationProperties.SetName(container, (container.DataContext as ComparatorRow)?.SpokenName);

        private Preset? PresetNamed(string? name) => presets.FirstOrDefault(p => p.Name == name);

        private void OnPresetChoiceChanged(PopUp popUp) {
            ToolTip.SetTip(popUp, popUp.SelectedItem);
            UpdateCompareState();
        }

        private void UpdateCompareState() {
            bool distinct = leftPresetPopUp.SelectedItem is not null && leftPresetPopUp.SelectedItem != rightPresetPopUp.SelectedItem;
            compareButton.IsEnabled = distinct && !loading;
            ToolTip.SetTip(compareButton, distinct ? null : "Choose a different preset on either side.");
            emptyPanel.Sentence = distinct ? EmptySentence : SamePresetSentence;
            emptyPanel.IsVisible = !compared;
            groupsGrid.IsVisible = compared;
        }

        private async Task CompareAsync() {
            if (PresetNamed(leftPresetPopUp.SelectedItem) is not Preset leftPreset || PresetNamed(rightPresetPopUp.SelectedItem) is not Preset rightPreset)
                return;

            loading = true;
            UpdateCompareState();
            try {
                DataLoadResult left = await LoadCacheAsync(leftPreset).ConfigureAwait(true);
                if (left.Cache is not DataCache leftCache) {
                    OnLoadStopped(left.Outcome, leftPresetPopUp);
                    return;
                }
                DataLoadResult right = await LoadCacheAsync(rightPreset).ConfigureAwait(true);
                if (right.Cache is not DataCache rightCache) {
                    OnLoadStopped(right.Outcome, rightPresetPopUp);
                    return;
                }

                ClearAllLists();
                if (LeftCache is not null && !ReferenceEquals(LeftCache, leftCache) && !ReferenceEquals(LeftCache, rightCache))
                    LeftCache.Clear();
                if (RightCache is not null && !ReferenceEquals(RightCache, leftCache) && !ReferenceEquals(RightCache, rightCache))
                    RightCache.Clear();
                LeftCache = leftCache;
                RightCache = rightCache;
                compared = true;
                ApplyGroupNames(leftPreset.Name, rightPreset.Name);
                ((ComparatorTableList)bothList).SetColumnVersions(leftCache, rightCache);

                ProcessMods(leftCache.IncludedMods, rightCache.IncludedMods, unfilteredModTabObjects);
                ProcessObjects(leftCache.Items, rightCache.Items, unfilteredItemTabObjects);
                ProcessObjects(leftCache.Recipes, rightCache.Recipes, unfilteredRecipeTabObjects);
                ProcessObjects(FilterAssemblers(leftCache, PlantTowerRules.IsAssemblerListEntry), FilterAssemblers(rightCache, PlantTowerRules.IsAssemblerListEntry), unfilteredAssemblerTabObjects);
                ProcessObjects(FilterAssemblers(leftCache, EntityType.Miner, EntityType.OffshorePump), FilterAssemblers(rightCache, EntityType.Miner, EntityType.OffshorePump), unfilteredMinerTabObjects);
                ProcessObjects(FilterAssemblers(leftCache, EntityType.Boiler, EntityType.BurnerGenerator, EntityType.Generator, EntityType.Reactor), FilterAssemblers(rightCache, EntityType.Boiler, EntityType.BurnerGenerator, EntityType.Generator, EntityType.Reactor), unfilteredPowerTabObjects);
                ProcessObjects(leftCache.Beacons.Values.ToDictionary(b => b.Name), rightCache.Beacons.Values.ToDictionary(b => b.Name), unfilteredBeaconTabObjects);
                ProcessObjects(leftCache.Modules, rightCache.Modules, unfilteredModuleTabObjects);

                UpdateUnfilteredRows();
                UpdateFilteredLists();
            } finally {
                loading = false;
                UpdateCompareState();
            }
        }

        private static void OnLoadStopped(DataLoadOutcome outcome, PopUp failedSide) {
            if (outcome != DataLoadOutcome.ChooseAnother)
                return;
            failedSide.Focus(NavigationMethod.Tab);
            failedSide.IsOpen = true;
        }

        private void ApplyGroupNames(string left, string right) {
            leftOnlyHeading.Text = "Only in " + left;
            rightOnlyHeading.Text = "Only in " + right;
            ((ComparatorTableList)bothList).SetColumnNames(left, right);
            ToolTip.SetTip(leftOnlyHeading, leftOnlyHeading.Text);
            ToolTip.SetTip(rightOnlyHeading, rightOnlyHeading.Text);
        }

        private static Dictionary<string, IAssembler>? FilterAssemblers(DataCache? cache, params EntityType[] types) =>
            FilterAssemblers(cache, a => types.Contains(a.EntityType));

        private static Dictionary<string, IAssembler>? FilterAssemblers(DataCache? cache, Func<IAssembler, bool> keep) =>
            cache?.Assemblers.Values.Where(keep).ToDictionary(a => a.Name);

        private Task<DataLoadResult> LoadCacheAsync(Preset preset) =>
            LoadCacheStub?.Invoke(preset) ?? RealLoadCacheAsync(preset);

        internal static DataLoadWindow NewLoadWindow(Preset preset, Func<Preset, DataCache, IProgress<KeyValuePair<int, string>>, CancellationToken, Task>? load = null) =>
            new(preset, true, DataLoadMode.AfterLaunch, null, load, null) { PresentsOutdatedAlert = false };

        private async Task<DataLoadResult> RealLoadCacheAsync(Preset preset) {
            DataLoadWindow window = NewLoadWindow(preset);
            await window.ShowDialog(this).ConfigureAwait(true);
            return new DataLoadResult(window.Result, window.Outcome);
        }

        //Ports ComparePresets' mod-diff block (reference PresetComparatorForm.cs:184-197) verbatim,
        //including the Ordinal sort every bucket gets (unlike ProcessObjects' plain-comparer dictionary
        //ordering below - upstream uses two different comparers here, kept as-is).
        internal static void ProcessMods(IReadOnlyDictionary<string, string>? leftMods, IReadOnlyDictionary<string, string>? rightMods, List<object>[] output) {
            foreach (KeyValuePair<string, string> kvp in leftMods ?? new Dictionary<string, string>()) {
                string key = kvp.Key + "_" + kvp.Value;
                if (rightMods?.ContainsKey(kvp.Key) is true)
                    output[Left].Add(key);
                else
                    output[LeftOnly].Add(key);
            }
            foreach (KeyValuePair<string, string> kvp in rightMods ?? new Dictionary<string, string>()) {
                string key = kvp.Key + "_" + kvp.Value;
                if (leftMods?.ContainsKey(kvp.Key) is true)
                    output[Right].Add(key);
                else
                    output[RightOnly].Add(key);
            }
            for (int i = 0; i < 4; i++)
                output[i].Sort((a, b) => string.Compare((string)a, (string)b, StringComparison.Ordinal));
        }

        //Ports ComparePresets' ProcessObject local function (reference PresetComparatorForm.cs:135-159)
        //verbatim: Available-first/then-Key ordering for the exclusive buckets, and a separate combined
        //sort (Available-first/then-Name Ordinal) for the matched-pair bucket so [Left] and [Right] stay
        //aligned index-for-index.
        internal static void ProcessObjects<T>(IReadOnlyDictionary<string, T>? leftDict, IReadOnlyDictionary<string, T>? rightDict, List<object>[] output) where T : IDataObjectBase {
            if (leftDict is null || rightDict is null)
                return;

            var centerSet = new List<(T Left, T Right)>();
            foreach (KeyValuePair<string, T> kvp in leftDict.OrderByDescending(k => k.Value.Available).ThenBy(k => k.Key)) {
                if (!rightDict.ContainsKey(kvp.Key))
                    output[LeftOnly].Add(kvp.Value);
                else
                    centerSet.Add((kvp.Value, rightDict[kvp.Key]));
            }
            foreach (KeyValuePair<string, T> kvp in rightDict.OrderByDescending(k => k.Value.Available).ThenBy(k => k.Key))
                if (!leftDict.ContainsKey(kvp.Key))
                    output[RightOnly].Add(kvp.Value);

            centerSet.Sort((a, b) => {
                int availableDiff = (a.Left.Available || a.Right.Available).CompareTo(b.Left.Available || b.Right.Available);
                return availableDiff != 0 ? -availableDiff : string.Compare(a.Left.Name, b.Left.Name, StringComparison.Ordinal);
            });
            foreach ((T left, T right) in centerSet) {
                output[Left].Add(left);
                output[Right].Add(right);
            }
        }

        //Ports UpdateUnfilteredLVIs' per-tab similarInternals switch (reference PresetComparatorForm.cs:266-329)
        //verbatim, including the recipes branch's time-scaled ratio check and the assemblers/miners/power
        //stub. Divergence (docs/upstream-divergences.md): assemblers/miners/power always report
        //similarInternals=true - upstream's own "//QUALITY UPDATE REQUIRED" comment marks that comparison
        //as disabled/incomplete; ported as-is rather than fixed.
        internal static (bool SimilarInternals, bool SimilarNames) EvaluateSimilarity(int tabIndex, string leftName, object leftTag, string rightName, object rightTag) {
            bool similarNames = leftName.Equals(rightName, StringComparison.OrdinalIgnoreCase);
            bool similarInternals = true;
            switch (tabIndex) {
                case 0: //mods
                    similarInternals = similarNames;
                    break;
                case 1: //items
                    similarInternals &= (leftTag as IItem)?.Available == (rightTag as IItem)?.Available;
                    break;
                case 2: { //recipes
                    var lRecipe = leftTag as IRecipe;
                    var rRecipe = rightTag as IRecipe;

                    similarInternals = lRecipe?.IngredientList.Count == rRecipe?.IngredientList.Count && lRecipe?.ProductList.Count == rRecipe?.ProductList.Count;
                    similarInternals &= lRecipe?.Available == rRecipe?.Available;
                    bool exactInternals = similarInternals;
                    double scale = (rRecipe?.Time / lRecipe?.Time) ?? 0;
                    if (similarInternals) {
                        foreach (IItem lingredient in lRecipe?.IngredientList ?? []) {
                            IItem? ringredient = rRecipe?.IngredientList.FirstOrDefault(item => item.Name == lingredient.Name);
                            similarInternals = similarInternals && ringredient is not null && lRecipe is not null && rRecipe is not null &&
                                Math.Abs((scale * lRecipe.IngredientSet[lingredient] / rRecipe.IngredientSet[ringredient]) - 1) < 0.001;
                            exactInternals = exactInternals && similarInternals && ringredient is not null && lRecipe?.IngredientSet[lingredient] == rRecipe?.IngredientSet[ringredient];
                        }
                        foreach (IItem lproduct in lRecipe?.ProductList ?? []) {
                            if (similarInternals) {
                                IItem? rproduct = rRecipe?.ProductList.FirstOrDefault(item => item.Name == lproduct.Name);
                                similarInternals = similarInternals && rproduct is not null && lRecipe is not null && rRecipe is not null &&
                                    Math.Abs((scale * lRecipe.ProductSet[lproduct] / rRecipe.ProductSet[rproduct]) - 1) < 0.001;
                                exactInternals = exactInternals && similarInternals && rproduct is not null && lRecipe?.ProductSet[lproduct] == rRecipe?.ProductSet[rproduct];
                            }
                        }
                    }
                    similarNames = similarNames && exactInternals;
                    break;
                }
                case 3: //assemblers
                case 4: //miners
                case 5: //power (aka: assemblers) - QUALITY UPDATE REQUIRED upstream, ported as-is
                    similarInternals = true;
                    break;
                case 6: //beacons
                    similarInternals = (leftTag as IBeacon)?.ModuleSlots == (rightTag as IBeacon)?.ModuleSlots;
                    break;
                case 7: { //modules - pollution deliberately not compared, matching upstream
                    var lModule = leftTag as IModule;
                    var rModule = rightTag as IModule;
                    similarInternals = lModule is not null && rModule is not null &&
                        lModule.GetProductivityBonus() == rModule.GetProductivityBonus() &&
                        lModule.GetSpeedBonus() == rModule.GetSpeedBonus() &&
                        lModule.GetConsumptionBonus() == rModule.GetConsumptionBonus() &&
                        lModule.GetQualityBonus() == rModule.GetQualityBonus();
                    break;
                }
            }
            return (similarInternals, similarNames);
        }

        private void UpdateUnfilteredRows() => ViewModel.RebuildTab(comparisonTabStrip.SelectedIndex, unfilteredSelectedTabObjects);

        private static bool Matches(ComparatorRow row, string filter) =>
            row.FilterKey.Contains(filter, StringComparison.Ordinal) || row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        //Fresh lists every call: ItemsSource no-ops on a reference-equal value, which would leave stale rows realized.
        private void UpdateFilteredLists() {
            string filter = (filterField.Text ?? "").ToLowerInvariant();
            bool hideIdentical = hideIdenticalCheckBox.IsChecked == true;
            bool hideSimilar = hideSimilarCheckBox.IsChecked == true;
            bool showUnavailable = showUnavailableCheckBox.IsChecked == true;

            bool Shown(ComparatorRow row) => showUnavailable || !row.IsUnavailable;

            List<ComparatorRow> leftOnly = [.. ViewModel.LeftOnlyRows.Where(r => Shown(r) && Matches(r, filter))];
            List<ComparatorRow> rightOnly = [.. ViewModel.RightOnlyRows.Where(r => Shown(r) && Matches(r, filter))];
            List<ComparatorPairRow> visiblePairs = [.. ViewModel.BothRows.Where(p => p.Left.Tag is not IDataObjectBase || Shown(p.Left) || Shown(p.Right))];
            List<ComparatorPairRow> unhidden = [.. visiblePairs.Where(p =>
                !(hideIdentical && p.Similarity == RowSimilarity.Equal) && !(hideSimilar && p.Similarity == RowSimilarity.CloseEnough))];
            List<ComparatorPairRow> both = [.. unhidden.Where(p => Matches(p.Left, filter) || p.Right.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))];
            foreach (ComparatorPairRow pair in both)
                pair.ShowIdentical = !hideIdentical;

            leftOnlyList.ItemsSource = leftOnly;
            bothList.ItemsSource = both;
            rightOnlyList.ItemsSource = rightOnly;
            leftOnlyCount.Text = leftOnly.Count.ToString("N0", DisplayCulture.Format);
            bothCount.Text = both.Count.ToString("N0", DisplayCulture.Format);
            rightOnlyCount.Text = rightOnly.Count.ToString("N0", DisplayCulture.Format);
            AutomationProperties.SetName(leftOnlyGroup, leftOnlyHeading.Text + ", " + Counted(leftOnly.Count));
            AutomationProperties.SetName(bothGroup, "In both, " + Counted(both.Count));
            AutomationProperties.SetName(rightOnlyGroup, rightOnlyHeading.Text + ", " + Counted(rightOnly.Count));

            bool filtering = filter.Length > 0;
            SetNoResults(leftOnlyNoResults, leftOnly.Count, filtering ? "No matches" : "None");
            SetNoResults(rightOnlyNoResults, rightOnly.Count, filtering ? "No matches" : "None");
            SetNoResults(bothNoResults, both.Count, filtering && unhidden.Count > 0 ? "No matches" : visiblePairs.Count > 0 ? "Nothing differs" : "None");
        }

        private string Counted(int count) {
            (string one, string many) = CountNouns[comparisonTabStrip.SelectedIndex];
            return count.ToString("N0", DisplayCulture.Format) + " " + (count == 1 ? one : many);
        }

        private static void SetNoResults(TextBlock text, int count, string message) {
            text.Text = message;
            text.IsVisible = count == 0;
        }

        private void ClearAllLists() {
            foreach (List<object>[] set in tabSet)
                foreach (List<object> list in set)
                    list.Clear();
            ViewModel.Clear();
            leftOnlyList.ItemsSource = null;
            bothList.ItemsSource = null;
            rightOnlyList.ItemsSource = null;
        }

        private void OnPresetComparatorKeyDown(object? sender, KeyEventArgs e) {
            bool primary = e.KeyModifiers == KeyModifiers.Meta;
            if (primary && e.Key is >= Key.D1 and <= Key.D8) {
                e.Handled = true;
                comparisonTabStrip.SelectedIndex = e.Key - Key.D1;
            } else if (primary && e.Key == Key.F) {
                e.Handled = true;
                filterField.Focus(NavigationMethod.Tab);
            } else if ((primary && e.Key == Key.W) || (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && string.IsNullOrEmpty(filterField.Text)
                    && leftPresetPopUp.IsOpen == false && rightPresetPopUp.IsOpen == false)) {
                e.Handled = true;
                Close();
            }
        }

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
        internal PopUp LeftPresetPopUpControl => leftPresetPopUp;
        internal PopUp RightPresetPopUpControl => rightPresetPopUp;
        internal Button CompareButtonControl => compareButton;
        internal TabStrip ComparisonTabStripControl => comparisonTabStrip;
        internal SearchField FilterFieldControl => filterField;
        internal CheckBox HideIdenticalCheckBoxControl => hideIdenticalCheckBox;
        internal CheckBox ShowUnavailableCheckBoxControl => showUnavailableCheckBox;
        internal ListBox LeftOnlyListControl => leftOnlyList;
        internal ListBox BothListControl => bothList;
        internal ListBox RightOnlyListControl => rightOnlyList;
        internal TextBlock LeftOnlyNoResultsControl => leftOnlyNoResults;
        internal StatePanel EmptyPanelControl => emptyPanel;
        internal Grid GroupsGridControl => groupsGrid;
        internal bool Compared => compared;

        internal Task SimulateCompareClickAsync() => CompareAsync();
    }

    internal sealed class ComparatorRow(object tag, string name, string filterKey, Bitmap? icon, bool isUnavailable, string tooltip) {
        public object Tag { get; } = tag;
        public string Name { get; } = name;
        public string FilterKey { get; } = filterKey;
        public Bitmap? Icon { get; } = icon;
        public bool IsUnavailable { get; } = isUnavailable;
        public string Tooltip { get; } = tooltip;
        public string SpokenName => IsUnavailable ? Name + ", unavailable" : Name;
    }

    internal sealed class ComparatorPairRow(ComparatorRow left, ComparatorRow right, PresetComparatorWindow.RowSimilarity similarity)
            : CommunityToolkit.Mvvm.ComponentModel.ObservableObject {
        private bool showIdentical;

        public ComparatorRow Left { get; } = left;
        public ComparatorRow Right { get; } = right;
        public PresetComparatorWindow.RowSimilarity Similarity { get; } = similarity;
        public bool IsIdentical => Similarity == PresetComparatorWindow.RowSimilarity.Equal;

        public bool ShowIdentical {
            get => showIdentical;
            set {
                if (SetProperty(ref showIdentical, value))
                    OnPropertyChanged(nameof(MatchText));
            }
        }

        public string MatchText => Similarity switch {
            PresetComparatorWindow.RowSimilarity.CloseEnough => "Similar",
            PresetComparatorWindow.RowSimilarity.Different => "Different",
            _ => ShowIdentical ? "Identical" : "",
        };

        public string SpokenName => string.Join(", ", new[] { Left.SpokenName, Right.SpokenName, MatchText.ToLowerInvariant() }.Where(part => part.Length > 0));
    }

    internal sealed class ComparatorTableList : ListBox {
        private Control? rowScroller;
        private TextBlock? leftHeaderCell;
        private TextBlock? rightHeaderCell;
        private TextBlock? leftVersionCell;
        private TextBlock? rightVersionCell;
        private (FactorioGameVersion? Version, bool Outdated)? leftVersion;
        private (FactorioGameVersion? Version, bool Outdated)? rightVersion;
        private string? leftHeader;
        private string? rightHeader;

        protected override Type StyleKeyOverride => typeof(ListBox);

        internal Control? ColumnHeader { get; private set; }

        internal void SetColumnNames(string left, string right) {
            leftHeader = left;
            rightHeader = right;
            ApplyColumnNames();
        }

        internal void SetColumnVersions(DataCache left, DataCache right) {
            leftVersion = (left.GameVersion, PresetExportFormat.IsOutdated(left));
            rightVersion = (right.GameVersion, PresetExportFormat.IsOutdated(right));
            ApplyColumnNames();
        }

        protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            leftVersionCell = e.NameScope.Find<TextBlock>("PART_LeftVersion");
            rightVersionCell = e.NameScope.Find<TextBlock>("PART_RightVersion");
            ColumnHeader = e.NameScope.Find<Control>("PART_ColumnHeader");
            rowScroller = e.NameScope.Find<Control>("PART_ScrollViewer");
            leftHeaderCell = e.NameScope.Find<TextBlock>("PART_LeftHeader");
            rightHeaderCell = e.NameScope.Find<TextBlock>("PART_RightHeader");
            ApplyColumnNames();
        }

        private void ApplyColumnNames() {
            foreach ((TextBlock? cell, TextBlock? versionCell, string? text, (FactorioGameVersion? Version, bool Outdated)? version) in new[] {
                (leftHeaderCell, leftVersionCell, leftHeader, leftVersion), (rightHeaderCell, rightVersionCell, rightHeader, rightVersion) }) {
                if (cell is null)
                    continue;
                cell.Text = text;
                ToolTip.SetTip(cell, text);
                if (version is not { } shown || text is null)
                    continue;
                if (versionCell is not null)
                    versionCell.Text = PresetVersionCaption.ForComparator(shown.Version, shown.Outdated);
                AutomationProperties.SetName(cell, PresetVersionCaption.ComparatorName(text, shown.Version, shown.Outdated));
            }
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new TablePeer(this);

        private sealed class TablePeer(ComparatorTableList owner) : ListBoxAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Table;

            protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() {
                if (owner.ColumnHeader is not Control header || owner.rowScroller is not Control rows)
                    return base.GetChildrenCore();
                return [CreatePeerForElement(header), CreatePeerForElement(rows)];
            }
        }
    }

    internal sealed class ComparatorHeaderText : TextBlock {
        protected override Type StyleKeyOverride => typeof(TextBlock);

        protected override AutomationPeer OnCreateAutomationPeer() => new HeaderTextPeer(this);

        private sealed class HeaderTextPeer(ComparatorHeaderText owner) : TextBlockAutomationPeer(owner) {
            protected override string? GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : base.GetNameCore();
        }
    }

    internal sealed class ComparatorColumnHeader : Grid {
        protected override AutomationPeer OnCreateAutomationPeer() => new HeaderPeer(this);

        private sealed class HeaderPeer(ComparatorColumnHeader owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Header;

            protected override bool IsControlElementCore() => true;

            protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() {
                var peers = new List<AutomationPeer>();
                foreach (Control child in owner.Children)
                    foreach (Control cell in child is Grid group ? group.Children : [child])
                        peers.Add(CreatePeerForElement(cell));
                return peers;
            }

            protected override AutomationPeer? GetParentCore() =>
                owner.TemplatedParent is ComparatorTableList table ? CreatePeerForElement(table) : base.GetParentCore();
        }
    }
}
