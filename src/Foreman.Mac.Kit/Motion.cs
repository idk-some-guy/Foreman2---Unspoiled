using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Layout;
using System;

namespace Foreman.Mac.Kit {
    public enum TransitionKind { Fade, SlideFade, Scale, CrossFade, Reflow }

    //Every animated duration/easing the palette, the "Add to Graph" browser and the radial menu use
    //traces back here, so retuning one retunes all of them together.
    public static class Motion {
        public static readonly TimeSpan CrossFadeDuration = TimeSpan.FromMilliseconds(220);
        public static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan StaggerStep = TimeSpan.FromMilliseconds(12);
        public const int StaggerCap = 10;
        public static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(120);
        public static readonly TimeSpan ButtonStateDuration = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan RadialOpenDuration = TimeSpan.FromMilliseconds(160);
        public static readonly TimeSpan RadialCloseDuration = TimeSpan.FromMilliseconds(120);
        public const double RadialOpenScale = 0.9;
        public static readonly TimeSpan SheetOpenDuration = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan SheetCloseDuration = TimeSpan.FromMilliseconds(150);

        public static readonly TimeSpan PopoverDuration = TimeSpan.FromMilliseconds(120);
        public static readonly TimeSpan RadialSweepDuration = TimeSpan.FromMilliseconds(140);
        public static readonly TimeSpan RadialSweepCloseDuration = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan SwitchKnobDuration = TimeSpan.FromMilliseconds(120);
        public static readonly TimeSpan SegmentedSelectionDuration = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan InspectorReflowDuration = TimeSpan.FromMilliseconds(180);
        public static readonly TimeSpan DisclosureDuration = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan AlignTweenDuration = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan TabLabelCrossFadeDuration = TimeSpan.FromMilliseconds(120);
        public static readonly TimeSpan SpinnerDuration = TimeSpan.FromSeconds(0.8);
        public static readonly TimeSpan SpinnerReducedMotionDuration = TimeSpan.FromSeconds(3);

        //Set by AppearanceHub from the platform's Reduce Motion flag; an already-open site only picks up a
        //live change if it re-pushes its own Transitions on RefreshTheme (SheetHost and RadialMenu do).
        public static bool ReduceMotion { get; set; }

        public static Transitions ButtonTransitions => Transition(TransitionKind.Fade, ButtonStateDuration);
        public static Transitions SwitchTransitions => Transition(TransitionKind.SlideFade, SwitchKnobDuration);
        public static Transitions SegmentedTransitions => Transition(TransitionKind.SlideFade, SegmentedSelectionDuration);

        public static Easing CrossFadeEasing => new CubicEaseInOut();
        public static Easing EntranceEasing => new CubicEaseOut();
        public static Easing LeavingEasing => new CubicEaseIn();
        public static Easing RadialOpenEasing => new CubicEaseOut();

        //A row/cell beyond StaggerCap enters with the capped (not ever-growing) delay.
        public static TimeSpan StaggerDelay(int index) => StaggerStep * Math.Min(Math.Max(index, 0), StaggerCap);

        //Under ReduceMotion every kind collapses to a plain opacity fade of the same duration - nothing snaps.
        public static Transitions Transition(TransitionKind kind, TimeSpan duration, Easing? easing = null, TimeSpan delay = default) {
            if (ReduceMotion)
                return [
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = CrossFadeEasing, Delay = delay },
                ];

            Easing resolved = easing ?? EntranceEasing;
            return kind switch {
                TransitionKind.Fade =>
                    [
                        new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = resolved, Delay = delay },
                    ],
                TransitionKind.CrossFade =>
                    [
                        new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = CrossFadeEasing, Delay = delay },
                    ],
                TransitionKind.Reflow =>
                    [
                        new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = resolved, Delay = delay },
                        new DoubleTransition { Property = Layoutable.HeightProperty, Duration = duration, Easing = resolved, Delay = delay },
                    ],
                TransitionKind.SlideFade or TransitionKind.Scale =>
                    [
                        new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = resolved, Delay = delay },
                        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = resolved, Delay = delay },
                    ],
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
        }
    }
}
