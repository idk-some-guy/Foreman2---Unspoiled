using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Storage;
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
using Foreman.Mac.Views.Research;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using Foreman.Models.Research;
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

        public IRelayCommand ZoomInCommand { get; }
        public IRelayCommand ZoomOutCommand { get; }
        public IRelayCommand ActualSizeCommand { get; }
        public ContentControl GraphViewerHost { get; }
        public GraphCanvasControl GraphCanvas { get; }
        public InspectorView Inspector { get; }
        public Foreman.DataCaching.DataCache? DataCache { get; set; }
        public AppSettings? Settings {
            get => settings;
            set {
                settings = value;
                if (value is not null)
                    SetShowToolbar(value.ShowToolbar);
            }
        }
        public PresetResolver? PresetResolver { get; set; }
        public SettingsService? SettingsService { get; set; }

        private readonly WindowMinSizeScale minSizeScale;
        private readonly ToolbarRow toolbar;
        private readonly TextBlock titleBarTitle;
        private AppSettings? settings;
        private bool showToolbar = true;
        private bool searchRevealsToolbar;
        private NativeMenuItem? showToolbarMenuItem;
        private NativeMenuItem? roundedRoutingMenuItem;
        private NativeMenuItem? orthogonalRoutingMenuItem;
        private NativeMenuItem? clearWaypointsMenuItem;
        private RelayCommand presetComparatorCommand = null!;
        private RelayCommand selectNextNodeCommand = null!;
        private RelayCommand selectPreviousNodeCommand = null!;
        private NativeMenuItem selectNextNodeItem = null!;
        private NativeMenuItem selectPreviousNodeItem = null!;
        private readonly PopoverHost contentPopoverHost;
        private ToolbarItem viewOptionsItem = null!;
        private ToolbarItem saveItem = null!;
        private ToolbarItem syncResearchItem = null!;
        private NativeMenuItem syncResearchMenuItem = null!;
        private NativeMenuItem syncResearchFromAnotherMenuItem = null!;
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

        internal ToolbarRow Toolbar => toolbar;
        internal bool DocumentEdited => documentEdited;

        private AddBrowserWindow? browser;
        internal AddBrowserWindow? BrowserWindow => browser;
        internal AddRequest? BrowserRequest { get; private set; }
        private readonly Dictionary<(SKBitmap, int), Bitmap> browserIconCache = [];

        internal Action<SettingsWindow>? SettingsDialogStub { get; set; }

        private SettingsWindow? settingsWindow;
        private SettingsSession? settingsSession;

        internal SettingsWindow? CurrentSettingsWindow => settingsWindow;
        internal SettingsSession? CurrentSettingsSession => settingsSession;

        internal ResearchSyncFlow ResearchSync { get; }

        private GraphSummaryWindow? graphSummaryWindow;
        internal GraphSummaryWindow? CurrentGraphSummaryWindow => graphSummaryWindow;

        //Test-only seam: lets a test observe/short-circuit the Image Export dialog without a real modal
        //ShowDialog.
        internal Action<GraphViewer>? ImageExportDialogStub { get; set; }

        internal Func<string, Task<string?>>? ExportFilePathStub { get; set; }

        internal Func<Task<(string Json, string Path)?>>? OpenFileStub { get; set; }

        //Test-only seam: lets a test supply the cross-preset PresetSelectionWindow's outcome (io-reference.md
        //§8 slow path) without a real modal ShowDialog.
        internal Func<List<PresetErrorPackage>, Task<Preset?>>? PresetSelectionDialogStub { get; set; }
        internal Func<List<PresetErrorPackage>, Preset, Task<Preset?>>? ChooseAnotherPresetDialogStub { get; set; }
        internal Func<Preset, AppSettings, Task<DataLoadResult>>? DataLoadStub { get; set; }

        //Test-only seam: lets a test observe/short-circuit the silent-switch info message (io-reference.md
        //§8) without a real modal ShowDialog.
        internal Func<string, Task>? PresetSwitchInfoStub { get; set; }

        internal Action? AboutDialogStub { get; set; }

        internal Action<IReadOnlyList<Preset>>? PresetComparatorDialogStub { get; set; }

        internal ILauncher? LauncherOverride { get; set; }

        private GraphLibraryService? library;
        private bool loadingGraph;
        private RelayCommand revertToLastSavedCommand = null!;
        private NativeMenuItem openRecentItem = null!;

        internal GraphLibraryService? Library {
            get => library;
            set {
                if (library is not null) {
                    library.WriteFailed -= OnLibraryWriteFailed;
                    library.Saved -= OnLibrarySaved;
                    library.Snapshot = null;
                }
                library = value;
                if (library is not null) {
                    library.Snapshot = CaptureSnapshot;
                    library.Post = action => Dispatcher.UIThread.Post(action);
                    library.WriteFailed += OnLibraryWriteFailed;
                    library.Saved += OnLibrarySaved;
                }
            }
        }

        internal static MainWindow? MostRecentlyOpened { get; private set; }

        internal static MainWindow? ActiveWindow =>
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
            titleBarTitle = this.FindControl<TextBlock>("TitleBarTitle")!;
            contentPopoverHost = this.FindControl<PopoverHost>("ContentPopoverHost")!;
            AddHandler(KeyDownEvent, OnCancelInFlightKeyDown, RoutingStrategies.Tunnel);
            ZoomInCommand = new RelayCommand(MenuZoomIn);
            ZoomOutCommand = new RelayCommand(MenuZoomOut);
            ActualSizeCommand = new RelayCommand(MenuActualSize);
            ResearchSync = new ResearchSyncFlow(() => GraphCanvas.Viewer.Graph, () => DataCache!, () => Settings) { Announce = Announce };
            ResearchSync.RunningChanged += (_, _) => UpdateSyncResearchState();
            Commands = new ShellViewModel(
                onNew: () => Async.Fire(NewGraphAsync(), nameof(NewGraphAsync)),
                onLoad: () => Async.Fire(OpenGraphFileAsync(), nameof(OpenGraphFileAsync)),
                onSave: SaveVersion,
                onImport: () => Async.Fire(ImportGraphAsync(), nameof(ImportGraphAsync)),
                onExportImage: () => Async.Fire(OpenImageExportAsync(), nameof(OpenImageExportAsync)),
                onAddItem: () => GraphCanvas.AddItemAsync(ViewportCentre(), PaletteAnchorUnder("AddItemButton")),
                onAddRecipe: () => GraphCanvas.AddRecipeAsync(ViewportCentre(), PaletteAnchorUnder("AddRecipeButton")),
                onAutoconnect: () => { GraphCanvas.ClosePanels(); GraphCanvas.Viewer.AutoconnectDisconnectedInputs(); GraphCanvas.RequestRedraw(); },
                onAlignSelection: () => { GraphCanvas.ClosePanels(); GraphCanvas.AlignSelection(); GraphCanvas.RequestRedraw(); },
                onSettings: () => ShowSettings(),
                onGraphSummary: ShowGraphSummary,
                onSyncResearch: () => SyncResearch(chooseAnother: false),
                onSyncResearchFromAnotherSave: () => SyncResearch(chooseAnother: true),
                canExecuteAlignSelection: () => GraphCanvas.Viewer.SelectedNodes.Count > 0,
                canExecuteSyncResearch: () => isDocumentOpen && !ResearchSync.IsRunning);
            Inspector.AddRecipeCommand = Commands.AddRecipeCommand;
            Inspector.OpenPresetsRequested += (_, _) => ShowSettings(selectPresetsPane: true);
            Inspector.PickAnotherRecipeRequested += (_, node) =>
                OpenBrowser(new AddRequest(node.Location, AddScope.Recipes, SeedText: node.BaseRecipe.FriendlyName ?? ""));
            Inspector.RateUnitChanged += (_, unit) => SetRateUnit(unit);
            GraphCanvas.Viewer.SelectionChanged += (_, _) => Commands.AlignSelectionCommand.NotifyCanExecuteChanged();

            GraphCanvas.BrowseRequested += (_, request) => OpenBrowser(request);

            ViewModel = new MainWindowViewModel();
            DataContext = ViewModel;

            GraphCanvas.Viewer.SelectionChanged += (_, _) =>
                ViewModel.Inspector.SetSelection([.. GraphCanvas.Viewer.SelectedNodes.Select(n => n.ViewModel)]);
            Inspector.IsVisible = ViewModel.IsInspectorVisible;
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

            GraphCanvas.Viewer.Session.GraphMutated += (_, _) => OnGraphMutated();

            BuildToolbarRow();
            InitializeSolveStatus();
            BuildViewOptionsPopover();
            this.FindControl<Grid>("TitleBarRow")!.Children.Add(searchAnnouncer);
            toolbar.Title = ViewModel.DocumentTitle;
            GraphCanvas.KeyDown += OnGraphCanvasKeyDown;
            toolbar.SearchField.PropertyChanged += (_, e) => {
                if (e.Property == IsKeyboardFocusWithinProperty && !toolbar.SearchField.IsKeyboardFocusWithin && searchRevealsToolbar) {
                    searchRevealsToolbar = false;
                    ApplyToolbarShown();
                }
            };
            GraphCanvas.ViewScaleChanged += (_, _) => ResyncZoomLabel();
            NativeMenu.SetMenu(this, BuildNativeMenu());
            UpdateSyncResearchState();
            Opened += (_, _) => MostRecentlyOpened = this;
            if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatable) {
                EventHandler<ActivatedEventArgs> flushOnResign = (_, _) => OnAppDeactivated();
                activatable.Deactivated += flushOnResign;
                Closed += (_, _) => activatable.Deactivated -= flushOnResign;
            }
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
            syncResearchItem = ToolbarButton("SyncResearchButton", "Sync", "Sync", Commands.SyncResearchCommand, automationName: "Sync Research", overflowLabel: "Sync Research");
            toolbar.Groups.Add(new ToolbarGroup {
                Role = ToolbarGroupRole.Research,
                Items = [syncResearchItem],
            });
            viewOptionsItem = ToolbarButton("ViewOptionsButton", "View", "View Options", ViewModel.ToggleViewOptionsCommand, overflowLabel: "View Options");
            toolbar.Groups.Add(new ToolbarGroup {
                Role = ToolbarGroupRole.ShareView,
                Items = [
                    ToolbarButton("ExportImageButton", "Export", "Export", Commands.ExportImageCommand, automationName: "Export Image", acceleratorKey: "⇧⌘E", overflowLabel: "Export Image…"),
                    ToolbarButton("GraphSummaryButton", "Summary", "Summary", Commands.GraphSummaryCommand, automationName: "Graph Summary", overflowLabel: "Graph Summary"),
                    viewOptionsItem,
                ],
            });

            toolbar.SearchField.PlaceholderText = "Find node";
            AutomationProperties.SetName(toolbar.SearchField, "Find node");
            toolbar.SearchField.TextChanged += (_, _) => ResetSearchMatches();
            GraphCanvas.Viewer.Session.GraphMutated += (_, _) => RecomputeSearchMatchesOnGraphChange();
            GraphCanvas.Announced += (_, text) => Announce(text);
            GraphCanvas.InspectorRequested += (_, _) => FocusInspector();
            GraphCanvas.PropertyChanged += (_, e) => {
                if (e.Property == InputElement.IsFocusedProperty)
                    UpdateSelectNodeMenuItems();
            };
            GraphCanvas.Viewer.ElementsChanged += (_, _) => UpdateSelectNodeMenuItems();
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

            contentPopoverHost.Surface.Children.Add(viewOptionsPopover);
        }

        private static ToolbarItem ToolbarButton(string name, string glyphName, string label, IRelayCommand command, string? automationName = null, string? acceleratorKey = null, string? overflowLabel = null) {
            var item = new ToolbarItem { Name = name, Glyph = ToolbarGlyph(glyphName), Label = label, OverflowLabel = overflowLabel, AutomationName = automationName, Command = command, IsEnabled = command.CanExecute(null) };
            if (acceleratorKey is not null)
                AutomationProperties.SetAcceleratorKey(item, acceleratorKey);
            command.CanExecuteChanged += (_, _) => item.IsEnabled = command.CanExecute(null);
            return item;
        }

        private static Geometry? ToolbarGlyph(string name) =>
            Application.Current?.TryGetResource($"Kit.Glyph.{name}.17", null, out object? geometry) == true ? geometry as Geometry : null;

        private void ApplyDocumentEditedState(bool isDirty) {
            documentEdited = isDirty;
            if (OperatingSystem.IsMacOS() && TryGetPlatformHandle() is { HandleDescriptor: "NSWindow" } handle)
                NativeWindowChrome.SetDocumentEdited(handle.Handle, isDirty);
        }

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

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.F && e.KeyModifiers == PlatformModifiers.Primary) {
                FocusSearchField();
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
            if (!e.Handled && Inspector.IsKeyboardFocusWithin && ((e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) ||
                (e.Key == Key.Tab && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))) {
                GraphCanvas.RestoreKeyboardFocus();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private bool CanSelectNode() => isDocumentOpen && GraphCanvas.IsFocused && GraphCanvas.NodeElements.Count > 0;

        //Tab is a menu key equivalent only while the canvas is focused, or a text field would lose it.
        private void UpdateSelectNodeMenuItems() {
            bool focused = GraphCanvas.IsFocused;
            bool enabled = CanSelectNode();
            selectNextNodeItem.Gesture = focused ? new KeyGesture(Key.Tab) : null;
            selectPreviousNodeItem.Gesture = focused ? new KeyGesture(Key.Tab, KeyModifiers.Shift) : null;
            selectNextNodeItem.IsEnabled = enabled;
            selectPreviousNodeItem.IsEnabled = enabled;
            selectNextNodeCommand.NotifyCanExecuteChanged();
            selectPreviousNodeCommand.NotifyCanExecuteChanged();
        }

        private void FocusInspector() {
            Inspector.UpdateLayout();
            Inspector.FocusFirstControl();
        }

        private void FocusSearchField() {
            if (!showToolbar && !searchRevealsToolbar) {
                searchRevealsToolbar = true;
                ApplyToolbarShown();
            }
            toolbar.SearchField.Focus();
            toolbar.SearchField.SelectAll();
        }

        private bool replayingCancelKey;

        private void OnCancelInFlightKeyDown(object? sender, KeyEventArgs e) {
            if (replayingCancelKey || e.Key != Key.OemPeriod || e.KeyModifiers != PlatformModifiers.Primary)
                return;
            if (solveTracker.State == SolveStatusState.Solving) {
                GraphCanvas.Viewer.Graph.Solver.Cancel();
                e.Handled = true;
            } else if (contentPopoverHost.Surface.Children.OfType<PopoverFrame>().LastOrDefault(f => f.IsOpen) is { } popover) {
                e.Handled = ReplayCancelKey(popover);
            } else if (GraphCanvas.IsPanelOpen) {
                GraphCanvas.ClosePanels();
                e.Handled = true;
            } else if (SheetHost.For(this).FrontmostCancelTarget is { } sheet) {
                e.Handled = ReplayCancelKey(sheet);
            }
        }

        //The kit frames own their ⌘. behaviour (a sheet closes its own open popover first) and only recognise Meta, so Linux's Ctrl+period reaches them as ⌘.
        private bool ReplayCancelKey(InputElement target) {
            var replay = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.OemPeriod, KeyModifiers = KeyModifiers.Meta };
            replayingCancelKey = true;
            try {
                target.RaiseEvent(replay);
            } finally {
                replayingCancelKey = false;
            }
            return replay.Handled;
        }

        internal static string? DrawnName(BaseNodeElement node) => node.ViewModel switch {
            IRecipeNodeViewModel r => r.BaseRecipe.FriendlyName,
            ISupplierNodeViewModel s => s.SuppliedItem.FriendlyName,
            IConsumerNodeViewModel c => c.ConsumedItem.FriendlyName,
            IPassthroughNodeViewModel p => p.PassthroughItem.FriendlyName,
            ISpoilNodeViewModel sp => sp.InputItem.FriendlyName is string n ? n + " Spoilage" : null,
            IPlantNodeViewModel pl => pl.Seed.FriendlyName is string n ? n + " Planting" : null,
            _ => null,
        };

        internal static bool MatchesSearchQuery(BaseNodeElement node, string query) {
            string trimmed = query.Trim();
            if (trimmed.Length == 0)
                return false;
            return (DrawnName(node) is string name && name.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase)) ||
                (node.ViewModel.KeyNode && node.ViewModel.KeyNodeTitle.Contains(trimmed, StringComparison.CurrentCultureIgnoreCase));
        }

        private List<BaseNodeElement> OrderedSearchMatches(string query) =>
            CanvasTraversal.ReadingOrder(GraphCanvas.Viewer.NodeElements.Where(n => MatchesSearchQuery(n, query)));

        private int FirstSearchMatchIndexAtOrAfter(System.Drawing.Point point) {
            for (int i = 0; i < searchMatches.Count; i++) {
                System.Drawing.Point at = searchMatches[i].Location;
                if (at.Y > point.Y || (at.Y == point.Y && at.X >= point.X))
                    return i;
            }
            return -1;
        }

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

        private void MarkSearchMatches() {
            HashSet<BaseNodeElement> matches = [.. searchMatches];
            foreach (BaseNodeElement node in GraphCanvas.Viewer.NodeElements)
                node.IsFindMatch = matches.Contains(node);
            GraphCanvas.RequestRedraw();
        }

        private void SelectCurrentSearchMatch() {
            BaseNodeElement match = searchMatches[searchMatchIndex];
            GraphCanvas.Viewer.SetSelection([match]);
            GraphCanvas.PanIntoView(CanvasTraversal.WorldBounds(match), GraphCanvasControl.FindPanMargin);
            GraphCanvas.RequestRedraw();
        }

        private void ResetSearchMatches() {
            string query = toolbar.SearchField.Text ?? "";
            BaseNodeElement? wasCurrent = searchMatchIndex >= 0 && searchMatchIndex < searchMatches.Count ? searchMatches[searchMatchIndex] : null;

            searchMatches = string.IsNullOrWhiteSpace(query) ? [] : OrderedSearchMatches(query);
            MarkSearchMatches();
            int stillCurrent = wasCurrent is null ? -1 : searchMatches.IndexOf(wasCurrent);
            searchMatchIndex = searchMatches.Count == 0 ? -1 : stillCurrent >= 0 ? stillCurrent : AnchorSearchMatchIndex();

            if (searchMatchIndex >= 0)
                SelectCurrentSearchMatch();
            UpdateSearchMatchStatus();
            ScheduleSearchResultAnnouncement(query);
        }

        private void RecomputeSearchMatchesOnGraphChange() {
            string query = toolbar.SearchField.Text ?? "";
            if (string.IsNullOrWhiteSpace(query))
                return;

            BaseNodeElement? wasCurrent = searchMatchIndex >= 0 && searchMatchIndex < searchMatches.Count ? searchMatches[searchMatchIndex] : null;
            int wasIndex = searchMatchIndex;
            searchMatches = OrderedSearchMatches(query);
            MarkSearchMatches();
            int stillCurrent = wasCurrent is null ? -1 : searchMatches.IndexOf(wasCurrent);
            searchMatchIndex = searchMatches.Count == 0 ? -1
                : stillCurrent >= 0 ? stillCurrent
                : wasIndex < 0 || wasIndex >= searchMatches.Count ? 0
                : wasIndex;

            if (searchMatchIndex >= 0 && stillCurrent < 0)
                SelectCurrentSearchMatch();
            UpdateSearchMatchStatus();
        }

        private void StepSearchMatch(bool forward) {
            if (searchMatches.Count == 0)
                return;

            searchAnnounceTimer?.Stop();
            int next = searchMatchIndex + (forward ? 1 : -1);
            bool wrapped = next < 0 || next >= searchMatches.Count;
            searchMatchIndex = ((next % searchMatches.Count) + searchMatches.Count) % searchMatches.Count;

            SelectCurrentSearchMatch();
            UpdateSearchMatchStatus();
            AnnounceSearchStep(searchMatches[searchMatchIndex], wrapped, forward);
        }

        private void UpdateSearchMatchStatus() {
            string query = toolbar.SearchField.Text ?? "";
            toolbar.SearchField.MatchStatus = string.IsNullOrWhiteSpace(query) ? null
                : searchMatches.Count == 0 ? "No matches"
                : string.Create(DisplayCulture.Format, $"{searchMatchIndex + 1} of {searchMatches.Count}");
        }

        private static string MatchCountLabel(int count) => count == 1 ? "1 match" : string.Create(DisplayCulture.Format, $"{count} matches");

        //DispatcherTimer, not Task.Delay: a delay outliving its window fires on another test's UI thread
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

        private void AnnounceSearchStep(BaseNodeElement match, bool wrapped, bool forward) {
            string name = DrawnName(match) ?? match.ViewModel.KeyNodeTitle;
            string text = string.Create(DisplayCulture.Format, $"{searchMatchIndex + 1} of {searchMatches.Count}, {name}");
            Announce(wrapped ? (forward ? "Wrapped to first match. " : "Wrapped to last match. ") + text : text);
        }

        private void Announce(string text) {
            AutomationProperties.SetName(searchAnnouncer, string.Empty);
            AutomationProperties.SetName(searchAnnouncer, text);
        }

        private sealed class SearchAnnouncer : TextBlock {
            protected override AutomationPeer OnCreateAutomationPeer() => new AnnouncerAutomationPeer(this);

            private sealed class AnnouncerAutomationPeer(SearchAnnouncer owner) : ControlAutomationPeer(owner) {
                protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
                protected override AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Polite;
            }
        }

        public void RefreshTheme() {
            minSizeScale.Apply();
            ApplyCanvasTheme();
            RefreshBrowserIcons();
        }

        //Stale baked icons fall out of scope instead of being disposed: a still-attached Image can be measuring one
        private void RefreshBrowserIcons() {
            browserIconCache.Clear();
            if (browser?.DataContext is AddBrowserViewModel vm)
                vm.Refresh(forceRebuild: true);
        }

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

            WindowScaler.SetScale(settings.UiScale);

            ApplyCanvasTheme();

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
            GraphCanvas.Viewer.DefaultLinkRouting = settings.DefaultLinkRouting;
            context.FlagOUSuppliedNodes = settings.FlagOUSuppliedNodes;
            context.RoundAssemblerCount = settings.RoundAssemblerCount;
            context.AbbreviateSciPacks = settings.AbbreviateSciPacks;

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
        //same seam boot, preset reload, and every Settings Confirm already replay through.
        private void ApplyCanvasTheme() {
            if (Settings is not AppSettings settings)
                return;

            bool isDark = settings.FlagDarkMode == ThemeMode.Dark ||
                (settings.FlagDarkMode == ThemeMode.System && Application.Current?.ActualThemeVariant == ThemeVariant.Dark);
            GraphCanvas.Viewer.ApplyTheme(isDark);
            GraphCanvas.RequestRedraw();
        }

        //Runs on every settings load: the checkbox subscriptions must wire exactly once
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
                SetCalculationsPaused(pauseUpdatesCheckBox.IsChecked == true);
                ResyncNativeMenuCheckedStates();
                GraphCanvas.InvalidateVisual();
            };
            GraphCanvas.CalculationsResumed += (_, _) => pauseUpdatesCheckBox.IsChecked = false;
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

            ResyncNativeMenuCheckedStates();
        }

        private static int GridUnitFromIndex(int index) => index > 0 ? 6 * (int)Math.Pow(2, index - 1) : 0;

        internal async Task OpenGraphFileAsync() {
            if (DataCache is null)
                return;
            using IDisposable busy = ShellHost.HoldBusy();
            (string Json, string Path)? picked = await (OpenFileStub?.Invoke() ?? PickGraphFileAsync()).ConfigureAwait(true);
            if (picked is not (string json, string path))
                return;

            await OpenFjsonAsync(json, path).ConfigureAwait(true);
        }

        internal Window? AlertOwner { get; set; }

        internal bool ShowDeferred { get; set; }

        private Window AlertHost() {
            if (AlertOwner is { } owner)
                return owner;
            RevealDeferred();
            return this;
        }

        internal void RevealDeferred() {
            if (!ShowDeferred)
                return;
            ShowDeferred = false;
            Show();
            if (ShellContext is { } context)
                WelcomeLauncher.PresentPending(context);
        }

        internal async Task OpenFjsonPathAsync(string path) {
            if (DataCache is null)
                return;
            if (await ReadGraphFileAsync(AlertHost(), path).ConfigureAwait(true) is string json)
                await OpenFjsonAsync(json, path).ConfigureAwait(true);
        }

        internal static async Task<string?> ReadGraphFileAsync(Window owner, string path) {
            string name = Dialogs.MiddleTruncate(Path.GetFileNameWithoutExtension(path));
            try {
                string json = await File.ReadAllTextAsync(path).ConfigureAwait(true);
                if (GraphSaveCodec.ReadViewer(json) is not null)
                    return json;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                ErrorLogging.LogException(ex, $"Couldn't read dropped file '{path}'");
            }
            await Dialogs.ShowWarningAsync(owner, $"Couldn’t open “{name}”.", UnreadableGraphSentence).ConfigureAwait(true);
            return null;
        }

        private async Task<(string Json, string Path)?> PickGraphFileAsync() {
            if (AlertHost().StorageProvider is not IStorageProvider storage)
                return null;

            Directory.CreateDirectory(AppPaths.SavedGraphsDirectory);
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Open Graph",
                AllowMultiple = false,
                SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(new Uri(AppPaths.SavedGraphsDirectory)).ConfigureAwait(true),
                FileTypeFilter = [new FilePickerFileType("Foreman files") { Patterns = ["*.fjson"] }],
            }).ConfigureAwait(true);
            if (files.Count == 0)
                return null;

            await using Stream stream = await files[0].OpenReadAsync().ConfigureAwait(true);
            using var reader = new StreamReader(stream);
            return (await reader.ReadToEndAsync().ConfigureAwait(true), files[0].Path.LocalPath);
        }

        internal async Task OpenFjsonAsync(string json, string path) {
            string name = Path.GetFileNameWithoutExtension(path);
            GraphViewerSaveDocument? document = GraphSaveCodec.ReadViewer(json);
            if (document is null || Library is null) {
                await LoadReplacingAsync(json, name, null).ConfigureAwait(true);
                return;
            }

            string data = GraphSaveCodec.WriteViewerDocumentToString(document, writeIndented: true);
            if (Library.FindImport(path, data) is GraphRow existing) {
                await OpenLibraryGraphAsync(existing.Id).ConfigureAwait(true);
                return;
            }
            if (!await LoadReplacingAsync(data, name, null).ConfigureAwait(true) || Library is null)
                return;

            GraphRow row = Library.Create(name, CurrentPresetName, data, GraphCanvas.Viewer.Graph.Nodes.Count(), path);
            ShowOpenedGraph(row);
        }

        internal async Task<bool> OpenLibraryGraphAsync(Guid id, Preset? presetOverride = null) {
            if (Library is null || Library.OpenGraphId == id)
                return false;
            Library.FlushAndWait();
            if (Library.Find(id) is not GraphRow stored)
                return false;
            if (!await LoadReplacingAsync(stored.Data, stored.Name, presetOverride, announcePresetSwitch: false).ConfigureAwait(true))
                return false;
            if (Library is null)
                return false;

            ShowOpenedGraph(Library.Open(id) ?? stored);
            return true;
        }

        private async Task<bool> LoadReplacingAsync(string json, string name, Preset? presetOverride, bool announcePresetSwitch = true) {
            using IDisposable busy = ShellHost.HoldBusy();
            Library?.FlushAndWait();
            GraphRow? previous = Library?.OpenGraphId is Guid previousId ? Library.Find(previousId) : null;
            if (await LoadGraphJsonAsync(json, name, presetOverride, announcePresetSwitch).ConfigureAwait(true))
                return true;
            if (previous is null || Library is null)
                return false;
            if (Library.OpenGraphId != previous.Id && await LoadGraphJsonAsync(previous.Data, previous.Name, announcePresetSwitch: false).ConfigureAwait(true))
                Library.Resume(previous);
            if (Library.OpenGraphId == previous.Id)
                ViewModel.IsDirty = GraphLibraryService.IsEditedSinceSave(previous);
            return false;
        }

        internal void OnAppDeactivated() => Library?.FlushAndWait();

        private void ShowOpenedGraph(GraphRow row) {
            if (Library is not null && row.PresetName != CurrentPresetName)
                Library.SetPreset(CurrentPresetName);
            ViewModel.DocumentName = row.Name;
            ViewModel.IsDirty = GraphLibraryService.IsEditedSinceSave(row);
        }

        //Detaches the library graph before the swap so the load's own mutation events never autosave into the previous row.
        internal async Task<bool> LoadGraphJsonAsync(string json, string name, Preset? presetOverride = null, bool announcePresetSwitch = true) {
            if (DataCache is not Foreman.DataCaching.DataCache cache || Settings is not AppSettings settings)
                return false;

            GraphViewerSaveDocument? document = GraphSaveCodec.ReadViewer(json);
            if (document is null) {
                await Dialogs.ShowWarningAsync(AlertHost(), $"Couldn’t open “{Dialogs.MiddleTruncate(name)}”.", UnreadableGraphSentence).ConfigureAwait(true);
                return false;
            }

            Library?.FlushAndWait();
            string previousPresetName = settings.CurrentPresetName;
            Preset? chosenPreset = presetOverride ?? await ResolveChosenPresetAsync(document, announcePresetSwitch).ConfigureAwait(true);
            while (chosenPreset is not null && chosenPreset.Name != cache.PresetName) {
                DataLoadResult loaded = await LoadDataCacheForPresetAsync(chosenPreset, settings).ConfigureAwait(true);
                if (loaded.Cache is Foreman.DataCaching.DataCache newCache) {
                    cache = newCache;
                    break;
                }
                chosenPreset = loaded.Outcome == DataLoadOutcome.ChooseAnother
                    ? await ChooseAnotherPresetAsync(document, chosenPreset).ConfigureAwait(true)
                    : null;
            }
            if (chosenPreset is null) {
                if (settings.CurrentPresetName != previousPresetName) {
                    settings.CurrentPresetName = previousPresetName;
                    SettingsService?.Save(settings);
                }
                return false;
            }

            //LoadDocument is about to dispose every current node view model.
            ViewModel.Inspector.SetSelection([]);
            Library?.Detach();
            loadingGraph = true;
            GraphLoadResult result;
            try {
                result = GraphCanvas.LoadDocument(cache, document);
            } finally {
                loadingGraph = false;
            }
            if (!result.Success)
                await Dialogs.ShowWarningAsync(AlertHost(), $"Couldn’t open “{Dialogs.MiddleTruncate(name)}”.", result.ErrorMessage ?? UnreadableGraphSentence).ConfigureAwait(true);
            else
                ApplyLoadedGraphUiState(name);

            GraphCanvas.InvalidateVisual();
            return result.Success;
        }

        //Ports ResolveChosenPresetAsync (reference upstream ProductionGraphViewer.cs:1434-1507, io-reference.
        //md §8): the matching engine (PresetProcessor.TestPreset/PresetErrorPackage) is already ported, this
        //only orchestrates it. Fast path: the save's own SavedPresetName matches an installed preset with
        //zero errors, used with no dialog. Silent-switch: that fast-path match isn't the currently active
        //preset, so CurrentPresetName follows it with an info message instead of a dialog. Slow path: no
        //clean name match (or the name match itself had errors, which still carries its already-computed
        //errors into the list below rather than re-testing it) - every other installed preset gets tested
        //and the ranked picker decides. Returns null on cancel (dialog cancel or no installed presets).
        internal async Task<Preset?> ResolveChosenPresetAsync(GraphViewerSaveDocument saveDocument, bool announceSwitch = true) {
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
                if (announceSwitch)
                    await ShowPresetSwitchInfoAsync(string.Create(DisplayCulture.Format,
                        $"Loaded graph uses a different Preset.\nPreset switched from \"{previousPresetName}\" to \"{newPresetName}\"")).ConfigureAwait(true);
                settings.CurrentPresetName = newPresetName;
                settingsService.Save(settings);
            }

            return chosenPreset;
        }

        private Task<Preset?> ShowPresetSelectionDialogAsync(List<PresetErrorPackage> presetErrors) {
            RevealDeferred();
            return PresetSelectionDialogStub?.Invoke(presetErrors) ?? RealShowPresetSelectionDialogAsync(presetErrors);
        }

        //Ports the ResolveChosenPresetAsync slow path's dialog launch (upstream ProductionGraphViewer.cs:
        //1477-1488). The chosen preset is read while the sheet is still open, so a failed read shows on its strip.
        private async Task<Preset?> RealShowPresetSelectionDialogAsync(List<PresetErrorPackage> presetErrors, Preset? failed = null) {
            PresetSelectionSheet sheet = NewPresetSelectionSheet(presetErrors, failed);
            bool? result = await SheetHost.For(this).ShowAsync(sheet).ConfigureAwait(true);
            if (!GraphCanvas.IsPanelOpen)
                GraphCanvas.Focus();
            return result == true ? sheet.ChosenPreset : null;
        }

        internal static PresetSelectionSheet NewPresetSelectionSheet(List<PresetErrorPackage> presetErrors, Preset? failed = null) =>
            new(presetErrors, failed) { ReadPresetAsync = CanReadPresetAsync };

        private static Task<bool> CanReadPresetAsync(Preset preset) => Task.Run(() => {
            try {
                Foreman.DataCaching.PresetProcessor.PrepPreset(preset);
                return true;
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Preset selection: couldn't read " + preset.Name);
                return false;
            }
        });

        private Task ShowPresetSwitchInfoAsync(string message) {
            RevealDeferred();
            return PresetSwitchInfoStub?.Invoke(message) ?? Dialogs.ShowInfoAsync(this, message);
        }

        //Ports the ResolveChosenPresetAsync/ReloadGraphForCurrentPreset shared DataLoadForm spin-up (upstream
        //ProductionGraphViewer.cs:1365-1386's non-corrupt path): swaps in the chosen preset's DataCache,
        //shared by both the Load-a-file path (LoadGraphJsonAsync) and the Settings preset-switch path
        //(ReloadForPresetAsync). Only a Loaded outcome swaps the live DataCache and the current preset.
        private async Task<DataLoadResult> LoadDataCacheForPresetAsync(Preset preset, AppSettings settings) {
            RevealDeferred();
            DataLoadResult loaded = await (DataLoadStub?.Invoke(preset, settings) ?? RealLoadDataCacheAsync(preset, settings)).ConfigureAwait(true);
            if (loaded is { Outcome: DataLoadOutcome.Loaded, Cache: Foreman.DataCaching.DataCache newCache }) {
                DataCache = newCache;
                settings.CurrentPresetName = preset.Name;
            }
            return loaded;
        }

        private async Task<DataLoadResult> RealLoadDataCacheAsync(Preset preset, AppSettings settings) {
            var dataLoadWindow = new DataLoadWindow(preset, settings.UseRecipeBWfilters);
            dataLoadWindow.Show(this);
            await dataLoadWindow.LoadTask.ConfigureAwait(true);
            return new DataLoadResult(dataLoadWindow.Result, dataLoadWindow.Outcome);
        }

        private async Task<Preset?> ChooseAnotherPresetAsync(GraphViewerSaveDocument saveDocument, Preset failed) {
            if (Settings is not AppSettings settings || PresetResolver is not Services.PresetResolver presetResolver)
                return null;
            RevealDeferred();

            List<PresetErrorPackage> presetErrors = await RatePresetsAsync(presetResolver, settings.CurrentPresetName, saveDocument).ConfigureAwait(true);

            Preset? choice = await (ChooseAnotherPresetDialogStub?.Invoke(presetErrors, failed) ?? RealShowPresetSelectionDialogAsync(presetErrors, failed)).ConfigureAwait(true);
            if (choice is not null) {
                settings.CurrentPresetName = choice.Name;
                SettingsService?.Save(settings);
            }
            return choice;
        }

        internal static async Task<List<PresetErrorPackage>> RatePresetsAsync(Services.PresetResolver presetResolver, string currentPresetName, GraphViewerSaveDocument saveDocument) {
            ProductionGraphSaveDocument productionGraph = saveDocument.ProductionGraph;
            var modSet = new Dictionary<string, string>(saveDocument.IncludedMods);
            var itemNames = productionGraph.IncludedItems.ToList();
            var qualityNames = productionGraph.IncludedQualities.Select(q => q.Key).ToList();
            var recipeShorts = productionGraph.IncludedRecipes.ToList();
            var plantShorts = productionGraph.IncludedPlantProcesses.ToList();

            var presetErrors = new List<PresetErrorPackage>();
            foreach (Preset preset in presetResolver.BuildPresetList(currentPresetName))
                presetErrors.Add(await PresetProcessor.TestPreset(preset, modSet, itemNames, qualityNames, recipeShorts, plantShorts).ConfigureAwait(true));
            return presetErrors;
        }

        internal void ApplyLoadedGraphUiState(string name) {
            ViewModel.DocumentName = name;
            ViewModel.IsDirty = false;
            UpdateSyncResearchState();
            ResyncZoomLabel();
            ResyncRateOptionsDropDown();

            if (Settings is AppSettings settings) {
                settings.EnableExtraProductivityForNonMiners = GraphCanvas.Viewer.Graph.EnableExtraProductivityForNonMiners;
                settings.DefaultAssemblerOption = GraphCanvas.Viewer.Graph.AssemblerSelector.DefaultSelectionStyle;
                settings.DefaultModuleOption = GraphCanvas.Viewer.Graph.ModuleSelector.DefaultSelectionStyle;
                settings.DefaultNodeDirection = GraphCanvas.Viewer.Graph.DefaultNodeDirection;
            }
        }

        internal Foreman.DataCaching.DataCache? PendingCache { get; set; }

        internal bool IsClosed { get; private set; }

        internal async Task NewGraphAsync() {
            Library?.Detach();
            GraphCanvas.ClosePanels();
            ViewModel.Inspector.SetSelection([]);
            loadingGraph = true;
            try {
                GraphCanvas.Viewer.Graph.ClearGraph();
            } finally {
                loadingGraph = false;
            }
            ViewModel.IsDirty = false;
            UpdateSyncResearchState();
            ViewModel.DocumentName = null;
            if (PendingCache is { } pending) {
                PendingCache = null;
                if (Settings?.CurrentPresetName == pending.PresetName)
                    await AdoptCacheAsync(pending).ConfigureAwait(true);
            }
            if (Library is not null && CaptureSnapshot() is GraphSnapshot snapshot) {
                GraphRow row = Library.CreateBlank(Library.NextUntitledName(), CurrentPresetName, snapshot.Data);
                ViewModel.DocumentName = row.Name;
            }
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
        //ExportGraphAsync(path) already established.
        internal async Task ImportGraphJsonAsync(string json, string path) {
            if (DataCache is not Foreman.DataCaching.DataCache cache)
                return;

            try {
                ProductionGraphSaveDocument document = GraphSaveCodec.ReadGraphPayload(json) ?? throw new InvalidOperationException(
                    "This save file is too old or corrupt. Try opening it in the previous Foreman release and saving it again, then open the new file here.");
                System.Drawing.Point origin = GraphCanvas.Viewport.ScreenToGraph(new Avalonia.Point(GraphCanvas.Viewport.Width / 2, GraphCanvas.Viewport.Height / 2));
                ViewModel.Inspector.SetSelection([]);
                GraphCanvas.Viewer.ImportNodesFromDocument(cache, document, origin, applySolverSettings: true);
                GraphCanvas.RequestRedraw();
            } catch (Exception exception) {
                await Dialogs.ShowWarningAsync(this, $"Couldn’t import “{Dialogs.DocumentName(path)}”.",
                    "Foreman couldn’t read it as a graph. The details are in Foreman’s log.").ConfigureAwait(true);
                Foreman.DataCaching.ErrorLogging.LogException(exception, $"Error importing from file '{path}'");
            }
        }

        private string CurrentPresetName => DataCache?.PresetName ?? Settings?.CurrentPresetName ?? "";

        private GraphSnapshot? CaptureSnapshot() {
            if (DataCache is not Foreman.DataCaching.DataCache cache)
                return null;

            GraphCanvas.Viewer.Graph.SerializeNodeIdSet = null;
            string json = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, cache), writeIndented: true);
            return new GraphSnapshot(json, GraphCanvas.Viewer.Graph.Nodes.Count(), GraphThumbnail.Capture(GraphCanvas.Viewer));
        }

        private void SyncResearch(bool chooseAnother) {
            if (DataCache is not null)
                Async.Fire(ResearchSync.RunAsync(AlertHost(), chooseAnother), nameof(ResearchSyncFlow.RunAsync));
        }

        private void UpdateSyncResearchState() {
            ResearchSource? source = GraphCanvas.Viewer.Graph.Research.Source;
            bool enabled = isDocumentOpen && !ResearchSync.IsRunning;
            syncResearchMenuItem.Header = source is null ? "Sync Research…" : "Sync Research";
            syncResearchMenuItem.IsEnabled = enabled;
            syncResearchFromAnotherMenuItem.IsEnabled = enabled;
            if (source is null) {
                syncResearchItem.Tooltip = "Sync research from a save (⌘R)";
                AutomationProperties.SetHelpText(syncResearchItem, "Asks for a save, then syncs research from it.");
            } else {
                string name = Dialogs.MiddleTruncate(source.SaveName);
                syncResearchItem.Tooltip = $"Sync research from “{name}” (⌘R)";
                AutomationProperties.SetHelpText(syncResearchItem, $"Syncs research from “{name}”.");
            }
            Commands.SyncResearchCommand.NotifyCanExecuteChanged();
            Commands.SyncResearchFromAnotherSaveCommand.NotifyCanExecuteChanged();
        }

        private void OnGraphMutated() {
            UpdateSyncResearchState();
            if (loadingGraph)
                return;
            ViewModel.IsDirty = true;
            if (Library is null)
                return;
            EnsureLibraryGraph();
            Library.NotifyChanged();
        }

        private void EnsureLibraryGraph() {
            if (Library is null || Library.OpenGraphId is not null || CaptureSnapshot() is not GraphSnapshot snapshot)
                return;
            snapshot.Thumbnail?.Dispose();
            GraphRow row = Library.Create(Library.NextUntitledName(), CurrentPresetName, snapshot.Data, snapshot.NodeCount, null);
            ViewModel.DocumentName = row.Name;
        }

        private void OnLibrarySaved(object? sender, bool clean) {
            if (clean)
                ViewModel.IsDirty = false;
            RefreshLibraryMenus();
        }

        private void OnLibraryWriteFailed(object? sender, string name) =>
            Async.Fire(Dialogs.ShowWarningAsync(this, $"Couldn’t save “{Dialogs.MiddleTruncate(name)}”.",
                "Foreman keeps trying. Check that your disk has free space."), nameof(OnLibraryWriteFailed));

        internal void SaveVersion() {
            if (Library is null)
                return;
            EnsureLibraryGraph();
            Library.Save();
            RefreshLibraryMenus();
        }

        internal void DuplicateGraph() {
            if (Library is null)
                return;
            EnsureLibraryGraph();
            if (Library.OpenGraphId is null)
                return;
            Library.FlushAndWait();
            GraphRow copy = Library.Duplicate(ViewModel.DocumentTitle + " copy");
            ShowOpenedGraph(copy);
            RefreshLibraryMenus();
        }

        internal async Task RenameGraphAsync() {
            if (Library is null)
                return;
            EnsureLibraryGraph();
            if (Library.OpenGraphId is null)
                return;
            string? name = await SheetHost.For(this).ShowAsync(new RenameGraphSheet(ViewModel.DocumentTitle)).ConfigureAwait(true);
            if (name is null)
                return;
            Library.Rename(name);
            ViewModel.DocumentName = name;
        }

        internal async Task BrowseVersionsAsync() {
            if (Library is null)
                return;
            EnsureLibraryGraph();
            if (Library.OpenGraphId is null)
                return;
            Library.FlushAndWait();
            var sheet = new VersionBrowserSheet(ViewModel.DocumentTitle, Library.Versions()) {
                ConfirmDeleteAsync = () => Dialogs.ShowDestructiveConfirmAsync(this, "Delete this version?", "You can’t undo this action.", "Delete"),
                DeleteVersion = versionId => Library.DeleteVersion(versionId),
            };
            Guid? versionId = await SheetHost.For(this).ShowAsync(sheet).ConfigureAwait(true);
            if (versionId is Guid id)
                await RestoreVersionAsync(id).ConfigureAwait(true);
        }

        private void RevertToLastSaved() {
            if (Library?.Versions().FirstOrDefault(v => v.Kind == GraphVersionKind.Saved) is GraphVersionRow saved)
                Async.Fire(RestoreVersionAsync(saved.Id), nameof(RestoreVersionAsync));
        }

        internal async Task RestoreVersionAsync(Guid versionId) {
            if (Library is null || Library.OpenGraphId is not Guid graphId)
                return;
            Library.FlushAndWait();
            if (Library.Versions().FirstOrDefault(v => v.Id == versionId) is not GraphVersionRow version || Library.Find(graphId) is not GraphRow current)
                return;
            if (!await LoadReplacingAsync(version.Data, current.Name, null).ConfigureAwait(true))
                return;
            Library.Resume(current);
            if (Library.Restore(versionId) is GraphRow restored) {
                Library.Resume(restored);
                ShowOpenedGraph(restored);
            }
            RefreshLibraryMenus();
        }

        private async Task ExportGraphFileAsync() {
            string suggested = ViewModel.DocumentTitle.Replace('/', '-').Replace(':', '-') + ".fjson";
            string? path = await (ExportFilePathStub?.Invoke(suggested) ?? PickExportPathAsync(this, suggested)).ConfigureAwait(true);
            if (path is not null)
                await ExportGraphAsync(path).ConfigureAwait(true);
        }

        internal static async Task<string?> PickExportPathAsync(Window owner, string suggestedName) {
            if (owner.StorageProvider is not IStorageProvider storage)
                return null;

            Directory.CreateDirectory(AppPaths.SavedGraphsDirectory);
            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = "Export Graph",
                SuggestedFileName = suggestedName,
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

        internal async Task<bool> ExportGraphAsync(string path) {
            if (DataCache is not Foreman.DataCaching.DataCache cache)
                return false;

            try {
                GraphCanvas.Viewer.Graph.SerializeNodeIdSet = null;
                string json = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, cache), writeIndented: true);
                Utf8File.WriteAllText(path, json);
                return true;
            } catch (Exception exception) {
                await Dialogs.ShowWarningAsync(this, $"Couldn’t save “{Dialogs.DocumentName(path)}”.",
                    "Your graph is still open and unchanged. The details are in Foreman’s log.").ConfigureAwait(true);
                Foreman.DataCaching.ErrorLogging.LogException(exception, $"Error saving file '{path}'");
                return false;
            }
        }

        private const string UnreadableGraphSentence =
            "The file is too old or damaged to read. Open it in the previous Foreman release, save it again, then open the new copy here.";

        private bool closedByUser;
        private bool abandoned;

        internal void CloseAbandoned() {
            abandoned = true;
            Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e) {
            Library?.PruneUntouched();
            base.OnClosing(e);
            closedByUser = !e.Cancel && e.CloseReason == WindowCloseReason.WindowClosing;
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            ShellContext? context = ShellContext;
            IsClosed = true;
            if (!closedByUser || SettingsService is null || context is null)
                return;
            Library?.Detach();
            Library = null;
            ShellHost.Context = context;
            ShellHost.Sync();
            if (!abandoned && Settings is { ShowWelcomeAtLaunch: true })
                WelcomeLauncher.Show(context);
        }

        private ShellContext? shellContext;

        internal ShellContext? ShellContext {
            get {
                if (shellContext is null) {
                    if (Library is null || Settings is null || DataCache is null)
                        return null;
                    shellContext = new ShellContext(Library, Settings, DataCache, SettingsService, PresetResolver) { Window = this };
                }
                if (!IsClosed)
                    shellContext.Cache = PendingCache ?? DataCache ?? shellContext.Cache;
                return shellContext;
            }
            set => shellContext = value;
        }

        internal void ShowWelcome() {
            if (ShellContext is { } context)
                WelcomeLauncher.Show(context);
        }

        //Ports SettingsButton_Click's post-close cascade (reference upstream MainForm.cs:355-440): builds
        //Options from live state, shows the dialog, then on OK applies a preset switch/reload before always
        //persisting settings.json - matching upstream's presetReloaded branch and its unconditional
        //Properties.Settings.Default.Save() at the end of ApplySettingsDialogChanges. The non-preset-reload
        //branch extends as its tabs' widgets gain something to apply.
        internal void ShowSettings(bool selectPresetsPane = false, string? failedPreset = null) {
            //Toolbar/native-menu clicks never reach GraphCanvasControl.OnPointerPressed's own click-outside-
            //closes-panel guard (they're not canvas pointer events at all), so a panel left open under this
            //window stayed stale once it closed. Closes first, matching NewGraphAsync/LoadDocument's own
            //unconditional-close-on-major-UI-change precedent.
            GraphCanvas.ClosePanels();
            ViewModel.Inspector.SetSelection([]);

            if (settingsWindow is { } existing) {
                if (selectPresetsPane)
                    existing.SelectPresetsPane();
                if (failedPreset is not null)
                    existing.ShowPresetLoadFailure(failedPreset);
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
                if (!GraphCanvas.IsPanelOpen)
                    GraphCanvas.Focus();
            };

            settingsWindow = window;
            settingsSession = session;

            if (failedPreset is not null)
                window.ShowPresetLoadFailure(failedPreset);
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

            string? failedPreset = null;
            if (options.SelectedPreset is Preset selectedPreset
                && await ReloadForPresetAsync(selectedPreset, settings).ConfigureAwait(true) == DataLoadOutcome.ChooseAnother)
                failedPreset = selectedPreset.Name;

            SettingsService?.Save(settings);
            ShowSettings(failedPreset: failedPreset);
        }

        internal void ShowGraphSummary() {
            GraphCanvas.ClosePanels();

            if (graphSummaryWindow is { } existing) {
                if (!existing.IsVisible)
                    existing.Show(this);
                existing.Activate();
                return;
            }

            var host = new GraphSummaryHost {
                Tracker = () => solveTracker,
                Unit = () => GraphCanvas.Viewer.Graph.SelectedRateUnit,
                Reveal = RevealNodes,
                AddRecipe = () => {
                    Activate();
                    GraphCanvas.AddRecipeAsync(ViewportCentre(), PaletteAnchorUnder("AddRecipeButton"));
                },
                Settings = Settings,
            };
            GraphSummaryWindow window = GraphSummaryWindow.Create(GraphCanvas.Viewer.Session, host);
            window.SetTitle(ViewModel.DocumentTitle);
            void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
                if (e.PropertyName == nameof(MainWindowViewModel.DocumentTitle))
                    window.SetTitle(ViewModel.DocumentTitle);
            }
            ViewModel.PropertyChanged += OnViewModelChanged;
            window.Closed += (_, _) => {
                ViewModel.PropertyChanged -= OnViewModelChanged;
                if (ReferenceEquals(graphSummaryWindow, window))
                    graphSummaryWindow = null;
                if (!GraphCanvas.IsPanelOpen)
                    GraphCanvas.Focus();
            };
            graphSummaryWindow = window;
            if (!window.HasSavedPosition)
                window.Position = GraphSummaryPlacement.Resolve(OwnerFrame(), PixelSize.FromSize(new Size(window.Width, window.Height), RenderScaling), Screens.ScreenFromWindow(this)?.WorkingArea, RenderScaling);
            window.Show(this);
        }

        private PixelRect OwnerFrame() => new(Position, PixelSize.FromSize(FrameSize ?? ClientSize, RenderScaling));

        private void RevealNodes(IReadOnlyList<INodeViewModel> nodes) {
            RevealNodeIds([.. nodes.Select(n => n.Id.Value)]);
            Activate();
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
        private async Task<DataLoadOutcome> ReloadForPresetAsync(Preset preset, AppSettings settings) {
            if (DataCache is not Foreman.DataCaching.DataCache oldCache)
                return DataLoadOutcome.Cancelled;

            string graphJson = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, oldCache));

            DataLoadResult loaded = await LoadDataCacheForPresetAsync(preset, settings).ConfigureAwait(true);
            if (loaded.Cache is not Foreman.DataCaching.DataCache newCache)
                return loaded.Outcome;

            await RemapOntoCacheAsync(newCache, graphJson).ConfigureAwait(true);
            return DataLoadOutcome.Loaded;
        }

        internal async Task AdoptCacheAsync(Foreman.DataCaching.DataCache newCache) {
            if (DataCache is not Foreman.DataCaching.DataCache oldCache || ReferenceEquals(oldCache, newCache) || oldCache.PresetName == newCache.PresetName)
                return;

            string graphJson = GraphSaveCodec.WriteViewerDocumentToString(GraphViewerSaveAssembler.BuildSaveDocument(GraphCanvas.Viewer, oldCache));
            DataCache = newCache;
            await RemapOntoCacheAsync(newCache, graphJson).ConfigureAwait(true);
        }

        private async Task RemapOntoCacheAsync(Foreman.DataCaching.DataCache newCache, string graphJson) {
            GraphLoadResult result = GraphCanvas.LoadDocument(newCache, graphJson, setEnablesFromJson: false);
            if (!result.Success)
                await Dialogs.ShowWarningAsync(this, $"Couldn’t open “{Dialogs.MiddleTruncate(ViewModel.DocumentTitle)}”.", result.ErrorMessage ?? UnreadableGraphSentence).ConfigureAwait(true);

            ApplyLoadedSettings();
            Library?.SetPreset(newCache.PresetName ?? CurrentPresetName);
            GraphCanvas.RequestRedraw();
        }

        //Ports GraphViewer_KeyDown's Space handler (reference §7): a separate subscription on the canvas
        //control's own KeyDown event, alongside (not inside) GraphCanvasControl's own OnKeyDown override -
        //mirrors upstream attaching this as MainForm's own delegate rather than folding it into
        //ProductionGraphViewer_KeyDown itself, since it needs to reach the toolbar's gridlines checkbox.
        private void OnGraphCanvasKeyDown(object? sender, KeyEventArgs e) {
            //OnKeyDown does not mark panel-bound keys Handled: Space typed in the search box reaches this subscription, so the guard stays
            if (GraphCanvas.IsPanelOpen)
                return;
            if (e.Key != Key.Space)
                return;

            GraphCanvas.Grid.ShowGrid = !GraphCanvas.Grid.ShowGrid;
            gridlinesCheckBox.IsChecked = GraphCanvas.Grid.ShowGrid;
            GraphCanvas.InvalidateVisual();
            e.Handled = true;
        }

        internal int TitleBarDragRequestCount { get; private set; }

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

        private NativeMenuItem showGridlinesMenuItem = null!;
        private NativeMenuItem iconViewMenuItem = null!;
        private NativeMenuItem pauseCalculationsMenuItem = null!;
        private NativeMenuItem showInspectorMenuItem = null!;
        private NativeMenuItem pinInspectorMenuItem = null!;
        private NativeMenuItem[] minorGridlinesMenuItems = [];
        private NativeMenuItem[] majorGridlinesMenuItems = [];
        private NativeMenuItem[] rateUnitMenuItems = [];

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

        private void MenuCut() { GraphCanvas.CutSelection(); GraphCanvas.RequestRedraw(); }

        private void MenuCopy() => GraphCanvas.CopySelection();

        private void MenuPaste() => GraphCanvas.PasteFromClipboard();

        private void MenuDelete() { GraphCanvas.DeleteSelection(); GraphCanvas.RequestRedraw(); }

        //A menu key equivalent fires even while a text field has focus, so the field keeps ⌘A as its own select-all.
        private void MenuSelectAll() {
            if (FocusManager?.GetFocusedElement() is TextBox field) {
                field.SelectAll();
                return;
            }
            GraphCanvas.Viewer.SetSelection(GraphCanvas.Viewer.NodeElements);
            GraphCanvas.RequestRedraw();
        }

        private void MenuDeselectAll() { GraphCanvas.Viewer.ClearSelection(); GraphCanvas.RequestRedraw(); }

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
            SetCalculationsPaused(!GraphCanvas.Viewer.IsPaused);
            pauseUpdatesCheckBox.IsChecked = GraphCanvas.Viewer.IsPaused;
            ResyncNativeMenuCheckedStates();
            GraphCanvas.InvalidateVisual();
        }

        private void MenuToggleShowInspector() {
            ViewModel.Inspector.ShowOverride = !ViewModel.Inspector.ShowOverride;
            ResyncNativeMenuCheckedStates();
        }

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

        private void MenuMinimize() => WindowState = WindowState.Minimized;

        private void MenuZoomWindow() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void MenuBringAllToFront() => Activate();

        private const string RepositoryUrl = "https://github.com/idk-some-guy/Foreman2---Unspoiled";

        internal static string ReportIssueUrl() {
            string body = $"Foreman 2 {AppVersion.ShortSemVer} · macOS {Environment.OSVersion.Version} · {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}\n\n";
            return $"{RepositoryUrl}/issues/new?body={Uri.EscapeDataString(body)}";
        }

        private void OpenInBrowser(string url) => Async.Fire(OpenInBrowserAsync(url), nameof(OpenInBrowserAsync));

        private async Task OpenInBrowserAsync(string url) {
            if (await (LauncherOverride ?? Launcher).LaunchUriAsync(new Uri(url)).ConfigureAwait(true))
                return;
            var alert = new AlertSheet("Couldn’t open your web browser.", url, AlertButtons.YesNo, verb: "Copy Link", secondaryLabel: "OK");
            if (await SheetHost.For(this).ShowAsync(alert).ConfigureAwait(true) == ConfirmChoice.Yes && Clipboard is { } clipboard)
                await clipboard.SetTextAsync(url).ConfigureAwait(true);
        }

        private void MenuToggleShowToolbar() {
            searchRevealsToolbar = false;
            SetShowToolbar(!showToolbar);
            if (Settings is AppSettings current)
                SettingsService?.Save(current);
        }

        private void SetShowToolbar(bool value) {
            showToolbar = value;
            if (Settings is AppSettings current)
                current.ShowToolbar = value;
            if (showToolbarMenuItem is not null)
                showToolbarMenuItem.IsChecked = value;
            ApplyToolbarShown();
        }

        private const double ToolbarTitleBarHeight = 52;
        private const double PlainTitleBarHeight = 28;

        private void ApplyToolbarShown() {
            bool shown = showToolbar || searchRevealsToolbar;
            double height = shown ? ToolbarTitleBarHeight : PlainTitleBarHeight;
            double growth = ExtendClientAreaTitleBarHeightHint - height;
            if (growth != 0 && GraphCanvas.Bounds.Height > 0) {
                Viewport viewport = GraphCanvas.Viewport;
                viewport.ViewOffset = new System.Drawing.Point(viewport.ViewOffset.X, viewport.ViewOffset.Y + (int)Math.Round(growth / 2 / viewport.ViewScale));
                GraphCanvas.RequestRedraw();
            }
            ExtendClientAreaTitleBarHeightHint = height;
            toolbar.IsVisible = shown;
            titleBarTitle.IsVisible = !shown;
            if (titleBarSolveStatus is not null) {
                titleBarSolveStatus.IsVisible = !shown;
                (shown ? titleBarSolveStatus : toolbar.ZoomStepper.SolveStatus).IsErrorPopoverOpen = false;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == IsActiveProperty)
                titleBarTitle?.Classes.Set("secondary", !IsActive);
        }

        private WindowState stateBeforeFullScreen = WindowState.Maximized;

        private void MenuToggleFullScreen() {
            if (WindowState == WindowState.FullScreen) {
                WindowState = stateBeforeFullScreen;
                return;
            }
            stateBeforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
        }

        private PresetComparatorWindow? presetComparatorWindow;

        private List<Preset>? ComparablePresets() =>
            Settings is AppSettings current && PresetResolver is Services.PresetResolver presetResolver
                && presetResolver.BuildPresetList(current.CurrentPresetName) is { } presets && PresetComparatorWindow.CanCompare(presets)
                ? presets : null;

        private void ShowPresetComparator() {
            if (ComparablePresets() is not { } presets)
                return;
            if (PresetComparatorDialogStub is { } stub) {
                stub(presets);
                return;
            }
            if (presetComparatorWindow is { } existing) {
                existing.Show(this);
                existing.Activate();
                return;
            }
            var window = new PresetComparatorWindow(presets) {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = Position + new PixelPoint(50, 50),
            };
            window.Closed += (_, _) => {
                if (ReferenceEquals(presetComparatorWindow, window))
                    presetComparatorWindow = null;
            };
            presetComparatorWindow = window;
            window.Show(this);
        }

        internal static readonly string[] GridlineLabels = ["None", "1", "2", "4", "8", "16", "32", "64", "128", "256", "512", "1024"];
        private static readonly string[] RateUnitLabels = ["1 sec", "1 min", "5 min", "10 min", "30 min", "1 hour"];
        internal static readonly string[] RateUnitOptions = ["per second", "per minute", "per 5 minutes", "per 10 minutes", "per 30 minutes", "per hour"];

        internal static NativeMenu BuildAppMenu() {
            var menu = new NativeMenu();
            menu.Items.Add(WindowActionItem("About Foreman 2", w => w.ShowAbout()));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(WindowActionItem("Settings…", w => w.Commands.SettingsCommand.Execute(null), new KeyGesture(Key.OemComma, PlatformModifiers.Primary)));
            menu.Items.Add(new NativeMenuItemSeparator());
            foreach (NativeMenuItemBase item in ShellHost.BuildNoWindowItems())
                menu.Items.Add(item);
            menu.Items.Add(new NativeMenuItem("Hide Foreman 2") { Command = new DeferredMenuCommand(new RelayCommand(ShellHost.HideForeman)), Gesture = new KeyGesture(Key.H, PlatformModifiers.Primary) });
            menu.Items.Add(DisabledItem("Hide Others", new KeyGesture(Key.H, PlatformModifiers.Primary | KeyModifiers.Alt)));
            menu.Items.Add(DisabledItem("Show All"));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(QuitMenuItem());
            menu.Opening += (_, _) => ShellHost.Sync();
            return menu;
        }

        private static NativeMenuItem WindowActionItem(string header, Action<MainWindow> action, KeyGesture? gesture = null) =>
            new(header) { Command = new DeferredMenuCommand(new RelayCommand(() => Async.Fire(RunOnLiveWindowAsync(action), header))), Gesture = gesture };

        private static async Task RunOnLiveWindowAsync(Action<MainWindow> action) {
            if (await ShellHost.LiveWindowAsync().ConfigureAwait(true) is { } window)
                action(window);
        }

        private NativeMenu BuildNativeMenu() {
            presetComparatorCommand = new RelayCommand(ShowPresetComparator, () => ComparablePresets() is not null);
            revertToLastSavedCommand = new RelayCommand(RevertToLastSaved, () => Library?.OpenGraphSavedAt is not null);
            NativeMenuItem save = MenuItem("Save", Commands.SaveCommand, new KeyGesture(Key.S, PlatformModifiers.Primary));
            NativeMenuItem duplicate = ActionItem("Duplicate", DuplicateGraph, new KeyGesture(Key.S, PlatformModifiers.Primary | KeyModifiers.Shift));
            NativeMenuItem rename = ActionItem("Rename…", () => Async.Fire(RenameGraphAsync(), nameof(RenameGraphAsync)));
            var revertMenu = new NativeMenu {
                Items = {
                    new NativeMenuItem("Last Saved Version") { Command = new DeferredMenuCommand(revertToLastSavedCommand) },
                    new NativeMenuItemSeparator(),
                    ActionItem("Browse Versions…", () => Async.Fire(BrowseVersionsAsync(), nameof(BrowseVersionsAsync))),
                },
            };
            NativeMenuItem revert = new("Revert To") { Menu = revertMenu };
            NativeMenuItem exportFjson = ActionItem("Export as .fjson…", () => Async.Fire(ExportGraphFileAsync(), nameof(ExportGraphFileAsync)));
            NativeMenuItem exportImage = MenuItem("Export Image…", Commands.ExportImageCommand, new KeyGesture(Key.E, PlatformModifiers.Primary | KeyModifiers.Shift));
            NativeMenuItem close = ActionItem("Close", Close, new KeyGesture(Key.W, PlatformModifiers.Primary));
            openRecentItem = new NativeMenuItem("Open Recent") { IsEnabled = false, Menu = [] };
            syncResearchMenuItem = MenuItem("Sync Research…", Commands.SyncResearchCommand, new KeyGesture(Key.R, PlatformModifiers.Primary));
            syncResearchFromAnotherMenuItem = MenuItem("Sync Research From Another Save…", Commands.SyncResearchFromAnotherSaveCommand, new KeyGesture(Key.R, PlatformModifiers.Primary | KeyModifiers.Shift));
            var fileMenu = new NativeMenu {
                Items = {
                    MenuItem("New Graph", Commands.NewCommand, new KeyGesture(Key.N, PlatformModifiers.Primary)),
                    MenuItem("Open…", Commands.LoadCommand, new KeyGesture(Key.O, PlatformModifiers.Primary)),
                    openRecentItem,
                    new NativeMenuItemSeparator(),
                    close,
                    save,
                    duplicate,
                    rename,
                    revert,
                    new NativeMenuItemSeparator(),
                    syncResearchMenuItem,
                    syncResearchFromAnotherMenuItem,
                    new NativeMenuItemSeparator(),
                    MenuItem("Import Graph…", Commands.ImportCommand),
                    exportFjson,
                    exportImage,
                },
            };
            fileMenu.Opening += (_, _) => RefreshLibraryMenus();

            selectNextNodeCommand = new RelayCommand(() => GraphCanvas.StepNode(forward: true), CanSelectNode);
            selectPreviousNodeCommand = new RelayCommand(() => GraphCanvas.StepNode(forward: false), CanSelectNode);
            selectNextNodeItem = new NativeMenuItem("Select Next Node") { Command = new DeferredMenuCommand(selectNextNodeCommand), IsEnabled = false };
            selectPreviousNodeItem = new NativeMenuItem("Select Previous Node") { Command = new DeferredMenuCommand(selectPreviousNodeCommand), IsEnabled = false };
            var findMenu = new NativeMenu {
                Items = {
                    ActionItem("Find Node…", FocusSearchField, new KeyGesture(Key.F, PlatformModifiers.Primary)),
                    ActionItem("Find Next", () => StepSearchMatch(forward: true), new KeyGesture(Key.G, PlatformModifiers.Primary)),
                    ActionItem("Find Previous", () => StepSearchMatch(forward: false), new KeyGesture(Key.G, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    new NativeMenuItemSeparator(),
                    selectNextNodeItem,
                    selectPreviousNodeItem,
                },
            };

            //Cut/Copy/Paste/Delete carry no gesture: GraphCanvasControl owns Cmd+C/X/V/Delete, and a second binding would steal or double-fire
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
                    ActionItem("Select All", MenuSelectAll, new KeyGesture(Key.A, PlatformModifiers.Primary)),
                    ActionItem("Deselect All", MenuDeselectAll, new KeyGesture(Key.A, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    new NativeMenuItemSeparator(),
                    new NativeMenuItem("Find") { Menu = findMenu },
                },
            };

            minorGridlinesMenuItems = RadioItems(GridlineLabels, MenuSetMinorGridlines, Array.IndexOf(GridlineLabels, minorGridlinesPopUp.SelectedItem));
            majorGridlinesMenuItems = RadioItems(GridlineLabels, MenuSetMajorGridlines, Array.IndexOf(GridlineLabels, majorGridlinesPopUp.SelectedItem));
            rateUnitMenuItems = RadioItems(RateUnitLabels, MenuSetRateUnit, Array.IndexOf(RateUnitOptions, rateOptionsPopUp.SelectedItem));

            //A NativeMenu gesture on an unmodified key swallows every space typed in a text field
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

            showToolbarMenuItem = new NativeMenuItem("Show Toolbar") {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = showToolbar,
                Gesture = new KeyGesture(Key.T, PlatformModifiers.Primary | KeyModifiers.Alt),
                Command = new DeferredMenuCommand(new RelayCommand(MenuToggleShowToolbar)),
            };

            roundedRoutingMenuItem = RoutingItem("Rounded", LinkRouting.Rounded);
            orthogonalRoutingMenuItem = RoutingItem("Orthogonal", LinkRouting.Orthogonal);
            clearWaypointsMenuItem = ActionItem("Clear Waypoints", MenuClearWaypoints);
            clearWaypointsMenuItem.IsEnabled = false;
            var linkRoutingMenu = new NativeMenu {
                Items = {
                    roundedRoutingMenuItem,
                    orthogonalRoutingMenuItem,
                    new NativeMenuItemSeparator(),
                    clearWaypointsMenuItem,
                },
            };
            linkRoutingMenu.Opening += (_, _) => UpdateLinkRoutingMenu();
            GraphCanvas.Viewer.SelectionChanged += (_, _) => UpdateLinkRoutingMenu();

            NativeMenuItemBase[] canvasHalf = [
                MenuItem("Zoom In", ZoomInCommand, new KeyGesture(Key.OemPlus, PlatformModifiers.Primary)),
                MenuItem("Zoom Out", ZoomOutCommand, new KeyGesture(Key.OemMinus, PlatformModifiers.Primary)),
                ActionItem("Actual Size", MenuActualSize, new KeyGesture(Key.D0, PlatformModifiers.Primary)),
                ActionItem("Fit to Graph", MenuFitToGraph, new KeyGesture(Key.D0, PlatformModifiers.Primary | KeyModifiers.Shift)),
                new NativeMenuItemSeparator(),
                showGridlinesMenuItem,
                new NativeMenuItem("Minor Gridlines") { Menu = SubmenuFrom(minorGridlinesMenuItems) },
                new NativeMenuItem("Major Gridlines") { Menu = SubmenuFrom(majorGridlinesMenuItems) },
                new NativeMenuItem("Rate Unit") { Menu = SubmenuFrom(rateUnitMenuItems) },
                new NativeMenuItem("Link Routing") { Menu = linkRoutingMenu },
                iconViewMenuItem,
                pauseCalculationsMenuItem,
                new NativeMenuItemSeparator(),
                showInspectorMenuItem,
                pinInspectorMenuItem,
            ];
            var viewMenu = new NativeMenu();
            viewMenu.Opening += (_, _) => UpdateLinkRoutingMenu();
            foreach (NativeMenuItemBase item in canvasHalf)
                viewMenu.Items.Add(item);
            viewMenu.Items.Insert(viewMenu.Items.IndexOf(pauseCalculationsMenuItem) + 1, showSolveErrorMenuItem);
            viewMenu.Items.Add(new NativeMenuItemSeparator());
            viewMenu.Items.Add(showToolbarMenuItem);
            viewMenu.Items.Add(DisabledItem("Customize Toolbar…"));
            viewMenu.Items.Add(ActionItem("Enter Full Screen", MenuToggleFullScreen, EnterFullScreenGesture()));

            var windowMenu = new NativeMenu {
                Items = {
                    ActionItem("Minimize", MenuMinimize, new KeyGesture(Key.M, PlatformModifiers.Primary)),
                    ActionItem("Zoom", MenuZoomWindow),
                    new NativeMenuItemSeparator(),
                    ActionItem("Welcome to Foreman", ShowWelcome, new KeyGesture(Key.D1, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    new NativeMenuItem("Preset Comparator") { Command = new DeferredMenuCommand(presetComparatorCommand) },
                    new NativeMenuItemSeparator(),
                    ActionItem("Bring All to Front", MenuBringAllToFront),
                },
            };

            windowMenu.Opening += (_, _) => presetComparatorCommand.NotifyCanExecuteChanged();

            var helpMenu = new NativeMenu {
                Items = {
                    ActionItem("Foreman 2 Help", () => OpenInBrowser(RepositoryUrl + "#readme"), new KeyGesture(Key.OemQuestion, PlatformModifiers.Primary | KeyModifiers.Shift)),
                    ActionItem("Keyboard Shortcuts", () => OpenInBrowser(RepositoryUrl + "/blob/main/docs/keyboard-shortcuts.md")),
                    new NativeMenuItemSeparator(),
                    ActionItem("Foreman 2 on GitHub", () => OpenInBrowser(RepositoryUrl)),
                    ActionItem("Report an Issue…", () => OpenInBrowser(ReportIssueUrl())),
                },
            };

            documentScopedMenuItems = [.. WithSubmenuItems([save, duplicate, rename, revert, exportFjson, exportImage, close, .. editMenu.Items, .. canvasHalf])];

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

        private static KeyGesture? EnterFullScreenGesture() =>
            PlatformModifiers.Primary == KeyModifiers.Meta ? new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control) : null;

        private static IEnumerable<NativeMenuItem> WithSubmenuItems(IEnumerable<NativeMenuItemBase> items) {
            foreach (NativeMenuItem item in items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator)) {
                yield return item;
                if (item.Menu is NativeMenu submenu)
                    foreach (NativeMenuItem nested in WithSubmenuItems(submenu.Items))
                        yield return nested;
            }
        }

        private NativeMenuItem[] documentScopedMenuItems = [];
        private readonly Dictionary<NativeMenuItem, bool> enabledBeforeNoDocument = [];
        private bool isDocumentOpen = true;

        internal bool IsDocumentOpen {
            get => isDocumentOpen;
            set {
                if (isDocumentOpen == value)
                    return;
                isDocumentOpen = value;
                UpdateSyncResearchState();
                if (value) {
                    foreach ((NativeMenuItem item, bool enabled) in enabledBeforeNoDocument)
                        item.IsEnabled = enabled;
                    enabledBeforeNoDocument.Clear();
                    UpdateLinkRoutingMenu();
                    UpdateSelectNodeMenuItems();
                    return;
                }
                foreach (NativeMenuItem item in documentScopedMenuItems) {
                    enabledBeforeNoDocument[item] = item.IsEnabled;
                    item.IsEnabled = false;
                }
            }
        }

        internal void RefreshLibraryMenus() {
            revertToLastSavedCommand.NotifyCanExecuteChanged();
            UpdateSyncResearchState();
            RefreshOpenRecentMenu();
        }

        internal void RefreshOpenRecentMenu() {
            openRecentItem.Menu = BuildOpenRecentMenu(Library, Settings, PresetResolver,
                id => Async.Fire(OpenLibraryGraphAsync(id), nameof(OpenLibraryGraphAsync)), ClearOpenRecent);
            openRecentItem.IsEnabled = Library?.All().Count > 0;
        }

        internal static NativeMenu BuildOpenRecentMenu(GraphLibraryService? library, AppSettings? settings, PresetResolver? resolver, Action<Guid> open, Action clear) {
            IReadOnlyList<GraphRow> rows = library?.OpenRecent(settings?.OpenRecentClearedAt) ?? [];
            HashSet<string> installed = resolver is not null && settings is not null
                ? [.. resolver.BuildPresetList(settings.CurrentPresetName).Select(p => p.Name)]
                : [];
            var menu = new NativeMenu();
            foreach (GraphRow row in rows) {
                bool missing = resolver is not null && !installed.Contains(row.PresetName);
                Guid id = row.Id;
                menu.Items.Add(new NativeMenuItem(rows.Count(r => r.Name == row.Name) > 1 ? $"{row.Name} — {row.PresetName}" : row.Name) {
                    Command = new DeferredMenuCommand(new RelayCommand(() => open(id))),
                    IsEnabled = !missing,
                    ToolTip = missing ? $"Needs the “{row.PresetName}” preset" : null,
                });
            }
            if (rows.Count > 0)
                menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(new NativeMenuItem("Clear Menu") { Command = new DeferredMenuCommand(new RelayCommand(clear)), IsEnabled = rows.Count > 0 });
            return menu;
        }

        private void ClearOpenRecent() {
            if (Settings is not AppSettings current || Library is null)
                return;
            current.OpenRecentClearedAt = Library.Now;
            SettingsService?.Save(current);
            RefreshOpenRecentMenu();
        }

        private static NativeMenuItem MenuItem(string header, IRelayCommand command, KeyGesture? gesture = null) =>
            new(header) { Command = new DeferredMenuCommand(command), IsEnabled = command.CanExecute(null), Gesture = gesture };

        private static NativeMenuItem ActionItem(string header, Action action, KeyGesture? gesture = null) =>
            new(header) { Command = new DeferredMenuCommand(new RelayCommand(action)), Gesture = gesture };

        private NativeMenuItem RoutingItem(string header, LinkRouting routing) =>
            new(header) {
                ToggleType = MenuItemToggleType.Radio,
                Command = new DeferredMenuCommand(new RelayCommand(() => MenuSetLinkRouting(routing))),
                IsEnabled = false,
            };

        private void MenuSetLinkRouting(LinkRouting routing) {
            GraphCanvas.Viewer.SetSelectedLinksRouting(routing);
            UpdateLinkRoutingMenu();
            GraphCanvas.RequestRedraw();
        }

        private void MenuClearWaypoints() {
            GraphCanvas.Viewer.ClearSelectedWaypoints();
            UpdateLinkRoutingMenu();
            GraphCanvas.RequestRedraw();
        }

        internal void UpdateLinkRoutingMenu() {
            if (roundedRoutingMenuItem is null || orthogonalRoutingMenuItem is null || clearWaypointsMenuItem is null)
                return;

            HashSet<LinkElement> selected = GraphCanvas.Viewer.SelectedLinks;
            bool any = isDocumentOpen && selected.Count > 0;
            roundedRoutingMenuItem.IsEnabled = any;
            orthogonalRoutingMenuItem.IsEnabled = any;
            roundedRoutingMenuItem.IsChecked = any && selected.All(link => link.ViewModel.Routing == LinkRouting.Rounded);
            orthogonalRoutingMenuItem.IsChecked = any && selected.All(link => link.ViewModel.Routing == LinkRouting.Orthogonal);
            clearWaypointsMenuItem.IsEnabled = isDocumentOpen && selected.Any(link => link.ViewModel.Waypoints.Count > 0);
        }

        private static NativeMenuItem DisabledItem(string header, KeyGesture? gesture = null) =>
            new(header) { IsEnabled = false, Gesture = gesture };

        private static NativeMenuItem[] RadioItems(string[] labels, Action<int> onSelect, int currentIndex) {
            var command = new DeferredMenuCommand(new RelayCommand<int>(onSelect));
            var items = new NativeMenuItem[labels.Length];
            for (int i = 0; i < labels.Length; i++) {
                items[i] = new NativeMenuItem(labels[i]) {
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = i == currentIndex,
                    //CommandParameter must be set before Command, or IsEnabled snapshots CanExecute(null)
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

        private static NativeMenuItem QuitMenuItem() {
            var command = new RelayCommand(() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.TryShutdown());
            return new NativeMenuItem("Quit Foreman 2") {
                Gesture = new KeyGesture(Key.Q, PlatformModifiers.Primary),
                Command = new DeferredMenuCommand(command),
            };
        }

        //Defers Execute past AppKit's synchronous menu dispatch, or an async sheet strands the menu open
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
