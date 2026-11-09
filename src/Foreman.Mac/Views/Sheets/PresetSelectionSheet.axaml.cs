using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public partial class PresetSelectionSheet : SheetContent<bool?> {
        private const double NameMinWidth = 168;
        private const double ModsWidth = 64;
        private const double ItemsWidth = 64;
        private const double RecipesWidth = 72;
        private const double Gutter = 16;
        private const double HeaderHeight = 20;
        private const int MinRows = 4;
        private const int MaxRows = 10;

        private readonly List<PresetRow> presetRows;
        private readonly ListBox presetListView;
        private readonly Grid headerRow;
        private readonly Button confirmationButton;
        private readonly Button cancellingButton;
        private readonly List<IDisposable> tokenSubs = [];
        private double rowHeight = 26;
        private double listInset = 2;
        private Thickness rowPadding = new(10, 0);
        private bool committing;

        internal PresetSelectionViewModel ViewModel { get; } = new PresetSelectionViewModel();

        public Preset? ChosenPreset { get; private set; }

        internal Func<Preset, Task<bool>>? ReadPresetAsync { get; set; }

        public PresetSelectionSheet() : this([]) {
        }

        public PresetSelectionSheet(List<PresetErrorPackage> presetErrors, Preset? failed = null) {
            InitializeComponent();
            if (failed is not null)
                this.FindControl<TextBlock>("LedeText")!.Text =
                    $"\u201C{Dialogs.MiddleTruncate(failed.Name)}\u201D couldn\u2019t be loaded. The ratings show how much of this graph each other preset can reproduce.";
            ViewModel.Load(presetErrors);
            WidthClass = SheetWidthClass.Wide;
            Title = "Select a preset";

            presetListView = this.FindControl<ListBox>("PresetSelectionListView")!;
            headerRow = this.FindControl<Grid>("HeaderRow")!;
            cancellingButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            confirmationButton = SheetButtons.Make("Load Preset", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(cancellingButton, confirmationButton);
            DefaultButton = confirmationButton;

            presetListView.ItemTemplate = new FuncDataTemplate<PresetRow>((row, _) => new PresetRowGrid(row), supportsRecycling: false);
            presetListView.ContainerPrepared += (_, e) => {
                if (e.Container.DataContext is PresetRow row) {
                    AutomationProperties.SetName(e.Container, row.AccessibleName);
                    ToolTip.SetTip(e.Container, row.Tooltip);
                }
            };
            ApplyScale();

            presetRows = [.. ViewModel.Presets];
            presetListView.ItemsSource = presetRows;
            presetListView.SelectionChanged += (_, _) => ViewModel.Selected = presetListView.SelectedItem as PresetRow;
            presetListView.DoubleTapped += (_, _) => Async.Fire(CommitAsync(), nameof(CommitAsync));
            if (presetRows.FindIndex(r => r.Preset.Name != failed?.Name) is int first and >= 0)
                presetListView.SelectedIndex = first;
            confirmationButton.Click += (_, _) => Async.Fire(CommitAsync(), nameof(CommitAsync));
            cancellingButton.Click += (_, _) => Cancel();
            Loaded += (_, _) => presetListView.Focus();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
            headerRow.Height = HeaderHeight * WindowScaler.CurrentScale;
            ApplyColumns(headerRow);
            presetListView.Padding = new Thickness(listInset);
            double border = presetListView.BorderThickness.Left;
            headerRow.Margin = new Thickness(border + listInset + rowPadding.Left, 0, border + listInset + rowPadding.Right, 0);
            double chrome = 2 * (presetListView.BorderThickness.Top + listInset);
            presetListView.MinHeight = (MinRows * rowHeight) + chrome;
            presetListView.MaxHeight = (MaxRows * rowHeight) + chrome;
        }

        private static void ApplyColumns(Grid grid) {
            double scale = WindowScaler.CurrentScale;
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = NameMinWidth * scale });
            foreach (double width in new[] { ModsWidth, ItemsWidth, RecipesWidth }) {
                grid.ColumnDefinitions.Add(new ColumnDefinition(Gutter * scale, GridUnitType.Pixel));
                grid.ColumnDefinitions.Add(new ColumnDefinition(width * scale, GridUnitType.Pixel));
            }
        }

        private sealed class PresetRowGrid : Grid {
            public PresetRowGrid(PresetRow row) {
                Classes.Add("preset-row");
                ApplyColumns(this);
                Children.Add(Cell(row.Name, 0, HorizontalAlignment.Left, "preset-name"));
                Children.Add(Cell(row.ModsText, 2, HorizontalAlignment.Right, "preset-rating"));
                Children.Add(Cell(row.ItemsText, 4, HorizontalAlignment.Right, "preset-rating"));
                Children.Add(Cell(row.RecipesText, 6, HorizontalAlignment.Right, "preset-rating"));
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

            private static TextBlock Cell(string text, int column, HorizontalAlignment alignment, string role) {
                var cell = new TextBlock {
                    Text = text,
                    HorizontalAlignment = alignment,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Classes = { role },
                };
                if (role == "preset-rating")
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

        internal async Task CommitAsync() {
            if (committing || ViewModel.Selected is not PresetRow row)
                return;
            committing = true;
            try {
                ClearActivity();
                if (ReadPresetAsync is { } read && !await read(row.Preset).ConfigureAwait(true)) {
                    ReportFailure($"Couldn’t load “{Dialogs.MiddleTruncate(row.Name)}” — the preset file is missing or damaged.");
                    return;
                }
                ChosenPreset = row.Preset;
                Complete(true);
            } finally {
                committing = false;
            }
        }

        internal static List<PresetRow> BuildRows(List<PresetErrorPackage> presetErrors) {
            var rows = new List<PresetRow>();
            foreach (PresetErrorPackage p in presetErrors) {
                (float modsCompatibility, float itemsCompatibility, float recipesCompatibility) = Compatibility(p);

                string breakdown =
                    "Mods:\n" +
                    string.Create(DisplayCulture.Format, $"     ({p.RequiredMods.Count - p.MissingMods.Count - p.WrongVersionMods.Count}) Correct\n") +
                    string.Create(DisplayCulture.Format, $"     ({p.MissingMods.Count}) Missing\n") +
                    string.Create(DisplayCulture.Format, $"     ({p.AddedMods.Count}) Extra\n") +
                    string.Create(DisplayCulture.Format, $"     ({p.WrongVersionMods.Count}) Wrong Version\n") +
                    "Items:\n" +
                    string.Create(DisplayCulture.Format, $"     ({p.RequiredItems.Count - p.MissingItems.Count}) Correct\n") +
                    string.Create(DisplayCulture.Format, $"     ({p.MissingItems.Count}) Missing\n") +
                    "Recipes:\n" +
                    string.Create(DisplayCulture.Format, $"     ({p.RequiredRecipes.Count - p.MissingRecipes.Count - p.IncorrectRecipes.Count}) Correct\n") +
                    string.Create(DisplayCulture.Format, $"     ({p.MissingRecipes.Count}) Missing\n") +
                    string.Create(DisplayCulture.Format, $"     ({p.IncorrectRecipes.Count}) Incorrect");

                rows.Add(new PresetRow(p.Preset,
                    Percent(modsCompatibility),
                    Percent(itemsCompatibility),
                    Percent(recipesCompatibility),
                    p.Preset.Name + "\n" + string.Join(", ", PresetMods(p)) + "\n\n" + breakdown));
            }
            return rows;
        }

        internal static (float Mods, float Items, float Recipes) Compatibility(PresetErrorPackage p) => (
            Fraction(p.RequiredMods.Count - p.MissingMods.Count - p.WrongVersionMods.Count - p.AddedMods.Count, p.RequiredMods.Count),
            Fraction(p.RequiredItems.Count - p.MissingItems.Count, p.RequiredItems.Count),
            Fraction(p.RequiredRecipes.Count - p.MissingRecipes.Count - p.IncorrectRecipes.Count, p.RequiredRecipes.Count));

        private static float Fraction(int matched, int required) => required == 0 ? 1f : (float)matched / required;

        //Mod entries read "name|version".
        private static IEnumerable<string> PresetMods(PresetErrorPackage p) =>
            p.RequiredMods.Except(p.MissingMods).Concat(p.AddedMods).Select(mod => mod.Split('|')[0]);

        private static string Percent(float fraction) => (fraction * 100).ToString("0", DisplayCulture.Format) + "\u2007%";

        internal ListBox PresetSelectionListViewControl => presetListView;
        internal Grid HeaderRowControl => headerRow;
        internal Button ConfirmationButtonControl => confirmationButton;
        internal Button CancellingButtonControl => cancellingButton;

        internal void SimulateSelectRow(Preset preset) =>
            presetListView.SelectedItem = ((List<PresetRow>)presetListView.ItemsSource!).Find(r => r.Preset == preset);

        internal void SimulateConfirmClick() => Async.Fire(CommitAsync(), nameof(CommitAsync));
        internal void SimulateDoubleClickRow(Preset preset) {
            SimulateSelectRow(preset);
            Async.Fire(CommitAsync(), nameof(CommitAsync));
        }
        internal void SimulateCancelClick() => Cancel();
    }

    internal sealed class PresetRow(Preset preset, string modsText, string itemsText, string recipesText, string tooltip) {
        public Preset Preset { get; } = preset;
        public string Name => Preset.Name;
        public string ModsText { get; } = modsText;
        public string ItemsText { get; } = itemsText;
        public string RecipesText { get; } = recipesText;
        public string Tooltip { get; } = tooltip;
        public string AccessibleName => $"{Name}, mods {Spoken(ModsText)}, items {Spoken(ItemsText)}, recipes {Spoken(RecipesText)}";

        //Type-select matches the name column.
        public override string ToString() => Name;

        private static string Spoken(string percent) => percent.Replace("\u2007%", " percent", StringComparison.Ordinal);
    }
}
