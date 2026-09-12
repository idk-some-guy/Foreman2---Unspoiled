using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Controls {
    public class ProgressBar : Avalonia.Controls.ProgressBar {
        protected override Type StyleKeyOverride => typeof(ProgressBar);

        private Border? indicator;
        private readonly List<IDisposable> subs = [];

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
            ValueProperty.Changed.AddClassHandler<ProgressBar>((p, _) => p.ApplyFill());
            MinimumProperty.Changed.AddClassHandler<ProgressBar>((p, _) => p.ApplyFill());
            MaximumProperty.Changed.AddClassHandler<ProgressBar>((p, _) => p.ApplyFill());
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            indicator = e.NameScope.Find<Border>("PART_Indicator");
            ApplyFill();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
            //A detached keyframe animation completes for good, so the selector has to re-match to restart it.
            Classes.Set("animating", true);
            ApplyFill();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Classes.Set("animating", false);
            Unsubscribe();
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Size.ProgressBarWidth").Subscribe(new ResourceSinks.DoubleSink(v => { TrackWidth = v; ApplyFill(); }, TrackWidth)));
            subs.Add(this.GetResourceObservable("Foreman.Size.ProgressBarSegment").Subscribe(new ResourceSinks.DoubleSink(v => { SegmentWidth = v; ApplyFill(); }, SegmentWidth)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void ApplyFill() {
            Classes.Set("reduce-motion", Motion.ReduceMotion);
            SetValue(TravelDistanceProperty, double.NaN);
            TravelDistance = Math.Max(TrackWidth - SegmentWidth, 0);
            if (indicator is null)
                return;

            if (IsIndeterminate) {
                indicator.Width = Motion.ReduceMotion ? TrackWidth : SegmentWidth;
                return;
            }

            double range = Maximum - Minimum;
            double fraction = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
            indicator.Width = TrackWidth * fraction;
        }
    }
}
