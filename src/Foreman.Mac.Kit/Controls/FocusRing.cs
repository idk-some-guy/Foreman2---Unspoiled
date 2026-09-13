using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace Foreman.Mac.Kit.Controls {
    public static class FocusRing {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(FocusRing));

        public static readonly AttachedProperty<CornerRadius?> CornerRadiusProperty =
            AvaloniaProperty.RegisterAttached<Control, CornerRadius?>("CornerRadius", typeof(FocusRing));

        public static readonly AttachedProperty<bool> ShowAlwaysProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("ShowAlways", typeof(FocusRing));

        private static readonly AttachedProperty<bool> KeyboardFocusedProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("KeyboardFocused", typeof(FocusRing));

        private static readonly AttachedProperty<IDisposable?> RingBindingsProperty =
            AvaloniaProperty.RegisterAttached<Control, IDisposable?>("RingBindings", typeof(FocusRing));

        private static readonly AttachedProperty<bool> AttachHandlersAddedProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("AttachHandlersAdded", typeof(FocusRing));

        static FocusRing() {
            IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
            ShowAlwaysProperty.Changed.AddClassHandler<Control>(OnShowAlwaysChanged);
        }

        public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

        public static CornerRadius? GetCornerRadius(Control control) => control.GetValue(CornerRadiusProperty);
        public static void SetCornerRadius(Control control, CornerRadius? value) => control.SetValue(CornerRadiusProperty, value);

        public static bool GetShowAlways(Control control) => control.GetValue(ShowAlwaysProperty);
        public static void SetShowAlways(Control control, bool value) => control.SetValue(ShowAlwaysProperty, value);

        private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
            if (Equals(e.NewValue, true)) {
                control.GotFocus += OnGotFocus;
                control.LostFocus += OnLostFocus;
                AddAttachHandlers(control);
            } else {
                control.GotFocus -= OnGotFocus;
                control.LostFocus -= OnLostFocus;
                RemoveAttachHandlersIfInactive(control);
                ClearRing(control);
            }
        }

        private static void OnShowAlwaysChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
            if (Equals(e.NewValue, true)) {
                AddAttachHandlers(control);
                Show(control);
            } else {
                RemoveAttachHandlersIfInactive(control);
                if (!control.GetValue(KeyboardFocusedProperty))
                    ClearRing(control);
            }
        }

        private static void AddAttachHandlers(Control control) {
            if (control.GetValue(AttachHandlersAddedProperty))
                return;
            control.SetValue(AttachHandlersAddedProperty, true);
            control.AttachedToVisualTree += OnControlAttached;
            control.DetachedFromVisualTree += OnControlDetached;
        }

        private static void RemoveAttachHandlersIfInactive(Control control) {
            if (control.GetValue(IsEnabledProperty) || control.GetValue(ShowAlwaysProperty))
                return;
            control.SetValue(AttachHandlersAddedProperty, false);
            control.AttachedToVisualTree -= OnControlAttached;
            control.DetachedFromVisualTree -= OnControlDetached;
        }

        private static void OnControlAttached(object? sender, VisualTreeAttachmentEventArgs e) {
            var control = (Control)sender!;
            if (control.GetValue(ShowAlwaysProperty) || control.GetValue(KeyboardFocusedProperty))
                Show(control);
        }

        private static void OnControlDetached(object? sender, VisualTreeAttachmentEventArgs e) =>
            ClearRing((Control)sender!);

        private static void ClearRing(Control control) {
            if (control.GetValue(RingBindingsProperty) is IDisposable bindings) {
                bindings.Dispose();
                control.ClearValue(RingBindingsProperty);
            }
            AdornerLayer.SetAdorner(control, null);
        }

        private static void OnGotFocus(object? sender, FocusChangedEventArgs e) {
            var control = (Control)sender!;
            if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) {
                control.SetValue(KeyboardFocusedProperty, true);
                Show(control);
            }
        }

        private static void OnLostFocus(object? sender, FocusChangedEventArgs e) {
            var control = (Control)sender!;
            control.SetValue(KeyboardFocusedProperty, false);
            if (!control.GetValue(ShowAlwaysProperty))
                ClearRing(control);
        }

        private static void Show(Control control) {
            CornerRadius controlRadius = GetCornerRadius(control) ??
                (control is TemplatedControl templated ? templated.GetValue(Border.CornerRadiusProperty) : default);

            var ring = new Rectangle {
                Fill = null,
                IsHitTestVisible = false,
                //A 2pt offset plus half a 3pt stroke lands on a fractional pixel; rounding would add a stray pixel.
                UseLayoutRounding = false,
            };
            //AdornerLayer clips an adorner to the adorned element's bounds by default; our ring extends past it via a negative margin.
            AdornerLayer.SetIsClipEnabled(ring, false);

            double width = 3.0;
            double offset = 0.0;

            void ApplyWidth(object? value) {
                width = value is double w ? w : 3.0;
                ring.StrokeThickness = width;
                ring.Margin = RingMargin(width, offset);
            }

            void ApplyOffset(object? value) {
                offset = value is double o ? o : 0.0;
                ring.RadiusX = controlRadius.TopLeft + offset;
                ring.RadiusY = controlRadius.TopLeft + offset;
                ring.Margin = RingMargin(width, offset);
            }

            IDisposable stroke = ring.Bind(Shape.StrokeProperty, ring.GetResourceObservable("Foreman.Color.FocusRing", ToBrush));
            IDisposable thickness = ring.GetResourceObservable("Foreman.FocusRing.Width", ToWidth).Subscribe(new RingObserver(ApplyWidth));
            IDisposable margin = ring.GetResourceObservable("Foreman.FocusRing.Offset", ToOffset).Subscribe(new RingObserver(ApplyOffset));

            if (control.GetValue(RingBindingsProperty) is IDisposable previous)
                previous.Dispose();
            control.SetValue(RingBindingsProperty, new RingBindings(stroke, thickness, margin));

            AdornerLayer.SetAdorner(control, ring);
        }

        private static object? ToBrush(object? value) =>
            value switch {
                Color color => new SolidColorBrush(color),
                IBrush brush => brush,
                _ => null,
            };

        private static object ToWidth(object? value) => value is double width ? width : 3.0;

        private static object ToOffset(object? value) => value is double offset ? offset : 0.0;

        private static Thickness RingMargin(double width, double offset) => new(-(offset + width / 2));

        private sealed class RingObserver(Action<object?> onNext) : IObserver<object?> {
            public void OnNext(object? value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        private sealed class RingBindings(IDisposable stroke, IDisposable width, IDisposable offset) : IDisposable {
            public void Dispose() {
                stroke.Dispose();
                width.Dispose();
                offset.Dispose();
            }
        }
    }
}
