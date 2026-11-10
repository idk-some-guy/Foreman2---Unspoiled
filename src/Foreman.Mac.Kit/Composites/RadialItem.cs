using System.Windows.Input;

namespace Foreman.Mac.Kit.Composites {
    public sealed class RadialItem {
        public string? GlyphKey { get; set; }
        public string? Label { get; set; }
        public ICommand? Command { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsEmpty { get; private init; }

        public static RadialItem Empty => new() { IsEmpty = true, IsEnabled = false };
    }
}
