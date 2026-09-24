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
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 2;

            fixture.StateOf(grid).KeyDown(Key.Left);

            Assert.Equal(1, grid.FocusedIndex);
        }

        [AvaloniaFact]
        public void LeftArrow_AtTheVeryFirstCell_IsANoOp() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 0;

            fixture.StateOf(grid).KeyDown(Key.Left);

            Assert.Equal(0, grid.FocusedIndex);
        }

        [AvaloniaFact]
        public void RightArrow_MovesForwardAndWrapsIntoTheNextRow() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 1;

            fixture.StateOf(grid).KeyDown(Key.Right);

            Assert.Equal(2, grid.FocusedIndex);
        }

        [AvaloniaFact]
        public void UpArrow_AtTheTopRow_IsANoOp() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 0;

            fixture.StateOf(grid).KeyDown(Key.Up);

            Assert.Equal(0, grid.FocusedIndex);
        }

        [AvaloniaFact]
        public void DownArrow_MovesToTheSameColumnInTheNextRow() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 0;

            fixture.StateOf(grid).KeyDown(Key.Down);

            Assert.Equal(2, grid.FocusedIndex);
        }

        [AvaloniaFact]
        public void HomeAndEnd_JumpToTheFirstAndLastSlot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 1;

            fixture.StateOf(grid).KeyDown(Key.End);
            Assert.Equal(3, grid.FocusedIndex);

            fixture.StateOf(grid).KeyDown(Key.Home);
            Assert.Equal(0, grid.FocusedIndex);
        }

        [AvaloniaFact]
        public void Return_OnTheFocusedSlot_ArmsItAndRaisesChooseRequested() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 2;

            int? chosen = null;
            grid.ChooseRequested += (_, index) => chosen = index;

            fixture.StateOf(grid).KeyDown(Key.Return);

            Assert.Equal(2, chosen);
            Assert.Equal(2, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void Delete_OnTheFocusedSlot_RaisesClearedAndArmsIt() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 0;

            int? cleared = null;
            grid.Cleared += (_, index) => cleared = index;

            fixture.StateOf(grid).KeyDown(Key.Delete);

            Assert.Equal(0, cleared);
            Assert.Equal(0, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void Delete_WhenTheClearedHandlerCallsArmAfterClear_TheHostsTargetWinsOverTheHole() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 0;
            grid.Cleared += (_, _) => grid.ArmAfterClear(3);

            fixture.StateOf(grid).KeyDown(Key.Delete);

            Assert.Equal(3, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void ArmAfterClear_WithAnIndexPastTheSlots_ClampsToNoSelection_AndStillSuppressesTheHole() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 1;
            grid.Cleared += (_, _) => grid.ArmAfterClear(4);

            fixture.StateOf(grid).KeyDown(Key.Delete);

            Assert.Equal(-1, grid.SelectedIndex);
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
        public void ArrowKeys_FromNoFocus_LandOnTheFirstCell() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(columns: 2);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            foreach (Key key in new[] { Key.Left, Key.Right, Key.Up, Key.Down }) {
                grid.FocusedIndex = -1;
                fixture.StateOf(grid).KeyDown(key);
                Assert.Equal(0, grid.FocusedIndex);
            }
        }

        [AvaloniaFact]
        public void ReturnOrSpace_WithNothingFocused_DoesNothing() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
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
            Assert.Equal(-1, grid.SelectedIndex);
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
            grid.ChooseRequested += (_, _) => { };
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 0;

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(Cells(grid)[0]);
            Assert.Equal("Return chooses this item.", peer.GetHelpText());

            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete };
            grid.RaiseEvent(e);
            Assert.False(e.Handled);
        }

        //Arming needs no listener; only the app's own move-focus-to-the-options-grid side effect rides on ChooseRequested.
        [AvaloniaFact]
        public void ReturnWithNoChooseRequestedSubscriber_StillArmsTheFocusedSlot_AndIsHandled() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 0;

            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return };
            grid.RaiseEvent(e);

            Assert.True(e.Handled);
            Assert.Equal(0, grid.SelectedIndex);
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

        //The ring persists across a blur, so a later Tab-in lands back on the same cell.
        [AvaloniaFact]
        public void GotFocus_ShowsTheFocusRingOnTheFocusedCell_LostFocusHidesIt() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            var host = new StackPanel { Children = { grid, new Button() } };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 1;

            fixture.StateOf(grid).Focus();
            Assert.NotNull(AdornerLayer.GetAdorner(Cells(grid)[1]));

            fixture.Window.FocusManager?.Focus(host.Children[1], NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(AdornerLayer.GetAdorner(Cells(grid)[1]));
        }
    }

    //The equipped role's FocusedIndex/SelectedIndex split, keyboard contract and arming note (designer ruling 2026-09-17).
    public sealed class KitPickGridSlotArmingTests {
        private static ControlTheme KitPickGridTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PickGrid", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PickGrid FourSlots() {
            var grid = new PickGrid { Theme = KitPickGridTheme(), EmptyItemNoun = "module" };
            grid.Slots.Add(new PickSlot { Name = "Productivity Module 3" });
            grid.Slots.Add(new PickSlot { Name = "Speed Module 2" });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            return grid;
        }

        private static PickCell[] Cells(PickGrid grid) => [.. grid.GetVisualDescendants().OfType<PickCell>()];

        [AvaloniaFact]
        public void TabIntoTheEquippedGrid_FocusesSlotZero_ArmsNothing() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).Focus();

            Assert.Equal(0, grid.FocusedIndex);
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.DoesNotContain(Cells(grid), c => c.IsSelected);
        }

        [AvaloniaFact]
        public void SpaceOnTheFocusedSlot_ArmsInPlace_SpaceAgainDisarms() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 1;

            fixture.StateOf(grid).KeyDown(Key.Space);
            Assert.Equal(1, grid.SelectedIndex);
            Assert.Equal(1, grid.FocusedIndex);

            fixture.StateOf(grid).KeyDown(Key.Space);
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Equal(1, grid.FocusedIndex);
        }

        [AvaloniaFact]
        public void ClickingTheArmedSlotAgain_Disarms_ButKeepsFocus() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(Cells(grid)[1]).Click();
            Assert.Equal(1, grid.SelectedIndex);

            fixture.StateOf(Cells(grid)[1]).Click();

            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Equal(1, grid.FocusedIndex);
        }

        [AvaloniaFact]
        public void ReturnOnTheFocusedSlot_ArmsIt_AndRaisesChooseRequested_SoAHostCanMoveFocusToOptions() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 2;

            int? chosen = null;
            grid.ChooseRequested += (_, index) => chosen = index;
            fixture.StateOf(grid).KeyDown(Key.Return);

            Assert.Equal(2, grid.SelectedIndex);
            Assert.Equal(2, chosen);
        }

        [AvaloniaFact]
        public void DeleteEmptiesTheFocusedSlot_AndArmsIt() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 0;

            int? cleared = null;
            grid.Cleared += (_, index) => cleared = index;
            fixture.StateOf(grid).KeyDown(Key.Delete);

            Assert.Equal(0, cleared);
            Assert.Equal(0, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void Escape_FirstPressClearsTheArm_KeepsFocus_SecondPressResignsFocus() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 1;
            grid.SelectedIndex = 1;
            fixture.StateOf(grid).Focus();

            fixture.StateOf(grid).KeyDown(Key.Escape);
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.True(grid.IsFocused);

            fixture.StateOf(grid).KeyDown(Key.Escape);
            Assert.False(grid.IsFocused);
        }

        [AvaloniaFact]
        public void SlotsClearedThenRepopulated_ResetsBothFocusedAndSelectedIndex() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 2;
            grid.SelectedIndex = 1;

            grid.Slots.Clear();
            grid.Slots.Add(new PickSlot { Name = "New Module" });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(-1, grid.FocusedIndex);
            Assert.Equal(-1, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void ArmingNote_NothingArmed_NamesTheNextEmptySlot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots(); //Productivity, Speed, empty, empty - 2 of 4 filled
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("2 of 4 slots filled. The next module fills slot 3.", grid.ArmingNote);
        }

        [AvaloniaFact]
        public void ArmingNote_ASlotArmed_NamesTheOneBeingReplaced() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            grid.SelectedIndex = 1;

            Assert.Equal("Replacing slot 2. Press Escape to cancel.", grid.ArmingNote);
        }

        [AvaloniaFact]
        public void ArmingNote_AllFourStates() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), EmptyItemNoun = "module" };
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            grid.Slots.Add(new PickSlot { IsEmpty = true });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("No modules. The next module fills slot 1.", grid.ArmingNote);

            grid.Slots[0] = new PickSlot { Name = "Productivity Module 3" };
            grid.Slots[1] = new PickSlot { Name = "Speed Module 2" };
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("2 of 4 slots filled. The next module fills slot 3.", grid.ArmingNote);

            grid.SelectedIndex = 1;
            Assert.Equal("Replacing slot 2. Press Escape to cancel.", grid.ArmingNote);
            grid.SelectedIndex = -1;

            grid.Slots[2] = new PickSlot { Name = "Efficiency Module 1" };
            grid.Slots[3] = new PickSlot { Name = "Efficiency Module 2" };
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("All 4 slots full. Select a slot to replace.", grid.ArmingNote);
        }

        //A kit-generic Slots list can carry a hole anywhere, so the next target is the first empty index, not the filled count.
        [AvaloniaFact]
        public void ArmingNote_AHoleBeforeTheEnd_NamesTheHoleNotTheFilledCount() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), EmptyItemNoun = "module" };
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            grid.Slots.Add(new PickSlot { IsEmpty = true });
            grid.Slots.Add(new PickSlot { Name = "Speed Module 2" });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            grid.Slots.Add(new PickSlot { IsEmpty = true });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("1 of 4 slots filled. The next module fills slot 1.", grid.ArmingNote);
        }

        //The equipped grid's AXValue is the note line, and it changes live with the arm state.
        [AvaloniaFact]
        public void EquippedGrid_AutomationValueIsTheArmingNote_AndChangesWithSelectedIndex() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            var value = Assert.IsAssignableFrom<IValueProvider>(peer);
            Assert.Equal("2 of 4 slots filled. The next module fills slot 3.", value.Value);

            grid.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Replacing slot 2. Press Escape to cancel.", value.Value);
        }

        //Arming, disarming and equipping post the new note line politely, without moving focus (ruling row 4).
        [AvaloniaFact]
        public void ArmingViaSpace_PostsTheNewNoteAsAnAutomationEvent_WithoutMovingFocus() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            grid.FocusedIndex = 1;

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            AutomationPropertyChangedEventArgs? raised = null;
            peer.PropertyChanged += (_, e) => raised = e;

            fixture.StateOf(grid).KeyDown(Key.Space);

            Assert.NotNull(raised);
            Assert.Equal("Replacing slot 2. Press Escape to cancel.", raised!.NewValue);
            Assert.Equal(1, grid.FocusedIndex);
        }

        //Each option cell's AXHelp names the host-fed target, so the fact is there without reading the note.
        [AvaloniaFact]
        public void OptionCell_HelpTextNamesTheTarget_FillsWhenNothingIsArmed_ReplacesWhenASlotIs() {
            using var fixture = new KitFixture();
            var options = new PickGrid { Theme = KitPickGridTheme(), NamesSlots = false };
            options.Slots.Add(new PickSlot { Name = "Efficiency Module 1" });
            fixture.Window.Content = options;
            Dispatcher.UIThread.RunJobs();

            options.TargetSlot = 3;
            options.TargetIsReplace = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Fills slot 3", AutomationProperties.GetHelpText(Cells(options)[0]));

            options.TargetSlot = 2;
            options.TargetIsReplace = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Replaces slot 2", AutomationProperties.GetHelpText(Cells(options)[0]));
        }

        //Full and unarmed is the one state with no target: dimmed to the unavailable token, still hit-testable, commit a no-op.
        [AvaloniaFact]
        public void FullAndUnarmed_DimsEveryOptionCellToTheUnavailableToken_AndKeepsThemHitTestable() {
            using var fixture = new KitFixture();
            var options = new PickGrid { Theme = KitPickGridTheme(), NamesSlots = false };
            options.Slots.Add(new PickSlot { Name = "Efficiency Module 1" });
            options.Slots.Add(new PickSlot { Name = "Productivity Module 1" });
            fixture.Window.Content = options;
            Dispatcher.UIThread.RunJobs();

            options.TargetSlot = 0;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Opacity.Unavailable", Application.Current.ActualThemeVariant, out object? token);
            Assert.All(Cells(options), cell => {
                Assert.Equal(Assert.IsType<double>(token), cell.Opacity);
                Assert.True(cell.IsHitTestVisible);
            });
        }

        [AvaloniaFact]
        public void FullAndUnarmed_ACommitOnAnOptionCell_IsANoOp() {
            using var fixture = new KitFixture();
            var options = new PickGrid { Theme = KitPickGridTheme(), NamesSlots = false };
            options.Slots.Add(new PickSlot { Name = "Efficiency Module 1" });
            fixture.Window.Content = options;
            Dispatcher.UIThread.RunJobs();
            options.TargetSlot = 0;
            Dispatcher.UIThread.RunJobs();
            bool clicked = false;
            options.CellClicked += (_, _) => clicked = true;

            fixture.StateOf(Cells(options)[0]).Click();

            Assert.False(clicked);
            Assert.Equal(-1, options.SelectedIndex);
            Assert.Equal("Efficiency Module 1", options.Slots[0].Name);
        }

        [AvaloniaFact]
        public void FullAndUnarmed_OptionCellToolTipAndHelpText_NameTheFullState() {
            using var fixture = new KitFixture();
            var options = new PickGrid { Theme = KitPickGridTheme(), NamesSlots = false };
            options.Slots.Add(new PickSlot { Name = "Efficiency Module 1" });
            fixture.Window.Content = options;
            Dispatcher.UIThread.RunJobs();

            options.TargetSlot = 0;
            Dispatcher.UIThread.RunJobs();

            PickCell cell = Cells(options)[0];
            Assert.Equal("All slots full — select a slot to replace", ToolTip.GetTip(cell));
            Assert.Equal("All slots full — select a slot to replace", AutomationProperties.GetHelpText(cell));

            options.TargetSlot = 1; //a slot is armed - both revert (2026-09-17 ruling)
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Efficiency Module 1", ToolTip.GetTip(cell));
            Assert.Equal("Fills slot 1", AutomationProperties.GetHelpText(cell));
        }

        //A cell both unavailable in the save and full-and-unarmed reads the full state: the click is a no-op either way.
        [AvaloniaFact]
        public void FullAndUnarmed_OutranksTheMissingAndUnavailableTooltips() {
            using var fixture = new KitFixture();
            var options = new PickGrid { Theme = KitPickGridTheme(), NamesSlots = false };
            options.Slots.Add(new PickSlot { Name = "Efficiency Module 1", IsUnavailable = true });
            options.Slots.Add(new PickSlot { Name = "Productivity Module 1", IsMissing = true });
            fixture.Window.Content = options;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Efficiency Module 1 — not available in this save", ToolTip.GetTip(Cells(options)[0]));

            options.TargetSlot = 0;
            Dispatcher.UIThread.RunJobs();

            Assert.All(Cells(options), cell => {
                Assert.Equal("All slots full — select a slot to replace", ToolTip.GetTip(cell));
                Assert.Equal("All slots full — select a slot to replace", AutomationProperties.GetHelpText(cell));
            });
        }

        //A re-solve changes a slot's content, not the arming state, so VoiceOver hears nothing until an arm does change it.
        [AvaloniaFact]
        public void ReSolvingASlotsContent_PostsNoArmingAnnouncement_UnlikeAnArm() {
            using var fixture = new KitFixture();
            PickGrid grid = FourSlots();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            int posts = 0;
            peer.PropertyChanged += (_, _) => posts++;

            grid.Slots[1] = new PickSlot { Name = "Speed Module 2" };
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, posts);

            grid.Slots[1] = new PickSlot { Name = "Efficiency Module 1" };
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, posts);

            grid.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, posts);
            Assert.Equal("Replacing slot 2. Press Escape to cancel.", grid.ArmingNote);
        }
    }
}
