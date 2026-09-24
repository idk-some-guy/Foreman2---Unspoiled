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
    //Shared reflow/fade-out idioms FormRow, PalettePanel and ChooserGrid otherwise duplicated byte-for-byte.
    internal static class CompositeMotion {
        private static readonly ConditionalWeakTable<Control, StrongBox<int>> reflowsInFlight = [];
        private static event Action? ReflowEnded;

        //Scoped to one visual root: a reflow whose clock stopped with another root's session must not hold this one up.
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

        //FormRow and PalettePanel both drop back to auto height the same way: on detach, and once a reflow
        //settles. Cancels any in-flight reflow so its continuation never fires after the field is cleared.
        internal static void ClearExplicitHeight(Control host, ref CancellationTokenSource? reflowCancellation) {
            reflowCancellation?.Cancel();
            reflowCancellation = null;
            Transitions? transitions = host.Transitions;
            host.Transitions = null;
            host.Height = double.NaN;
            host.Transitions = transitions;
        }

        //Kit.FormRow's reflow idiom: pins the current height, then eases to newHeightAfterMeasure() once
        //posted past the layout pass a content change triggers, clearing back to auto on completion via
        //the caller's own onSettled (usually ClearExplicitHeight against its own field - async methods
        //can't take ref parameters, so the caller closes over its field instead).
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

        //A leaving cell/row gets its own throwaway wrap so it keeps rendering while it fades, then is removed.
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
            track.Children.Remove(wrap);
        }
    }
}
