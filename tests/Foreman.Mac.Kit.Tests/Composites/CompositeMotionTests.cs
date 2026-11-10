using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public class CompositeMotionTests {
        [AvaloniaFact]
        public void FadeOutAndRemove_ClosingTheWindowMidFade_DetachesWithoutThrowing_AndDropsEveryWrap() {
            var track = new StackPanel();
            var window = new Window { Content = track };
            window.Show();
            var wraps = new Panel[4];
            for (int i = 0; i < wraps.Length; i++) {
                var cell = new Border { Width = 20, Height = 20 };
                wraps[i] = new WrapPanel();
                track.Children.Add(cell);
                CompositeMotion.FadeOutAndRemove(cell, wraps[i], track);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(wraps.Length, track.Children.Count);

            window.Close();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Empty(track.Children);
        }
    }
}
