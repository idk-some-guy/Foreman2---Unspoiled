using System.Threading;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Foreman.Mac.UiTests.support {
    //Avalonia's Transitions animate using real elapsed time even under the headless render timer (a
    //forced tick reports Stopwatch.Elapsed since app start, not a fixed virtual frame step), so completing
    //one deterministically means sleeping past its duration first, then ticking once to sample the settled
    //value - a bare RunJobs() never advances it on its own.
    public static class AnimationClock {
        public static void Settle(int atLeastMilliseconds = 250) {
            Thread.Sleep(atLeastMilliseconds);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
