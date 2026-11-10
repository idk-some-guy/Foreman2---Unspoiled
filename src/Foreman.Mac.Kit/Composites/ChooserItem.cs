using Avalonia.Collections;
using Avalonia.Media;

namespace Foreman.Mac.Kit.Composites {
    public sealed class ChooserItem {
        public IImage? Icon { get; set; }
        public string? Name { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsAvailable { get; set; } = true;
        public bool IsIconMissing { get; set; }
    }

    public sealed class ChooserSection {
        public string? Group { get; set; }
        public string? Subgroup { get; set; }
        public AvaloniaList<ChooserItem> Items { get; } = [];
    }
}
