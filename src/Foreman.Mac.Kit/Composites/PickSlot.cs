using Avalonia.Media;

namespace Foreman.Mac.Kit.Composites {
    //One module/beacon slot's content, independent of the PickCell that renders it - a plain data holder,
    //not an AvaloniaObject, since the gallery and every K3 host rebuild the grid wholesale rather than
    //observing property changes on individual slots.
    public sealed class PickSlot {
        public IImage? Icon { get; set; }
        public string? Name { get; set; }
        public bool IsEmpty { get; set; }
        public bool IsMissing { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
