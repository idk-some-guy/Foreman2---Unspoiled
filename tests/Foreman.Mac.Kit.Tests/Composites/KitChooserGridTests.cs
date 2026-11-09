using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
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
        public void AnItemMarkedIconMissing_BuildsItsCellIconMissing_AndOnlyThatCell() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem copper = grid.Sections[0].Items[1];
            copper.IsIconMissing = true;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["Copper plate"], Cells(grid).Where(c => c.IsIconMissing).Select(c => c.FullName));
        }

        [AvaloniaFact]
        public void AutoFillColumns_WrapsAtThePitchAndStaysLeadingAligned() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ChooserCell[] cells = Cells(grid);
            Assert.Equal(cells[0].Bounds.Y, cells[1].Bounds.Y);
            Assert.True(cells[2].Bounds.Y > cells[0].Bounds.Y);
            Assert.True(cells[0].Bounds.X < cells[1].Bounds.X);
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
            ChooserItem iron = grid.Sections[0].Items[0];
            ChooserItem copper = grid.Sections[0].Items[1];
            grid.SelectedItem = iron;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Right);
            Assert.Equal(copper, grid.SelectedItem);

            fixture.StateOf(grid).KeyDown(Key.Left);
            Assert.Equal(iron, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void LeftArrow_AtTheVeryFirstCell_IsANoOp() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem iron = grid.Sections[0].Items[0];
            grid.SelectedItem = iron;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Left);

            Assert.Equal(iron, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void RightArrow_AtTheVeryLastCell_IsANoOp() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem moltenIron = grid.Sections[1].Items[1];
            grid.SelectedItem = moltenIron;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Right);

            Assert.Equal(moltenIron, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void DownArrow_FromTheLastRowOfASection_LandsInTheFirstRowOfTheNextSection() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem steel = grid.Sections[0].Items[2];
            ChooserItem castingIron = grid.Sections[1].Items[0];
            grid.SelectedItem = steel;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Down);

            Assert.Equal(castingIron, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void UpArrow_FromTheFirstRowOfASection_LandsInTheLastRowOfThePreviousSection() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem steel = grid.Sections[0].Items[2];
            ChooserItem castingIron = grid.Sections[1].Items[0];
            grid.SelectedItem = castingIron;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Up);

            Assert.Equal(steel, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void UpArrow_AtTheTopRow_IsANoOp() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem iron = grid.Sections[0].Items[0];
            grid.SelectedItem = iron;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Up);

            Assert.Equal(iron, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void HomeAndEnd_JumpToTheFirstAndLastCellInTheWholeGrid() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem iron = grid.Sections[0].Items[0];
            ChooserItem copper = grid.Sections[0].Items[1];
            ChooserItem moltenIron = grid.Sections[1].Items[1];
            grid.SelectedItem = copper;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.End);
            Assert.Equal(moltenIron, grid.SelectedItem);

            fixture.StateOf(grid).KeyDown(Key.Home);
            Assert.Equal(iron, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void PageDown_MovesBySomeMultipleOfARow_PageUpReturns() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid(height: 150);
            ChooserItem iron = grid.Sections[0].Items[0];
            grid.SelectedItem = iron;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            double startY = Cells(grid)[0].Bounds.Y;
            fixture.StateOf(grid).KeyDown(Key.PageDown);

            var cellByItem = grid.Sections.SelectMany(s => s.Items).Zip(Cells(grid)).ToDictionary(p => p.First, p => p.Second);
            Assert.True(cellByItem[grid.SelectedItem!].Bounds.Y > startY);

            fixture.StateOf(grid).KeyDown(Key.PageUp);
            Assert.Equal(iron, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void Return_RaisesActivatedForTheSelectedItem() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem copper = grid.Sections[0].Items[1];
            grid.SelectedItem = copper;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ChooserItem? activated = null;
            grid.Activated += (_, item) => activated = item;

            fixture.StateOf(grid).KeyDown(Key.Return);

            Assert.Equal(copper, activated);
        }

        [AvaloniaFact]
        public void TypeToSelect_MatchesWithinTheCurrentSectionFirst() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem iron = grid.Sections[0].Items[0];
            ChooserItem copper = grid.Sections[0].Items[1];
            grid.SelectedItem = iron;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).Type("c");

            Assert.Equal(copper, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void TypeToSelect_FallsThroughToOtherSectionsWhenNothingMatchesLocally() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem iron = grid.Sections[0].Items[0];
            ChooserItem moltenIron = grid.Sections[1].Items[1];
            grid.SelectedItem = iron;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).Type("m");

            Assert.Equal(moltenIron, grid.SelectedItem);
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

            ScrollViewer scroll = Scroll(grid);
            scroll.Offset = new Vector(0, 40);
            AnimationClock.Settle();

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
            ChooserItem steel = grid.Sections[0].Items[2];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            ChooserCell target = Cells(grid)[2];
            fixture.StateOf(target).Click();

            Assert.Equal(steel, grid.SelectedItem);
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
            ChooserItem copper = grid.Sections[0].Items[1];
            grid.SelectedItem = copper;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            grid.Query = "same items, different heading text";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(copper, grid.SelectedItem);
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
            ChooserItem iron = grid.Sections[0].Items[0];
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).KeyDown(Key.Right);

            Assert.Equal(iron, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void TypeToSelect_WithNothingSelected_StartsTheScanAtCellZero() {
            using var fixture = new KitFixture();
            var grid = new ChooserGrid { Theme = KitChooserGridTheme(), Width = 250 };
            var section = new ChooserSection { Subgroup = "Metals" };
            var ironPlate = new ChooserItem { Name = "Iron plate" };
            section.Items.Add(ironPlate);
            section.Items.Add(new ChooserItem { Name = "Ice pack" });
            grid.Sections.Add(section);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(grid).Type("I");

            Assert.Equal(ironPlate, grid.SelectedItem);
        }

        [AvaloniaFact]
        public void UnavailableItem_IsDimmedButChoosable_AClickSelectsIt_AndReturnActivatesIt() {
            using var fixture = new KitFixture();
            ChooserGrid grid = TwoSectionGrid();
            ChooserItem steel = grid.Sections[0].Items[2];
            steel.IsAvailable = false;
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            ChooserCell cell = Cells(grid).Single(c => c.FullName == "Steel plate");
            Assert.True(cell.IsUnavailable);
            Assert.True(cell.IsEnabled);
            Border frame = cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Application.Current!.TryGetResource("Foreman.Opacity.Unavailable", Application.Current.ActualThemeVariant, out object? unavailable);
            Assert.Equal(Assert.IsType<double>(unavailable), frame.Opacity);
            Assert.All(Cells(grid).Where(c => c != cell), c => Assert.False(c.IsUnavailable));

            fixture.StateOf(cell).Click();
            Assert.Same(steel, grid.SelectedItem);

            ChooserItem? activated = null;
            grid.Activated += (_, item) => activated = item;
            fixture.StateOf(grid).KeyDown(Key.Return);
            Assert.Same(steel, activated);
        }
    }
}
