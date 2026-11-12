using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Research;
using Foreman.Mac.Views.Sheets;
using Foreman.Models.Research;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static Foreman.Mac.UiTests.Views.Research.ResearchWindowTests;

namespace Foreman.Mac.UiTests.Views.Research {
    public class ResearchBonusesTests {
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

        private static ResearchState SyncedMining() => Synced();

        private static ResearchWindow OpenEdited(Rig rig) {
            ResearchWindow window = rig.Open(SyncedMining());
            Type(window.BonusRow(ResearchField.MiningProductivity).Stepper, "150");
            return window;
        }

        [AvaloniaFact]
        public void BonusRow_HandEdit_MarksAndSolves() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(SyncedMining());
            ManualTaskScheduler.Defer(rig.Graph);
            ResearchBonusRow row = window.BonusRow(ResearchField.MiningProductivity);
            Assert.False(row.IsEdited);
            Assert.Equal(0, row.Dot.Opacity);
            Assert.True(row.Revert.IsReserved);

            Type(row.Stepper, "150");
            AnimationClock.Settle(250);
            window.UpdateLayout();

            Assert.Equal(1.5, rig.Graph.Research.MiningProductivity);
            Assert.True(row.IsEdited);
            Assert.Equal(1, rig.Shell.SolveWorker.Pending);

            Assert.Equal(6, row.Dot.Bounds.Width);
            Assert.Equal(6, row.Dot.Bounds.Height);
            Assert.Equal(1, row.Dot.Opacity);
            Assert.Equal(Token("Foreman.Color.Accent"), Assert.IsAssignableFrom<ISolidColorBrush>(row.Dot.Fill).Color);
            Point center = row.Dot.TranslatePoint(new Point(3, 3), row.Row)!.Value;
            Assert.Equal(132 + 6, center.X, 0.5);
            Assert.Equal(row.Stepper.Bounds.Height / 2, center.Y, 1.0);

            Assert.Equal(FontWeight.SemiBold, row.LabelText.FontWeight);
            Assert.Equal("Synced value: 120 %", row.Note);
            Assert.False(row.Revert.IsReserved);
            Assert.Equal(20, row.Revert.Bounds.Width);
            Assert.Equal(20, row.Revert.Bounds.Height);
            Assert.True(row.Revert.IsEffectivelyVisible);
        }

        [AvaloniaFact]
        public void BonusRow_NoOpCommit_DoesNotQueueASolve() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(SyncedMining());
            ManualTaskScheduler.Defer(rig.Graph);
            Type(window.BonusRow(ResearchField.MiningProductivity).Stepper, "120");
            Assert.Equal(0, rig.Shell.SolveWorker.Pending);
            Assert.Equal(0, rig.Graph.Research.HandEditCount);
        }

        [AvaloniaFact]
        public void BonusRow_PercentDisplay_IsFractionTimesHundred() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(Synced(values: new() { [ResearchField.LabSpeed] = 0.35 }));
            Stepper stepper = window.BonusRow(ResearchField.LabSpeed).Stepper;

            Assert.Equal(35, stepper.Value);
            Assert.Equal("35.00", ValueBox(stepper).Text);
            Assert.Equal("%", stepper.Unit);
            Assert.Equal(0, stepper.Minimum);
            Assert.Equal(100000, stepper.Maximum);
            Assert.Equal(10, stepper.Increment);
            Assert.Equal(2, stepper.Decimals);
            Assert.True(stepper.IsWide);
            Assert.False(stepper.ShowsPlusSign);
        }

        [AvaloniaFact]
        public void BonusRow_Revert_RestoresSynced() {
            using var rig = new Rig();
            ResearchWindow window = OpenEdited(rig);
            ResearchBonusRow row = window.BonusRow(ResearchField.MiningProductivity);

            Click(row.Revert);
            AnimationClock.Settle(250);

            Assert.Equal(1.2, rig.Graph.Research.MiningProductivity);
            Assert.Equal(120, row.Stepper.Value);
            Assert.Equal(0, row.Dot.Opacity);
            Assert.True(row.Revert.IsReserved);
            Assert.Equal(FontWeight.Normal, row.LabelText.FontWeight);
        }

        [AvaloniaFact]
        public void BonusRow_CommandBackspace_RevertsFocusedRow() {
            using var rig = new Rig();
            ResearchWindow window = OpenEdited(rig);
            ResearchBonusRow mining = window.BonusRow(ResearchField.MiningProductivity);
            ResearchBonusRow lab = window.BonusRow(ResearchField.LabSpeed);

            ValueBox(lab.Stepper).Focus();
            CanvasKeys.KeyDownOn(ValueBox(lab.Stepper), Key.Back, KeyModifiers.Meta);
            Assert.Equal(1.5, rig.Graph.Research.MiningProductivity);

            ValueBox(mining.Stepper).Focus();
            CanvasKeys.KeyDownOn(ValueBox(mining.Stepper), Key.Back, KeyModifiers.Meta);
            Assert.Equal(1.2, rig.Graph.Research.MiningProductivity);

            ResearchState before = rig.Graph.Research;
            var clean = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back, KeyModifiers = KeyModifiers.Meta };
            bool handledBeforeTextBox = true;
            ValueBox(mining.Stepper).AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => handledBeforeTextBox = e.Handled, RoutingStrategies.Tunnel);
            ValueBox(mining.Stepper).RaiseEvent(clean);
            Assert.Same(before, rig.Graph.Research);
            Assert.False(handledBeforeTextBox);

            var edited = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back, KeyModifiers = KeyModifiers.Meta };
            Type(lab.Stepper, "50");
            ValueBox(lab.Stepper).RaiseEvent(edited);
            Assert.True(edited.Handled);
        }

        [AvaloniaFact]
        public void RevertAll_ClearsEveryMark() {
            using var rig = new Rig();
            ResearchWindow window = OpenEdited(rig);
            Type(window.BonusRow(ResearchField.LabSpeed).Stepper, "50");
            Assert.Equal(2, rig.Graph.Research.HandEditCount);
            Assert.True(window.SourceBar.RevertAllButton.IsVisible);

            Click(window.SourceBar.RevertAllButton);

            Assert.Equal(0, rig.Graph.Research.HandEditCount);
            Assert.False(window.SourceBar.RevertAllButton.IsVisible);
            Assert.False(SheetHost.For(window).IsOpen);
            Assert.False(SheetHost.For(rig.Window).IsOpen);
        }

        [AvaloniaFact]
        public void Bonuses_ShowRecipes_SwitchesToRecipes() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(Synced(recipes: new() { ["a"] = 0.1, ["b"] = 0.2, ["c"] = 0.3 }));

            Assert.Equal("Recipe productivity: 3 recipes", window.RecipeProductivityRow.Text);
            Assert.Equal("Show Recipes", window.RecipeProductivityRow.ShowRecipesButton.Content);
            Click(window.RecipeProductivityRow.ShowRecipesButton);
            Assert.Equal(1, window.SegmentControl.SelectedIndex);
        }

        [AvaloniaFact]
        public void CountRows_ShowPlusNotPercent() {
            using var rig = new Rig();
            var counts = new[] { ResearchField.InserterStackBonus, ResearchField.BulkInserterCapacityBonus, ResearchField.BeltStackBonus, ResearchField.RobotCargo };
            ResearchWindow window = rig.Open(Synced(values: counts.ToDictionary(f => f, _ => 2.0)));

            foreach (ResearchField field in counts) {
                Stepper stepper = window.BonusRow(field).Stepper;
                Assert.Equal("+2", ValueBox(stepper).Text);
                Assert.Equal(2, stepper.Value);
                Assert.Equal(1, stepper.Increment);
                Assert.Equal(0, stepper.Decimals);
                Assert.True(stepper.ShowsPlusSign);
                Assert.True(string.IsNullOrEmpty(stepper.Unit));
            }
            Type(window.BonusRow(ResearchField.BeltStackBonus).Stepper, "3");
            Assert.Equal(3, rig.Graph.Research.BeltStackBonus);
        }

        [AvaloniaFact]
        public void AbsentField_ShowsNotInThisSave() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(Synced());
            ResearchBonusRow row = window.BonusRow(ResearchField.LabSpeed);

            Assert.True(row.Stepper.IsEmpty);
            Assert.Equal("None", row.Stepper.Placeholder);
            Assert.Equal("Not in this save.", row.Note);
            Assert.Equal("None percent", SpokenValue(row.Stepper));

            Type(row.Stepper, "5");

            Assert.Equal(0.05, rig.Graph.Research.LabSpeed);
            Assert.True(row.IsEdited);
            Assert.Equal("Not in this save.", row.Note);

            Click(row.Revert);
            Assert.Null(rig.Graph.Research.LabSpeed);
            Assert.True(row.Stepper.IsEmpty);
        }

        [AvaloniaFact]
        public void StoredGroups_ShowNotAppliedYet() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(Synced());

            Assert.Equal(["Production", "Labs", "Inserters and belts", "Robots", "Trains"], window.BonusesView.Groups.Select(g => g.Title));
            Assert.Equal([null, "Not applied yet", "Not applied yet", "Not applied yet", "Not applied yet"], window.BonusesView.Groups.Select(g => g.Caption));
            Assert.Equal(["Mining productivity", "Recipe productivity"], window.BonusesView.Groups[0].Labels);
            Assert.Equal(["Lab speed", "Lab productivity"], window.BonusesView.Groups[1].Labels);
            Assert.Equal(["Inserter capacity", "Bulk inserter capacity", "Belt stack size"], window.BonusesView.Groups[2].Labels);
            Assert.Equal(["Robot speed", "Robot cargo size", "Robot battery"], window.BonusesView.Groups[3].Labels);
            Assert.Equal(["Braking force"], window.BonusesView.Groups[4].Labels);
        }

        [AvaloniaFact]
        public void Bonuses_VoiceOver() {
            using var rig = new Rig();
            ResearchWindow window = OpenEdited(rig);
            ResearchBonusRow mining = window.BonusRow(ResearchField.MiningProductivity);
            ResearchBonusRow lab = window.BonusRow(ResearchField.LabSpeed);

            Assert.Equal("Mining productivity", AutomationProperties.GetName(mining.Stepper));
            Assert.Equal("150 percent", SpokenValue(mining.Stepper));
            Assert.Equal("Edited by hand. Synced value 120 percent.", AutomationProperties.GetHelpText(mining.Stepper));
            Assert.Equal("Revert Mining productivity", AutomationProperties.GetName(mining.Revert));
            Assert.Equal("Sets it back to 120 % from the save.", AutomationProperties.GetHelpText(mining.Revert));

            Assert.Equal("Lab speed", AutomationProperties.GetName(lab.Stepper));
            Assert.Equal("Not in this save. Not applied yet.", AutomationProperties.GetHelpText(lab.Stepper));

            Type(lab.Stepper, "20");
            Assert.Equal("20 percent", SpokenValue(lab.Stepper));

            ResearchBonusRow cargo = window.BonusRow(ResearchField.RobotCargo);
            Type(cargo.Stepper, "2");
            Assert.Equal("plus 2", SpokenValue(cargo.Stepper));
            Assert.Equal("Edited by hand. Not in this save. Not applied yet.", AutomationProperties.GetHelpText(cargo.Stepper));

            Assert.Equal("Revert All Edits", AutomationProperties.GetName(window.SourceBar.RevertAllButton));
            Assert.Equal("Sets 3 values back to the save.", AutomationProperties.GetHelpText(window.SourceBar.RevertAllButton));

            Click(lab.Revert);
            Click(cargo.Revert);
            Assert.Equal("Sets 1 value back to the save.", AutomationProperties.GetHelpText(window.SourceBar.RevertAllButton));
            Assert.Equal("Recipe productivity: 0 recipes", AutomationProperties.GetName(window.RecipeProductivityRow.Content));
        }

        [AvaloniaFact]
        public void Bonuses_ExternalChange_RefreshesTheRows() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(SyncedMining());
            rig.Graph.Research = Synced(values: new() { [ResearchField.MiningProductivity] = 0.5 });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(50, window.BonusRow(ResearchField.MiningProductivity).Stepper.Value);
        }

        [AvaloniaFact]
        public void Bonuses_NeverSynced_HasNoSyncedNote() {
            using var rig = new Rig();
            ResearchWindow window = rig.Open(ResearchState.None);
            ResearchBonusRow row = window.BonusRow(ResearchField.MiningProductivity);

            Assert.Null(row.Note);
            Type(row.Stepper, "30");
            Assert.Equal(0.3, rig.Graph.Research.MiningProductivity);
            Assert.Null(row.Note);
            Assert.True(row.IsEdited);
        }
    }
}
