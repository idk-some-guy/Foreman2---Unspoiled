using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using System.Threading;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.support {
    public static class RealClick {
        //Past the platform's multi-click window, so the next press gets its own fresh ClickCount of 1.
        private const int SeparateClickGapMs = 700;

        public static void On(Control target) {
            AvaloniaWindow window = target.GetVisualAncestors().OfType<AvaloniaWindow>().First();
            Point center = new(target.Bounds.Width / 2, target.Bounds.Height / 2);
            Point screen = Avalonia.VisualExtensions.TranslatePoint(target, center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        //Back to back, same spot - raises a real DoubleTapped, ClickCount 1 then 2, like a fast double-click.
        public static void DoubleClick(Control target) {
            On(target);
            On(target);
        }

        //Unlike DoubleClick above, far enough apart that each press gets its own ClickCount of 1.
        public static void TwoSeparateClicks(Control target) {
            On(target);
            Thread.Sleep(SeparateClickGapMs);
            On(target);
        }
    }
}
