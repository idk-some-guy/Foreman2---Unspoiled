using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.Views {
    //Ports Forms/SettingsForm.cs's window shell and Presets tab (reference §5). Confirm/Cancel commit
    //semantics: unlike the other tabs (Tasks 2/3 add their own UpdateSettings() writes here), preset
    //selection is never deferred to Confirm - upstream commits Options.SelectedPreset and closes with OK
    //the instant a preset is picked (double-click or the right-click "Use This Preset" item), matching
    //SettingsForm.cs's PresetListBox_MouseDoubleClick/SelectPresetMenuItem_Click. Plain Confirm only
    //applies whatever the not-yet-built tabs have written directly into Options by then.
    public partial class SettingsWindow : Window {
        public sealed class SettingsWindowOptions(DataCache cache) {
            public DataCache DCache { get; } = cache;
            public List<Preset>? Presets { get; set; } = [];
            public Preset? SelectedPreset { get; set; }
            public bool RequireReload { get; set; }
            public HashSet<IDataObjectBase> EnabledObjects { get; set; } = [];

            //Set by ShowLoadFromSaveAsync on a successful load, alongside its own eager settings.json write;
            //OpenSettingsAsync's tail (MainWindow.axaml.cs) applies it onto its own settings before saving,
            //the same way EnabledObjects/RequireReload flow back - otherwise that tail's unconditional Save
            //reverts the eager write with its stale in-memory copy.
            public string? LastSaveFileLocation { get; set; }

            //Graph Options tab (reference §5) - ports SettingsForm.SettingsFormOptions' matching members.
            public uint QualitySteps { get; set; }
            public LevelOfDetail LevelOfDetail { get; set; }
            public int NodeCountForSimpleView { get; set; }
            public int IconsOnlyIconSize { get; set; }
            public bool ArrowsOnLinks { get; set; }
            public bool SimplePassthroughNodes { get; set; }
            public bool DynamicLinkWidth { get; set; }
            public bool AbbreviateSciPacks { get; set; }
            public bool ShowRecipeToolTip { get; set; }
            public bool RoundAssemblerCount { get; set; }
            public bool LockedRecipeEditPanelPosition { get; set; }
            public bool FlagOUSuppliedNodes { get; set; }
            public ThemeMode FlagDarkMode { get; set; }
            public bool ShowErrorArrows { get; set; }
            public bool ShowWarningArrows { get; set; }
            public bool ShowDisconnectedArrows { get; set; }
            public bool ShowOUSuppliedArrows { get; set; }
            public AssemblerSelector.Style DefaultAssemblerStyle { get; set; }
            public ModuleSelector.Style DefaultModuleStyle { get; set; }
            public NodeDirection DefaultNodeDirection { get; set; }
            public bool SmartNodeDirection { get; set; }
            public bool EnableExtraProductivityForNonMiners { get; set; }
            public bool DevShowUnavailableItems { get; set; }
            public bool DevUseRecipeBWFilters { get; set; }
            public decimal SolverLowPriorityPower { get; set; }
            public bool SolverPullConsumerNodes { get; set; }
            public decimal SolverPullConsumerNodesPower { get; set; }
        }

        private readonly TextBlock currentPresetLabel;
        private readonly ListBox modSelectionBox;
        private readonly ListBox presetListBox;
        private readonly MenuItem selectPresetMenuItem;
        private readonly MenuItem deletePresetMenuItem;
        private readonly Button importPresetButton;
        private readonly Button comparePresetsButton;
        private readonly Button confirmButton;
        private readonly Button cancelButton;

        private readonly TextBox filterTextBox;
        private readonly CheckBox showUnavailablesFilterCheckBox;
        private readonly Button loadEnabledFromSaveButton;
        private readonly Button setEnabledFromSciencePacksButton;
        private readonly Button enableAllButton;
        private readonly ListBox assemblerListView;
        private readonly ListBox minerListView;
        private readonly ListBox powerListView;
        private readonly ListBox beaconListView;
        private readonly ListBox moduleListView;
        private readonly ListBox recipeListView;
        private readonly ListBox qualityListView;

        private readonly NumericUpDown qualityStepsInput;
        private readonly RadioButton lowLodRadioButton;
        private readonly RadioButton mediumLodRadioButton;
        private readonly RadioButton highLodRadioButton;
        private readonly NumericUpDown nodeCountForSimpleViewInput;
        private readonly NumericUpDown iconsSizeInput;
        private readonly CheckBox arrowsOnLinksCheckBox;
        private readonly CheckBox dynamicLWCheckBox;
        private readonly CheckBox abbreviateSciPackCheckBox;
        private readonly CheckBox showNodeRecipeCheckBox;
        private readonly CheckBox roundAssemblerCountCheckBox;
        private readonly CheckBox recipeEditPanelPositionLockCheckBox;
        private readonly CheckBox flagOUSupplyNodesCheckBox;
        private readonly CheckBox flagDarkModeCheckBox;
        private readonly CheckBox errorArrowsCheckBox;
        private readonly CheckBox warningArrowsCheckBox;
        private readonly CheckBox disconnectedArrowsCheckBox;
        private readonly CheckBox ouSuppliedArrowsCheckBox;
        private readonly ComboBox assemblerSelectorStyleDropDown;
        private readonly ComboBox moduleSelectorStyleDropDown;
        private readonly ComboBox nodeDirectionDropDown;
        private readonly CheckBox smartNodeDirectionCheckBox;
        private readonly CheckBox simplePassthroughNodesCheckBox;
        private readonly CheckBox showProductivityBonusOnAllCheckBox;
        private readonly CheckBox showUnavailablesCheckBox;
        private readonly CheckBox loadBarrelingCheckBox;
        private readonly NumericUpDown lowPriorityPowerInput;
        private readonly CheckBox pullConsumerNodesCheckBox;
        private readonly NumericUpDown pullConsumerNodesPowerInput;

        private readonly List<EnabledObjectsListItem> unfilteredAssemblerList = [];
        private readonly List<EnabledObjectsListItem> unfilteredMinerList = [];
        private readonly List<EnabledObjectsListItem> unfilteredPowerList = [];
        private readonly List<EnabledObjectsListItem> unfilteredBeaconList = [];
        private readonly List<EnabledObjectsListItem> unfilteredModuleList = [];
        private readonly List<EnabledObjectsListItem> unfilteredRecipeList = [];
        private readonly List<EnabledObjectsListItem> unfilteredQualityList = [];

        //Dedups baked icons by source SKBitmap reference, same intent as upstream's EnabledObjectsIconIndex
        //(reference §5) - many recipes/items share the one prototype bitmap.
        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

        public SettingsWindowOptions Options { get; }

        //Injectable seam for "Load from save" (mirrors MainWindow.SettingsService): defaults to the real
        //settings.json when unset, so tests can point it at a temp-directory-scoped instance instead.
        public SettingsService? SettingsService { get; set; }

        //Test-only seam: lets a test supply the delete confirmation answer without a real modal dialog.
        internal Func<Preset, Task<bool>>? DeleteConfirmationStub { get; set; }
        internal bool? DialogResultValue { get; private set; }

        public SettingsWindow() : this(new SettingsWindowOptions(new DataCache(false))) {
        }

        public SettingsWindow(SettingsWindowOptions options) {
            InitializeComponent();
            Options = options;

            currentPresetLabel = this.FindControl<TextBlock>("CurrentPresetLabel")!;
            modSelectionBox = this.FindControl<ListBox>("ModSelectionBox")!;
            presetListBox = this.FindControl<ListBox>("PresetListBox")!;
            selectPresetMenuItem = this.FindControl<MenuItem>("SelectPresetMenuItem")!;
            deletePresetMenuItem = this.FindControl<MenuItem>("DeletePresetMenuItem")!;
            importPresetButton = this.FindControl<Button>("ImportPresetButton")!;
            comparePresetsButton = this.FindControl<Button>("ComparePresetsButton")!;
            confirmButton = this.FindControl<Button>("ConfirmButton")!;
            cancelButton = this.FindControl<Button>("CancelSettingsButton")!;

            filterTextBox = this.FindControl<TextBox>("FilterTextBox")!;
            showUnavailablesFilterCheckBox = this.FindControl<CheckBox>("ShowUnavailablesFilterCheckBox")!;
            loadEnabledFromSaveButton = this.FindControl<Button>("LoadEnabledFromSaveButton")!;
            setEnabledFromSciencePacksButton = this.FindControl<Button>("SetEnabledFromSciencePacksButton")!;
            enableAllButton = this.FindControl<Button>("EnableAllButton")!;
            assemblerListView = this.FindControl<ListBox>("AssemblerListView")!;
            minerListView = this.FindControl<ListBox>("MinerListView")!;
            powerListView = this.FindControl<ListBox>("PowerListView")!;
            beaconListView = this.FindControl<ListBox>("BeaconListView")!;
            moduleListView = this.FindControl<ListBox>("ModuleListView")!;
            recipeListView = this.FindControl<ListBox>("RecipeListView")!;
            qualityListView = this.FindControl<ListBox>("QualityListView")!;

            qualityStepsInput = this.FindControl<NumericUpDown>("QualityStepsInput")!;
            lowLodRadioButton = this.FindControl<RadioButton>("LowLodRadioButton")!;
            mediumLodRadioButton = this.FindControl<RadioButton>("MediumLodRadioButton")!;
            highLodRadioButton = this.FindControl<RadioButton>("HighLodRadioButton")!;
            nodeCountForSimpleViewInput = this.FindControl<NumericUpDown>("NodeCountForSimpleViewInput")!;
            iconsSizeInput = this.FindControl<NumericUpDown>("IconsSizeInput")!;
            arrowsOnLinksCheckBox = this.FindControl<CheckBox>("ArrowsOnLinksCheckBox")!;
            dynamicLWCheckBox = this.FindControl<CheckBox>("DynamicLWCheckBox")!;
            abbreviateSciPackCheckBox = this.FindControl<CheckBox>("AbbreviateSciPackCheckBox")!;
            showNodeRecipeCheckBox = this.FindControl<CheckBox>("ShowNodeRecipeCheckBox")!;
            roundAssemblerCountCheckBox = this.FindControl<CheckBox>("RoundAssemblerCountCheckBox")!;
            recipeEditPanelPositionLockCheckBox = this.FindControl<CheckBox>("RecipeEditPanelPositionLockCheckBox")!;
            flagOUSupplyNodesCheckBox = this.FindControl<CheckBox>("FlagOUSupplyNodesCheckBox")!;
            flagDarkModeCheckBox = this.FindControl<CheckBox>("FlagDarkModeCheckBox")!;
            errorArrowsCheckBox = this.FindControl<CheckBox>("ErrorArrowsCheckBox")!;
            warningArrowsCheckBox = this.FindControl<CheckBox>("WarningArrowsCheckBox")!;
            disconnectedArrowsCheckBox = this.FindControl<CheckBox>("DisconnectedArrowsCheckBox")!;
            ouSuppliedArrowsCheckBox = this.FindControl<CheckBox>("OUSuppliedArrowsCheckBox")!;
            assemblerSelectorStyleDropDown = this.FindControl<ComboBox>("AssemblerSelectorStyleDropDown")!;
            moduleSelectorStyleDropDown = this.FindControl<ComboBox>("ModuleSelectorStyleDropDown")!;
            nodeDirectionDropDown = this.FindControl<ComboBox>("NodeDirectionDropDown")!;
            smartNodeDirectionCheckBox = this.FindControl<CheckBox>("SmartNodeDirectionCheckBox")!;
            simplePassthroughNodesCheckBox = this.FindControl<CheckBox>("SimplePassthroughNodesCheckBox")!;
            showProductivityBonusOnAllCheckBox = this.FindControl<CheckBox>("ShowProductivityBonusOnAllCheckBox")!;
            showUnavailablesCheckBox = this.FindControl<CheckBox>("ShowUnavailablesCheckBox")!;
            loadBarrelingCheckBox = this.FindControl<CheckBox>("LoadBarrelingCheckBox")!;
            lowPriorityPowerInput = this.FindControl<NumericUpDown>("LowPriorityPowerInput")!;
            pullConsumerNodesCheckBox = this.FindControl<CheckBox>("PullConsumerNodesCheckBox")!;
            pullConsumerNodesPowerInput = this.FindControl<NumericUpDown>("PullConsumerNodesPowerInput")!;

            currentPresetLabel.Text = Options.SelectedPreset?.Name;
            currentPresetLabel.PointerPressed += (_, _) => presetListBox.SelectedItem = null;

            presetListBox.ItemsSource = (Options.Presets ?? []).Where(p => !p.IsCurrentlySelected).ToList();
            //Ports UpdatePresetLabel's bold toggle (reference SettingsForm.cs:263-265): bold means "this is
            //still the active preset" - it drops the moment the list picks out a different candidate.
            presetListBox.SelectionChanged += (_, _) => {
                UpdateModList();
                currentPresetLabel.FontWeight = presetListBox.SelectedItem is null ? FontWeight.Bold : FontWeight.Normal;
            };
            presetListBox.DoubleTapped += (_, _) => {
                if (presetListBox.SelectedItem is Preset preset)
                    SelectPreset(preset);
            };
            presetListBox.AddHandler(Control.ContextRequestedEvent, OnPresetListBoxContextRequested);

            selectPresetMenuItem.Click += (_, _) => {
                if (presetListBox.SelectedItem is Preset preset)
                    SelectPreset(preset);
            };
            deletePresetMenuItem.Click += (_, _) => {
                if (presetListBox.SelectedItem is Preset preset)
                    Async.Fire(DeletePresetAsync(preset), nameof(DeletePresetAsync));
            };

            importPresetButton.Click += (_, _) => Async.Fire(ShowImportPresetAsync(), nameof(ShowImportPresetAsync));
            comparePresetsButton.Click += (_, _) => Async.Fire(ShowComparePresetsAsync(), nameof(ShowComparePresetsAsync));

            confirmButton.Click += (_, _) => ApplyConfirm();
            cancelButton.Click += (_, _) => ApplyCancel();

            //TextChanging (not TextChanged) - TextChanged defers via Dispatcher.UIThread.Post (see
            //EditFlowPanel's equivalent comment), TextChanging fires synchronously per keystroke, matching
            //upstream's immediate per-keystroke Filters_Changed (reference SettingsForm.cs:338-340).
            filterTextBox.TextChanging += (_, _) => UpdateFilteredLists();
            showUnavailablesFilterCheckBox.IsCheckedChanged += (_, _) => UpdateFilteredLists();
            loadEnabledFromSaveButton.Click += (_, _) => Async.Fire(ShowLoadFromSaveAsync(), nameof(ShowLoadFromSaveAsync));
            setEnabledFromSciencePacksButton.Click += (_, _) => Async.Fire(ShowAssignFromSciencePacksAsync(), nameof(ShowAssignFromSciencePacksAsync));
            enableAllButton.Click += (_, _) => EnableAll();

            foreach (ListBox list in AllListViews)
                WireSelectAllShortcut(list);

            LoadGraphOptionsTab();

            UpdateModList();
            LoadUnfilteredLists();
        }

        //Ports the Graph Options tab's constructor read (reference SettingsForm.cs:121-174): seeds every
        //widget from Options once, up front - Confirm/preset-switch commits go back through
        //CommitPendingChanges below, matching upstream's constructor-read / UpdateSettings-write split.
        private void LoadGraphOptionsTab() {
            qualityStepsInput.Value = Options.QualitySteps;

            switch (Options.LevelOfDetail) {
                case LevelOfDetail.Low:
                    lowLodRadioButton.IsChecked = true;
                    break;
                case LevelOfDetail.High:
                    highLodRadioButton.IsChecked = true;
                    break;
                default:
                    mediumLodRadioButton.IsChecked = true;
                    break;
            }
            nodeCountForSimpleViewInput.Value = Math.Min(nodeCountForSimpleViewInput.Maximum, Options.NodeCountForSimpleView);
            iconsSizeInput.Value = Options.IconsOnlyIconSize;

            arrowsOnLinksCheckBox.IsChecked = Options.ArrowsOnLinks;
            simplePassthroughNodesCheckBox.IsChecked = Options.SimplePassthroughNodes;
            dynamicLWCheckBox.IsChecked = Options.DynamicLinkWidth;
            abbreviateSciPackCheckBox.IsChecked = Options.AbbreviateSciPacks;
            showNodeRecipeCheckBox.IsChecked = Options.ShowRecipeToolTip;
            roundAssemblerCountCheckBox.IsChecked = Options.RoundAssemblerCount;
            recipeEditPanelPositionLockCheckBox.IsChecked = Options.LockedRecipeEditPanelPosition;
            flagOUSupplyNodesCheckBox.IsChecked = Options.FlagOUSuppliedNodes;
            flagDarkModeCheckBox.IsChecked = Options.FlagDarkMode == ThemeMode.Dark;

            errorArrowsCheckBox.IsChecked = Options.ShowErrorArrows;
            warningArrowsCheckBox.IsChecked = Options.ShowWarningArrows;
            disconnectedArrowsCheckBox.IsChecked = Options.ShowDisconnectedArrows;
            ouSuppliedArrowsCheckBox.IsChecked = Options.ShowOUSuppliedArrows;

            nodeDirectionDropDown.SelectedIndex = Options.DefaultNodeDirection == NodeDirection.Down ? 1 : 0;
            smartNodeDirectionCheckBox.IsChecked = Options.SmartNodeDirection;

            assemblerSelectorStyleDropDown.ItemsSource = AssemblerSelector.StyleNames;
            assemblerSelectorStyleDropDown.SelectedIndex = (int)Options.DefaultAssemblerStyle;
            moduleSelectorStyleDropDown.ItemsSource = ModuleSelector.StyleNames;
            moduleSelectorStyleDropDown.SelectedIndex = (int)Options.DefaultModuleStyle;

            showProductivityBonusOnAllCheckBox.IsChecked = Options.EnableExtraProductivityForNonMiners;
            showUnavailablesCheckBox.IsChecked = Options.DevShowUnavailableItems;
            loadBarrelingCheckBox.IsChecked = !Options.DevUseRecipeBWFilters;

            lowPriorityPowerInput.Value = Math.Min(lowPriorityPowerInput.Maximum, Options.SolverLowPriorityPower);
            pullConsumerNodesCheckBox.IsChecked = Options.SolverPullConsumerNodes;
            pullConsumerNodesPowerInput.Value = Math.Min(pullConsumerNodesPowerInput.Maximum, Options.SolverPullConsumerNodesPower);
        }

        //Ports UpdateSettings' Graph Options half (reference SettingsForm.cs:429-475): every widget on this
        //tab writes back into Options only from here, called by CommitPendingChanges below - never live-on-
        //click (that's Enabled Objects' own membership toggle, reference §5).
        private void CommitGraphOptionsTab() {
            Options.QualitySteps = (uint)qualityStepsInput.Value.GetValueOrDefault();

            Options.LevelOfDetail = lowLodRadioButton.IsChecked == true ? LevelOfDetail.Low
                : highLodRadioButton.IsChecked == true ? LevelOfDetail.High
                : LevelOfDetail.Medium;
            Options.NodeCountForSimpleView = (int)nodeCountForSimpleViewInput.Value.GetValueOrDefault();
            Options.IconsOnlyIconSize = (int)iconsSizeInput.Value.GetValueOrDefault();

            Options.ArrowsOnLinks = arrowsOnLinksCheckBox.IsChecked == true;
            Options.SimplePassthroughNodes = simplePassthroughNodesCheckBox.IsChecked == true;
            Options.DynamicLinkWidth = dynamicLWCheckBox.IsChecked == true;
            Options.AbbreviateSciPacks = abbreviateSciPackCheckBox.IsChecked == true;
            Options.ShowRecipeToolTip = showNodeRecipeCheckBox.IsChecked == true;
            Options.RoundAssemblerCount = roundAssemblerCountCheckBox.IsChecked == true;
            Options.LockedRecipeEditPanelPosition = recipeEditPanelPositionLockCheckBox.IsChecked == true;
            Options.FlagOUSuppliedNodes = flagOUSupplyNodesCheckBox.IsChecked == true;
            //Upstream's checkbox is binary (MainForm.cs:36-46: checked -> SetDarkMode, unchecked -> SetLightMode,
            //no OS-follow option) - unchecked must force Light, not System, or an OS already in dark appearance
            //leaves the app dark despite the user unchecking the box.
            Options.FlagDarkMode = flagDarkModeCheckBox.IsChecked == true ? ThemeMode.Dark : ThemeMode.Light;

            Options.ShowErrorArrows = errorArrowsCheckBox.IsChecked == true;
            Options.ShowWarningArrows = warningArrowsCheckBox.IsChecked == true;
            Options.ShowDisconnectedArrows = disconnectedArrowsCheckBox.IsChecked == true;
            Options.ShowOUSuppliedArrows = ouSuppliedArrowsCheckBox.IsChecked == true;

            Options.DefaultAssemblerStyle = (AssemblerSelector.Style)assemblerSelectorStyleDropDown.SelectedIndex;
            Options.DefaultModuleStyle = (ModuleSelector.Style)moduleSelectorStyleDropDown.SelectedIndex;
            Options.DefaultNodeDirection = nodeDirectionDropDown.SelectedIndex == 1 ? NodeDirection.Down : NodeDirection.Up;
            Options.SmartNodeDirection = smartNodeDirectionCheckBox.IsChecked == true;

            Options.EnableExtraProductivityForNonMiners = showProductivityBonusOnAllCheckBox.IsChecked == true;
            Options.DevShowUnavailableItems = showUnavailablesCheckBox.IsChecked == true;
            Options.DevUseRecipeBWFilters = loadBarrelingCheckBox.IsChecked != true;

            Options.SolverLowPriorityPower = lowPriorityPowerInput.Value.GetValueOrDefault();
            Options.SolverPullConsumerNodes = pullConsumerNodesCheckBox.IsChecked == true;
            Options.SolverPullConsumerNodesPower = pullConsumerNodesPowerInput.Value.GetValueOrDefault();
        }

        private IEnumerable<ListBox> AllListViews {
            get {
                yield return assemblerListView;
                yield return minerListView;
                yield return powerListView;
                yield return beaconListView;
                yield return moduleListView;
                yield return recipeListView;
                yield return qualityListView;
            }
        }

        private IEnumerable<List<EnabledObjectsListItem>> AllUnfilteredLists {
            get {
                yield return unfilteredAssemblerList;
                yield return unfilteredMinerList;
                yield return unfilteredPowerList;
                yield return unfilteredBeaconList;
                yield return unfilteredModuleList;
                yield return unfilteredRecipeList;
                yield return unfilteredQualityList;
            }
        }

        //Ports LoadUnfilteredLists (reference SettingsForm.cs:198-230): per-category source, ordered
        //Available-first then FriendlyName (Qualities instead sort by IDataObjectBase's own IComparable,
        //matching LoadUnfilteredList's origin-is-IEnumerable<IQuality> branch).
        private void LoadUnfilteredLists() {
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(a => a.EntityType == EntityType.Assembler), unfilteredAssemblerList, assemblerListView);
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(a => a.EntityType is EntityType.Miner or EntityType.OffshorePump), unfilteredMinerList, minerListView);
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(a => a.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor), unfilteredPowerList, powerListView);
            LoadUnfilteredList(Options.DCache.Beacons.Values, unfilteredBeaconList, beaconListView);
            LoadUnfilteredList(Options.DCache.Modules.Values, unfilteredModuleList, moduleListView);
            LoadUnfilteredList(Options.DCache.Recipes.Values, unfilteredRecipeList, recipeListView);
            LoadUnfilteredList(Options.DCache.Qualities.Values, unfilteredQualityList, qualityListView, sortByQualityOrder: true);

            foreach (EnabledObjectsListItem item in unfilteredRecipeList)
                if (item.DataObject is IRecipe recipe)
                    item.TooltipContent = new Image { Source = BakeRecipeTooltip(recipe) };

            UpdateFilteredLists();
        }

        private void LoadUnfilteredList<T>(IEnumerable<T> origin, List<EnabledObjectsListItem> destination, ListBox list, bool sortByQualityOrder = false) where T : IDataObjectBase {
            IEnumerable<T> ordered = sortByQualityOrder
                ? origin.OrderByDescending(o => o.Available).ThenBy(o => (IDataObjectBase)o)
                : origin.OrderByDescending(o => o.Available).ThenBy(o => o.FriendlyName);

            foreach (T dataObject in ordered) {
                var item = new EnabledObjectsListItem(dataObject, GetOrBakeIcon(dataObject.Icon)) {
                    IsChecked = Options.EnabledObjects.Contains(dataObject)
                };
                item.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(EnabledObjectsListItem.IsChecked))
                        SetEnabledMembership(item, list);
                };
                destination.Add(item);
            }
        }

        //Ports the live-toggle half of ListView_MouseClick/MouseDoubleClick (reference SettingsForm.cs:349-402):
        //upstream writes straight into Options.EnabledObjects on every click, no UpdateSettings() commit step.
        //Also ports both methods' selected-rows bulk toggle: a row that's part of the active multi-selection
        //carries its own new checked state onto every other selected row, rather than toggling independently.
        private void SetEnabledMembership(EnabledObjectsListItem item, ListBox list) {
            if (item.IsChecked)
                Options.EnabledObjects.Add(item.DataObject);
            else
                Options.EnabledObjects.Remove(item.DataObject);

            if (list.SelectedItems is { } selected && selected.Contains(item))
                foreach (object? selectedObj in selected)
                    if (selectedObj is EnabledObjectsListItem selectedItem && selectedItem != item)
                        selectedItem.IsChecked = item.IsChecked;
        }

        private Bitmap GetOrBakeIcon(SKBitmap icon) {
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
            return SnapshotToBitmap(surface);
        }

        //Bakes RecipePainter's (already-ported, docs/panels-reference.md §7) single-recipe layout into a
        //static image for the row's ToolTip.Tip - the same GPU-lease-avoidance bake used for icons above,
        //since this window isn't GraphCanvasControl's own top-level composited surface (see IconButton's
        //comment on nested Skia leases corrupting the frame on macOS).
        private static Bitmap BakeRecipeTooltip(IRecipe recipe) {
            IRecipe[] recipes = [recipe];
            System.Drawing.Size size = RecipePainter.GetSize(recipes, abbreviateSciPacks: false);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(Math.Max(1, size.Width), Math.Max(1, size.Height), SKColorType.Bgra8888, SKAlphaType.Premul));
            RecipePainter.Paint(recipes, surface.Canvas, new DrawingPoint(0, 0), abbreviateSciPacks: false);
            return SnapshotToBitmap(surface);
        }

        private static Bitmap SnapshotToBitmap(SKSurface surface) {
            using SKPixmap pixmap = surface.PeekPixels();
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixmap.Info.Width, pixmap.Info.Height), new Vector(96, 96), pixmap.RowBytes);
        }

        //Ports UpdateFilteredList (reference SettingsForm.cs:242-255): reassigning ItemsSource each call
        //(rather than upstream's Clear+AddRange into a fixed VirtualListSize) sidesteps that WinForms
        //virtual-mode bookkeeping entirely - Avalonia's ItemsSource swap is inherently consistent.
        private void UpdateFilteredLists() {
            assemblerListView.ItemsSource = FilterList(unfilteredAssemblerList);
            minerListView.ItemsSource = FilterList(unfilteredMinerList);
            powerListView.ItemsSource = FilterList(unfilteredPowerList);
            beaconListView.ItemsSource = FilterList(unfilteredBeaconList);
            moduleListView.ItemsSource = FilterList(unfilteredModuleList);
            recipeListView.ItemsSource = FilterList(unfilteredRecipeList);
            qualityListView.ItemsSource = FilterList(unfilteredQualityList);
        }

        private List<EnabledObjectsListItem> FilterList(List<EnabledObjectsListItem> unfiltered) {
            string filter = filterTextBox.Text ?? "";
            bool showUnavailables = showUnavailablesFilterCheckBox.IsChecked ?? false;

            return [.. unfiltered.Where(item =>
                (showUnavailables || item.DataObject.Available) &&
                (string.IsNullOrEmpty(filter) || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))];
        }

        //Ports EnableAllButton_Click (reference SettingsForm.cs:559-580) verbatim.
        private void EnableAll() {
            Options.EnabledObjects.Clear();
            if (Options.DCache.PlayerAssembler is IAssembler playerAssembler)
                Options.EnabledObjects.Add(playerAssembler);

            foreach (IAssembler assembler in Options.DCache.Assemblers.Values.Where(a => a.AssociatedItems.Any(i => i.Available)))
                Options.EnabledObjects.Add(assembler);
            foreach (IBeacon beacon in Options.DCache.Beacons.Values.Where(b => b.AssociatedItems.Any(i => i.Available)))
                Options.EnabledObjects.Add(beacon);
            foreach (IModule module in Options.DCache.Modules.Values.Where(m => m.AssociatedItem.Available))
                Options.EnabledObjects.Add(module);
            foreach (IRecipe recipe in Options.DCache.Recipes.Values.Where(r => r.Available))
                Options.EnabledObjects.Add(recipe);
            foreach (IQuality quality in Options.DCache.Qualities.Values.Where(q => q.Available))
                Options.EnabledObjects.Add(quality);

            UpdateEnabledStatus();
        }

        //Ports UpdateEnabledStatus's Checked-refresh half (reference SettingsForm.cs:582-637) - no
        //VirtualListSize juggling needed here, see UpdateFilteredLists above.
        private void UpdateEnabledStatus() {
            foreach (List<EnabledObjectsListItem> list in AllUnfilteredLists)
                foreach (EnabledObjectsListItem item in list)
                    item.IsChecked = Options.EnabledObjects.Contains(item.DataObject);

            UpdateFilteredLists();
        }

        //Ports ListView_KeyDown's Ctrl+A (reference SettingsForm.cs:344-347), Cmd on macOS/Ctrl on Linux
        //(PlatformModifiers.Primary): selects every row in this sub-tab via NativeMethods.SelectAllItems's
        //equivalent, independent of check state. Tunnel routing, ahead of ListBox's own bubble-phase key
        //handling (arrow navigation, type-ahead), so the shortcut always wins here regardless of what a
        //focused row would otherwise do with it.
        private static void WireSelectAllShortcut(ListBox list) =>
            list.AddHandler(InputElement.KeyDownEvent, (_, e) => {
                if (e.Key == Key.A && e.KeyModifiers.HasFlag(PlatformModifiers.Primary)) {
                    SelectAllRows(list);
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);

        private static void SelectAllRows(ListBox list) {
            if (list.ItemsSource is not IEnumerable items || list.SelectedItems is not { } selected)
                return;
            selected.Clear();
            foreach (object? item in items)
                selected.Add(item);
        }

        //Test-only seam: lets a test drive SaveFileLoadWindow's own OpenSaveFilePathStub/LoadPipelineStub
        //seams directly (calling RunAsync itself) instead of a real modal ShowDialog, then still runs the
        //real outcome-handling tail below - same "replace only the launch" convention as
        //ComparePresetsDialogStub.
        internal Func<SaveFileLoadWindow, Task>? LoadFromSaveDialogStub { get; set; }

        //Ports LoadEnabledFromSaveButton_Click (reference §5, SettingsForm.cs:535-545): opens
        //SaveFileLoadWindow at owner+50/+50. OK refreshes the checked state from the now-updated
        //Options.EnabledObjects; Abort shows the caller's own verbatim message (upstream keeps this string
        //on SettingsForm, not the dialog itself); Cancel does nothing.
        private async Task ShowLoadFromSaveAsync() {
            var settingsService = SettingsService ?? new SettingsService();
            AppSettings settings = settingsService.Load();
            var window = new SaveFileLoadWindow(Options.DCache, Options.EnabledObjects, settings.LastSaveFileLocation) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };

            await (LoadFromSaveDialogStub?.Invoke(window) ?? window.ShowDialog(this)).ConfigureAwait(true);

            if (window.Outcome == SaveFileLoadOutcome.Ok) {
                if (window.ResolvedSaveFileLocation is string resolvedLocation) {
                    settings.LastSaveFileLocation = resolvedLocation;
                    settingsService.Save(settings);
                    Options.LastSaveFileLocation = resolvedLocation;
                }
                UpdateEnabledStatus();
            } else if (window.Outcome == SaveFileLoadOutcome.Abort) {
                await ShowWarningAsync("",
                    "Error while reading save file. Try running factorio, opening the save game, saving again, and retrying?").ConfigureAwait(true);
            }
        }

        //Test-only seam: lets a test drive SciencePacksWindow's own seams directly instead of a real modal
        //ShowDialog - same "replace only the launch" convention as LoadFromSaveDialogStub.
        internal Func<SciencePacksWindow, Task>? AssignFromSciencePacksDialogStub { get; set; }

        //Ports SetEnabledFromSciencePacksButton_Click (reference §6, SettingsForm.cs:547-557): opens
        //SciencePacksWindow at owner+50/+50; OK refreshes the checked state from the now-updated
        //Options.EnabledObjects, Cancel does nothing.
        private async Task ShowAssignFromSciencePacksAsync() {
            var window = new SciencePacksWindow(Options.DCache, Options.EnabledObjects) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };

            await (AssignFromSciencePacksDialogStub?.Invoke(window) ?? window.ShowDialog(this)).ConfigureAwait(true);

            if (window.Accepted)
                UpdateEnabledStatus();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void SelectPreset(Preset preset) {
            CommitPendingChanges();
            Options.SelectedPreset = preset;
            CloseWithResult(true);
        }

        //Test-only seam: counts calls alongside the real committed values below.
        internal int CommitPendingChangesCallCount { get; private set; }

        //Every early-close path must call this (upstream calls UpdateSettings() from all of them: double-click
        //line 303, "Use This Preset" line 333, Confirm line 429, import auto-switch 508/513) - the Graph
        //Options tab's widget commits route through here, not live-on-click.
        private void CommitPendingChanges() {
            CommitPendingChangesCallCount++;
            CommitGraphOptionsTab();
        }

        //Ports UpdateModList's ModSelectionBox half (reference SettingsForm.cs:181-196) - the difficulty
        //labels UpdateModList also sets belong to groupBox4, skipped per §5 (hidden dead UI).
        private void UpdateModList() {
            Preset? selected = presetListBox.SelectedItem as Preset ?? Options.SelectedPreset;
            modSelectionBox.ItemsSource = null;
            if (selected is null)
                return;

            PresetInfo info = PresetProcessor.ReadPresetInfo(selected);
            if (info.ModList is null)
                return;

            var modList = info.ModList.Select(kvp => kvp.Key + "_" + kvp.Value).ToList();
            modList.Sort(StringComparer.Ordinal);
            modSelectionBox.ItemsSource = modList;
        }

        private void OnPresetListBoxContextRequested(object? sender, ContextRequestedEventArgs e) {
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is not { DataContext: Preset preset }) {
                e.Handled = true;
                return;
            }

            presetListBox.SelectedItem = preset;
            UpdatePresetMenuCaptionsFor(preset);
        }

        //Ports PresetListBox_MouseDown's caption logic (reference SettingsForm.cs:268-297): within
        //PresetListBox the active preset was already excluded, so IsCurrentlySelected can't actually be
        //true here - kept anyway since upstream keeps the branch too (line-for-line discipline).
        internal void UpdatePresetMenuCaptionsFor(Preset preset) {
            selectPresetMenuItem.Header = preset.IsCurrentlySelected ? "Current Preset" : "Use This Preset";
            selectPresetMenuItem.IsEnabled = !preset.IsCurrentlySelected;
            if (preset.IsDefaultPreset) {
                deletePresetMenuItem.Header = "Default Preset";
                deletePresetMenuItem.IsEnabled = false;
            } else {
                deletePresetMenuItem.Header = "Delete This Preset";
                deletePresetMenuItem.IsEnabled = !preset.IsCurrentlySelected;
            }
        }

        private async Task DeletePresetAsync(Preset preset) {
            bool confirmed = await ConfirmDeleteAsync(preset).ConfigureAwait(true);
            if (!confirmed)
                return;

            //File-location policy (docs/upstream-divergences.md): delete only ever targets the user's
            //writable Presets directory - a bundle-shipped preset (read via GetPresetPath's fallback) is
            //never touched here.
            foreach (string extension in new[] { ".pjson", ".json", ".dat" }) {
                string path = PresetProcessor.GetUserPresetPath(preset.Name, extension);
                if (File.Exists(path))
                    File.Delete(path);
            }

            Options.Presets?.Remove(preset);
            presetListBox.ItemsSource = (Options.Presets ?? []).Where(p => !p.IsCurrentlySelected).ToList();
        }

        private Task<bool> ConfirmDeleteAsync(Preset preset) =>
            DeleteConfirmationStub?.Invoke(preset) ?? Dialogs.ShowConfirmAsync(this, "Confirm Delete",
                "Are you sure you wish to delete the \"" + preset.Name + "\" preset? This is irreversible.");

        //Test-only seam: lets a test capture a warning dialog's title/message without a real modal window.
        internal Func<string, string, Task>? WarningDialogStub { get; set; }

        private Task ShowWarningAsync(string title, string message) =>
            WarningDialogStub?.Invoke(title, message) ?? Dialogs.ShowWarningAsync(this, title, message);

        //Test-only seam: lets a test drive PresetImportWindow's own seams directly instead of a real modal
        //ShowDialog (see LoadFromSaveDialogStub for the established convention).
        internal Func<PresetImportWindow, Task>? ImportPresetDialogStub { get; set; }

        //Ports ImportPresetButton_Click (reference §4/§7, SettingsForm.cs:484-513): opens PresetImportWindow
        //at owner+250/+50 - upstream's own offset for this one dialog, wider than the +50/+50 every other
        //Settings-launched window uses, since Import's own window is itself already positioned +50/+50 off
        //MainForm and would otherwise land right on top of Settings. On success either the overwritten-active
        //branch (RequireReload, closes Settings) or the switch-to-new-preset prompt (Yes closes Settings via
        //the existing SelectPreset helper, No leaves Settings open with the new preset already in the list).
        private async Task ShowImportPresetAsync() {
            var window = new PresetImportWindow(Options.Presets ?? []) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(250, 50),
            };

            await (ImportPresetDialogStub?.Invoke(window) ?? window.ShowDialog(this)).ConfigureAwait(true);

            if (window.ImportStarted)
                GC.Collect(); //a preset export opens a lot of zip files and processes a lot of bitmaps - large mod packs can leave 2GB+ of memory stuck in garbage otherwise.

            if (window.DialogResultValue != true || string.IsNullOrEmpty(window.NewPresetName))
                return;

            Preset? newPreset = Options.Presets?.FirstOrDefault(p => string.Equals(p.Name, window.NewPresetName, StringComparison.OrdinalIgnoreCase));
            if (newPreset is null) {
                newPreset = new Preset(window.NewPresetName, isCurrentlySelected: false, isDefaultPreset: false);
                Options.Presets?.Add(newPreset);
                presetListBox.ItemsSource = (Options.Presets ?? []).Where(p => !p.IsCurrentlySelected).ToList();
            }

            if (ReferenceEquals(newPreset, Options.Presets is { Count: > 0 } presets ? presets[0] : null)) {
                Options.RequireReload = true;
                CommitPendingChanges();
                CloseWithResult(true);
            } else if (await ShowSwitchToImportedPresetConfirmAsync().ConfigureAwait(true)) {
                SelectPreset(newPreset);
            }
        }

        //Test-only seam: lets a test supply the "switch to the new preset?" answer without a real modal
        //dialog (see DeleteConfirmationStub for the established convention).
        internal Func<Task<bool>>? ImportSwitchConfirmationStub { get; set; }

        private Task<bool> ShowSwitchToImportedPresetConfirmAsync() =>
            ImportSwitchConfirmationStub?.Invoke() ?? Dialogs.ShowConfirmAsync(this, "", "Preset import complete! Do you wish to switch to the new preset?");

        //Test-only seam: lets a test observe the real Compare Presets launch without an actual modal
        //ShowDialog (see ImportPresetButton's WarningDialogStub for the established convention).
        internal Func<List<Preset>, Task>? ComparePresetsDialogStub { get; set; }

        //Ports ComparePresetsButton_Click's <2-preset guard verbatim (reference SettingsForm.cs:520-524).
        private Task ShowComparePresetsAsync() {
            if ((Options.Presets?.Count ?? 0) < 2)
                return ShowWarningAsync("", "Can not compare presets!\n...you only have 1 preset :/");

            return ComparePresetsDialogStub?.Invoke(Options.Presets ?? []) ?? RealShowComparePresetsAsync();
        }

        //Ports ComparePresetsButton_Click's PresetComparatorForm launch (reference SettingsForm.cs:526-530):
        //Manual position at owner+50/+50, genuinely modal.
        private async Task RealShowComparePresetsAsync() {
            var window = new PresetComparatorWindow(Options.Presets ?? []) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };
            await window.ShowDialog(this).ConfigureAwait(true);
        }

        private void ApplyConfirm() {
            CommitPendingChanges();
            CloseWithResult(true);
        }
        private void ApplyCancel() => CloseWithResult(false);

        private void CloseWithResult(bool result) {
            DialogResultValue = result;
            Close(result);
        }

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these) - see ShapePropertiesWindow's
        //equivalent comment for the established convention this follows.
        internal ListBox PresetListBoxControl => presetListBox;
        internal TextBlock CurrentPresetLabelControl => currentPresetLabel;
        internal ListBox ModSelectionBoxControl => modSelectionBox;
        internal MenuItem SelectPresetMenuItemControl => selectPresetMenuItem;
        internal MenuItem DeletePresetMenuItemControl => deletePresetMenuItem;
        internal Button ImportPresetButtonControl => importPresetButton;
        internal Button ComparePresetsButtonControl => comparePresetsButton;

        internal void SimulateCurrentPresetLabelClick() => presetListBox.SelectedItem = null;
        internal void SimulateDoubleClickPreset(Preset preset) {
            presetListBox.SelectedItem = preset;
            SelectPreset(preset);
        }
        internal void SimulateSelectPresetMenuItemClick(Preset preset) {
            presetListBox.SelectedItem = preset;
            SelectPreset(preset);
        }
        internal Task SimulateDeletePresetMenuItemClickAsync(Preset preset) {
            presetListBox.SelectedItem = preset;
            return DeletePresetAsync(preset);
        }
        internal void SimulateConfirmClick() => ApplyConfirm();
        internal void SimulateCancelClick() => ApplyCancel();
        internal Task SimulateComparePresetsClickAsync() => ShowComparePresetsAsync();
        internal Task SimulateImportPresetClickAsync() => ShowImportPresetAsync();

        internal TextBox FilterTextBoxControl => filterTextBox;
        internal CheckBox ShowUnavailablesFilterCheckBoxControl => showUnavailablesFilterCheckBox;
        internal Button LoadEnabledFromSaveButtonControl => loadEnabledFromSaveButton;
        internal Button SetEnabledFromSciencePacksButtonControl => setEnabledFromSciencePacksButton;
        internal Button EnableAllButtonControl => enableAllButton;
        internal ListBox AssemblerListViewControl => assemblerListView;
        internal ListBox MinerListViewControl => minerListView;
        internal ListBox PowerListViewControl => powerListView;
        internal ListBox BeaconListViewControl => beaconListView;
        internal ListBox ModuleListViewControl => moduleListView;
        internal ListBox RecipeListViewControl => recipeListView;
        internal ListBox QualityListViewControl => qualityListView;

        internal NumericUpDown QualityStepsInputControl => qualityStepsInput;
        internal RadioButton LowLodRadioButtonControl => lowLodRadioButton;
        internal RadioButton MediumLodRadioButtonControl => mediumLodRadioButton;
        internal RadioButton HighLodRadioButtonControl => highLodRadioButton;
        internal NumericUpDown NodeCountForSimpleViewInputControl => nodeCountForSimpleViewInput;
        internal NumericUpDown IconsSizeInputControl => iconsSizeInput;
        internal CheckBox ArrowsOnLinksCheckBoxControl => arrowsOnLinksCheckBox;
        internal CheckBox DynamicLWCheckBoxControl => dynamicLWCheckBox;
        internal CheckBox AbbreviateSciPackCheckBoxControl => abbreviateSciPackCheckBox;
        internal CheckBox ShowNodeRecipeCheckBoxControl => showNodeRecipeCheckBox;
        internal CheckBox RoundAssemblerCountCheckBoxControl => roundAssemblerCountCheckBox;
        internal CheckBox RecipeEditPanelPositionLockCheckBoxControl => recipeEditPanelPositionLockCheckBox;
        internal CheckBox FlagOUSupplyNodesCheckBoxControl => flagOUSupplyNodesCheckBox;
        internal CheckBox FlagDarkModeCheckBoxControl => flagDarkModeCheckBox;
        internal CheckBox ErrorArrowsCheckBoxControl => errorArrowsCheckBox;
        internal CheckBox WarningArrowsCheckBoxControl => warningArrowsCheckBox;
        internal CheckBox DisconnectedArrowsCheckBoxControl => disconnectedArrowsCheckBox;
        internal CheckBox OUSuppliedArrowsCheckBoxControl => ouSuppliedArrowsCheckBox;
        internal ComboBox AssemblerSelectorStyleDropDownControl => assemblerSelectorStyleDropDown;
        internal ComboBox ModuleSelectorStyleDropDownControl => moduleSelectorStyleDropDown;
        internal ComboBox NodeDirectionDropDownControl => nodeDirectionDropDown;
        internal CheckBox SmartNodeDirectionCheckBoxControl => smartNodeDirectionCheckBox;
        internal CheckBox SimplePassthroughNodesCheckBoxControl => simplePassthroughNodesCheckBox;
        internal CheckBox ShowProductivityBonusOnAllCheckBoxControl => showProductivityBonusOnAllCheckBox;
        internal CheckBox ShowUnavailablesCheckBoxControl => showUnavailablesCheckBox;
        internal CheckBox LoadBarrelingCheckBoxControl => loadBarrelingCheckBox;
        internal NumericUpDown LowPriorityPowerInputControl => lowPriorityPowerInput;
        internal CheckBox PullConsumerNodesCheckBoxControl => pullConsumerNodesCheckBox;
        internal NumericUpDown PullConsumerNodesPowerInputControl => pullConsumerNodesPowerInput;

        internal void SimulateEnableAllClick() => EnableAll();
        internal Task SimulateLoadFromSaveClickAsync() => ShowLoadFromSaveAsync();
        internal Task SimulateAssignFromSciencePacksClickAsync() => ShowAssignFromSciencePacksAsync();
    }
}
