using System.Threading;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Foreman.Mac.Kit.Tests.support {
    //Mirrors Foreman.Mac.UiTests.support.AnimationClock: sleep past the transition, then force a render tick.
    public static class AnimationClock {
        public static void Settle(int atLeastMilliseconds = 250) {
            Thread.Sleep(atLeastMilliseconds);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
