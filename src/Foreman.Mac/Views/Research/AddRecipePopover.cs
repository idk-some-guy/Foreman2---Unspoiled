using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Views.Research {
    internal sealed record RecipeChoice(string Name, string FriendlyName, Bitmap? Icon);

    internal sealed class AddRecipePopover {
        public const int MaxRows = 8;
        public const double Width = 272;

        private readonly Func<IReadOnlyList<RecipeChoice>> candidates;
        private readonly Action<string> chosen;
        private readonly ListBox list;
        private readonly List<RecipeChoice> shown = [];

        public AddRecipePopover(Func<IReadOnlyList<RecipeChoice>> candidates, Action<string> chosen) {
            this.candidates = candidates;
            this.chosen = chosen;

            Search = new SearchField { Theme = KitTheme("Kit.SearchField"), PlaceholderText = "Find a recipe" };
            AutomationProperties.SetName(Search, "Find a recipe");
            Search.PropertyChanged += (_, e) => {
                if (e.Property == TextBox.TextProperty)
                    Rebuild();
            };
            Search.SearchCommitted += (_, _) => ChooseSelected();
            Search.MoveDownRequested += (_, _) => MoveSelection(1);
            Search.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => {
                if (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.None) {
                    MoveSelection(-1);
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);

            list = new ListBox {
                Theme = KitTheme("Kit.List"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                SelectionMode = SelectionMode.Single,
                Focusable = false,
            };
            AutomationProperties.SetName(list, "Recipes");

            NoMatch = new TextBlock { Theme = KitTheme("Kit.Text"), Classes = { "callout", "secondary" }, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 4) };

            var content = new Border {
                Padding = new Thickness(10),
                Child = new StackPanel { Spacing = 8, Children = { Search, list, NoMatch } },
            };
            AutomationProperties.SetName(content, "Add Recipe");
            Frame = new PopoverFrame { Name = "AddRecipePopover", Theme = KitTheme("Kit.PopoverFrame"), Content = content, PanelWidth = Width };
        }

        public PopoverFrame Frame { get; }
        public SearchField Search { get; }
        public TextBlock NoMatch { get; }
        public IReadOnlyList<string> ShownNames => [.. shown.Select(c => c.Name)];
        public string? SelectedName => list.SelectedIndex is var index && index >= 0 && index < shown.Count ? shown[index].Name : null;

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"AddRecipePopover: kit theme '{key}' not found.");
        }

        public void Open(Control anchor) {
            Search.Text = "";
            Rebuild();
            Frame.Anchor = anchor;
            Frame.IsOpen = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                if (Frame.IsOpen)
                    Search.Focus();
            });
        }

        public void Choose(string name) {
            Frame.IsOpen = false;
            chosen(name);
        }

        private void ChooseSelected() {
            if (SelectedName is { } name)
                Choose(name);
        }

        private void MoveSelection(int step) {
            if (shown.Count == 0)
                return;
            list.SelectedIndex = Math.Clamp(list.SelectedIndex + step, 0, shown.Count - 1);
        }

        private void Rebuild() {
            string query = Search.Text?.Trim() ?? "";
            shown.Clear();
            shown.AddRange(candidates()
                .Where(c => query.Length == 0 || c.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(MaxRows));

            list.Items.Clear();
            foreach (RecipeChoice choice in shown)
                list.Items.Add(BuildItem(choice));
            list.SelectedIndex = shown.Count > 0 ? 0 : -1;
            list.IsVisible = shown.Count > 0;
            NoMatch.IsVisible = shown.Count == 0;
            NoMatch.Text = $"No recipe matches “{query}”.";
        }

        private ListBoxItem BuildItem(RecipeChoice choice) {
            var icon = new Image { Source = choice.Icon, Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Theme = KitTheme("Kit.Text"), Text = choice.FriendlyName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { icon, name } };
            var item = new ListBoxItem { Theme = KitTheme("Kit.ListBoxItem"), Content = row };
            AutomationProperties.SetName(item, choice.FriendlyName);
            item.PointerReleased += (_, e) => {
                if (e.InitialPressMouseButton == MouseButton.Left) {
                    Choose(choice.Name);
                    e.Handled = true;
                }
            };
            return item;
        }
    }
}
