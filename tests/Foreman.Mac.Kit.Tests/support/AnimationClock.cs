using System.Threading;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Foreman.Mac.Kit.Tests.support {
    //Avalonia's clock reports progress 0 on its first pulse, so an initial tick primes it before the sleep.
    public static class AnimationClock {
        public static void Settle(int atLeastMilliseconds = 250) {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(atLeastMilliseconds);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
