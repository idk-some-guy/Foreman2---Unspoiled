using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    //Every test here moves real keyboard focus, which is process-wide in the headless runner.
    [Collection(KitScaleGroup.Name)]
    public class FocusRingTests {
        private static Button EnabledButton() {
            var button = new Button { Content = "Save", Width = 80, Height = 24 };
            FocusRing.SetIsEnabled(button, true);
            return button;
        }

        private static Rectangle? RingOn(Control control) => AdornerLayer.GetAdorner(control) as Rectangle;

        [AvaloniaFact]
        public void KeyboardFocus_ShowsARingAtTheTokenStrokeThicknessAndInflatedBounds() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            fixture.Window.Content = button;

            fixture.StateOf(button).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            Assert.Equal(3, ring.StrokeThickness);
            Assert.Equal(new Size(button.Bounds.Width + 7, button.Bounds.Height + 7), ring.Bounds.Size);
        }

        [AvaloniaFact]
        public void KeyboardFocus_RingIsNotClippedByTheAdornerLayer() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            fixture.Window.Content = button;

            fixture.StateOf(button).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            Assert.False(AdornerLayer.GetIsClipEnabled(ring));
            Assert.Equal(new Size(button.Bounds.Width + 7, button.Bounds.Height + 7), ring.Bounds.Size);
        }

        [AvaloniaFact]
        public void PointerFocus_ShowsNoRing() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            fixture.Window.Content = button;

            button.Focus(NavigationMethod.Pointer);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(RingOn(button));
        }

        [AvaloniaFact]
        public void IncreaseContrast_RingIsFourPointAtFullOpacity() {
            using var fixture = new KitFixture();
            fixture.Appearance.ContrastPreference = ColorContrastPreference.High;
            var hub = new AppearanceHub(Application.Current!, fixture.Appearance);
            try {
                Button button = EnabledButton();
                fixture.Window.Content = button;

                fixture.StateOf(button).Focus();

                Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
                Assert.Equal(4, ring.StrokeThickness);
                Color color = Assert.IsAssignableFrom<ISolidColorBrush>(ring.Stroke).Color;
                Assert.Equal(255, color.A);
            } finally {
                hub.Dispose();
            }
        }

        [AvaloniaFact]
        public void Blur_RemovesTheRing() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            var other = new Button { Content = "Other" };
            fixture.Window.Content = new StackPanel { Children = { button, other } };

            fixture.StateOf(button).Focus();
            Assert.NotNull(RingOn(button));

            other.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(RingOn(button));
        }

        [AvaloniaFact]
        public void DisablingTheBehaviour_RemovesTheRingAndStopsReactingToFocus() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            var other = new Button { Content = "Other" };
            fixture.Window.Content = new StackPanel { Children = { button, other } };

            fixture.StateOf(button).Focus();
            Assert.NotNull(RingOn(button));

            FocusRing.SetIsEnabled(button, false);
            Assert.Null(RingOn(button));

            other.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(button).Focus();

            Assert.Null(RingOn(button));
        }

        [AvaloniaFact]
        public void CornerRadiusOverride_SetsTheRingRadiusOverTheControlsOwnUnsetDefault() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            FocusRing.SetCornerRadius(button, new CornerRadius(6));
            fixture.Window.Content = button;

            fixture.StateOf(button).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            Assert.Equal(8, ring.RadiusX);
            Assert.Equal(8, ring.RadiusY);
        }

        [AvaloniaFact]
        public void NoCornerRadiusOverride_FallsBackToTheControlsOwnBorderCornerRadius() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            button.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            fixture.Window.Content = button;

            fixture.StateOf(button).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            Assert.Equal(6, ring.RadiusX);
            Assert.Equal(6, ring.RadiusY);
        }

        [AvaloniaFact]
        public void ShowAlways_RendersTheRingWithoutRealFocus() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            fixture.Window.Content = button;

            FocusRing.SetShowAlways(button, true);
            Dispatcher.UIThread.RunJobs();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(button)!;
            Assert.Contains(ring, layer.Children);
        }

        [AvaloniaFact]
        public void ShowAlways_TurnedOff_RemovesTheRingWhenNotReallyFocused() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            fixture.Window.Content = button;
            FocusRing.SetShowAlways(button, true);
            Dispatcher.UIThread.RunJobs();

            FocusRing.SetShowAlways(button, false);

            Assert.Null(RingOn(button));
        }

        [AvaloniaFact]
        public void ShowAlways_TurnedOff_KeepsTheRingWhileReallyKeyboardFocused() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            fixture.Window.Content = button;
            FocusRing.SetShowAlways(button, true);
            fixture.StateOf(button).Focus();

            FocusRing.SetShowAlways(button, false);

            Assert.NotNull(RingOn(button));
        }

        [AvaloniaFact]
        public void DetachWhileKeyboardFocused_RemovesTheRingAndLeavesNoSubscription() {
            using var fixture = new KitFixture();
            var panel = new StackPanel();
            fixture.Window.Content = panel;
            Button button = EnabledButton();
            panel.Children.Add(button);
            fixture.StateOf(button).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(button)!;

            panel.Children.Remove(button);
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(ring, layer.Children);
            Assert.Null(AdornerLayer.GetAdorner(button));
        }

        [AvaloniaTheory]
        [InlineData(15, 15)]
        [InlineData(30, 18)]
        public void KeyboardFocus_RingBoundsInflateBySevenOverTheControlsOwnSize(double width, double height) {
            using var fixture = new KitFixture();
            var control = new Button { Content = "x", Width = width, Height = height };
            FocusRing.SetIsEnabled(control, true);
            fixture.Window.Content = control;

            fixture.StateOf(control).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(control));
            Assert.Equal(control.Bounds.Width + 7, ring.Bounds.Width);
            Assert.Equal(control.Bounds.Height + 7, ring.Bounds.Height);
        }

        [AvaloniaFact]
        public void PillCornerRadius_ClampsToTheDrawnHalfExtentAfterStrokeDeflation() {
            using var fixture = new KitFixture();
            var control = new Button { Content = "x", Width = 30, Height = 18 };
            FocusRing.SetIsEnabled(control, true);
            FocusRing.SetCornerRadius(control, new CornerRadius(100));
            fixture.Window.Content = control;

            fixture.StateOf(control).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(control));
            Assert.Equal(11, ring.RadiusX);
            Assert.Equal(11, ring.RadiusY);
        }

        [AvaloniaFact]
        public void PillCornerRadius_RenderedGeometryFormsATrueCapsuleWithNoOverlappingArcs() {
            using var fixture = new KitFixture();
            var control = new Button { Content = "x", Width = 30, Height = 18 };
            FocusRing.SetIsEnabled(control, true);
            FocusRing.SetCornerRadius(control, new CornerRadius(100));
            fixture.Window.Content = control;

            fixture.StateOf(control).Focus();
            Dispatcher.UIThread.RunJobs();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(control));
            Rect rendered = ring.RenderedGeometry!.Bounds;
            Assert.True(rendered.Width <= ring.Bounds.Width + 0.01, $"rendered width {rendered.Width} > ring width {ring.Bounds.Width}");
            Assert.True(rendered.Height <= ring.Bounds.Height + 0.01, $"rendered height {rendered.Height} > ring height {ring.Bounds.Height}");
            Assert.Equal(control.Bounds.Height + 4, rendered.Height, 2);
        }

        [AvaloniaFact]
        public void ShowAlways_SetBeforeParenting_RingOffsetResolvesOnceAttached() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            FocusRing.SetShowAlways(button, true);

            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            Assert.Equal(new Thickness(-3.5), ring.Margin);
            Assert.Equal(5, ring.RadiusX);
        }

        [AvaloniaFact]
        public void OffsetResourceChange_AfterAttach_UpdatesMarginAndRadiusLive() {
            using var fixture = new KitFixture();
            Button button = EnabledButton();
            fixture.Window.Content = button;
            fixture.StateOf(button).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            Assert.Equal(new Thickness(-3.5), ring.Margin);
            double radiusBeforeChange = ring.RadiusX;

            try {
                Application.Current!.Resources["Foreman.FocusRing.Offset"] = 5.0;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(new Thickness(-6.5), ring.Margin);
                Assert.Equal(radiusBeforeChange + 3, ring.RadiusX);
            } finally {
                Application.Current!.Resources.Remove("Foreman.FocusRing.Offset");
            }
        }

        [AvaloniaFact]
        public void DetachAndReattach_TheShowAlwaysRingDoesNotComeBackFrozen() {
            using var fixture = new KitFixture();
            var panel = new StackPanel();
            fixture.Window.Content = panel;
            Button button = EnabledButton();
            FocusRing.SetShowAlways(button, true);
            panel.Children.Add(button);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(RingOn(button));

            panel.Children.Remove(button);
            Dispatcher.UIThread.RunJobs();
            panel.Children.Add(button);
            Dispatcher.UIThread.RunJobs();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(button));
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(button)!;
            Assert.Contains(ring, layer.Children);
            Assert.Equal(new Thickness(-3.5), ring.Margin);

            try {
                Application.Current!.Resources["Foreman.FocusRing.Offset"] = 5.0;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(new Thickness(-6.5), ring.Margin);
            } finally {
                Application.Current!.Resources.Remove("Foreman.FocusRing.Offset");
            }
        }

        [AvaloniaFact]
        public void ShowAlwaysWithoutIsEnabled_DetachRemovesTheRingAndReattachRestoresIt() {
            using var fixture = new KitFixture();
            var panel = new StackPanel();
            fixture.Window.Content = panel;
            var button = new Button { Content = "Save", Width = 80, Height = 24 };
            FocusRing.SetShowAlways(button, true);
            panel.Children.Add(button);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(RingOn(button));

            panel.Children.Remove(button);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(RingOn(button));
            Assert.Null(AdornerLayer.GetAdorner(button));

            panel.Children.Add(button);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(RingOn(button));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class FocusRingScaleTests {
        [AvaloniaFact]
        public void KeyboardFocus_AtOnePointFiveScale_StrokeThicknessAndBoundsStayFixed() {
            using var fixture = new KitFixture();
            var button = new Button { Content = "Save", Width = 80, Height = 24 };
            FocusRing.SetIsEnabled(button, true);
            fixture.Window.Content = button;

            ScaleContract.Apply(1.5);
            fixture.StateOf(button).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(button));
            Assert.Equal(3, ring.StrokeThickness);
            Assert.Equal(new Size(button.Bounds.Width + 7, button.Bounds.Height + 7), ring.Bounds.Size);
        }

        [AvaloniaTheory]
        [InlineData(15, 15)]
        [InlineData(30, 18)]
        public void KeyboardFocus_AtOnePointFiveScale_RingBoundsInflateBySevenOverTheControlsOwnSize(double width, double height) {
            using var fixture = new KitFixture();
            var control = new Button { Content = "x", Width = width, Height = height };
            FocusRing.SetIsEnabled(control, true);
            fixture.Window.Content = control;

            ScaleContract.Apply(1.5);
            fixture.StateOf(control).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(control));
            Assert.Equal(control.Bounds.Width + 7, ring.Bounds.Width);
            Assert.Equal(control.Bounds.Height + 7, ring.Bounds.Height);
        }
    }
}
