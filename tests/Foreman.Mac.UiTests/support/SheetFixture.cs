using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Views.Sheets;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.support {
    //Presents a sheet body on a real SheetHost so a test can read the kit frame's rendered geometry.
    internal static class SheetFixture {
        public static (Window Window, SheetHost Host, Task<T?> Result) Present<T>(SheetContent<T> sheet, double width = 900, double height = 700) {
            var window = new Window { Width = width, Height = height, Content = new Panel() };
            window.Show();
            SheetHost host = SheetHost.For(window);
            Task<T?> result = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();
            return (window, host, result);
        }

        public static double Token(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value));
            return Assert.IsType<double>(value);
        }

        public static SheetFrame FrameOf(SheetHost host, Control content) =>
            host.Children.OfType<SheetFrame>().Single(f => f.GetVisualDescendants().Contains(content));

        public static Border PanelOf(SheetHost host, Control content) =>
            FrameOf(host, content).GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

        public static CornerRadius Radius(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value));
            return Assert.IsType<CornerRadius>(value);
        }

        public static void AssertNoAlert(SheetHost host) => Assert.Empty(host.GetVisualDescendants().OfType<AlertSheet>());

        public static StatePanel StripOf(SheetHost host) => host.GetVisualDescendants().OfType<StatePanel>().Single();

        public static void Click(Window window, Control target) {
            AnimationClock.UntilReflowsSettled(window);
            window.UpdateLayout();
            Point point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
