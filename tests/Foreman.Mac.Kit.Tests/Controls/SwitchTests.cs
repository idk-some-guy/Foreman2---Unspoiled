using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class SwitchTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitSwitchTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Switch", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Switch Themed() => new() { Theme = KitSwitchTheme() };

        private static Border Track(Switch toggle) => toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Track");
        private static Border Knob(Switch toggle) => toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Knob");

        [AvaloniaFact]
        public void KitSwitch_IsAControlThemeTargetingSwitch() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Switch), KitSwitchTheme().TargetType);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            Switch toggle = Themed();
            fixture.Window.Content = toggle;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.SwitchWidth", Application.Current.ActualThemeVariant, out object? width);
            Application.Current!.TryGetResource("Foreman.Size.SwitchHeight", Application.Current.ActualThemeVariant, out object? height);
            Application.Current!.TryGetResource("Foreman.Size.SwitchKnob", Application.Current.ActualThemeVariant, out object? knobSize);

            Assert.Equal(Assert.IsType<double>(width), Track(toggle).Width);
            Assert.Equal(Assert.IsType<double>(height), Track(toggle).Height);
            Assert.Equal(Assert.IsType<double>(knobSize), Knob(toggle).Width);
        }

        [AvaloniaFact]
        public void Off_TrackIsBorderStrongAndKnobSitsAtTheInset() {
            using var fixture = new KitFixture();
            Switch toggle = Themed();
            fixture.Window.Content = toggle;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), Track(toggle).Background?.ToImmutable());

            Application.Current!.TryGetResource("Foreman.Size.SwitchKnobInset", Application.Current.ActualThemeVariant, out object? inset);
            var transform = Assert.IsType<TransformOperations>(Knob(toggle).RenderTransform);
            Assert.Equal((double)inset!, transform.Value.M31);
        }

        [AvaloniaFact]
        public void On_TrackIsAccentAndKnobSitsAtWidthMinusInsetMinusKnob() {
            using var fixture = new KitFixture();
            Switch toggle = Themed();
            fixture.Window.Content = toggle;

            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Track(toggle).Background?.ToImmutable());

            Application.Current!.TryGetResource("Foreman.Size.SwitchWidth", Application.Current.ActualThemeVariant, out object? width);
            Application.Current!.TryGetResource("Foreman.Size.SwitchKnobInset", Application.Current.ActualThemeVariant, out object? inset);
            Application.Current!.TryGetResource("Foreman.Size.SwitchKnob", Application.Current.ActualThemeVariant, out object? knobSize);
            double expected = (double)width! - (double)inset! - (double)knobSize!;

            var transform = Assert.IsType<TransformOperations>(Knob(toggle).RenderTransform);
            Assert.Equal(expected, transform.Value.M31, 3);
        }

        [AvaloniaFact]
        public void OnToggle_MidTransition_RenderTransformIsTransformOperations() {
            using var fixture = new KitFixture();
            Switch toggle = Themed();
            fixture.Window.Content = toggle;
            Dispatcher.UIThread.RunJobs();

            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.IsType<TransformOperations>(Knob(toggle).RenderTransform);
        }

        [AvaloniaFact]
        public void OnPressed_TrackDarkensToAccentPressed() {
            using var fixture = new KitFixture();
            Switch toggle = Themed();
            fixture.Window.Content = toggle;

            toggle.IsChecked = true;
            fixture.StateOf(toggle).Hover();
            fixture.StateOf(toggle).Press();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentPressed").ToImmutable(), Track(toggle).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            Switch toggle = Themed();
            fixture.Window.Content = toggle;

            toggle.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), toggle.Opacity);
        }

        [AvaloniaFact]
        public void Transitions_SlideTheKnobAtTheSwitchKnobDuration() {
            using var fixture = new KitFixture();
            Switch toggle = Themed();
            fixture.Window.Content = toggle;
            Dispatcher.UIThread.RunJobs();

            var transition = Assert.Single(Knob(toggle).Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Motion.SwitchKnobDuration, transition.Duration);
        }

        [AvaloniaFact]
        public void ReduceMotion_KnobHasNoRenderTransformTransition() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            Switch toggle = Themed();
            fixture.Window.Content = toggle;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(Knob(toggle).Transitions!, t => t is TransformOperationsTransition);
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAPillShapedFocusRing() {
            using var fixture = new KitFixture();
            Switch toggle = Themed();
            fixture.Window.Content = toggle;

            fixture.StateOf(toggle).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(toggle));

            Application.Current!.TryGetResource("Foreman.Radius.Pill", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            double expected = ((CornerRadius)radius!).TopLeft + (double)offset!;
            Assert.Equal(expected, ring.RadiusX);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class SwitchScaleTests {
        [AvaloniaFact]
        public void TrackSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Switch", null, out object? theme));
            var toggle = new Switch { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = toggle;
            Dispatcher.UIThread.RunJobs();

            Border track = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Track");
            Assert.Equal(30, track.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(45, track.Width);
        }

        [AvaloniaFact]
        public void OnStateKnobX_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Switch", null, out object? theme));
            var toggle = new Switch { Theme = Assert.IsType<ControlTheme>(theme), IsChecked = true };
            fixture.Window.Content = toggle;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Border knob = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Knob");
            var transform = Assert.IsType<TransformOperations>(knob.RenderTransform);
            Assert.Equal(21, transform.Value.M31, 3);
        }

        [AvaloniaFact]
        public void OnStateKnobX_SurvivesDetachAndReattach() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Switch", null, out object? theme));
            var toggle = new Switch { Theme = Assert.IsType<ControlTheme>(theme), IsChecked = true };
            var host = new ContentControl { Content = toggle };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            host.Content = null;
            Dispatcher.UIThread.RunJobs();
            host.Content = toggle;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Border knob = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Knob");
            var transform = Assert.IsType<TransformOperations>(knob.RenderTransform);
            Assert.Equal(21, transform.Value.M31, 3);
        }
    }
}
