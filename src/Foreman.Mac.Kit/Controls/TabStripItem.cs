using Avalonia.Media;

namespace Foreman.Mac.Kit.Controls {
    //.toolbar-tabs reads Glyph; .underline never draws one (addendum §05).
    public sealed class TabStripItem {
        public string Text { get; set; } = "";
        public Geometry? Glyph { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? ToolTip { get; set; }
    }
}
