using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using Foreman.Mac.Themes;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Gate fix 8, item 2b/2c / gate fix 11: the browser's segment switch cross-fades instead of jumping, the
    //sidebar's selection highlight eases, and cells fade in as they're filtered into view - all on Motion's
    //shared timings, and none of it replays for a no-op click or an equivalent search.
    public sealed class BrowserAnimationTests {
        private static (AddBrowserWindow win, AddBrowserViewModel vm) Open() {
            var fx = CatalogFixture.WithGroups();
            var settings = new AppSettings();
            var vm = new AddBrowserViewModel(new AddCatalog(fx.Cache, settings), fx.Cache, settings, (_, _) => null);
            var win = new AddBrowserWindow { DataContext = vm };
            win.Show();
            Dispatcher.UIThread.RunJobs();
            return (win, vm);
        }

        [AvaloniaFact]
        public void TheSectionsAreHostedInATransitioningContentControl_WithACrossFadeFromMotion() {
            var (win, _) = Open();

            var host = win.GetVisualDescendants().OfType<TransitioningContentControl>().Single(c => c.Name == "SegmentHost");
            var crossFade = Assert.IsType<CrossFade>(host.PageTransition);
            Assert.Equal(Motion.CrossFadeDuration.TotalMilliseconds, crossFade.Duration.TotalMilliseconds);
            Assert.IsType(Motion.CrossFadeEasing.GetType(), crossFade.FadeInEasing);
            Assert.IsType(Motion.CrossFadeEasing.GetType(), crossFade.FadeOutEasing);
        }

        //Gate fix 11, item 2a: a filter keystroke mutates Sections in place without touching Segment, so
        //the cross-fade host must never dip back to Opacity 0 for it - only an Items<->Recipes switch does.
        [AvaloniaFact]
        public void FilteringWithinASegment_NeverDropsTheSegmentHostsOpacity() {
            var (win, vm) = Open();
            var host = win.GetVisualDescendants().OfType<TransitioningContentControl>().Single(c => c.Name == "SegmentHost");
            Assert.Equal(1, host.Opacity);

            vm.SearchText = "chest";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, host.Opacity);

            vm.SearchText = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, host.Opacity);
        }

        [AvaloniaFact]
        public void GroupButton_EasesItsBackgroundOverMotionsHoverDuration() {
            var (win, _) = Open();

            Button group = win.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("foreman-browser-group"));
            var background = Assert.Single(group.Transitions!.OfType<BrushTransition>(), t => t.Property == Panel.BackgroundProperty);
            Assert.Equal(Motion.HoverDuration.TotalMilliseconds, background.Duration.TotalMilliseconds);
        }

        [AvaloniaFact]
        public void EnteringCell_StartsAtOpacityZero() {
            var cell = NewStyledCell("entering");

            Assert.Equal(0, cell.Opacity);
            Assert.NotNull(cell.RenderTransform);
        }

        [AvaloniaFact]
        public void SettledCell_IsFullyOpaque_AndDeclaresTheSpecifiedTransition() {
            var cell = NewStyledCell();

            Assert.Equal(1, cell.Opacity);
            var opacity = Assert.Single(cell.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.EntranceDuration.TotalMilliseconds, opacity.Duration.TotalMilliseconds);
            Assert.IsType(Motion.EntranceEasing.GetType(), opacity.Easing);

            var slide = Assert.Single(cell.Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Motion.EntranceDuration.TotalMilliseconds, slide.Duration.TotalMilliseconds);
            Assert.IsType(Motion.EntranceEasing.GetType(), slide.Easing);
        }

        //End to end: a cell realized by the real browser sheds .entering and settles at full opacity.
        [AvaloniaFact]
        public void ACellRealizedByTheBrowser_EventuallySettlesAtFullOpacity_WithEnteringShed() {
            var (win, _) = Open();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Button cell = win.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("foreman-browser-cell"));
            Assert.DoesNotContain("entering", cell.Classes);
            Assert.Equal(1, cell.Opacity, 2);
        }

        //Gate fix 11, item 2b: cells cascade top-down instead of popping together.
        [AvaloniaFact]
        public void CellsRealizedByTheBrowser_HaveAStaggerDelayThatGrowsWithIndex() {
            var (win, _) = Open(); //Storage subgroup has two available cells - Wooden Chest (0), Iron Chest (1).
            Dispatcher.UIThread.RunJobs();

            var cells = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-cell")).ToList();
            TimeSpan DelayOf(int index) => cells[index].Transitions!.OfType<DoubleTransition>().First(t => t.Property == Visual.OpacityProperty).Delay;

            Assert.Equal(Motion.StaggerDelay(0), DelayOf(0));
            Assert.Equal(Motion.StaggerDelay(1), DelayOf(1));
            Assert.True(DelayOf(1) > DelayOf(0));
        }

        //Gate fix 11, item 1: clicking the already-selected group must not rebuild Sections - the same
        //cell Buttons stay in the visual tree, so nothing replays its entrance animation.
        [AvaloniaFact]
        public void ClickingTheAlreadySelectedGroup_LeavesTheRealizedCellsUntouched() {
            var (win, vm) = Open();
            Dispatcher.UIThread.RunJobs();
            var cellsBefore = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-cell")).ToList();

            vm.SelectGroup(vm.Groups[0].Group);
            Dispatcher.UIThread.RunJobs();

            var cellsAfter = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-cell")).ToList();
            Assert.Equal(cellsBefore.Count, cellsAfter.Count);
            Assert.True(cellsBefore.Zip(cellsAfter, ReferenceEquals).All(same => same));
        }

        //Gate fix 11, item 1: retyping an equivalent search (trailing whitespace) must not rebuild Sections.
        [AvaloniaFact]
        public void RetypingAnEquivalentSearch_LeavesTheRealizedCellsUntouched() {
            var (win, vm) = Open();
            vm.SearchText = "chest";
            Dispatcher.UIThread.RunJobs();
            var cellsBefore = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-cell")).ToList();

            vm.SearchText = "chest ";
            Dispatcher.UIThread.RunJobs();

            var cellsAfter = win.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("foreman-browser-cell")).ToList();
            Assert.Equal(cellsBefore.Count, cellsAfter.Count);
            Assert.True(cellsBefore.Zip(cellsAfter, ReferenceEquals).All(same => same));
        }

        private static Button NewStyledCell(params string[] extraClasses) {
            var cell = new Button { Classes = { "foreman-browser-cell" } };
            foreach (string extra in extraClasses)
                cell.Classes.Add(extra);
            var window = new Avalonia.Controls.Window { Content = cell, Width = 200, Height = 60 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return cell;
        }
    }
}
