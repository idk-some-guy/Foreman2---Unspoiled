using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Controls;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.support {
    public class AnimationClockTests {
        private static double SpinOneWindow() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Spinner", null, out object? theme));
            var spinner = new Spinner { Theme = (ControlTheme)theme! };
            fixture.Window.Content = spinner;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Arc head = spinner.GetVisualDescendants().OfType<Arc>().Single(a => a.Name == "PART_Head");
            var group = Assert.IsType<TransformGroup>(head.RenderTransform);
            return Assert.Single(group.Children.OfType<RotateTransform>()).Angle;
        }

        [AvaloniaFact]
        public void Settle_AdvancesInfiniteAnimations_InEveryWindow_NotJustTheFirst() {
            double first = SpinOneWindow();
            double second = SpinOneWindow();

            Assert.NotEqual(0, first);
            Assert.NotEqual(0, second);
        }
    }
}
