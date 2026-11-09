using Avalonia;
using Avalonia.Controls;
using System;
using System.Linq;

namespace Foreman.Mac.Kit.Controls {
    public static class TrimmedTooltip {
        public static readonly AttachedProperty<bool> EnabledProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, bool>("Enabled", typeof(TrimmedTooltip));

        public static readonly AttachedProperty<Control?> HostProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, Control?>("Host", typeof(TrimmedTooltip));

        private static readonly AttachedProperty<Tip?> WrittenProperty =
            AvaloniaProperty.RegisterAttached<TextBlock, Tip?>("Written", typeof(TrimmedTooltip));

        static TrimmedTooltip() => EnabledProperty.Changed.AddClassHandler<TextBlock>(OnEnabledChanged);

        public static bool GetEnabled(TextBlock text) => text.GetValue(EnabledProperty);
        public static void SetEnabled(TextBlock text, bool value) => text.SetValue(EnabledProperty, value);

        public static Control? GetHost(TextBlock text) => text.GetValue(HostProperty);
        public static void SetHost(TextBlock text, Control? value) => text.SetValue(HostProperty, value);

        private static void OnEnabledChanged(TextBlock text, AvaloniaPropertyChangedEventArgs e) {
            text.LayoutUpdated -= OnLayoutUpdated;
            text.PropertyChanged -= OnPropertyChanged;
            if (Equals(e.NewValue, true)) {
                text.LayoutUpdated += OnLayoutUpdated;
                text.PropertyChanged += OnPropertyChanged;
                Apply(text);
            } else {
                Show(text, null);
            }
        }

        private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
            if (e.Property == Visual.BoundsProperty)
                Apply((TextBlock)sender!);
        }

        private static void OnLayoutUpdated(object? sender, EventArgs e) => Apply((TextBlock)sender!);

        private static void Apply(TextBlock text) =>
            Show(text, text.Bounds.Width > 0 && text.TextLayout.TextLines.Any(l => l.HasCollapsed) && text.Text is { } full ? new Tip(full) : null);

        //Avalonia hands a string tip to the screen reader as help text, so the trimmed label would be read twice; this shows the same text but isn't a string.
        private sealed record Tip(string Text) {
            public override string ToString() => Text;
        }

        private static void Show(TextBlock text, Tip? tip) {
            Control target = GetHost(text) ?? text;
            object? current = ToolTip.GetTip(target);
            Tip? written = text.GetValue(WrittenProperty);
            if (current is not null && !Equals(current, written))
                return;
            if (!Equals(current, tip))
                ToolTip.SetTip(target, tip);
            text.SetValue(WrittenProperty, tip);
        }
    }
}
