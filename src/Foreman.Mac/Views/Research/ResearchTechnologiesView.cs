using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.ViewModels;
using Foreman.Models.Research;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Foreman.Mac.Views.Research {
    internal sealed class ResearchTechnologyRow {
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required ResearchTechnology Technology { get; init; }
        public required ListBoxItem Item { get; init; }
        public required TextBlock NameText { get; init; }
        public required TextBlock LevelsText { get; init; }
        public required TextBlock StateText { get; init; }
        public required Image IconImage { get; init; }
        public int StateRank => Technology.Researched ? 0 : Technology.IsPartlyResearched ? 1 : 2;
    }

    internal sealed class ResearchTechnologiesView : Grid {
        private const double ColumnGap = 8;

        private readonly ResearchViewModel viewModel;
        private readonly Func<DataCache?> cache;
        private readonly Dictionary<string, ResearchTechnologyRow> rows = [];
        private readonly Dictionary<ListBoxItem, ResearchTechnologyRow> rowsByItem = [];
        private readonly Dictionary<SKBitmap, Bitmap> icons = [];
        private readonly List<ResearchTechnologyRow> shown = [];
        private IReadOnlyList<ResearchTechnology>? built;
        private readonly ResearchTableHeader header;
        private bool rendering;

        public ResearchTechnologiesView(ResearchViewModel viewModel, Func<DataCache?> cache, AppSettings? settings, Action chooseSave) {
            this.viewModel = viewModel;
            this.cache = cache;
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");
            HeaderRow = new Grid { Height = 20 };
            header = new ResearchTableHeader(HeaderRow, "TechnologiesHeader", ["Technology", "Levels", "State"], [HorizontalAlignment.Left, HorizontalAlignment.Right, HorizontalAlignment.Left], 0, Render);

            StateFilter = new Segmented { Theme = ResearchTableKit.KitTheme("Kit.Segmented"), VerticalAlignment = VerticalAlignment.Center };
            StateFilter.Items.AddRange(["All", "Researched", "Not researched"]);
            StateFilter.SelectedIndex = Math.Clamp(settings?.ResearchTechnologyFilter ?? 0, 0, 2);
            AutomationProperties.SetName(StateFilter, "Technology state");
            StateFilter.PropertyChanged += (_, e) => {
                if (e.Property != Segmented.SelectedIndexProperty)
                    return;
                if (settings is not null)
                    settings.ResearchTechnologyFilter = StateFilter.SelectedIndex;
                Render();
            };

            Filter = new SearchField { Theme = ResearchTableKit.KitTheme("Kit.SearchField"), Width = 200, PlaceholderText = "Filter", VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(Filter, "Filter");
            Filter.PropertyChanged += (_, e) => {
                if (e.Property == TextBox.TextProperty && !rendering)
                    Render();
            };
            Filter.MoveDownRequested += (_, _) => FocusFirstRow();

            var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 12, 0, 10), Children = { StateFilter, Filter } };
            Grid.SetColumn(Filter, 2);
            Children.Add(controls);

            Grid.SetRow(HeaderRow, 1);
            Children.Add(HeaderRow);

            Table = new ListBox { Name = "TechnologiesTable", Theme = ResearchTableKit.KitTheme("Kit.List"), SelectionMode = SelectionMode.Single, IsTextSearchEnabled = true };
            AutomationProperties.SetName(Table, "Technology table");
            KeyboardNavigation.SetTabNavigation(Table, KeyboardNavigationMode.Once);

            EmptyPanel = NewPanel(StatePanelShape.Empty, "No technologies yet", "Sync from a save to list its technologies and their levels.", "Choose Save…", chooseSave);
            NoMatchPanel = NewPanel(StatePanelShape.NoResults, "", "Check the spelling, or clear the filter.", "Clear Filter", () => Filter.Text = "");
            HiddenPanel = NewPanel(StatePanelShape.NoResults, "No technologies to show", "Every technology is hidden by the filter.", "Show All", () => StateFilter.SelectedIndex = 0);
            var tableHost = new Grid { Children = { Table, EmptyPanel, NoMatchPanel, HiddenPanel } };
            EmptyPanel.IsVisible = false;
            NoMatchPanel.IsVisible = false;
            HiddenPanel.IsVisible = false;
            Grid.SetRow(tableHost, 2);
            Children.Add(tableHost);

            Footer = new TextBlock { Theme = ResearchTableKit.KitTheme("Kit.Text"), Classes = { "caption", "secondary" }, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontFeatures = FontFeatureCollection.Parse("tnum") };
            Grid.SetRow(Footer, 3);
            Children.Add(Footer);
        }

        public Segmented StateFilter { get; }
        public SearchField Filter { get; }
        public Grid HeaderRow { get; }
        public ListBox Table { get; }
        public StatePanel EmptyPanel { get; }
        public StatePanel NoMatchPanel { get; }
        public StatePanel HiddenPanel { get; }
        public TextBlock Footer { get; }

        public IReadOnlyList<string> ShownNames => [.. shown.Select(r => r.Name)];
        public string? SelectedName => Table.SelectedItem is ListBoxItem item && rowsByItem.TryGetValue(item, out ResearchTechnologyRow? row) ? row.Name : null;
        public bool HasFilter => !string.IsNullOrEmpty(Filter.Text);

        public ResearchTechnologyRow Row(string name) => rows[name];

        private static StatePanel NewPanel(StatePanelShape shape, string headline, string sentence, string label, Action action) {
            var button = new Button { Theme = ResearchTableKit.KitTheme("Kit.Button"), Content = label };
            ButtonRow.SetRole(button, ButtonRowRole.Default);
            button.Click += (_, _) => action();
            var row = new ButtonRow { Theme = ResearchTableKit.KitTheme("Kit.ButtonRow") };
            row.Items.Add(button);
            return new StatePanel { Theme = ResearchTableKit.KitTheme("Kit.StatePanel"), Shape = shape, Headline = headline, Sentence = sentence, Buttons = row, MovesFocusOnAppear = false };
        }

        private void FocusFirstRow() => ResearchTableKit.FocusFirstRow(Table, shown.Count > 0 ? shown[0].Item : null);

        public void FocusFilter() => Filter.Focus(NavigationMethod.Tab);

        public void SelectStateFilter(int index) => StateFilter.SelectedIndex = Math.Clamp(index, 0, 2);

        public void SortBy(int column) => header.SortBy(column);

        public void Refresh() {
            IReadOnlyList<ResearchTechnology> technologies = viewModel.State.Technologies;
            if (built is null || !built.SequenceEqual(technologies)) {
                rows.Clear();
                rowsByItem.Clear();
                foreach (ResearchTechnology technology in technologies)
                    rows[technology.Name] = BuildRow(technology);
                built = technologies;
                shown.Clear();
            }
            Render();
        }

        private ResearchTechnologyRow BuildRow(ResearchTechnology technology) {
            ITechnology? known = cache() is { } data && data.Technologies.TryGetValue(technology.Name, out ITechnology? hit) ? hit : null;
            string display = known?.FriendlyName ?? technology.Name;

            var icon = new Image { Source = known?.Icon is { } bitmap ? IconBaker.GetOrBake(icons, bitmap, 32) : null, Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock { Theme = ResearchTableKit.KitTheme("Kit.Text"), Text = display, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if (known is null)
                LiveBrush.Bind(nameText, TextBlock.ForegroundProperty, "Foreman.Color.TextSecondary");
            var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { icon, nameText } };

            var levelsText = new TextBlock {
                Theme = ResearchTableKit.KitTheme("Kit.Text"), Text = LevelsOf(technology), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                FontFeatures = FontFeatureCollection.Parse("tnum"), TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var stateText = new TextBlock { Theme = ResearchTableKit.KitTheme("Kit.Text"), Text = StateOf(technology), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if (!technology.Researched && !technology.IsPartlyResearched)
                LiveBrush.Bind(stateText, TextBlock.ForegroundProperty, "Foreman.Color.TextSecondary");

            var grid = new Grid { ColumnDefinitions = Columns(), ColumnSpacing = ColumnGap, Background = Brushes.Transparent, Children = { nameCell, levelsText, stateText } };
            Grid.SetColumn(levelsText, 1);
            Grid.SetColumn(stateText, 2);
            var item = new ListBoxItem { Theme = ResearchTableKit.KitTheme("Kit.ListBoxItem"), Content = grid };
            AutomationProperties.SetName(item, SpokenName(display, technology));
            Avalonia.Controls.Primitives.TextSearch.SetText(item, display);
            item.AddHandler(GotFocusEvent, (_, _) => {
                if (!rendering)
                    Table.SelectedItem = item;
            }, Avalonia.Interactivity.RoutingStrategies.Bubble);

            var row = new ResearchTechnologyRow { Name = technology.Name, DisplayName = display, Technology = technology, Item = item, NameText = nameText, LevelsText = levelsText, StateText = stateText, IconImage = icon };
            rowsByItem[item] = row;
            return row;
        }

        private static ColumnDefinitions Columns() => new("*,90,130");

        private static string LevelsOf(ResearchTechnology technology) {
            if (!technology.IsLeveled)
                return "";
            string max = technology.IsUnlimited ? "∞" : technology.LevelCount.ToString(CultureInfo.InvariantCulture);
            return string.Create(DisplayCulture.Format, $"{technology.LevelsDone} of {max}");
        }

        private static string StateOf(ResearchTechnology technology) =>
            technology.Researched ? "Researched" : technology.IsPartlyResearched ? "Partly researched" : "Not researched";

        private static string SpokenName(string display, ResearchTechnology technology) {
            if (technology.Researched)
                return display + ", researched";
            if (!technology.IsPartlyResearched)
                return display + ", not researched";
            return technology.IsUnlimited
                ? string.Create(DisplayCulture.Format, $"{display}, {technology.LevelsDone} levels done, no maximum, partly researched")
                : string.Create(DisplayCulture.Format, $"{display}, {technology.LevelsDone} of {technology.LevelCount} levels, partly researched");
        }

        private IEnumerable<ResearchTechnologyRow> Ordered(IEnumerable<ResearchTechnologyRow> source) {
            IOrderedEnumerable<ResearchTechnologyRow> ordered = header.SortColumn switch {
                1 => header.SortAscending ? source.OrderBy(r => r.Technology.LevelsDone) : source.OrderByDescending(r => r.Technology.LevelsDone),
                2 => header.SortAscending ? source.OrderBy(r => r.StateRank) : source.OrderByDescending(r => r.StateRank),
                _ => header.SortAscending ? source.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase) : source.OrderByDescending(r => r.DisplayName, StringComparer.OrdinalIgnoreCase),
            };
            return header.SortColumn == 0
                ? ordered.ThenBy(r => r.Name, StringComparer.Ordinal)
                : ordered.ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.Ordinal);
        }

        private bool PassesState(ResearchTechnologyRow row) => StateFilter.SelectedIndex switch {
            1 => row.Technology.Researched || row.Technology.IsPartlyResearched,
            2 => !row.Technology.Researched && !row.Technology.IsPartlyResearched,
            _ => true,
        };

        private static bool Matches(ResearchTechnologyRow row, string query) =>
            query.Length == 0 || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase);

        private void Render() {
            string query = Filter.Text ?? "";
            List<ResearchTechnologyRow> next = [.. Ordered(rows.Values.Where(r => PassesState(r) && Matches(r, query)))];
            string? selected = SelectedName;

            rendering = true;
            if (!next.SequenceEqual(shown)) {
                shown.Clear();
                shown.AddRange(next);
                Table.Items.Clear();
                foreach (ResearchTechnologyRow row in shown)
                    Table.Items.Add(row.Item);
            }
            Table.SelectedItem = selected is not null && rows.TryGetValue(selected, out ResearchTechnologyRow? keep) && shown.Contains(keep) ? keep.Item : null;
            rendering = false;

            int total = rows.Count;
            bool showTable = shown.Count > 0;
            Table.IsVisible = showTable;
            HeaderRow.IsVisible = showTable;
            EmptyPanel.IsVisible = total == 0;
            NoMatchPanel.IsVisible = total > 0 && !showTable && query.Length > 0;
            HiddenPanel.IsVisible = total > 0 && !showTable && query.Length == 0;
            NoMatchPanel.Headline = $"No technologies match “{query}”";
            Footer.IsVisible = total > 0;
            int researched = rows.Values.Count(r => r.Technology.Researched);
            Footer.Text = string.Create(DisplayCulture.Format, $"{total} {(total == 1 ? "technology" : "technologies")} · {researched} researched");
            header.Build(this, Columns(), ColumnGap);
        }
    }
}
