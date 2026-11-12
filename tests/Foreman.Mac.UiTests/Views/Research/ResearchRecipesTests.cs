using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Research;
using Foreman.Models.Research;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Foreman.Mac.UiTests.Views.Research.ResearchWindowTests;

namespace Foreman.Mac.UiTests.Views.Research {
    public class ResearchRecipesTests {
        private static TextBox ValueBox(Stepper stepper) =>
            stepper.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_ValueBox");

        private static void Type(Stepper stepper, string text) {
            TextBox box = ValueBox(stepper);
            box.Text = text;
            CanvasKeys.KeyDownOn(box, Key.Enter);
        }

        private static void Click(Button button) {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        private static string? SpokenValue(Control control) =>
            ControlAutomationPeer.CreatePeerForElement(control).GetProvider<IValueProvider>()?.Value;

        private static Avalonia.Media.Color Token(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value), key);
            return Assert.IsType<Avalonia.Media.Color>(value);
        }

        private static Button PanelButton(Foreman.Mac.Kit.Composites.StatePanel panel) => panel.Buttons!.Items.OfType<Button>().Single();

        private static ResearchState ThreeRecipes() => Synced(recipes: new() { ["steel-plate"] = 0.2, ["plastic-bar"] = 0.1, ["processing-unit"] = 0.3 });

        private static ResearchWindow OpenRecipes(Rig rig, ResearchState state) {
            ResearchWindow window = rig.Open(state);
            window.SelectSegment(1);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            return window;
        }

        private static void OpenPopover(ResearchWindow window) {
            Click(window.RecipesView.AddButton);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void Recipes_AddByHand_ThenRevertRemoves() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());
            ManualTaskScheduler.Defer(rig.Graph);
            AddRecipePopover popover = window.RecipesView.Popover;

            OpenPopover(window);
            Assert.True(popover.Frame.IsOpen);
            Assert.Equal(272, popover.Frame.PanelWidth);
            Assert.Equal(272, popover.Frame.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel").Bounds.Width);
            Assert.Equal("Find a recipe", popover.Search.PlaceholderText);
            Assert.Equal(8, popover.ShownNames.Count);

            popover.Search.Text = "steel";
            Assert.NotEmpty(popover.ShownNames);
            Assert.DoesNotContain("steel-plate", popover.ShownNames);
            Assert.All(popover.ShownNames, n => Assert.Contains("steel", n));

            popover.Search.Text = "gear";
            Assert.Equal(["iron-gear-wheel"], popover.ShownNames);
            CanvasKeys.KeyDownOn(popover.Search, Key.Return);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.False(popover.Frame.IsOpen);
            Assert.Equal(0, rig.Graph.Research.RecipeProductivity["iron-gear-wheel"]);
            Assert.True(rig.Graph.Research.IsRecipeHandAdded("iron-gear-wheel"));
            Assert.Equal(1, rig.Shell.SolveWorker.Pending);

            ResearchRecipeRow row = window.RecipesView.Row("iron-gear-wheel");
            Assert.Equal("Iron gear wheel", row.NameText.Text);
            Assert.Equal("added by hand", row.Caption.Text);
            Assert.True(row.Caption.IsVisible);
            TextBox box = ValueBox(row.Stepper);
            Assert.True(box.IsFocused);
            Assert.Equal(0, box.SelectionStart);
            Assert.Equal(box.Text!.Length, box.SelectionEnd);
            Assert.Equal(1, row.Dot.Opacity);
            Assert.Equal("Remove Iron gear wheel", AutomationProperties.GetName(row.Revert));

            Click(row.Revert);

            Assert.False(rig.Graph.Research.RecipeProductivity.ContainsKey("iron-gear-wheel"));
            Assert.DoesNotContain("iron-gear-wheel", window.RecipesView.ShownNames);
        }

        [AvaloniaFact]
        public void Recipes_AddByHand_ClickingARowAdds() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());
            AddRecipePopover popover = window.RecipesView.Popover;
            OpenPopover(window);
            popover.Search.Text = "gear";

            popover.Choose("iron-gear-wheel");
            Dispatcher.UIThread.RunJobs();

            Assert.True(rig.Graph.Research.RecipeProductivity.ContainsKey("iron-gear-wheel"));
            Assert.False(popover.Frame.IsOpen);
        }

        [AvaloniaFact]
        public void Recipes_Footer() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());

            Assert.Equal("3 recipes. Research counts inside each recipe’s productivity limit.", window.RecipesView.Footer.Text);
            Assert.True(window.RecipesView.Footer.IsVisible);

            window.RecipesView.Filter.Text = "steel";
            Assert.Equal("3 recipes. Research counts inside each recipe’s productivity limit.", window.RecipesView.Footer.Text);
        }

        [AvaloniaFact]
        public void Recipes_Columns_AndOrder() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());

            Assert.Equal(["plastic-bar", "processing-unit", "steel-plate"], window.RecipesView.ShownNames);
            Assert.Equal("Filter", window.RecipesView.Filter.PlaceholderText);
            Assert.Equal(200, window.RecipesView.Filter.Bounds.Width);
            Assert.Equal("Add Recipe…", window.RecipesView.AddButton.Content);
            ColumnDefinitions columns = window.RecipesView.HeaderRow.ColumnDefinitions;
            Assert.Equal(4, columns.Count);
            Assert.Equal([10d, 96d, 20d], new[] { columns[0], columns[2], columns[3] }.Select(c => c.Width.Value));
            Assert.Equal(20, window.RecipesView.HeaderRow.Bounds.Height);

            ResearchRecipeRow row = window.RecipesView.Row("steel-plate");
            Assert.Equal("%", row.Stepper.Unit);
            Assert.Equal(10, row.Stepper.Increment);
            Assert.Equal(0, row.Stepper.Minimum);
            Assert.Equal(100000, row.Stepper.Maximum);
            Assert.Equal(2, row.Stepper.Decimals);
            Assert.True(row.Stepper.Bounds.Width <= 96);
        }

        [AvaloniaFact]
        public void Recipes_HeaderClick_Sorts() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());

            window.RecipesView.SortBy(0);
            Assert.Equal(["steel-plate", "processing-unit", "plastic-bar"], window.RecipesView.ShownNames);
            window.RecipesView.SortBy(1);
            Assert.Equal(["processing-unit", "steel-plate", "plastic-bar"], window.RecipesView.ShownNames);
            window.RecipesView.SortBy(1);
            Assert.Equal(["plastic-bar", "steel-plate", "processing-unit"], window.RecipesView.ShownNames);
        }

        [AvaloniaFact]
        public void Recipes_NotInPreset_ShowsInternalName() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, Synced(recipes: new() { ["modded-widget"] = 0.1, ["steel-plate"] = 0.2 }));

            ResearchRecipeRow unknown = window.RecipesView.Row("modded-widget");
            Assert.Equal("modded-widget", unknown.NameText.Text);
            Assert.Equal(Token("Foreman.Color.TextSecondary"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(unknown.NameText.Foreground).Color);
            Assert.Null(unknown.IconImage.Source);

            ResearchRecipeRow known = window.RecipesView.Row("steel-plate");
            Assert.Equal("Steel plate", known.NameText.Text);
            Assert.NotNull(known.IconImage.Source);
            Assert.Equal(16, known.IconImage.Width);
            Assert.NotEqual(Token("Foreman.Color.TextSecondary"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(known.NameText.Foreground).Color);
        }

        [AvaloniaFact]
        public void Recipes_Empty() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, Synced());

            Assert.True(window.RecipesView.EmptyPanel.IsVisible);
            Assert.False(window.RecipesView.NoMatchPanel.IsVisible);
            Assert.False(window.RecipesView.Table.IsVisible);
            Assert.False(window.RecipesView.Footer.IsVisible);
            Assert.Equal("No recipe productivity", window.RecipesView.EmptyPanel.Headline);
            Assert.Equal("A sync lists the recipes research has made more productive. You can also add one by hand.", window.RecipesView.EmptyPanel.Sentence);
            Assert.Equal("Add Recipe…", PanelButton(window.RecipesView.EmptyPanel).Content);

            PanelButton(window.RecipesView.EmptyPanel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.RecipesView.Popover.Frame.IsOpen);
        }

        [AvaloniaFact]
        public void Recipes_NoResults() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());

            window.RecipesView.Filter.Text = "zz";
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.RecipesView.NoMatchPanel.IsVisible);
            Assert.False(window.RecipesView.EmptyPanel.IsVisible);
            Assert.False(window.RecipesView.Table.IsVisible);
            Assert.Equal("No recipes match “zz”", window.RecipesView.NoMatchPanel.Headline);
            Assert.Equal("Check the spelling, or clear the filter.", window.RecipesView.NoMatchPanel.Sentence);
            Assert.Equal("Clear Filter", PanelButton(window.RecipesView.NoMatchPanel).Content);

            PanelButton(window.RecipesView.NoMatchPanel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", window.RecipesView.Filter.Text ?? "");
            Assert.False(window.RecipesView.NoMatchPanel.IsVisible);
            Assert.Equal(3, window.RecipesView.ShownNames.Count);
        }

        [AvaloniaFact]
        public void Recipes_Filter_MatchesFriendlyNames() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());

            window.RecipesView.Filter.Text = "STEEL";
            Assert.Equal(["steel-plate"], window.RecipesView.ShownNames);
        }

        [AvaloniaFact]
        public void Recipes_PopoverNoMatch() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());
            AddRecipePopover popover = window.RecipesView.Popover;
            OpenPopover(window);

            popover.Search.Text = "zz";

            Assert.Empty(popover.ShownNames);
            Assert.True(popover.NoMatch.IsVisible);
            Assert.Equal("No recipe matches “zz”.", popover.NoMatch.Text);

            CanvasKeys.KeyDownOn(popover.Search, Key.Return);
            Assert.True(popover.Frame.IsOpen);
            Assert.Equal(3, rig.Graph.Research.RecipeProductivity.Count);

            popover.Search.Text = "gear";
            Assert.False(popover.NoMatch.IsVisible);

            CanvasKeys.KeyDownOn(popover.Search, Key.Escape);
            Assert.False(popover.Frame.IsOpen);
            Assert.True(window.IsVisible);
        }

        [AvaloniaFact]
        public void Recipes_Delete() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, Synced(recipes: new() { ["steel-plate"] = 0.2, ["plastic-bar"] = 0.1 }));
            window.RecipesView.AddRecipe("iron-gear-wheel");
            Type(window.RecipesView.Row("steel-plate").Stepper, "50");
            Assert.True(rig.Graph.Research.IsRecipeHandEdited("steel-plate"));

            window.RecipesView.Select("iron-gear-wheel");
            CanvasKeys.KeyDownOn(window.RecipesView.Table, Key.Delete);
            Assert.False(rig.Graph.Research.RecipeProductivity.ContainsKey("iron-gear-wheel"));

            window.RecipesView.Select("steel-plate");
            CanvasKeys.KeyDownOn(window.RecipesView.Table, Key.Delete);
            Assert.Equal(0.2, rig.Graph.Research.RecipeProductivity["steel-plate"]);
            Assert.False(rig.Graph.Research.IsRecipeHandEdited("steel-plate"));

            ResearchState before = rig.Graph.Research;
            window.RecipesView.Select("plastic-bar");
            CanvasKeys.KeyDownOn(window.RecipesView.Table, Key.Delete);
            Assert.Same(before, rig.Graph.Research);
            Assert.Equal(0.1, rig.Graph.Research.RecipeProductivity["plastic-bar"]);
        }

        [AvaloniaFact]
        public void Recipes_CommandBackspace_RevertsFocusedRow() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());
            Type(window.RecipesView.Row("steel-plate").Stepper, "50");
            TextBox box = ValueBox(window.RecipesView.Row("steel-plate").Stepper);
            box.Focus();

            CanvasKeys.KeyDownOn(box, Key.Back, KeyModifiers.Meta);

            Assert.Equal(0.2, rig.Graph.Research.RecipeProductivity["steel-plate"]);
        }

        [AvaloniaFact]
        public void Recipes_VoiceOver() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());
            ResearchRecipeRow steel = window.RecipesView.Row("steel-plate");

            Assert.Equal("Steel plate, 20 percent productivity", steel.AutomationName);
            Assert.Equal("Steel plate, 20 percent productivity", AutomationProperties.GetName(steel.Item));
            Assert.Equal("20 percent", SpokenValue(steel.Stepper));
            Assert.Equal("Revert Steel plate", AutomationProperties.GetName(steel.Revert));
            Assert.Equal("Sets it back to 20 % from the save.", AutomationProperties.GetHelpText(steel.Revert));

            Type(steel.Stepper, "35.5");
            Assert.Equal("Steel plate, 35.5 percent productivity, edited by hand", steel.AutomationName);
            Assert.Equal("Steel plate, 35.5 percent productivity, edited by hand", AutomationProperties.GetName(steel.Item));

            window.RecipesView.AddRecipe("iron-gear-wheel");
            Assert.Equal("Iron gear wheel, 0 percent productivity, added by hand", window.RecipesView.Row("iron-gear-wheel").AutomationName);
        }

        [AvaloniaFact]
        public void Recipes_PercentStepper() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, Synced(recipes: new() { ["steel-plate"] = 0.2 }));
            ManualTaskScheduler.Defer(rig.Graph);
            ResearchRecipeRow row = window.RecipesView.Row("steel-plate");

            Assert.Equal(20, row.Stepper.Value);
            Assert.Equal("20.00", ValueBox(row.Stepper).Text);
            Assert.Equal(0, row.Dot.Opacity);
            Assert.True(row.Revert.IsReserved);

            Type(row.Stepper, "35");
            AnimationClock.Settle(250);

            Assert.Equal(0.35, rig.Graph.Research.RecipeProductivity["steel-plate"]);
            Assert.True(rig.Graph.Research.IsRecipeHandEdited("steel-plate"));
            Assert.Equal(1, rig.Shell.SolveWorker.Pending);
            Assert.Equal(1, row.Dot.Opacity);
            Assert.False(row.Revert.IsReserved);
            Assert.False(row.Caption.IsVisible);
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, row.NameText.FontWeight);

            Click(row.Revert);
            AnimationClock.Settle(250);

            Assert.Equal(0.2, rig.Graph.Research.RecipeProductivity["steel-plate"]);
            Assert.Equal(20, row.Stepper.Value);
            Assert.Equal(0, row.Dot.Opacity);
            Assert.True(row.Revert.IsReserved);
        }

        [AvaloniaFact]
        public void Recipes_NoOpCommit_DoesNotQueueASolve() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, Synced(recipes: new() { ["steel-plate"] = 0.2 }));
            ManualTaskScheduler.Defer(rig.Graph);

            Type(window.RecipesView.Row("steel-plate").Stepper, "20");

            Assert.Equal(0, rig.Shell.SolveWorker.Pending);
        }

        [AvaloniaFact]
        public void Recipes_ExternalChange_RefreshesTheRows() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());

            rig.Graph.Research = Synced(recipes: new() { ["steel-plate"] = 0.5, ["copper-cable"] = 0.1 });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["copper-cable", "steel-plate"], window.RecipesView.ShownNames);
            Assert.Equal(50, window.RecipesView.Row("steel-plate").Stepper.Value);
        }

        [AvaloniaFact]
        public void Recipes_CommandF_FocusesFilter_EscapeClearsThenCloses() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());

            CanvasKeys.KeyDownOn(window, Key.F, KeyModifiers.Meta);
            Assert.True(window.RecipesView.Filter.IsFocused);

            window.RecipesView.Filter.Text = "steel";
            CanvasKeys.KeyDownOn(window, Key.Escape);
            Assert.Equal("", window.RecipesView.Filter.Text ?? "");
            Assert.True(window.IsVisible);
            Assert.Equal(3, window.RecipesView.ShownNames.Count);

            CanvasKeys.KeyDownOn(window, Key.Escape);
            Assert.False(window.IsVisible);
        }

        [AvaloniaFact]
        public void Recipes_Escape_InStepper_DoesNotClearTheFilter() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());
            window.RecipesView.Filter.Text = "steel";
            TextBox box = ValueBox(window.RecipesView.Row("steel-plate").Stepper);
            box.Focus();

            CanvasKeys.KeyDownOn(box, Key.Escape);

            Assert.Equal("steel", window.RecipesView.Filter.Text);
            Assert.True(window.IsVisible);
        }

        [AvaloniaFact]
        public void Recipes_ArrowsMoveRows() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());

            window.RecipesView.Select("plastic-bar");
            window.RecipesView.Row("plastic-bar").Item.Focus();
            CanvasKeys.KeyDownOn(window.RecipesView.Row("plastic-bar").Item, Key.Down);
            Assert.Equal("processing-unit", window.RecipesView.SelectedName);
        }

        [AvaloniaFact]
        public void Recipes_Tab_EntersTheTableOnce_ThenTheSelectedRowsStepper_ThenLeaves() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());
            ResearchRecipesView view = window.RecipesView;
            string[] names = [.. view.ShownNames];
            Assert.Equal(3, names.Length);

            view.FocusFilter();
            Assert.True(view.Filter.IsKeyboardFocusWithin);
            TabFromFocus(window);
            Assert.True(view.AddButton.IsKeyboardFocusWithin);
            TabFromFocus(window);
            Assert.True(view.Table.IsKeyboardFocusWithin);
            Assert.Same(view.Row(names[0]).Item, window.FocusManager!.GetFocusedElement());

            CanvasKeys.KeyDownOn(view.Row(names[0]).Item, Key.Down);
            Assert.Equal(names[1], view.SelectedName);
            TabFromFocus(window);
            Assert.True(ValueBox(view.Row(names[1]).Stepper).IsFocused);

            TabFromFocus(window);
            Assert.False(view.Table.IsKeyboardFocusWithin, $"Tab should leave the table, reached {window.FocusManager!.GetFocusedElement()}");
            Assert.True(window.SegmentControl.IsKeyboardFocusWithin);
        }

        [AvaloniaFact]
        public void Recipes_Tab_FromAnEditedRowsStepper_ReachesItsRevert_ThenLeaves() {
            using var rig = new Rig();
            ResearchWindow window = OpenRecipes(rig, ThreeRecipes());
            ResearchRecipesView view = window.RecipesView;
            string name = view.ShownNames[1];
            Type(view.Row(name).Stepper, "55");
            Assert.False(view.Row(name).Revert.IsReserved);

            view.Select(name);
            view.Row(name).Item.Focus(NavigationMethod.Tab);
            TabFromFocus(window);
            Assert.True(ValueBox(view.Row(name).Stepper).IsFocused);
            TabFromFocus(window);
            Assert.True(view.Row(name).Revert.IsKeyboardFocusWithin, $"reached {window.FocusManager!.GetFocusedElement()}");
            TabFromFocus(window);
            Assert.False(view.Table.IsKeyboardFocusWithin);
            Assert.True(window.SegmentControl.IsKeyboardFocusWithin);

            CanvasKeys.KeyDownOn((Control)window.FocusManager!.GetFocusedElement()!, Key.Tab, KeyModifiers.Shift);
            Assert.True(view.Table.IsKeyboardFocusWithin);
        }

        private static void TabFromFocus(ResearchWindow window) =>
            CanvasKeys.KeyDownOn((Control)window.FocusManager!.GetFocusedElement()!, Key.Tab);
    }
}
