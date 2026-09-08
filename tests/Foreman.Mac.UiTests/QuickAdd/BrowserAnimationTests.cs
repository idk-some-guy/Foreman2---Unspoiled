using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views;
using System.Linq;
using Xunit;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Gate fix 8, item 2b/2c: the browser's segment switch cross-fades instead of jumping, the sidebar's
    //selection highlight eases, and cells fade in as they're filtered into view.
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
        public void TheSectionsAreHostedInATransitioningContentControl_WithAnEighteenMillisecondCrossFade() {
            var (win, _) = Open();

            var host = win.GetVisualDescendants().OfType<TransitioningContentControl>().Single(c => c.Name == "SegmentHost");
            var crossFade = Assert.IsType<CrossFade>(host.PageTransition);
            Assert.Equal(180, crossFade.Duration.TotalMilliseconds);
        }

        [AvaloniaFact]
        public void GroupButton_EasesItsBackgroundOverOneHundredTwentyMilliseconds() {
            var (win, _) = Open();

            Button group = win.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("foreman-browser-group"));
            var background = Assert.Single(group.Transitions!.OfType<BrushTransition>(), t => t.Property == Panel.BackgroundProperty);
            Assert.Equal(120, background.Duration.TotalMilliseconds);
        }

        [AvaloniaFact]
        public void EnteringCell_StartsAtOpacityZero() {
            var cell = NewStyledCell("entering");

            Assert.Equal(0, cell.Opacity);
        }

        [AvaloniaFact]
        public void SettledCell_IsFullyOpaque_AndDeclaresTheSpecifiedTransition() {
            var cell = NewStyledCell();

            Assert.Equal(1, cell.Opacity);
            var opacity = Assert.Single(cell.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(120, opacity.Duration.TotalMilliseconds);
            Assert.IsType<CubicEaseOut>(opacity.Easing);
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
