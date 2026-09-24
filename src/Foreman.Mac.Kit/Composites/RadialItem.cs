using System.Windows.Input;

namespace Foreman.Mac.Kit.Composites {
    //One of a Kit.RadialRing's six fixed slots: an icon key resolved against Kit.Glyph.{key}.17, a label and the
    //command it commits. A slot with IsEnabled false still shows its wedge but never arms; an Empty slot draws a bare wedge.
    public sealed class RadialItem {
        public string? GlyphKey { get; set; }
        public string? Label { get; set; }
        public ICommand? Command { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsEmpty { get; private init; }

        public static RadialItem Empty => new() { IsEmpty = true, IsEnabled = false };
    }
}
