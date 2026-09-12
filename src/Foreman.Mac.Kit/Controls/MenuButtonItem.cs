namespace Foreman.Mac.Kit.Controls {
    //One checkable entry in a Kit.MenuButton's menu; IsDefault is what Reset restores and what IsFilled compares against.
    public sealed class MenuButtonItem {
        public required string Text { get; init; }
        public bool IsDefault { get; init; }
        public bool IsChecked { get; set; }
    }
}
