using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitPickGridTests {
        private static ControlTheme KitPickGridTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PickGrid", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PickGrid FourSlots(int columns = 0) {
            var grid = new PickGrid { Theme = KitPickGridTheme(), Columns = columns };
            grid.Slots.Add(new PickSlot { Name = "Productivity Module 3" });
            grid.Slots.Add(new PickSlot { Name = "Speed Module 2" });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            return grid;
        }

        private static Grid Track(PickGrid grid) =>
            grid.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");

        private static PickCell[] Cells(PickGrid grid) =>
            [.. grid.GetVisualDescendants().OfType<PickCell>()];

        [AvaloniaFact]
        public void KitPickGrid_IsAControlThemeTargetingPickGrid() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(PickGrid), KitPickGridTheme().TargetType);
        }

        [AvaloniaFact]
        public void BuildsOnePickCellPerSlot_EachMarginedByThePickCellGap() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell[] cells = Cells(grid);
            Assert.Equal(4, cells.Length);
            Application.Current!.TryGetResource("Foreman.Thickness.PickCellGap", Application.Current.ActualThemeVariant, out object? gap);
            Assert.All(cells, c => Assert.Equal(Assert.IsType<Thickness>(gap), c.Margin));
        }

        [AvaloniaFact]
        public void NoExplicitColumns_LaysOutAsOneRow_NoStretch() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Grid track = Track(grid);
            Assert.Equal(4, track.ColumnDefinitions.Count);
            Assert.Single(track.RowDefinitions);
            Assert.All(track.ColumnDefinitions, c => Assert.False(c.Width.IsStar));
        }

        [AvaloniaFact]
        public void ExplicitColumns_WrapsIntoRows() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell[] cells = Cells(grid);
            Assert.Equal(0, Grid.GetRow(cells[0]));
            Assert.Equal(0, Grid.GetColumn(cells[0]));
            Assert.Equal(0, Grid.GetRow(cells[1]));
            Assert.Equal(1, Grid.GetColumn(cells[1]));
            Assert.Equal(1, Grid.GetRow(cells[2]));
            Assert.Equal(0, Grid.GetColumn(cells[2]));
            Assert.Equal(1, Grid.GetRow(cells[3]));
            Assert.Equal(1, Grid.GetColumn(cells[3]));
        }

        [AvaloniaFact]
        public void SelectedIndex_MarksExactlyOneCellSelected() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = 1;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell[] cells = Cells(grid);
            Assert.False(cells[0].IsSelected);
            Assert.True(cells[1].IsSelected);
            Assert.False(cells[2].IsSelected);
            Assert.False(cells[3].IsSelected);
        }

        [AvaloniaFact]
        public void OneTabStop_TheGridIsFocusable_IndividualCellsAreNot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.True(grid.Focusable);
            Assert.All(Cells(grid), c => Assert.False(c.Focusable));
        }

        [AvaloniaFact]
        public void LeftArrow_AtTheFirstCellOfARow_WrapsToTheLastCellOfThePreviousRow() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            grid.SelectedIndex = 2;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Left);

            Assert.Equal(1, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void LeftArrow_AtTheVeryFirstCell_IsANoOp() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Left);

            Assert.Equal(0, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void RightArrow_MovesForwardAndWrapsIntoTheNextRow() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            grid.SelectedIndex = 1;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Right);

            Assert.Equal(2, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void UpArrow_AtTheTopRow_IsANoOp() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Up);

            Assert.Equal(0, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void DownArrow_MovesToTheSameColumnInTheNextRow() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Down);

            Assert.Equal(2, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void HomeAndEnd_JumpToTheFirstAndLastSlot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            grid.SelectedIndex = 1;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.End);
            Assert.Equal(3, grid.SelectedIndex);

            fixture.StateOf(grid).KeyDown(Key.Home);
            Assert.Equal(0, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void ReturnOrSpace_RaisesChooseRequestedForTheSelectedSlot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = 2;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            int? chosen = null;
            grid.ChooseRequested += (_, index) => chosen = index;

            fixture.StateOf(grid).KeyDown(Key.Return);

            Assert.Equal(2, chosen);
        }

        [AvaloniaFact]
        public void DeleteOrBackspace_RaisesClearedForTheSelectedSlot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            int? cleared = null;
            grid.Cleared += (_, index) => cleared = index;

            fixture.StateOf(grid).KeyDown(Key.Delete);

            Assert.Equal(0, cleared);
        }

        [AvaloniaFact]
        public void ZeroSlots_TheGridDrawsNothing() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme() };
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.False(grid.IsVisible);
        }

        [AvaloniaFact]
        public void VoiceOver_GroupNameNamesTheSlotCount() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.GroupLabel = "Module slots";
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            Assert.Equal("Module slots, 4 slots", peer.GetName());
        }

        [AvaloniaFact]
        public void VoiceOver_EachCellNamesContentAndPosition() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell[] cells = Cells(grid);
            AutomationPeer first = ControlAutomationPeer.CreatePeerForElement(cells[0]);
            AutomationPeer third = ControlAutomationPeer.CreatePeerForElement(cells[2]);

            Assert.Equal("Productivity Module 3, slot 1 of 4", first.GetName());
            Assert.Equal("Empty, slot 3 of 4", third.GetName());
        }

        [AvaloniaFact]
        public void ClickingACell_SelectsItAndFocusesTheGrid() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell target = Cells(grid)[2];
            fixture.StateOf(target).Click();

            Assert.Equal(2, grid.SelectedIndex);
            Assert.True(grid.IsFocused);
            Assert.Null(AdornerLayer.GetAdorner(Cells(grid)[2]));
        }

        [AvaloniaFact]
        public void VoiceOver_CellHelpTextNamesTheRemoveCommand() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(Cells(grid)[0]);
            Assert.Equal("Return chooses a module. Delete removes it.", peer.GetHelpText());
        }

        [AvaloniaFact]
        public void SlotWithNoIconAndNotEmpty_ShowsNeitherPlusNorAlertGlyph_LoadingRow() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme() };
            grid.Slots.Add(new PickSlot { Icon = null, IsEmpty = false });
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell cell = Cells(grid)[0];
            Viewbox plusBox = cell.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_PlusBox");
            Viewbox alertBox = cell.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_AlertBox");
            Assert.False(plusBox.IsVisible);
            Assert.False(alertBox.IsVisible);
        }

        [AvaloniaFact]
        public void GotFocus_ShowsTheFocusRingOnTheSelectedCell_LostFocusHidesIt() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = 1;
            var host = new StackPanel { Children = { grid, new Button() } };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).Focus();
            Assert.NotNull(AdornerLayer.GetAdorner(Cells(grid)[1]));

            fixture.Window.FocusManager?.Focus(host.Children[1], NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(AdornerLayer.GetAdorner(Cells(grid)[1]));
        }
    }
}
