using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProgressBar = Foreman.Mac.Kit.Controls.ProgressBar;

namespace Foreman.Mac.Views {
    internal sealed partial class WelcomeWindow : Window, IThemeAware {
        private const double BaseWidth = 660;
        private const double BaseHeight = 420;

        private static (double Width, double Height)? rememberedSize;

        private readonly WelcomeHost host;
        private readonly WindowMinSizeScale minSizeScale;
        private readonly Border leftPane;
        private readonly Button newGraphButton;
        private readonly Button openButton;
        private readonly TextBlock groupTitle;
        private readonly ListBox presetList;
        private readonly CheckBox showAtLaunchCheckBox;
        private readonly ListBox recentList;
        private readonly SearchField filterField;
        private readonly TextBlock countText;
        private readonly StatePanel emptyPanel;
        private readonly StatePanel noResultsPanel;
        private readonly StatePanel errorPanel;
        private readonly Border loadingStrip;
        private readonly TextBlock loadingSentence;
        private readonly ProgressBar loadingBar;
        private readonly Button cancelButton;
        private readonly Grid actionsGroup;
        private readonly Grid recentsGroup;
        private double appliedScale = 1;
        private bool initializing = true;

        internal WelcomeWindow(WelcomeHost host) {
            this.host = host;
            InitializeComponent();
            ViewModel = new WelcomeViewModel(host);
            leftPane = this.FindControl<Border>("LeftPane")!;
            newGraphButton = this.FindControl<Button>("NewGraphButton")!;
            openButton = this.FindControl<Button>("OpenButton")!;
            groupTitle = this.FindControl<TextBlock>("GroupTitle")!;
            presetList = this.FindControl<ListBox>("PresetList")!;
            showAtLaunchCheckBox = this.FindControl<CheckBox>("ShowAtLaunchCheckBox")!;
            recentList = this.FindControl<ListBox>("RecentList")!;
            filterField = this.FindControl<SearchField>("FilterField")!;
            countText = this.FindControl<TextBlock>("CountText")!;
            emptyPanel = this.FindControl<StatePanel>("EmptyPanel")!;
            noResultsPanel = this.FindControl<StatePanel>("NoResultsPanel")!;
            errorPanel = this.FindControl<StatePanel>("ErrorPanel")!;
            loadingStrip = this.FindControl<Border>("LoadingStrip")!;
            loadingSentence = this.FindControl<TextBlock>("LoadingSentence")!;
            loadingBar = this.FindControl<ProgressBar>("LoadingBar")!;
            cancelButton = this.FindControl<Button>("CancelButton")!;
            actionsGroup = this.FindControl<Grid>("ActionsGroup")!;
            recentsGroup = this.FindControl<Grid>("RecentsGroup")!;

            BuildMenu();
            this.FindControl<TextBlock>("VersionText")!.Text = AppVersion.VersionedDisplay;
            AutomationProperties.SetName(actionsGroup, "Actions and presets");
            newGraphButton.Content = ActionContent("New", "New Graph", "N");
            openButton.Content = ActionContent("Open", "Open…", "O");
            AutomationProperties.SetName(newGraphButton, "New Graph");
            AutomationProperties.SetName(openButton, "Open…");
            newGraphButton.Click += (_, _) => Async.Fire(NewGraphFromMenuAsync(), nameof(NewGraphFromMenuAsync));
            openButton.Click += (_, _) => Async.Fire(OpenFileFromMenuAsync(), nameof(OpenFileFromMenuAsync));
            cancelButton.Click += (_, _) => ViewModel.CancelLoad();

            presetList.ItemsSource = ViewModel.Presets;
            presetList.ItemTemplate = new FuncDataTemplate<Preset>((preset, _) => {
                var label = new TextBlock { Theme = KitTheme("Kit.Text"), Text = preset.Name, VerticalAlignment = VerticalAlignment.Center };
                label.Classes.Add("callout");
                label.Classes.Add("middle-ellipsis");
                return label;
            }, supportsRecycling: false);
            presetList.SelectedItem = ViewModel.SelectedPreset;
            presetList.SelectionChanged += OnPresetSelectionChanged;

            showAtLaunchCheckBox.IsChecked = host.Settings.ShowWelcomeAtLaunch;
            showAtLaunchCheckBox.IsCheckedChanged += (_, _) => {
                if (initializing)
                    return;
                host.Settings.ShowWelcomeAtLaunch = showAtLaunchCheckBox.IsChecked == true;
                host.SaveSettings();
            };

            recentList.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
            recentList.ItemsSource = ViewModel.Rows;
            recentList.ItemTemplate = new FuncDataTemplate<WelcomeRow>((row, _) => row is null ? null : BuildRecentRow(row), supportsRecycling: false);
            recentList.DoubleTapped += (_, _) => OpenSelected();
            recentList.KeyDown += OnRecentKeyDown;

            filterField.PropertyChanged += (_, e) => {
                if (e.Property == TextBox.TextProperty)
                    ViewModel.FilterText = filterField.Text ?? "";
            };
            BuildPanels();

            ViewModel.PropertyChanged += OnViewModelChanged;
            ViewModel.Rows.CollectionChanged += (_, _) => ApplyState();
            AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
            minSizeScale = new WindowMinSizeScale(this);
            ApplySize();
            ApplyState();
            initializing = false;

            AppearanceHub.Current?.Subscribe(this);
            Closing += (_, _) => RememberSize();
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        internal WelcomeViewModel ViewModel { get; }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void RefreshTheme() {
            minSizeScale.Apply();
            RescaleSize();
            ViewModel.Refresh();
        }

        private void ApplySize() {
            (double width, double height) = rememberedSize ?? (BaseWidth, BaseHeight);
            appliedScale = WindowScaler.CurrentScale;
            Width = Math.Max(width, BaseWidth) * appliedScale;
            Height = Math.Max(height, BaseHeight) * appliedScale;
        }

        private void RescaleSize() {
            double scale = WindowScaler.CurrentScale;
            if (scale == appliedScale)
                return;
            Width = ClientSize.Width / appliedScale * scale;
            Height = ClientSize.Height / appliedScale * scale;
            appliedScale = scale;
        }

        internal static void ForgetSize() => rememberedSize = null;

        private void RememberSize() {
            if (ClientSize.Width > 0 && ClientSize.Height > 0)
                rememberedSize = (ClientSize.Width / appliedScale, ClientSize.Height / appliedScale);
        }

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"WelcomeWindow: kit theme '{key}' not found.");
        }

        private static Grid ActionContent(string glyph, string label, string key) {
            Application.Current!.TryGetResource($"Kit.Glyph.{glyph}.16", null, out object? geometry);
            var path = new Avalonia.Controls.Shapes.Path { Theme = KitTheme("Kit.Glyph"), Data = geometry as Geometry };
            path.Classes.Add("welcome-glyph");
            var box = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = path, VerticalAlignment = VerticalAlignment.Center };
            IObservable<object?> size = box.GetResourceObservable("Foreman.Size.Glyph16");
            box.Bind(WidthProperty, size);
            box.Bind(HeightProperty, size);

            var name = new TextBlock { Theme = KitTheme("Kit.Text"), Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
            name.Classes.Add("body");
            string chord = OperatingSystem.IsMacOS() ? "⌘" + key : "Ctrl+" + key;
            var shortcut = new TextBlock { Theme = KitTheme("Kit.Text"), Text = chord, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            shortcut.Classes.Add("caption");
            shortcut.Classes.Add("tertiary");

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(shortcut, 2);
            grid.Children.Add(box);
            grid.Children.Add(name);
            grid.Children.Add(shortcut);
            return grid;
        }

        private void BuildPanels() {
            emptyPanel.Shape = StatePanelShape.Empty;
            emptyPanel.Headline = "No graphs yet";
            emptyPanel.Sentence = "Graphs you create or open appear here. New graphs use the preset chosen on the left.";
            Button emptyNew = SheetButtons.Make("New Graph", ButtonRowRole.Default);
            emptyNew.IsDefault = true;
            emptyNew.Click += (_, _) => Async.Fire(NewGraphFromMenuAsync(), nameof(NewGraphFromMenuAsync));
            emptyPanel.Buttons = SheetButtons.Row(emptyNew);
            EmptyNewGraphButton = emptyNew;

            noResultsPanel.Shape = StatePanelShape.NoResults;
            noResultsPanel.ForceCompact = true;
            Button clear = SheetButtons.Make("Clear Filter", ButtonRowRole.Default);
            clear.Click += (_, _) => {
                filterField.Text = "";
                ViewModel.FilterText = "";
            };
            noResultsPanel.Buttons = SheetButtons.Row(clear);

            errorPanel.Shape = StatePanelShape.Error;
            errorPanel.Sentence = "Its data file is missing or unreadable. Choose another preset on the left, or try again.";
            Button tryAgain = SheetButtons.Make("Try Again", ButtonRowRole.Default);
            tryAgain.IsDefault = true;
            tryAgain.Click += (_, _) => Async.Fire(ViewModel.TryAgainAsync(), nameof(WelcomeViewModel.TryAgainAsync));
            errorPanel.Buttons = SheetButtons.Row(tryAgain);
        }

        private Button EmptyNewGraphButton { get; set; } = null!;

        private RecentRow BuildRecentRow(WelcomeRow row) {
            var recent = new RecentRow {
                Theme = KitTheme("Kit.RecentRow"),
                Label = row.Name,
                PresetLabel = row.PresetLabel,
                UnavailableMeta = row.UnavailableMeta,
                HelpHints = "Delete Graph: Command Delete.",
                NodeCount = row.NodeCount,
                RelativeTime = row.RelativeTime,
                AbsoluteTime = row.AbsoluteTime,
                IsUnavailable = row.Unavailable,
                Focusable = false,
                DataContext = row,
            };
            recent.Icon = ThumbnailBrush(row.Thumbnail, recent);
            foreach (MenuRow item in BuildRowMenu(row))
                recent.MenuItems.Add(item);
            recent.AttachedToVisualTree += (_, _) => {
                if (recent.FindAncestorOfType<ListBoxItem>() is { } container)
                    recent.Bind(RecentRow.IsSelectedProperty, container.GetObservable(ListBoxItem.IsSelectedProperty));
                recent.Bind(RecentRow.IsKeyProperty, this.GetObservable(IsActiveProperty));
            };
            return recent;
        }

        internal IReadOnlyList<MenuRow> BuildRowMenu(WelcomeRow row) {
            MenuRow Item(string text, Func<Task> action) => new() {
                Text = text,
                HasCheckSlot = false,
                Activate = () => Async.Fire(action(), text),
            };

            List<MenuRow> items = [
                Item("Open", () => OpenRowAsync(row)),
                Item("Duplicate", () => DuplicateRowAsync(row)),
                Item("Rename…", () => RenameRowAsync(row)),
                Item("Export as .fjson…", () => ExportRowAsync(row)),
            ];
            if (row.Unavailable)
                items.Add(Item("Choose Preset…", () => ChoosePresetAsync(row)));
            items.Add(MenuRow.Separator());
            items.Add(Item("Delete Graph…", () => DeleteRowAsync(row)));
            return items;
        }

        internal static IBrush ThumbnailBrush(byte[]? png, Control owner) {
            if (png is { Length: > 0 }) {
                try {
                    using var stream = new MemoryStream(png);
                    return new ImageBrush(new Bitmap(stream)) { Stretch = Stretch.UniformToFill };
                } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) {
                    ErrorLogging.LogException(ex, "Welcome: couldn't read a graph thumbnail");
                }
            }

            Application.Current!.TryGetResource("Kit.Glyph.EmptyGraph.32", null, out object? geometry);
            var stroke = new SolidColorBrush();
            LiveBrush.Observe(owner, "Foreman.Color.TextTertiary", resolved => stroke.Color = ((ISolidColorBrush)resolved).Color);
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 32, 32)), Brush = Avalonia.Media.Brushes.Transparent });
            group.Children.Add(new GeometryDrawing { Geometry = geometry as Geometry, Pen = new Pen(stroke, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round) });
            return new DrawingBrush(group) { Stretch = Stretch.Uniform };
        }

        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();

        private void ApplyState() {
            bool failed = ViewModel.Phase == WelcomeLoadPhase.Failed;
            bool loading = ViewModel.Phase == WelcomeLoadPhase.Loading;
            countText.Text = ViewModel.TotalCount.ToString(CultureInfo.CurrentCulture);
            AutomationProperties.SetName(recentsGroup, $"Recent graphs, {ViewModel.TotalCount} {(ViewModel.TotalCount == 1 ? "graph" : "graphs")}");
            filterField.IsVisible = ViewModel.ShowFilter;
            recentList.IsVisible = ViewModel.TotalCount > 0 && !failed && !ViewModel.HasNoMatches;
            emptyPanel.IsVisible = ViewModel.IsEmpty && !failed;
            noResultsPanel.IsVisible = ViewModel.HasNoMatches && !failed;
            noResultsPanel.Headline = $"No graphs match “{ViewModel.FilterText.Trim()}”";
            errorPanel.IsVisible = failed;
            errorPanel.Headline = $"Couldn’t load “{Dialogs.MiddleTruncate(ViewModel.FailedPresetName)}”";

            SetStripShown(loading);
            loadingSentence.Text = ViewModel.LoadSentence;
            loadingBar.Value = ViewModel.LoadPercent;
            AutomationProperties.SetHelpText(loadingBar, ViewModel.LoadSentence);

            bool enabled = ViewModel.ActionsEnabled;
            newGraphButton.IsEnabled = enabled;
            openButton.IsEnabled = enabled;
            EmptyNewGraphButton.IsEnabled = enabled;
            SyncMenu();
            if (ReferenceEquals(WelcomeLauncher.Current, this))
                ShellHost.Sync();
        }

        private NativeMenuItem[] menuActions = [];
        private NativeMenuItem openRecentItem = new("Open Recent");

        private void BuildMenu() {
            NativeMenuItem Action(string header, Func<Task> run, KeyGesture gesture) => new(header) {
                Command = new MainWindow.DeferredMenuCommand(new RelayCommand(() => Async.Fire(run(), header))),
                Gesture = gesture,
            };

            openRecentItem = new NativeMenuItem("Open Recent") { Menu = [] };
            menuActions = [
                Action("New Graph", NewGraphFromMenuAsync, new KeyGesture(Key.N, PlatformModifiers.Primary)),
                Action("Open…", OpenFileFromMenuAsync, new KeyGesture(Key.O, PlatformModifiers.Primary)),
            ];
            var file = new NativeMenu { Items = { menuActions[0], menuActions[1], openRecentItem } };
            var welcome = new NativeMenuItem("Welcome to Foreman") {
                Command = new MainWindow.DeferredMenuCommand(new RelayCommand(Activate)),
                Gesture = new KeyGesture(Key.D1, PlatformModifiers.Primary | KeyModifiers.Shift),
            };
            var menu = new NativeMenu {
                Items = {
                    new NativeMenuItem("File") { Menu = file },
                    new NativeMenuItem("Window") { Menu = [welcome] },
                },
            };
            menu.Opening += (_, _) => SyncMenu();
            NativeMenu.SetMenu(this, menu);
            SyncMenu();
        }

        private void SyncMenu() {
            if (menuActions.Length == 0)
                return;
            bool enabled = ViewModel.ActionsEnabled;
            foreach (NativeMenuItem item in menuActions)
                item.IsEnabled = enabled;
            openRecentItem.Menu = host.OpenRecentMenu(id => {
                if (host.Library.Find(id) is { } row)
                    Async.Fire(OpenGraphFromMenuAsync(row), nameof(OpenGraphFromMenuAsync));
            }, host.ClearOpenRecent);
            openRecentItem.IsEnabled = enabled && host.Library.All().Count > 0;
        }

        private bool stripShown;
        private int stripVersion;

        private void SetStripShown(bool shown) {
            if (shown == stripShown)
                return;
            stripShown = shown;
            int version = ++stripVersion;
            loadingStrip.Transitions = null;
            if (shown) {
                loadingStrip.IsVisible = true;
                double full = FullStripHeight();
                loadingStrip.Height = Motion.ReduceMotion ? full : 0;
                loadingStrip.Opacity = 0;
                loadingStrip.Transitions = Motion.Transition(TransitionKind.Reflow, Motion.DisclosureDuration);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    if (version != stripVersion)
                        return;
                    loadingStrip.Height = full;
                    loadingStrip.Opacity = 1;
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => {
                        if (version == stripVersion)
                            loadingStrip.Height = double.NaN;
                    }, Motion.DisclosureDuration);
                });
                return;
            }

            loadingStrip.Height = loadingStrip.Bounds.Height;
            loadingStrip.Transitions = Motion.Transition(TransitionKind.Reflow, Motion.DisclosureDuration);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                if (version != stripVersion)
                    return;
                if (!Motion.ReduceMotion)
                    loadingStrip.Height = 0;
                loadingStrip.Opacity = 0;
                Avalonia.Threading.DispatcherTimer.RunOnce(() => {
                    if (version == stripVersion)
                        loadingStrip.IsVisible = false;
                }, Motion.DisclosureDuration);
            });
        }

        private double FullStripHeight() {
            loadingStrip.Height = double.NaN;
            loadingStrip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return loadingStrip.DesiredSize.Height;
        }

        private void OnPresetSelectionChanged(object? sender, SelectionChangedEventArgs e) {
            if (initializing || presetList.SelectedItem is not Preset preset)
                return;
            Async.Fire(ViewModel.SelectPresetAsync(preset), nameof(WelcomeViewModel.SelectPresetAsync));
        }

        internal bool ActionsEnabled => ViewModel.ActionsEnabled;

        private bool menuActionRunning;

        internal bool CanOpenFiles => ViewModel.ActionsEnabled && !menuActionRunning;

        private async Task<bool> RunMenuActionAsync(Func<Task<bool>> action) {
            if (menuActionRunning || !ViewModel.ActionsEnabled)
                return false;
            menuActionRunning = true;
            bool opened;
            try {
                opened = await action().ConfigureAwait(true);
            } finally {
                menuActionRunning = false;
            }
            if (opened)
                Close();
            Async.Fire(ShellHost.OpenPendingFilesAsync(), nameof(ShellHost.OpenPendingFilesAsync));
            return opened;
        }

        internal Task<bool> NewGraphFromMenuAsync() => RunMenuActionAsync(host.NewGraph);

        internal Task<bool> OpenFileFromMenuAsync() => RunMenuActionAsync(host.OpenFile);

        internal Task<bool> OpenPathFromMenuAsync(string path) => RunMenuActionAsync(() => host.OpenPath(path));

        internal Task<bool> OpenGraphFromMenuAsync(GraphRow row) => RunMenuActionAsync(() => host.OpenGraph(row, null));

        internal async Task OpenRowAsync(WelcomeRow row) {
            if (!ViewModel.ActionsEnabled)
                return;
            using IDisposable busy = ShellHost.HoldBusy();
            if (row.Unavailable) {
                string headline = $"“{Dialogs.MiddleTruncate(row.Name)}” can’t be opened.";
                string message = $"Its preset, “{Dialogs.MiddleTruncate(row.PresetName)}”, isn’t installed. Choose a preset that has its items and recipes.";
                if (await Dialogs.ShowConfirmAsync(this, headline, message, "Choose Preset…").ConfigureAwait(true))
                    await ChoosePresetAsync(row).ConfigureAwait(true);
                return;
            }
            if (await host.OpenGraph(row.Graph, null).ConfigureAwait(true))
                Close();
        }

        internal Func<WelcomeRow, Task<Preset?>>? ChoosePresetOverride { get; set; }

        internal async Task ChoosePresetAsync(WelcomeRow row) {
            using IDisposable busy = ShellHost.HoldBusy();
            Preset? chosen = ChoosePresetOverride is { } stub ? await stub(row).ConfigureAwait(true) : await ShowPresetSheetAsync(row).ConfigureAwait(true);
            if (chosen is null)
                return;
            if (await host.OpenGraph(row.Graph, chosen).ConfigureAwait(true))
                Close();
        }

        private async Task<Preset?> ShowPresetSheetAsync(WelcomeRow row) {
            List<PresetErrorPackage> ratings = await host.RatePresets(row.Graph).ConfigureAwait(true);
            PresetSelectionSheet sheet = MainWindow.NewPresetSelectionSheet(ratings);
            bool? result = await SheetHost.For(this).ShowAsync(sheet).ConfigureAwait(true);
            return result == true ? sheet.ChosenPreset : null;
        }

        internal Task DuplicateRowAsync(WelcomeRow row) {
            host.Library.DuplicateGraph(row.Id, row.Name + " copy");
            ViewModel.Refresh();
            return Task.CompletedTask;
        }

        internal async Task RenameRowAsync(WelcomeRow row) {
            string? name = await SheetHost.For(this).ShowAsync(new RenameGraphSheet(row.Name)).ConfigureAwait(true);
            if (name is null)
                return;
            host.Library.RenameGraph(row.Id, name);
            ViewModel.Refresh();
        }

        internal Task ExportRowAsync(WelcomeRow row) => host.ExportGraph(row.Graph);

        internal async Task DeleteRowAsync(WelcomeRow row) {
            bool confirmed = await Dialogs.ShowDestructiveConfirmAsync(this, $"Delete “{Dialogs.MiddleTruncate(row.Name)}”?",
                "The graph and all its versions are removed from Foreman. You can’t undo this action.", "Delete").ConfigureAwait(true);
            if (!confirmed)
                return;
            host.Library.Delete(row.Id);
            ViewModel.Refresh();
        }

        private WelcomeRow? SelectedRow => recentList.SelectedItem as WelcomeRow;

        private void OpenSelected() {
            if (SelectedRow is { } row)
                Async.Fire(OpenRowAsync(row), nameof(OpenRowAsync));
        }

        private void OnRecentKeyDown(object? sender, KeyEventArgs e) {
            if (SelectedRow is not { } row)
                return;
            if ((e.Key == Key.Return && e.KeyModifiers == KeyModifiers.None) || (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.Meta)) {
                OpenSelected();
                e.Handled = true;
            } else if (e.Key == Key.Back && e.KeyModifiers == PlatformModifiers.Primary) {
                Async.Fire(DeleteRowAsync(row), nameof(DeleteRowAsync));
                e.Handled = true;
            } else if ((e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift) || e.Key == Key.Apps) {
                recentList.GetVisualDescendants().OfType<RecentRow>().FirstOrDefault(r => ReferenceEquals(r.DataContext, row))?.Open();
                e.Handled = true;
            }
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) {
                if (host.HasOpenGraph()) {
                    Close();
                    e.Handled = true;
                }
                return;
            }
            if (e.KeyModifiers != PlatformModifiers.Primary)
                return;
            switch (e.Key) {
                case Key.N:
                    Async.Fire(NewGraphFromMenuAsync(), nameof(NewGraphFromMenuAsync));
                    e.Handled = true;
                    break;
                case Key.O:
                    Async.Fire(OpenFileFromMenuAsync(), nameof(OpenFileFromMenuAsync));
                    e.Handled = true;
                    break;
                case Key.W:
                    Close();
                    e.Handled = true;
                    break;
                case Key.F when filterField.IsVisible:
                    filterField.Focus();
                    e.Handled = true;
                    break;
            }
        }

        internal Border LeftPane => leftPane;
        internal Button NewGraphButton => newGraphButton;
        internal Button OpenButton => openButton;
        internal TextBlock GroupTitle => groupTitle;
        internal ListBox PresetList => presetList;
        internal CheckBox ShowAtLaunchCheckBox => showAtLaunchCheckBox;
        internal ListBox RecentList => recentList;
        internal SearchField FilterField => filterField;
        internal TextBlock CountText => countText;
        internal StatePanel EmptyPanel => emptyPanel;
        internal StatePanel NoResultsPanel => noResultsPanel;
        internal StatePanel ErrorPanel => errorPanel;
        internal Border LoadingStrip => loadingStrip;
        internal TextBlock LoadingSentence => loadingSentence;
        internal ProgressBar LoadingBar => loadingBar;
        internal Button CancelButton => cancelButton;
        internal Grid ActionsGroup => actionsGroup;
        internal Grid RecentsGroup => recentsGroup;
        internal IEnumerable<RecentRow> RecentRows => recentList.GetVisualDescendants().OfType<RecentRow>();
    }
}
