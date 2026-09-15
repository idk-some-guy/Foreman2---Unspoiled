using System.Windows.Input;

namespace Foreman.Mac.Kit.Composites {
    //One of a Kit.RadialRing's six wedges: an icon key resolved against Kit.Glyph.{key}.17, a label and the
    //command it commits. A slot with IsEnabled false still occupies its wedge but never arms.
    public sealed class RadialItem {
        public string? GlyphKey { get; set; }
        public string? Label { get; set; }
        public ICommand? Command { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
