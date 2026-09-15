using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Controls {
    public class Spinner : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(Spinner);

        private Ellipse? track;
        private Arc? head;
        private readonly List<IDisposable> subs = [];

        public double Diameter { get; private set; } = 18;
        public double StrokeWidth { get; private set; } = 2;
        public TimeSpan RotationDuration { get; private set; } = Motion.SpinnerDuration;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            track = e.NameScope.Find<Ellipse>("PART_Track");
            head = e.NameScope.Find<Arc>("PART_Head");
            ApplyMetrics();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
            //Never stops under Reduce Motion - it slows to SpinnerReducedMotionDuration.
            RotationDuration = Motion.ReduceMotion ? Motion.SpinnerReducedMotionDuration : Motion.SpinnerDuration;
            Classes.Set("reduce-motion", Motion.ReduceMotion);
            //A detached keyframe animation completes for good, so the selector has to re-match to restart it.
            Classes.Set("animating", IsVisible);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Classes.Set("animating", false);
            Unsubscribe();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty)
                Classes.Set("animating", change.GetNewValue<bool>());
        }

        private void Subscribe() {
            Unsubscribe();
            bool large = Classes.Contains("lg");
            subs.Add(this.GetResourceObservable(large ? "Foreman.Size.SpinnerLarge" : "Foreman.Size.Spinner")
                .Subscribe(new ResourceSinks.DoubleSink(v => { Diameter = v; ApplyMetrics(); }, Diameter)));
            subs.Add(this.GetResourceObservable(large ? "Foreman.Size.SpinnerStrokeLarge" : "Foreman.Size.SpinnerStroke")
                .Subscribe(new ResourceSinks.DoubleSink(v => { StrokeWidth = v; ApplyMetrics(); }, StrokeWidth)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void ApplyMetrics() {
            if (track is not null) {
                track.Width = Diameter;
                track.Height = Diameter;
                track.StrokeThickness = StrokeWidth;
            }
            if (head is not null) {
                head.Width = Diameter;
                head.Height = Diameter;
                head.StrokeThickness = StrokeWidth;
            }
        }
    }
}
