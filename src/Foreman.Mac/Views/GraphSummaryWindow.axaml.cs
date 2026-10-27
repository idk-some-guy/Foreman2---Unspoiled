using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    internal sealed class GraphSummaryHost {
        public Func<SolveStatusTracker?> Tracker { get; init; } = () => null;
        public Func<string> RateName { get; init; } = () => "second";
        public Action<IReadOnlyList<INodeViewModel>>? Reveal { get; init; }
        public Action? AddRecipe { get; init; }
        public AppSettings? Settings { get; init; }
    }

    public sealed partial class GraphSummaryWindow : Window, IThemeAware {
        private static readonly string[] TopLabels = ["Buildings", "Items and fluids", "Key nodes"];
        private static readonly string[] BuildingLabels = ["Assemblers", "Miners", "Power", "Beacons"];
        private static readonly string[] ItemLabels = ["Items", "Fluids"];
        private const double EdgeInset = 20;
        private const double DefaultWidth = 780;
        private const double DefaultHeight = 560;

        private sealed class SummaryListBox : ListBox {
            protected override Type StyleKeyOverride => typeof(ListBox);
            protected override AutomationPeer OnCreateAutomationPeer() => new SummaryTablePeer(this);
        }

        private sealed class SummaryTablePeer(SummaryListBox owner) : ListBoxAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Table;
        }

        private readonly Segmented topSegment;
        private readonly Segmented subSegment;
        private readonly MenuButton showButton;
        private readonly SearchField filterBox;
        private readonly Button exportButton;
        private readonly Grid headerRow;
        private readonly Grid tableHost;
        private readonly Grid controlRow;
        private readonly Grid tableArea;
        private readonly SummaryListBox table;
        private readonly WindowMinSizeScale minSizeScale;
        private readonly GraphSummaryViewModel viewModel = new();
        private readonly Dictionary<SummaryTableId, List<SummaryRow>> rows = [];
        private readonly Dictionary<SummaryTableId, SummarySort> sorts = [];
        private readonly Dictionary<SummaryTableId, string?> selectedKeys = [];
        private readonly bool[] itemKinds = [.. Enumerable.Repeat(true, SummaryTables.ItemKinds.Length)];
        private readonly bool[] keyNodeKinds = [.. Enumerable.Repeat(true, SummaryTables.KeyNodeKinds.Length)];
        private readonly int[] subIndex = [0, 0];
        private readonly StatePanel emptyPanel;
        private readonly StatePanel noMatchPanel;
        private readonly StatePanel hiddenPanel;

        private IProductionGraphSession? session;
        private GraphSummaryHost host = new();
        private SolveStatusTracker? subscribedTracker;
        private int segment;
        private bool rendering;
        private bool syncingKinds;
        private SummaryTableId? renderedTable;
        private GridLength[] widths = [];
        private double[] minWidths = [];
        private bool lastFailed;

        public GraphSummaryWindow() {
            InitializeComponent();
            topSegment = this.FindControl<Segmented>("TopSegment")!;
            subSegment = this.FindControl<Segmented>("SubSegment")!;
            showButton = this.FindControl<MenuButton>("ShowButton")!;
            filterBox = this.FindControl<SearchField>("FilterBox")!;
            exportButton = this.FindControl<Button>("ExportButton")!;
            headerRow = this.FindControl<Grid>("HeaderRow")!;
            tableHost = this.FindControl<Grid>("TableHost")!;
            controlRow = this.FindControl<Grid>("ControlRow")!;
            tableArea = this.FindControl<Grid>("TableArea")!;

            table = new SummaryListBox { Name = "SummaryTable", Theme = KitTheme("Kit.List"), SelectionMode = SelectionMode.Single, IsTextSearchEnabled = true };
            TextSearch.SetTextBinding(table, new Binding(nameof(SummaryRow.SearchText)));
            AutomationProperties.SetName(table, "Graph Summary table");
            table.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SummaryRow>((row, _) => row is null ? null : BuildRowContent(row), supportsRecycling: false);
            table.ContainerPrepared += OnContainerPrepared;
            table.SelectionChanged += OnTableSelectionChanged;
            table.DoubleTapped += (_, _) => RevealSelected();
            table.KeyDown += OnTableKeyDown;
            tableHost.Children.Add(table);

            emptyPanel = NewPanel(StatePanelShape.Empty, "Nothing to summarize", "Add nodes to the graph and their totals appear here.", "Add Recipe…", () => host.AddRecipe?.Invoke());
            noMatchPanel = NewPanel(StatePanelShape.NoResults, "", "Clear the filter or choose another category.", "Clear Filter", ClearFilter);
            hiddenPanel = NewPanel(StatePanelShape.NoResults, "Nothing to show", "Every row is hidden by the Show menu.", "Show All", ShowAllKinds);
            foreach (StatePanel panel in new[] { emptyPanel, noMatchPanel, hiddenPanel }) {
                panel.IsVisible = false;
                tableHost.Children.Add(panel);
            }

            topSegment.Items.AddRange(TopLabels);
            AutomationProperties.SetName(topSegment, "Summary");
            AutomationProperties.SetName(subSegment, "Category");
            subSegment.Items.AddRange(BuildingLabels);
            topSegment.PropertyChanged += (_, e) => {
                if (e.Property == Segmented.SelectedIndexProperty && !rendering)
                    SelectSegment(topSegment.SelectedIndex);
            };
            subSegment.PropertyChanged += (_, e) => {
                if (e.Property == Segmented.SelectedIndexProperty && !rendering && segment < 2 && subSegment.SelectedIndex >= 0) {
                    subIndex[segment] = subSegment.SelectedIndex;
                    Render();
                }
            };
            filterBox.PropertyChanged += (_, e) => {
                if (e.Property == TextBox.TextProperty && !rendering)
                    Render();
            };
            showButton.ItemToggled += (_, _) => OnKindsToggled();
            exportButton.Click += (_, _) => Async.Fire(ExportCsvAsync(), nameof(ExportCsvAsync));

            AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            minSizeScale = new WindowMinSizeScale(this);
            Opened += (_, _) => AppearanceHub.Current?.Subscribe(this);
            Closing += (_, _) => RememberGeometry();
            Closed += (_, _) => {
                AppearanceHub.Current?.Unsubscribe(this);
                Detach();
            };
        }

        internal static GraphSummaryWindow Create(IProductionGraphSession session, string rateName, GraphSummaryHost? host = null) {
            var window = new GraphSummaryWindow();
            window.Attach(session, host ?? new GraphSummaryHost { RateName = () => rateName });
            return window;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"GraphSummaryWindow: kit theme '{key}' not found.");
        }

        private static StatePanel NewPanel(StatePanelShape shape, string headline, string sentence, string label, Action action) {
            var button = new Button { Theme = KitTheme("Kit.Button"), Content = label };
            ButtonRow.SetRole(button, ButtonRowRole.Default);
            button.Click += (_, _) => action();
            var row = new ButtonRow { Theme = KitTheme("Kit.ButtonRow") };
            row.Items.Add(button);
            return new StatePanel { Theme = KitTheme("Kit.StatePanel"), Shape = shape, Headline = headline, Sentence = sentence, Buttons = row, MovesFocusOnAppear = false };
        }

        private void Attach(IProductionGraphSession session, GraphSummaryHost host) {
            this.session = session;
            this.host = host;
            segment = Math.Clamp(host.Settings?.GraphSummarySegment ?? 0, 0, 2);
            session.NodeValuesUpdated += OnNodeValuesUpdated;
            SubscribeTracker();
            topSegment.SelectedIndex = segment;
            ApplySegmentChrome();
            ApplySavedGeometry();
            ApplyMargins();
            Rebuild();
        }

        private void Detach() {
            if (session is not null)
                session.NodeValuesUpdated -= OnNodeValuesUpdated;
            session = null;
            if (subscribedTracker is not null)
                subscribedTracker.Changed -= OnTrackerChanged;
            subscribedTracker = null;
        }

        private void SubscribeTracker() {
            SolveStatusTracker? tracker = host.Tracker();
            if (ReferenceEquals(tracker, subscribedTracker))
                return;
            if (subscribedTracker is not null)
                subscribedTracker.Changed -= OnTrackerChanged;
            subscribedTracker = tracker;
            if (tracker is not null)
                tracker.Changed += OnTrackerChanged;
        }

        private void OnTrackerChanged(object? sender, EventArgs e) {
            if (Dispatcher.UIThread.CheckAccess())
                Render();
            else
                Dispatcher.UIThread.Post(Render);
        }

        private void OnNodeValuesUpdated(object? sender, EventArgs e) {
            if (Dispatcher.UIThread.CheckAccess())
                Rebuild();
            else
                Dispatcher.UIThread.Post(Rebuild);
        }

        internal GraphSummaryViewModel ViewModel => viewModel;

        internal bool IsSolveFailed => host.Tracker() is { State: SolveStatusState.Failed };

        private void ApplySavedGeometry() {
            Width = DefaultWidth * WindowScaler.CurrentScale;
            Height = DefaultHeight * WindowScaler.CurrentScale;
            if (host.Settings is not AppSettings settings)
                return;
            if (settings.GraphSummaryWidth is double width && settings.GraphSummaryHeight is double height) {
                Width = Math.Max(width * WindowScaler.CurrentScale, MinWidth);
                Height = Math.Max(height * WindowScaler.CurrentScale, MinHeight);
            }
            if (settings.GraphSummaryLeft is double left && settings.GraphSummaryTop is double top) {
                var saved = new PixelPoint((int)left, (int)top);
                if (Screens.All.Count == 0 || Screens.All.Any(s => s.WorkingArea.Contains(saved)))
                    Position = saved;
            }
        }

        internal bool HasSavedPosition => host.Settings is { GraphSummaryLeft: not null, GraphSummaryTop: not null };

        private void RememberGeometry() {
            if (host.Settings is not AppSettings settings)
                return;
            if (ClientSize.Width > 0 && ClientSize.Height > 0) {
                settings.GraphSummaryWidth = ClientSize.Width / WindowScaler.CurrentScale;
                settings.GraphSummaryHeight = ClientSize.Height / WindowScaler.CurrentScale;
            }
            settings.GraphSummaryLeft = Position.X;
            settings.GraphSummaryTop = Position.Y;
            settings.GraphSummarySegment = segment;
        }

        internal void SetTitle(string graphName) => Title = $"Graph Summary — {graphName}";

        private void ApplyMargins() {
            controlRow.Margin = new Thickness(EdgeInset, Token("Foreman.Space.Space4", 16), EdgeInset, 0);
            tableArea.Margin = new Thickness(EdgeInset, Token("Foreman.Space.Space3", 12), EdgeInset, EdgeInset);
        }

        public void RefreshTheme() {
            minSizeScale.Apply();
            ApplyMargins();
            if (session is not null)
                Render();
        }

        internal int SegmentIndex => segment;
        internal Segmented TopSegmentControl => topSegment;
        internal Segmented SubSegmentControl => subSegment;
        internal MenuButton ShowButtonControl => showButton;
        internal SearchField FilterBoxControl => filterBox;
        internal Button ExportButtonControl => exportButton;
        internal ListBox TableControl => table;
        internal Grid HeaderRowControl => headerRow;
        internal StatePanel EmptyPanel => emptyPanel;
        internal StatePanel NoMatchPanel => noMatchPanel;
        internal StatePanel HiddenPanel => hiddenPanel;

        internal void SelectSegment(int index) {
            index = Math.Clamp(index, 0, 2);
            segment = index;
            if (host.Settings is AppSettings settings)
                settings.GraphSummarySegment = index;
            if (topSegment.SelectedIndex != index) {
                rendering = true;
                topSegment.SelectedIndex = index;
                rendering = false;
            }
            ApplySegmentChrome();
            Render();
        }

        internal void SelectSubSegment(int index) {
            if (segment > 1 || index < 0 || index >= subSegment.Items.Count)
                return;
            subSegment.SelectedIndex = index;
        }

        private void ApplySegmentChrome() {
            rendering = true;
            subSegment.IsVisible = segment < 2;
            if (segment < 2) {
                subSegment.Items.Clear();
                subSegment.Items.AddRange(segment == 0 ? BuildingLabels : ItemLabels);
                subSegment.SelectedIndex = subIndex[segment];
            }
            showButton.IsVisible = segment > 0;
            if (segment > 0) {
                string[] labels = segment == 1 ? SummaryTables.ItemKinds : SummaryTables.KeyNodeKinds;
                bool[] kinds = segment == 1 ? itemKinds : keyNodeKinds;
                syncingKinds = true;
                showButton.Items.Clear();
                showButton.Items.AddRange(labels.Select((label, i) => new MenuButtonItem { Text = label, IsDefault = true, IsChecked = kinds[i] }));
                syncingKinds = false;
            }
            rendering = false;
        }

        private void OnKindsToggled() {
            if (syncingKinds || segment == 0)
                return;
            bool[] kinds = segment == 1 ? itemKinds : keyNodeKinds;
            for (int i = 0; i < kinds.Length && i < showButton.Items.Count; i++)
                kinds[i] = showButton.Items[i].IsChecked;
            Render();
        }

        private void ShowAllKinds() {
            bool[] kinds = segment == 1 ? itemKinds : keyNodeKinds;
            Array.Fill(kinds, true);
            ApplySegmentChrome();
            Render();
        }

        internal void SetKind(int index, bool on) {
            if (segment == 0)
                return;
            showButton.Items[index].IsChecked = on;
            OnKindsToggled();
        }

        private void ClearFilter() {
            filterBox.Text = "";
            filterBox.Focus();
        }

        internal SummaryTableId CurrentTable => segment switch {
            0 => (SummaryTableId)subIndex[0],
            1 => subIndex[1] == 0 ? SummaryTableId.Items : SummaryTableId.Fluids,
            _ => SummaryTableId.KeyNodes,
        };

        internal string BuildingCountText { get; private set; } = "";
        internal string BeaconCountText { get; private set; } = "";
        internal string PowerConsumptionText { get; private set; } = "";
        internal string PowerProductionText { get; private set; } = "";
        internal string? PowerNetText { get; private set; }
        internal int RebuildCount { get; private set; }

        internal IReadOnlyList<SummaryRow> Rows(SummaryTableId id) => rows.TryGetValue(id, out List<SummaryRow>? list) ? list : [];

        internal IReadOnlyList<SummaryRow> Shown => table.ItemsSource as IReadOnlyList<SummaryRow> ?? [];

        private void Rebuild() {
            if (session is null)
                return;
            RebuildCount++;
            SubscribeTracker();
            viewModel.Refresh(session.View.Nodes);
            foreach (SummaryTableId id in Enum.GetValues<SummaryTableId>()) {
                List<SummaryRow> list = SummaryTables.Build(id, viewModel);
                if (!sorts.TryGetValue(id, out SummarySort? sort))
                    sorts[id] = sort = SummaryTables.DefaultSort(id);
                SummaryTables.Sort(list, id, sort);
                rows[id] = list;
            }

            BuildingCountText = "#Buildings: " + GraphicsStuff.DoubleToString(viewModel.BuildingTotal);
            BeaconCountText = "#Beacons: " + GraphicsStuff.DoubleToString(viewModel.BeaconTotal);
            PowerConsumptionText = "Power Consumption: " + GraphicsStuff.DoubleToEnergy(viewModel.PowerConsumption, "W");
            PowerProductionText = "Power Production: " + GraphicsStuff.DoubleToEnergy(viewModel.PowerProduction, "W");
            PowerNetText = viewModel.PowerConsumption > 0 && viewModel.PowerProduction > 0
                ? "Net Power: " + GraphicsStuff.DoubleToEnergy(viewModel.PowerProduction - viewModel.PowerConsumption, "W")
                : null;
            Render();
        }

        private bool[] CurrentKinds => segment == 1 ? itemKinds : keyNodeKinds;

        private List<SummaryRow> Filtered(SummaryTableId id) {
            string query = filterBox.Text ?? "";
            bool useKinds = id is SummaryTableId.Items or SummaryTableId.Fluids or SummaryTableId.KeyNodes;
            bool[] kinds = id == SummaryTableId.KeyNodes ? keyNodeKinds : itemKinds;
            return [.. Rows(id).Where(r => SummaryTables.MatchesQuery(id, r, query) && (!useKinds || SummaryTables.PassesKinds(id, r, kinds)))];
        }

        private void Render() {
            if (session is null)
                return;
            SummaryTableId id = CurrentTable;
            bool failed = IsSolveFailed;
            bool sameTable = renderedTable == id;
            double offset = sameTable ? ScrollOffset() : 0;
            List<SummaryRow> shown = Filtered(id);
            bool graphEmpty = !session.View.Nodes.Any();
            string query = filterBox.Text ?? "";
            bool allKinds = id is SummaryTableId.Items or SummaryTableId.Fluids or SummaryTableId.KeyNodes && (id == SummaryTableId.KeyNodes ? keyNodeKinds : itemKinds).All(k => k);
            bool noneVisible = shown.Count == 0 && Rows(id).Count > 0 && string.IsNullOrEmpty(query);
            bool hiddenByKinds = noneVisible && !allKinds;

            rendering = true;
            lastFailed = failed;
            ComputeLayout(id, shown);
            BuildHeader(id);
            table.ItemsSource = shown;
            selectedKeys.TryGetValue(id, out string? key);
            table.SelectedItem = key is null ? null : shown.FirstOrDefault(r => r.Key == key);
            rendering = false;
            renderedTable = id;

            bool showTable = !graphEmpty && !(shown.Count == 0 && Rows(id).Count > 0);
            headerRow.IsVisible = showTable;
            table.IsVisible = showTable;
            emptyPanel.IsVisible = graphEmpty || noneVisible && allKinds;
            noMatchPanel.IsVisible = !graphEmpty && shown.Count == 0 && Rows(id).Count > 0 && !noneVisible;
            hiddenPanel.IsVisible = !graphEmpty && hiddenByKinds;
            noMatchPanel.Headline = $"No matches for “{query}”";
            exportButton.IsEnabled = !graphEmpty;

            int total = CurrentKinds.Length;
            int enabled = CurrentKinds.Count(k => k);
            AutomationProperties.SetName(showButton, $"Show, {enabled} of {total} kinds");

            if (showTable && offset > 0) {
                table.UpdateLayout();
                if (TableScroller is { } viewer)
                    viewer.Offset = new Vector(viewer.Offset.X, offset);
            }
        }

        private ScrollViewer? TableScroller => table.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        private double ScrollOffset() => TableScroller?.Offset.Y ?? 0;

        internal double TableScrollOffset => ScrollOffset();


        internal void ScrollTableTo(double offset) {
            UpdateLayout();
            if (TableScroller is { } viewer)
                viewer.Offset = new Vector(viewer.Offset.X, offset);
        }

        private void OnTableSelectionChanged(object? sender, SelectionChangedEventArgs e) {
            if (rendering)
                return;
            selectedKeys[CurrentTable] = (table.SelectedItem as SummaryRow)?.Key;
        }

        internal void SelectRow(string key) => table.SelectedItem = Shown.FirstOrDefault(r => r.Key == key);

        internal string? SelectedKey => (table.SelectedItem as SummaryRow)?.Key;

        internal void SortBy(int column) {
            SummaryTableId id = CurrentTable;
            sorts[id] = SummaryTables.Toggle(id, sorts.GetValueOrDefault(id), column);
            SummaryTables.Sort(rows[id], id, sorts[id]);
            Render();
        }

        internal SummarySort SortOf(SummaryTableId id) => sorts.GetValueOrDefault(id) ?? SummaryTables.DefaultSort(id);

        private static TextBlock Text(string text, bool caption, bool bold) {
            var block = new TextBlock { Theme = KitTheme("Kit.Text"), Text = text };
            block.Classes.Add(caption ? "caption" : "body");
            if (bold)
                block.Bind(TextBlock.FontWeightProperty, block.GetResourceObservable("Foreman.Type.Caption.Emphasized.FontWeight"));
            return block;
        }

        private double Measure(string text, bool caption, bool bold) {
            double size = Token(caption ? "Foreman.Type.Caption.FontSize" : "Foreman.Type.Body.FontSize", caption ? 11 : 13);
            FontWeight weight = bold && this.TryFindResource("Foreman.Type.Caption.Emphasized.FontWeight", ActualThemeVariant, out object? value) && value is FontWeight w ? w : FontWeight.Normal;
            var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, weight);
            return new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, null).Width;
        }

        private double Token(string key, double fallback) =>
            this.TryFindResource(key, ActualThemeVariant, out object? value) && value is double d ? d : fallback;

        private void ComputeLayout(SummaryTableId id, List<SummaryRow> shown) {
            IReadOnlyList<SummaryColumn> columns = SummaryTables.Columns(id);
            int name = SummaryTables.NameColumn(id);
            double sortRoom = Token("Foreman.Size.GlyphMicro", 12) + Token("Foreman.Space.Space1", 4);
            widths = new GridLength[columns.Count];
            minWidths = new double[columns.Count];
            for (int i = 0; i < columns.Count; i++) {
                bool isName = i == name || id == SummaryTableId.KeyNodes && i == 2;
                if (isName) {
                    widths[i] = new GridLength(1, GridUnitType.Star);
                    minWidths[i] = 160;
                    continue;
                }
                double content = Measure(columns[i].Header, caption: true, bold: true) + sortRoom;
                string? longest = shown.Select(r => r.Cells[i]).OrderByDescending(c => c.Length).FirstOrDefault();
                if (longest is not null)
                    content = Math.Max(content, Measure(longest, caption: false, bold: false));
                if (id == SummaryTableId.KeyNodes && i == 0)
                    content = Math.Max(content, Measure("Passthrough", caption: false, bold: false));
                widths[i] = new GridLength(Math.Ceiling(content), GridUnitType.Pixel);
            }
        }

        private Thickness RowInset() {
            Thickness frame = this.TryFindResource("Foreman.Thickness.ListFramePadding", ActualThemeVariant, out object? f) && f is Thickness ft ? ft : default;
            Thickness item = this.TryFindResource("Foreman.Thickness.ListRowPadding", ActualThemeVariant, out object? r) && r is Thickness rt ? rt : default;
            return new Thickness(1 + frame.Left + item.Left, 0, 1 + frame.Right + item.Right, 0);
        }

        private Grid NewColumnGrid() {
            var grid = new Grid();
            grid.Bind(Grid.ColumnSpacingProperty, grid.GetResourceObservable("Foreman.Space.Space2"));
            for (int i = 0; i < widths.Length; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(widths[i]) { MinWidth = minWidths[i] });
            return grid;
        }

        private void BuildHeader(SummaryTableId id) {
            IReadOnlyList<SummaryColumn> columns = SummaryTables.Columns(id);
            SummarySort sort = SortOf(id);
            headerRow.Children.Clear();
            headerRow.ColumnDefinitions.Clear();
            headerRow.Margin = RowInset();
            headerRow.Bind(Grid.ColumnSpacingProperty, headerRow.GetResourceObservable("Foreman.Space.Space2"));
            for (int i = 0; i < columns.Count; i++)
                headerRow.ColumnDefinitions.Add(new ColumnDefinition(widths[i]) { MinWidth = minWidths[i] });

            for (int i = 0; i < columns.Count; i++) {
                int column = i;
                bool sorted = sort.Column == i;
                string arrow = sorted ? (sort.Ascending ? " ▴" : " ▾") : "";
                TextBlock label = Text(columns[i].Header + arrow, caption: true, bold: true);
                if (!sorted)
                    label.Classes.Add("secondary");
                label.HorizontalAlignment = columns[i].Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                label.VerticalAlignment = VerticalAlignment.Center;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                var cell = new Border { Background = Brushes.Transparent, Child = label, Focusable = false, Name = "SummaryHeader" + i };
                AutomationProperties.SetName(cell, sorted ? $"{columns[i].Header}, sorted {(sort.Ascending ? "ascending" : "descending")}" : columns[i].Header);
                cell.PointerPressed += (_, e) => {
                    if (e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) {
                        SortBy(column);
                        e.Handled = true;
                    }
                };
                Grid.SetColumn(cell, i);
                headerRow.Children.Add(cell);
            }
        }

        private Grid BuildRowContent(SummaryRow row) {
            SummaryTableId id = renderedTable ?? CurrentTable;
            IReadOnlyList<SummaryColumn> columns = SummaryTables.Columns(id);
            int name = SummaryTables.NameColumn(id);
            bool stale = row.IsStale || lastFailed;
            Grid grid = NewColumnGrid();
            grid.Background = Brushes.Transparent;
            for (int i = 0; i < columns.Count; i++) {
                bool zero = row.Cells[i] == SummaryTables.Zero;
                TextBlock text = Text(row.Cells[i], caption: false, bold: false);
                text.TextTrimming = TextTrimming.CharacterEllipsis;
                text.VerticalAlignment = VerticalAlignment.Center;
                if (columns[i].Numeric) {
                    text.HorizontalAlignment = HorizontalAlignment.Right;
                    text.TextAlignment = TextAlignment.Right;
                    text.FontFeatures = FontFeatureCollection.Parse("tnum");
                    if (zero || stale)
                        text.Classes.Add("secondary");
                    if (stale && !zero)
                        text.FontStyle = FontStyle.Italic;
                }

                Control cell = text;
                if (i == name) {
                    var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("Foreman.Space.Space15"));
                    var icon = new Avalonia.Controls.Image { Source = row.Icon, Stretch = Stretch.Uniform };
                    icon.Bind(WidthProperty, icon.GetResourceObservable("Foreman.Size.Glyph20"));
                    icon.Bind(HeightProperty, icon.GetResourceObservable("Foreman.Size.Glyph20"));
                    panel.Children.Add(icon);
                    panel.Children.Add(text);
                    cell = panel;
                }
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
            return grid;
        }

        private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e) {
            if (e.Container is not ListBoxItem item || table.ItemsSource is not IList<SummaryRow> source || e.Index >= source.Count)
                return;
            SummaryRow row = source[e.Index];
            item.Classes.Set("status-error", row.Status == RowStatus.Error);
            item.Classes.Set("status-warning", row.Status == RowStatus.Warning);
            SummaryTableId id = renderedTable ?? CurrentTable;
            AutomationProperties.SetName(item, SummaryTables.AutomationName(id, row, host.RateName()));
        }

        internal IReadOnlyList<INodeViewModel> NodesFor(SummaryTableId id, SummaryRow row) {
            if (session is null)
                return [];
            IEnumerable<INodeViewModel> all = session.View.Nodes;
            return id switch {
                SummaryTableId.KeyNodes => row.Tag is INodeViewModel node ? [node] : [],
                SummaryTableId.Beacons => [.. all.OfType<IRecipeNodeViewModel>().Where(n => n.SelectedBeacon.Equals(row.Tag))],
                SummaryTableId.Assemblers or SummaryTableId.Miners or SummaryTableId.Power => [.. all.OfType<IRecipeNodeViewModel>().Where(n => n.SelectedAssembler.Equals(row.Tag))],
                _ => [.. all.Where(n => UsesItem(n, (ItemQualityPair)row.Tag))],
            };
        }

        private static bool UsesItem(INodeViewModel node, ItemQualityPair item) => node switch {
            IRecipeNodeViewModel r => r.Inputs.Contains(item) || r.Outputs.Contains(item),
            ISupplierNodeViewModel s => s.SuppliedItem.Equals(item),
            IConsumerNodeViewModel c => c.ConsumedItem.Equals(item),
            IPassthroughNodeViewModel p => p.PassthroughItem.Equals(item),
            _ => false,
        };

        internal void RevealSelected() {
            if (table.SelectedItem is not SummaryRow row)
                return;
            IReadOnlyList<INodeViewModel> nodes = NodesFor(CurrentTable, row);
            if (nodes.Count > 0)
                host.Reveal?.Invoke(nodes);
        }

        private void OnTableKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key != Key.Enter)
                return;
            RevealSelected();
            e.Handled = true;
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e) {
            bool meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if (meta && e.Key == Key.W) {
                Close();
                e.Handled = true;
            } else if (meta && e.Key == Key.F) {
                filterBox.Focus(NavigationMethod.Tab);
                e.Handled = true;
            } else if (meta && !alt && e.Key is >= Key.D1 and <= Key.D3) {
                SelectSegment(e.Key - Key.D1);
                e.Handled = true;
            } else if (meta && alt && e.Key is >= Key.D1 and <= Key.D4) {
                SelectSubSegment(e.Key - Key.D1);
                e.Handled = true;
            } else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) {
                if (!string.IsNullOrEmpty(filterBox.Text))
                    filterBox.Text = "";
                else
                    Close();
                e.Handled = true;
            }
        }

        internal Func<Task<string?>>? SaveFilePathStub { get; set; }
        internal string? LastCsvWritten { get; private set; }

        internal Task ExportCsvAsync() => WriteCsvAsync(SummaryTables.Tables((SummarySegment)segment));

        private async Task WriteCsvAsync(IReadOnlyList<SummaryTableId> ids) {
            string? path = await (SaveFilePathStub?.Invoke() ?? PickSaveFilePathAsync()).ConfigureAwait(true);
            if (path is null)
                return;

            string rate = host.RateName();
            var lines = new List<string[]>();
            foreach (SummaryTableId id in ids) {
                lines.Add(SummaryTables.CsvHeader(id, rate));
                foreach (SummaryRow row in Filtered(id))
                    lines.Add([.. row.Cells.Select(c => c.Replace(",", "").Replace("\n", "; ").Replace("\t", ""))]);
                lines.Add([""]);
            }
            if (lines.Count > 0)
                lines.RemoveAt(lines.Count - 1);

            var csv = new StringBuilder();
            foreach (string[] line in lines)
                csv.AppendLine(string.Join(",", line));

            Utf8File.WriteAllText(path, csv.ToString());
            LastCsvWritten = csv.ToString();
        }

        private async Task<string?> PickSaveFilePathAsync() {
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
    }
}
