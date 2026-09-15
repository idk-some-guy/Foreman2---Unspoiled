using Avalonia;
using Avalonia.Controls;

namespace Foreman.Mac.Kit.Controls {
    //A form row's control column has no "next chip" for the last one removed; this names where focus goes instead.
    public static class ChipHost {
        public static readonly AttachedProperty<Control?> FocusFallbackProperty =
            AvaloniaProperty.RegisterAttached<Control, Control?>("FocusFallback", typeof(ChipHost));

        public static Control? GetFocusFallback(Chip chip) => chip.GetValue(FocusFallbackProperty);
        public static void SetFocusFallback(Chip chip, Control? value) => chip.SetValue(FocusFallbackProperty, value);
    }
}
