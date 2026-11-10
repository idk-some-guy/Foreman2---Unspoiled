using System;

namespace Foreman.Mac.Kit.Controls {
    public sealed class MenuButtonAction {
        public required string Text { get; init; }
        public bool IsEnabled { get; init; } = true;
        public Action? Activate { get; init; }
    }
}
