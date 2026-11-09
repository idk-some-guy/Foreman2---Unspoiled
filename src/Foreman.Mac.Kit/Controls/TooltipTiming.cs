using Avalonia;
using Avalonia.Controls;

namespace Foreman.Mac.Kit.Controls {
    public static class TooltipTiming {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(TooltipTiming));

        static TooltipTiming() => IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);

        public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e) {
            if (!Equals(e.NewValue, true))
                return;
            ToolTip.SetShowDelay(control, (int)Motion.TooltipDelay.TotalMilliseconds);
            ToolTip.SetBetweenShowDelay(control, (int)Motion.TooltipWarmWindow.TotalMilliseconds);
        }
    }
}
