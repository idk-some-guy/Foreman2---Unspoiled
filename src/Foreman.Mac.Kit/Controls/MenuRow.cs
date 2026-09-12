using System;

namespace Foreman.Mac.Kit.Controls {
    //One row of a Kit.MenuPanel: a value/checkable item, a separator, or an action (Reset has no check slot).
    public sealed class MenuRow {
        public required string Text { get; init; }
        public bool IsChecked { get; set; }
        public bool HasCheckSlot { get; init; } = true;
        public bool IsSeparator { get; init; }
        public bool IsEnabled { get; init; } = true;
        public bool KeepOpenOnActivate { get; init; }
        public Action? Activate { get; init; }

        public static MenuRow Separator() => new() { Text = "", IsSeparator = true, HasCheckSlot = false, IsEnabled = false };
    }
}
