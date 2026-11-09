using Avalonia.Media;

namespace Foreman.Mac.Kit.Composites {
    public sealed class PickSlot {
        public IImage? Icon { get; set; }
        public string? Name { get; set; }
        public bool IsEmpty { get; set; }
        public bool IsMissing { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsUnavailable { get; set; }
        public bool IsNone { get; set; }
        public Color? Swatch { get; set; }
    }
}
