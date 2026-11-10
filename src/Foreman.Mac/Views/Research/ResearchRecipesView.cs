using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
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
    internal sealed class ResearchRecipeRow {
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required ListBoxItem Item { get; init; }
        public required Stepper Stepper { get; init; }
        public required Ellipse Dot { get; init; }
        public required RevertButton Revert { get; init; }
        public required TextBlock NameText { get; init; }
        public required TextBlock Caption { get; init; }
        public required Image IconImage { get; init; }
        public bool IsApplying { get; set; }
        public string AutomationName { get; set; } = "";
    }

    internal sealed class ResearchRecipesView : Grid {
        private const double ColumnGap = 8;
        private const string FooterSentence = "Research counts inside each recipe’s productivity limit.";

        private readonly ResearchViewModel viewModel;
        private readonly Func<DataCache?> cache;
        private readonly Dictionary<string, ResearchRecipeRow> rows = [];
        private readonly Dictionary<ListBoxItem, ResearchRecipeRow> rowsByItem = [];
        private readonly Dictionary<SKBitmap, Bitmap> icons = [];
        private readonly List<ResearchRecipeRow> shown = [];
        private readonly Button emptyAddButton;
        private readonly ResearchTableHeader header;
        private bool rendering;

        public ResearchRecipesView(ResearchViewModel viewModel, Func<DataCache?> cache, PopoverHost popoverHost) {
            this.viewModel = viewModel;
            this.cache = cache;
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");
            HeaderRow = new Grid { Height = 20 };
            header = new ResearchTableHeader(HeaderRow, "RecipesHeader", ["Recipe", "Productivity"], [HorizontalAlignment.Left, HorizontalAlignment.Right], 1, Render);

            Filter = new SearchField { Theme = ResearchTableKit.KitTheme("Kit.SearchField"), Width = 200, PlaceholderText = "Filter", VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(Filter, "Filter");
            Filter.PropertyChanged += (_, e) => {
                if (e.Property == TextBox.TextProperty && !rendering)
                    Render();
            };
            Filter.MoveDownRequested += (_, _) => FocusFirstRow();

            AddButton = new Button { Theme = ResearchTableKit.KitTheme("Kit.Button"), Content = "Add Recipe…", VerticalAlignment = VerticalAlignment.Center };
            AddButton.Click += (_, _) => OpenPopover(AddButton);

            var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 12, 0, 10), Children = { Filter, AddButton } };
            Grid.SetColumn(AddButton, 2);
            Children.Add(controls);

            Grid.SetRow(HeaderRow, 1);
            Children.Add(HeaderRow);

            Table = new ListBox { Name = "RecipesTable", Theme = ResearchTableKit.KitTheme("Kit.List"), SelectionMode = SelectionMode.Single, IsTextSearchEnabled = false };
            AutomationProperties.SetName(Table, "Recipe productivity table");
            KeyboardNavigation.SetTabNavigation(Table, KeyboardNavigationMode.Once);
            Table.AddHandler(KeyDownEvent, OnTableKeyDown, RoutingStrategies.Tunnel);

            (EmptyPanel, emptyAddButton) = NewPanel(StatePanelShape.Empty, "No recipe productivity", "A sync lists the recipes research has made more productive. You can also add one by hand.", "Add Recipe…", () => OpenPopover(emptyAddButton!));
            (NoMatchPanel, _) = NewPanel(StatePanelShape.NoResults, "", "Check the spelling, or clear the filter.", "Clear Filter", () => Filter.Text = "");
            var tableHost = new Grid { Children = { Table, EmptyPanel, NoMatchPanel } };
            EmptyPanel.IsVisible = false;
            NoMatchPanel.IsVisible = false;
            Grid.SetRow(tableHost, 2);
            Children.Add(tableHost);

            Footer = new TextBlock { Theme = ResearchTableKit.KitTheme("Kit.Text"), Classes = { "caption", "secondary" }, Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(Footer, 3);
            Children.Add(Footer);

            Popover = new AddRecipePopover(Candidates, AddRecipe);
            popoverHost.Surface.Children.Add(Popover.Frame);
        }

        public event EventHandler? Changed;

        public SearchField Filter { get; }
        public Button AddButton { get; }
        public Grid HeaderRow { get; }
        public ListBox Table { get; }
        public StatePanel EmptyPanel { get; }
        public StatePanel NoMatchPanel { get; }
        public TextBlock Footer { get; }
        public AddRecipePopover Popover { get; }

        public IReadOnlyList<string> ShownNames => [.. shown.Select(r => r.Name)];
        public string? SelectedName => Table.SelectedItem is ListBoxItem item && rowsByItem.TryGetValue(item, out ResearchRecipeRow? row) ? row.Name : null;
        public bool HasFilter => !string.IsNullOrEmpty(Filter.Text);

        public ResearchRecipeRow Row(string name) => rows[name];

        private static (StatePanel Panel, Button Button) NewPanel(StatePanelShape shape, string headline, string sentence, string label, Action action) {
            var button = new Button { Theme = ResearchTableKit.KitTheme("Kit.Button"), Content = label };
            ButtonRow.SetRole(button, ButtonRowRole.Default);
            button.Click += (_, _) => action();
            var row = new ButtonRow { Theme = ResearchTableKit.KitTheme("Kit.ButtonRow") };
            row.Items.Add(button);
            return (new StatePanel { Theme = ResearchTableKit.KitTheme("Kit.StatePanel"), Shape = shape, Headline = headline, Sentence = sentence, Buttons = row, MovesFocusOnAppear = false }, button);
        }

        private void FocusFirstRow() => ResearchTableKit.FocusFirstRow(Table, shown.Count > 0 ? shown[0].Item : null);

        public void FocusFilter() => Filter.Focus(NavigationMethod.Tab);

        public void Select(string name) {
            if (rows.TryGetValue(name, out ResearchRecipeRow? row) && shown.Contains(row))
                Table.SelectedItem = row.Item;
        }

        public void SortBy(int column) => header.SortBy(column);

        public void AddRecipe(string name) {
            viewModel.SetRecipe(name, 0);
            Changed?.Invoke(this, EventArgs.Empty);
            if (!rows.TryGetValue(name, out ResearchRecipeRow? row))
                return;
            if (!shown.Contains(row)) {
                Filter.Text = "";
                Render();
            }
            Table.SelectedItem = row.Item;
            row.Item.BringIntoView();
            FocusStepper(row, selectAll: true);
        }

        public void Refresh() {
            ResearchState state = viewModel.State;
            foreach (string stale in rows.Keys.Where(n => !state.RecipeProductivity.ContainsKey(n)).ToList()) {
                rowsByItem.Remove(rows[stale].Item);
                rows.Remove(stale);
            }
            foreach (string name in state.RecipeProductivity.Keys)
                if (!rows.ContainsKey(name))
                    rows[name] = BuildRow(name);
            foreach (ResearchRecipeRow row in rows.Values)
                RefreshRow(row, state);
            Render();
        }

        private IReadOnlyList<RecipeChoice> Candidates() {
            if (cache() is not DataCache data)
                return [];
            return [.. data.Recipes.Values
                .Where(r => !r.IsMissing && !r.Name.StartsWith("§§", StringComparison.Ordinal) && !viewModel.State.RecipeProductivity.ContainsKey(r.Name))
                .OrderBy(r => r.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .Select(r => new RecipeChoice(r.Name, r.FriendlyName, IconFor(r)))];
        }

        private Bitmap? IconFor(IRecipe recipe) => recipe.Icon is { } icon ? IconBaker.GetOrBake(icons, icon, 32) : null;

        private void OpenPopover(Control anchor) => Popover.Open(anchor);

        private ResearchRecipeRow BuildRow(string name) {
            IRecipe? recipe = cache() is { } data && data.Recipes.TryGetValue(name, out IRecipe? known) ? known : null;
            string display = recipe?.FriendlyName ?? name;

            var stepper = new Stepper {
                Theme = ResearchTableKit.KitTheme("Kit.Stepper"),
                Minimum = 0,
                Maximum = 100000,
                Increment = 10,
                Decimals = 2,
                Unit = "%",
                IsWide = true,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var revert = new RevertButton { Theme = ResearchTableKit.KitTheme("Kit.RevertButton"), IsReserved = true, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Opacity = 0, Transitions = Motion.Transition(TransitionKind.Fade, Motion.HoverDuration) };
            LiveBrush.Bind(dot, Shape.FillProperty, "Foreman.Color.Accent");

            var icon = new Image { Source = recipe is null ? null : IconFor(recipe), Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock { Theme = ResearchTableKit.KitTheme("Kit.Text"), Text = display, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if (recipe is null)
                LiveBrush.Bind(nameText, TextBlock.ForegroundProperty, "Foreman.Color.TextSecondary");
            var caption = new TextBlock { Theme = ResearchTableKit.KitTheme("Kit.Text"), Classes = { "caption", "tertiary" }, Text = "added by hand", VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { icon, nameText, caption } };

            var grid = new Grid { ColumnDefinitions = Columns(), ColumnSpacing = ColumnGap, Background = Brushes.Transparent, Children = { dot, nameCell, stepper, revert } };
            Grid.SetColumn(nameCell, 1);
            Grid.SetColumn(stepper, 2);
            Grid.SetColumn(revert, 3);
            var item = new ListBoxItem { Theme = ResearchTableKit.KitTheme("Kit.ListBoxItem"), Content = grid };
            item.AddHandler(GotFocusEvent, (_, _) => {
                if (!rendering)
                    Table.SelectedItem = item;
            }, RoutingStrategies.Bubble);

            var row = new ResearchRecipeRow { Name = name, DisplayName = display, Item = item, Stepper = stepper, Dot = dot, Revert = revert, NameText = nameText, Caption = caption, IconImage = icon };
            AutomationProperties.SetName(stepper, display + " productivity");
            stepper.PropertyChanged += (_, e) => {
                if (!row.IsApplying && e.Property == Stepper.ValueProperty)
                    Commit(row);
            };
            revert.Click += (_, _) => {
                bool removed = viewModel.State.IsRecipeHandAdded(name);
                viewModel.RevertRecipe(name);
                Changed?.Invoke(this, EventArgs.Empty);
                if (removed)
                    Table.Focus();
                else
                    FocusStepper(row, selectAll: false);
            };
            rowsByItem[item] = row;
            return row;
        }

        private static ColumnDefinitions Columns() => new("10,*,96,20");

        private void Commit(ResearchRecipeRow row) {
            viewModel.SetRecipe(row.Name, row.Stepper.Value);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshRow(ResearchRecipeRow row, ResearchState state) {
            string name = row.Name;
            bool added = state.IsRecipeHandAdded(name);
            bool edited = state.IsRecipeHandEdited(name);
            bool marked = added || edited;
            double percent = viewModel.RecipeDisplayPercent(name);

            row.IsApplying = true;
            row.Stepper.Value = percent;
            row.IsApplying = false;

            row.Dot.Opacity = marked ? 1 : 0;
            row.Revert.IsReserved = !marked;
            row.NameText.FontWeight = marked ? FontWeight.SemiBold : FontWeight.Normal;
            row.Caption.IsVisible = added;

            string spoken = percent.ToString("0.##", CultureInfo.InvariantCulture);
            row.AutomationName = $"{row.DisplayName}, {spoken} percent productivity" + (added ? ", added by hand" : edited ? ", edited by hand" : "");
            AutomationProperties.SetName(row.Item, row.AutomationName);
            AutomationProperties.SetName(row.Revert, (added ? "Remove " : "Revert ") + row.DisplayName);
            AutomationProperties.SetHelpText(row.Revert, added ? "Removes it from the list."
                : state.SyncedRecipeProductivity.TryGetValue(name, out double synced) ? $"Sets it back to {ResearchText.Percent(synced)} from the save." : null);
        }

        private IEnumerable<ResearchRecipeRow> Ordered(IEnumerable<ResearchRecipeRow> source) {
            if (header.SortColumn == 1) {
                IOrderedEnumerable<ResearchRecipeRow> byValue = header.SortAscending
                    ? source.OrderBy(r => r.Stepper.Value)
                    : source.OrderByDescending(r => r.Stepper.Value);
                return byValue.ThenBy(r => r.Name, StringComparer.Ordinal);
            }
            return header.SortAscending ? source.OrderBy(r => r.Name, StringComparer.Ordinal) : source.OrderByDescending(r => r.Name, StringComparer.Ordinal);
        }

        private static bool Matches(ResearchRecipeRow row, string query) =>
            query.Length == 0 || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase);

        private void Render() {
            string query = Filter.Text ?? "";
            List<ResearchRecipeRow> next = [.. Ordered(rows.Values.Where(r => Matches(r, query)))];
            string? selected = SelectedName;
            TextBox? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;

            rendering = true;
            if (!next.SequenceEqual(shown)) {
                shown.Clear();
                shown.AddRange(next);
                Table.Items.Clear();
                foreach (ResearchRecipeRow row in shown)
                    Table.Items.Add(row.Item);
            }
            Table.SelectedItem = selected is not null && rows.TryGetValue(selected, out ResearchRecipeRow? keep) && shown.Contains(keep) ? keep.Item : null;
            rendering = false;
            if (focused is not null && !focused.IsFocused && TopLevel.GetTopLevel(focused) is not null && focused != Filter)
                focused.Focus();

            int total = rows.Count;
            bool showTable = shown.Count > 0;
            Table.IsVisible = showTable;
            HeaderRow.IsVisible = showTable;
            EmptyPanel.IsVisible = total == 0;
            NoMatchPanel.IsVisible = total > 0 && !showTable;
            NoMatchPanel.Headline = $"No recipes match “{query}”";
            Footer.IsVisible = total > 0;
            Footer.Text = string.Create(DisplayCulture.Format, $"{total} {(total == 1 ? "recipe" : "recipes")}. {FooterSentence}");
            header.Build(this, Columns(), ColumnGap);
        }

        private void FocusStepper(ResearchRecipeRow row, bool selectAll) {
            (TopLevel.GetTopLevel(this) as Layoutable)?.UpdateLayout();
            TextBox? box = row.Stepper.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (box is null)
                return;
            box.Focus();
            if (selectAll)
                box.SelectAll();
        }

        private bool MoveWithinRow(Visual? source, bool backward) {
            if (source?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { } item || !rowsByItem.TryGetValue(item, out ResearchRecipeRow? row))
                return false;
            bool onItem = ReferenceEquals(source, item);
            bool onRevert = source.FindAncestorOfType<RevertButton>(includeSelf: true) is not null;
            bool revertStop = !row.Revert.IsReserved;
            if (backward) {
                if (onRevert)
                    FocusStepper(row, selectAll: false);
                else if (!onItem)
                    item.Focus(NavigationMethod.Tab);
                return onRevert || !onItem;
            }
            if (onItem)
                FocusStepper(row, selectAll: false);
            else if (!onRevert && revertStop)
                row.Revert.Focus(NavigationMethod.Tab);
            return onItem || (!onRevert && revertStop);
        }

        private void OnTableKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Tab && (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift) && MoveWithinRow(e.Source as Visual, e.KeyModifiers == KeyModifiers.Shift)) {
                e.Handled = true;
                return;
            }
            bool commandBackspace = e.Key == Key.Back && e.KeyModifiers == KeyModifiers.Meta;
            bool delete = e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox;
            if (!commandBackspace && !delete)
                return;
            ResearchRecipeRow? row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item && rowsByItem.TryGetValue(item, out ResearchRecipeRow? hit)
                ? hit
                : SelectedName is { } name ? rows[name] : null;
            if (row is null)
                return;
            if (viewModel.DiscardRecipeEdit(row.Name)) {
                Changed?.Invoke(this, EventArgs.Empty);
                if (!rows.ContainsKey(row.Name))
                    Table.Focus();
                e.Handled = true;
            } else if (delete)
                e.Handled = true;
        }
    }
}
