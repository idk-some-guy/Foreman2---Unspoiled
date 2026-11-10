using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Foreman.Mac.Kit.Composites;
using System;

namespace Foreman.Mac.Views.Sheets {
    internal static class SheetButtons {
        public static Button Make(string label, ButtonRowRole role) {
            var button = new Button { Theme = KitTheme("Kit.Button"), Content = label };
            ButtonRow.SetRole(button, role);
            return button;
        }

        public static ButtonRow Row(params Button[] buttons) {
            var row = new ButtonRow { Theme = KitTheme("Kit.ButtonRow") };
            foreach (Button button in buttons)
                row.Items.Add(button);
            return row;
        }

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"SheetButtons: kit theme '{key}' not found.");
        }
    }
}
