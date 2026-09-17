using Avalonia;
using Avalonia.Automation;
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
using Foreman.Mac.Kit.Controls;
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
        public void UntouchedGrid_DefaultsToNoSelection_NoCellIsMarkedSelected() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(-1, grid.SelectedIndex);
            Assert.All(Cells(grid), c => Assert.False(c.IsSelected));
        }

        [AvaloniaFact]
        public void ArrowKeys_FromNoSelection_LandOnTheFirstCell() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            foreach (Key key in new[] { Key.Left, Key.Right, Key.Up, Key.Down }) {
                grid.SelectedIndex = -1;
                fixture.StateOf(grid).KeyDown(key);
                Assert.Equal(0, grid.SelectedIndex);
            }
        }

        [AvaloniaFact]
        public void ReturnOrSpace_WithNoSelection_DoesNothing() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = -1;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            int? chosen = null;
            grid.ChooseRequested += (_, index) => chosen = index;

            var returnEvent = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return };
            grid.RaiseEvent(returnEvent);
            var spaceEvent = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space };
            grid.RaiseEvent(spaceEvent);

            Assert.Null(chosen);
            Assert.False(returnEvent.Handled);
            Assert.False(spaceEvent.Handled);
        }

        //Keyboard entry needs a cell to land on: with nothing selected the grid takes the first one, so
        //the selection and focus rings both draw and Return/Delete have a target (addendum §03).
        [AvaloniaFact]
        public void KeyboardFocus_WithNoSelection_TakesTheFirstCell_SoTheRingsShowAndReturnChooses() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            int? chosen = null;
            grid.ChooseRequested += (_, index) => chosen = index;
            fixture.StateOf(grid).Focus();

            Assert.Equal(0, grid.SelectedIndex);
            Assert.True(Cells(grid)[0].IsSelected);
            Assert.Equal(1, Cells(grid).Count(FocusRing.GetShowAlways));

            var returnEvent = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return };
            grid.RaiseEvent(returnEvent);

            Assert.True(returnEvent.Handled);
            Assert.Equal(0, chosen);
        }

        [AvaloniaFact]
        public void PointerFocus_WithNoSelection_SelectsNothing_UntilACellIsClicked() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            grid.Focus(NavigationMethod.Pointer);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(-1, grid.SelectedIndex);
            Assert.All(Cells(grid), c => Assert.False(FocusRing.GetShowAlways(c)));

            fixture.StateOf(Cells(grid)[2]).Click();

            Assert.Equal(2, grid.SelectedIndex);
        }

        //The cell's own EmptyItemNoun is covered in KitPickCellTests; this pins the hop from the grid.
        [AvaloniaFact]
        public void EmptyItemNoun_ReachesTheEmptyCellsTooltip() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.EmptyItemNoun = "module";
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Empty slot — press Return to choose a module", ToolTip.GetTip(Cells(grid)[2]));
        }

        [AvaloniaFact]
        public void NeitherChooseRequestedNorClearedSubscribed_CellHelpTextNamesNoCommand() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("", AutomationProperties.GetHelpText(Cells(grid)[0]));
        }

        [AvaloniaFact]
        public void ZeroSlots_TheGridDrawsNothing() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme() };
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.False(grid.IsVisible);
        }

        //A host never writes IsVisible to bootstrap a grid: a grid still waiting for its first layout pass
        //(so never templated) derives visibility from Slots on its own, in both directions.
        [AvaloniaFact]
        public void UntemplatedGrid_DerivesIsVisibleFromItsSlots() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme() };

            grid.Slots.Add(new PickSlot { IsEmpty = true });
            Assert.True(grid.IsVisible);

            grid.Slots.Clear();
            Assert.False(grid.IsVisible);

            grid.Slots.Add(new PickSlot { IsEmpty = true });
            Assert.True(grid.IsVisible);
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
            grid.ChooseRequested += (_, _) => { };
            grid.Cleared += (_, _) => { };
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(Cells(grid)[0]);
            Assert.Equal("Return chooses this item. Delete removes it.", peer.GetHelpText());
        }

        [AvaloniaFact]
        public void NoClearedSubscriber_CellHelpTextNamesOnlyChoose_AndDeleteIsUnhandled() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = 0;
            grid.ChooseRequested += (_, _) => { };
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(Cells(grid)[0]);
            Assert.Equal("Return chooses this item.", peer.GetHelpText());

            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete };
            grid.RaiseEvent(e);
            Assert.False(e.Handled);
        }

        [AvaloniaFact]
        public void NoChooseRequestedSubscriber_ReturnIsUnhandled() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return };
            grid.RaiseEvent(e);
            Assert.False(e.Handled);
        }

        [AvaloniaFact]
        public void NamesSlotsFalse_CellsAndGroupNameCarryNoSlotWording() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.NamesSlots = false;
            grid.GroupLabel = "Available modules";
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer cellPeer = ControlAutomationPeer.CreatePeerForElement(Cells(grid)[0]);
            Assert.Equal("Productivity Module 3, 1 of 4", cellPeer.GetName());

            AutomationPeer groupPeer = ControlAutomationPeer.CreatePeerForElement(grid);
            Assert.Equal("Available modules, 4 items", groupPeer.GetName());
        }

        [AvaloniaFact]
        public void ClickingACell_RaisesCellClicked_ButArrowNavigationDoesNot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            int? clicked = null;
            grid.CellClicked += (_, index) => clicked = index;

            fixture.StateOf(Cells(grid)[2]).Click();
            Assert.Equal(2, clicked);

            clicked = null;
            fixture.StateOf(grid).KeyDown(Key.Left);
            Assert.Null(clicked);
        }

        [AvaloniaFact]
        public void ReplacingASlot_UpdatesTheExistingCellInPlace_WithoutRebuildingOtherCells() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell[] before = Cells(grid);
            grid.Slots[1] = new PickSlot { Name = "Efficiency Module 1" };
            Dispatcher.UIThread.RunJobs();

            PickCell[] after = Cells(grid);
            Assert.Equal(before.Length, after.Length);
            for (int i = 0; i < before.Length; i++)
                Assert.Same(before[i], after[i]);
            Assert.Equal("Efficiency Module 1", after[1].AccessibleName);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(after[1]);
            Assert.Equal("Efficiency Module 1, slot 2 of 4", peer.GetName());
        }

        [AvaloniaFact]
        public void ReplacingASlotWithIdenticalContent_StillUpdatesInPlace_NoCellRecreated() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell before = Cells(grid)[0];
            grid.Slots[0] = new PickSlot { Name = "Productivity Module 3" };
            Dispatcher.UIThread.RunJobs();

            Assert.Same(before, Cells(grid)[0]);
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

        //A stale index >= the shrunk count defeats the keyboard-entry guard (GotFocus only takes the first
        //cell when SelectedIndex is < 0), so a shrink must clamp it itself.
        [AvaloniaFact]
        public void SlotsShrink_ClampsAStaleSelectedIndex_ToTheNewLastSlot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = 3;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            grid.Slots.RemoveAt(3);
            grid.Slots.RemoveAt(2);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, grid.SelectedIndex);
            Assert.True(Cells(grid)[1].IsSelected);
        }

        [AvaloniaFact]
        public void AllSlotsRemoved_ClampsSelectedIndexToNoSelection() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            grid.SelectedIndex = 2;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            grid.Slots.Clear();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(-1, grid.SelectedIndex);
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
