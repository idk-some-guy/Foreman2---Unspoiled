using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using Foreman.Mac.Views.Inspector;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using Foreman.Serialization;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Foreman.Mac {
    public partial class MainWindow : Window, IThemeAware {
        public ShellViewModel Commands { get; }
        public MainWindowViewModel ViewModel { get; }

        //Task 7 (contract #toolbar's zoom stepper, marked "(new)"): wraps MenuZoomIn/MenuZoomOut so the
        //stepper's own buttons and BuildNativeMenu's View > Zoom In/Zoom Out items share one command
        //instance each - the same shared-instance pattern Commands.SaveCommand etc. already use across the
        //toolbar and the native menu (see Bind/MenuItem below).
        public IRelayCommand ZoomInCommand { get; }
        public IRelayCommand ZoomOutCommand { get; }
        //The readout-click command (contract #toolbar's zoom stepper), shared the same way ZoomIn/Out are.
        public IRelayCommand ActualSizeCommand { get; }
        public ContentControl GraphViewerHost { get; }
        public GraphCanvasControl GraphCanvas { get; }
        public InspectorView Inspector { get; }
        public Foreman.DataCaching.DataCache? DataCache { get; set; }
        public AppSettings? Settings { get; set; }
        public PresetResolver? PresetResolver { get; set; }
        public SettingsService? SettingsService { get; set; }

        private readonly WindowMinSizeScale minSizeScale;
        private readonly ToolbarRow toolbar;
        private ToolbarItem viewOptionsItem = null!;
        private ToolbarItem saveItem = null!;
        private PopoverFrame viewOptionsPopover = null!;
        private PopUp minorGridlinesPopUp = null!;
        private PopUp majorGridlinesPopUp = null!;
        private CheckBox gridlinesCheckBox = null!;
        private PopUp rateOptionsPopUp = null!;
        private CheckBox iconViewCheckBox = null!;
        private CheckBox pauseUpdatesCheckBox = null!;
        private bool documentEdited;
        private int searchMatchIndex = -1;
        private List<BaseNodeElement> searchMatches = [];
        private readonly SearchAnnouncer searchAnnouncer = new() { Name = "SearchAnnouncer", Opacity = 0, Width = 0, Height = 0, IsHitTestVisible = false };

        //Test-only seam: the built toolbar row, its search field and zoom stepper.
        internal ToolbarRow Toolbar => toolbar;
        internal bool DocumentEdited => documentEdited;

        //One "Add to Graph" browser for the window's lifetime; BrowseRequested reopens/reseeds it.
        private AddBrowserWindow? browser;
        internal AddBrowserWindow? BrowserWindow => browser;
        internal AddRequest? BrowserRequest { get; private set; }
        //Keyed on (bitmap, size): OpenBrowser bakes the same source icons at 16px (group sidebar) and
        //40px (cell grid), and DataCache.UnknownIcon is one shared SKBitmap instance across every entry.
        private readonly Dictionary<(SKBitmap, int), Bitmap> browserIconCache = [];

        //Test-only seam: lets a test drive the Settings window headless instead of a real Show(this) - the
        //window is fully constructed and wired (ViewModel, Session, ReloadRequested) either way.
        internal Action<SettingsWindow>? SettingsDialogStub { get; set; }

        //R5/R9: MainWindow owns one Settings instance and one comparator-adjacent session for the app's
        //lifetime - ⌘, reuses/activates it rather than stacking a second window.
        private SettingsWindow? settingsWindow;
        private SettingsSession? settingsSession;

        internal SettingsWindow? CurrentSettingsWindow => settingsWindow;
        internal SettingsSession? CurrentSettingsSession => settingsSession;

        //Test-only seam: lets a test observe/short-circuit the Graph Summary dialog without a real modal
        //ShowDialog.
        internal Action<IProductionGraphSession>? GraphSummaryDialogStub { get; set; }

        //Test-only seam: lets a test observe/short-circuit the Image Export dialog without a real modal
        //ShowDialog.
        internal Action<GraphViewer>? ImageExportDialogStub { get; set; }

        //Test-only seam: lets a test supply Save As's file-save-picker result without a real modal picker
        //(mirrors GraphSummaryWindow's own SaveFilePathStub).
        internal Func<Task<string?>>? SaveFilePathStub { get; set; }

        //Test-only seams for TestGraphSavedStatus's discard and save prompts.
        internal Func<Task<bool>>? DiscardUnsavedGraphConfirmStub { get; set; }
        internal Func<Task<ConfirmChoice>>? SaveBeforeContinuingChoiceStub { get; set; }

        //Test-only seam: lets a test supply the cross-preset PresetSelectionWindow's outcome (io-reference.md
        //§8 slow path) without a real modal ShowDialog.
        internal Func<List<PresetErrorPackage>, Task<Preset?>>? PresetSelectionDialogStub { get; set; }

        //Test-only seam: lets a test observe/short-circuit the silent-switch info message (io-reference.md
        //§8) without a real modal ShowDialog.
        internal Func<string, Task>? PresetSwitchInfoStub { get; set; }

        //Test-only seam: lets a test observe/short-circuit the About panel without a real modal ShowDialog,
        //same shape as GraphSummaryDialogStub above.
        internal Action? AboutDialogStub { get; set; }

        //Ports MainForm's savefilePath/savefileBaselineJson fields (reference: upstream MainForm.cs, io-
        //reference.md §2). savefileBaselineJson is the dirty-check baseline: a captured JSON string, not a
        //re-read from disk, refreshed by SaveGraph/ApplyLoadedGraphUiState and cleared by New.
        private string? savefilePath;
        private string? savefileBaselineJson;

        internal string? SaveFilePath => savefilePath;
        internal string? SaveFileBaselineJson => savefileBaselineJson;

        //A (contract #menubar): the bold app menu lives on Application, built once in App.Initialize
        //before any MainWindow exists (see the call site there), so its About/Settings/Hide items can't
        //close over a window instance - they resolve the live target here instead, at click time.
        internal static MainWindow? MostRecentlyOpened { get; private set; }

        private static MainWindow? ActiveWindow =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow
            ?? MostRecentlyOpened;

        public MainWindow() {
            InitializeComponent();
            WindowState = WindowState.Maximized;
            GraphViewerHost = this.FindControl<ContentControl>("GraphViewerHost")!;
            GraphCanvas = new GraphCanvasControl();
            GraphViewerHost.Content = GraphCanvas;
            Inspector = this.FindControl<InspectorView>("Inspector")!;
            Inspector.Viewer = GraphCanvas.Viewer;
            Inspector.RequestRedraw = () => GraphCanvas.InvalidateVisual();
            toolbar = this.FindControl<ToolbarRow>("Toolbar")!;
            ZoomInCommand = new RelayCommand(MenuZoomIn);
            ZoomOutCommand = new RelayCommand(MenuZoomOut);
            ActualSizeCommand = new RelayCommand(MenuActualSize);
            Commands = new ShellViewModel(
                onNew: () => Async.Fire(NewGraphAsync(), nameof(NewGraphAsync)),
                onLoad: () => Async.Fire(LoadGraphAsync(), nameof(LoadGraphAsync)),
                onSave: () => Async.Fire(SaveGraphOrPromptAsync(), nameof(SaveGraphOrPromptAsync)),
                onSaveAs: () => Async.Fire(SaveGraphAsAsync(), nameof(SaveGraphAsAsync)),
                onImport: () => Async.Fire(ImportGraphAsync(), nameof(ImportGraphAsync)),
                onExportImage: () => Async.Fire(OpenImageExportAsync(), nameof(OpenImageExportAsync)),
                onAddItem: () => GraphCanvas.AddItemAsync(ViewportCentre(), PaletteAnchorUnder("AddItemButton")),
                onAddRecipe: () => GraphCanvas.AddRecipeAsync(ViewportCentre(), PaletteAnchorUnder("AddRecipeButton")),
                onAutoconnect: () => { GraphCanvas.ClosePanels(); GraphCanvas.Viewer.AutoconnectDisconnectedInputs(); GraphCanvas.RequestRedraw(); },
                onAlignSelection: () => { GraphCanvas.ClosePanels(); GraphCanvas.Viewer.AlignSelected(); GraphCanvas.RequestRedraw(); },
                onSettings: () => ShowSettings(),
                onGraphSummary: () => Async.Fire(OpenGraphSummaryAsync(), nameof(OpenGraphSummaryAsync)),
                canExecuteAlignSelection: () => GraphCanvas.Viewer.SelectedNodes.Count > 0);
            Inspector.AddRecipeCommand = Commands.AddRecipeCommand;
            Inspector.OpenPresetsRequested += (_, _) => ShowSettings(selectPresetsPane: true);
            Inspector.PickAnotherRecipeRequested += (_, node) =>
                OpenBrowser(new AddRequest(node.Location, AddScope.Recipes, SeedText: node.BaseRecipe.FriendlyName ?? ""));
            Inspector.RateUnitChanged += (_, unit) => SetRateUnit(unit);
            //Task 7: keeps AlignSelectionCommand's CanExecute in step with the canvas's live selection -
            //GraphViewer.SelectionChanged is the aggregate signal every selection-mutating method raises.
            GraphCanvas.Viewer.SelectionChanged += (_, _) => Commands.AlignSelectionCommand.NotifyCanExecuteChanged();

            GraphCanvas.BrowseRequested += (_, request) => OpenBrowser(request);

            ViewModel = new MainWindowViewModel();
            DataContext = ViewModel;

            //Same SelectionChanged signal AlignSelectionCommand.CanExecute uses above - not a second tracker.
            GraphCanvas.Viewer.SelectionChanged += (_, _) =>
                ViewModel.Inspector.SetSelection([.. GraphCanvas.Viewer.SelectedNodes.Select(n => n.ViewModel)]);
            Inspector.IsVisible = ViewModel.IsInspectorVisible;
            //Commits once per resize gesture; converts the rendered (scaled) pixel value back to InspectorWidth's logical units before saving.
            Inspector.WidthCommitted += (_, value) => {
                if (Settings is AppSettings inspectorSettings) {
                    inspectorSettings.InspectorWidth = Math.Clamp(value / WindowScaler.CurrentScale, 260, 380);
                    SettingsService?.Save(inspectorSettings);
                }
            };
            ViewModel.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(MainWindowViewModel.IsInspectorVisible))
                    Inspector.IsVisible = ViewModel.IsInspectorVisible;
                else if (e.PropertyName == nameof(MainWindowViewModel.DocumentTitle))
                    toolbar.Title = ViewModel.DocumentTitle;
                else if (e.PropertyName == nameof(MainWindowViewModel.IsDirty)) {
                    saveItem.IsOn = ViewModel.IsDirty;
                    ApplyDocumentEditedState(ViewModel.IsDirty);
                } else if (e.PropertyName == nameof(MainWindowViewModel.IsViewOptionsOpen))
                    viewOptionsPopover.IsOpen = ViewModel.IsViewOptionsOpen;
            };

            //Task 8 (contract #toolbar's Save .on dirty-tint state): one genuine graph-mutated signal -
            //node/link add/remove, node-property edits, a committed node drag, annotation add/remove/edit.
            //Replaces the five-event wiring the C1 gate flagged (M-4): NodeValuesUpdated fired on a plain
            //solve too, so Save lit up with nothing to save - a UI hint only, never the close-confirmation gate.
            GraphCanvas.Viewer.Session.GraphMutated += (_, _) => ViewModel.IsDirty = true;

            BuildToolbarRow();
            BuildViewOptionsPopover();
            this.FindControl<Grid>("TitleBarRow")!.Children.Add(searchAnnouncer);
            toolbar.Title = ViewModel.DocumentTitle;
            GraphCanvas.KeyDown += OnGraphCanvasKeyDown;
            //Mouse-wheel zoom bypasses every other ResyncZoomLabel call site, so it gets its own via this signal.
            GraphCanvas.ViewScaleChanged += (_, _) => ResyncZoomLabel();
            NativeMenu.SetMenu(this, BuildNativeMenu());
            Opened += (_, _) => MostRecentlyOpened = this;
            //SheetHost overlays Content directly (spanning child); TitleBarRow as the anchor keeps sheets
            //flush under the toolbar instead of covering it.
            Views.Sheets.SheetHost.For(this, this.FindControl<Grid>("TitleBarRow"));
            minSizeScale = new WindowMinSizeScale(this, (1150, 720));

            AppearanceHub.Current?.Subscribe(this);
            Closed += (_, _) => {
                AppearanceHub.Current?.Unsubscribe(this);
                foreach (Bitmap bitmap in browserIconCache.Values)
                    bitmap.Dispose();
                browserIconCache.Clear();
            };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //v2 §02: document -> add -> arrange -> share/view; Settings stays app-menu-only (no gear).
        private void BuildToolbarRow() {
            saveItem = ToolbarButton("SaveButton", "Save", "Save", Commands.SaveCommand, acceleratorKey: "⌘S");
            toolbar.Groups.Add(new ToolbarGroup {
                Role = ToolbarGroupRole.Document,
                Items = [
                    ToolbarButton("NewGraphButton", "New", "New", Commands.NewCommand, automationName: "New Graph", acceleratorKey: "⌘N"),
                    ToolbarButton("LoadGraphButton", "Open", "Load", Commands.LoadCommand, acceleratorKey: "⌘O"),
                    saveItem,
                ],
            });
            toolbar.Groups.Add(new ToolbarGroup {
                Role = ToolbarGroupRole.Add,
                Items = [
                    ToolbarButton("AddItemButton", "Item", "Item", Commands.AddItemCommand, automationName: "Add Item"),
                    ToolbarButton("AddRecipeButton", "Recipe", "Recipe", Commands.AddRecipeCommand, automationName: "Add Recipe"),
                ],
            });
            toolbar.Groups.Add(new ToolbarGroup {
                Role = ToolbarGroupRole.Arrange,
                Items = [
                    ToolbarButton("AutoconnectButton", "Connect", "Autoconnect", Commands.AutoconnectCommand),
                    ToolbarButton("AlignSelectionButton", "Align", "Align", Commands.AlignSelectionCommand, automationName: "Align Selected"),
                ],
            });
            viewOptionsItem = ToolbarButton("ViewOptionsButton", "View", "View Options", ViewModel.ToggleViewOptionsCommand);
            toolbar.Groups.Add(new ToolbarGroup {
                Role = ToolbarGroupRole.ShareView,
                Items = [
                    ToolbarButton("ExportImageButton", "Export", "Export", Commands.ExportImageCommand, automationName: "Export Image", acceleratorKey: "⇧⌘E"),
                    ToolbarButton("GraphSummaryButton", "Summary", "Summary", Commands.GraphSummaryCommand, automationName: "Graph Summary"),
                    viewOptionsItem,
                ],
            });

            toolbar.SearchField.PlaceholderText = "Find node";
            AutomationProperties.SetName(toolbar.SearchField, "Find node");
            toolbar.SearchField.TextChanged += (_, _) => ResetSearchMatches();
            GraphCanvas.Viewer.Session.GraphMutated += (_, _) => RecomputeSearchMatchesOnGraphChange();
            toolbar.SearchField.SearchCommitted += (_, _) => StepSearchMatch(forward: true);
            toolbar.SearchField.SearchCommittedBackward += (_, _) => StepSearchMatch(forward: false);
            toolbar.ZoomStepper.ActualSizeCommand = ActualSizeCommand;
            toolbar.ZoomStepper.ZoomInCommand = ZoomInCommand;
            toolbar.ZoomStepper.ZoomOutCommand = ZoomOutCommand;
        }

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"MainWindow: kit theme '{key}' not found.");
        }

        private System.Drawing.Point ViewportCentre() =>
            GraphCanvas.Viewport.ScreenToGraph(new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2));

        private Avalonia.Point? PaletteAnchorUnder(string itemName) {
            if (this.GetVisualDescendants().OfType<ToolbarItem>().FirstOrDefault(i => i.Name == itemName && i.IsEffectivelyVisible) is not ToolbarItem item
                || item.TranslatePoint(new Avalonia.Point(0, item.Bounds.Height), GraphCanvas) is not Avalonia.Point under)
                return null;
            return new Avalonia.Point(under.X, Math.Max(0, under.Y));
        }

        //Label copy is the ratified mock's own (v2 §06.1, A16/A22/A26): "Show"/"Minor"/"Major" under "Gridlines", "Rate unit"/"Display" under "Graph".
        private void BuildViewOptionsPopover() {
            minorGridlinesPopUp = new PopUp { Name = "MinorGridlinesDropDown", Theme = KitTheme("Kit.PopUp") };
            majorGridlinesPopUp = new PopUp { Name = "MajorGridlinesDropDown", Theme = KitTheme("Kit.PopUp") };
            foreach (string item in GridlineLabels) {
                minorGridlinesPopUp.Items.Add(item);
                majorGridlinesPopUp.Items.Add(item);
            }
            minorGridlinesPopUp.SelectedItem = GridlineLabels[0];
            majorGridlinesPopUp.SelectedItem = GridlineLabels[0];
            minorGridlinesPopUp.Command = new RelayCommand<string>(item => OnGridlineCommitted(isMinor: true, item));
            majorGridlinesPopUp.Command = new RelayCommand<string>(item => OnGridlineCommitted(isMinor: false, item));

            gridlinesCheckBox = new CheckBox { Name = "GridlinesCheckbox", Theme = KitTheme("Kit.CheckBox"), Content = "Show gridlines" };

            rateOptionsPopUp = new PopUp { Name = "RateOptionsDropDown", Theme = KitTheme("Kit.PopUp") };
            foreach (string item in RateUnitOptions)
                rateOptionsPopUp.Items.Add(item);
            rateOptionsPopUp.SelectedItem = RateUnitOptions[0];
            rateOptionsPopUp.Command = new RelayCommand<string>(OnRateOptionsCommitted);

            iconViewCheckBox = new CheckBox { Name = "IconViewCheckBox", Theme = KitTheme("Kit.CheckBox"), Content = "Icon view" };
            pauseUpdatesCheckBox = new CheckBox { Name = "PauseUpdatesCheckbox", Theme = KitTheme("Kit.CheckBox"), Content = "Pause all calculations" };

            var gridlinesGroup = new FormGroup { Theme = KitTheme("Kit.FormGroup"), Title = "Gridlines" };
            gridlinesGroup.Items.Add(new FormRow { Theme = KitTheme("Kit.FormRow"), Label = "Show", LabelColumn = FormRowLabelColumn.Popover, Content = gridlinesCheckBox });
            gridlinesGroup.Items.Add(new FormRow { Theme = KitTheme("Kit.FormRow"), Label = "Minor", LabelColumn = FormRowLabelColumn.Popover, Content = minorGridlinesPopUp });
            gridlinesGroup.Items.Add(new FormRow { Theme = KitTheme("Kit.FormRow"), Label = "Major", LabelColumn = FormRowLabelColumn.Popover, Content = majorGridlinesPopUp });

            var display = new StackPanel { Spacing = 6, Children = { iconViewCheckBox, pauseUpdatesCheckBox } };
            var graphGroup = new FormGroup { Theme = KitTheme("Kit.FormGroup"), Title = "Graph", TrailingGap = 0 };
            graphGroup.Items.Add(new FormRow { Theme = KitTheme("Kit.FormRow"), Label = "Rate unit", LabelColumn = FormRowLabelColumn.Popover, Content = rateOptionsPopUp });
            graphGroup.Items.Add(new FormRow { Theme = KitTheme("Kit.FormRow"), Label = "Display", LabelColumn = FormRowLabelColumn.Popover, Content = display });

            var content = new StackPanel { Spacing = 0, Children = { gridlinesGroup, graphGroup } };
            AutomationProperties.SetName(content, "View Options");
            viewOptionsPopover = new PopoverFrame {
                Name = "ViewOptionsPopover", Theme = KitTheme("Kit.PopoverFrame"), Content = content, Anchor = viewOptionsItem,
            };
            viewOptionsPopover.Bind(PopoverFrame.PanelWidthProperty, new DynamicResourceExtension("Foreman.Size.PopoverWidth"));
            viewOptionsPopover.Closed += (_, _) => ViewModel.IsViewOptionsOpen = false;

            this.FindControl<PopoverHost>("ContentPopoverHost")!.Surface.Children.Add(viewOptionsPopover);
        }

        //AutomationName carries the full command name for VoiceOver; acceleratorKey is the command's own menu-bar shortcut, when it has one.
        private static ToolbarItem ToolbarButton(string name, string glyphName, string label, IRelayCommand command, string? automationName = null, string? acceleratorKey = null) {
            var item = new ToolbarItem { Name = name, Glyph = ToolbarGlyph(glyphName), Label = label, AutomationName = automationName, Command = command, IsEnabled = command.CanExecute(null) };
            if (acceleratorKey is not null)
                AutomationProperties.SetAcceleratorKey(item, acceleratorKey);
            command.CanExecuteChanged += (_, _) => item.IsEnabled = command.CanExecute(null);
            return item;
        }

        private static Geometry? ToolbarGlyph(string name) =>
            Application.Current?.TryGetResource($"Kit.Glyph.{name}.17", null, out object? geometry) == true ? geometry as Geometry : null;

        //Best-effort real AppKit affordance for A21 (unsaved state is the close button's dot, never title text).
        private void ApplyDocumentEditedState(bool isDirty) {
            documentEdited = isDirty;
            if (OperatingSystem.IsMacOS() && TryGetPlatformHandle() is { HandleDescriptor: "NSWindow" } handle)
                NativeWindowChrome.SetDocumentEdited(handle.Handle, isDirty);
        }

        //Raw P/Invoke, not LibraryImport: CA2101's source-generated marshalling needs AllowUnsafeBlocks project-wide.
        private static class NativeWindowChrome {
            [DllImport("/usr/lib/libobjc.dylib")]
            private static extern IntPtr sel_registerName(IntPtr name);

            [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
            private static extern void SendBool(IntPtr receiver, IntPtr selector, bool value);

            internal static void SetDocumentEdited(IntPtr nsWindow, bool edited) {
                IntPtr selector = Marshal.StringToHGlobalAnsi("setDocumentEdited:");
                try {
                    SendBool(nsWindow, sel_registerName(selector), edited);
                } finally {
                    Marshal.FreeHGlobal(selector);
                }
            }
        }

        //⌘F/⌘G/⇧⌘G reach the search field regardless of focus (v2 §02); ⌘. is command-only cancel-in-flight, no LP-solve producer wired yet (§14 ruling 4).
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.F && e.KeyModifiers == PlatformModifiers.Primary) {
                toolbar.SearchField.Focus();
                toolbar.SearchField.SelectAll();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.G && e.KeyModifiers == PlatformModifiers.Primary) {
                StepSearchMatch(forward: true);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.G && e.KeyModifiers == (PlatformModifiers.Primary | KeyModifiers.Shift)) {
                StepSearchMatch(forward: false);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.OemPeriod && e.KeyModifiers == PlatformModifiers.Primary && toolbar.ZoomStepper.IsSolving) {
                toolbar.ZoomStepper.IsSolving = false;
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        //Find node ruling §01: matches each node's drawn name (recipe/item FriendlyName, or "<name> Spoilage"/"<name> Planting") plus KeyNodeTitle on key nodes; type banners, assembler/module names, item tabs and annotations are never in NodeElements, so they're excluded for free.
        internal static string? DrawnName(BaseNodeElement node) => node.ViewModel switch {
            IRecipeNodeViewModel r => r.BaseRecipe.FriendlyName,
            ISupplierNodeViewModel s => s.SuppliedItem.FriendlyName,
            IConsumerNodeViewModel c => c.ConsumedItem.FriendlyName,
            IPassthroughNodeViewModel p => p.PassthroughItem.FriendlyName,
            ISpoilNodeViewModel sp => sp.InputItem.FriendlyName is string n ? n + " Spoilage" : null,
            IPlantNodeViewModel pl => pl.Seed.FriendlyName is string n ? n + " Planting" : null,
            _ => null,
        };

        //Trimmed, substring, culture-aware case-insensitive per §01; an all-whitespace query matches nothing.
        internal static bool MatchesSearchQuery(BaseNodeElement node, string query) {
            string trimmed = query.Trim();
            if (trimmed.Length == 0)
                return false;
            return (DrawnName(node) is string name && name.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase)) ||
                (node.ViewModel.KeyNode && node.ViewModel.KeyNodeTitle.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase));
        }

        //Order (centre Y, centre X, id): a node's own Location is already its centre, so no extra bounds math is needed to rank matches.
        private List<BaseNodeElement> OrderedSearchMatches(string query) =>
            [.. GraphCanvas.Viewer.NodeElements
                .Where(n => MatchesSearchQuery(n, query))
                .OrderBy(n => n.Location.Y).ThenBy(n => n.Location.X).ThenBy(n => n.ViewModel.Id.Value)];

        private static System.Drawing.Rectangle WorldBounds(BaseNodeElement node) =>
            new(node.Location.X - node.Width / 2, node.Location.Y - node.Height / 2, node.Width, node.Height);

        //The first match at or after a graph point, in the same (Y, X) order OrderedSearchMatches ranks by.
        private int FirstSearchMatchIndexAtOrAfter(System.Drawing.Point point) {
            for (int i = 0; i < searchMatches.Count; i++) {
                System.Drawing.Point at = searchMatches[i].Location;
                if (at.Y > point.Y || (at.Y == point.Y && at.X >= point.X))
                    return i;
            }
            return -1;
        }

        //Find node ruling §03 row 1: the current selection, else the viewport centre, else the first match.
        private int AnchorSearchMatchIndex() {
            if (searchMatches.Count == 0)
                return -1;
            if (GraphCanvas.Viewer.SelectedNodes.Count == 1) {
                int atSelection = FirstSearchMatchIndexAtOrAfter(GraphCanvas.Viewer.SelectedNodes.First().Location);
                if (atSelection >= 0)
                    return atSelection;
            }
            System.Drawing.Point centre = GraphCanvas.Viewport.ScreenToGraph(new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2));
            int atCentre = FirstSearchMatchIndexAtOrAfter(centre);
            return atCentre >= 0 ? atCentre : 0;
        }

        private static double FindPanMarginPoints() =>
            Application.Current?.TryGetResource("Foreman.Metrics.FindPanMargin", null, out object? value) == true && value is double d ? d : 80;

        //Find node ruling §02: the current match is the canvas selection, panned into view only if needed, never zoomed.
        private void SelectCurrentSearchMatch() {
            BaseNodeElement match = searchMatches[searchMatchIndex];
            GraphCanvas.Viewer.SetSelection([match]);
            GraphCanvas.PanIntoView(WorldBounds(match), FindPanMarginPoints());
            GraphCanvas.RequestRedraw();
        }

        //Find node ruling §03 rows 1-2: the current match is set the moment the query first produces matches, never a silent reset to 1; a node that is still current after an edit stays current, renumbered.
        private void ResetSearchMatches() {
            string query = toolbar.SearchField.Text ?? "";
            BaseNodeElement? wasCurrent = searchMatchIndex >= 0 && searchMatchIndex < searchMatches.Count ? searchMatches[searchMatchIndex] : null;

            searchMatches = string.IsNullOrWhiteSpace(query) ? [] : OrderedSearchMatches(query);
            int stillCurrent = wasCurrent is null ? -1 : searchMatches.IndexOf(wasCurrent);
            searchMatchIndex = searchMatches.Count == 0 ? -1 : stillCurrent >= 0 ? stillCurrent : AnchorSearchMatchIndex();

            if (searchMatchIndex >= 0)
                SelectCurrentSearchMatch();
            UpdateSearchMatchStatus();
            ScheduleSearchResultAnnouncement(query);
        }

        //Find node ruling §03 "Graph changes mid-find": a node added, deleted or renamed recomputes the run, so the count never outlives its nodes; the index only re-selects when the node it pointed at is gone.
        private void RecomputeSearchMatchesOnGraphChange() {
            string query = toolbar.SearchField.Text ?? "";
            if (string.IsNullOrWhiteSpace(query))
                return;

            BaseNodeElement? wasCurrent = searchMatchIndex >= 0 && searchMatchIndex < searchMatches.Count ? searchMatches[searchMatchIndex] : null;
            int wasIndex = searchMatchIndex;
            searchMatches = OrderedSearchMatches(query);
            int stillCurrent = wasCurrent is null ? -1 : searchMatches.IndexOf(wasCurrent);
            searchMatchIndex = searchMatches.Count == 0 ? -1
                : stillCurrent >= 0 ? stillCurrent
                : wasIndex < 0 || wasIndex >= searchMatches.Count ? 0
                : wasIndex;

            if (searchMatchIndex >= 0 && stillCurrent < 0)
                SelectCurrentSearchMatch();
            UpdateSearchMatchStatus();
        }

        //⌘G/Return and ⇧⌘G/⇧Return wrap both ways silently; the count going from "3 of 3" to "1 of 3" is the only feedback.
        private void StepSearchMatch(bool forward) {
            if (searchMatches.Count == 0)
                return;

            //The step is the answer to the step: the query's pending summary would otherwise land on top of it.
            searchAnnounceTimer?.Stop();
            int next = searchMatchIndex + (forward ? 1 : -1);
            bool wrapped = next < 0 || next >= searchMatches.Count;
            searchMatchIndex = ((next % searchMatches.Count) + searchMatches.Count) % searchMatches.Count;

            SelectCurrentSearchMatch();
            UpdateSearchMatchStatus();
            AnnounceSearchStep(searchMatches[searchMatchIndex], wrapped, forward);
        }

        //Find node ruling §04: the field's only three states are empty, "No matches", and "n of N".
        private void UpdateSearchMatchStatus() {
            string query = toolbar.SearchField.Text ?? "";
            toolbar.SearchField.MatchStatus = string.IsNullOrWhiteSpace(query) ? null
                : searchMatches.Count == 0 ? "No matches"
                : string.Create(DisplayCulture.Format, $"{searchMatchIndex + 1} of {searchMatches.Count}");
        }

        private static string MatchCountLabel(int count) => count == 1 ? "1 match" : string.Create(DisplayCulture.Format, $"{count} matches");

        //Find node ruling §05: DispatcherTimer, not Task.Delay - a delay outliving its window fires on another test's UI thread.
        private static readonly TimeSpan SearchAnnounceDebounce = TimeSpan.FromMilliseconds(300);
        private DispatcherTimer? searchAnnounceTimer;

        private void ScheduleSearchResultAnnouncement(string query) {
            searchAnnounceTimer?.Stop();
            if (string.IsNullOrWhiteSpace(query))
                return;
            searchAnnounceTimer = RestartTimer(searchAnnounceTimer, (_, _) => Announce(searchMatches.Count == 0 ? "No matches" : MatchCountLabel(searchMatches.Count)));
        }

        private static DispatcherTimer RestartTimer(DispatcherTimer? timer, EventHandler onElapsed) {
            if (timer is null) {
                timer = new DispatcherTimer { Interval = SearchAnnounceDebounce };
                timer.Tick += onElapsed;
            }
            timer.Stop();
            timer.Start();
            return timer;
        }

        //Find node ruling §05: the wrap announcement names the direction, the only place wrap is signalled to VoiceOver.
        private void AnnounceSearchStep(BaseNodeElement match, bool wrapped, bool forward) {
            string name = DrawnName(match) ?? match.ViewModel.KeyNodeTitle;
            string text = string.Create(DisplayCulture.Format, $"{searchMatchIndex + 1} of {searchMatches.Count}, {name}");
            Announce(wrapped ? (forward ? "Wrapped to first match. " : "Wrapped to last match. ") + text : text);
        }

        private void Announce(string text) => AutomationProperties.SetName(searchAnnouncer, text);

        //Visually hidden live region for search-result announcements, separate from the field's own accessible name ("Find node").
        private sealed class SearchAnnouncer : TextBlock {
            protected override AutomationPeer OnCreateAutomationPeer() => new AnnouncerAutomationPeer(this);

            private sealed class AnnouncerAutomationPeer(SearchAnnouncer owner) : ControlAutomationPeer(owner) {
                protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
                protected override AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Polite;
            }
        }

        //The canvas has no DynamicResource of its own to pick up a live theme (System mode's OS flip),
        //accent, contrast, or scale change, so it re-seeds here on every AppearanceHub notification.
        public void RefreshTheme() {
            minSizeScale.Apply();
            ApplyCanvasTheme();
            RefreshBrowserIcons();
        }

        //browserIconCache carries no theme tint of its own (same as InspectorView's caches) - clearing it
        //forces the next GetOrBake to rebake fresh. Unlike InspectorView's direct Source reassignment, the
        //browser's cells flow through an ObservableCollection an ItemsControl rebinds asynchronously, so an
        //old bitmap can still be mid-render when the flip lands; only vm.Refresh()'s freshly baked instances
        //replace it, and letting the stale ones fall out of scope (rather than disposing them here) is what
        //keeps a still-attached Image from measuring an already-disposed one. Driven from here, not
        //AddBrowserWindow's own RefreshTheme, since the cache and the browser instance both live on MainWindow.
        private void RefreshBrowserIcons() {
            browserIconCache.Clear();
            if (browser?.DataContext is AddBrowserViewModel vm)
                vm.Refresh(forceRebuild: true);
        }

        //lazy: DataCache/Settings are not ready at construction; PlaceRequested is wired once here
        internal void OpenBrowser(AddRequest request) {
            if (DataCache is not Foreman.DataCaching.DataCache cache || Settings is not AppSettings settings)
                return;

            if (browser is null) {
                var vm = new AddBrowserViewModel(new AddCatalog(cache, settings), cache, settings,
                    (bmp, size) => IconBaker.GetOrBake(browserIconCache, bmp, size),
                    GraphCanvas.Viewer.Graph.DefaultAssemblerQuality,
                    resolve => Dispatcher.UIThread.Post(resolve, DispatcherPriority.Background));
                vm.ImportPresetRequested += (_, _) => ShowSettings(selectPresetsPane: true);
                browser = new AddBrowserWindow { DataContext = vm };
                vm.PlaceRequested += (_, pick) => {
                    if (BrowserRequest is not AddRequest activeRequest)
                        return;
                    //Re-read the vm's link fresh: ClearChip can change it between placements.
                    AddRequest requestForPlacement = activeRequest with { Link = vm.Link };
                    BrowserRequest = GraphCanvas.PlaceCandidate(pick.Candidate, requestForPlacement, pick.Role, pick.Quality, pick.SpoilPlantDirection, pick.ExplicitOrigin);
                };
            }
            var browserVm = (AddBrowserViewModel)browser.DataContext!;
            browserVm.Seed(request);
            if (request.OriginsRole is NodeType role)
                browserVm.SeedOrigins(role);
            BrowserRequest = request;
            if (!browser.IsVisible)
                browser.Show(this);
            browser.Activate();
        }

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

            //C-1 (final fix wave): WindowScaler.SetScale moved here from OpenSettingsAsync's own Confirm
            //tail - this is the same seam (boot, preset reload, every Settings Confirm) the theme seed
            //below already replays through, so a persisted UiScale now takes effect at cold start instead
            //of only after the user opens Settings once.
            WindowScaler.SetScale(settings.UiScale);

            ApplyCanvasTheme();

            //Phase 9 C2a Task 3: seeds the pin the same way UiScale/theme are seeded above - boot, preset
            //reload, and every Settings Confirm all replay through this one seam.
            ViewModel.Inspector.IsPinned = settings.InspectorPinned;
            ViewModel.Inspector.Width = Math.Clamp(settings.InspectorWidth, 260, 380) * WindowScaler.CurrentScale;

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

        //Ports MainForm.SetDarkMode/SetLightMode's cold-start read (upstream MainForm.cs:36-46): App.
        //ApplyTheme only swaps the window chrome's ThemeVariant, so the canvas needs its own seed here -
        //replayed at boot, preset reload, and every Settings Confirm (all via ApplyLoadedSettings above,
        //which calls this directly), and now also on a live OS appearance flip while FlagDarkMode is
        //System (RefreshTheme, this window's AppearanceHub subscription) - GraphCanvas's Skia surface has
        //no DynamicResource of its own to pick that last one up for free.
        private void ApplyCanvasTheme() {
            if (Settings is not AppSettings settings)
                return;

            bool isDark = settings.FlagDarkMode == ThemeMode.Dark ||
                (settings.FlagDarkMode == ThemeMode.System && Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
            GraphCanvas.Viewer.ApplyTheme(isDark);
            GraphCanvas.RequestRedraw();
        }

        //The value/enable resync below replays safely on every call; the checkbox subscriptions are guarded to wire exactly once.
        private bool viewOptionControlsBound;

        private void BindViewOptionControls(AppSettings settings) {
            minorGridlinesPopUp.SelectedItem = GridlineLabels[Math.Clamp(settings.MinorGridlines, 0, GridlineLabels.Length - 1)];
            majorGridlinesPopUp.SelectedItem = GridlineLabels[Math.Clamp(settings.MajorGridlines, 0, GridlineLabels.Length - 1)];
            gridlinesCheckBox.IsChecked = settings.AltGridlines;
            ResyncRateOptionsDropDown();
            iconViewCheckBox.IsChecked = GraphCanvas.Viewer.IconsOnly;
            pauseUpdatesCheckBox.IsChecked = GraphCanvas.Viewer.Graph.PauseUpdates;

            GraphCanvas.Grid.CurrentGridUnit = GridUnitFromIndex(Array.IndexOf(GridlineLabels, minorGridlinesPopUp.SelectedItem));
            GraphCanvas.Grid.CurrentMajorGridUnit = GridUnitFromIndex(Array.IndexOf(GridlineLabels, majorGridlinesPopUp.SelectedItem));
            GraphCanvas.Grid.ShowGrid = gridlinesCheckBox.IsChecked == true;
            ResyncNativeMenuCheckedStates();

            minorGridlinesPopUp.IsEnabled = true;
            majorGridlinesPopUp.IsEnabled = true;
            gridlinesCheckBox.IsEnabled = true;
            rateOptionsPopUp.IsEnabled = true;
            iconViewCheckBox.IsEnabled = true;
            pauseUpdatesCheckBox.IsEnabled = true;

            if (viewOptionControlsBound)
                return;
            viewOptionControlsBound = true;

            gridlinesCheckBox.IsCheckedChanged += (_, _) => {
                GraphCanvas.Grid.ShowGrid = gridlinesCheckBox.IsChecked == true;
                if (Settings is AppSettings current)
                    current.AltGridlines = gridlinesCheckBox.IsChecked == true;
                ResyncNativeMenuCheckedStates();
                GraphCanvas.InvalidateVisual();
            };
            iconViewCheckBox.IsCheckedChanged += (_, _) => {
                GraphCanvas.Viewer.IconsOnly = iconViewCheckBox.IsChecked == true;
                if (Settings is AppSettings current)
                    current.IconsOnlyView = iconViewCheckBox.IsChecked == true;
                ResyncNativeMenuCheckedStates();
                GraphCanvas.InvalidateVisual();
            };
            pauseUpdatesCheckBox.IsCheckedChanged += (_, _) => {
                GraphCanvas.Viewer.Graph.PauseUpdates = pauseUpdatesCheckBox.IsChecked == true;
                if (!GraphCanvas.Viewer.Graph.PauseUpdates)
                    GraphCanvas.Viewer.Graph.UpdateNodeValues();
                ResyncNativeMenuCheckedStates();
                GraphCanvas.InvalidateVisual();
            };
        }

        //Kit.PopUp.Command fires only on a real user commit, never on a SelectedItem write.
        private void OnGridlineCommitted(bool isMinor, string? item) {
            if (item is null)
                return;
            int index = Array.IndexOf(GridlineLabels, item);
            if (isMinor)
                GraphCanvas.Grid.CurrentGridUnit = GridUnitFromIndex(index);
            else
                GraphCanvas.Grid.CurrentMajorGridUnit = GridUnitFromIndex(index);
            if (Settings is AppSettings current) {
                if (isMinor)
                    current.MinorGridlines = index;
                else
                    current.MajorGridlines = index;
            }
            ResyncNativeMenuCheckedStates();
            GraphCanvas.InvalidateVisual();
        }

        private void OnRateOptionsCommitted(string? item) {
            if (item is null)
                return;
            SetRateUnit((ProductionGraph.RateUnit)Array.IndexOf(RateUnitOptions, item));
        }

        //Rate unit is a graph-level setting shared by the inspector's own pop-up, View Options' pop-up and
        //the View menu radios (2026-09-17 building-picker ruling) - one method serves every entry point, so
        //a pick from any of them mirrors into the other two and persists immediately rather than waiting on
        //the window-close flush.
        private void SetRateUnit(ProductionGraph.RateUnit unit) {
            GraphCanvas.Viewer.Graph.SelectedRateUnit = unit;
            GraphCanvas.Viewer.Graph.UpdateNodeValues();
            ResyncRateOptionsDropDown();
            Inspector.RefreshSegment();
            GraphCanvas.InvalidateVisual();
            if (Settings is AppSettings settings)
                SettingsService?.Save(settings);
        }

        //Ports ApplyLoadedGraphUiState's dropdown resync (upstream MainForm.cs:227-231, first thing it does
        //after a load): LoadDocument already applies the save's rate unit to Graph.SelectedRateUnit, but
        //nothing kept RateOptionsDropDown or Settings in step with it. A plain SelectedItem write never
        //invokes Command (only a real user commit does), so this needs no unsubscribe-before-write guard.
        internal void ResyncRateOptionsDropDown() {
            ProductionGraph.RateUnit unit = GraphCanvas.Viewer.Graph.SelectedRateUnit;
            rateOptionsPopUp.SelectedItem = RateUnitOptions[(int)unit];

            if (Settings is AppSettings settings)
                settings.DefaultRateUnit = unit;

            //A plain file Load reaches this method directly (ApplyLoadedGraphUiState), not through
            //BindViewOptionControls, so the Rate Unit submenu's checkmark needs its own resync here too.
            ResyncNativeMenuCheckedStates();
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
                FileTypeFilter = [new FilePickerFileType("Foreman files") { Patterns = ["*.fjson"] }],
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
                await Dialogs.ShowWarningAsync(this, $"Couldn’t open “{Dialogs.DocumentName(path)}”.", UnreadableGraphSentence).ConfigureAwait(true);
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

            //LoadDocument is about to dispose every current node view model.
            ViewModel.Inspector.SetSelection([]);
            GraphLoadResult result = GraphCanvas.LoadDocument(cache, document);
            if (!result.Success)
                await Dialogs.ShowWarningAsync(this, $"Couldn’t open “{Dialogs.DocumentName(path)}”.", result.ErrorMessage ?? UnreadableGraphSentence).ConfigureAwait(true);
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
                await ShowPresetSwitchInfoAsync(string.Create(DisplayCulture.Format,
                    $"Loaded graph uses a different Preset.\nPreset switched from \"{previousPresetName}\" to \"{newPresetName}\"")).ConfigureAwait(true);
                settings.CurrentPresetName = newPresetName;
                settingsService.Save(settings);
            }

            return chosenPreset;
        }

        private Task<Preset?> ShowPresetSelectionDialogAsync(List<PresetErrorPackage> presetErrors) =>
            PresetSelectionDialogStub?.Invoke(presetErrors) ?? RealShowPresetSelectionDialogAsync(presetErrors);

        //Ports the ResolveChosenPresetAsync slow path's dialog launch (upstream ProductionGraphViewer.cs:
        //1477-1488). The chosen preset is read while the sheet is still open, so a failed read shows on its strip.
        private async Task<Preset?> RealShowPresetSelectionDialogAsync(List<PresetErrorPackage> presetErrors) {
            PresetSelectionSheet sheet = NewPresetSelectionSheet(presetErrors);
            bool? result = await SheetHost.For(this).ShowAsync(sheet).ConfigureAwait(true);
            if (!GraphCanvas.IsPanelOpen)
                GraphCanvas.Focus();
            return result == true ? sheet.ChosenPreset : null;
        }

        internal static PresetSelectionSheet NewPresetSelectionSheet(List<PresetErrorPackage> presetErrors) =>
            new(presetErrors) { ReadPresetAsync = CanReadPresetAsync };

        private static Task<bool> CanReadPresetAsync(Preset preset) => Task.Run(() => {
            try {
                Foreman.DataCaching.PresetProcessor.PrepPreset(preset);
                return true;
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Preset selection: couldn't read " + preset.Name);
                return false;
            }
        });

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
            ViewModel.IsDirty = false;
            ResyncZoomLabel();
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

            GraphCanvas.ClosePanels();
            ViewModel.Inspector.SetSelection([]);
            GraphCanvas.Viewer.Graph.ClearGraph();
            savefilePath = null;
            savefileBaselineJson = null;
            ViewModel.IsDirty = false;
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
                FileTypeFilter = [new FilePickerFileType("Foreman files") { Patterns = ["*.fjson"] }],
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
                //Belt-and-braces: ImportNodesFromDocument below re-sets the selection itself right after this.
                ViewModel.Inspector.SetSelection([]);
                GraphCanvas.Viewer.ImportNodesFromDocument(cache, document, origin, applySolverSettings: true);
                GraphCanvas.RequestRedraw();
            } catch (Exception exception) {
                await Dialogs.ShowWarningAsync(this, $"Couldn’t import “{Dialogs.DocumentName(path)}”.",
                    "Foreman couldn’t read it as a graph. The details are in Foreman’s log.").ConfigureAwait(true);
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
                ViewModel.IsDirty = false;
                UpdateTitleBar();
                return true;
            } catch (Exception exception) {
                await Dialogs.ShowWarningAsync(this, $"Couldn’t save “{Dialogs.DocumentName(path)}”.",
                    "Your graph is still open and unchanged. The details are in Foreman’s log.").ConfigureAwait(true);
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

        //Called from New/Load only; a titled graph prompts only when its serialization differs from the save baseline.
        internal async Task<bool> TestGraphSavedStatusAsync() {
            if (DataCache is not Foreman.DataCaching.DataCache cache)
                return true;

            if (savefilePath is null)
                return !GraphCanvas.Viewer.Graph.Nodes.Any()
                    || await ShowDiscardUnsavedGraphConfirmAsync("This graph has never been saved. You can’t undo this action.").ConfigureAwait(true);

            if (!File.Exists(savefilePath))
                return await ShowDiscardUnsavedGraphConfirmAsync(
                    $"Its file “{Dialogs.DocumentName(savefilePath)}” is no longer where it was saved. You can’t undo this action.").ConfigureAwait(true);

            GraphCanvas.Viewer.Graph.SerializeNodeIdSet = null;
            string currentSaveJson = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, cache), writeIndented: true);
            string savedJson = savefileBaselineJson ?? Utf8File.ReadAllText(savefilePath);

            if (savedJson != currentSaveJson) {
                ConfirmChoice choice = await ShowSaveBeforeContinuingChoiceAsync(savefilePath).ConfigureAwait(true);
                if (choice == ConfirmChoice.Cancel)
                    return false;
                if (choice == ConfirmChoice.Yes)
                    await SaveGraphAsync(savefilePath).ConfigureAwait(true);
            }
            return true;
        }

        private const string UnreadableGraphSentence =
            "The file is too old or damaged to read. Open it in the previous Foreman release, save it again, then open the new copy here.";

        private Task<bool> ShowDiscardUnsavedGraphConfirmAsync(string message) =>
            DiscardUnsavedGraphConfirmStub?.Invoke() ?? Dialogs.ShowDestructiveConfirmAsync(this, "Discard this unsaved graph?", message, "Discard");

        private Task<ConfirmChoice> ShowSaveBeforeContinuingChoiceAsync(string path) =>
            SaveBeforeContinuingChoiceStub?.Invoke() ?? Dialogs.ShowSaveChangesAsync(this, path);

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

        //Ports the title-bar update used identically after Save/Load/New/preset-reload (reference: upstream
        //MainForm.cs:188,240,263,444) - the preset no longer appears in the title (contract #toolbar), just
        //the document name, computed in MainWindowViewModel.DocumentTitle.
        private void UpdateTitleBar() {
            ViewModel.CurrentFileName = savefilePath;
        }

        //Ports SettingsButton_Click's post-close cascade (reference upstream MainForm.cs:355-440): builds
        //Options from live state, shows the dialog, then on OK applies a preset switch/reload before always
        //persisting settings.json - matching upstream's presetReloaded branch and its unconditional
        //Properties.Settings.Default.Save() at the end of ApplySettingsDialogChanges. Tasks 2/3 extend the
        //non-preset-reload branch as their tabs' widgets gain something to apply.
        internal void ShowSettings(bool selectPresetsPane = false) {
            //Toolbar/native-menu clicks never reach GraphCanvasControl.OnPointerPressed's own click-outside-
            //closes-panel guard (they're not canvas pointer events at all), so a panel left open under this
            //window stayed stale once it closed. Closes first, matching NewGraphAsync/LoadDocument's own
            //unconditional-close-on-major-UI-change precedent.
            GraphCanvas.ClosePanels();
            ViewModel.Inspector.SetSelection([]);

            //R5/⌘, - one instance for the app's lifetime; a second invocation just brings it forward.
            if (settingsWindow is { } existing) {
                if (selectPresetsPane)
                    existing.SelectPresetsPane();
                if (SettingsDialogStub is null)
                    existing.Show(this);
                existing.Activate();
                return;
            }

            if (DataCache is not Foreman.DataCaching.DataCache cache
                || Settings is not AppSettings settings
                || PresetResolver is not Services.PresetResolver presetResolver
                || SettingsService is not Services.SettingsService settingsService)
                return;

            var options = new SettingsWindow.SettingsWindowOptions(cache) {
                Presets = presetResolver.BuildPresetList(settings.CurrentPresetName),
            };
            options.SelectedPreset = options.Presets.Count > 0 ? options.Presets[0] : null;

            //Ports MainForm.cs:402-406: seeds the Enabled Objects tab's baseline from the live cache's
            //current membership, since ShowLoadFromSaveAsync/ShowAssignFromSciencePacksAsync hand this set
            //to their sheets as "currently enabled" (the tab's own checkboxes read dataObject.Enabled
            //directly and don't need this, but those two sheets still diff against a starting set).
            options.EnabledObjects.UnionWith(cache.Recipes.Values.Where(r => r.Enabled));
            options.EnabledObjects.UnionWith(cache.Assemblers.Values.Where(r => r.Enabled));
            options.EnabledObjects.UnionWith(cache.Beacons.Values.Where(r => r.Enabled));
            options.EnabledObjects.UnionWith(cache.Modules.Values.Where(r => r.Enabled));
            options.EnabledObjects.UnionWith(cache.Qualities.Values.Where(r => r.Enabled));

            var window = new SettingsWindow(options) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };
            window.SettingsService = settingsService;
            window.Settings = settings;
            //Every General/Advanced/Annotations/Appearance field is AppSettings-backed already (unlike the
            //old Options DTO, nothing here has a separate "live GraphViewer state" to seed from - settings
            //itself stays authoritative since this window is the only thing that ever changes these fields).
            window.ViewModel.LoadFrom(settings);

            var session = new SettingsSession(settings, settingsService, (Application.Current as App)!,
                resolve: ReSolveAfterSettingsChangeAsync,
                applyTheme: mode => {
                    (Application.Current as App)?.ApplyTheme(mode);
                    return Task.CompletedTask;
                },
                applyScale: WindowScaler.SetScale);
            window.Session = session;

            window.ViewModel.SettingChanged += name => OnSettingsChanged(window, settings, name);
            window.ReloadRequested += () => Async.Fire(HandlePresetReloadAsync(window, options), nameof(HandlePresetReloadAsync));
            window.Closed += (_, _) => {
                if (ReferenceEquals(settingsWindow, window)) {
                    settingsWindow = null;
                    settingsSession = null;
                }
                //See FloatingPanelHost's own focus-restore comment further down this file.
                if (!GraphCanvas.IsPanelOpen)
                    GraphCanvas.Focus();
            };

            settingsWindow = window;
            settingsSession = session;

            if (SettingsDialogStub is { } stub)
                stub(window);
            else
                window.Show(this);
        }

        //Session.Apply runs before ApplyLoadedSettings: it applies the theme that ApplyLoadedSettings reads
        private void OnSettingsChanged(SettingsWindow window, AppSettings settings, string propertyName) {
            window.ViewModel.ApplyTo(settings);

            if (propertyName == nameof(ViewModels.SettingsViewModel.LoadBarrelingChecked)) {
                window.RequestReload();
                return;
            }

            settingsSession?.Apply(propertyName);
            ApplyLoadedSettings();
        }

        //Ports MainForm.cs:501-503's apply-tail: a re-solve must actually run against the settings just
        //committed, not just write them onto AppSettings/the live graph properties.
        private Task ReSolveAfterSettingsChangeAsync() {
            GraphCanvas.Viewer.Graph.UpdateNodeMaxQualities();
            GraphCanvas.Viewer.Graph.UpdateNodeStates(true);
            GraphCanvas.Viewer.Graph.UpdateNodeValues();
            GraphCanvas.InvalidateVisual();
            return Task.CompletedTask;
        }

        //Ports MainForm.cs:505-506's reopen tail: closes the one-off Settings instance a preset switch (or
        //a barreling/crating filter change) needs, reloads, then reopens a fresh instance so the person sees
        //a dialog reflecting whatever that reload changed.
        private async Task HandlePresetReloadAsync(SettingsWindow window, SettingsWindow.SettingsWindowOptions options) {
            window.Close();

            if (Settings is not AppSettings settings)
                return;

            if (options.SelectedPreset is Preset selectedPreset)
                await ReloadForPresetAsync(selectedPreset, settings).ConfigureAwait(true);

            SettingsService?.Save(settings);
            ShowSettings();
        }

        //Ports GraphSummaryButton_Click (reference upstream MainForm.cs:564-569): always the whole session,
        //matching MainForm's own call (the node-subset constructor overload has no live caller upstream
        //either). C2c Task 3 moved this off GraphSummaryWindow onto SheetHost.
        internal Task OpenGraphSummaryAsync() {
            //See OpenSettingsAsync's own close-first comment: same toolbar-bypasses-canvas-pointer-events gap.
            GraphCanvas.ClosePanels();

            if (GraphSummaryDialogStub is Action<IProductionGraphSession> stub) {
                stub(GraphCanvas.Viewer.Session);
                return Task.CompletedTask;
            }
            return RealShowGraphSummaryAsync();
        }

        private async Task RealShowGraphSummaryAsync() {
            var sheet = new GraphSummarySheet(GraphCanvas.Viewer.Session, GraphCanvas.Viewer.Graph.GetRateName());
            await SheetHost.For(this).ShowAsync(sheet).ConfigureAwait(true);
            //See RealShowSettingsDialogAsync's own focus-restore comment, including the open-panel guard.
            if (!GraphCanvas.IsPanelOpen)
                GraphCanvas.Focus();
        }

        //Ports ExportImageButton_Click (reference upstream MainForm.cs:508-514): owner+50/+50, same Manual
        //placement as Settings/Graph Summary.
        internal Task OpenImageExportAsync() {
            //See OpenSettingsAsync's own close-first comment: same toolbar-bypasses-canvas-pointer-events gap.
            GraphCanvas.ClosePanels();

            if (ImageExportDialogStub is Action<GraphViewer> stub) {
                stub(GraphCanvas.Viewer);
                return Task.CompletedTask;
            }
            return RealShowImageExportAsync();
        }

        private async Task RealShowImageExportAsync() {
            await SheetHost.For(this).ShowAsync(new ImageExportSheet(GraphCanvas.Viewer)).ConfigureAwait(true);
            if (!GraphCanvas.IsPanelOpen)
                GraphCanvas.Focus();
        }

        //Foreman 2 > About Foreman 2 (contract #menubar): the ratified About panel (design doc .about-card),
        //carrying ViewModel.VersionLabel (== AppVersion.VersionedDisplay). I-4 (final fix wave): the
        //titlebar's own version TextBlock is gone, so this panel is now the label's only reader.
        private void ShowAbout() => Async.Fire(ShowAboutAsync(), nameof(ShowAboutAsync));

        internal Task ShowAboutAsync() {
            if (AboutDialogStub is Action stub) {
                stub();
                return Task.CompletedTask;
            }
            return RealShowAboutAsync();
        }

        private async Task RealShowAboutAsync() {
            var dialog = new AboutWindow(ViewModel.VersionLabel);
            await dialog.ShowDialog(this).ConfigureAwait(true);
            if (!GraphCanvas.IsPanelOpen)
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
                await Dialogs.ShowWarningAsync(this, $"Couldn’t open “{Dialogs.MiddleTruncate(ViewModel.DocumentTitle)}”.", result.ErrorMessage ?? UnreadableGraphSentence).ConfigureAwait(true);

            ApplyLoadedSettings();
            UpdateTitleBar();
            GraphCanvas.RequestRedraw();
        }

        //Ports GraphViewer_KeyDown's Space handler (reference §7): a separate subscription on the canvas
        //control's own KeyDown event, alongside (not inside) GraphCanvasControl's own OnKeyDown override -
        //mirrors upstream attaching this as MainForm's own delegate rather than folding it into
        //ProductionGraphViewer_KeyDown itself, since it needs to reach the toolbar's gridlines checkbox.
        private void OnGraphCanvasKeyDown(object? sender, KeyEventArgs e) {
            //Gate fix 8: GraphCanvasControl's own OnKeyDown no longer marks a panel-bound key Handled (that
            //was what broke live typing into the search box), so Space typed there now reaches this
            //subscription too - this guard is what keeps it from toggling gridlines while the panel is open.
            if (GraphCanvas.IsPanelOpen)
                return;
            if (e.Key != Key.Space)
                return;

            GraphCanvas.Grid.ShowGrid = !GraphCanvas.Grid.ShowGrid;
            gridlinesCheckBox.IsChecked = GraphCanvas.Grid.ShowGrid;
            GraphCanvas.InvalidateVisual();
            e.Handled = true;
        }

        //Test-only seam: counts genuine BeginMoveDrag requests raised by OnTitleBarPointerPressed below, so
        //a headless test can prove the empty titlebar background is draggable while a toolbar button press
        //(which the Button itself handles first) never reaches this handler at all.
        internal int TitleBarDragRequestCount { get; private set; }

        //Ports macOS's native "drag empty titlebar to move, double-click to zoom" behavior onto the unified
        //titlebar toolbar (macos-design window conventions): ExtendClientAreaToDecorationsHint hands us the
        //whole row as ordinary content, so both gestures need an explicit handler here. Buttons placed on
        //top of this Grid already mark PointerPressed handled before it bubbles here, so only clicks on the
        //empty background between/around them ever reach this method.
        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (e.ClickCount == 2) {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }

            TitleBarDragRequestCount++;
            BeginMoveDrag(e);
        }

        //Live references to the built radio/checkbox NativeMenuItems, kept so ResyncNativeMenuCheckedStates
        //below can update their IsChecked after BuildNativeMenu's own one-time snapshot (taken before Settings
        //is even assigned - see ApplyLoadedSettings' own boot-ordering comment).
        private NativeMenuItem showGridlinesMenuItem = null!;
        private NativeMenuItem iconViewMenuItem = null!;
        private NativeMenuItem pauseCalculationsMenuItem = null!;
        private NativeMenuItem showInspectorMenuItem = null!;
        private NativeMenuItem pinInspectorMenuItem = null!;
        private NativeMenuItem[] minorGridlinesMenuItems = [];
        private NativeMenuItem[] majorGridlinesMenuItems = [];
        private NativeMenuItem[] rateUnitMenuItems = [];

        //Keeps the View menu's checkmarks/radio selections in step with the popover controls they mirror -
        //called from BindViewOptionControls' own resync tail (boot, preset switch, every Settings Confirm),
        //ResyncRateOptionsDropDown (the plain-file-Load path that bypasses OnRateOptionsCommitted by design),
        //and each of the popover's own commit/IsCheckedChanged handlers (a popover- or Space-driven change),
        //so no trigger source ever leaves the menu stale.
        private void ResyncNativeMenuCheckedStates() {
            showGridlinesMenuItem.IsChecked = gridlinesCheckBox.IsChecked == true;
            iconViewMenuItem.IsChecked = iconViewCheckBox.IsChecked == true;
            pauseCalculationsMenuItem.IsChecked = pauseUpdatesCheckBox.IsChecked == true;
            showInspectorMenuItem.IsChecked = ViewModel.Inspector.ShowOverride;
            pinInspectorMenuItem.IsChecked = ViewModel.Inspector.IsPinned;
            ResyncRadioChecks(minorGridlinesMenuItems, Array.IndexOf(GridlineLabels, minorGridlinesPopUp.SelectedItem));
            ResyncRadioChecks(majorGridlinesMenuItems, Array.IndexOf(GridlineLabels, majorGridlinesPopUp.SelectedItem));
            ResyncRadioChecks(rateUnitMenuItems, Array.IndexOf(RateUnitOptions, rateOptionsPopUp.SelectedItem));
        }

        private static void ResyncRadioChecks(NativeMenuItem[] items, int selectedIndex) {
            for (int i = 0; i < items.Length; i++)
                items[i].IsChecked = i == selectedIndex;
        }

        //Native menu action handlers (contract #menubar), plain methods rather than ShellViewModel commands since none of them is toolbar-shared.
        private void MenuCut() { GraphCanvas.CutSelection(); GraphCanvas.RequestRedraw(); }

        private void MenuCopy() => GraphCanvas.CopySelection();

        private void MenuPaste() { GraphCanvas.PasteFromClipboard(); GraphCanvas.RequestRedraw(); }

        private void MenuDelete() { GraphCanvas.DeleteSelection(); GraphCanvas.RequestRedraw(); }

        private void MenuZoomIn() {
            var center = new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2);
            GraphCanvas.Viewport.ZoomAt(center, zoomIn: true, GraphCanvas.Viewer.Graph.Bounds);
            ResyncZoomLabel();
            GraphCanvas.RequestRedraw();
        }

        private void MenuZoomOut() {
            var center = new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2);
            GraphCanvas.Viewport.ZoomAt(center, zoomIn: false, GraphCanvas.Viewer.Graph.Bounds);
            ResyncZoomLabel();
            GraphCanvas.RequestRedraw();
        }

        private void MenuActualSize() {
            var center = new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2);
            GraphCanvas.Viewport.SetAbsoluteZoom(center, 1f, GraphCanvas.Viewer.Graph.Bounds);
            ResyncZoomLabel();
            GraphCanvas.RequestRedraw();
        }

        private void MenuFitToGraph() {
            GraphCanvas.Viewport.FitToBounds(GraphCanvas.Viewer.Graph.Bounds, GraphCanvas.Viewer.Graph.Bounds);
            ResyncZoomLabel();
            GraphCanvas.RequestRedraw();
        }

        //Viewport.ViewScale isn't bindable, so every zoom entry point resyncs the stepper's readout by calling this.
        private void ResyncZoomLabel() =>
            toolbar.ZoomStepper.Value = Math.Round(GraphCanvas.Viewport.ViewScale * 100);

        //Mirrors OnGraphCanvasKeyDown's own Space handler (reference §7): mutates Grid/Viewer/Settings and the
        //popover control's displayed value directly, rather than only flipping the control and trusting
        //BindViewOptionControls' IsCheckedChanged handler to carry it the rest of the way. ShellBootstrapper
        //shows MainWindow (and its NativeMenu) before ApplyLoadedSettings ever runs (preset resolution and
        //data-cache loading happen in between), so a menu click has the same live-before-bound window Space
        //already had to handle - if BindViewOptionControls' handler is already subscribed by the time this
        //runs, its re-write of the same values is redundant but harmless.
        private void MenuToggleShowGridlines() {
            GraphCanvas.Grid.ShowGrid = !GraphCanvas.Grid.ShowGrid;
            gridlinesCheckBox.IsChecked = GraphCanvas.Grid.ShowGrid;
            if (Settings is AppSettings settings)
                settings.AltGridlines = GraphCanvas.Grid.ShowGrid;
            ResyncNativeMenuCheckedStates();
            GraphCanvas.InvalidateVisual();
        }

        private void MenuToggleIconView() {
            GraphCanvas.Viewer.IconsOnly = !GraphCanvas.Viewer.IconsOnly;
            iconViewCheckBox.IsChecked = GraphCanvas.Viewer.IconsOnly;
            if (Settings is AppSettings settings)
                settings.IconsOnlyView = GraphCanvas.Viewer.IconsOnly;
            ResyncNativeMenuCheckedStates();
            GraphCanvas.InvalidateVisual();
        }

        private void MenuTogglePauseCalculations() {
            GraphCanvas.Viewer.Graph.PauseUpdates = !GraphCanvas.Viewer.Graph.PauseUpdates;
            if (!GraphCanvas.Viewer.Graph.PauseUpdates)
                GraphCanvas.Viewer.Graph.UpdateNodeValues();
            pauseUpdatesCheckBox.IsChecked = GraphCanvas.Viewer.Graph.PauseUpdates;
            ResyncNativeMenuCheckedStates();
            GraphCanvas.InvalidateVisual();
        }

        //Show Inspector overrides visibility for the current session only - it never touches the pin,
        //so it reverts to whatever IsPinned/selection already implied the next time both are false.
        private void MenuToggleShowInspector() {
            ViewModel.Inspector.ShowOverride = !ViewModel.Inspector.ShowOverride;
            ResyncNativeMenuCheckedStates();
        }

        //Pin Inspector persists through the same settings-save path Settings Confirm uses for UiScale
        //(settingsService.Save below) - there's no dialog step for this one, so the toggle itself writes.
        private void MenuTogglePinInspector() {
            ViewModel.Inspector.IsPinned = !ViewModel.Inspector.IsPinned;
            if (Settings is AppSettings settings) {
                settings.InspectorPinned = ViewModel.Inspector.IsPinned;
                if (SettingsService is Services.SettingsService settingsService)
                    settingsService.Save(settings);
            }
            ResyncNativeMenuCheckedStates();
        }

        private void MenuSetMinorGridlines(int index) {
            minorGridlinesPopUp.SelectedItem = GridlineLabels[index];
            GraphCanvas.Grid.CurrentGridUnit = GridUnitFromIndex(index);
            if (Settings is AppSettings settings)
                settings.MinorGridlines = index;
            ResyncNativeMenuCheckedStates();
            GraphCanvas.InvalidateVisual();
        }

        private void MenuSetMajorGridlines(int index) {
            majorGridlinesPopUp.SelectedItem = GridlineLabels[index];
            GraphCanvas.Grid.CurrentMajorGridUnit = GridUnitFromIndex(index);
            if (Settings is AppSettings settings)
                settings.MajorGridlines = index;
            ResyncNativeMenuCheckedStates();
            GraphCanvas.InvalidateVisual();
        }

        private void MenuSetRateUnit(int index) => SetRateUnit((ProductionGraph.RateUnit)index);

        //Single-window app: hiding the main window is the whole of what "hide this app" means here. Hide
        //Others/Show All would need to enumerate and retarget other applications' windows, which has no
        //public Avalonia API and no native-interop layer anywhere in this codebase - see the task report.
        private void MenuHide() => Hide();

        private void MenuMinimize() => WindowState = WindowState.Minimized;

        private void MenuZoomWindow() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void MenuBringAllToFront() => Activate();

        //Also seeds the popover's MinorGridlinesDropDown/MajorGridlinesDropDown pop-ups - one list either way.
        internal static readonly string[] GridlineLabels = ["None", "1", "2", "4", "8", "16", "32", "64", "128", "256", "512", "1024"];
        private static readonly string[] RateUnitLabels = ["1 sec", "1 min", "5 min", "10 min", "30 min", "1 hour"];
        //v2 §06.1's own popover copy (A22): the long-form values, distinct from the View menu's abbreviated RateUnitLabels above.
        internal static readonly string[] RateUnitOptions = ["per second", "per minute", "per 5 minutes", "per 10 minutes", "per 30 minutes", "per hour"];

        //A (contract #menubar): lives on Application.Current, not the window - see App.Initialize's call site.
        internal static NativeMenu BuildAppMenu() =>
            new() {
                Items = {
                    WindowActionItem("About Foreman 2", w => w.ShowAbout()),
                    new NativeMenuItemSeparator(),
                    WindowActionItem("Settings", w => w.Commands.SettingsCommand.Execute(null), new KeyGesture(Key.OemComma, PlatformModifiers.Primary)),
                    new NativeMenuItemSeparator(),
                    WindowActionItem("Hide Foreman 2", w => w.Hide(), new KeyGesture(Key.H, PlatformModifiers.Primary)),
                    DisabledItem("Hide Others", new KeyGesture(Key.H, PlatformModifiers.Primary | KeyModifiers.Alt)),
                    DisabledItem("Show All"),
                    new NativeMenuItemSeparator(),
                    QuitMenuItem(),
                },
            };

        //Built before any MainWindow exists (App.Initialize), so unlike ActionItem/MenuItem below this
        //resolves ActiveWindow at click time instead of closing over `this`; a no-op if no window is open.
        private static NativeMenuItem WindowActionItem(string header, Action<MainWindow> action, KeyGesture? gesture = null) =>
            new(header) { Command = new DeferredMenuCommand(new RelayCommand(() => { if (ActiveWindow is MainWindow w) action(w); })), Gesture = gesture };

        private NativeMenu BuildNativeMenu() {
            var fileMenu = new NativeMenu {
                Items = {
                    MenuItem("New", Commands.NewCommand, new KeyGesture(Key.N, PlatformModifiers.Primary)),
                    MenuItem("Open", Commands.LoadCommand, new KeyGesture(Key.O, PlatformModifiers.Primary)),
                    MenuItem("Save", Commands.SaveCommand, new KeyGesture(Key.S, PlatformModifiers.Primary)),
                    MenuItem("Save As", Commands.SaveAsCommand, new KeyGesture(Key.S, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    new NativeMenuItemSeparator(),
                    MenuItem("Import", Commands.ImportCommand),
                    MenuItem("Export Image", Commands.ExportImageCommand, new KeyGesture(Key.E, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    new NativeMenuItemSeparator(),
                    ActionItem("Close", Close, new KeyGesture(Key.W, PlatformModifiers.Primary)),
                },
            };

            //Cut/Copy/Paste/Delete are Command-bound but gesture-less by design (contract #menubar conflicts
            //1/3): GraphCanvasControl.OnKeyDown/OnKeyUp already own Cmd+C/X/V/Delete, so registering the same
            //KeyGesture here would risk the native menu's key equivalent stealing the event, or firing twice.
            var editMenu = new NativeMenu {
                Items = {
                    DisabledItem("Undo", new KeyGesture(Key.Z, PlatformModifiers.Primary)),
                    DisabledItem("Redo", new KeyGesture(Key.Z, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    new NativeMenuItemSeparator(),
                    ActionItem("Cut", MenuCut),
                    ActionItem("Copy", MenuCopy),
                    ActionItem("Paste", MenuPaste),
                    new NativeMenuItemSeparator(),
                    ActionItem("Delete", MenuDelete),
                },
            };

            minorGridlinesMenuItems = RadioItems(GridlineLabels, MenuSetMinorGridlines, Array.IndexOf(GridlineLabels, minorGridlinesPopUp.SelectedItem));
            majorGridlinesMenuItems = RadioItems(GridlineLabels, MenuSetMajorGridlines, Array.IndexOf(GridlineLabels, majorGridlinesPopUp.SelectedItem));
            rateUnitMenuItems = RadioItems(RateUnitLabels, MenuSetRateUnit, Array.IndexOf(RateUnitOptions, rateOptionsPopUp.SelectedItem));

            //⌥⌘G, not upstream's bare Space (contract #menubar conflict 2): a global NativeMenu gesture on an
            //unmodified key would fire even while a text field has focus, swallowing every space typed there.
            showGridlinesMenuItem = new NativeMenuItem("Show Gridlines") {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = gridlinesCheckBox.IsChecked == true,
                Gesture = new KeyGesture(Key.G, PlatformModifiers.Primary | KeyModifiers.Alt),
                Command = new DeferredMenuCommand(new RelayCommand(MenuToggleShowGridlines)),
            };
            iconViewMenuItem = new NativeMenuItem("Icon View") {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = iconViewCheckBox.IsChecked == true,
                Command = new DeferredMenuCommand(new RelayCommand(MenuToggleIconView)),
            };
            pauseCalculationsMenuItem = new NativeMenuItem("Pause All Calculations") {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = pauseUpdatesCheckBox.IsChecked == true,
                Gesture = new KeyGesture(Key.P, PlatformModifiers.Primary | KeyModifiers.Alt),
                Command = new DeferredMenuCommand(new RelayCommand(MenuTogglePauseCalculations)),
            };
            showInspectorMenuItem = new NativeMenuItem("Show Inspector") {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = ViewModel.Inspector.ShowOverride,
                Gesture = new KeyGesture(Key.I, PlatformModifiers.Primary | KeyModifiers.Alt),
                Command = new DeferredMenuCommand(new RelayCommand(MenuToggleShowInspector)),
            };
            pinInspectorMenuItem = new NativeMenuItem("Pin Inspector") {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = ViewModel.Inspector.IsPinned,
                Gesture = new KeyGesture(Key.I, PlatformModifiers.Primary | KeyModifiers.Alt | KeyModifiers.Shift),
                Command = new DeferredMenuCommand(new RelayCommand(MenuTogglePinInspector)),
            };

            var viewMenu = new NativeMenu {
                Items = {
                    MenuItem("Zoom In", ZoomInCommand, new KeyGesture(Key.OemPlus, PlatformModifiers.Primary)),
                    MenuItem("Zoom Out", ZoomOutCommand, new KeyGesture(Key.OemMinus, PlatformModifiers.Primary)),
                    ActionItem("Actual Size", MenuActualSize, new KeyGesture(Key.D0, PlatformModifiers.Primary)),
                    ActionItem("Fit to Graph", MenuFitToGraph, new KeyGesture(Key.D0, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    new NativeMenuItemSeparator(),
                    showGridlinesMenuItem,
                    new NativeMenuItem("Minor Gridlines") { Menu = SubmenuFrom(minorGridlinesMenuItems) },
                    new NativeMenuItem("Major Gridlines") { Menu = SubmenuFrom(majorGridlinesMenuItems) },
                    new NativeMenuItemSeparator(),
                    iconViewMenuItem,
                    pauseCalculationsMenuItem,
                    new NativeMenuItem("Rate Unit") { Menu = SubmenuFrom(rateUnitMenuItems) },
                    new NativeMenuItemSeparator(),
                    showInspectorMenuItem,
                    pinInspectorMenuItem,
                    new NativeMenuItemSeparator(),
                    DisabledItem("Level of Detail"),
                },
            };

            var windowMenu = new NativeMenu {
                Items = {
                    ActionItem("Minimize", MenuMinimize, new KeyGesture(Key.M, PlatformModifiers.Primary)),
                    ActionItem("Zoom", MenuZoomWindow),
                    new NativeMenuItemSeparator(),
                    ActionItem("Bring All to Front", MenuBringAllToFront),
                },
            };

            var helpMenu = new NativeMenu { Items = { MenuItem("Help / Git repo", Commands.HelpCommand) } };

            return new NativeMenu {
                Items = {
                    new NativeMenuItem("File") { Menu = fileMenu },
                    new NativeMenuItem("Edit") { Menu = editMenu },
                    new NativeMenuItem("View") { Menu = viewMenu },
                    new NativeMenuItem("Window") { Menu = windowMenu },
                    new NativeMenuItem("Help") { Menu = helpMenu },
                },
            };
        }

        private static NativeMenuItem MenuItem(string header, IRelayCommand command, KeyGesture? gesture = null) =>
            new(header) { Command = new DeferredMenuCommand(command), IsEnabled = command.CanExecute(null), Gesture = gesture };

        private static NativeMenuItem ActionItem(string header, Action action, KeyGesture? gesture = null) =>
            new(header) { Command = new DeferredMenuCommand(new RelayCommand(action)), Gesture = gesture };

        private static NativeMenuItem DisabledItem(string header, KeyGesture? gesture = null) =>
            new(header) { IsEnabled = false, Gesture = gesture };

        //Builds one radio-toggle NativeMenuItem per label (contract #menubar's Minor/Major Gridlines and Rate
        //Unit submenus): each item carries its own index as CommandParameter rather than a closure per item,
        //so RadioItems can share one RelayCommand<int> across the whole group.
        private static NativeMenuItem[] RadioItems(string[] labels, Action<int> onSelect, int currentIndex) {
            var command = new DeferredMenuCommand(new RelayCommand<int>(onSelect));
            var items = new NativeMenuItem[labels.Length];
            for (int i = 0; i < labels.Length; i++) {
                items[i] = new NativeMenuItem(labels[i]) {
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = i == currentIndex,
                    //B: CommandParameter must be set before Command, or IsEnabled snapshots CanExecute(null).
                    CommandParameter = i,
                    Command = command,
                };
            }
            return items;
        }

        private static NativeMenu SubmenuFrom(NativeMenuItem[] items) {
            var menu = new NativeMenu();
            foreach (NativeMenuItem item in items)
                menu.Items.Add(item);
            return menu;
        }

        //M-3 (final fix wave): TryShutdown, not Shutdown - Shutdown tears every window down unconditionally,
        //bypassing MainWindow.OnClosing's own unsaved-changes gate the same way ⌘W's Close command doesn't.
        //TryShutdown closes each open window first (raising the ordinary Closing event on every one of
        //them), so a dirty graph still prompts on ⌘Q exactly as it does on ⌘W or the traffic-light close.
        private static NativeMenuItem QuitMenuItem() {
            var command = new RelayCommand(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown());
            return new NativeMenuItem("Quit Foreman 2") {
                Gesture = new KeyGesture(Key.Q, PlatformModifiers.Primary),
                Command = new DeferredMenuCommand(command),
            };
        }

        //C1: defers Execute past AppKit's synchronous menu dispatch, so an async sheet doesn't strand the menu open.
        internal sealed class DeferredMenuCommand(ICommand inner) : ICommand {
            internal ICommand Inner { get; } = inner;

            public event EventHandler? CanExecuteChanged {
                add => Inner.CanExecuteChanged += value;
                remove => Inner.CanExecuteChanged -= value;
            }

            public bool CanExecute(object? parameter) => Inner.CanExecute(parameter);

            public void Execute(object? parameter) => Dispatcher.UIThread.Post(() => Inner.Execute(parameter), DispatcherPriority.Background);
        }
    }
}
