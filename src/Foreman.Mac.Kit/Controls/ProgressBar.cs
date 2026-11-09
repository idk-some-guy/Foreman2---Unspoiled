using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Kit.Controls {
    public class ProgressBar : Avalonia.Controls.ProgressBar {
        protected override Type StyleKeyOverride => typeof(ProgressBar);

        private Border? indicator;
        private readonly List<IDisposable> subs = [];
        private CancellationTokenSource? fillRun;
        private bool isAttached;
        private double arrangedWidth;

        public static readonly StyledProperty<double> TravelDistanceProperty =
            AvaloniaProperty.Register<ProgressBar, double>(nameof(TravelDistance));

        public double TrackWidth { get; private set; } = 140;
        public double SegmentWidth { get; private set; } = 40;

        public double TravelDistance {
            get => GetValue(TravelDistanceProperty);
            private set => SetValue(TravelDistanceProperty, value);
        }

        static ProgressBar() {
            IsIndeterminateProperty.Changed.AddClassHandler<ProgressBar>((p, _) => p.ApplyFill());
            ValueProperty.Changed.AddClassHandler<ProgressBar>((p, _) => p.ApplyFill(animate: true));
            MinimumProperty.Changed.AddClassHandler<ProgressBar>((p, _) => p.ApplyFill());
            MaximumProperty.Changed.AddClassHandler<ProgressBar>((p, _) => p.ApplyFill());
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            indicator = e.NameScope.Find<Border>("PART_Fill");
            ApplyFill();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            isAttached = true;
            Subscribe();
            //A detached keyframe animation completes for good, so the selector has to re-match to restart it.
            Classes.Set("animating", true);
            ApplyFill();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Classes.Set("animating", false);
            isAttached = false;
            fillRun?.Cancel();
            fillRun = null;
            Unsubscribe();
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Size.ProgressBarWidth").Subscribe(new ResourceSinks.DoubleSink(v => { TrackWidth = v; ApplyFill(); }, TrackWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Size.ProgressBarSegment").Subscribe(new ResourceSinks.DoubleSink(v => { SegmentWidth = v; ApplyFill(); }, SegmentWidth)));
        }

        protected override Size ArrangeOverride(Size finalSize) {
            Size arranged = base.ArrangeOverride(finalSize);
            if (arranged.Width != arrangedWidth) {
                arrangedWidth = arranged.Width;
                ApplyFill();
            }
            return arranged;
        }

        private double Track => arrangedWidth > 0 ? arrangedWidth : TrackWidth;

        protected override AutomationPeer OnCreateAutomationPeer() => new ProgressBarPercentPeer(this);

        private double Fraction {
            get {
                double range = Maximum - Minimum;
                return range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
            }
        }

        private sealed class ProgressBarPercentPeer(ProgressBar owner) : ProgressBarAutomationPeer(owner), IValueProvider {
            bool IValueProvider.IsReadOnly => true;
            string? IValueProvider.Value => owner.IsIndeterminate ? null : $"{(int)Math.Round(owner.Fraction * 100, MidpointRounding.AwayFromZero)} %";
            void IValueProvider.SetValue(string? value) { }
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void ApplyFill(bool animate = false) {
            Classes.Set("reduce-motion", Motion.ReduceMotion);
            SetValue(TravelDistanceProperty, double.NaN);
            TravelDistance = Math.Max(Track - SegmentWidth, 0);
            if (indicator is null)
                return;

            if (IsIndeterminate) {
                indicator.Width = Motion.ReduceMotion ? Track : SegmentWidth;
                return;
            }

            SetFillWidth(Track * Fraction, animate);
        }

        private void SetFillWidth(double target, bool animate) {
            fillRun?.Cancel();
            fillRun = null;
            Border fill = indicator!;
            double from = fill.Width;
            if (!animate || Motion.ReduceMotion || !isAttached || double.IsNaN(from) || Math.Abs(from - target) < 0.5) {
                fill.Width = target;
                return;
            }

            var run = new CancellationTokenSource();
            fillRun = run;
            var animation = new Animation {
                Duration = Motion.EntranceDuration,
                Easing = Motion.EntranceEasing,
                Children = {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(WidthProperty, from) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(WidthProperty, target) } },
                },
            };
            //RunAsync leaves no end value behind, so the continuation pins it.
            animation.RunAsync(fill, run.Token).ContinueWith(_ => {
                if (!run.IsCancellationRequested)
                    fill.Width = target;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
