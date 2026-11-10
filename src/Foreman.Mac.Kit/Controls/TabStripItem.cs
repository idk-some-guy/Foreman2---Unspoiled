using Avalonia.Media;

namespace Foreman.Mac.Kit.Controls {
    public sealed class TabStripItem {
        public string Text { get; set; } = "";
        public Geometry? Glyph { get; set; }
        public Geometry? GlyphFill { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? ToolTip { get; set; }
    }
}
