using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Media;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public partial class VersionBrowserSheet : SheetContent<Guid?> {
        private const int MinRows = 4;
        private const int MaxRows = 10;

        private readonly ListBox versionList;
        private readonly Grid headerRow;
        private readonly Button deleteButton;
        private readonly Button cancelButton;
        private readonly Button restoreButton;
        private readonly List<IDisposable> tokenSubs = [];
        private readonly DateTime nowLocal;
        private List<VersionRow> rows;
        private double rowHeight;
        private double listInset;
        private Thickness rowPadding;

        internal Func<Task<bool>>? ConfirmDeleteAsync { get; set; }
        internal Action<Guid>? DeleteVersion { get; set; }

        public VersionBrowserSheet() : this("", []) {
        }

        internal VersionBrowserSheet(string graphName, IReadOnlyList<GraphVersionRow> versions, DateTime? nowLocal = null) {
            InitializeComponent();
            this.nowLocal = nowLocal ?? DateTime.Now;
            WidthClass = SheetWidthClass.Wide;
            Title = $"Versions of “{Dialogs.MiddleTruncate(graphName)}”";

            versionList = this.FindControl<ListBox>("VersionList")!;
            headerRow = this.FindControl<Grid>("HeaderRow")!;
            deleteButton = SheetButtons.Make("Delete Version…", ButtonRowRole.Helper);
            cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            restoreButton = SheetButtons.Make("Restore", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(deleteButton, cancelButton, restoreButton);
            DefaultButton = restoreButton;

            rows = [.. versions.Select(v => new VersionRow(v, this.nowLocal))];
            versionList.ItemTemplate = new FuncDataTemplate<VersionRow>((row, _) => row is null ? new Grid() : new VersionRowGrid(row), supportsRecycling: false);
            versionList.ContainerPrepared += (_, e) => {
                if (e.Container.DataContext is VersionRow row)
                    AutomationProperties.SetName(e.Container, row.AccessibleName);
            };
            versionList.ItemsSource = rows;
            versionList.SelectionChanged += (_, _) => UpdateButtons();
            versionList.DoubleTapped += (_, _) => Restore();
            UpdateButtons();
            ApplyScale();

            restoreButton.Click += (_, _) => Restore();
            cancelButton.Click += (_, _) => Cancel();
            deleteButton.Click += (_, _) => Async.Fire(DeleteSelectedAsync(), nameof(DeleteSelectedAsync));
            Loaded += (_, _) => versionList.Focus();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private VersionRow? Selected => versionList.SelectedItem as VersionRow;

        private void UpdateButtons() {
            bool selected = Selected is not null;
            restoreButton.IsEnabled = selected;
            deleteButton.IsEnabled = selected;
        }

        private void Restore() {
            if (Selected is VersionRow row)
                Complete(row.Id);
        }

        internal async Task DeleteSelectedAsync() {
            if (Selected is not VersionRow row)
                return;
            if (ConfirmDeleteAsync is not null && !await ConfirmDeleteAsync().ConfigureAwait(true))
                return;
            DeleteVersion?.Invoke(row.Id);
            rows = [.. rows.Where(r => r.Id != row.Id)];
            versionList.ItemsSource = rows;
            UpdateButtons();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            WindowScaler.Rescaled += OnRescaled;
            tokenSubs.Add(this.GetResourceObservable("Foreman.Size.ListRowHeight").Subscribe(new TokenSink(v => { if (v is double d) rowHeight = d; ApplyScale(); })));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Space.Space05").Subscribe(new TokenSink(v => { if (v is double d) listInset = d; ApplyScale(); })));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Thickness.ListRowPadding").Subscribe(new TokenSink(v => { if (v is Thickness t) rowPadding = t; ApplyScale(); })));
            ApplyScale();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            WindowScaler.Rescaled -= OnRescaled;
            foreach (IDisposable sub in tokenSubs)
                sub.Dispose();
            tokenSubs.Clear();
        }

        private void OnRescaled(object? sender, EventArgs e) => ApplyScale();

        private void ApplyScale() {
            headerRow.Height = Token(headerRow, "Foreman.Size.VersionHeaderHeight") * WindowScaler.CurrentScale;
            ApplyColumns(headerRow);
            versionList.Padding = new Thickness(listInset);
            double border = versionList.BorderThickness.Left;
            headerRow.Margin = new Thickness(border + listInset + rowPadding.Left, 0, border + listInset + rowPadding.Right, 0);
            double chrome = 2 * (versionList.BorderThickness.Top + listInset);
            versionList.MinHeight = (MinRows * rowHeight) + chrome;
            versionList.MaxHeight = (MaxRows * rowHeight) + chrome;
        }

        private static double Token(Control control, string key) =>
            control.TryFindResource(key, control.ActualThemeVariant, out object? value) && value is double d ? d : 0;

        private static void ApplyColumns(Grid grid) {
            double scale = WindowScaler.CurrentScale;
            double gutter = Token(grid, "Foreman.Space.Space2") * scale;
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(gutter, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(Token(grid, "Foreman.Size.VersionKindColumn") * scale, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(gutter, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(Token(grid, "Foreman.Size.VersionNodesColumn") * scale, GridUnitType.Pixel));
        }

        private sealed class VersionRowGrid : Grid {
            public VersionRowGrid(VersionRow row) {
                ApplyColumns(this);
                Children.Add(Cell(row.WhenText, 0, HorizontalAlignment.Left, false, false));
                Children.Add(Cell(row.KindText, 2, HorizontalAlignment.Left, false, true));
                Children.Add(Cell(row.NodesText, 4, HorizontalAlignment.Right, true, false));
            }

            protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
                base.OnAttachedToVisualTree(e);
                WindowScaler.Rescaled += OnRescaled;
                ApplyColumns(this);
            }

            protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
                base.OnDetachedFromVisualTree(e);
                WindowScaler.Rescaled -= OnRescaled;
            }

            private void OnRescaled(object? sender, EventArgs e) => ApplyColumns(this);

            private static TextBlock Cell(string text, int column, HorizontalAlignment alignment, bool tabular, bool secondary) {
                var cell = new TextBlock {
                    Text = text,
                    HorizontalAlignment = alignment,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                if (secondary && Application.Current?.TryGetResource("Kit.Text", null, out object? theme) == true && theme is ControlTheme controlTheme) {
                    cell.Theme = controlTheme;
                    cell.Classes.Add("secondary");
                }
                if (tabular)
                    cell.FontFeatures = FontFeatureCollection.Parse("tnum");
                Grid.SetColumn(cell, column);
                return cell;
            }
        }

        private sealed class TokenSink(Action<object?> onNext) : IObserver<object?> {
            public void OnNext(object? value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        internal static string FormatWhen(DateTime utc, DateTime nowLocal) {
            DateTime local = utc.ToLocalTime();
            string time = local.ToString("h:mm tt", DisplayCulture.Format);
            if (local.Date == nowLocal.Date)
                return "Today, " + time;
            if (local.Date == nowLocal.Date.AddDays(-1))
                return "Yesterday, " + time;
            return local.ToString("MMM d", DisplayCulture.Format) + ", " + time;
        }

        internal ListBox VersionListControl => versionList;
        internal Grid HeaderRowControl => headerRow;
        internal Button DeleteButtonControl => deleteButton;
        internal Button CancelButtonControl => cancelButton;
        internal Button RestoreButtonControl => restoreButton;
        internal IReadOnlyList<VersionRow> Rows => rows;
    }

    internal sealed class VersionRow(GraphVersionRow version, DateTime nowLocal) {
        public Guid Id { get; } = version.Id;
        public string WhenText { get; } = VersionBrowserSheet.FormatWhen(version.CreatedAt, nowLocal);
        public string KindText { get; } = version.Kind switch {
            GraphVersionKind.Saved => "Saved",
            GraphVersionKind.BeforeRestore => "Before Restore",
            _ => "Automatic",
        };
        public string NodesText { get; } = version.NodeCount.ToString(DisplayCulture.Format);
        public string AccessibleName => $"{WhenText}, {KindText}, {NodesText} nodes";
        public override string ToString() => WhenText;
    }
}
