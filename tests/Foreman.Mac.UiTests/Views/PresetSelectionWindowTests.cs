using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers PresetSelectionSheet (io-reference.md §8) against hand-built PresetErrorPackage fixtures: the
    //percent formulas, the tooltip, the ranked list, the four-column row template, keyboard and VoiceOver, and
    //the failed-read strip. MainWindow.ResolveChosenPresetAsync is the only production call site.
    public class PresetSelectionWindowTests {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static PresetErrorPackage NewPackage(Preset preset, int requiredMods, int missingMods, int addedMods, int wrongVersionMods,
                int requiredItems, int missingItems, int requiredRecipes, int missingRecipes, int incorrectRecipes) {
            var errors = new PresetErrorPackage(preset);
            for (int i = 0; i < requiredMods; i++)
                errors.RequiredMods.Add("mod" + i);
            for (int i = 0; i < missingMods; i++)
                errors.MissingMods.Add("missing-mod" + i);
            for (int i = 0; i < addedMods; i++)
                errors.AddedMods.Add("added-mod" + i);
            for (int i = 0; i < wrongVersionMods; i++)
                errors.WrongVersionMods.Add("wrong-version-mod" + i);
            for (int i = 0; i < requiredItems; i++)
                errors.RequiredItems.Add("item" + i);
            for (int i = 0; i < missingItems; i++)
                errors.MissingItems.Add("missing-item" + i);
            for (int i = 0; i < requiredRecipes; i++)
                errors.RequiredRecipes.Add("recipe" + i);
            for (int i = 0; i < missingRecipes; i++)
                errors.MissingRecipes.Add("missing-recipe" + i);
            for (int i = 0; i < incorrectRecipes; i++)
                errors.IncorrectRecipes.Add("incorrect-recipe" + i);
            return errors;
        }

        //---- Percent formulas: the unit in the value, a figure space before the percent sign ------------

        [AvaloniaFact]
        public void BuildRows_PercentTextMatchesUpstreamFormula_WithTheUnitInTheValue() {
            var preset = new Preset("P", false, false);
            //Mods: (4 - 1 missing - 0 wrongVersion - 0 added) / 4 = 0.75
            //Items: (10 - 2 missing) / 10 = 0.80
            //Recipes: (5 - 1 missing - 1 incorrect) / 5 = 0.60
            var errors = NewPackage(preset, requiredMods: 4, missingMods: 1, addedMods: 0, wrongVersionMods: 0,
                requiredItems: 10, missingItems: 2, requiredRecipes: 5, missingRecipes: 1, incorrectRecipes: 1);

            List<PresetRow> rows = PresetSelectionSheet.BuildRows([errors]);

            PresetRow row = Assert.Single(rows);
            Assert.Equal("75\u2007%", row.ModsText);
            Assert.Equal("80\u2007%", row.ItemsText);
            Assert.Equal("60\u2007%", row.RecipesText);
        }

        //---- Tooltip: the full name, the preset's mods, then upstream's breakdown ---------------------------

        [AvaloniaFact]
        public void BuildRows_TooltipCarriesTheFullNameAndModList_ThenTheUpstreamBreakdown() {
            var preset = new Preset("Factorio 2.0.55 — Space Age with a very long preset name that trims", false, false);
            var errors = NewPackage(preset, requiredMods: 4, missingMods: 1, addedMods: 1, wrongVersionMods: 1,
                requiredItems: 3, missingItems: 1, requiredRecipes: 6, missingRecipes: 2, incorrectRecipes: 1);
            errors.RequiredMods[0] = "missing-mod0";

            List<PresetRow> rows = PresetSelectionSheet.BuildRows([errors]);

            string breakdown =
                "Mods:\n" +
                "     (2) Correct\n" + //4 required - 1 missing - 1 wrongVersion
                "     (1) Missing\n" +
                "     (1) Extra\n" +
                "     (1) Wrong Version\n" +
                "Items:\n" +
                "     (2) Correct\n" + //3 required - 1 missing
                "     (1) Missing\n" +
                "Recipes:\n" +
                "     (3) Correct\n" + //6 required - 2 missing - 1 incorrect
                "     (2) Missing\n" +
                "     (1) Incorrect";
            Assert.Equal(preset.Name + "\nmod1, mod2, mod3, added-mod0\n\n" + breakdown, Assert.Single(rows).Tooltip);
        }

        //---- Commit paths: Confirm button, double-click, Cancel ---------------------------------------

        [AvaloniaFact]
        public void SimulateConfirmClick_WithSelectedRow_SetsChosenPresetAndClosesOk() {
            var preset = new Preset("P", false, false);
            var errors = NewPackage(preset, 1, 0, 0, 0, 1, 0, 1, 0, 0);
            var sheet = new PresetSelectionSheet([errors]);

            sheet.SimulateSelectRow(preset);
            sheet.SimulateConfirmClick();

            Assert.Equal(preset, sheet.ChosenPreset);
        }

        [AvaloniaFact]
        public void SimulateDoubleClickRow_SetsChosenPresetAndClosesOk() {
            var preset = new Preset("P", false, false);
            var errors = NewPackage(preset, 1, 0, 0, 0, 1, 0, 1, 0, 0);
            var sheet = new PresetSelectionSheet([errors]);

            sheet.SimulateDoubleClickRow(preset);

            Assert.Equal(preset, sheet.ChosenPreset);
        }

        [AvaloniaFact]
        public void SimulateCancelClick_LeavesChosenPresetNull() {
            var preset = new Preset("P", false, false);
            var errors = NewPackage(preset, 1, 0, 0, 0, 1, 0, 1, 0, 0);
            var sheet = new PresetSelectionSheet([errors]);

            sheet.SimulateCancelClick();

            Assert.Null(sheet.ChosenPreset);
        }

        //---- the kit sheet: the wide width, the column set, the best match live on open ------------------

        private static readonly int[] GutterColumns = [1, 3, 5];

        private static List<PresetErrorPackage> ThreePresets() => [
            NewPackage(new Preset("1.1.110 Vanilla", false, false), 2, 1, 0, 0, 4, 2, 4, 2, 0),
            NewPackage(new Preset("Factorio 2.0.55 — Space Age", false, false), 2, 0, 0, 0, 4, 0, 4, 0, 0),
            NewPackage(new Preset("Space Age + Quality", false, false), 2, 0, 1, 0, 4, 1, 4, 1, 0),
        ];

        [AvaloniaFact]
        public void PresetSelection_Width_IsTheNamedSheetWidth() {
            var sheet = new PresetSelectionSheet(ThreePresets());
            (_, SheetHost host, Task<bool?> result) = SheetFixture.Present(sheet);

            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthWide"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal("Select a preset", SheetFixture.FrameOf(host, sheet).Title);
            Assert.Null(sheet.SheetHeight);
            Assert.Equal(["Cancel", "Load Preset"], sheet.Buttons!.Items.Cast<Button>().Select(b => b.Content));
            Assert.Same(sheet.ConfirmationButtonControl, sheet.Buttons.ResolveDefaultButton());
            Assert.Same(sheet.CancellingButtonControl, sheet.Buttons.Items.Cast<Button>().Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Cancel));
            var texts = sheet.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("No preset matches this graph exactly; the ratings show how much of it each preset can reproduce.", texts);
            Assert.Equal(["Preset", "Mods", "Items", "Recipes"], sheet.HeaderRowControl.Children.OfType<TextBlock>().Select(t => t.Text));

            CanvasKeys.KeyDownOn(sheet.PresetSelectionListViewControl, Key.Escape);
            Assert.True(result.IsCompleted);
            Assert.Null(result.Result);
            Assert.Null(sheet.ChosenPreset);
        }

        [AvaloniaFact]
        public void PresetSelection_NameColumn_IsAtLeast168_AndTheThreeRatingColumnsAre64_64_72() {
            ScaleGuard.AssertDefault();
            try {
                AssertColumnsAt(1.0);
                AssertColumnsAt(1.5);
            } finally {
                Foreman.Mac.Services.WindowScaler.SetScale(1.0);
            }
        }

        private static void AssertColumnsAt(double factor) {
            {
                Foreman.Mac.Services.WindowScaler.SetScale(factor);
                var sheet = new PresetSelectionSheet(ThreePresets());
                (_, SheetHost host, _) = SheetFixture.Present(sheet, 1200, 900);

                Grid header = sheet.HeaderRowControl;
                Assert.True(header.ColumnDefinitions[0].ActualWidth >= 168 * factor - 0.5);
                Assert.Equal(64 * factor, header.ColumnDefinitions[2].ActualWidth, 0.5);
                Assert.Equal(64 * factor, header.ColumnDefinitions[4].ActualWidth, 0.5);
                Assert.Equal(72 * factor, header.ColumnDefinitions[6].ActualWidth, 0.5);
                Assert.All(GutterColumns, i => Assert.Equal(16 * factor, header.ColumnDefinitions[i].ActualWidth, 0.5));
                Grid row = sheet.PresetSelectionListViewControl.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("preset-row"));
                Assert.Equal(header.ColumnDefinitions[6].ActualWidth, row.ColumnDefinitions[6].ActualWidth, 0.5);
                TextBlock mods = row.Children.OfType<TextBlock>().ElementAt(1);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, mods.HorizontalAlignment);
            }
        }

        [AvaloniaFact]
        public void PresetSelection_OpensWithTheBestMatchSelected_AndReturnLoadsIt() {
            var sheet = new PresetSelectionSheet(ThreePresets());
            (_, SheetHost host, Task<bool?> result) = SheetFixture.Present(sheet);

            PresetRow selected = Assert.IsType<PresetRow>(sheet.PresetSelectionListViewControl.SelectedItem);
            Assert.Equal("Factorio 2.0.55 — Space Age", selected.Name);
            var listPeer = ControlAutomationPeer.CreatePeerForElement(sheet.PresetSelectionListViewControl);
            AutomationPeer firstRow = PeerTree(listPeer).First(p => p is ControlAutomationPeer { Owner: ListBoxItem { IsSelected: true } });
            Assert.Equal("Factorio 2.0.55 — Space Age, mods 100 percent, items 100 percent, recipes 100 percent", firstRow.GetName());
            Control container = sheet.PresetSelectionListViewControl.ContainerFromItem(selected)!;
            string tip = Assert.IsType<string>(ToolTip.GetTip(container));
            Assert.Equal(selected.Tooltip, tip);
            Assert.StartsWith(selected.Preset.Name + "\n", tip);

            CanvasKeys.KeyDownOn(sheet.PresetSelectionListViewControl, Key.Enter);
            Assert.True(result.IsCompleted);
            Assert.Equal("Factorio 2.0.55 — Space Age", sheet.ChosenPreset!.Name);
        }

        [AvaloniaFact]
        public void ChooseAnother_Lede_NamesTheFailedPreset_AndTheBestOtherPresetIsSelected() {
            var sheet = new PresetSelectionSheet(ThreePresets(), new Preset("Factorio 2.0.55 — Space Age", false, false));

            Assert.Equal("\u201CFactorio 2.0.55 — Space Age\u201D couldn\u2019t be loaded. The ratings show how much of this graph each other preset can reproduce.",
                sheet.GetLogicalDescendants().OfType<TextBlock>().First().Text);
            PresetRow selected = Assert.IsType<PresetRow>(sheet.PresetSelectionListViewControl.SelectedItem);
            Assert.NotEqual("Factorio 2.0.55 — Space Age", selected.Name);
        }

        private static IEnumerable<AutomationPeer> PeerTree(AutomationPeer root) {
            foreach (AutomationPeer child in root.GetChildren()) {
                yield return child;
                foreach (AutomationPeer descendant in PeerTree(child))
                    yield return descendant;
            }
        }

        //---- the list: ItemTemplate rows aligned under the header, heights from the row token, figures, weight ----

        private static List<PresetErrorPackage> Presets(int count) =>
            [.. Enumerable.Range(0, count).Select(i => NewPackage(new Preset("Preset " + i, false, false), 2, 0, 0, 0, 4, 0, 4, 0, 0))];

        private static double Resolved(string key) => SheetFixture.Token(key);

        private static Grid FirstRowGrid(PresetSelectionSheet sheet) =>
            sheet.PresetSelectionListViewControl.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("preset-row"));

        [AvaloniaFact]
        public void HeaderCells_LineUpWithTheFirstRowsCells_At100And150Percent() {
            ScaleGuard.AssertDefault();
            try {
                foreach (double factor in new[] { 1.0, 1.5 }) {
                    Foreman.Mac.Services.WindowScaler.SetScale(factor);
                    var sheet = new PresetSelectionSheet(ThreePresets());
                    (Window window, _, _) = SheetFixture.Present(sheet, 1200, 900);
                    AnimationClock.UntilReflowsSettled(window);

                    TextBlock[] header = [.. sheet.HeaderRowControl.Children.OfType<TextBlock>()];
                    Grid row = FirstRowGrid(sheet);
                    for (int column = 0; column < 7; column += 2) {
                        double headerLeft = sheet.HeaderRowControl.TranslatePoint(new Point(ColumnLeft(sheet.HeaderRowControl, column), 0), sheet)!.Value.X;
                        double rowLeft = row.TranslatePoint(new Point(ColumnLeft(row, column), 0), sheet)!.Value.X;
                        Assert.Equal(headerLeft, rowLeft, 0.5);
                        double headerRight = headerLeft + sheet.HeaderRowControl.ColumnDefinitions[column].ActualWidth;
                        double rowRight = rowLeft + row.ColumnDefinitions[column].ActualWidth;
                        Assert.Equal(headerRight, rowRight, 0.5);
                    }
                    Assert.Equal(4, header.Length);
                    window.Close();
                }
            } finally {
                Foreman.Mac.Services.WindowScaler.SetScale(1.0);
            }
        }

        private static double ColumnLeft(Grid grid, int column) =>
            grid.ColumnDefinitions.Take(column).Sum(c => c.ActualWidth);

        [AvaloniaFact]
        public void ListHeight_IsTenRowsWithTwelvePresets_AndFourRowsPlusChromeWithTwo_AtTheControlRadius() {
            var many = new PresetSelectionSheet(Presets(12));
            (Window window, _, _) = SheetFixture.Present(many, 1200, 900);
            AnimationClock.UntilReflowsSettled(window);
            ListBox list = many.PresetSelectionListViewControl;
            ScrollViewer scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            double rowHeight = Resolved("Foreman.Size.ListRowHeight");
            Assert.Equal(10 * rowHeight, scroller.Viewport.Height - scroller.Padding.Top - scroller.Padding.Bottom, 0.5);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.Equal(SheetFixture.Radius("Foreman.Radius.Control"), list.CornerRadius);

            var two = new PresetSelectionSheet(Presets(2));
            (Window window2, _, _) = SheetFixture.Present(two, 1200, 900);
            AnimationClock.UntilReflowsSettled(window2);
            ListBox shortList = two.PresetSelectionListViewControl;
            double chrome = 2 * (shortList.BorderThickness.Top + Resolved("Foreman.Space.Space05"));
            Assert.Equal(4 * rowHeight + chrome, shortList.Bounds.Height, 0.5);
        }

        [AvaloniaFact]
        public void NumericCells_UseTabularFigures_AndOnlyTheSelectedRowsNameIs600() {
            var sheet = new PresetSelectionSheet(ThreePresets());
            (Window window, _, _) = SheetFixture.Present(sheet);
            AnimationClock.UntilReflowsSettled(window);

            Grid[] rows = [.. sheet.PresetSelectionListViewControl.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("preset-row"))];
            foreach (Grid row in rows) {
                TextBlock[] cells = [.. row.Children.OfType<TextBlock>()];
                foreach (TextBlock numeric in cells.Skip(1))
                    Assert.Contains(numeric.FontFeatures!, f => f.Tag == "tnum");
            }
            ListBoxItem selected = sheet.PresetSelectionListViewControl.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.IsSelected);
            ListBoxItem other = sheet.PresetSelectionListViewControl.GetVisualDescendants().OfType<ListBoxItem>().First(i => !i.IsSelected);
            Assert.Equal(FontWeight.SemiBold, selected.GetVisualDescendants().OfType<TextBlock>().First().FontWeight);
            Assert.Equal(FontWeight.Normal, other.GetVisualDescendants().OfType<TextBlock>().First().FontWeight);
        }

        [AvaloniaFact]
        public void TypeSelect_MatchesTheNameColumn() {
            var sheet = new PresetSelectionSheet(ThreePresets());
            SheetFixture.Present(sheet);
            ListBox list = sheet.PresetSelectionListViewControl;
            list.Focus();

            CanvasKeys.TextInputOn(list, "S");

            Assert.Equal("Space Age + Quality", Assert.IsType<PresetRow>(list.SelectedItem).Name);
        }

        //---- failed read: the strip, Try Again, and nothing chosen until a read succeeds ---------------------

        [AvaloniaFact]
        public async Task LoadPreset_WhenThePresetCannotBeRead_ShowsTheFailureStrip_AndTryAgainLoadsItOnceReadable() {
            bool readable = false;
            var sheet = new PresetSelectionSheet(ThreePresets()) {
                ReadPresetAsync = _ => Task.FromResult(readable),
            };
            (Window window, SheetHost host, Task<bool?> result) = SheetFixture.Present(sheet);

            await sheet.CommitAsync().WaitAsync(Timeout);
            AnimationClock.Settle();

            Assert.False(result.IsCompleted);
            Assert.Null(sheet.ChosenPreset);
            StatePanel strip = SheetFixture.StripOf(host);
            Assert.Equal(StatePanelShape.Error, strip.Shape);
            Assert.Equal("Couldn’t load “Factorio 2.0.55 — Space Age” — the preset file is missing or damaged.", strip.Sentence);
            Assert.Equal("Try Again", sheet.ConfirmationButtonControl.Content);
            Assert.True(sheet.ConfirmationButtonControl.IsEffectivelyEnabled);

            readable = true;
            SheetFixture.Click(window, sheet.ConfirmationButtonControl);
            Assert.True(await result.WaitAsync(System.TimeSpan.FromSeconds(10)));
            Assert.Equal("Factorio 2.0.55 — Space Age", sheet.ChosenPreset!.Name);
        }

        [AvaloniaFact]
        public async Task MainWindowsSheet_ReadsTheChosenPreset_FailingForOneThatIsNotThere() {
            PresetSelectionSheet sheet = MainWindow.NewPresetSelectionSheet(ThreePresets());

            Assert.NotNull(sheet.ReadPresetAsync);
            Assert.False(await sheet.ReadPresetAsync!(new Preset("No Such Preset " + System.Guid.NewGuid(), false, false)).WaitAsync(Timeout));
            Assert.True(await sheet.ReadPresetAsync!(new Preset("Factorio 2.0 Vanilla", true, true)).WaitAsync(Timeout));
        }
    }
}
