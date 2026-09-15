using Avalonia.Collections;
using Avalonia.Media;

namespace Foreman.Mac.Kit.Composites {
    //A single catalog entry - what one Kit.ChooserCell shows. Icon/Name/IsEnabled only; selection and
    //keyboard position live on the grid, matching PickSlot's own split.
    public sealed class ChooserItem {
        public IImage? Icon { get; set; }
        public string? Name { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    //One heading plus the items under it. Group/Subgroup are kept apart rather than pre-joined so the grid
    //can pick "Group · Subgroup" or the bare Subgroup depending on whether a search Query is active.
    public sealed class ChooserSection {
        public string? Group { get; set; }
        public string? Subgroup { get; set; }
        public AvaloniaList<ChooserItem> Items { get; } = [];
    }
}
