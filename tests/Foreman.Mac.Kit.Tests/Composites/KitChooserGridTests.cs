using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitChooserGridTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitChooserGridTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ChooserGrid", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        //Section A wraps (3 items over 2 columns), Section B does not (2 items) - lets the wrap/leading-align
        //and the cross-section Up/Down land land on real geometry rather than assumed columns.
        private static ChooserGrid TwoSectionGrid(double width = 250, double height = 400) {
            var grid = new ChooserGrid { Theme = KitChooserGridTheme(), Width = width, Height = height };
            var sectionA = new ChooserSection { Group = "Production", Subgroup = "Smelting" };
            sectionA.Items.Add(new ChooserItem { Name = "Iron plate" });
            sectionA.Items.Add(new ChooserItem { Name = "Copper plate" });
            sectionA.Items.Add(new ChooserItem { Name = "Steel plate" });
            var sectionB = new ChooserSection { Group = "Production", Subgroup = "Metallurgy" };
            sectionB.Items.Add(new ChooserItem { Name = "Casting iron plate" });
            sectionB.Items.Add(new ChooserItem { Name = "Molten iron from lava" });
            grid.Sections.Add(sectionA);
            grid.Sections.Add(sectionB);
            return grid;
        }

        private static ChooserCell[] Cells(ChooserGrid grid) =>
            [.. grid.GetVisualDescendants().OfType<ChooserCell>()];

        private static StackPanel[] Headings(ChooserGrid grid) =>
            [.. grid.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Track")
                .Children.OfType<StackPanel>().Where(p => p.Classes.Contains("chooser-heading"))];

        private static string HeadingName(StackPanel heading) =>
            AutomationProperties.GetName(heading) ?? "";

        private static ScrollViewer Scroll(ChooserGrid grid) =>
            grid.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PART_Scroll");

        [AvaloniaFact]
        public void KitChooserGrid_IsAControlThemeTargetingChooserGrid() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ChooserGrid), KitChooserGridTheme().TargetType);
        }

        [AvaloniaFact]
        public void AutoFillColumns_WrapsAtThePitchAndStaysLeadingAligned() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ChooserCell[] cells = Cells(grid);
            //Section A: Iron plate, Copper plate share row 0; Steel plate alone on row 1.
            Assert.Equal(cells[0].Bounds.Y, cells[1].Bounds.Y);
            Assert.True(cells[2].Bounds.Y > cells[0].Bounds.Y);
            Assert.True(cells[0].Bounds.X < cells[1].Bounds.X);
            //Last row of the section does not stretch or centre - the lone cell sits at the leading column.
            Assert.Equal(cells[0].Bounds.X, cells[2].Bounds.X);
        }

        [AvaloniaFact]
        public void Headings_AreSectionHeaderType_AndNotFocusable() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            StackPanel[] headings = Headings(grid);
            Assert.Equal(2, headings.Length);
            TextBlock run = headings[0].Children.OfType<TextBlock>().First();
            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current.TryGetResource("Foreman.Type.SectionHeader.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            Assert.Equal(Assert.IsType<double>(size), run.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), run.FontWeight);
            Assert.All(headings, h => Assert.All(h.Children.OfType<TextBlock>(), r => Assert.False(r.Focusable)));
        }

        [AvaloniaFact]
        public void BrowsingHeading_IsTheBareSubgroupName_TextUnchanged() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            StackPanel[] headings = Headings(grid);
            Assert.Equal("Smelting", HeadingName(headings[0]));
            Assert.Equal("Metallurgy", HeadingName(headings[1]));
        }

        [AvaloniaFact]
        public void SearchHeading_ReadsGroupMiddotSubgroup_WhenQueryIsActive() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.Query = "plate";
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            StackPanel[] headings = Headings(grid);
            Assert.Equal("Production · Smelting", HeadingName(headings[0]));
            Assert.Equal("Production · Metallurgy", HeadingName(headings[1]));
        }

        [AvaloniaFact]
        public void OneTabStop_TheGridIsFocusable_CellsAreNot() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.True(grid.Focusable);
            Assert.All(Cells(grid), c => Assert.False(c.Focusable));
        }

        [AvaloniaFact]
        public void RightArrow_MovesForward_LeftArrowMovesBack() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[0];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Right);
            Assert.Equal(grid.Sections[0].Items[1], grid.SelectedItem);

            fixture.StateOf(grid).KeyDown(Key.Left);
            Assert.Equal(grid.Sections[0].Items[0], grid.SelectedItem);
        }

        [AvaloniaFact]
        public void LeftArrow_AtTheVeryFirstCell_IsANoOp() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[0];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Left);

            Assert.Equal(grid.Sections[0].Items[0], grid.SelectedItem);
        }

        [AvaloniaFact]
        public void RightArrow_AtTheVeryLastCell_IsANoOp() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[1].Items[1];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Right);

            Assert.Equal(grid.Sections[1].Items[1], grid.SelectedItem);
        }

        [AvaloniaFact]
        public void DownArrow_FromTheLastRowOfASection_LandsInTheFirstRowOfTheNextSection() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[2]; //Steel plate, alone on row 1 of section A
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Down);

            Assert.Equal(grid.Sections[1].Items[0], grid.SelectedItem); //Casting iron plate, row 0 of section B
        }

        [AvaloniaFact]
        public void UpArrow_FromTheFirstRowOfASection_LandsInTheLastRowOfThePreviousSection() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[1].Items[0]; //Casting iron plate, row 0 of section B
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Up);

            Assert.Equal(grid.Sections[0].Items[2], grid.SelectedItem); //Steel plate, row 1 of section A
        }

        [AvaloniaFact]
        public void UpArrow_AtTheTopRow_IsANoOp() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[0];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Up);

            Assert.Equal(grid.Sections[0].Items[0], grid.SelectedItem);
        }

        [AvaloniaFact]
        public void HomeAndEnd_JumpToTheFirstAndLastCellInTheWholeGrid() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[1];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.End);
            Assert.Equal(grid.Sections[1].Items[1], grid.SelectedItem);

            fixture.StateOf(grid).KeyDown(Key.Home);
            Assert.Equal(grid.Sections[0].Items[0], grid.SelectedItem);
        }

        [AvaloniaFact]
        public void PageDown_MovesBySomeMultipleOfARow_PageUpReturns() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid(height: 150);
            grid.SelectedItem = grid.Sections[0].Items[0];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            double startY = Cells(grid)[0].Bounds.Y;
            fixture.StateOf(grid).KeyDown(Key.PageDown);

            var cellByItem = grid.Sections.SelectMany(s => s.Items).Zip(Cells(grid)).ToDictionary(p => p.First, p => p.Second);
            Assert.True(cellByItem[grid.SelectedItem!].Bounds.Y > startY);

            fixture.StateOf(grid).KeyDown(Key.PageUp);
            Assert.Equal(grid.Sections[0].Items[0], grid.SelectedItem);
        }

        [AvaloniaFact]
        public void Return_RaisesActivatedForTheSelectedItem() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[1];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ChooserItem? activated = null;
            grid.Activated += (_, item) => activated = item;

            fixture.StateOf(grid).KeyDown(Key.Return);

            Assert.Equal(grid.Sections[0].Items[1], activated);
        }

        [AvaloniaFact]
        public void TypeToSelect_MatchesWithinTheCurrentSectionFirst() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[0]; //Iron plate
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).Type("c");

            Assert.Equal(grid.Sections[0].Items[1], grid.SelectedItem); //Copper plate, same section
        }

        [AvaloniaFact]
        public void TypeToSelect_FallsThroughToOtherSectionsWhenNothingMatchesLocally() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[0]; //Iron plate; no section-A item starts with "m"
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).Type("m");

            Assert.Equal(grid.Sections[1].Items[1], grid.SelectedItem); //Molten iron from lava
        }

        [AvaloniaFact]
        public void Entrance_StaggersByStaggerStep_CappedAtStaggerCap() {
            using var fixture = new KitFixture();
            var grid = new ChooserGrid { Theme = KitChooserGridTheme(), Width = 700 };
            var section = new ChooserSection { Subgroup = "Long list" };
            for (int i = 0; i < 15; i++)
                section.Items.Add(new ChooserItem { Name = $"Item {i}" });
            grid.Sections.Add(section);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ChooserCell[] cells = Cells(grid);
            var t5 = Assert.Single(cells[5].Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.StaggerStep * 5, t5.Delay);

            var t12 = Assert.Single(cells[12].Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.StaggerStep * Motion.StaggerCap, t12.Delay);
        }

        [AvaloniaFact]
        public void ReduceMotion_YieldsEmptyTransitions_AndZeroDelay() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.All(Cells(grid), c => Assert.Empty(c.Transitions!));
            Assert.All(Cells(grid), c => Assert.Equal(1, c.Opacity));
        }

        [AvaloniaFact]
        public void LiveReduceMotion_RePushesTransitionsOnAnAlreadyBuiltGrid() {
            using var fixture = new KitFixture();
            AppearanceHub? previousCurrent = AppearanceHub.Current;
            var hub = new AppearanceHub(Application.Current!, fixture.Appearance);
            AppearanceHub.Current = hub;
            try {
                ChooserGrid grid = TwoSectionGrid();
                fixture.Window.Content = grid;
                Dispatcher.UIThread.RunJobs();

                Assert.NotEmpty(Cells(grid)[0].Transitions!);

                fixture.Appearance.ReduceMotion = true;
                fixture.Appearance.RaiseChanged();
                Dispatcher.UIThread.RunJobs();

                Assert.All(Cells(grid), c => Assert.Empty(c.Transitions!));
                Assert.All(Cells(grid), c => Assert.Equal(1, c.Opacity));

                fixture.Appearance.ReduceMotion = false;
                fixture.Appearance.RaiseChanged();
                Dispatcher.UIThread.RunJobs();

                Assert.All(Cells(grid), c => Assert.NotEmpty(c.Transitions!));
            } finally {
                hub.Dispose();
                AppearanceHub.Current = previousCurrent;
            }
        }

        [AvaloniaFact]
        public void StickyHeading_StaysAtTheTopOfTheViewportWhileItsSectionScrolls() {
            using var fixture = new KitFixture();
            var grid = new ChooserGrid { Theme = KitChooserGridTheme(), Width = 250, Height = 150 };
            var section = new ChooserSection { Subgroup = "Smelting" };
            for (int i = 0; i < 20; i++)
                section.Items.Add(new ChooserItem { Name = $"Item {i}" });
            grid.Sections.Add(section);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ScrollViewer scroll = Scroll(grid);
            scroll.Offset = new Vector(0, 40);
            Dispatcher.UIThread.RunJobs();

            StackPanel heading = Headings(grid)[0];
            Point topLeft = heading.TranslatePoint(new Point(0, 0), scroll) ?? new Point(-1, -1);
            Assert.Equal(0, topLeft.Y, 1);
        }

        [AvaloniaFact]
        public void StickyHeadings_AfterScroll_SettleWithNoRenderTransformReassignments_AndBoundedLayouts() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid(height: 150);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            //Past the first heading's natural position and into the second heading pushing it back up - the branch that exercises pushLimit.
            ScrollViewer scroll = Scroll(grid);
            scroll.Offset = new Vector(0, 40);
            AnimationClock.Settle();

            //UpdateStickyHeadings always reassigns RenderTransform when it runs, so counting that change is
            //a direct proxy for how many times the scroll-driven Dispatcher.Post loop actually re-fires.
            int updates = 0;
            int layouts = 0;
            foreach (StackPanel heading in Headings(grid))
                heading.PropertyChanged += (_, e) => { if (e.Property == Visual.RenderTransformProperty) updates++; };
            grid.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, updates);
            Assert.True(layouts <= 10, $"ChooserGrid laid out {layouts} times settling sticky headings.");
        }

        [AvaloniaFact]
        public void StateContent_HidesHeadingsAndCells_AndRendersTheSlot() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            var slot = new TextBlock { Text = "No results" };
            grid.StateContent = slot;
            Dispatcher.UIThread.RunJobs();

            Assert.False(Scroll(grid).IsVisible);
            ContentPresenter presenter = grid.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_State");
            Assert.True(presenter.IsVisible);
            Assert.Equal(slot, presenter.Content);
        }

        [AvaloniaFact]
        public void StateContent_AcceptsAKitStatePanel_AndHidesTheGridUnderneath() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.True(Application.Current!.TryGetResource("Kit.StatePanel", null, out object? theme));
            Assert.True(Application.Current!.TryGetResource("Kit.ButtonRow", null, out object? rowTheme));
            Assert.True(Application.Current!.TryGetResource("Kit.Button", null, out object? buttonTheme));
            var row = new ButtonRow { Theme = (ControlTheme)rowTheme! };
            row.Items.Add(new Button { Theme = (ControlTheme)buttonTheme!, Content = "Clear Filters" });
            grid.StateContent = new StatePanel {
                Theme = (ControlTheme)theme!,
                Shape = StatePanelShape.NoResults,
                Headline = "No results",
                Sentence = "Nothing matches this search.",
                Buttons = row,
            };
            Dispatcher.UIThread.RunJobs();

            Assert.False(Scroll(grid).IsVisible);
            Assert.Single(grid.GetVisualDescendants().OfType<StatePanel>());
        }

        [AvaloniaFact]
        public void VoiceOver_GroupNameAnnouncesResultCount() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            Assert.Equal("5 results", peer.GetName());
        }

        [AvaloniaFact]
        public void VoiceOver_EachCellNamesFullNamePositionAndSection() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(Cells(grid)[0]);
            Assert.Equal("Iron plate, 1 of 3, Production Smelting", peer.GetName());
        }

        [AvaloniaFact]
        public void ClickingACell_SelectsItAndFocusesTheGrid() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ChooserCell target = Cells(grid)[2];
            fixture.StateOf(target).Click();

            Assert.Equal(grid.Sections[0].Items[2], grid.SelectedItem);
            Assert.True(grid.IsFocused);
        }

        [AvaloniaFact]
        public void EmptySections_AreSkipped_ZeroItemsDrawsNoHeadingOrCell() {
            using var fixture = new KitFixture();
            var grid = new ChooserGrid { Theme = KitChooserGridTheme(), Width = 250 };
            grid.Sections.Add(new ChooserSection { Subgroup = "Empty group" });
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(Headings(grid));
            Assert.Empty(Cells(grid));
        }

        [AvaloniaFact]
        public void Pumping_RepeatedRebuilds_SettleToABoundedNonZeroLayout() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            for (int i = 0; i < 5; i++) {
                grid.Sections[0].Items.Add(new ChooserItem { Name = $"Extra {i}" });
                Dispatcher.UIThread.RunJobs();
            }
            AnimationClock.Settle(400);

            Assert.True(grid.Bounds.Width > 0);
            Assert.True(grid.Bounds.Height > 0);
            Assert.Equal(10, Cells(grid).Length);
            Assert.Equal(2, Headings(grid).Length);
        }

        [AvaloniaFact]
        public void RemovingAnItem_FadesItOutAndRemovesItWithoutThrowing() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ChooserItem removed = grid.Sections[0].Items[0];
            grid.Sections[0].Items.RemoveAt(0);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(400);

            Assert.DoesNotContain(Cells(grid), c => c.FullName == removed.Name);
            Assert.Equal(4, Cells(grid).Length);
        }

        [AvaloniaFact]
        public void ReAttachingAfterDetach_KeepsTheStickyHeadingSubscriptionHooked() {
            using var fixture = new KitFixture();
            var grid = new ChooserGrid { Theme = KitChooserGridTheme(), Width = 250, Height = 150 };
            var section = new ChooserSection { Subgroup = "Smelting" };
            for (int i = 0; i < 20; i++)
                section.Items.Add(new ChooserItem { Name = $"Item {i}" });
            grid.Sections.Add(section);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ScrollViewer scroll = Scroll(grid);
            scroll.Offset = new Vector(0, 40);
            Dispatcher.UIThread.RunJobs();

            StackPanel heading = Headings(grid)[0];
            Point topLeft = heading.TranslatePoint(new Point(0, 0), scroll) ?? new Point(-1, -1);
            Assert.Equal(0, topLeft.Y, 1);
        }

        [AvaloniaFact]
        public void SelectionSurvives_ARebuildWithTheSameItems() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.SelectedItem = grid.Sections[0].Items[1];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            grid.Query = "same items, different heading text";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(grid.Sections[0].Items[1], grid.SelectedItem);
            Assert.True(Cells(grid)[1].IsSelected);
        }

        [AvaloniaFact]
        public void SearchHeading_MiddleRunGap_EqualsTheSpace15Token() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            grid.Query = "plate";
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            StackPanel heading = Headings(grid)[0];
            TextBlock[] runs = [.. heading.Children.OfType<TextBlock>()];
            Assert.Equal(3, runs.Length);

            Application.Current!.TryGetResource("Foreman.Space.Space15", Application.Current.ActualThemeVariant, out object? token);
            double gap = runs[1].Bounds.X - (runs[0].Bounds.X + runs[0].Bounds.Width);
            Assert.Equal(Assert.IsType<double>(token), gap, 1);
        }

        [AvaloniaFact]
        public void RightArrow_WithNothingSelected_LandsOnCellZero_NotCellOne() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Right);

            Assert.Equal(grid.Sections[0].Items[0], grid.SelectedItem);
        }

        [AvaloniaFact]
        public void TypeToSelect_WithNothingSelected_StartsTheScanAtCellZero() {
            using var fixture = new KitFixture();
            var grid = new ChooserGrid { Theme = KitChooserGridTheme(), Width = 250 };
            var section = new ChooserSection { Subgroup = "Metals" };
            section.Items.Add(new ChooserItem { Name = "Iron plate" });
            section.Items.Add(new ChooserItem { Name = "Ice pack" });
            grid.Sections.Add(section);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).Type("I");

            Assert.Equal(grid.Sections[0].Items[0], grid.SelectedItem); //Iron plate, not Ice pack
        }
    }
}
