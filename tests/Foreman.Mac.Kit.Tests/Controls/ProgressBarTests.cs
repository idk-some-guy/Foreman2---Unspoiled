using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;
using ProgressBar = Foreman.Mac.Kit.Controls.ProgressBar;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class ProgressBarTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitProgressBarTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ProgressBar", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ProgressBar Themed() => new() { Theme = KitProgressBarTheme() };

        private static Border Indicator(ProgressBar bar) =>
            bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");

        private static Style IndicatorStyle(ControlTheme theme, bool reducedMotion) =>
            theme.Children.OfType<Style>()
                .Single(s => s.Selector!.ToString().Contains("PART_Fill")
                    && s.Selector.ToString().Contains(":not(.reduce-motion)") == !reducedMotion);

        [AvaloniaFact]
        public void KitProgressBar_IsAControlThemeTargetingProgressBar() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ProgressBar), KitProgressBarTheme().TargetType);
        }

        [AvaloniaFact]
        public void Bounds_AreOneFortyBySix_RadiusPill() {
            using var fixture = new KitFixture();
            ProgressBar bar = Themed();
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(140, bar.Width);
            Assert.Equal(6, bar.Height);

            Application.Current!.TryGetResource("Foreman.Radius.Pill", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Indicator(bar).CornerRadius);
        }

        [AvaloniaFact]
        public void Track_ResolvesToBorderStrong() {
            using var fixture = new KitFixture();
            ProgressBar bar = Themed();
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();

            var track = Assert.IsType<Border>(Indicator(bar).Parent);
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), track.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Fill_ResolvesToAccent() {
            using var fixture = new KitFixture();
            ProgressBar bar = Themed();
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Indicator(bar).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Determinate_IndicatorWidthTracksValueFraction() {
            using var fixture = new KitFixture();
            ProgressBar bar = Themed();
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.Value = 62;
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(140 * 0.62, Indicator(bar).Width, 3);
        }

        [AvaloniaFact]
        public void Indeterminate_IndicatorStartsAtSegmentWidth_AndSweeps() {
            using var fixture = new KitFixture();
            ProgressBar bar = Themed();
            bar.IsIndeterminate = true;
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(40, Indicator(bar).Width);
            var group = Assert.IsType<TransformGroup>(Indicator(bar).RenderTransform);
            var translate = Assert.Single(group.Children.OfType<TranslateTransform>());
            Assert.True(translate.X > 0);
        }

        [AvaloniaFact]
        public void Indeterminate_ReduceMotion_IsFullWidthAndAnimatesOpacityNotTransform() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            ProgressBar bar = Themed();
            bar.IsIndeterminate = true;
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(140, Indicator(bar).Width);
            double travelX = (Indicator(bar).RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().SingleOrDefault()?.X ?? 0;
            Assert.Equal(0, travelX);

            var pulse = Assert.IsType<Animation>(Assert.Single(IndicatorStyle(KitProgressBarTheme(), reducedMotion: true).Animations));
            Assert.Equal(Motion.IndeterminateProgressDuration, pulse.Duration);
        }

        [AvaloniaFact]
        public void Detach_ThenReattach_IndeterminateAnimationIsLiveAgain() {
            using var fixture = new KitFixture();
            ProgressBar bar = Themed();
            bar.IsIndeterminate = true;
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            var group = Assert.IsType<TransformGroup>(Indicator(bar).RenderTransform);
            var translate = Assert.Single(group.Children.OfType<TranslateTransform>());
            Assert.True(translate.X > 0);
        }

        private static ProgressBar ShownAt(KitFixture fixture, double value) {
            ProgressBar bar = Themed();
            bar.Maximum = 1;
            bar.Value = value;
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();
            return bar;
        }

        [AvaloniaFact]
        public void Determinate_AValueChange_EasesTheFill_ThenSettlesOnTheTarget() {
            using var fixture = new KitFixture();
            ProgressBar bar = ShownAt(fixture, 0.2);
            Assert.Equal(140 * 0.2, Indicator(bar).Width, 3);

            bar.Value = 0.8;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Indicator(bar).Width < 140 * 0.8 - 1);

            AnimationClock.Settle(400);
            Assert.Equal(140 * 0.8, Indicator(bar).Width, 3);
        }

        [AvaloniaFact]
        public void Automation_IsAProgressIndicator_WhoseValueReadsTheRoundedPercent_AndNoneWhileIndeterminate() {
            using var fixture = new KitFixture();
            ProgressBar bar = ShownAt(fixture, 0.428);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(bar);

            Assert.Equal(AutomationControlType.ProgressBar, peer.GetAutomationControlType());
            var value = Assert.IsAssignableFrom<IValueProvider>(peer);
            Assert.True(value.IsReadOnly);
            Assert.Equal("43 %", value.Value);

            bar.Value = 1;
            Assert.Equal("100 %", value.Value);

            bar.IsIndeterminate = true;
            Assert.Null(value.Value);
        }

        [AvaloniaFact]
        public void Determinate_ReduceMotion_StepsTheFill() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            ProgressBar bar = ShownAt(fixture, 0.2);

            bar.Value = 0.8;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(140 * 0.8, Indicator(bar).Width, 3);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class ProgressBarScaleTests {
        [AvaloniaFact]
        public void TrackWidth_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.ProgressBar", null, out object? theme));
            var bar = new ProgressBar { Theme = (ControlTheme)theme! };
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(140, bar.TrackWidth);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(210, bar.TrackWidth);
        }

        [AvaloniaFact]
        public void TravelDistance_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.ProgressBar", null, out object? theme));
            var bar = new ProgressBar { Theme = (ControlTheme)theme!, IsIndeterminate = true };
            fixture.Window.Content = bar;
            Dispatcher.UIThread.RunJobs();

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(210, bar.TrackWidth);
            Assert.Equal(60, bar.SegmentWidth);
            Assert.Equal(150, bar.TravelDistance);

            AnimationClock.Settle();
            var indicator = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            var group = Assert.IsType<TransformGroup>(indicator.RenderTransform);
            var translate = Assert.Single(group.Children.OfType<TranslateTransform>());
            Assert.True(translate.X > 0);
        }
    }
}
