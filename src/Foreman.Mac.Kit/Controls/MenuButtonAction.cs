using System;

namespace Foreman.Mac.Kit.Controls {
    public sealed class MenuButtonAction {
        public required string Text { get; init; }
        public bool IsEnabled { get; init; } = true;
        public bool IsSeparator { get; init; }
        public Action? Activate { get; init; }
    }
}
