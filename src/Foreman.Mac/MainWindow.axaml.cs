using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac {
    public partial class MainWindow : Window {
        public ShellCommands Commands { get; }
        public ContentControl GraphViewerHost { get; }
        public GraphCanvasControl GraphCanvas { get; }
        public Foreman.DataCaching.DataCache? DataCache { get; set; }
        public AppSettings? Settings { get; set; }
        public PresetResolver? PresetResolver { get; set; }
        public SettingsService? SettingsService { get; set; }

        //Test-only seam: lets a test supply the Settings dialog's outcome without a real modal ShowDialog
        //(mirrors GraphCanvasControl's TextPropertiesDialogStub/ShapePropertiesDialogStub).
        internal Func<SettingsWindow.SettingsWindowOptions, Task<bool?>>? SettingsDialogStub { get; set; }

        //Test-only seam: lets a test observe/short-circuit the Graph Summary dialog without a real modal
        //ShowDialog.
        internal Action<IProductionGraphSession>? GraphSummaryDialogStub { get; set; }

        //Test-only seam: lets a test observe/short-circuit the Image Export dialog without a real modal
        //ShowDialog.
        internal Action<GraphViewer>? ImageExportDialogStub { get; set; }

        //Test-only seam: lets a test supply Save As's file-save-picker result without a real modal picker
        //(mirrors GraphSummaryWindow's own SaveFilePathStub).
        internal Func<Task<string?>>? SaveFilePathStub { get; set; }

        //Test-only seams for TestGraphSavedStatus's two prompts (OK/Cancel discard, Yes/No/Cancel save-and-
        //continue) - same stub-or-real pattern as SettingsDialogStub above.
        internal Func<Task<bool>>? DiscardUnsavedGraphConfirmStub { get; set; }
        internal Func<Task<ConfirmChoice>>? SaveBeforeContinuingChoiceStub { get; set; }

        //Test-only seam: lets a test supply the cross-preset PresetSelectionWindow's outcome (io-reference.md
        //§8 slow path) without a real modal ShowDialog.
        internal Func<List<PresetErrorPackage>, Task<Preset?>>? PresetSelectionDialogStub { get; set; }

        //Test-only seam: lets a test observe/short-circuit the silent-switch info message (io-reference.md
        //§8) without a real modal ShowDialog.
        internal Func<string, Task>? PresetSwitchInfoStub { get; set; }

        //Ports MainForm's savefilePath/savefileBaselineJson fields (reference: upstream MainForm.cs, io-
        //reference.md §2). savefileBaselineJson is the dirty-check baseline: a captured JSON string, not a
        //re-read from disk, refreshed by SaveGraph/ApplyLoadedGraphUiState and cleared by New.
        private string? savefilePath;
        private string? savefileBaselineJson;
        private readonly string defaultAppName;

        internal string? SaveFilePath => savefilePath;
        internal string? SaveFileBaselineJson => savefileBaselineJson;

        public MainWindow() {
            InitializeComponent();
            WindowState = WindowState.Maximized;
            defaultAppName = Title ?? "Foreman 2";
            GraphViewerHost = this.FindControl<ContentControl>("GraphViewerHost")!;
            GraphCanvas = new GraphCanvasControl();
            GraphViewerHost.Content = GraphCanvas;
            Commands = new ShellCommands(
                onNew: () => Async.Fire(NewGraphAsync(), nameof(NewGraphAsync)),
                onLoad: () => Async.Fire(LoadGraphAsync(), nameof(LoadGraphAsync)),
                onSave: () => Async.Fire(SaveGraphOrPromptAsync(), nameof(SaveGraphOrPromptAsync)),
                onSaveAs: () => Async.Fire(SaveGraphAsAsync(), nameof(SaveGraphAsAsync)),
                onImport: () => Async.Fire(ImportGraphAsync(), nameof(ImportGraphAsync)),
                onExportImage: () => Async.Fire(OpenImageExportAsync(), nameof(OpenImageExportAsync)),
                onAddItem: () => GraphCanvas.AddItemAsync(GraphCanvas.Viewport.ScreenToGraph(new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2))),
                onAddRecipe: () => GraphCanvas.AddRecipeAsync(GraphCanvas.Viewport.ScreenToGraph(new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2))),
                onAutoconnect: () => { GraphCanvas.FloatingPanelHost.Close(); GraphCanvas.Viewer.AutoconnectDisconnectedInputs(); GraphCanvas.RequestRedraw(); },
                onAlignSelection: () => { GraphCanvas.FloatingPanelHost.Close(); GraphCanvas.Viewer.AlignSelected(); GraphCanvas.RequestRedraw(); },
                onSettings: () => Async.Fire(OpenSettingsAsync(), nameof(OpenSettingsAsync)),
                onGraphSummary: () => Async.Fire(OpenGraphSummaryAsync(), nameof(OpenGraphSummaryAsync)));

            BindToolbarButtons();
            GraphCanvas.KeyDown += OnGraphCanvasKeyDown;
            this.FindControl<TextBlock>("VersionLabel")!.Text = AppVersion.VersionedDisplay;
            NativeMenu.SetMenu(this, BuildNativeMenu());
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Ports MainForm_Load's settings-application block (reference: upstream MainForm.cs:70-135) - preset/
        //fuel-priority defaults stay untouched since neither has a settings-dialog widget yet; the knobs
        //below have no toolbar control of their own either (that's the Settings dialog's job), so they still
        //get seeded onto the canvas here so a persisted value takes effect at launch instead of silently
        //reverting to a code-level default every time. Called once boot has both DataCache and Settings
        //assigned.
        public void ApplyLoadedSettings() {
            if (Settings is not AppSettings settings)
                return;

            //Ports the AnnotText*/AnnotShape* static-field cold-start read (reference §6): TextAnnotationElement
            //and ShapeAnnotationElement can't read AppSettings at static-init time (it loads async), so this is
            //the equivalent one-time seed once settings are actually ready. AnnotationSettings also lets the
            //properties windows write new defaults straight back into the same object MainWindow flushes on
            //close.
            TextAnnotationElement.LoadDefaultsFrom(settings);
            ShapeAnnotationElement.LoadDefaultsFrom(settings);
            GraphCanvas.AnnotationSettings = settings;

            //DCache/DefaultAssemblerQuality previously stayed unset until the first Load (LoadDocument was the
            //only assigner) - a fresh/New graph had neither, so recipe-node creation (CreateRecipeNode throws
            //without DefaultAssemblerQuality) and this task's Add Item/Add Recipe choosers (need DCache) were
            //unreachable before ever opening a file. Both belong here alongside the rest of this boot-time seed.
            if (DataCache is Foreman.DataCaching.DataCache cache) {
                GraphCanvas.Viewer.Context.DCache = cache;
                GraphCanvas.Viewer.Graph.DefaultAssemblerQuality = cache.DefaultQuality;
            }

            GraphCanvas.Viewer.Graph.SelectedRateUnit = settings.DefaultRateUnit;
            GraphCanvas.Viewer.Graph.EnableExtraProductivityForNonMiners = settings.EnableExtraProductivityForNonMiners;
            GraphCanvas.Viewer.Context.EnableExtraProductivityForNonMiners = settings.EnableExtraProductivityForNonMiners;
            GraphCanvas.Viewer.IconsOnly = settings.IconsOnlyView;

            //Ports MainForm.SetDarkMode/SetLightMode's cold-start read (upstream MainForm.cs:36-46): App.
            //ApplyTheme only swaps the window chrome's ThemeVariant, so the canvas needs its own seed here -
            //same seam boot, preset reload, and every Settings Confirm already replay through.
            bool isDark = settings.FlagDarkMode == ThemeMode.Dark ||
                (settings.FlagDarkMode == ThemeMode.System && Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
            GraphCanvas.Viewer.ApplyTheme(isDark);
            GraphCanvas.RequestRedraw();

            //Ports the SmartNodeDirection/DefaultNodeDirection cold-start read (reference §8): wired here
            //rather than at construction since GraphCanvasControl has no settings reference of its own.
            GraphCanvas.SmartNodeDirection = settings.SmartNodeDirection;
            GraphCanvas.Viewer.Graph.DefaultNodeDirection = settings.DefaultNodeDirection;
            GraphCanvas.Viewer.Graph.AssemblerSelector.DefaultSelectionStyle = settings.DefaultAssemblerOption;
            GraphCanvas.Viewer.Graph.ModuleSelector.DefaultSelectionStyle = settings.DefaultModuleOption;
            GraphCanvas.Viewer.Graph.DefaultToSimplePassthroughNodes = settings.SimplePassthroughNodes;

            //Ports the Advanced (Solver options) group box's live half (reference §5, upstream MainForm.cs
            //446/494-496): these 4 fields have no Properties.Settings.Default counterpart upstream either -
            //GraphViewer.Graph is where the LP solver (GraphOptimisation.cs) reads them from directly.
            settings.QualitySteps = Math.Clamp(settings.QualitySteps, 1, 20);
            GraphCanvas.Viewer.Graph.MaxQualitySteps = (uint)settings.QualitySteps;
            GraphCanvas.Viewer.Graph.LowPriorityPower = (double)settings.LowPriorityPower;
            GraphCanvas.Viewer.Graph.PullOutputNodes = settings.PullConsumerNodes;
            GraphCanvas.Viewer.Graph.PullOutputNodesPower = (double)settings.PullConsumerNodesPower;

            NodeElementContext context = GraphCanvas.Viewer.Context;
            context.LevelOfDetail = Enum.IsDefined(typeof(LevelOfDetail), settings.LevelOfDetail)
                ? (LevelOfDetail)settings.LevelOfDetail
                : LevelOfDetail.Medium;
            settings.LevelOfDetail = (int)context.LevelOfDetail;
            context.ArrowsOnLinks = settings.ArrowsOnLinks;
            context.DynamicLinkWidth = settings.DynamicLineWidth;
            context.FlagOUSuppliedNodes = settings.FlagOUSuppliedNodes;
            context.RoundAssemblerCount = settings.RoundAssemblerCount;
            context.AbbreviateSciPacks = settings.AbbreviateSciPacks;
            context.ShowRecipeToolTip = settings.ShowRecipeToolTip;

            settings.IconsSize = Math.Clamp(settings.IconsSize, 8, 256);
            context.IconsDrawSize = settings.IconsSize;

            GraphCanvas.Viewer.NodeCountForSimpleView = settings.NodeCountForSimpleView;

            PointingArrowRenderer arrowRenderer = GraphCanvas.Viewer.ArrowRenderer;
            arrowRenderer.ShowErrorArrows = settings.ShowErrorArrows;
            arrowRenderer.ShowWarningArrows = settings.ShowWarningArrows;
            arrowRenderer.ShowDisconnectedArrows = settings.ShowDisconnectedArrows;
            arrowRenderer.ShowOUNodeArrows = settings.ShowOUSuppliedArrows;

            BindViewOptionControls(settings);
        }

        //Called from boot, ReloadForPresetAsync, and every OpenSettingsAsync Confirm (twice on a preset-
        //switching Confirm - once from ReloadForPresetAsync's own ApplyLoadedSettings call, once more from
        //OpenSettingsAsync's own call after it). The value/enable resync below is idempotent and safe to
        //replay on every call; the six event subscriptions are not, so they're guarded to run exactly once
        //(same fix shape as ResyncRateOptionsDropDown's unsubscribe-before-subscribe, applied at the source
        //instead - a construction-time subscribe would need these controls looked up a second time). The
        //handlers themselves read the live Settings property rather than closing over this call's settings
        //parameter, since Settings can be swapped to a different AppSettings instance between calls.
        private bool viewOptionControlsBound;

        private void BindViewOptionControls(AppSettings settings) {
            var minorGridlines = this.FindControl<ComboBox>("MinorGridlinesDropDown")!;
            var majorGridlines = this.FindControl<ComboBox>("MajorGridlinesDropDown")!;
            var gridlinesCheckbox = this.FindControl<CheckBox>("GridlinesCheckbox")!;
            var rateOptions = this.FindControl<ComboBox>("RateOptionsDropDown")!;
            var iconView = this.FindControl<CheckBox>("IconViewCheckBox")!;
            var pauseUpdates = this.FindControl<CheckBox>("PauseUpdatesCheckbox")!;

            minorGridlines.SelectedIndex = settings.MinorGridlines;
            majorGridlines.SelectedIndex = settings.MajorGridlines;
            gridlinesCheckbox.IsChecked = settings.AltGridlines;
            //Minor#4 (final fix wave): routes through the same unsubscribe-before-write idiom
            //ResyncRateOptionsDropDown uses below, rather than writing SelectedIndex directly - a repeat
            //call (preset-switching Confirm) with the handler already subscribed would otherwise fire
            //OnRateOptionsSelectionChanged and re-solve on top of this call's own apply-tail solve.
            ResyncRateOptionsDropDown();
            iconView.IsChecked = GraphCanvas.Viewer.IconsOnly;
            pauseUpdates.IsChecked = GraphCanvas.Viewer.Graph.PauseUpdates;

            GraphCanvas.Grid.CurrentGridUnit = GridUnitFromIndex(minorGridlines.SelectedIndex);
            GraphCanvas.Grid.CurrentMajorGridUnit = GridUnitFromIndex(majorGridlines.SelectedIndex);
            GraphCanvas.Grid.ShowGrid = gridlinesCheckbox.IsChecked == true;

            minorGridlines.IsEnabled = true;
            majorGridlines.IsEnabled = true;
            gridlinesCheckbox.IsEnabled = true;
            rateOptions.IsEnabled = true;
            iconView.IsEnabled = true;
            pauseUpdates.IsEnabled = true;

            if (viewOptionControlsBound)
                return;
            viewOptionControlsBound = true;

            minorGridlines.SelectionChanged += (_, _) => {
                GraphCanvas.Grid.CurrentGridUnit = GridUnitFromIndex(minorGridlines.SelectedIndex);
                if (Settings is AppSettings current)
                    current.MinorGridlines = minorGridlines.SelectedIndex;
                GraphCanvas.InvalidateVisual();
            };
            majorGridlines.SelectionChanged += (_, _) => {
                GraphCanvas.Grid.CurrentMajorGridUnit = GridUnitFromIndex(majorGridlines.SelectedIndex);
                if (Settings is AppSettings current)
                    current.MajorGridlines = majorGridlines.SelectedIndex;
                GraphCanvas.InvalidateVisual();
            };
            gridlinesCheckbox.IsCheckedChanged += (_, _) => {
                GraphCanvas.Grid.ShowGrid = gridlinesCheckbox.IsChecked == true;
                if (Settings is AppSettings current)
                    current.AltGridlines = gridlinesCheckbox.IsChecked == true;
                GraphCanvas.InvalidateVisual();
            };
            //No rateOptions.SelectionChanged += here: ResyncRateOptionsDropDown's own unsubscribe-before-
            //subscribe write above already owns that handler's lifecycle, on every call.
            iconView.IsCheckedChanged += (_, _) => {
                GraphCanvas.Viewer.IconsOnly = iconView.IsChecked == true;
                if (Settings is AppSettings current)
                    current.IconsOnlyView = iconView.IsChecked == true;
                GraphCanvas.InvalidateVisual();
            };
            pauseUpdates.IsCheckedChanged += (_, _) => {
                GraphCanvas.Viewer.Graph.PauseUpdates = pauseUpdates.IsChecked == true;
                if (!GraphCanvas.Viewer.Graph.PauseUpdates)
                    GraphCanvas.Viewer.Graph.UpdateNodeValues();
                GraphCanvas.InvalidateVisual();
            };
        }

        private void OnRateOptionsSelectionChanged(object? sender, SelectionChangedEventArgs e) {
            var rateOptions = (ComboBox)sender!;
            var unit = (ProductionGraph.RateUnit)rateOptions.SelectedIndex;
            GraphCanvas.Viewer.Graph.SelectedRateUnit = unit;
            if (Settings is AppSettings settings)
                settings.DefaultRateUnit = unit;
            GraphCanvas.Viewer.Graph.UpdateNodeValues();
            GraphCanvas.InvalidateVisual();
        }

        //Ports ApplyLoadedGraphUiState's dropdown resync (upstream MainForm.cs:227-231, first thing it does
        //after a load): LoadDocument already applies the save's rate unit to Graph.SelectedRateUnit, but
        //nothing kept RateOptionsDropDown or Settings in step with it. Unsubscribing around the index write
        //avoids re-running OnRateOptionsSelectionChanged's own GraphCanvas.Viewer.Graph.UpdateNodeValues()
        //call, since LoadDocument already solved the freshly loaded graph once.
        internal void ResyncRateOptionsDropDown() {
            var rateOptions = this.FindControl<ComboBox>("RateOptionsDropDown")!;
            rateOptions.SelectionChanged -= OnRateOptionsSelectionChanged;
            rateOptions.SelectedIndex = (int)GraphCanvas.Viewer.Graph.SelectedRateUnit;
            rateOptions.SelectionChanged += OnRateOptionsSelectionChanged;

            if (Settings is AppSettings settings)
                settings.DefaultRateUnit = GraphCanvas.Viewer.Graph.SelectedRateUnit;
        }

        private static int GridUnitFromIndex(int index) => index > 0 ? 6 * (int)Math.Pow(2, index - 1) : 0;

        //Ports LoadGraphButton_Click -> LoadGraph() -> LoadGraph(path) (reference: upstream MainForm.cs:198-
        //225, io-reference.md §2 "Load"): the open-file picker and TestGraphSavedStatus gate; the parse,
        //preset resolution, and post-load bookkeeping are LoadGraphJsonAsync's job.
        private async Task LoadGraphAsync() {
            if (DataCache is null || StorageProvider is not IStorageProvider storage)
                return;
            if (!await TestGraphSavedStatusAsync().ConfigureAwait(true))
                return;

            Directory.CreateDirectory(AppPaths.SavedGraphsDirectory);
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Open Graph",
                AllowMultiple = false,
                SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(AppPaths.SavedGraphsDirectory)).ConfigureAwait(true),
                FileTypeFilter = [new FilePickerFileType("Foreman files") { Patterns = ["*.fjson", "*.json"] }],
            }).ConfigureAwait(true);
            if (files.Count == 0)
                return;

            string path = files[0].Path.LocalPath;
            string json;
            await using (Stream stream = await files[0].OpenReadAsync().ConfigureAwait(true))
            using (var reader = new StreamReader(stream))
                json = await reader.ReadToEndAsync().ConfigureAwait(true);

            await LoadGraphJsonAsync(json, path).ConfigureAwait(true);
        }

        //Ports LoadFromJson/LoadFromSaveDocument's non-dialog half (reference upstream ProductionGraphViewer.
        //cs:1414-1432, io-reference.md §8): parses the save once, resolves which preset to load it against
        //via ResolveChosenPresetAsync (fast/silent-switch/slow-path dialog, or null on cancel), swaps in that
        //preset's DataCache only when it differs from the one already active, then loads. Internal so tests
        //can drive it without a real file picker, the same seam ImportGraphJsonAsync already established.
        internal async Task LoadGraphJsonAsync(string json, string path) {
            if (DataCache is not Foreman.DataCaching.DataCache cache || Settings is not AppSettings settings)
                return;

            GraphViewerSaveDocument? document = GraphSaveCodec.ReadViewer(json);
            if (document is null) {
                await Dialogs.ShowWarningAsync(this, "Cannot load save",
                    "This save file is too old or corrupt. Try opening it in the previous Foreman release and saving it again, then open the new file here.").ConfigureAwait(true);
                return;
            }

            Preset? chosenPreset = await ResolveChosenPresetAsync(document).ConfigureAwait(true);
            if (chosenPreset is null)
                return;

            if (chosenPreset.Name != cache.PresetName) {
                Foreman.DataCaching.DataCache? newCache = await LoadDataCacheForPresetAsync(chosenPreset, settings).ConfigureAwait(true);
                if (newCache is null)
                    return;
                cache = newCache;
            }

            GraphLoadResult result = GraphCanvas.LoadDocument(cache, document);
            if (!result.Success)
                await Dialogs.ShowWarningAsync(this, "Cannot load save", result.ErrorMessage ?? "This save file is too old or corrupt.").ConfigureAwait(true);
            else
                ApplyLoadedGraphUiState(path);

            GraphCanvas.InvalidateVisual();
        }

        //Ports ResolveChosenPresetAsync (reference upstream ProductionGraphViewer.cs:1434-1507, io-reference.
        //md §8): the matching engine (PresetProcessor.TestPreset/PresetErrorPackage) is already ported, this
        //only orchestrates it. Fast path: the save's own SavedPresetName matches an installed preset with
        //zero errors, used with no dialog. Silent-switch: that fast-path match isn't the currently active
        //preset, so CurrentPresetName follows it with an info message instead of a dialog. Slow path: no
        //clean name match (or the name match itself had errors, which still carries its already-computed
        //errors into the list below rather than re-testing it) - every other installed preset gets tested
        //and the ranked picker decides. Returns null on cancel (dialog cancel or no installed presets).
        internal async Task<Preset?> ResolveChosenPresetAsync(GraphViewerSaveDocument saveDocument) {
            if (Settings is not AppSettings settings || PresetResolver is not Services.PresetResolver presetResolver || SettingsService is not Services.SettingsService settingsService)
                return null;

            ProductionGraphSaveDocument productionGraph = saveDocument.ProductionGraph;
            var modSet = new Dictionary<string, string>(saveDocument.IncludedMods);
            var itemNames = productionGraph.IncludedItems.ToList();
            var qualityNames = productionGraph.IncludedQualities.Select(q => q.Key).ToList();
            var recipeShorts = productionGraph.IncludedRecipes.ToList();
            var plantShorts = productionGraph.IncludedPlantProcesses.ToList();

            List<Preset> allPresets = presetResolver.BuildPresetList(settings.CurrentPresetName);
            if (allPresets.Count == 0)
                return null;

            var presetErrors = new List<PresetErrorPackage>();
            Preset? chosenPreset = null;

            Preset? savedPreset = saveDocument.SavedPresetName is string savedPresetName
                ? allPresets.FirstOrDefault(p => p.Name == savedPresetName)
                : null;
            if (savedPreset is not null) {
                PresetErrorPackage errors = await PresetProcessor.TestPreset(savedPreset, modSet, itemNames, qualityNames, recipeShorts, plantShorts).ConfigureAwait(true);
                if (errors.ErrorCount == 0)
                    chosenPreset = savedPreset;
                else {
                    presetErrors.Add(errors);
                    allPresets.Remove(savedPreset);
                }
            }

            if (chosenPreset is null) {
                foreach (Preset preset in allPresets)
                    presetErrors.Add(await PresetProcessor.TestPreset(preset, modSet, itemNames, qualityNames, recipeShorts, plantShorts).ConfigureAwait(true));

                Preset? dialogChoice = await ShowPresetSelectionDialogAsync(presetErrors).ConfigureAwait(true);
                if (dialogChoice is null)
                    return null;

                chosenPreset = dialogChoice;
                settings.CurrentPresetName = chosenPreset.Name;
                settingsService.Save(settings);
            } else if (chosenPreset.Name != settings.CurrentPresetName) {
                string previousPresetName = settings.CurrentPresetName;
                string newPresetName = chosenPreset.Name;
                await ShowPresetSwitchInfoAsync(string.Format(DisplayCulture.Format,
                    "Loaded graph uses a different Preset.\nPreset switched from \"{0}\" to \"{1}\"", previousPresetName, newPresetName)).ConfigureAwait(true);
                settings.CurrentPresetName = newPresetName;
                settingsService.Save(settings);
            }

            return chosenPreset;
        }

        private Task<Preset?> ShowPresetSelectionDialogAsync(List<PresetErrorPackage> presetErrors) =>
            PresetSelectionDialogStub?.Invoke(presetErrors) ?? RealShowPresetSelectionDialogAsync(presetErrors);

        //Ports the ResolveChosenPresetAsync slow path's dialog launch (upstream ProductionGraphViewer.cs:
        //1477-1488): owner+50/+50, same Manual placement as Settings/Graph Summary/Image Export.
        private async Task<Preset?> RealShowPresetSelectionDialogAsync(List<PresetErrorPackage> presetErrors) {
            var window = new PresetSelectionWindow(presetErrors) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };
            bool? result = await window.ShowDialog<bool?>(this).ConfigureAwait(true);
            if (!GraphCanvas.FloatingPanelHost.IsOpen)
                GraphCanvas.Focus();
            return result == true ? window.ChosenPreset : null;
        }

        private Task ShowPresetSwitchInfoAsync(string message) =>
            PresetSwitchInfoStub?.Invoke(message) ?? Dialogs.ShowInfoAsync(this, message);

        //Ports the ResolveChosenPresetAsync/ReloadGraphForCurrentPreset shared DataLoadForm spin-up (upstream
        //ProductionGraphViewer.cs:1365-1386's non-corrupt path): swaps in the chosen preset's DataCache,
        //shared by both the Load-a-file path (LoadGraphJsonAsync) and the Settings preset-switch path
        //(ReloadForPresetAsync). Returns null (leaving the live DataCache untouched) if the load fails.
        private async Task<Foreman.DataCaching.DataCache?> LoadDataCacheForPresetAsync(Preset preset, AppSettings settings) {
            var dataLoadWindow = new DataLoadWindow(preset, settings.UseRecipeBWfilters);
            dataLoadWindow.Show();
            await dataLoadWindow.LoadTask.ConfigureAwait(true);
            if (dataLoadWindow.Result is not Foreman.DataCaching.DataCache newCache)
                return null;

            DataCache = newCache;
            settings.CurrentPresetName = preset.Name;
            return newCache;
        }

        //Ports ApplyLoadedGraphUiState (reference: upstream MainForm.cs:227-241): records the save path,
        //captures the dirty-check baseline, resyncs the 5 Default* settings a load can change (rate unit via
        //ResyncRateOptionsDropDown, the other 4 here), and rewrites the title bar. Internal so tests can call
        //it directly after a LoadDocument call, the same seam ResyncRateOptionsDropDown already uses.
        internal void ApplyLoadedGraphUiState(string path) {
            savefilePath = path;
            CaptureSaveBaseline();
            ResyncRateOptionsDropDown();

            if (Settings is AppSettings settings) {
                settings.EnableExtraProductivityForNonMiners = GraphCanvas.Viewer.Graph.EnableExtraProductivityForNonMiners;
                settings.DefaultAssemblerOption = GraphCanvas.Viewer.Graph.AssemblerSelector.DefaultSelectionStyle;
                settings.DefaultModuleOption = GraphCanvas.Viewer.Graph.ModuleSelector.DefaultSelectionStyle;
                settings.DefaultNodeDirection = GraphCanvas.Viewer.Graph.DefaultNodeDirection;
            }

            UpdateTitleBar();
        }

        //Ports NewGraph's dirty-gate and path-reset (reference: upstream MainForm.cs:243-264); the preset
        //switch upstream's NewGraph also performs has no equivalent here yet (out of this task's scope, see
        //the New command's own pre-existing behavior).
        internal async Task NewGraphAsync() {
            if (!await TestGraphSavedStatusAsync().ConfigureAwait(true))
                return;

            GraphCanvas.FloatingPanelHost.Close();
            GraphCanvas.Viewer.Graph.ClearGraph();
            savefilePath = null;
            savefileBaselineJson = null;
            UpdateTitleBar();
            GraphCanvas.RequestRedraw();
        }

        //Ports ImportGraphButton_Click -> ImportGraph() (reference: upstream MainForm.cs:271-282, io-
        //reference.md §2 "Import Graph"): same open-file picker shape as Load, but no TestGraphSavedStatus
        //gate - only Load/New call that, never Save/SaveAs/Import.
        private async Task ImportGraphAsync() {
            if (DataCache is null || StorageProvider is not IStorageProvider storage)
                return;

            Directory.CreateDirectory(AppPaths.SavedGraphsDirectory);
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Import Graph",
                AllowMultiple = false,
                SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(AppPaths.SavedGraphsDirectory)).ConfigureAwait(true),
                FileTypeFilter = [new FilePickerFileType("Foreman files") { Patterns = ["*.fjson", "*.json"] }],
            }).ConfigureAwait(true);
            if (files.Count == 0)
                return;

            string json;
            await using (Stream stream = await files[0].OpenReadAsync().ConfigureAwait(true))
            using (var reader = new StreamReader(stream))
                json = await reader.ReadToEndAsync().ConfigureAwait(true);

            await ImportGraphJsonAsync(json, files[0].Path.LocalPath).ConfigureAwait(true);
        }

        //Ports ImportGraph(path) (reference: upstream MainForm.cs:284-296): ReadGraphPayload's dual-accept
        //(bare fragment or full viewer save) feeds GraphViewer.ImportNodesFromDocument's centroid/offset/
        //selection merge. Internal so tests can drive it without a real file picker, the same seam
        //SaveGraphAsync(path) already established.
        internal async Task ImportGraphJsonAsync(string json, string path) {
            if (DataCache is not Foreman.DataCaching.DataCache cache)
                return;

            try {
                ProductionGraphSaveDocument document = GraphSaveCodec.ReadGraphPayload(json) ?? throw new InvalidOperationException(
                    "This save file is too old or corrupt. Try opening it in the previous Foreman release and saving it again, then open the new file here.");
                System.Drawing.Point origin = GraphCanvas.Viewport.ScreenToGraph(new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2));
                GraphCanvas.Viewer.ImportNodesFromDocument(cache, document, origin, applySolverSettings: true);
                GraphCanvas.RequestRedraw();
            } catch (Exception exception) {
                await Dialogs.ShowWarningAsync(this, "", "Could not import this file. See log for more details.").ConfigureAwait(true);
                Foreman.DataCaching.ErrorLogging.LogException(exception, $"Error importing from file '{path}'");
            }
        }

        //Ports SaveButton_Click (reference: upstream MainForm.cs:140-143): falls back to Save As whenever
        //there's no path yet or the write failed.
        internal async Task SaveGraphOrPromptAsync() {
            if (savefilePath is null || !await SaveGraphAsync(savefilePath).ConfigureAwait(true))
                await SaveGraphAsAsync().ConfigureAwait(true);
        }

        //Ports SaveGraphAs (reference: upstream MainForm.cs:165-179, io-reference.md §2 "Save / SaveAs"):
        //.fjson filter/extension, Saved Graphs initial directory (created if missing), overwrite prompt, and
        //the "Flowchart.fjson" default file name.
        internal async Task SaveGraphAsAsync() {
            string? path = await (SaveFilePathStub?.Invoke() ?? RealPickSaveGraphPathAsync()).ConfigureAwait(true);
            if (path is null)
                return;

            await SaveGraphAsync(path).ConfigureAwait(true);
        }

        private async Task<string?> RealPickSaveGraphPathAsync() {
            if (StorageProvider is not IStorageProvider storage)
                return null;

            Directory.CreateDirectory(AppPaths.SavedGraphsDirectory);
            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = "Save Graph",
                SuggestedFileName = "Flowchart.fjson",
                DefaultExtension = ".fjson",
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(AppPaths.SavedGraphsDirectory)).ConfigureAwait(true),
                FileTypeChoices = [
                    new FilePickerFileType("Foreman files") { Patterns = ["*.fjson"] },
                    new FilePickerFileType("All files") { Patterns = ["*.*"] }
                ],
            }).ConfigureAwait(true);
            return file?.Path.LocalPath;
        }

        //Ports SaveGraph(path) (reference: upstream MainForm.cs:181-196): serializes the whole graph (never
        //a selection subset), writes it, and captures both the save path and the dirty-check baseline before
        //rewriting the title bar. Catches everything, matching upstream's own catch-all.
        internal async Task<bool> SaveGraphAsync(string path) {
            if (DataCache is not Foreman.DataCaching.DataCache cache)
                return false;

            try {
                GraphCanvas.Viewer.Graph.SerializeNodeIdSet = null;
                string json = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, cache), writeIndented: true);
                Utf8File.WriteAllText(path, json);
                savefilePath = path;
                savefileBaselineJson = json;
                UpdateTitleBar();
                return true;
            } catch (Exception exception) {
                await Dialogs.ShowWarningAsync(this, "", "Could not save this file. See log for more details").ConfigureAwait(true);
                Foreman.DataCaching.ErrorLogging.LogException(exception, $"Error saving file '{path}'");
                return false;
            }
        }

        private void CaptureSaveBaseline() {
            if (DataCache is not Foreman.DataCaching.DataCache cache)
                return;

            GraphCanvas.Viewer.Graph.SerializeNodeIdSet = null;
            savefileBaselineJson = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, cache), writeIndented: true);
        }

        //Ports TestGraphSavedStatus (reference: upstream MainForm.cs:298-321, io-reference.md §2 "Dirty
        //tracking"): an untitled empty graph never prompts; an untitled non-empty graph gets the OK/Cancel
        //discard prompt; a titled graph re-serializes and diffs against the save baseline, prompting Yes/No/
        //Cancel only when they differ. Called from New/Load, never from Save/SaveAs/Import.
        internal async Task<bool> TestGraphSavedStatusAsync() {
            const string exitMsg = "The current graph hasn't been saved!\nIf you continue, you will lose it forever!";
            const string exitTitle = "Are you sure?";

            if (DataCache is not Foreman.DataCaching.DataCache cache)
                return true;

            if (savefilePath is null)
                return !GraphCanvas.Viewer.Graph.Nodes.Any() || await ShowDiscardUnsavedGraphConfirmAsync(exitTitle, exitMsg).ConfigureAwait(true);

            if (!File.Exists(savefilePath))
                return await ShowDiscardUnsavedGraphConfirmAsync(exitTitle, exitMsg).ConfigureAwait(true);

            GraphCanvas.Viewer.Graph.SerializeNodeIdSet = null;
            string currentSaveJson = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, cache), writeIndented: true);
            string savedJson = savefileBaselineJson ?? Utf8File.ReadAllText(savefilePath);

            if (savedJson != currentSaveJson) {
                ConfirmChoice choice = await ShowSaveBeforeContinuingChoiceAsync(exitTitle, "The current graph has been modified!\nDo you wish to save before continuing?").ConfigureAwait(true);
                if (choice == ConfirmChoice.Cancel)
                    return false;
                if (choice == ConfirmChoice.Yes)
                    await SaveGraphAsync(savefilePath).ConfigureAwait(true);
            }
            return true;
        }

        private Task<bool> ShowDiscardUnsavedGraphConfirmAsync(string title, string message) =>
            DiscardUnsavedGraphConfirmStub?.Invoke() ?? Dialogs.ShowConfirmAsync(this, title, message);

        private Task<ConfirmChoice> ShowSaveBeforeContinuingChoiceAsync(string title, string message) =>
            SaveBeforeContinuingChoiceStub?.Invoke() ?? Dialogs.ShowYesNoCancelAsync(this, title, message);

        private bool closeConfirmed;

        //Ports MainForm's implicit close gate (upstream never overrides OnFormClosing at all - WinForms
        //just lets the process exit mid-save). This port needs an explicit cancel-then-reclose (Avalonia's
        //Closing can't block synchronously), same pattern as DataLoadWindow.OnClosing: cancel the first
        //Close, await TestGraphSavedStatusAsync, then Close again for real once it says so. ShellBootstrapper's
        //own Closing subscription (settings save) still runs on that second pass, via base.OnClosing.
        protected override void OnClosing(WindowClosingEventArgs e) {
            if (!closeConfirmed) {
                e.Cancel = true;
                Async.Fire(ConfirmCloseAsync(), nameof(ConfirmCloseAsync));
                return;
            }
            base.OnClosing(e);
        }

        private async Task ConfirmCloseAsync() {
            if (await TestGraphSavedStatusAsync().ConfigureAwait(true)) {
                closeConfirmed = true;
                Close();
            }
        }

        //Ports the title-bar format string used identically after Save/Load/New/preset-reload (reference:
        //upstream MainForm.cs:188,240,263,444).
        private void UpdateTitleBar() =>
            Title = string.Format(DisplayCulture.Format, defaultAppName + " ({0}) - {1}", Settings?.CurrentPresetName ?? "", savefilePath ?? "Untitled");

        //Ports SettingsButton_Click's post-close cascade (reference upstream MainForm.cs:355-440): builds
        //Options from live state, shows the dialog, then on OK applies a preset switch/reload before always
        //persisting settings.json - matching upstream's presetReloaded branch and its unconditional
        //Properties.Settings.Default.Save() at the end of ApplySettingsDialogChanges. Tasks 2/3 extend the
        //non-preset-reload branch as their tabs' widgets gain something to apply.
        internal async Task OpenSettingsAsync() {
            //Toolbar/native-menu clicks never reach GraphCanvasControl.OnPointerPressed's own click-outside-
            //closes-panel guard (they're not canvas pointer events at all), so a panel left open under this
            //modal stayed stale once it closed. Closes first, matching NewGraphAsync/LoadDocument's own
            //unconditional-close-on-major-UI-change precedent.
            GraphCanvas.FloatingPanelHost.Close();

            if (DataCache is not Foreman.DataCaching.DataCache cache
                || Settings is not AppSettings settings
                || PresetResolver is not Services.PresetResolver presetResolver
                || SettingsService is not Services.SettingsService settingsService)
                return;

            var options = new SettingsWindow.SettingsWindowOptions(cache) {
                Presets = presetResolver.BuildPresetList(settings.CurrentPresetName),
            };
            options.SelectedPreset = options.Presets.Count > 0 ? options.Presets[0] : null;

            //Ports MainForm.cs:402-406: seeds EnabledObjects from the live cache's current membership before
            //the dialog opens, so the else branch below has a faithful baseline to diff a plain Confirm against.
            options.EnabledObjects.UnionWith(cache.Recipes.Values.Where(r => r.Enabled));
            options.EnabledObjects.UnionWith(cache.Assemblers.Values.Where(r => r.Enabled));
            options.EnabledObjects.UnionWith(cache.Beacons.Values.Where(r => r.Enabled));
            options.EnabledObjects.UnionWith(cache.Modules.Values.Where(r => r.Enabled));
            options.EnabledObjects.UnionWith(cache.Qualities.Values.Where(r => r.Enabled));

            //Ports MainForm.cs:366-400's Graph Options tab population from live GraphViewer/Graph state,
            //falling back to the persisted AppSettings value for the couple of fields with no live control
            //yet (LockedRecipeEditPanelPosition, ShowUnavailable/UseRecipeBWfilters - all DEV/session-only).
            var viewer = GraphCanvas.Viewer;
            options.QualitySteps = viewer.Graph.MaxQualitySteps;
            options.LevelOfDetail = viewer.Context.LevelOfDetail;
            options.NodeCountForSimpleView = viewer.NodeCountForSimpleView;
            options.IconsOnlyIconSize = viewer.Context.IconsDrawSize;
            options.ArrowsOnLinks = viewer.Context.ArrowsOnLinks;
            options.SimplePassthroughNodes = viewer.Graph.DefaultToSimplePassthroughNodes;
            options.DynamicLinkWidth = viewer.Context.DynamicLinkWidth;
            options.ShowRecipeToolTip = viewer.Context.ShowRecipeToolTip;
            options.LockedRecipeEditPanelPosition = settings.LockedRecipeEditorPosition;
            options.FlagOUSuppliedNodes = viewer.Context.FlagOUSuppliedNodes;
            options.FlagDarkMode = settings.FlagDarkMode;
            options.DefaultAssemblerStyle = viewer.Graph.AssemblerSelector.DefaultSelectionStyle;
            options.DefaultModuleStyle = viewer.Graph.ModuleSelector.DefaultSelectionStyle;
            options.DefaultNodeDirection = viewer.Graph.DefaultNodeDirection;
            options.SmartNodeDirection = GraphCanvas.SmartNodeDirection;
            options.ShowErrorArrows = viewer.ArrowRenderer.ShowErrorArrows;
            options.ShowWarningArrows = viewer.ArrowRenderer.ShowWarningArrows;
            options.ShowDisconnectedArrows = viewer.ArrowRenderer.ShowDisconnectedArrows;
            options.ShowOUSuppliedArrows = viewer.ArrowRenderer.ShowOUNodeArrows;
            options.RoundAssemblerCount = viewer.Context.RoundAssemblerCount;
            options.AbbreviateSciPacks = viewer.Context.AbbreviateSciPacks;
            options.EnableExtraProductivityForNonMiners = viewer.Graph.EnableExtraProductivityForNonMiners;
            options.DevShowUnavailableItems = settings.ShowUnavailable;
            options.DevUseRecipeBWFilters = settings.UseRecipeBWfilters;
            options.SolverLowPriorityPower = (decimal)viewer.Graph.LowPriorityPower;
            options.SolverPullConsumerNodes = viewer.Graph.PullOutputNodes;
            options.SolverPullConsumerNodesPower = (decimal)viewer.Graph.PullOutputNodesPower;

            bool? result = await ShowSettingsDialogAsync(options).ConfigureAwait(true);
            if (result != true)
                return;

            bool presetReloaded = options.RequireReload
                || options.SelectedPreset != (options.Presets.Count > 0 ? options.Presets[0] : null)
                || options.DevUseRecipeBWFilters != settings.UseRecipeBWfilters;
            if (presetReloaded) {
                settings.UseRecipeBWfilters = options.DevUseRecipeBWFilters;
                if (options.SelectedPreset is Preset selectedPreset)
                    await ReloadForPresetAsync(selectedPreset, settings).ConfigureAwait(true);
            } else {
                //Ports MainForm.cs:422-435's not-loading-a-new-preset branch: writes EnabledObjects membership
                //straight onto the live cache instead of reloading it.
                foreach (IRecipe recipe in cache.Recipes.Values)
                    recipe.Enabled = options.EnabledObjects.Contains(recipe);
                foreach (IAssembler assembler in cache.Assemblers.Values)
                    assembler.Enabled = options.EnabledObjects.Contains(assembler);
                foreach (IBeacon beacon in cache.Beacons.Values)
                    beacon.Enabled = options.EnabledObjects.Contains(beacon);
                foreach (IModule module in cache.Modules.Values)
                    module.Enabled = options.EnabledObjects.Contains(module);
                foreach (IQuality quality in cache.Qualities.Values)
                    quality.Enabled = options.EnabledObjects.Contains(quality);
                cache.DefaultQuality?.Enabled = true;
                cache.RocketAssembler?.Enabled = cache.Assemblers.TryGetValue("rocket-silo", out IAssembler? silo) && silo?.Enabled == true;
            }

            //Ports ApplySettingsDialogChanges' Graph Options half (reference §5, upstream MainForm.cs
            //441-497): rather than duplicating a push-to-GraphViewer/push-to-Properties.Settings.Default
            //pair per field like upstream, we write the committed values onto AppSettings once and replay
            //them through the same ApplyLoadedSettings seam cold start and preset reloads already use -
            //a single settings-to-live-graph path instead of two. LockedRecipeEditorPosition is persisted
            //without a live effect yet (GraphCanvasControl.EditNode's locked-origin branch is future scope,
            //see its own comment).
            settings.QualitySteps = (int)options.QualitySteps;
            settings.LevelOfDetail = (int)options.LevelOfDetail;
            settings.NodeCountForSimpleView = options.NodeCountForSimpleView;
            settings.IconsSize = options.IconsOnlyIconSize;
            settings.ArrowsOnLinks = options.ArrowsOnLinks;
            settings.SimplePassthroughNodes = options.SimplePassthroughNodes;
            settings.DynamicLineWidth = options.DynamicLinkWidth;
            settings.ShowRecipeToolTip = options.ShowRecipeToolTip;
            settings.LockedRecipeEditorPosition = options.LockedRecipeEditPanelPosition;
            settings.FlagOUSuppliedNodes = options.FlagOUSuppliedNodes;
            settings.FlagDarkMode = options.FlagDarkMode;
            settings.DefaultAssemblerOption = options.DefaultAssemblerStyle;
            settings.DefaultModuleOption = options.DefaultModuleStyle;
            settings.DefaultNodeDirection = options.DefaultNodeDirection;
            settings.SmartNodeDirection = options.SmartNodeDirection;
            settings.ShowErrorArrows = options.ShowErrorArrows;
            settings.ShowWarningArrows = options.ShowWarningArrows;
            settings.ShowDisconnectedArrows = options.ShowDisconnectedArrows;
            settings.ShowOUSuppliedArrows = options.ShowOUSuppliedArrows;
            settings.RoundAssemblerCount = options.RoundAssemblerCount;
            settings.AbbreviateSciPacks = options.AbbreviateSciPacks;
            settings.EnableExtraProductivityForNonMiners = options.EnableExtraProductivityForNonMiners;
            settings.ShowUnavailable = options.DevShowUnavailableItems;
            settings.LowPriorityPower = options.SolverLowPriorityPower;
            settings.PullConsumerNodes = options.SolverPullConsumerNodes;
            settings.PullConsumerNodesPower = options.SolverPullConsumerNodesPower;
            if (options.LastSaveFileLocation is string lastSaveFileLocation)
                settings.LastSaveFileLocation = lastSaveFileLocation;

            ApplyLoadedSettings();
            (Application.Current as App)?.ApplyTheme(settings.FlagDarkMode);

            //Ports MainForm.cs:501-503's apply-tail: a Confirm must actually re-solve against the settings
            //just committed, not just write them onto AppSettings/the live graph properties.
            GraphCanvas.Viewer.Graph.UpdateNodeMaxQualities();
            GraphCanvas.Viewer.Graph.UpdateNodeStates(true);
            GraphCanvas.Viewer.Graph.UpdateNodeValues();
            GraphCanvas.InvalidateVisual();

            settingsService.Save(settings);

            //Ports MainForm.cs:505-506's reopen tail: a preset import that overwrote the currently active
            //preset (RequireReload) reloads it above, then Settings reopens itself so the user sees a fresh
            //dialog reflecting whatever that reload changed - retires the placeholder-import divergence note
            //(docs/upstream-divergences.md).
            if (options.RequireReload)
                await OpenSettingsAsync().ConfigureAwait(true);
        }

        private Task<bool?> ShowSettingsDialogAsync(SettingsWindow.SettingsWindowOptions options) =>
            SettingsDialogStub?.Invoke(options) ?? RealShowSettingsDialogAsync(options);

        //Ports SettingsButton_Click's modal launch (reference upstream MainForm.cs:410-412): owner+50/+50,
        //matching the summary and comparator windows' own Manual placement.
        private async Task<bool?> RealShowSettingsDialogAsync(SettingsWindow.SettingsWindowOptions options) {
            var window = new SettingsWindow(options) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };
            bool? result = await window.ShowDialog<bool?>(this).ConfigureAwait(true);
            //Extends FloatingPanelHost.Close's own focus-restore contract (reference §7) to a genuine modal
            //window: Avalonia doesn't hand focus back to GraphCanvas on its own once the dialog closes,
            //leaving canvas keyboard shortcuts (Space, Delete, ...) dead until the next click. FloatingPanelHost.
            //IsOpen is always false here now - OpenSettingsAsync's own toolbar-staleness fix (phase 7) closes
            //any open panel before this dialog ever opens - so the guard below always focuses the canvas; kept
            //as a defensive belt-and-braces in case a future call site reaches this with a panel still open:
            //pulling focus onto the canvas would then leave the panel's own content with none, and
            //GraphCanvasControl.OnKeyDown itself early-returns while a panel is open, so its keyboard would
            //go dead too until the next click.
            if (!GraphCanvas.FloatingPanelHost.IsOpen)
                GraphCanvas.Focus();
            return result;
        }

        //Ports GraphSummaryButton_Click (reference upstream MainForm.cs:564-569): a true modal blocking
        //dialog positioned at owner+50/+50, not the Designer's CenterParent default - always the whole
        //session, matching MainForm's own call (the node-subset constructor overload has no live caller
        //upstream either).
        internal Task OpenGraphSummaryAsync() {
            //See OpenSettingsAsync's own close-first comment: same toolbar-bypasses-canvas-pointer-events gap.
            GraphCanvas.FloatingPanelHost.Close();

            if (GraphSummaryDialogStub is Action<IProductionGraphSession> stub) {
                stub(GraphCanvas.Viewer.Session);
                return Task.CompletedTask;
            }
            return RealShowGraphSummaryAsync();
        }

        private async Task RealShowGraphSummaryAsync() {
            var window = new GraphSummaryWindow(GraphCanvas.Viewer.Session, GraphCanvas.Viewer.Graph.GetRateName()) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };
            await window.ShowDialog(this).ConfigureAwait(true);
            //See RealShowSettingsDialogAsync's own focus-restore comment, including the open-panel guard.
            if (!GraphCanvas.FloatingPanelHost.IsOpen)
                GraphCanvas.Focus();
        }

        //Ports ExportImageButton_Click (reference upstream MainForm.cs:508-514): owner+50/+50, same Manual
        //placement as Settings/Graph Summary.
        internal Task OpenImageExportAsync() {
            //See OpenSettingsAsync's own close-first comment: same toolbar-bypasses-canvas-pointer-events gap.
            GraphCanvas.FloatingPanelHost.Close();

            if (ImageExportDialogStub is Action<GraphViewer> stub) {
                stub(GraphCanvas.Viewer);
                return Task.CompletedTask;
            }
            return RealShowImageExportAsync();
        }

        private async Task RealShowImageExportAsync() {
            var window = new ImageExportWindow(GraphCanvas.Viewer) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };
            await window.ShowDialog(this).ConfigureAwait(true);
            if (!GraphCanvas.FloatingPanelHost.IsOpen)
                GraphCanvas.Focus();
        }

        //Ports ReloadGraphForCurrentPreset (MainForm.cs:416-421, ProductionGraphViewer.cs:1409-1412): swaps
        //in the new preset's DataCache via LoadDataCacheForPresetAsync, then remaps the live graph onto it
        //by round-tripping through the save codec (GraphViewerSaveAssembler.BuildSaveDocument -> GraphCanvas.
        //LoadDocument), the same path GraphSaveLoader uses for an ordinary file load - nodes whose items
        //exist in the new preset survive; the rest are dropped by LoadProductionGraph's own per-node remap.
        //setEnablesFromJson: false (upstream ProductionGraphViewer.cs:1289, ReloadGraphForCurrentPreset's own
        //call) matters here specifically: the serialized document's EnabledRecipes/etc. still name the OLD
        //cache's enabled set, which would otherwise zero out everything in the new cache that doesn't share a
        //name with it - the freshly booted cache's own default-enabled state is what should survive instead.
        private async Task ReloadForPresetAsync(Preset preset, AppSettings settings) {
            if (DataCache is not Foreman.DataCaching.DataCache oldCache)
                return;

            string graphJson = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, oldCache));

            Foreman.DataCaching.DataCache? newCache = await LoadDataCacheForPresetAsync(preset, settings).ConfigureAwait(true);
            if (newCache is null)
                return;

            GraphLoadResult result = GraphCanvas.LoadDocument(newCache, graphJson, setEnablesFromJson: false);
            if (!result.Success)
                await Dialogs.ShowWarningAsync(this, "Cannot load save", result.ErrorMessage ?? "This save file is too old or corrupt.").ConfigureAwait(true);

            ApplyLoadedSettings();
            UpdateTitleBar();
            GraphCanvas.RequestRedraw();
        }

        private void BindToolbarButtons() {
            Bind("NewGraphButton", Commands.New);
            Bind("LoadGraphButton", Commands.Load);
            Bind("SaveButton", Commands.Save);
            Bind("SaveAsGraphButton", Commands.SaveAs);
            Bind("ImportGraphButton", Commands.Import);
            Bind("ExportImageButton", Commands.ExportImage);
            Bind("AddItemButton", Commands.AddItem);
            Bind("AddRecipeButton", Commands.AddRecipe);
            Bind("AutoconnectButton", Commands.Autoconnect);
            Bind("SettingsButton", Commands.Settings);
            Bind("HelpButton", Commands.Help);
            Bind("GraphSummaryButton", Commands.GraphSummary);
            Bind("AlignSelectionButton", Commands.AlignSelection);
        }

        //Ports GraphViewer_KeyDown's Space handler (reference §7): a separate subscription on the canvas
        //control's own KeyDown event, alongside (not inside) GraphCanvasControl's own OnKeyDown override -
        //mirrors upstream attaching this as MainForm's own delegate rather than folding it into
        //ProductionGraphViewer_KeyDown itself, since it needs to reach the toolbar's gridlines checkbox.
        private void OnGraphCanvasKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key != Key.Space)
                return;

            GraphCanvas.Grid.ShowGrid = !GraphCanvas.Grid.ShowGrid;
            this.FindControl<CheckBox>("GridlinesCheckbox")!.IsChecked = GraphCanvas.Grid.ShowGrid;
            GraphCanvas.InvalidateVisual();
            e.Handled = true;
        }

        private void Bind(string buttonName, ShellCommand command) {
            var button = this.FindControl<Button>(buttonName)!;
            button.Command = command;
            button.IsEnabled = command.IsImplemented;
        }

        private NativeMenu BuildNativeMenu() {
            var appMenu = new NativeMenu {
                Items = {
                    new NativeMenuItem("About Foreman 2") { IsEnabled = false },
                    MenuItem("Settings", Commands.Settings),
                    new NativeMenuItemSeparator(),
                    QuitMenuItem(),
                },
            };

            var fileMenu = new NativeMenu {
                Items = {
                    MenuItem("New", Commands.New, new KeyGesture(Key.N, PlatformModifiers.Primary)),
                    MenuItem("Open", Commands.Load, new KeyGesture(Key.O, PlatformModifiers.Primary)),
                    MenuItem("Save", Commands.Save, new KeyGesture(Key.S, PlatformModifiers.Primary)),
                    MenuItem("Save As", Commands.SaveAs, new KeyGesture(Key.S, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    MenuItem("Import", Commands.Import),
                    MenuItem("Export Image", Commands.ExportImage, new KeyGesture(Key.E, PlatformModifiers.Primary | KeyModifiers.Shift)),
                },
            };

            var editMenu = new NativeMenu {
                Items = {
                    new NativeMenuItem("Undo") { IsEnabled = false },
                    new NativeMenuItem("Redo") { IsEnabled = false },
                    new NativeMenuItemSeparator(),
                    new NativeMenuItem("Cut") { IsEnabled = false },
                    new NativeMenuItem("Copy") { IsEnabled = false },
                    new NativeMenuItem("Paste") { IsEnabled = false },
                },
            };

            var viewMenu = new NativeMenu {
                Items = {
                    new NativeMenuItem("Show Gridlines") { IsEnabled = false },
                    new NativeMenuItem("Rate Unit") { IsEnabled = false },
                    new NativeMenuItem("Icon View") { IsEnabled = false },
                    new NativeMenuItem("Level of Detail") { IsEnabled = false },
                },
            };

            var helpMenu = new NativeMenu { Items = { MenuItem("Help / Git repo", Commands.Help) } };

            return new NativeMenu {
                Items = {
                    new NativeMenuItem { Menu = appMenu },
                    new NativeMenuItem("File") { Menu = fileMenu },
                    new NativeMenuItem("Edit") { Menu = editMenu },
                    new NativeMenuItem("View") { Menu = viewMenu },
                    new NativeMenuItem("Help") { Menu = helpMenu },
                },
            };
        }

        private static NativeMenuItem MenuItem(string header, ShellCommand command, KeyGesture? gesture = null) =>
            new(header) { Command = command, IsEnabled = command.IsImplemented, Gesture = gesture };

        private static NativeMenuItem QuitMenuItem() {
            var item = new NativeMenuItem("Quit Foreman 2") { Gesture = new KeyGesture(Key.Q, PlatformModifiers.Primary) };
            item.Click += (_, _) => (Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
            return item;
        }
    }
}
