using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using System;
using System.Globalization;

namespace Foreman.Mac.Kit.Controls {
    public class Switch : ToggleSwitch {
        protected override Type StyleKeyOverride => typeof(Switch);

        private Border? knob;
        private double width = 30, knobSize = 14, inset = 2;
        private IDisposable? widthSub, knobSub, insetSub;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            knob = e.NameScope.Find<Border>("PART_Knob");
            Reposition(animate: false);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == IsCheckedProperty)
                Reposition(animate: true);
        }

        private void Subscribe() {
            Unsubscribe();
            widthSub = this.GetResourceObservable("Foreman.Size.SwitchWidth", ToWidth).Subscribe(new Sink(v => { width = v; Reposition(animate: false); }));
            knobSub = this.GetResourceObservable("Foreman.Size.SwitchKnob", ToKnob).Subscribe(new Sink(v => { knobSize = v; Reposition(animate: false); }));
            insetSub = this.GetResourceObservable("Foreman.Size.SwitchKnobInset", ToInset).Subscribe(new Sink(v => { inset = v; Reposition(animate: false); }));
        }

        private void Unsubscribe() {
            widthSub?.Dispose();
            knobSub?.Dispose();
            insetSub?.Dispose();
            widthSub = knobSub = insetSub = null;
        }

        //A real toggle slides; a token/scale change snaps by briefly clearing Transitions first.
        private void Reposition(bool animate) {
            if (knob is null)
                return;

            double x = IsChecked == true ? width - inset - knobSize : inset;
            TransformOperations operations = TransformOperations.Parse($"translateX({x.ToString(CultureInfo.InvariantCulture)}px)");
            if (animate) {
                knob.RenderTransform = operations;
                return;
            }

            var transitions = knob.Transitions;
            knob.Transitions = null;
            knob.RenderTransform = operations;
            knob.Transitions = transitions;
        }

        private static object ToWidth(object? value) => value is double d ? d : 30.0;
        private static object ToKnob(object? value) => value is double d ? d : 14.0;
        private static object ToInset(object? value) => value is double d ? d : 2.0;

        private sealed class Sink(Action<double> onNext) : IObserver<object?> {
            public void OnNext(object? value) => onNext(value is double d ? d : 0.0);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
