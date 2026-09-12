using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Foreman.Mac.Kit;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Tokens {
    public sealed class MotionTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        [Theory]
        [InlineData(120)] // PopoverDuration
        public void PopoverDuration_IsOneHundredTwentyMilliseconds(int expectedMs) =>
            Assert.Equal(expectedMs, Motion.PopoverDuration.TotalMilliseconds);

        [Fact]
        public void RadialSweepDurations_AreOneHundredFortyOpeningAndOneHundredClosing() {
            Assert.Equal(140, Motion.RadialSweepDuration.TotalMilliseconds);
            Assert.Equal(100, Motion.RadialSweepCloseDuration.TotalMilliseconds);
        }

        [Fact]
        public void SwitchKnobDuration_IsOneHundredTwentyMilliseconds() =>
            Assert.Equal(120, Motion.SwitchKnobDuration.TotalMilliseconds);

        [Fact]
        public void SegmentedSelectionDuration_IsOneHundredMilliseconds() =>
            Assert.Equal(100, Motion.SegmentedSelectionDuration.TotalMilliseconds);

        [Fact]
        public void ButtonStateDuration_IsOneHundredMilliseconds() =>
            Assert.Equal(100, Motion.ButtonStateDuration.TotalMilliseconds);

        [Fact]
        public void InspectorReflowDuration_IsOneHundredEightyMilliseconds() =>
            Assert.Equal(180, Motion.InspectorReflowDuration.TotalMilliseconds);

        [Fact]
        public void DisclosureDuration_IsOneHundredFiftyMilliseconds() =>
            Assert.Equal(150, Motion.DisclosureDuration.TotalMilliseconds);

        [Fact]
        public void AlignTweenDuration_IsTwoHundredMilliseconds() =>
            Assert.Equal(200, Motion.AlignTweenDuration.TotalMilliseconds);

        [Fact]
        public void TabLabelCrossFadeDuration_IsOneHundredTwentyMilliseconds() =>
            Assert.Equal(120, Motion.TabLabelCrossFadeDuration.TotalMilliseconds);

        [Fact]
        public void SpinnerDuration_SlowsFromPointEightToThreeSecondsUnderReduceMotion() {
            Assert.Equal(800, Motion.SpinnerDuration.TotalMilliseconds);
            Assert.Equal(3000, Motion.SpinnerReducedMotionDuration.TotalMilliseconds);
        }

        //The 10 pre-existing duration/scale constants (Task 4/5) keep their names and values untouched.
        [Fact]
        public void ThePreExistingTenConstants_KeepTheirValues() {
            Assert.Equal(220, Motion.CrossFadeDuration.TotalMilliseconds);
            Assert.Equal(200, Motion.EntranceDuration.TotalMilliseconds);
            Assert.Equal(12, Motion.StaggerStep.TotalMilliseconds);
            Assert.Equal(10, Motion.StaggerCap);
            Assert.Equal(120, Motion.HoverDuration.TotalMilliseconds);
            Assert.Equal(160, Motion.RadialOpenDuration.TotalMilliseconds);
            Assert.Equal(120, Motion.RadialCloseDuration.TotalMilliseconds);
            Assert.Equal(0.9, Motion.RadialOpenScale);
            Assert.Equal(200, Motion.SheetOpenDuration.TotalMilliseconds);
            Assert.Equal(150, Motion.SheetCloseDuration.TotalMilliseconds);
        }

        [Fact]
        public void EntranceEasing_IsCubicEaseOut() => Assert.IsType<CubicEaseOut>(Motion.EntranceEasing);

        [Fact]
        public void LeavingEasing_IsCubicEaseIn() => Assert.IsType<CubicEaseIn>(Motion.LeavingEasing);

        [Fact]
        public void CrossFadeEasing_IsCubicEaseInOut() => Assert.IsType<CubicEaseInOut>(Motion.CrossFadeEasing);

        [AvaloniaFact]
        public void Fade_ReturnsAnOpacityOnlyTransition() {
            var duration = TimeSpan.FromMilliseconds(150);
            var delay = TimeSpan.FromMilliseconds(24);

            Transitions transitions = Motion.Transition(TransitionKind.Fade, duration, Motion.LeavingEasing, delay);

            var opacity = Assert.Single(transitions.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(duration, opacity.Duration);
            Assert.IsType<CubicEaseIn>(opacity.Easing);
            Assert.Equal(delay, opacity.Delay);
            Assert.DoesNotContain(transitions, t => t is TransformOperationsTransition);
        }

        [AvaloniaTheory]
        [InlineData(TransitionKind.SlideFade)]
        [InlineData(TransitionKind.Scale)]
        public void SlideFadeAndScale_ReturnAnOpacityAndARenderTransformTransition(TransitionKind kind) {
            var duration = TimeSpan.FromMilliseconds(200);

            Transitions transitions = Motion.Transition(kind, duration, Motion.EntranceEasing);

            var opacity = Assert.Single(transitions.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(duration, opacity.Duration);
            Assert.IsType<CubicEaseOut>(opacity.Easing);

            var transform = Assert.Single(transitions.OfType<TransformOperationsTransition>());
            Assert.Equal(Visual.RenderTransformProperty, transform.Property);
            Assert.Equal(duration, transform.Duration);
            Assert.IsType<CubicEaseOut>(transform.Easing);
        }

        [AvaloniaFact]
        public void CrossFade_AlwaysUsesTheCrossFadeEasing_RegardlessOfWhatWasPassedIn() {
            Transitions transitions = Motion.Transition(TransitionKind.CrossFade, Motion.CrossFadeDuration, Motion.EntranceEasing);

            var opacity = Assert.Single(transitions.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.IsType<CubicEaseInOut>(opacity.Easing);
            Assert.DoesNotContain(transitions, t => t is TransformOperationsTransition);
        }

        [AvaloniaFact]
        public void Reflow_ReturnsAnOpacityAndAHeightTransition() {
            Transitions transitions = Motion.Transition(TransitionKind.Reflow, Motion.InspectorReflowDuration);

            Assert.Single(transitions.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            var height = Assert.Single(transitions.OfType<DoubleTransition>(), t => t.Property == Layoutable.HeightProperty);
            Assert.Equal(Motion.InspectorReflowDuration, height.Duration);
        }

        [AvaloniaTheory]
        [InlineData(TransitionKind.Fade)]
        [InlineData(TransitionKind.SlideFade)]
        [InlineData(TransitionKind.Scale)]
        [InlineData(TransitionKind.CrossFade)]
        [InlineData(TransitionKind.Reflow)]
        public void UnderReduceMotion_EveryKindCollapsesToAnOpacityOnlyCrossFadeOfTheSameDuration(TransitionKind kind) {
            Motion.ReduceMotion = true;
            var duration = TimeSpan.FromMilliseconds(173);
            var delay = TimeSpan.FromMilliseconds(6);

            Transitions transitions = Motion.Transition(kind, duration, Motion.EntranceEasing, delay);

            var only = Assert.Single(transitions);
            var opacity = Assert.IsType<DoubleTransition>(only);
            Assert.Equal(Visual.OpacityProperty, opacity.Property);
            Assert.Equal(duration, opacity.Duration);
            Assert.Equal(delay, opacity.Delay);
            Assert.IsType<CubicEaseInOut>(opacity.Easing);
        }
    }
}
