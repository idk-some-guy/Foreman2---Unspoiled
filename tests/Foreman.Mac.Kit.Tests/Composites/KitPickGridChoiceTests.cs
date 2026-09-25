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
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitPickGridChoiceTests {
        private static ControlTheme KitPickGridTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PickGrid", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        //Four buildings, matching the building-picker mock §02 (one unavailable, the third selected).
        private static PickGrid FourCandidates(int selected = 2) {
            var grid = new PickGrid {
                Theme = KitPickGridTheme(),
                IsChoiceMode = true,
                NamesSlots = false,
                GroupLabel = "Building",
            };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 1" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 2" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 3" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 4", IsUnavailable = true });
            grid.SelectedIndex = selected;
            return grid;
        }

        private static PickCell[] Cells(PickGrid grid) =>
            [.. grid.GetVisualDescendants().OfType<PickCell>()];

        [AvaloniaFact]
        public void Choice_ExactlyOneCellSelected_ForEveryCandidateListIncludingOne() {
            using var fixture = new KitFixture();
            PickGrid grid = FourCandidates();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.Single(Cells(grid), c => c.IsSelected);
            Assert.True(Cells(grid)[2].IsSelected);

            var one = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true };
            one.Slots.Add(new PickSlot { Name = "Electric furnace" });
            one.SelectedIndex = 0;
            var host = new StackPanel { Children = { one } };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            Assert.Single(Cells(one), c => c.IsSelected);
        }

        [AvaloniaFact]
        public void Choice_CellBoundsAre26AtRadius5_GapIs5() {
            using var fixture = new KitFixture();
            PickGrid grid = FourCandidates();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell cell = Cells(grid)[0];
            Assert.Equal(26, cell.Bounds.Width);
            Assert.Equal(26, cell.Bounds.Height);
            Application.Current!.TryGetResource("Foreman.Radius.PickCell", Application.Current.ActualThemeVariant, out object? radius);
            Border frame = cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Assert.Equal(Assert.IsType<CornerRadius>(radius), frame.CornerRadius);

            Application.Current!.TryGetResource("Foreman.Thickness.PickCellGap", Application.Current.ActualThemeVariant, out object? gap);
            Assert.All(Cells(grid), c => Assert.Equal(Assert.IsType<Thickness>(gap), c.Margin));
        }

        [AvaloniaFact]
        public void Choice_WrapsAtTheControlColumn_AndNeverExceedsIt() {
            using var fixture = new KitFixture();
            PickGrid grid = FourCandidates();
            var host = new Border { Width = 108, Child = grid };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            PickCell[] cells = Cells(grid);
            Assert.All(cells, c => Assert.True(
                Avalonia.VisualExtensions.TranslatePoint(c, new Point(c.Bounds.Width, 0), host)!.Value.X <= 108.01));
            //At 108pt (three per row, per the mock's 260pt-minimum figure) a fourth candidate wraps to row two.
            Assert.True(cells[3].Bounds.Y > cells[0].Bounds.Y);
        }

        [AvaloniaFact]
        public void Choice_UpToNineCells_NoCapEnforcedByTheKitItself() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true };
            for (int i = 0; i < 9; i++)
                grid.Slots.Add(new PickSlot { Name = $"Candidate {i}" });
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(9, Cells(grid).Length);
        }

        [AvaloniaFact]
        public void Choice_NoCellRendersThePlusGlyph() {
            using var fixture = new KitFixture();
            PickGrid grid = FourCandidates();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.All(Cells(grid), c => {
                Viewbox plus = c.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_PlusBox");
                Assert.False(plus.IsVisible);
            });
        }

        [AvaloniaFact]
        public void Choice_DeleteLeavesTheSelectionUnchanged() {
            using var fixture = new KitFixture();
            PickGrid grid = FourCandidates();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            int? cleared = null;
            grid.Cleared += (_, i) => cleared = i;
            fixture.StateOf(grid).KeyDown(Key.Delete);

            Assert.Null(cleared);
            Assert.Equal(2, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void Choice_NoneCellRendersTheNoneSlashGlyph_AndCanBeSelected() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false };
            grid.Slots.Add(new PickSlot { Name = "No beacon", IsNone = true });
            grid.Slots.Add(new PickSlot { Name = "Beacon" });
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell noneCell = Cells(grid)[0];
            Assert.True(noneCell.IsSelected);
            Viewbox noneBox = noneCell.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_NoneBox");
            Assert.True(noneBox.IsVisible);
        }

        //Choice mode's own rebuild path (Rebuild() swaps Grid<->WrapPanel and rebinds every cell) never
        //loops or grows unbounded across repeated Slots writes (composites addendum's pumping rule).
        [AvaloniaFact]
        public void Choice_RebuildPumping_StaysBounded() {
            using var fixture = new KitFixture();
            PickGrid grid = FourCandidates();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            for (int i = 0; i < 50; i++) {
                grid.Slots[0] = new PickSlot { Name = $"Assembling machine {i}" };
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(4, Cells(grid).Length);
            Assert.True(grid.Bounds.Width > 0);
        }

        //A25 Loading state (§05 States): icons unresolved renders plain cells, no per-cell spinner, the
        //composites addendum rule carried into choice mode.
        [AvaloniaFact]
        public void Choice_LoadingState_IconsUnresolved_RendersPlainCells_NoGlyphOrSpinner() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 1" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 2" });
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.All(Cells(grid), c => {
                Assert.Null(c.Icon);
                Assert.DoesNotContain(c.GetVisualDescendants().OfType<Viewbox>(), v => v.IsVisible);
                Assert.Empty(c.GetVisualDescendants().OfType<Foreman.Mac.Kit.Controls.Spinner>());
            });
        }

        [AvaloniaFact]
        public void Choice_UnavailableCandidate_StaysFocusableAndChoosable() {
            using var fixture = new KitFixture();
            PickGrid grid = FourCandidates();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell unavailable = Cells(grid)[3];
            Assert.True(unavailable.IsEnabled);
            Assert.True(unavailable.IsHitTestVisible);

            int? chosen = null;
            grid.ChooseRequested += (_, i) => chosen = i;
            fixture.StateOf(unavailable).Click();

            Assert.Equal(3, chosen);
        }
    }

    public sealed class KitPickGridChoiceAnnounceTests {
        private static ControlTheme KitPickGridTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PickGrid", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PickGrid TwoCandidates() {
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false, GroupLabel = "Building" };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 1" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 2" });
            grid.SelectedIndex = 0;
            return grid;
        }

        [AvaloniaFact]
        public void CommittingByPointer_RaisesAPropertyChangedEvent_CarryingTheNewName() {
            using var fixture = new KitFixture();
            PickGrid grid = TwoCandidates();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            AutomationPropertyChangedEventArgs? raised = null;
            peer.PropertyChanged += (_, e) => raised = e;

            PickCell[] cells = [.. grid.GetVisualDescendants().OfType<PickCell>()];
            fixture.StateOf(cells[1]).Click();

            Assert.NotNull(raised);
            Assert.Equal("Assembling machine 2", raised!.NewValue);
        }

        [AvaloniaFact]
        public void CommittingByReturn_RaisesAPropertyChangedEvent_ButAnArrowKeyDoesNot() {
            using var fixture = new KitFixture();
            PickGrid grid = TwoCandidates();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(grid).Focus();
            grid.ChooseRequested += (_, _) => { };

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            int raisedCount = 0;
            peer.PropertyChanged += (_, _) => raisedCount++;

            fixture.StateOf(grid).KeyDown(Key.Right);
            Assert.Equal(0, raisedCount);

            fixture.StateOf(grid).KeyDown(Key.Return);
            Assert.Equal(1, raisedCount);
        }
    }

    public sealed class KitPickGridChoiceKeyboardTests {
        private static ControlTheme KitPickGridTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PickGrid", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PickGrid FourInARow(int selected = 0) {
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 1" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 2" });
            grid.Slots.Add(new PickSlot { Name = "Electric furnace" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 3" });
            grid.SelectedIndex = selected;
            return grid;
        }

        private static PickCell[] Cells(PickGrid grid) =>
            [.. grid.GetVisualDescendants().OfType<PickCell>()];

        [AvaloniaFact]
        public void OneTabStop_TheGridIsFocusable_CellsAreNot() {
            using var fixture = new KitFixture();
            PickGrid grid = FourInARow();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.True(grid.Focusable);
            Assert.All(Cells(grid), c => Assert.False(c.Focusable));
        }

        [AvaloniaFact]
        public void RightArrow_MovesFocus_AndLeavesTheArmedSelectionUntouched() {
            using var fixture = new KitFixture();
            PickGrid grid = FourInARow(selected: 0);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(grid).Focus();

            fixture.StateOf(grid).KeyDown(Key.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, grid.SelectedIndex);
            Assert.True(Cells(grid)[0].IsSelected);
            //The focused (not armed) cell now carries the ring alone.
            Assert.True(FocusRing.GetShowAlways(Cells(grid)[1]));
            Assert.False(FocusRing.GetShowAlways(Cells(grid)[0]));
        }

        [AvaloniaFact]
        public void HomeAndEnd_MoveFocusToFirstAndLast_WithoutArming() {
            using var fixture = new KitFixture();
            PickGrid grid = FourInARow(selected: 1);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(grid).Focus();

            fixture.StateOf(grid).KeyDown(Key.End);
            Assert.Equal(1, grid.SelectedIndex);
            Assert.True(FocusRing.GetShowAlways(Cells(grid)[3]));

            fixture.StateOf(grid).KeyDown(Key.Home);
            Assert.Equal(1, grid.SelectedIndex);
            Assert.True(FocusRing.GetShowAlways(Cells(grid)[0]));
        }

        [AvaloniaFact]
        public void SpaceAndReturn_EachCommitTheFocusedCellExactlyOnce() {
            using var fixture = new KitFixture();
            PickGrid grid = FourInARow(selected: 0);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(grid).Focus();

            int chooseCount = 0;
            int? chosen = null;
            grid.ChooseRequested += (_, i) => { chooseCount++; chosen = i; };

            fixture.StateOf(grid).KeyDown(Key.Right);
            fixture.StateOf(grid).KeyDown(Key.Space);
            Assert.Equal(1, chooseCount);
            Assert.Equal(1, chosen);

            fixture.StateOf(grid).KeyDown(Key.Right);
            fixture.StateOf(grid).KeyDown(Key.Return);
            Assert.Equal(2, chooseCount);
            Assert.Equal(2, chosen);
        }

        //Left/Right are plain linear index steps; a WrapPanel lays cells out in that same reading order, so
        //stepping past a row's last cell lands on the next row's first cell for free - no row-aware code.
        [AvaloniaFact]
        public void LeftAndRightArrows_WrapAtRowEnds_AndClampAtTheGridEnds() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false };
            grid.Slots.Add(new PickSlot { Name = "A" });
            grid.Slots.Add(new PickSlot { Name = "B" });
            grid.Slots.Add(new PickSlot { Name = "C" });
            grid.Slots.Add(new PickSlot { Name = "D" });
            grid.SelectedIndex = 0;
            var host = new Border { Width = 72, Child = grid };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(grid).Focus();

            PickCell[] cells = Cells(grid);
            Assert.True(cells[2].Bounds.Y > cells[1].Bounds.Y); //sanity: two rows of two, B ends row one

            fixture.StateOf(grid).KeyDown(Key.Right); //index0 -> 1 (B, last of row one)
            fixture.StateOf(grid).KeyDown(Key.Right); //wraps to row two's first cell
            Assert.True(FocusRing.GetShowAlways(cells[2]));

            fixture.StateOf(grid).KeyDown(Key.Left); //wraps back to row one's last cell
            Assert.True(FocusRing.GetShowAlways(cells[1]));

            fixture.StateOf(grid).KeyDown(Key.End);
            fixture.StateOf(grid).KeyDown(Key.Right); //clamps at the last cell
            Assert.True(FocusRing.GetShowAlways(cells[3]));

            fixture.StateOf(grid).KeyDown(Key.Home);
            fixture.StateOf(grid).KeyDown(Key.Left); //clamps at the first cell
            Assert.True(FocusRing.GetShowAlways(cells[0]));
            Assert.Equal(0, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void DownArrow_LandsOnTheNearestXCellInTheNextRow_AndClampsAtTheLastRow() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false };
            for (int i = 0; i < 5; i++)
                grid.Slots.Add(new PickSlot { Name = $"Candidate {i}" });
            grid.SelectedIndex = 0;
            var host = new Border { Width = 108, Child = grid };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(grid).Focus();

            PickCell[] cells = Cells(grid);
            Assert.True(cells[3].Bounds.Y > cells[2].Bounds.Y); //sanity: row two starts at index 3 (two cells)

            fixture.StateOf(grid).KeyDown(Key.Right);
            fixture.StateOf(grid).KeyDown(Key.Right); //focus index 2, the last (rightmost) cell of row one

            fixture.StateOf(grid).KeyDown(Key.Down);
            Assert.True(FocusRing.GetShowAlways(cells[4])); //row two's rightmost cell sits nearer index 2's X than its leftmost
            Assert.Equal(0, grid.SelectedIndex);

            fixture.StateOf(grid).KeyDown(Key.Down); //already the last row - clamps
            Assert.True(FocusRing.GetShowAlways(cells[4]));
            Assert.Equal(0, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void UpArrow_LandsOnTheNearestXCellInThePreviousRow_AndClampsAtTheFirstRow() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false };
            for (int i = 0; i < 5; i++)
                grid.Slots.Add(new PickSlot { Name = $"Candidate {i}" });
            grid.SelectedIndex = 0;
            var host = new Border { Width = 108, Child = grid };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(grid).Focus();

            PickCell[] cells = Cells(grid);
            fixture.StateOf(grid).KeyDown(Key.End); //focus index 4, row two's last cell

            fixture.StateOf(grid).KeyDown(Key.Up);
            Assert.True(FocusRing.GetShowAlways(cells[1])); //row one's middle cell sits nearest index 4's X
            Assert.Equal(0, grid.SelectedIndex);

            fixture.StateOf(grid).KeyDown(Key.Up); //already the first row - clamps
            Assert.True(FocusRing.GetShowAlways(cells[1]));
            Assert.Equal(0, grid.SelectedIndex);
        }

        [AvaloniaFact]
        public void TypeToSelect_JumpsFocusToTheFirstMatchingName() {
            using var fixture = new KitFixture();
            PickGrid grid = FourInARow(selected: 0);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(grid).Focus();

            grid.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "e" });
            grid.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "l" });
            grid.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "e" });

            Assert.Equal(0, grid.SelectedIndex);
            Assert.True(FocusRing.GetShowAlways(Cells(grid)[2]));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public sealed class KitPickGridChoiceScaleTests {
        private static ControlTheme KitPickGridTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PickGrid", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        //The choice grid's own 150% coverage (moved off the gallery tile, which now keeps only registration):
        //the cell multiplies with the display factor, the gap thickness does not, and the wrap recompute
        //against the scaled control column can turn a one-row list into two (§04's own 100%/150% pair).
        [AvaloniaFact]
        public void At150Percent_CellIs39_GapStaysAt5_AndAOneRowListWrapsToTwoRows() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 1" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 2" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 3" });
            grid.SelectedIndex = 0;
            var host = new Border { Width = 108, Child = grid };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            PickCell[] before = [.. grid.GetVisualDescendants().OfType<PickCell>()];
            Assert.All(before, c => Assert.Equal(before[0].Bounds.Y, c.Bounds.Y)); //one row at 100%

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            PickCell[] cells = [.. grid.GetVisualDescendants().OfType<PickCell>()];
            Assert.Equal(39, cells[0].Bounds.Width);
            Assert.Equal(new Thickness(2.5), cells[0].Margin); //Foreman.Thickness.PickCellGap does not multiply
            Assert.True(cells[2].Bounds.Y > cells[0].Bounds.Y); //the same three candidates now take two rows
        }
    }

    public sealed class KitPickGridChoiceA11yTests {
        private static ControlTheme KitPickGridTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PickGrid", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        [AvaloniaFact]
        public void AutomationRoleIsListWithTheRowLabelAsItsName() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false, GroupLabel = "Building" };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 3" });
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
            Assert.Equal("Building", peer.GetName());
        }

        [AvaloniaFact]
        public void EachCellIsRadioButtonNamed_WithIsSelectedTrueOnExactlyOne() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false, GroupLabel = "Building" };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 1" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 2" });
            grid.SelectedIndex = 1;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell[] cells = [.. grid.GetVisualDescendants().OfType<PickCell>()];
            AutomationPeer first = ControlAutomationPeer.CreatePeerForElement(cells[0]);
            AutomationPeer second = ControlAutomationPeer.CreatePeerForElement(cells[1]);

            Assert.Equal(AutomationControlType.RadioButton, first.GetAutomationControlType());
            Assert.Equal("Assembling machine 1, 1 of 2", first.GetName());
            Assert.Equal("Assembling machine 2, 2 of 2", second.GetName());

            Assert.False(first.GetProvider<ISelectionItemProvider>()!.IsSelected);
            Assert.True(second.GetProvider<ISelectionItemProvider>()!.IsSelected);
        }

        [AvaloniaFact]
        public void TheUnavailableCellCarriesHelpText_AndStaysEnabled() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 3", IsUnavailable = true });
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell cell = grid.GetVisualDescendants().OfType<PickCell>().Single();
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(cell);
            Assert.Equal("Assembling machine 3 — not available in this save", peer.GetHelpText());
            Assert.True(peer.IsEnabled());
        }

        //A choice grid is a radio group by definition, never a set of slots - NamesSlots' default (true,
        //meant for the equipped/options grids) must not leak "slot" wording into a choice grid's own names.
        [AvaloniaFact]
        public void NamesSlotsLeftAtDefault_ChoiceCellsStillNameWithNoSlotWording() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, GroupLabel = "Building" };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 1" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 2" });
            grid.SelectedIndex = 0;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            PickCell[] cells = [.. grid.GetVisualDescendants().OfType<PickCell>()];
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(cells[0]);
            Assert.Equal("Assembling machine 1, 1 of 2", peer.GetName());

            AutomationPeer groupPeer = ControlAutomationPeer.CreatePeerForElement(grid);
            Assert.Equal("Building", groupPeer.GetName());
        }

        [AvaloniaFact]
        public void TheGroupsHelpTextEqualsTheValueLine() {
            using var fixture = new KitFixture();
            var grid = new PickGrid { Theme = KitPickGridTheme(), IsChoiceMode = true, NamesSlots = false, GroupLabel = "Building" };
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 1" });
            grid.Slots.Add(new PickSlot { Name = "Assembling machine 3" });
            grid.SelectedIndex = 1;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            Assert.Equal("Assembling machine 3", peer.GetHelpText());
        }
    }
}
