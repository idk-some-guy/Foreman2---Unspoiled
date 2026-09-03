using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    //Ports Forms/PresetComparatorForm.cs(+.Designer.cs) (reference docs/panels-reference.md §6): a modal
    //side-by-side diff of two presets' data caches, opened from SettingsWindow's Compare Presets button.
    //Structural note carried over from upstream: ComparisonTabControl's 8 TabItems are headers only (no
    //content) - the 4 list views + filter row live once, below the tab control, and TabTable's dataset
    //swaps per selected tab (unfilteredSelectedTabObjects/Rows). Preserving that (rather than duplicating
    //4 lists per tab) matches the "preserve upstream's side-by-side layout structure" phase-5a lesson.
    //
    //Divergence (docs/upstream-divergences.md): the preset list comes from the caller's already-resolved
    //List<Preset> (the same list SettingsWindow's own Presets tab uses) instead of upstream's own
    //Presets-directory rescan in LoadPresetOptions - behaviorally equivalent, avoids a redundant disk
    //scan the caller already did. Hover tooltips (ListView_StartHover/EndHover, RecipeToolTip/TextToolTip)
    //aren't ported - not in this task's interface list; deferred alongside the rest of the tooltip surface.
    public partial class PresetComparatorWindow : Window {
        internal const int LeftOnly = 0;
        internal const int Left = 1;
        internal const int Right = 2;
        internal const int RightOnly = 3;

        internal enum RowSimilarity { Equal, CloseEnough, Different }

        internal sealed class ComparatorRow(object tag, string name, string filterKey, Bitmap? icon, IBrush foreground, FontStyle fontStyle) {
            public object Tag { get; } = tag;
            public string Name { get; } = name;
            public string FilterKey { get; } = filterKey;
            public Bitmap? Icon { get; } = icon;
            public IBrush Foreground { get; } = foreground;
            public FontStyle FontStyle { get; } = fontStyle;
            public IBrush Background { get; set; } = EqualBackground;
            public RowSimilarity Similarity { get; set; } = RowSimilarity.Equal;
        }

        private static readonly IBrush EqualBackground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        private static readonly IBrush CloseEnoughBackground = new SolidColorBrush(Color.FromRgb(240, 230, 140));
        private static readonly IBrush DifferentBackground = new SolidColorBrush(Color.FromRgb(255, 192, 203));
        private static readonly IBrush AvailableForeground = new SolidColorBrush(Color.FromRgb(0, 0, 0));
        private static readonly IBrush UnavailableForeground = new SolidColorBrush(Color.FromRgb(139, 0, 0));

        private readonly ComboBox leftPresetSelectionBox;
        private readonly ComboBox rightPresetSelectionBox;
        private readonly Border presetSelectionGroup;
        private readonly Button processPresetsButton;
        private readonly Button closeButton;
        private readonly TabControl comparisonTabControl;
        private readonly TextBox filterTextBox;
        private readonly CheckBox hideEqualObjectsCheckBox;
        private readonly CheckBox hideSimilarObjectsCheckBox;
        private readonly CheckBox showUnavailableCheckBox;
        private readonly ListBox leftOnlyListView;
        private readonly ListBox leftListView;
        private readonly ListBox rightListView;
        private readonly ListBox rightOnlyListView;
        private readonly SyncedListPair syncedPair;

        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

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

        private readonly List<ComparatorRow>[] unfilteredSelectedTabRows = [[], [], [], []];
        private readonly List<ComparatorRow>[] filteredSelectedTabRows = [[], [], [], []];

        private bool comparing;

        internal DataCache? LeftCache { get; private set; }
        internal DataCache? RightCache { get; private set; }

        //Test-only seam: lets a test supply preloaded caches without a real modal DataLoadWindow (see
        //SettingsWindow's DeleteConfirmationStub/WarningDialogStub for the established convention).
        internal Func<Preset, Task<DataCache?>>? LoadCacheStub { get; set; }

        public PresetComparatorWindow() : this([]) {
        }

        public PresetComparatorWindow(IReadOnlyList<Preset> presets) {
            InitializeComponent();

            leftPresetSelectionBox = this.FindControl<ComboBox>("LeftPresetSelectionBox")!;
            rightPresetSelectionBox = this.FindControl<ComboBox>("RightPresetSelectionBox")!;
            presetSelectionGroup = this.FindControl<Border>("PresetSelectionGroup")!;
            processPresetsButton = this.FindControl<Button>("ProcessPresetsButton")!;
            closeButton = this.FindControl<Button>("CloseButton")!;
            comparisonTabControl = this.FindControl<TabControl>("ComparisonTabControl")!;
            filterTextBox = this.FindControl<TextBox>("FilterTextBox")!;
            hideEqualObjectsCheckBox = this.FindControl<CheckBox>("HideEqualObjectsCheckBox")!;
            hideSimilarObjectsCheckBox = this.FindControl<CheckBox>("HideSimilarObjectsCheckBox")!;
            showUnavailableCheckBox = this.FindControl<CheckBox>("ShowUnavailableCheckBox")!;
            leftOnlyListView = this.FindControl<ListBox>("LeftOnlyListView")!;
            leftListView = this.FindControl<ListBox>("LeftListView")!;
            rightListView = this.FindControl<ListBox>("RightListView")!;
            rightOnlyListView = this.FindControl<ListBox>("RightOnlyListView")!;

            syncedPair = new SyncedListPair(leftListView, rightListView);

            leftPresetSelectionBox.ItemsSource = presets;
            rightPresetSelectionBox.ItemsSource = presets;
            if (presets.Count >= 2) {
                leftPresetSelectionBox.SelectedIndex = 0;
                rightPresetSelectionBox.SelectedIndex = 1;
            }

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
            unfilteredSelectedTabObjects = tabSet[0];

            processPresetsButton.Click += (_, _) => Async.Fire(ProcessPresetsClickedAsync(), nameof(ProcessPresetsClickedAsync));
            leftPresetSelectionBox.SelectionChanged += (_, _) => UpdateProcessButtonCaption();
            rightPresetSelectionBox.SelectionChanged += (_, _) => UpdateProcessButtonCaption();
            closeButton.Click += (_, _) => Close();
            comparisonTabControl.SelectionChanged += (_, _) => {
                unfilteredSelectedTabObjects = tabSet[comparisonTabControl.SelectedIndex];
                UpdateUnfilteredRows();
                UpdateFilteredLists();
            };
            filterTextBox.TextChanging += (_, _) => UpdateFilteredLists();
            hideEqualObjectsCheckBox.IsCheckedChanged += (_, _) => UpdateFilteredLists();
            hideSimilarObjectsCheckBox.IsCheckedChanged += (_, _) => UpdateFilteredLists();
            showUnavailableCheckBox.IsCheckedChanged += (_, _) => UpdateFilteredLists();

            Closed += (_, _) => {
                if (!comparing)
                    return;
                comparing = false;
                ClearAllLists();
                LeftCache?.Clear();
                LeftCache = null;
                RightCache?.Clear();
                RightCache = null;
                GC.Collect();
            };

            UpdateProcessButtonCaption();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Ports PresetSelectionBox_SelectedValueChanged (reference SettingsForm... PresetComparatorForm.cs:404-408).
        private void UpdateProcessButtonCaption() {
            bool enabled = leftPresetSelectionBox.SelectedIndex != rightPresetSelectionBox.SelectedIndex;
            processPresetsButton.IsEnabled = enabled;
            processPresetsButton.Content = enabled ? "Read Presets And Compare" : "Cant Compare Preset To Itself";
        }

        //Ports ProcessPresetsButton_Click (reference PresetComparatorForm.cs:387-402): the caption/enabled
        //flips only happen after ComparePresets (or the clear-out) completes, matching upstream's ordering.
        private async Task ProcessPresetsClickedAsync() {
            comparing = !comparing;
            if (comparing) {
                await ComparePresetsAsync().ConfigureAwait(true);
            } else {
                ClearAllLists();
                LeftCache?.Clear();
                LeftCache = null;
                RightCache?.Clear();
                RightCache = null;
                GC.Collect();
            }
            presetSelectionGroup.IsEnabled = !comparing;
            processPresetsButton.Content = comparing ? "Select Other Presets" : "Read Presets And Compare";
        }

        //Ports ComparePresets (reference PresetComparatorForm.cs:133-211): loads both caches, buckets every
        //category into Left Only / matched-pair / Right Only, then rebuilds the currently selected tab.
        private async Task ComparePresetsAsync() {
            if (leftPresetSelectionBox.SelectedItem is not Preset leftPreset || rightPresetSelectionBox.SelectedItem is not Preset rightPreset)
                return;

            LeftCache = await LoadCacheAsync(leftPreset).ConfigureAwait(true);
            RightCache = await LoadCacheAsync(rightPreset).ConfigureAwait(true);

            ProcessMods(LeftCache?.IncludedMods, RightCache?.IncludedMods, unfilteredModTabObjects);
            ProcessObjects(LeftCache?.Items, RightCache?.Items, unfilteredItemTabObjects);
            ProcessObjects(LeftCache?.Recipes, RightCache?.Recipes, unfilteredRecipeTabObjects);
            ProcessObjects(FilterAssemblers(LeftCache, EntityType.Assembler), FilterAssemblers(RightCache, EntityType.Assembler), unfilteredAssemblerTabObjects);
            ProcessObjects(FilterAssemblers(LeftCache, EntityType.Miner, EntityType.OffshorePump), FilterAssemblers(RightCache, EntityType.Miner, EntityType.OffshorePump), unfilteredMinerTabObjects);
            ProcessObjects(FilterAssemblers(LeftCache, EntityType.Boiler, EntityType.BurnerGenerator, EntityType.Generator, EntityType.Reactor), FilterAssemblers(RightCache, EntityType.Boiler, EntityType.BurnerGenerator, EntityType.Generator, EntityType.Reactor), unfilteredPowerTabObjects);
            ProcessObjects(LeftCache?.Beacons.Values.ToDictionary(b => b.Name), RightCache?.Beacons.Values.ToDictionary(b => b.Name), unfilteredBeaconTabObjects);
            ProcessObjects(LeftCache?.Modules, RightCache?.Modules, unfilteredModuleTabObjects);

            UpdateUnfilteredRows();
            UpdateFilteredLists();
        }

        private static Dictionary<string, IAssembler>? FilterAssemblers(DataCache? cache, params EntityType[] types) =>
            cache?.Assemblers.Values.Where(a => types.Contains(a.EntityType)).ToDictionary(a => a.Name);

        private Task<DataCache?> LoadCacheAsync(Preset preset) =>
            LoadCacheStub?.Invoke(preset) ?? RealLoadCacheAsync(preset);

        //Ports the DataLoadForm half of ComparePresets (this.Left+150, this.Top+100, ShowDialog) via our
        //existing DataLoadWindow preset-loading path (docs/panels-reference.md §6, DataLoadForm reference).
        private async Task<DataCache?> RealLoadCacheAsync(Preset preset) {
            var window = new DataLoadWindow(preset) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(150, 100),
            };
            await window.ShowDialog(this).ConfigureAwait(true);
            return window.Result;
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

        //Ports UpdateUnfilteredLVIs (reference PresetComparatorForm.cs:213-336): rebuilds all 4 row lists
        //for the currently selected tab, then colors the matched Left/Right pair.
        private void UpdateUnfilteredRows() {
            int tabIndex = comparisonTabControl.SelectedIndex;
            for (int i = 0; i < 4; i++) {
                unfilteredSelectedTabRows[i].Clear();
                foreach (object obj in unfilteredSelectedTabObjects[i])
                    unfilteredSelectedTabRows[i].Add(tabIndex == 0 ? NewModRow((string)obj) : NewObjectRow((IDataObjectBase)obj));
            }

            for (int i = 0; i < unfilteredSelectedTabRows[Left].Count; i++) {
                ComparatorRow l = unfilteredSelectedTabRows[Left][i];
                ComparatorRow r = unfilteredSelectedTabRows[Right][i];
                (bool similarInternals, bool similarNames) = EvaluateSimilarity(tabIndex, l.Name, l.Tag, r.Name, r.Tag);

                RowSimilarity similarity = similarInternals ? (similarNames ? RowSimilarity.Equal : RowSimilarity.CloseEnough) : RowSimilarity.Different;
                IBrush background = similarity switch {
                    RowSimilarity.Equal => EqualBackground,
                    RowSimilarity.CloseEnough => CloseEnoughBackground,
                    _ => DifferentBackground,
                };
                l.Similarity = similarity;
                l.Background = background;
                r.Similarity = similarity;
                r.Background = background;
            }
        }

        //Mod rows have no Available concept - unconditionally regular/black, matching upstream's mod
        //branch. FilterKey intentionally keeps the raw (unlowered) text here too, mirroring upstream's
        //lvItem.Name = lvItem.Text for mods (as opposed to doBase.Name.ToLowerInvariant() below).
        private static ComparatorRow NewModRow(string text) =>
            new(text, text, text, null, AvailableForeground, FontStyle.Normal);

        private ComparatorRow NewObjectRow(IDataObjectBase dataObject) =>
            new(dataObject, dataObject.FriendlyName, dataObject.Name.ToLowerInvariant(), GetOrBakeIcon(dataObject.Icon),
                dataObject.Available ? AvailableForeground : UnavailableForeground,
                dataObject.Available ? FontStyle.Normal : FontStyle.Italic);

        //Ports UpdateFilteredLists (reference PresetComparatorForm.cs:338-385). Rebuilds fresh List
        //instances per call rather than clearing the shared arrays in place: the 4 ListBoxes stay bound to
        //the same shared array slots across every tab (per this file's own top-of-file structural note),
        //but Avalonia's ItemsSource setter no-ops on a reference-equal value, so reassigning the SAME
        //mutated List back onto ItemsSource left every list showing its previously realized rows - most
        //visibly the Mods tab's content bleeding into every other tab after a real click switched it.
        private void UpdateFilteredLists() {
            string filter = (filterTextBox.Text ?? "").ToLowerInvariant();
            bool hideEqual = hideEqualObjectsCheckBox.IsChecked == true;
            bool hideSimilar = hideSimilarObjectsCheckBox.IsChecked == true;
            bool showUnavailable = showUnavailableCheckBox.IsChecked == true;

            for (int i = 0; i <= RightOnly; i += 3) {
                var filtered = new List<ComparatorRow>();
                foreach (ComparatorRow row in unfilteredSelectedTabRows[i])
                    if ((showUnavailable || row.Tag is not IDataObjectBase dObj || dObj.Available) &&
                        (row.FilterKey.Contains(filter, StringComparison.Ordinal) || row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                        filtered.Add(row);
                filteredSelectedTabRows[i] = filtered;
            }

            var filteredLeft = new List<ComparatorRow>();
            var filteredRight = new List<ComparatorRow>();
            for (int j = 0; j < unfilteredSelectedTabRows[Left].Count; j++) {
                ComparatorRow left = unfilteredSelectedTabRows[Left][j];
                ComparatorRow right = unfilteredSelectedTabRows[Right][j];

                bool leftIsData = left.Tag is IDataObjectBase ld && ld.Available;
                bool rightIsData = right.Tag is IDataObjectBase rd && rd.Available;
                bool bothTyped = left.Tag is IDataObjectBase && right.Tag is IDataObjectBase;

                if (showUnavailable || !bothTyped || leftIsData || rightIsData) {
                    if (!(hideEqual && left.Similarity == RowSimilarity.Equal) &&
                        !(hideSimilar && left.Similarity == RowSimilarity.CloseEnough) &&
                        (left.FilterKey.Contains(filter, StringComparison.Ordinal) ||
                         left.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                         right.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))) {
                        filteredLeft.Add(left);
                        filteredRight.Add(right);
                    }
                }
            }
            filteredSelectedTabRows[Left] = filteredLeft;
            filteredSelectedTabRows[Right] = filteredRight;

            leftOnlyListView.ItemsSource = filteredSelectedTabRows[LeftOnly];
            leftListView.ItemsSource = filteredSelectedTabRows[Left];
            rightListView.ItemsSource = filteredSelectedTabRows[Right];
            rightOnlyListView.ItemsSource = filteredSelectedTabRows[RightOnly];
        }

        //Ports ClearAllLists (reference PresetComparatorForm.cs:112-131).
        private void ClearAllLists() {
            foreach (List<object>[] set in tabSet)
                foreach (List<object> list in set)
                    list.Clear();
            for (int i = 0; i < 4; i++) {
                unfilteredSelectedTabRows[i].Clear();
                filteredSelectedTabRows[i].Clear();
            }
            leftOnlyListView.ItemsSource = null;
            leftListView.ItemsSource = null;
            rightListView.ItemsSource = null;
            rightOnlyListView.ItemsSource = null;
        }

        private Bitmap GetOrBakeIcon(SKBitmap icon) {
            if (!bakedIconCache.TryGetValue(icon, out Bitmap? baked)) {
                baked = BakeIcon(icon);
                bakedIconCache[icon] = baked;
            }
            return baked;
        }

        //Matches upstream's comparator-specific IconList.ImageSize of 32x32 (its other lists elsewhere
        //in the app use 24x24 - this form is the one exception, ported as-is).
        private static Bitmap BakeIcon(SKBitmap icon) {
            const int size = 32;
            using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var paint = new SKPaint { IsAntialias = true };
            surface.Canvas.DrawBitmap(icon, new SKRect(0, 0, size, size), paint);
            using SKPixmap pixmap = surface.PeekPixels();
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixmap.Info.Width, pixmap.Info.Height), new Vector(96, 96), pixmap.RowBytes);
        }

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
        internal ComboBox LeftPresetSelectionBoxControl => leftPresetSelectionBox;
        internal ComboBox RightPresetSelectionBoxControl => rightPresetSelectionBox;
        internal Border PresetSelectionGroupControl => presetSelectionGroup;
        internal Button ProcessPresetsButtonControl => processPresetsButton;
        internal Button CloseButtonControl => closeButton;
        internal TabControl ComparisonTabControlControl => comparisonTabControl;
        internal TextBox FilterTextBoxControl => filterTextBox;
        internal CheckBox HideEqualObjectsCheckBoxControl => hideEqualObjectsCheckBox;
        internal CheckBox HideSimilarObjectsCheckBoxControl => hideSimilarObjectsCheckBox;
        internal CheckBox ShowUnavailableCheckBoxControl => showUnavailableCheckBox;
        internal ListBox LeftOnlyListViewControl => leftOnlyListView;
        internal ListBox LeftListViewControl => leftListView;
        internal ListBox RightListViewControl => rightListView;
        internal ListBox RightOnlyListViewControl => rightOnlyListView;
        internal SyncedListPair SyncedPair => syncedPair;
        internal bool Comparing => comparing;

        internal Task SimulateProcessPresetsClickAsync() => ProcessPresetsClickedAsync();
    }
}
