using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman.Mac.Canvas;

namespace Foreman.Mac.UiTests.support {
    //Raises KeyDown/KeyUp straight on the canvas instead of routing through Avalonia.Headless's
    //process-wide keyboard device, whose ambient FocusedElement a parallel test collection can steal
    //mid-dispatch (see MainWindowShellTests.SKey_OnGraphCanvas_PansEvenWhenAmbientFocusIsStolen).
    public static class CanvasKeys {
        //Same rationale, generalized to any control rather than just the canvas.
        public static void KeyDownOn(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
            Dispatcher.UIThread.RunJobs();
        }

        public static void KeyDownOnCanvas(GraphCanvasControl canvas, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
            Dispatcher.UIThread.RunJobs();
        }

        public static void KeyUpOnCanvas(GraphCanvasControl canvas, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
            Dispatcher.UIThread.RunJobs();
        }

        public static void PressOnCanvas(GraphCanvasControl canvas, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            KeyDownOnCanvas(canvas, key, modifiers);
            KeyUpOnCanvas(canvas, key, modifiers);
        }
    }
}
