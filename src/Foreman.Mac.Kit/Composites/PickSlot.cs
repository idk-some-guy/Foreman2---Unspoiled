using Avalonia.Media;

namespace Foreman.Mac.Kit.Composites {
    //One module/beacon slot's content, independent of the PickCell that renders it - a plain data holder, not an AvaloniaObject, since hosts rebuild the grid wholesale rather than observing property changes.
    public sealed class PickSlot {
        public IImage? Icon { get; set; }
        public string? Name { get; set; }
        public bool IsEmpty { get; set; }
        public bool IsMissing { get; set; }
        public bool IsEnabled { get; set; } = true;
        //Choice mode: enabled in the preset but not researched in this save - dims without disabling (A22 amended).
        public bool IsUnavailable { get; set; }
        //Choice mode: the leading "None" cell (Beacon) - a real, selectable member of the set, not an empty slot.
        public bool IsNone { get; set; }
        //A colour cell draws this swatch in place of an icon.
        public Color? Swatch { get; set; }
    }
}
