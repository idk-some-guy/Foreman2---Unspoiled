using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Views.Inspector;
using System.Linq;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Inspector {
    //CompactStepper in isolation (Task 9): the NumericUpDown replacement built to reach the mock's
    //22px/52-field/15x11-arrow shape (contract §03 CSS 439-450) without retemplating internals this port
    //doesn't control.
    public class CompactStepperTests {
        private static (AvaloniaWindow Window, CompactStepper Stepper) NewStepper() {
            var stepper = new CompactStepper { Minimum = 0, Maximum = 10, Increment = 1, FormatString = "N2" };
            var window = new AvaloniaWindow { Content = stepper, Width = 200, Height = 100 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return (window, stepper);
        }

        private static void Click(AvaloniaWindow window, Button button) {
            Avalonia.Point center = new(button.Bounds.Width / 2, button.Bounds.Height / 2);
            Avalonia.Point screen = Avalonia.VisualExtensions.TranslatePoint(button, center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void SettingValue_FormatsIntoText() {
            (_, CompactStepper stepper) = NewStepper();
            stepper.Value = 3;
            Assert.Equal("3.00", stepper.Text);
        }

        [AvaloniaFact]
        public void IncrementButton_AddsIncrementToValue() {
            (AvaloniaWindow window, CompactStepper stepper) = NewStepper();
            stepper.Value = 2;
            Button increment = stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "IncrementButton");

            Click(window, increment);

            Assert.Equal(3, stepper.Value);
        }

        [AvaloniaFact]
        public void DecrementButton_SubtractsIncrementFromValue() {
            (AvaloniaWindow window, CompactStepper stepper) = NewStepper();
            stepper.Value = 2;
            Button decrement = stepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DecrementButton");

            Click(window, decrement);

            Assert.Equal(1, stepper.Value);
        }

        [AvaloniaFact]
        public void ValueAboveMaximum_ClampsToMaximum() {
            (_, CompactStepper stepper) = NewStepper();
            stepper.Value = 999;
            Assert.Equal(10, stepper.Value);
        }

        [AvaloniaFact]
        public void ValueBelowMinimum_ClampsToMinimum() {
            (_, CompactStepper stepper) = NewStepper();
            stepper.Value = -5;
            Assert.Equal(0, stepper.Value);
        }

        [AvaloniaFact]
        public void ValueChanged_FiresOnValueAssignment() {
            (_, CompactStepper stepper) = NewStepper();
            double? observed = null;
            stepper.ValueChanged += (_, value) => observed = value;

            stepper.Value = 4;

            Assert.Equal(4, observed);
        }
    }
}
