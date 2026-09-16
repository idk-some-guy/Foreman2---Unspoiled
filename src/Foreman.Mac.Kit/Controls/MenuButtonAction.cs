using System;

namespace Foreman.Mac.Kit.Controls {
    //One plain action entry in a Kit.MenuButton's overflow menu - no check slot, no Reset row (that machinery is MenuButtonItem's).
    public sealed class MenuButtonAction {
        public required string Text { get; init; }
        public bool IsEnabled { get; init; } = true;
        public Action? Activate { get; init; }
    }
}
