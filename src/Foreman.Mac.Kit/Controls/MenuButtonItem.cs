namespace Foreman.Mac.Kit.Controls {
    public sealed class MenuButtonItem {
        public required string Text { get; init; }
        public bool IsDefault { get; init; }
        public bool IsChecked { get; set; }
    }
}
