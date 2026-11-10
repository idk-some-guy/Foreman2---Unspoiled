using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
            widthSub = this.GetResourceObservable("Foreman.Size.SwitchWidth", ToWidth).Subscribe(new ResourceSinks.DoubleSink(v => { width = v; Reposition(animate: false); }, width));
            knobSub = this.GetResourceObservable("Foreman.Size.SwitchKnob", ToKnob).Subscribe(new ResourceSinks.DoubleSink(v => { knobSize = v; Reposition(animate: false); }, knobSize));
            insetSub = this.GetResourceObservable("Foreman.Size.SwitchKnobInset", ToInset).Subscribe(new ResourceSinks.DoubleSink(v => { inset = v; Reposition(animate: false); }, inset));
        }

        private void Unsubscribe() {
            widthSub?.Dispose();
            knobSub?.Dispose();
            insetSub?.Dispose();
            widthSub = knobSub = insetSub = null;
        }

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
    }
}
