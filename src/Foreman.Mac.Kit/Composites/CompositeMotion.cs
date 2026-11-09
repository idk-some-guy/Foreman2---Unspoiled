using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Kit.Composites {
    internal static class CompositeMotion {
        private static readonly ConditionalWeakTable<Control, StrongBox<int>> reflowsInFlight = [];
        private static event Action? ReflowEnded;

        internal static Task ReflowsSettled(Visual root) {
            if (!IsReflowing(root))
                return Task.CompletedTask;
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Check() {
                if (IsReflowing(root))
                    return;
                ReflowEnded -= Check;
                settled.TrySetResult();
            }
            ReflowEnded += Check;
            return settled.Task;
        }

        private static bool IsReflowing(Visual root) =>
            root.GetSelfAndVisualDescendants().OfType<Control>()
                .Any(c => reflowsInFlight.TryGetValue(c, out StrongBox<int>? count) && count.Value > 0);

        internal static void ClearExplicitHeight(Control host, ref CancellationTokenSource? reflowCancellation) {
            reflowCancellation?.Cancel();
            reflowCancellation = null;
            Transitions? transitions = host.Transitions;
            host.Transitions = null;
            host.Height = double.NaN;
            host.Transitions = transitions;
        }

        internal static void ReflowHeight(Control host, Func<double> newHeightAfterMeasure, Action onSettled, ref CancellationTokenSource? reflowCancellation) {
            if (Motion.ReduceMotion || TopLevel.GetTopLevel(host) is null)
                return;
            double oldHeight = host.Bounds.Height;
            if (oldHeight <= 0)
                return;

            Transitions? transitions = host.Transitions;
            host.Transitions = null;
            host.Height = oldHeight;
            host.Transitions = transitions;

            reflowCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            reflowCancellation = cancellation;
            reflowsInFlight.GetOrCreateValue(host).Value++;
            Dispatcher.UIThread.Post(() => _ = RunReflow(host, oldHeight, newHeightAfterMeasure, cancellation, onSettled), DispatcherPriority.Loaded);
        }

        internal static Animation BuildReflowAnimation(double oldHeight, double newHeight) => new() {
            Duration = Motion.InspectorReflowDuration,
            Easing = Motion.EntranceEasing,
            Children = {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Layoutable.HeightProperty, oldHeight) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Layoutable.HeightProperty, newHeight) } },
            },
        };

        private static async Task RunReflow(Control host, double oldHeight, Func<double> newHeightAfterMeasure, CancellationTokenSource cancellation, Action onSettled) {
            try {
                CancellationToken token = cancellation.Token;
                if (token.IsCancellationRequested)
                    return;
                double newHeight = newHeightAfterMeasure();
                Animation reflow = BuildReflowAnimation(oldHeight, newHeight);

                try {
                    await reflow.RunAsync(host, token);
                } catch (OperationCanceledException) {
                    return;
                }

                if (token.IsCancellationRequested)
                    return;
                onSettled();
            } finally {
                if (reflowsInFlight.TryGetValue(host, out StrongBox<int>? count))
                    count.Value--;
                ReflowEnded?.Invoke();
            }
        }

        internal static void FadeOutAndRemove(Control control, Panel wrap, Panel track) {
            if (control.Parent is Panel oldParent)
                oldParent.Children.Remove(control);
            wrap.Children.Add(control);
            track.Children.Add(wrap);

            var animation = new Animation {
                Duration = Motion.CrossFadeDuration,
                Easing = Motion.LeavingEasing,
                Children = {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                },
            };
            _ = FadeAsync(animation, control, wrap, track);
        }

        private static async Task FadeAsync(Animation animation, Control control, Panel wrap, Panel track) {
            await animation.RunAsync(control);
            control.Opacity = 0;
            Dispatcher.UIThread.Post(() => track.Children.Remove(wrap));
        }
    }
}
