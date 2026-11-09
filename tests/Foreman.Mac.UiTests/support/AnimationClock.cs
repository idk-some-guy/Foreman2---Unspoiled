using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;

namespace Foreman.Mac.UiTests.support {
    //Mirrors Foreman.Mac.Kit.Tests.support.AnimationClock: Dispatcher.RunJobs never promotes
    //DispatcherTimers, so the render clock only keeps pulsing inside a real dispatcher frame.
    public static class AnimationClock {
        public static void Settle(int atLeastMilliseconds = 250) {
            Dispatcher.UIThread.RunJobs();
            var frame = new DispatcherFrame();
            DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(atLeastMilliseconds));
            Dispatcher.UIThread.PushFrame(frame);
            Dispatcher.UIThread.RunJobs();
        }

        //Pumps the render clock until root's kit height reflows finish; the timer only fails a hang, it never ends a pass.
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

        public static bool SheetPanelIsOpen(Visual inside) => IsOpen(SheetPanelOf(inside));

        public static void UntilSheetOpen(Visual inside) {
            Dispatcher.UIThread.RunJobs();
            Border panel = SheetPanelOf(inside);
            Until(panel, () => IsOpen(panel), "The sheet never finished opening.");
        }

        public static void UntilExpanded(Control strip) =>
            Until(strip, () => strip.Opacity >= 1 && double.IsNaN(strip.Height), "The strip never finished expanding.");

        public static void UntilHidden(Control strip) =>
            Until(strip, () => !strip.IsVisible, "The strip never finished collapsing.");

        public static void UntilFadedIn(Visual strip) =>
            Until(strip, () => strip.Opacity >= 1, "The state strip never finished fading in.");

        public static void UntilPopoverOpen(Visual anchor) {
            Dispatcher.UIThread.RunJobs();
            Border panel = ((Visual)TopLevel.GetTopLevel(anchor)!).GetVisualDescendants().OfType<PopoverFrame>()
                .First(f => f.Anchor == anchor).GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Panel");
            Until(panel, () => IsOpen(panel), "The popover never finished opening.");
        }

        //The timer only fails a hang, it never ends a pass.
        private static void Until(Visual watched, Func<bool> reached, string failure) {
            Dispatcher.UIThread.RunJobs();
            if (!reached()) {
                var frame = new DispatcherFrame();
                void Check(object? sender, AvaloniaPropertyChangedEventArgs e) {
                    if (reached())
                        frame.Continue = false;
                }
                watched.PropertyChanged += Check;
                try {
                    using IDisposable guard = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromSeconds(10));
                    Dispatcher.UIThread.PushFrame(frame);
                } finally {
                    watched.PropertyChanged -= Check;
                }
            }
            if (!reached())
                throw new TimeoutException(failure);
            Dispatcher.UIThread.RunJobs();
        }

        private static bool IsOpen(Border panel) =>
            panel.Opacity >= 1 && (panel.RenderTransform?.Value ?? Matrix.Identity) == Matrix.Identity;

        private static Border SheetPanelOf(Visual inside) =>
            inside.GetVisualAncestors().OfType<Border>().First(b => b.Name == "PART_Panel");
    }
}
