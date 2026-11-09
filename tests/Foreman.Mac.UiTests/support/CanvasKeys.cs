using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman.Mac.Canvas;
using System;

namespace Foreman.Mac.UiTests.support {
    public static class CanvasKeys {
        public static void KeyDownOn(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
            Dispatcher.UIThread.RunJobs();
        }

        public static void TextInputOn(Control target, string text) {
            target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
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

        public static PhysicalKey PhysicalKeyFor(char letter) => Enum.Parse<PhysicalKey>(char.ToUpperInvariant(letter).ToString());
    }
}
