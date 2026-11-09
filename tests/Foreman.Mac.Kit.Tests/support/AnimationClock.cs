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

        public static void UntilReflowsSettled(Avalonia.Visual root) {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Tasks.Task settled = Foreman.Mac.Kit.Motion.ReflowsSettled(root);
            if (!settled.IsCompleted) {
                var frame = new DispatcherFrame();
                settled.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), System.Threading.Tasks.TaskScheduler.Default);
                using IDisposable guard = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.PushFrame(frame);
            }
            if (!settled.IsCompleted)
                throw new TimeoutException("A kit height reflow never settled.");
            Dispatcher.UIThread.RunJobs();
        }
    }
}
