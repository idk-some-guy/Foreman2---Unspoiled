using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class SpinnerTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitSpinnerTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Spinner", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Spinner Themed(params string[] classes) {
            var spinner = new Spinner { Theme = KitSpinnerTheme() };
            foreach (string cls in classes)
                spinner.Classes.Add(cls);
            return spinner;
        }

        private static Ellipse Track(Spinner spinner) =>
            spinner.GetVisualDescendants().OfType<Ellipse>().Single(el => el.Name == "PART_Track");

        internal static Arc Head(Spinner spinner) =>
            spinner.GetVisualDescendants().OfType<Arc>().Single(a => a.Name == "PART_Head");

        private static Style HeadStyle(ControlTheme theme, bool reducedMotion) =>
            theme.Children.OfType<Style>()
                .Single(s => s.Selector!.ToString().Contains("PART_Head")
                    && s.Selector.ToString().Contains(":not(.reduce-motion)") == !reducedMotion);

        [AvaloniaFact]
        public void KitSpinner_IsAControlThemeTargetingSpinner() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Spinner), KitSpinnerTheme().TargetType);
        }

        [AvaloniaFact]
        public void Default_IsEighteenPointTwoStroke() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(18, Track(spinner).Width);
            Assert.Equal(18, Track(spinner).Height);
            Assert.Equal(2, Track(spinner).StrokeThickness);
            Assert.Equal(18, Head(spinner).Width);
            Assert.Equal(2, Head(spinner).StrokeThickness);
        }

        [AvaloniaFact]
        public void LargeClass_IsTwentyFourPointTwoPointFiveStroke() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed("lg");
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(24, Track(spinner).Width);
            Assert.Equal(2.5, Track(spinner).StrokeThickness);
            Assert.Equal(24, Head(spinner).Width);
            Assert.Equal(2.5, Head(spinner).StrokeThickness);
        }

        [AvaloniaFact]
        public void TrackAndHead_ResolveToBorderStrongAndAccent() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), Track(spinner).Stroke?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Head(spinner).Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void HeadSweepIsNinetyDegrees() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(90, Head(spinner).SweepAngle);
        }

        [AvaloniaFact]
        public void Head_AnimatesViaARotateTransformOnItself_NotTheRoot() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            var group = Assert.IsType<TransformGroup>(Head(spinner).RenderTransform);
            var rotate = Assert.Single(group.Children.OfType<RotateTransform>());
            Assert.NotEqual(0, rotate.Angle);
        }

        [AvaloniaFact]
        public void Detach_ThenReattach_HeadAnimationIsLiveAgain() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            var group = Assert.IsType<TransformGroup>(Head(spinner).RenderTransform);
            var rotate = Assert.Single(group.Children.OfType<RotateTransform>());
            Assert.NotEqual(0, rotate.Angle);
        }

        [AvaloniaFact]
        public void Duration_IsSpinnerDurationByDefault() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("reduce-motion", spinner.Classes);
            Assert.Equal(Motion.SpinnerDuration, spinner.RotationDuration);
            var animation = Assert.IsType<Animation>(Assert.Single(HeadStyle(KitSpinnerTheme(), reducedMotion: false).Animations));
            Assert.Equal(Motion.SpinnerDuration, animation.Duration);
        }

        [AvaloniaFact]
        public void Duration_FlipsToReducedMotionDuration_UnderReduceMotion() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("reduce-motion", spinner.Classes);
            Assert.Equal(Motion.SpinnerReducedMotionDuration, spinner.RotationDuration);
            var animation = Assert.IsType<Animation>(Assert.Single(HeadStyle(KitSpinnerTheme(), reducedMotion: true).Animations));
            Assert.Equal(Motion.SpinnerReducedMotionDuration, animation.Duration);
        }

        [AvaloniaTheory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void HeadCurve_StaysOnTheTracksCircleThroughARotation(double angle) {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            var group = Assert.IsType<TransformGroup>(Head(spinner).RenderTransform);
            var rotate = Assert.Single(group.Children.OfType<RotateTransform>());
            rotate.SetCurrentValue(RotateTransform.AngleProperty, angle);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(angle, rotate.Angle);
            AssertHeadStaysInsideTheSpinner(spinner);
        }

        internal static void AssertHeadStaysInsideTheSpinner(Spinner spinner) {
            Arc head = Head(spinner);

            Matrix transform = head.TransformToVisual(spinner) ?? Matrix.Identity;
            Rect transformedBounds = new Rect(head.Bounds.Size).TransformToAABB(transform);

            double tolerance = head.Bounds.Width / 18.0;
            Assert.True(new Rect(spinner.Bounds.Size).Inflate(tolerance).Contains(transformedBounds),
                $"head's transformed bounds {transformedBounds} escaped the spinner's own bounds {spinner.Bounds} — the head has drifted off-center");
        }

        [AvaloniaFact]
        public void HiddenAtAttach_GainsTheAnimatingClass_WhenShownLater() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            spinner.IsVisible = false;
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("animating", spinner.Classes);

            spinner.IsVisible = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("animating", spinner.Classes);
        }

        [AvaloniaFact]
        public void Detach_UnsubscribesFromResourceObservables() {
            using var fixture = new KitFixture();
            Spinner spinner = Themed();
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            double before = spinner.Diameter;
            Application.Current!.Resources["Foreman.Size.Spinner"] = 99.0;
            try {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(before, spinner.Diameter);
            } finally {
                Application.Current!.Resources.Remove("Foreman.Size.Spinner");
            }
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class SpinnerScaleTests {
        [AvaloniaTheory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void HeadCurve_StaysOnTheTracksCircleAtOneAndAHalfTimesScale(double angle) {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Spinner", null, out object? theme));
            var spinner = new Spinner { Theme = (ControlTheme)theme! };
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();
            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            var group = Assert.IsType<TransformGroup>(SpinnerTests.Head(spinner).RenderTransform);
            var rotate = Assert.Single(group.Children.OfType<RotateTransform>());
            rotate.SetCurrentValue(RotateTransform.AngleProperty, angle);
            Dispatcher.UIThread.RunJobs();

            SpinnerTests.AssertHeadStaysInsideTheSpinner(spinner);
        }

        [AvaloniaFact]
        public void Diameter_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Spinner", null, out object? theme));
            var spinner = new Spinner { Theme = (ControlTheme)theme! };
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(18, spinner.Diameter);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(27, spinner.Diameter);
            Assert.Equal(3, spinner.StrokeWidth);
        }
    }
}
