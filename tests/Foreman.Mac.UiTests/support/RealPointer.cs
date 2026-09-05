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
    }
}
