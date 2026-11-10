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
using Foreman.DataCaching.Storage;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DrawingPoint = System.Drawing.Point;
using TabStrip = Foreman.Mac.Kit.Controls.TabStrip;
using TabStripItem = Foreman.Mac.Kit.Controls.TabStripItem;

namespace Foreman.Mac.Views {
    public partial class SettingsWindow : Window, IThemeAware {
        public sealed class SettingsWindowOptions(DataCache cache) {
            public DataCache DCache { get; } = cache;
            public List<Preset>? Presets { get; set; } = [];
            public Preset? SelectedPreset { get; set; }
            public HashSet<IDataObjectBase> EnabledObjects { get; set; } = [];

            public string? LastSaveFileLocation { get; set; }
        }

        private readonly Settings.PresetsTab presetsTab = new();
        private readonly Settings.EnabledObjectsTab enabledObjectsTab = new();
        private readonly Settings.GeneralTab generalTab = new();
        private readonly Settings.AdvancedTab advancedTab = new();
        private readonly Settings.AnnotationsTab annotationsTab = new();
        private readonly Settings.AppearanceTab appearanceTab = new();

        private static readonly string[] PaneNames = ["Presets", "Enabled Objects", "General", "Advanced", "Annotations", "Appearance"];
        private static readonly string[] PaneGlyphKeys = [
            "Kit.Glyph.Settings.Presets.18", "Kit.Glyph.Settings.EnabledObjects.18", "Kit.Glyph.Settings.General.18",
            "Kit.Glyph.Settings.Advanced.18", "Kit.Glyph.Settings.Annotations.18", "Kit.Glyph.Settings.Appearance.18",
        ];

        private readonly TabStrip paneStrip;
        private readonly UserControl[] tabControls;
        private readonly ContentControl tabBody;
        private readonly TextBlock footerText;
        private readonly DockPanel footerRow;
        private readonly Button restoreDefaultsButton;
        private int activeTabIndex = -1;

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

        private const double FixedWidth = 520, FixedHeight = 480, MaxResizableWidth = 900, MaxResizableHeight = 760;
        private double appliedScale = 1;

        public SettingsWindowOptions Options { get; }

        public SettingsViewModel ViewModel { get; }

        //Injectable seam for "Load from save" (mirrors MainWindow.SettingsService): defaults to the real
        //settings.json when unset, so tests can point it at a temp-directory-scoped instance instead.
        public SettingsService? SettingsService { get; set; }

        public AppSettings? Settings { get; set; }

        public SettingsSession? Session { get; set; }

        internal event Action? ReloadRequested;
        internal void RequestReload() => ReloadRequested?.Invoke();

        //Test-only seam: lets a test supply the delete confirmation answer without a real modal dialog.
        internal Func<Preset, Task<bool>>? DeleteConfirmationStub { get; set; }

        public SettingsWindow() : this(new SettingsWindowOptions(new DataCache(false))) {
        }

        public SettingsWindow(SettingsWindowOptions options) {
            InitializeComponent();
            Options = options;
            ViewModel = new SettingsViewModel();
            DataContext = ViewModel;

            tabBody = this.FindControl<ContentControl>("TabBody")!;
            footerText = this.FindControl<TextBlock>("FooterText")!;
            footerText.Text = "Foreman 2 · " + AppVersion.VersionedDisplay;
            footerRow = this.FindControl<DockPanel>("FooterRow")!;
            tabBody.AddHandler<FocusChangedEventArgs>(GotFocusEvent, BringFocusedRowIntoView);
            restoreDefaultsButton = this.FindControl<Button>("RestoreDefaultsButton")!;

            paneStrip = this.FindControl<TabStrip>("PaneStrip")!;
            for (int i = 0; i < PaneNames.Length; i++)
                paneStrip.Items.Add(new TabStripItem {
                    Text = PaneNames[i],
                    Glyph = (Geometry)this.FindResource(PaneGlyphKeys[i])!,
                    ToolTip = $"{PaneNames[i]} — ⌘{i + 1}",
                });
            paneStrip.SelectionChanged += (_, _) => SelectTab(paneStrip.SelectedIndex);
            restoreDefaultsButton.Click += (_, _) => Async.Fire(RestoreDefaultsAsync(), nameof(RestoreDefaultsAsync));
            ViewModel.PropertyChanged += (_, _) => UpdateRestoreDefaults();
            tabControls = [presetsTab, enabledObjectsTab, generalTab, advancedTab, annotationsTab, appearanceTab];

            //DataContext reaches a tab by inheritance only once TabBody.Content attaches it, so an unselected tab needs it set here.
            enabledObjectsTab.DataContext = ViewModel;
            generalTab.DataContext = ViewModel;
            advancedTab.DataContext = ViewModel;
            annotationsTab.DataContext = ViewModel;
            appearanceTab.DataContext = ViewModel;

            WirePresetsTab();
            WireEnabledObjectsTab();

            SelectTab(0);

            currentPresetLabel.Text = Options.SelectedPreset?.Name;
            TextBlock currentVersion = presetsTab.CurrentPresetVersionTextControl;
            PresetCaption.SetNameTarget(currentVersion, currentPresetLabel);
            PresetCaption.SetPreset(currentVersion, Options.SelectedPreset);

            RefreshPresetList();
            RefreshModList();
            LoadUnfilteredLists();
            SheetHost.For(this);

            AppearanceHub.Current?.Subscribe(this);
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void RefreshTheme() {
            if (WindowScaler.CurrentScale != appliedScale) {
                RecordPaneSize(appliedScale);
                ApplyPaneSize();
            }
        }

        private void SelectTab(int index) {
            if (activeTabIndex == index)
                return;
            if (activeTabIndex >= 0)
                RecordPaneSize(appliedScale);
            activeTabIndex = index;
            paneStrip.SelectedIndex = index;
            tabBody.Content = tabControls[index];
            if (tabControls[index].Content is ScrollViewer paneScroller)
                paneScroller.Offset = default;
            Title = PaneNames[index];
            ApplyPaneSize();
            UpdateRestoreDefaults();
        }

        private void BringFocusedRowIntoView(object? sender, FocusChangedEventArgs e) {
            if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional
                && tabBody.Content is UserControl { Content: ScrollViewer } && e.Source is Visual source)
                source.FindAncestorOfType<Kit.Composites.FormRow>(includeSelf: true)?.BringIntoView();
        }

        private static bool IsResizable(int index) => (SettingsPane)index is SettingsPane.EnabledObjects or SettingsPane.Advanced;

        private (double Width, double Height) RememberedSize(int index) {
            (double? width, double? height) = (SettingsPane)index == SettingsPane.EnabledObjects
                ? (Settings?.SettingsEnabledObjectsWidth, Settings?.SettingsEnabledObjectsHeight)
                : (Settings?.SettingsAdvancedWidth, Settings?.SettingsAdvancedHeight);
            return (Math.Clamp(width ?? FixedWidth, FixedWidth, MaxResizableWidth), Math.Clamp(height ?? FixedHeight, FixedHeight, MaxResizableHeight));
        }

        private void ApplyPaneSize() {
            appliedScale = WindowScaler.CurrentScale;
            bool resizable = IsResizable(activeTabIndex);
            (double width, double height) = resizable ? RememberedSize(activeTabIndex) : (FixedWidth, FixedHeight);
            CanResize = resizable;
            MinWidth = FixedWidth * appliedScale;
            MinHeight = FixedHeight * appliedScale;
            MaxWidth = (resizable ? MaxResizableWidth : FixedWidth) * appliedScale;
            MaxHeight = (resizable ? MaxResizableHeight : FixedHeight) * appliedScale;
            Width = width * appliedScale;
            Height = height * appliedScale;
        }

        private void RecordPaneSize(double scale) {
            if (!IsResizable(activeTabIndex) || Settings is not AppSettings settings || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;
            double width = Math.Clamp(ClientSize.Width / scale, FixedWidth, MaxResizableWidth);
            double height = Math.Clamp(ClientSize.Height / scale, FixedHeight, MaxResizableHeight);
            if ((width, height) == RememberedSize(activeTabIndex))
                return;
            if ((SettingsPane)activeTabIndex == SettingsPane.EnabledObjects)
                (settings.SettingsEnabledObjectsWidth, settings.SettingsEnabledObjectsHeight) = (width, height);
            else
                (settings.SettingsAdvancedWidth, settings.SettingsAdvancedHeight) = (width, height);
            Session?.Apply("SettingsPaneSize");
        }

        private void UpdateRestoreDefaults() {
            var pane = (SettingsPane)activeTabIndex;
            restoreDefaultsButton.IsVisible = SettingsViewModel.HasDefaults(pane);
            restoreDefaultsButton.IsEnabled = !ViewModel.IsAtDefaults(pane);
        }

        private async Task RestoreDefaultsAsync() {
            var pane = (SettingsPane)activeTabIndex;
            if (!await Dialogs.ShowDestructiveConfirmAsync(this, $"Restore the default {PaneNames[activeTabIndex]} settings?",
                    "Every setting in this pane goes back to its original value. You can’t undo this action.", "Restore Defaults").ConfigureAwait(true))
                return;

            ViewModel.RestoreDefaults(pane);
            UpdateRestoreDefaults();
            if (restoreDefaultsButton.IsEnabled)
                restoreDefaultsButton.Focus();
            else if (tabControls[activeTabIndex].GetVisualDescendants().OfType<InputElement>()
                         .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible) is { } first)
                FocusWithoutScrolling(first);
        }

        private static void FocusWithoutScrolling(InputElement target) {
            //Focus raises RequestBringIntoView on the focused control; handling it at the source keeps the offset.
            EventHandler<RequestBringIntoViewEventArgs> keep = static (_, e) => e.Handled = true;
            target.AddHandler(RequestBringIntoViewEvent, keep);
            try {
                target.Focus();
            } finally {
                target.RemoveHandler(RequestBringIntoViewEvent, keep);
            }
        }

        internal void SelectPresetsPane() => SelectTab(0);

        internal void ShowPresetLoadFailure(string presetName) {
            SelectTab(0);
            presetListBox.SelectedItem = Options.Presets?.FirstOrDefault(p => p.Name == presetName);
            modListReadGeneration++;
            modSelectionBox.ItemsSource = null;
            presetsTab.ShowModListFailure(presetName);
        }

        internal TabStrip PaneStripControl => paneStrip;
        internal int ActiveTabIndex => activeTabIndex;
        internal TextBlock FooterTextControl => footerText;
        internal DockPanel FooterRowControl => footerRow;
        internal Button RestoreDefaultsButtonControl => restoreDefaultsButton;
        internal ContentControl TabBodyControl => tabBody;
        internal void SimulateTabClick(int index) => SelectTab(index);

        internal Settings.GeneralTab GeneralTabControl => generalTab;
        internal Settings.AdvancedTab AdvancedTabControl => advancedTab;
        internal Settings.AnnotationsTab AnnotationsTabControl => annotationsTab;
        internal Settings.AppearanceTab AppearanceTabControl => appearanceTab;
        internal Settings.EnabledObjectsTab EnabledObjectsTabControl => enabledObjectsTab;
        internal Settings.PresetsTab PresetsTabControl => presetsTab;

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyModifiers == KeyModifiers.Meta && e.Key is >= Key.D1 and <= Key.D6) {
                e.Handled = true;
                if (!SheetHost.For(this).IsOpen)
                    SelectTab(e.Key - Key.D1);
                return;
            }
            if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))) {
                e.Handled = true;
                Close();
                return;
            }
            base.OnKeyDown(e);
        }

        private bool closeConfirmed;

        protected override void OnClosing(WindowClosingEventArgs e) {
            RecordPaneSize(appliedScale);
            if (!closeConfirmed && Session is not null) {
                e.Cancel = true;
                Async.Fire(FlushThenCloseAsync(), nameof(FlushThenCloseAsync));
                return;
            }
            base.OnClosing(e);
        }

        private async Task FlushThenCloseAsync() {
            await Session!.FlushAsync().ConfigureAwait(true);
            closeConfirmed = true;
            Close();
        }

        private TextBlock currentPresetLabel = null!;
        private ListBox modSelectionBox = null!;
        private ListBox presetListBox = null!;

        private void WirePresetsTab() {
            currentPresetLabel = presetsTab.CurrentPresetLabelControl;
            modSelectionBox = presetsTab.ModSelectionBoxControl;
            presetListBox = presetsTab.PresetListBoxControl;

            currentPresetLabel.PointerPressed += (_, _) => presetListBox.SelectedItem = null;

            //Ports UpdatePresetLabel's bold toggle (reference SettingsForm.cs:263-265): bold means "this is
            //still the active preset" - it drops the moment the list picks out a different candidate.
            presetListBox.SelectionChanged += (_, _) => {
                RefreshModList();
                currentPresetLabel.FontWeight = presetListBox.SelectedItem is null ? FontWeight.Bold : FontWeight.Normal;
            };
            presetListBox.DoubleTapped += (_, _) => {
                if (presetListBox.SelectedItem is Preset preset)
                    SelectPreset(preset);
            };
            presetListBox.AddHandler(Control.ContextRequestedEvent, OnPresetListBoxContextRequested);

            presetsTab.SelectPresetMenuItemControl.Click += (_, _) => {
                if (presetListBox.SelectedItem is Preset preset)
                    SelectPreset(preset);
            };
            presetsTab.DeletePresetMenuItemControl.Click += (_, _) => {
                if (presetListBox.SelectedItem is Preset preset)
                    Async.Fire(DeletePresetAsync(preset), nameof(DeletePresetAsync));
            };

            presetsTab.ImportPresetButtonControl.Click += (_, _) => Async.Fire(ShowImportPresetAsync(), nameof(ShowImportPresetAsync));
            presetsTab.ComparePresetsButtonControl.Click += (_, _) => Async.Fire(ShowComparePresetsAsync(), nameof(ShowComparePresetsAsync));
            presetsTab.RetryPresetReadButtonControl.Click += (_, _) => RefreshModList();
        }

        private void RefreshPresetList() {
            presetListBox.ItemsSource = (Options.Presets ?? []).Where(p => !p.IsCurrentlySelected).ToList();
            bool canCompare = PresetComparatorWindow.CanCompare(Options.Presets);
            Button compare = presetsTab.ComparePresetsButtonControl;
            compare.IsEnabled = canCompare;
            ToolTip.SetTip(compare, canCompare ? null : "Import another preset to compare it with this one.");
        }

        private void SelectPreset(Preset preset) {
            Options.SelectedPreset = preset;
            Session?.Apply("PresetSwitch");
            RequestReload();
        }

        internal Func<Preset, Task<PresetInfo>>? PresetInfoReaderStub { get; set; }

        internal Task ModListRead { get; private set; } = Task.CompletedTask;
        private int modListReadGeneration;

        private const string PresetReadErrorMod = "ERROR READING PRESET!";

        private void RefreshModList() => ModListRead = ReadModListAsync(++modListReadGeneration);

        private async Task ReadModListAsync(int generation) {
            Preset? selected = presetListBox.SelectedItem as Preset ?? Options.SelectedPreset;
            modSelectionBox.ItemsSource = null;
            if (selected is null) {
                presetsTab.ShowModList();
                return;
            }

            presetsTab.ShowModListReading();
            PresetInfo info = await (PresetInfoReaderStub?.Invoke(selected) ?? Task.Run(() => PresetProcessor.ReadPresetInfo(selected))).ConfigureAwait(true);
            if (generation != modListReadGeneration)
                return;

            if (info.ModList is null || info.ModList.ContainsKey(PresetReadErrorMod)) {
                presetsTab.ShowModListFailure(selected.Name);
                return;
            }

            var modList = info.ModList.Select(kvp => kvp.Key + "_" + kvp.Value).ToList();
            modList.Sort(StringComparer.Ordinal);
            modSelectionBox.ItemsSource = modList;
            presetsTab.ShowModList();
        }

        private void OnPresetListBoxContextRequested(object? sender, ContextRequestedEventArgs e) {
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is not { DataContext: Preset preset }) {
                e.Handled = true;
                return;
            }

            presetListBox.SelectedItem = preset;
            UpdatePresetMenuCaptionsFor(preset);
        }

        internal void UpdatePresetMenuCaptionsFor(Preset preset) {
            MenuItem selectPresetMenuItem = presetsTab.SelectPresetMenuItemControl;
            MenuItem deletePresetMenuItem = presetsTab.DeletePresetMenuItemControl;
            selectPresetMenuItem.Header = "Use Preset";
            selectPresetMenuItem.IsEnabled = !preset.IsCurrentlySelected;
            deletePresetMenuItem.Header = "Delete Preset";
            deletePresetMenuItem.IsEnabled = !preset.IsDefaultPreset && !preset.IsCurrentlySelected;
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

            DeletePresetDbRows(preset.Name);

            Options.Presets?.Remove(preset);
            RefreshPresetList();
        }

        private static void DeletePresetDbRows(string presetName) {
            using ForemanDbContext db = ForemanDbContext.Open(StoragePaths.DbPath(PresetProcessor.ResolveUserDataDirectory()));

            if (db.Presets.Find(presetName) is PresetDataRow dataRow)
                db.Presets.Remove(dataRow);

            List<PresetIconRow> iconRows = [.. db.PresetIcons.Where(row => row.PresetName == presetName)];
            db.PresetIcons.RemoveRange(iconRows);
            db.SaveChanges();

            IconStore.RemoveOrphanedBlobs(db);
        }

        private Task<bool> ConfirmDeleteAsync(Preset preset) =>
            DeleteConfirmationStub?.Invoke(preset) ?? Dialogs.ShowDestructiveConfirmAsync(this, $"Delete the preset “{Dialogs.MiddleTruncate(preset.Name)}”?",
                "Its items, recipes, and icons are removed from Foreman. You can’t undo this action.", "Delete");

        //Test-only seam: lets a test capture a warning dialog's title/message without a real modal window.
        internal Func<string, string, Task>? WarningDialogStub { get; set; }

        private Task ShowWarningAsync(string title, string message) =>
            WarningDialogStub?.Invoke(title, message) ?? Dialogs.ShowWarningAsync(this, title, message);

        internal Func<PresetImportSheet, Task>? ImportPresetDialogStub { get; set; }

        private async Task ShowImportPresetAsync() {
            var sheet = new PresetImportSheet(Options.Presets ?? []);

            await (ImportPresetDialogStub?.Invoke(sheet) ?? SheetHost.For(this).ShowAsync(sheet)).ConfigureAwait(true);

            if (sheet.ImportStarted)
                GC.Collect(); //a preset export opens a lot of zip files and processes a lot of bitmaps - large mod packs can leave 2GB+ of memory stuck in garbage otherwise.

            if (sheet.DialogResultValue != true || string.IsNullOrEmpty(sheet.NewPresetName))
                return;

            Preset? newPreset = Options.Presets?.FirstOrDefault(p => string.Equals(p.Name, sheet.NewPresetName, StringComparison.OrdinalIgnoreCase));
            if (newPreset is null) {
                newPreset = new Preset(sheet.NewPresetName, isCurrentlySelected: false, isDefaultPreset: false);
                Options.Presets?.Add(newPreset);
                RefreshPresetList();
            }

            if (ReferenceEquals(newPreset, Options.Presets is { Count: > 0 } presets ? presets[0] : null)) {
                Options.SelectedPreset = newPreset;
                RequestReload();
            } else if (await ShowSwitchToImportedPresetConfirmAsync(newPreset).ConfigureAwait(true)) {
                SelectPreset(newPreset);
            }
        }

        //Test-only seam: lets a test supply the "switch to the new preset?" answer without a real modal
        //dialog.
        internal Func<Task<bool>>? ImportSwitchConfirmationStub { get; set; }

        private Task<bool> ShowSwitchToImportedPresetConfirmAsync(Preset preset) =>
            ImportSwitchConfirmationStub?.Invoke() ?? Dialogs.ShowConfirmAsync(this, $"Switch to the new preset “{Dialogs.MiddleTruncate(preset.Name)}”?",
                "The import is saved either way; switching reloads the open graph with this preset’s items and recipes.", "Switch Preset");

        internal Func<List<Preset>, Task>? ComparePresetsDialogStub { get; set; }

        private PresetComparatorWindow? comparatorWindow;

        private Task ShowComparePresetsAsync() {
            if (!presetsTab.ComparePresetsButtonControl.IsEnabled)
                return Task.CompletedTask;

            return ComparePresetsDialogStub?.Invoke(Options.Presets ?? []) ?? RealShowComparePresetsAsync();
        }

        private Task RealShowComparePresetsAsync() {
            if (comparatorWindow is { } existing) {
                existing.Show(this);
                existing.Activate();
                return Task.CompletedTask;
            }

            var window = new PresetComparatorWindow(Options.Presets ?? []) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };
            window.Closed += (_, _) => {
                if (ReferenceEquals(comparatorWindow, window))
                    comparatorWindow = null;
            };
            comparatorWindow = window;
            window.Show(this);
            return Task.CompletedTask;
        }

        private void WireEnabledObjectsTab() {
            enabledObjectsTab.LoadEnabledFromSaveButtonControl.Click += (_, _) => Async.Fire(ShowLoadFromSaveAsync(), nameof(ShowLoadFromSaveAsync));
            enabledObjectsTab.SetEnabledFromSciencePacksButtonControl.Click += (_, _) => Async.Fire(ShowAssignFromSciencePacksAsync(), nameof(ShowAssignFromSciencePacksAsync));
            enabledObjectsTab.EnableAllButtonControl.Click += (_, _) => EnableAll();
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

        private void LoadUnfilteredLists() {
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(PlantTowerRules.IsAssemblerListEntry), unfilteredAssemblerList);
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(a => a.EntityType is EntityType.Miner or EntityType.OffshorePump), unfilteredMinerList);
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(a => a.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor), unfilteredPowerList);
            LoadUnfilteredList(Options.DCache.Beacons.Values, unfilteredBeaconList);
            LoadUnfilteredList(Options.DCache.Modules.Values, unfilteredModuleList);
            LoadUnfilteredList(Options.DCache.Recipes.Values, unfilteredRecipeList);
            LoadUnfilteredList(Options.DCache.Qualities.Values, unfilteredQualityList, sortByQualityOrder: true);

            foreach (EnabledObjectsListItem item in unfilteredRecipeList)
                if (item.DataObject is IRecipe recipe)
                    item.TooltipContent = new Image { Source = BakeRecipeTooltip(recipe) };

            ViewModel.LoadUnfilteredLists(unfilteredAssemblerList, unfilteredMinerList, unfilteredPowerList,
                unfilteredBeaconList, unfilteredModuleList, unfilteredRecipeList, unfilteredQualityList);
        }

        private void LoadUnfilteredList<T>(IEnumerable<T> origin, List<EnabledObjectsListItem> destination, bool sortByQualityOrder = false) where T : IDataObjectBase {
            IEnumerable<T> ordered = sortByQualityOrder
                ? origin.OrderByDescending(o => o.Available).ThenBy(o => (IDataObjectBase)o)
                : origin.OrderByDescending(o => o.Available).ThenBy(o => o.FriendlyName);

            foreach (T dataObject in ordered) {
                var item = new EnabledObjectsListItem(dataObject, GetOrBakeIcon(dataObject.Icon)) {
                    IsChecked = dataObject.Enabled
                };
                item.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(EnabledObjectsListItem.IsChecked))
                        SetEnabledMembership(item, enabledObjectsTab.ObjectListControl);
                };
                destination.Add(item);
            }
        }

        //Ports the live-toggle half of ListView_MouseClick/MouseDoubleClick (reference SettingsForm.cs:349-402):
        //writes straight onto the live DataCache object, then reacts through SettingsSession. Also
        //ports both methods' selected-rows bulk toggle: a row that's part of the active multi-selection
        //carries its own new checked state onto every other selected row.
        private void SetEnabledMembership(EnabledObjectsListItem item, ListBox list) {
            if (item.IsChecked)
                Options.EnabledObjects.Add(item.DataObject);
            else
                Options.EnabledObjects.Remove(item.DataObject);
            item.DataObject.Enabled = item.IsChecked;

            if (item.DataObject is IAssembler { Name: "rocket-silo" })
                Options.DCache.RocketAssembler?.Enabled = item.IsChecked;

            if (list.SelectedItems is { } selected && selected.Contains(item))
                foreach (object? selectedObj in selected)
                    if (selectedObj is EnabledObjectsListItem selectedItem && selectedItem != item)
                        selectedItem.IsChecked = item.IsChecked;

            Session?.Apply("EnabledObjects");
        }

        private const int EnabledObjectsIconSize = 24;

        private Bitmap GetOrBakeIcon(SKBitmap icon) => IconBaker.GetOrBake(bakedIconCache, icon, EnabledObjectsIconSize);

        private static Bitmap BakeRecipeTooltip(IRecipe recipe) {
            IRecipe[] recipes = [recipe];
            System.Drawing.Size size = RecipePainter.GetSize(recipes, abbreviateSciPacks: false);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(Math.Max(1, size.Width), Math.Max(1, size.Height), SKColorType.Bgra8888, SKAlphaType.Premul));
            RecipePainter.Paint(recipes, surface.Canvas, new DrawingPoint(0, 0), abbreviateSciPacks: false);
            return IconBaker.SnapshotToBitmap(surface);
        }

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

            ApplyEnabledObjectsLiveFromOptions();
        }

        private void ApplyEnabledObjectsLiveFromOptions() {
            foreach (IRecipe recipe in Options.DCache.Recipes.Values)
                recipe.Enabled = Options.EnabledObjects.Contains(recipe);
            foreach (IAssembler assembler in Options.DCache.Assemblers.Values)
                assembler.Enabled = Options.EnabledObjects.Contains(assembler);
            foreach (IBeacon beacon in Options.DCache.Beacons.Values)
                beacon.Enabled = Options.EnabledObjects.Contains(beacon);
            foreach (IModule module in Options.DCache.Modules.Values)
                module.Enabled = Options.EnabledObjects.Contains(module);
            foreach (IQuality quality in Options.DCache.Qualities.Values)
                quality.Enabled = Options.EnabledObjects.Contains(quality);
            Options.DCache.DefaultQuality?.Enabled = true;
            //Unconditional: a silo-less preset must still force this back to false.
            Options.DCache.RocketAssembler?.Enabled = Options.DCache.Assemblers.TryGetValue("rocket-silo", out IAssembler? silo) && silo?.Enabled == true;

            UpdateEnabledStatus();
            Session?.Apply("EnabledObjects");
        }

        private void UpdateEnabledStatus() {
            foreach (List<EnabledObjectsListItem> list in AllUnfilteredLists)
                foreach (EnabledObjectsListItem item in list)
                    item.IsChecked = Options.EnabledObjects.Contains(item.DataObject);

            ViewModel.RefreshFilteredLists();
        }

        internal Func<SaveFileLoadSheet, Task>? LoadFromSaveDialogStub { get; set; }

        private async Task ShowLoadFromSaveAsync() {
            var settingsService = SettingsService ?? new SettingsService();
            AppSettings settings = settingsService.Load();
            var sheet = new SaveFileLoadSheet(Options.DCache, Options.EnabledObjects, settings.LastSaveFileLocation);

            await (LoadFromSaveDialogStub?.Invoke(sheet) ?? SheetHost.For(this).ShowAsync(sheet)).ConfigureAwait(true);

            if (sheet.Outcome == SaveFileLoadOutcome.Ok) {
                if (sheet.ResolvedSaveFileLocation is string resolvedLocation) {
                    settings.LastSaveFileLocation = resolvedLocation;
                    settingsService.Save(settings);
                    Options.LastSaveFileLocation = resolvedLocation;
                    if (Settings is not null)
                        Settings.LastSaveFileLocation = resolvedLocation;
                }
                ApplyEnabledObjectsLiveFromOptions();
            } else if (sheet.Outcome == SaveFileLoadOutcome.Abort) {
                await ShowWarningAsync("Couldn’t read the save file.",
                    "Open the save in Factorio, save it again, and then try loading it here.").ConfigureAwait(true);
            }
        }

        internal Func<SciencePacksSheet, Task>? AssignFromSciencePacksDialogStub { get; set; }

        private async Task ShowAssignFromSciencePacksAsync() {
            var sheet = new SciencePacksSheet(Options.DCache, Options.EnabledObjects);

            await (AssignFromSciencePacksDialogStub?.Invoke(sheet) ?? SheetHost.For(this).ShowAsync(sheet)).ConfigureAwait(true);

            if (sheet.Accepted)
                ApplyEnabledObjectsLiveFromOptions();
        }

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
        internal Task SimulateComparePresetsClickAsync() => ShowComparePresetsAsync();
        internal void SimulateModListReload() => RefreshModList();
        internal Task SimulateImportPresetClickAsync() => ShowImportPresetAsync();

        internal void SimulateEnableAllClick() => EnableAll();
        internal Task SimulateLoadFromSaveClickAsync() => ShowLoadFromSaveAsync();
        internal Task SimulateAssignFromSciencePacksClickAsync() => ShowAssignFromSciencePacksAsync();
    }
}
