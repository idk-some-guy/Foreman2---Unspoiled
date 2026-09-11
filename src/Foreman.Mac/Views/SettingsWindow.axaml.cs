using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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

namespace Foreman.Mac.Views {
    //C2c Task 4: non-modal, toolbar-tabbed Settings window - changes apply as they're made instead of on a
    //Confirm/Cancel pair (reference task-4-brief.md, phase9-ui-proposal-RATIFIED.html §06.3). Presets tab
    //still buffers a pending preset switch (picking one swaps the whole live DataCache, so it closes this
    //window and MainWindow reopens it once the reload lands - see RequestReload); every other tab writes
    //straight through SettingsViewModel onto the live AppSettings/graph via MainWindow's SettingChanged
    //handler and SettingsSession.
    public partial class SettingsWindow : Window, IThemeAware {
        public sealed class SettingsWindowOptions(DataCache cache) {
            public DataCache DCache { get; } = cache;
            public List<Preset>? Presets { get; set; } = [];
            public Preset? SelectedPreset { get; set; }
            public HashSet<IDataObjectBase> EnabledObjects { get; set; } = [];

            //Set by ShowLoadFromSaveAsync on a successful load, alongside its own eager settings.json write.
            public string? LastSaveFileLocation { get; set; }
        }

        private readonly Settings.PresetsTab presetsTab = new();
        private readonly Settings.EnabledObjectsTab enabledObjectsTab = new();
        private readonly Settings.GeneralTab generalTab = new();
        private readonly Settings.AdvancedTab advancedTab = new();
        private readonly Settings.AnnotationsTab annotationsTab = new();
        private readonly Settings.AppearanceTab appearanceTab = new();

        private readonly Button[] tabButtons;
        private readonly UserControl[] tabControls;
        private readonly TransitioningContentControl tabBody;
        private readonly TextBlock footerText;
        private int activeTabIndex = -1;

        private readonly List<EnabledObjectsListItem> unfilteredAssemblerList = [];
        private readonly List<EnabledObjectsListItem> unfilteredMinerList = [];
        private readonly List<EnabledObjectsListItem> unfilteredPowerList = [];
        private readonly List<EnabledObjectsListItem> unfilteredBeaconList = [];
        private readonly List<EnabledObjectsListItem> unfilteredModuleList = [];
        private readonly List<EnabledObjectsListItem> unfilteredRecipeList = [];
        private readonly List<EnabledObjectsListItem> unfilteredQualityList = [];

        //Dedups baked icons by source SKBitmap reference, same intent as upstream's EnabledObjectsIconIndex.
        private readonly Dictionary<SKBitmap, Bitmap> bakedIconCache = [];

        public SettingsWindowOptions Options { get; }

        public SettingsViewModel ViewModel { get; }

        //Injectable seam for "Load from save" (mirrors MainWindow.SettingsService): defaults to the real
        //settings.json when unset, so tests can point it at a temp-directory-scoped instance instead.
        public SettingsService? SettingsService { get; set; }

        //Set by MainWindow to the same live AppSettings instance SettingsSession holds - ShowLoadFromSaveAsync
        //needs to write LastSaveFileLocation onto it directly (C1: otherwise the next debounced Session save
        //would overwrite the just-written location with this object's stale in-memory copy).
        public AppSettings? Settings { get; set; }

        //R5 live apply: set by MainWindow right after construction - EnabledObjects membership and a
        //requested preset switch both react through it (theme/scale/resolve/save), same as every
        //SettingsViewModel property does via SettingChanged.
        public SettingsSession? Session { get; set; }

        //R5: picking a preset (or an import that overwrites the active one) swaps the whole live DataCache,
        //which this window's own Enabled Objects lists are built against - MainWindow closes this window,
        //reloads, and reopens a fresh instance in response, rather than trying to keep a stale DataCache's
        //controls alive underneath a live reload.
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

            tabBody = this.FindControl<TransitioningContentControl>("TabBody")!;
            footerText = this.FindControl<TextBlock>("FooterText")!;
            footerText.Text = "Foreman 2 · " + AppVersion.VersionedDisplay;

            tabButtons = [
                this.FindControl<Button>("PresetsTabButton")!,
                this.FindControl<Button>("EnabledObjectsTabButton")!,
                this.FindControl<Button>("GeneralTabButton")!,
                this.FindControl<Button>("AdvancedTabButton")!,
                this.FindControl<Button>("AnnotationsTabButton")!,
                this.FindControl<Button>("AppearanceTabButton")!,
            ];
            tabControls = [presetsTab, enabledObjectsTab, generalTab, advancedTab, annotationsTab, appearanceTab];

            //Bindings must resolve the moment a value changes, not only once a tab is actually clicked into
            //view - DataContext otherwise only reaches a tab by inheritance once TabBody.Content attaches it,
            //which never happens for a tab nobody has selected yet.
            enabledObjectsTab.DataContext = ViewModel;
            generalTab.DataContext = ViewModel;
            advancedTab.DataContext = ViewModel;
            annotationsTab.DataContext = ViewModel;
            appearanceTab.DataContext = ViewModel;

            WirePresetsTab();
            WireEnabledObjectsTab();

            SelectTab(0);

            currentPresetLabel.Text = Options.SelectedPreset?.Name;

            presetListBox.ItemsSource = (Options.Presets ?? []).Where(p => !p.IsCurrentlySelected).ToList();

            UpdateModList();
            EnabledObjectsListItem.RefreshTheme();
            LoadUnfilteredLists();
            //Before WindowScaler.Track below - see MainWindow's own matching comment (Content is still the
            //root Grid at this point).
            SheetHost.For(this);
            WindowScaler.Track(this);

            AppearanceHub.Current?.Subscribe(this);
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Task 5: EnabledObjectsListItem.RefreshTheme() re-resolves the shared brushes and pushes them out
        //to every row already on screen (RowBackground/Foreground are live-computed, not baked at row
        //construction) - nothing here needs to rebuild the lists.
        public void RefreshTheme() => EnabledObjectsListItem.RefreshTheme();

        //--- Tab strip ------------------------------------------------------------------------------------

        private void OnPresetsTabClick(object? sender, RoutedEventArgs e) => SelectTab(0);
        private void OnEnabledObjectsTabClick(object? sender, RoutedEventArgs e) => SelectTab(1);
        private void OnGeneralTabClick(object? sender, RoutedEventArgs e) => SelectTab(2);
        private void OnAdvancedTabClick(object? sender, RoutedEventArgs e) => SelectTab(3);
        private void OnAnnotationsTabClick(object? sender, RoutedEventArgs e) => SelectTab(4);
        private void OnAppearanceTabClick(object? sender, RoutedEventArgs e) => SelectTab(5);

        private void SelectTab(int index) {
            if (activeTabIndex == index)
                return;
            activeTabIndex = index;

            for (int i = 0; i < tabButtons.Length; i++)
                tabButtons[i].Classes.Set("active", i == index);

            tabBody.Content = tabControls[index];
        }

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
        internal IReadOnlyList<Button> TabButtonControls => tabButtons;
        internal int ActiveTabIndex => activeTabIndex;
        internal TextBlock FooterTextControl => footerText;
        internal TransitioningContentControl TabBodyControl => tabBody;
        internal void SimulateTabClick(int index) => SelectTab(index);

        internal Settings.GeneralTab GeneralTabControl => generalTab;
        internal Settings.AdvancedTab AdvancedTabControl => advancedTab;
        internal Settings.AnnotationsTab AnnotationsTabControl => annotationsTab;
        internal Settings.AppearanceTab AppearanceTabControl => appearanceTab;
        internal Settings.EnabledObjectsTab EnabledObjectsTabControl => enabledObjectsTab;
        internal Settings.PresetsTab PresetsTabControl => presetsTab;

        //⌘W/Esc close this window - MainWindow tracks one instance for the app's lifetime and hands out a
        //fresh one next time (see MainWindow.ShowSettings).
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))) {
                e.Handled = true;
                Close();
                return;
            }
            base.OnKeyDown(e);
        }

        private bool closeConfirmed;

        //Cancel-then-reclose (reference MainWindow.OnClosing): Session's pending 300ms coalesced save/
        //resolve must actually run before this window is gone, not get dropped when MainWindow nulls the
        //session out from under it on Closed.
        protected override void OnClosing(WindowClosingEventArgs e) {
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

        //--- Presets tab ------------------------------------------------------------------------------------

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
                UpdateModList();
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
        }

        //Preset switch is the one thing here that isn't a plain live-apply write: it swaps the whole
        //DataCache this window's other tabs are built against, so this window closes and MainWindow reopens
        //a fresh one once the reload lands (RequestReload).
        private void SelectPreset(Preset preset) {
            Options.SelectedPreset = preset;
            Session?.Apply("PresetSwitch");
            RequestReload();
        }

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

        internal void UpdatePresetMenuCaptionsFor(Preset preset) {
            MenuItem selectPresetMenuItem = presetsTab.SelectPresetMenuItemControl;
            MenuItem deletePresetMenuItem = presetsTab.DeletePresetMenuItemControl;
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

            DeletePresetDbRows(preset.Name);

            Options.Presets?.Remove(preset);
            presetListBox.ItemsSource = (Options.Presets ?? []).Where(p => !p.IsCurrentlySelected).ToList();
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
            DeleteConfirmationStub?.Invoke(preset) ?? Dialogs.ShowConfirmAsync(this, "Confirm Delete",
                "Are you sure you wish to delete the \"" + preset.Name + "\" preset? This is irreversible.");

        //Test-only seam: lets a test capture a warning dialog's title/message without a real modal window.
        internal Func<string, string, Task>? WarningDialogStub { get; set; }

        private Task ShowWarningAsync(string title, string message) =>
            WarningDialogStub?.Invoke(title, message) ?? Dialogs.ShowWarningAsync(this, title, message);

        //Test-only seam: lets a test drive PresetImportSheet's own seams directly instead of a real SheetHost
        //presentation.
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
                presetListBox.ItemsSource = (Options.Presets ?? []).Where(p => !p.IsCurrentlySelected).ToList();
            }

            if (ReferenceEquals(newPreset, Options.Presets is { Count: > 0 } presets ? presets[0] : null)) {
                Options.SelectedPreset = newPreset;
                RequestReload();
            } else if (await ShowSwitchToImportedPresetConfirmAsync().ConfigureAwait(true)) {
                SelectPreset(newPreset);
            }
        }

        //Test-only seam: lets a test supply the "switch to the new preset?" answer without a real modal
        //dialog.
        internal Func<Task<bool>>? ImportSwitchConfirmationStub { get; set; }

        private Task<bool> ShowSwitchToImportedPresetConfirmAsync() =>
            ImportSwitchConfirmationStub?.Invoke() ?? Dialogs.ShowConfirmAsync(this, "", "Preset import complete! Do you wish to switch to the new preset?");

        //Test-only seam: lets a test observe the real Compare Presets launch without an actual window.
        internal Func<List<Preset>, Task>? ComparePresetsDialogStub { get; set; }

        private PresetComparatorWindow? comparatorWindow;

        //R9: PresetComparatorWindow is non-modal too, one instance, reused/activated on a second click.
        private Task ShowComparePresetsAsync() {
            if ((Options.Presets?.Count ?? 0) < 2)
                return ShowWarningAsync("", "Can not compare presets!\n...you only have 1 preset :/");

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

        //--- Enabled Objects tab ----------------------------------------------------------------------------

        private void WireEnabledObjectsTab() {
            enabledObjectsTab.LoadEnabledFromSaveButtonControl.Click += (_, _) => Async.Fire(ShowLoadFromSaveAsync(), nameof(ShowLoadFromSaveAsync));
            enabledObjectsTab.SetEnabledFromSciencePacksButtonControl.Click += (_, _) => Async.Fire(ShowAssignFromSciencePacksAsync(), nameof(ShowAssignFromSciencePacksAsync));
            enabledObjectsTab.EnableAllButtonControl.Click += (_, _) => EnableAll();
        }

        private IEnumerable<ListBox> AllListViews => enabledObjectsTab.AllListViews;

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
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(a => a.EntityType == EntityType.Assembler), unfilteredAssemblerList, enabledObjectsTab.AssemblerListViewControl);
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(a => a.EntityType is EntityType.Miner or EntityType.OffshorePump), unfilteredMinerList, enabledObjectsTab.MinerListViewControl);
            LoadUnfilteredList(Options.DCache.Assemblers.Values.Where(a => a.EntityType is EntityType.Boiler or EntityType.BurnerGenerator or EntityType.Generator or EntityType.Reactor), unfilteredPowerList, enabledObjectsTab.PowerListViewControl);
            LoadUnfilteredList(Options.DCache.Beacons.Values, unfilteredBeaconList, enabledObjectsTab.BeaconListViewControl);
            LoadUnfilteredList(Options.DCache.Modules.Values, unfilteredModuleList, enabledObjectsTab.ModuleListViewControl);
            LoadUnfilteredList(Options.DCache.Recipes.Values, unfilteredRecipeList, enabledObjectsTab.RecipeListViewControl);
            LoadUnfilteredList(Options.DCache.Qualities.Values, unfilteredQualityList, enabledObjectsTab.QualityListViewControl, sortByQualityOrder: true);

            foreach (EnabledObjectsListItem item in unfilteredRecipeList)
                if (item.DataObject is IRecipe recipe)
                    item.TooltipContent = new Image { Source = BakeRecipeTooltip(recipe) };

            ViewModel.LoadUnfilteredLists(unfilteredAssemblerList, unfilteredMinerList, unfilteredPowerList,
                unfilteredBeaconList, unfilteredModuleList, unfilteredRecipeList, unfilteredQualityList);
        }

        private void LoadUnfilteredList<T>(IEnumerable<T> origin, List<EnabledObjectsListItem> destination, ListBox list, bool sortByQualityOrder = false) where T : IDataObjectBase {
            IEnumerable<T> ordered = sortByQualityOrder
                ? origin.OrderByDescending(o => o.Available).ThenBy(o => (IDataObjectBase)o)
                : origin.OrderByDescending(o => o.Available).ThenBy(o => o.FriendlyName);

            foreach (T dataObject in ordered) {
                //Reads live Enabled straight off the DataCache - no more buffered commit step, so the
                //checkbox always reflects what the graph would actually see right now.
                var item = new EnabledObjectsListItem(dataObject, GetOrBakeIcon(dataObject.Icon)) {
                    IsChecked = dataObject.Enabled
                };
                item.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(EnabledObjectsListItem.IsChecked))
                        SetEnabledMembership(item, list);
                };
                destination.Add(item);
            }
        }

        //Ports the live-toggle half of ListView_MouseClick/MouseDoubleClick (reference SettingsForm.cs:349-402):
        //writes straight onto the live DataCache object, then reacts through SettingsSession (R5). Also
        //ports both methods' selected-rows bulk toggle: a row that's part of the active multi-selection
        //carries its own new checked state onto every other selected row.
        private void SetEnabledMembership(EnabledObjectsListItem item, ListBox list) {
            if (item.IsChecked)
                Options.EnabledObjects.Add(item.DataObject);
            else
                Options.EnabledObjects.Remove(item.DataObject);
            item.DataObject.Enabled = item.IsChecked;

            //rocket-silo drives the synthetic RocketAssembler pseudo-entity (reference EnableAll below).
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

        //Ports EnableAllButton_Click (reference SettingsForm.cs:559-580), now applied live.
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

        //Writes Options.EnabledObjects onto the live DataCache in bulk - used by EnableAll and the two
        //sheets below, which both replace membership wholesale rather than one row at a time.
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
            //Unconditional, not gated on TryGetValue's own bool: a silo-less preset must still force this
            //back to false rather than leave it at the constructor default (reference upstream fix - the
            //old MainWindow.OpenSettingsAsync tail carried the exact same shape).
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
                await ShowWarningAsync("",
                    "Error while reading save file. Try running factorio, opening the save game, saving again, and retrying?").ConfigureAwait(true);
            }
        }

        internal Func<SciencePacksSheet, Task>? AssignFromSciencePacksDialogStub { get; set; }

        private async Task ShowAssignFromSciencePacksAsync() {
            var sheet = new SciencePacksSheet(Options.DCache, Options.EnabledObjects);

            await (AssignFromSciencePacksDialogStub?.Invoke(sheet) ?? SheetHost.For(this).ShowAsync(sheet)).ConfigureAwait(true);

            if (sheet.Accepted)
                ApplyEnabledObjectsLiveFromOptions();
        }

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these).
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
        internal Task SimulateImportPresetClickAsync() => ShowImportPresetAsync();

        internal void SimulateEnableAllClick() => EnableAll();
        internal Task SimulateLoadFromSaveClickAsync() => ShowLoadFromSaveAsync();
        internal Task SimulateAssignFromSciencePacksClickAsync() => ShowAssignFromSciencePacksAsync();
    }
}
