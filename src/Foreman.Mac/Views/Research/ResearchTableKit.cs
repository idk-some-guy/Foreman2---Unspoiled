using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using System;

namespace Foreman.Mac.Views.Research {
    internal static class ResearchTableKit {
        public static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"Research table: kit theme '{key}' not found.");
        }

        public static Thickness RowInset(Control owner) {
            Thickness frame = owner.TryFindResource("Foreman.Thickness.ListFramePadding", owner.ActualThemeVariant, out object? f) && f is Thickness ft ? ft : default;
            Thickness item = owner.TryFindResource("Foreman.Thickness.ListRowPadding", owner.ActualThemeVariant, out object? r) && r is Thickness rt ? rt : default;
            return new Thickness(1 + frame.Left + item.Left, 0, 1 + frame.Right + item.Right, 0);
        }

        public static void FocusFirstRow(ListBox table, ListBoxItem? first) {
            if (first is null)
                return;
            table.SelectedItem = first;
            first.Focus();
        }
    }
}
