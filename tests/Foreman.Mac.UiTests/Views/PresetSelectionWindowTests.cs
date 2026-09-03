using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Views;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers PresetSelectionWindow (io-reference.md §8, upstream PresetSelectionForm.cs+.Designer.cs) against
    //hand-built PresetErrorPackage fixtures - no real preset files needed, since every value under test here
    //(sort order, percent formulas, tooltip text) is pure arithmetic/formatting over the package's own counts.
    public class PresetSelectionWindowTests {
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

        //---- Verbatim strings -----------------------------------------------------------------------

        [AvaloniaFact]
        public void Constructor_TitleAndCaptionsMatchUpstreamVerbatim() {
            var window = new PresetSelectionWindow([]);

            Assert.Equal("Please select Preset", window.Title);
            Assert.Equal("Load with seleted preset", window.ConfirmationButtonControl.Content);
            Assert.Equal("Dont Load", window.CancellingButtonControl.Content);
        }

        [AvaloniaFact]
        public void Constructor_BodyLabelsMatchUpstreamVerbatim() {
            var window = new PresetSelectionWindow([]);
            window.Show();

            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("No preset was found to match the saved graph exactly.", texts);
            Assert.Contains("Please select which preset you wish to use based on the given compatibility ratings.", texts);
        }

        //---- Sorting (PresetErrorPackage.CompareTo: MissingMods, then AddedMods, then MICount) --------

        [AvaloniaFact]
        public void Constructor_SortsRowsByAscendingSeverity() {
            var worst = new Preset("Worst", false, false);
            var middle = new Preset("Middle", false, false);
            var best = new Preset("Best", false, false);

            //Unsorted input on purpose - the constructor itself must do the sorting.
            var errors = new List<PresetErrorPackage> {
                NewPackage(worst, requiredMods: 1, missingMods: 1, addedMods: 0, wrongVersionMods: 0,
                    requiredItems: 1, missingItems: 0, requiredRecipes: 1, missingRecipes: 0, incorrectRecipes: 0),
                NewPackage(best, requiredMods: 1, missingMods: 0, addedMods: 0, wrongVersionMods: 0,
                    requiredItems: 1, missingItems: 0, requiredRecipes: 1, missingRecipes: 0, incorrectRecipes: 0),
                NewPackage(middle, requiredMods: 1, missingMods: 0, addedMods: 1, wrongVersionMods: 0,
                    requiredItems: 1, missingItems: 0, requiredRecipes: 1, missingRecipes: 0, incorrectRecipes: 0),
            };

            var window = new PresetSelectionWindow(errors);

            var rowNames = ((List<PresetSelectionWindow.PresetRow>)window.PresetSelectionListViewControl.ItemsSource!).Select(r => r.Name).ToList();
            Assert.Equal(["Best", "Middle", "Worst"], rowNames);
        }

        //---- Percent formulas ("%00" format: multiplies by 100, prints '%' before the digits) ---------

        [AvaloniaFact]
        public void BuildRows_PercentTextMatchesUpstreamFormulaAndFormat() {
            var preset = new Preset("P", false, false);
            //Mods: (4 - 1 missing - 0 wrongVersion - 0 added) / 4 = 0.75 -> "%75"
            //Items: (10 - 2 missing) / 10 = 0.80 -> "%80"
            //Recipes: (5 - 1 missing - 1 incorrect) / 5 = 0.60 -> "%60"
            var errors = NewPackage(preset, requiredMods: 4, missingMods: 1, addedMods: 0, wrongVersionMods: 0,
                requiredItems: 10, missingItems: 2, requiredRecipes: 5, missingRecipes: 1, incorrectRecipes: 1);

            List<PresetSelectionWindow.PresetRow> rows = PresetSelectionWindow.BuildRows([errors]);

            PresetSelectionWindow.PresetRow row = Assert.Single(rows);
            Assert.Equal("%75", row.ModsText);
            Assert.Equal("%80", row.ItemsText);
            Assert.Equal("%60", row.RecipesText);
        }

        //---- Tooltip breakdown (PresetSelectionForm.cs:42-54, verbatim) -------------------------------

        [AvaloniaFact]
        public void BuildRows_TooltipMatchesUpstreamBreakdownVerbatim() {
            var preset = new Preset("P", false, false);
            var errors = NewPackage(preset, requiredMods: 4, missingMods: 1, addedMods: 1, wrongVersionMods: 1,
                requiredItems: 3, missingItems: 1, requiredRecipes: 6, missingRecipes: 2, incorrectRecipes: 1);

            List<PresetSelectionWindow.PresetRow> rows = PresetSelectionWindow.BuildRows([errors]);

            string expected =
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
            Assert.Equal(expected, Assert.Single(rows).Tooltip);
        }

        //---- Commit paths: Confirm button, double-click, Cancel ---------------------------------------

        [AvaloniaFact]
        public void SimulateConfirmClick_WithSelectedRow_SetsChosenPresetAndClosesOk() {
            var preset = new Preset("P", false, false);
            var errors = NewPackage(preset, 1, 0, 0, 0, 1, 0, 1, 0, 0);
            var window = new PresetSelectionWindow([errors]);

            window.SimulateSelectRow(preset);
            window.SimulateConfirmClick();

            Assert.Equal(preset, window.ChosenPreset);
        }

        [AvaloniaFact]
        public void SimulateDoubleClickRow_SetsChosenPresetAndClosesOk() {
            var preset = new Preset("P", false, false);
            var errors = NewPackage(preset, 1, 0, 0, 0, 1, 0, 1, 0, 0);
            var window = new PresetSelectionWindow([errors]);

            window.SimulateDoubleClickRow(preset);

            Assert.Equal(preset, window.ChosenPreset);
        }

        [AvaloniaFact]
        public void SimulateCancelClick_LeavesChosenPresetNull() {
            var preset = new Preset("P", false, false);
            var errors = NewPackage(preset, 1, 0, 0, 0, 1, 0, 1, 0, 0);
            var window = new PresetSelectionWindow([errors]);

            window.SimulateCancelClick();

            Assert.Null(window.ChosenPreset);
        }
    }
}
