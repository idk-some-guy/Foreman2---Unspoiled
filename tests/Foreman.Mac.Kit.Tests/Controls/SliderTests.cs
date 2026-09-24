using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Provider;
using Avalonia.Automation.Peers;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;
using Slider = Foreman.Mac.Kit.Controls.Slider;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class SliderTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitSliderTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Slider", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Slider Themed() => new() { Theme = KitSliderTheme(), Width = 124 };

        private static Border Track(Slider slider) =>
            slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Track");

        private static Border Thumb(Slider slider) =>
            slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Thumb");

        private static Border Fill(Slider slider) =>
            slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");

        private static Canvas Ticks(Slider slider) =>
            slider.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "PART_Ticks");

        [AvaloniaFact]
        public void KitSlider_IsAControlThemeTargetingSlider() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Slider), KitSliderTheme().TargetType);
        }

        [AvaloniaFact]
        public void Track_HeightAndRadiusComeFromTokens() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.SliderTrack", Application.Current.ActualThemeVariant, out object? trackHeight);
            Application.Current!.TryGetResource("Foreman.Radius.Pill", Application.Current.ActualThemeVariant, out object? radius);

            Assert.Equal(Assert.IsType<double>(trackHeight), Track(slider).Height);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Track(slider).CornerRadius);
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), Track(slider).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Fill(slider).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void ThumbDiameter_GrowsAcrossNormalHoverPressed() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(14, Thumb(slider).Width);

            fixture.StateOf(slider).Hover();
            AnimationClock.Settle();
            Assert.Equal(15, Thumb(slider).Width);

            fixture.StateOf(slider).Press();
            AnimationClock.Settle();
            Assert.Equal(16, Thumb(slider).Width);
        }

        [AvaloniaFact]
        public void Thumb_AtBothTrackExtremes_OverhangIsNotClippedByAnAncestor() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            slider.Value = 0;
            Dispatcher.UIThread.RunJobs();
            AssertThumbOverhangIsUnclipped(slider);

            slider.Value = 100;
            Dispatcher.UIThread.RunJobs();
            AssertThumbOverhangIsUnclipped(slider);
        }

        private static void AssertThumbOverhangIsUnclipped(Slider slider) {
            Border thumb = Thumb(slider);
            for (Visual? ancestor = slider; ancestor is not null; ancestor = ancestor.GetVisualParent()) {
                if (!ancestor.ClipToBounds)
                    continue;
                Point topLeft = thumb.TranslatePoint(new Point(0, 0), ancestor) ?? default;
                Point bottomRight = thumb.TranslatePoint(new Point(thumb.Bounds.Width, thumb.Bounds.Height), ancestor) ?? default;
                bool clipped = topLeft.X < 0 || topLeft.Y < 0 || bottomRight.X > ancestor.Bounds.Width || bottomRight.Y > ancestor.Bounds.Height;
                Assert.False(clipped, $"{ancestor.GetType().Name} (ClipToBounds=true) clips the thumb overhang.");
            }
        }

        [AvaloniaFact]
        public void FocusAdorner_AttachesToTheThumbNotTheTrack() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(Thumb(slider)).Focus();

            Assert.Null(AdornerLayer.GetAdorner(Track(slider)));
            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(Thumb(slider)));

            Application.Current!.TryGetResource("Foreman.FocusRing.Width", Application.Current.ActualThemeVariant, out object? width);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            Assert.Equal(Assert.IsType<double>(width), ring.StrokeThickness);
            Assert.Equal(-((double)offset! + (double)width! / 2), ring.Margin.Left);
        }

        [AvaloniaFact]
        public void Ticks_OnlyExistWhenDiscreteWithElevenOrFewerSteps() {
            using var fixture = new KitFixture();
            Slider continuous = Themed();
            fixture.Window.Content = continuous;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Ticks(continuous).Children);

            Slider discrete = Themed();
            discrete.Minimum = 0;
            discrete.Maximum = 100;
            discrete.Step = 25;
            fixture.Window.Content = discrete;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, Ticks(discrete).Children.Count);

            Slider tooManySteps = Themed();
            tooManySteps.Minimum = 0;
            tooManySteps.Maximum = 100;
            tooManySteps.Step = 1;
            fixture.Window.Content = tooManySteps;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Ticks(tooManySteps).Children);
        }

        [AvaloniaFact]
        public void RetemplateTwice_ThenOneLayoutPass_RebuildsTicksOnlyOnce() {
            using var fixture = new KitFixture();
            ControlTheme theme = KitSliderTheme();
            Slider slider = new() { Theme = theme, Width = 124, Minimum = 0, Maximum = 100, Step = 25 };
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            slider.Theme = null;
            Dispatcher.UIThread.RunJobs();
            slider.Theme = theme;
            Dispatcher.UIThread.RunJobs();

            int baseline = slider.TickRebuilds;

            slider.Width += 40;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(baseline + 1, slider.TickRebuilds);
        }

        [AvaloniaFact]
        public void ArrowKeys_StepOnePercentContinuous() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Value = 50;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(Thumb(slider)).Focus();

            fixture.StateOf(Thumb(slider)).KeyDown(Key.Right);
            Assert.Equal(51, slider.Value);

            fixture.StateOf(Thumb(slider)).KeyDown(Key.Left, KeyModifiers.Shift);
            Assert.Equal(41, slider.Value);
        }

        [AvaloniaFact]
        public void ArrowKeys_StepOneDiscreteStep() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Step = 25;
            slider.Value = 50;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(Thumb(slider)).Focus();

            fixture.StateOf(Thumb(slider)).KeyDown(Key.Right);
            Assert.Equal(75, slider.Value);
        }

        [AvaloniaFact]
        public void AltArrow_StepsByOneTenthOfAPercent() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Value = 50;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(Thumb(slider)).Focus();

            fixture.StateOf(Thumb(slider)).KeyDown(Key.Right, KeyModifiers.Alt);
            Assert.Equal(50.1, slider.Value, 3);
        }

        [AvaloniaFact]
        public void HomeAndEnd_ReachMinimumAndMaximum() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 10;
            slider.Maximum = 90;
            slider.Value = 50;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(Thumb(slider)).Focus();

            fixture.StateOf(Thumb(slider)).KeyDown(Key.Home);
            Assert.Equal(10, slider.Value);

            fixture.StateOf(Thumb(slider)).KeyDown(Key.End);
            Assert.Equal(90, slider.Value);
        }

        [AvaloniaFact]
        public void PageUpDown_StepsByTwentyFivePercent() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Value = 50;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(Thumb(slider)).Focus();

            fixture.StateOf(Thumb(slider)).KeyDown(Key.PageUp);
            Assert.Equal(75, slider.Value);

            fixture.StateOf(Thumb(slider)).KeyDown(Key.PageDown);
            fixture.StateOf(Thumb(slider)).KeyDown(Key.PageDown);
            Assert.Equal(25, slider.Value);
        }

        //A host that rules its own page step (the sheets' 10 % opacity) sets PageStep; unset keeps the quarter-range default.
        [AvaloniaFact]
        public void PageStep_WhenSet_ReplacesTheQuarterRangePage() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Step = 1;
            slider.PageStep = 10;
            slider.Value = 50;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(Thumb(slider)).Focus();

            fixture.StateOf(Thumb(slider)).KeyDown(Key.PageUp);
            Assert.Equal(60, slider.Value);
            fixture.StateOf(Thumb(slider)).KeyDown(Key.PageDown);
            fixture.StateOf(Thumb(slider)).KeyDown(Key.PageDown);
            Assert.Equal(40, slider.Value);
            fixture.StateOf(Thumb(slider)).KeyDown(Key.Right);
            Assert.Equal(41, slider.Value);
        }

        [AvaloniaFact]
        public void Escape_DuringADrag_CancelsBackToTheStartingValue() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Value = 50;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(slider).Press();
            slider.Value = 90;
            fixture.StateOf(Thumb(slider)).KeyDown(Key.Escape);

            Assert.Equal(50, slider.Value);
        }

        [AvaloniaFact]
        public void Escape_WhenNotDragging_LeavesTheEventUnhandled() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(Thumb(slider)).Focus();

            var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            Thumb(slider).RaiseEvent(args);

            Assert.False(args.Handled);
        }

        [AvaloniaFact]
        public void SetValueDirectly_StillClampsViaCoercion() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            slider.SetValue(Slider.ValueProperty, 500);

            Assert.Equal(100, slider.Value);
        }

        [AvaloniaFact]
        public void ReduceMotion_ThumbHasOnlyAnOpacityTransition() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            Slider slider = Themed();
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            var transition = Assert.Single(Thumb(slider).Transitions!);
            var doubleTransition = Assert.IsType<DoubleTransition>(transition);
            Assert.Equal(Visual.OpacityProperty, doubleTransition.Property);
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            fixture.Window.Content = slider;

            slider.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), slider.Opacity);
        }

        [AvaloniaFact]
        public void ShowValue_DisplaysTheRoundedValueWithUnit() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Value = 72;
            slider.Unit = "%";
            slider.ShowValue = true;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            var label = slider.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ValueLabel");
            Assert.True(label.IsVisible);
            Assert.Equal("72\u202F%", label.Text);
        }

        [AvaloniaFact]
        public void ValueLabel_WidthStaysFixedAcrossDigitCounts() {
            using var fixture = new KitFixture();
            Slider narrow = Themed();
            narrow.Minimum = 0;
            narrow.Maximum = 100;
            narrow.Value = 5;
            narrow.ShowValue = true;
            fixture.Window.Content = narrow;
            Dispatcher.UIThread.RunJobs();
            var narrowLabel = narrow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ValueLabel");
            double narrowWidth = narrowLabel.Bounds.Width;

            Slider wide = Themed();
            wide.Minimum = 0;
            wide.Maximum = 100;
            wide.Value = 100;
            wide.ShowValue = true;
            fixture.Window.Content = wide;
            Dispatcher.UIThread.RunJobs();
            var wideLabel = wide.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ValueLabel");

            Assert.Equal(narrowWidth, wideLabel.Bounds.Width);
        }

        [AvaloniaFact]
        public void AutomationPeer_IsASliderWithRangeValue_AndAValueDescriptionCarryingTheUnit() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Step = 1;
            slider.PageStep = 10;
            slider.Unit = "%";
            slider.Value = 40;
            Avalonia.Automation.AutomationProperties.SetName(slider, "Opacity");
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(slider);
            Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
            Assert.Equal("Opacity", peer.GetName());
            IRangeValueProvider range = peer.GetProvider<IRangeValueProvider>()!;
            Assert.Equal((40d, 0d, 100d, 1d, 10d), (range.Value, range.Minimum, range.Maximum, range.SmallChange, range.LargeChange));
            Assert.False(range.IsReadOnly);
            Assert.Equal("40 percent", peer.GetProvider<IValueProvider>()!.Value);

            range.SetValue(55);
            Assert.Equal(55, slider.Value);
            Assert.Equal("55 percent", peer.GetProvider<IValueProvider>()!.Value);
        }

        [AvaloniaFact]
        public void AutomationPeer_ContinuousSmallChangeIsOnePercent_AndASpokenUnitReplacesTheSymbol() {
            using var fixture = new KitFixture();
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 200;
            slider.Unit = "px";
            slider.SpokenUnit = "pixels";
            slider.Value = 3;
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(slider);
            Assert.Equal(2, peer.GetProvider<IRangeValueProvider>()!.SmallChange);
            Assert.Equal(50, peer.GetProvider<IRangeValueProvider>()!.LargeChange);
            Assert.Equal("3 pixels", peer.GetProvider<IValueProvider>()!.Value);

            slider.Unit = null;
            slider.SpokenUnit = null;
            Assert.Equal("3", peer.GetProvider<IValueProvider>()!.Value);
        }

        private static Slider Opacity() {
            Slider slider = Themed();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.Step = 1;
            slider.Unit = "%";
            slider.Value = 40;
            Avalonia.Automation.AutomationProperties.SetName(slider, "Opacity");
            return slider;
        }

        [AvaloniaFact]
        public void AccessibleFocus_AfterTab_IsTheSlider_WhileTheThumbKeepsKeyboardFocus() {
            using var fixture = new KitFixture();
            Slider slider = Opacity();

            AutomationPeer? focus = AccessibleFocus.TabOnto(fixture.Window, slider);

            Assert.True(Thumb(slider).IsFocused);
            Assert.NotNull(focus);
            Assert.Equal((AutomationControlType.Slider, "Opacity"), (focus.GetAutomationControlType(), focus.GetName()));
            Assert.Equal(40, focus.GetProvider<IRangeValueProvider>()!.Value);
            Assert.Equal("40 percent", focus.GetProvider<IValueProvider>()!.Value);
            Assert.True(focus.HasKeyboardFocus());
        }

        [AvaloniaFact]
        public void AutomationPeer_RightArrow_RaisesOneRangeValueChange() {
            using var fixture = new KitFixture();
            Slider slider = Opacity();
            AutomationPeer focus = AccessibleFocus.TabOnto(fixture.Window, slider)!;
            var events = AccessibleFocus.Record(focus);

            fixture.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(41, slider.Value);
            AutomationPropertyChangedEventArgs change = Assert.Single(events, e => e.Property == RangeValuePatternIdentifiers.ValueProperty);
            Assert.Equal((40d, 41d), ((double)change.OldValue!, (double)change.NewValue!));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class SliderScaleTests {
        [AvaloniaFact]
        public void MetricsAndThumb_TrackScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Slider", null, out object? theme));
            var slider = new Slider { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(26, slider.Height);
            Assert.Equal(80, slider.MinWidth);
            Assert.Equal(124, slider.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(39, slider.Height);
            Assert.Equal(120, slider.MinWidth);
            Assert.Equal(186, slider.Width);
        }

        [AvaloniaFact]
        public void ThumbAndTicks_TrackScaleContractAtCodeResolvedValues() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Slider", null, out object? theme));
            var slider = new Slider { Theme = Assert.IsType<ControlTheme>(theme), Width = 124, Minimum = 0, Maximum = 100, Step = 25 };
            fixture.Window.Content = slider;
            Dispatcher.UIThread.RunJobs();

            Border thumb = slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Thumb");
            Canvas ticks = slider.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "PART_Ticks");
            Assert.Equal(14, thumb.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(21, thumb.Width);

            var mark = Assert.IsType<Rectangle>(ticks.Children[0]);
            Assert.Equal(6, mark.Height);
            Assert.Equal(ticks.Bounds.Height / 2 + 7.5, Canvas.GetTop(mark));
        }
    }
}
