using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitSplitDividerTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitSplitDividerTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.SplitDivider", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static SplitDivider Themed() => new() { Theme = KitSplitDividerTheme() };

        private static Rectangle Grip(SplitDivider divider) =>
            divider.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "PART_Grip");

        private static Rectangle Hairline(SplitDivider divider) =>
            divider.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "PART_Hairline");

        [AvaloniaFact]
        public void KitSplitDivider_IsAControlThemeTargetingSplitDivider() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(SplitDivider), KitSplitDividerTheme().TargetType);
        }

        [AvaloniaFact]
        public void HitWidth_IsFivePoints() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(5, divider.Bounds.Width);
        }

        [AvaloniaFact]
        public void Hairline_IsOnePointWideCenteredTwoPointsIntoTheHitArea() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            Rectangle hairline = Hairline(divider);
            Assert.Equal(1, hairline.Width);
            double leftEdge = hairline.TranslatePoint(new Point(0, 0), divider)!.Value.X;
            Assert.Equal(2, leftEdge, 2);
        }

        [AvaloniaFact]
        public void Grip_Is1x28_OpacityZeroAtRest() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            Rectangle grip = Grip(divider);
            Assert.Equal(1, grip.Width);
            Assert.Equal(28, grip.Height);
            Assert.Equal(0, grip.Opacity);
        }

        [AvaloniaFact]
        public void Grip_OpacityOneOnPointerOver() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Hover();
            AnimationClock.Settle((int)Motion.HoverDuration.TotalMilliseconds + 100);
            Assert.Equal(1, Grip(divider).Opacity);
        }

        [AvaloniaFact]
        public void Dragging_TurnsHairlineAndGripAccent() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Press();
            AnimationClock.Settle((int)Motion.HoverDuration.TotalMilliseconds + 100);

            Color accent = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.Accent")).Color;
            Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(Grip(divider).Fill).Color);
            Assert.Equal(1, Grip(divider).Opacity);
            Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(Hairline(divider).Fill).Color);

            fixture.StateOf(divider).Release();
        }

        [AvaloniaFact]
        public void IncreaseContrast_GripVisibleAtRestAtFullOpacity() {
            using var fixture = new KitFixture();
            fixture.Appearance.ContrastPreference = ColorContrastPreference.High;
            var hub = new AppearanceHub(Application.Current!, fixture.Appearance);
            try {
                SplitDivider divider = Themed();
                fixture.Window.Content = divider;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, Grip(divider).Opacity);
            } finally {
                hub.Dispose();
            }
        }

        [AvaloniaFact]
        public void SettingValueBelowMinimum_ClampsToMinimum() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            divider.Value = 100;
            Assert.Equal(260, divider.Value);
        }

        [AvaloniaFact]
        public void SettingValueAboveMaximum_ClampsToMaximum() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            divider.Value = 500;
            Assert.Equal(380, divider.Value);
        }

        [AvaloniaFact]
        public void Drag_TracksThePointerImmediatelyWithNoAnimation() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Drag(new Point(2, 100), new Point(42, 100));

            Assert.Equal(340, divider.Value);
        }

        [AvaloniaFact]
        public void Drag_WhenTheHostColumnTracksValue_StillTracksThePointerAcrossMultipleMoves() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            var grid = new Grid {
                ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition(1, GridUnitType.Star) },
            };
            grid.ColumnDefinitions[0].Bind(ColumnDefinition.WidthProperty, new Binding(nameof(SplitDivider.Value)) {
                Source = divider,
                Converter = new FuncValueConverter<double, GridLength>(v => new GridLength(v)),
            });
            Grid.SetColumn(divider, 1);
            grid.Children.Add(divider);
            fixture.Window.Content = grid;
            Dispatcher.UIThread.RunJobs();

            double startValue = divider.Value;
            Point pressPoint = divider.TranslatePoint(new Point(2, divider.Bounds.Height / 2), fixture.Window)!.Value;

            fixture.Window.MouseDown(pressPoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Point current = pressPoint;
            for (int step = 0; step < 4; step++) {
                current = new Point(current.X + 10, current.Y);
                fixture.Window.MouseMove(current, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
            }

            fixture.Window.MouseUp(current, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(startValue + 40, divider.Value, 1);
        }

        [AvaloniaFact]
        public void EscapeDuringDrag_RestoresPreDragWidth() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(300, divider.Value);

            fixture.StateOf(divider).Press();
            Point moved = Avalonia.VisualExtensions.TranslatePoint(divider, new Point(60, divider.Bounds.Height / 2), fixture.Window) ?? default;
            fixture.Window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(300, divider.Value);

            fixture.StateOf(divider).KeyDown(Key.Escape);
            Assert.Equal(300, divider.Value);
        }

        [AvaloniaFact]
        public void DoubleClick_ResetsValueToThreeHundred() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            divider.Value = 340;
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Click();
            fixture.StateOf(divider).Click();
            AnimationClock.Settle(300);

            Assert.Equal(300, divider.Value);
        }

        [AvaloniaFact]
        public void DoubleClickThenImmediateDrag_DraggedValueSurvivesTheInFlightReset() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            divider.Value = 340;
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Click();
            fixture.StateOf(divider).Click();
            fixture.StateOf(divider).Drag(new Point(2, 100), new Point(22, 100));
            AnimationClock.Settle(300);

            Assert.NotEqual(300, divider.Value);
        }

        [AvaloniaFact]
        public void ReduceMotion_DoubleClickReset_IsInstant() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            SplitDivider divider = Themed();
            divider.Value = 340;
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Click();
            fixture.StateOf(divider).Click();

            Assert.Equal(300, divider.Value);
        }

        [AvaloniaFact]
        public void ArrowKeys_StepByEight_ShiftArrowStepsByTwentyFour() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Focus();
            fixture.StateOf(divider).KeyDown(Key.Right);
            Assert.Equal(308, divider.Value);

            fixture.StateOf(divider).KeyDown(Key.Left);
            Assert.Equal(300, divider.Value);

            fixture.StateOf(divider).KeyDown(Key.Right, KeyModifiers.Shift);
            Assert.Equal(324, divider.Value);

            fixture.StateOf(divider).KeyDown(Key.Left, KeyModifiers.Shift);
            Assert.Equal(300, divider.Value);
        }

        [AvaloniaFact]
        public void HomeEnd_JumpToMinimumAndMaximum() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Focus();
            fixture.StateOf(divider).KeyDown(Key.Home);
            Assert.Equal(260, divider.Value);

            fixture.StateOf(divider).KeyDown(Key.End);
            Assert.Equal(380, divider.Value);
        }

        [AvaloniaFact]
        public void Enter_ResetsValueToThreeHundred() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            divider.Value = 260;
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Focus();
            fixture.StateOf(divider).KeyDown(Key.Enter);
            AnimationClock.Settle(300);

            Assert.Equal(300, divider.Value);
        }

        [AvaloniaFact]
        public void ValueChanged_RaisesOnEveryResolvedChange() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            double? last = null;
            divider.ValueChanged += (_, v) => last = v;

            fixture.StateOf(divider).Focus();
            fixture.StateOf(divider).KeyDown(Key.Right);

            Assert.Equal(308, last);
        }

        [AvaloniaFact]
        public void Anchor_DefaultsToLeading() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            Assert.Equal(SplitDividerAnchor.Leading, divider.Anchor);
        }

        [AvaloniaFact]
        public void TrailingAnchor_DragTowardTheFarEdge_GrowsValue() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            divider.Anchor = SplitDividerAnchor.Trailing;
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Drag(new Point(2, 100), new Point(-38, 100));

            Assert.Equal(340, divider.Value);
        }

        [AvaloniaFact]
        public void TrailingAnchor_DragTowardTheWindowEdge_ShrinksValue() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            divider.Anchor = SplitDividerAnchor.Trailing;
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(divider).Drag(new Point(2, 100), new Point(42, 100));

            Assert.Equal(260, divider.Value);
        }

        [AvaloniaFact]
        public void ValueCommitted_FiresOnceAfterADrag_NotPerPointerMove() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            int commits = 0;
            divider.ValueCommitted += (_, _) => commits++;

            Point pressPoint = divider.TranslatePoint(new Point(2, divider.Bounds.Height / 2), fixture.Window)!.Value;
            fixture.Window.MouseDown(pressPoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Point current = pressPoint;
            for (int step = 0; step < 4; step++) {
                current = new Point(current.X + 10, current.Y);
                fixture.Window.MouseMove(current, RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(0, commits);

            fixture.Window.MouseUp(current, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, commits);
        }

        [AvaloniaFact]
        public void ValueCommitted_FiresOnEachKeyboardStep() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            int commits = 0;
            divider.ValueCommitted += (_, _) => commits++;

            fixture.StateOf(divider).Focus();
            fixture.StateOf(divider).KeyDown(Key.Right);

            Assert.Equal(1, commits);
        }

        [AvaloniaFact]
        public void AutomationPeer_RoleIsSplitterWithMinMaxValuePopulated() {
            using var fixture = new KitFixture();
            SplitDivider divider = Themed();
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(divider);
            Assert.Equal("Splitter", peer.GetClassName());
            Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());

            var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
            Assert.Equal(260, range.Minimum);
            Assert.Equal(380, range.Maximum);
            Assert.Equal(300, range.Value);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class KitSplitDividerScaleTests {
        [AvaloniaFact]
        public void HitWidth_StaysFiveAtOneAndAHalfTimesScale() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.SplitDivider", null, out object? theme));
            var divider = new SplitDivider { Theme = (ControlTheme)theme! };
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(5, divider.Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(5, divider.Bounds.Width);

            ScaleContract.Apply(1.0);
        }

        //The cascade sets Minimum and Maximum through two separate resource notifications; CoerceValue must tolerate the transient crossing either way.
        [AvaloniaFact]
        public void MinimumAndMaximum_TrackTheTokensAcrossAScaleCrossing_AndNeverThrow() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.SplitDivider", null, out object? theme));
            var divider = new SplitDivider { Theme = (ControlTheme)theme! };
            fixture.Window.Content = divider;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(260, divider.Minimum);
            Assert.Equal(380, divider.Maximum);
            Assert.Equal(300, divider.Value);

            try {
                ScaleContract.Apply(1.5);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(390, divider.Minimum);
                Assert.Equal(570, divider.Maximum);
                Assert.InRange(divider.Value, divider.Minimum, divider.Maximum);

                ScaleContract.Apply(1.0);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(260, divider.Minimum);
                Assert.Equal(380, divider.Maximum);
                Assert.InRange(divider.Value, divider.Minimum, divider.Maximum);

                ScaleContract.Apply(2.0);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(520, divider.Minimum);
                Assert.Equal(760, divider.Maximum);
                Assert.InRange(divider.Value, divider.Minimum, divider.Maximum);
            } finally {
                ScaleContract.Apply(1.0);
            }
        }

        [AvaloniaFact]
        public void CoerceValue_ToleratesATransientMinimumAboveMaximum() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.SplitDivider", null, out object? theme));
            var divider = new SplitDivider { Theme = (ControlTheme)theme!, Maximum = 380 };

            divider.Minimum = 390;

            Assert.Equal(390, divider.Value);
        }
    }
}
