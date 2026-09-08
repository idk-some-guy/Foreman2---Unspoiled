using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.support {
    public static class RealClick {
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

        //a press elsewhere resets MouseDevice's click count by position, no timing involved
        public static void TwoSeparateClicks(Control target) {
            On(target);
            On(NeutralSpotFarFrom(target));
            On(target);
        }

        private static Control NeutralSpotFarFrom(Control target) {
            AvaloniaWindow window = target.GetVisualAncestors().OfType<AvaloniaWindow>().First();
            return (Control?)window.FindControl<TextBox>("SearchBox") ?? window;
        }
    }
}
