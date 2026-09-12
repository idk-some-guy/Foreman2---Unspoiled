using System;
using Avalonia.Threading;

namespace Foreman.Mac.Kit.Tests.support {
    //Dispatcher.RunJobs never promotes DispatcherTimers, so the render clock only keeps pulsing inside a real dispatcher frame.
    public static class AnimationClock {
        public static void Settle(int atLeastMilliseconds = 250) {
            var frame = new DispatcherFrame();
            DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(atLeastMilliseconds));
            Dispatcher.UIThread.PushFrame(frame);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
