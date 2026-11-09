using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.UiTests.support;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class CanvasAxisLockModifierTests {
        private static Point CanvasCenter(MainWindow window) =>
            VisualExtensions.TranslatePoint(window.GraphCanvas, new Point(window.GraphCanvas.Bounds.Width / 2, window.GraphCanvas.Bounds.Height / 2), window)!.Value;

        [AvaloniaFact]
        public void PointerPress_WithNoShiftModifier_ClearsAStuckAxisLock() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.GraphCanvas.Focus();
            CanvasKeys.KeyDownOnCanvas(window.GraphCanvas, Key.LeftShift, KeyModifiers.Shift);
            Assert.True(window.GraphCanvas.Grid.LockDragToAxis);

            window.MouseDown(CanvasCenter(window), MouseButton.Left, RawInputModifiers.None);

            Assert.False(window.GraphCanvas.Grid.LockDragToAxis);
        }

        [AvaloniaFact]
        public void PointerMove_WithShiftModifier_SetsTheAxisLock() {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Point center = CanvasCenter(window);
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            Assert.False(window.GraphCanvas.Grid.LockDragToAxis);

            window.MouseMove(center, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);

            Assert.True(window.GraphCanvas.Grid.LockDragToAxis);
        }

        [AvaloniaFact]
        public void LosingFocusToAnotherControl_DoesNotClearAnActiveAxisLock() {
            var canvas = new GraphCanvasControl();
            canvas.Viewport.SetSize(400, 400);
            var other = new TextBox { Width = 50, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var window = new Window { Content = new Grid { Children = { canvas, other } }, Width = 400, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var center = new Point(200, 200);
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(center, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
            Assert.True(canvas.Grid.LockDragToAxis);

            other.Focus();

            Assert.True(canvas.Grid.LockDragToAxis);
        }
    }
}
