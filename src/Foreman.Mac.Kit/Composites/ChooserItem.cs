using Avalonia.Collections;
using Avalonia.Media;

namespace Foreman.Mac.Kit.Composites {
    //A single catalog entry - what one Kit.ChooserCell shows; selection and keyboard position live on the grid.
    public sealed class ChooserItem {
        public IImage? Icon { get; set; }
        public string? Name { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsAvailable { get; set; } = true;
        //Set when the icon failed to decode; a null Icon alone still means it is loading.
        public bool IsIconMissing { get; set; }
    }

    //One heading plus the items under it; Group/Subgroup stay apart so the grid can pick either depending on whether a search Query is active.
    public sealed class ChooserSection {
        public string? Group { get; set; }
        public string? Subgroup { get; set; }
        public AvaloniaList<ChooserItem> Items { get; } = [];
    }
}
