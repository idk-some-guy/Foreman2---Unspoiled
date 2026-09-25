using Avalonia.Media;

namespace Foreman.Mac.ViewModels {
    //Backs PickGrid: one cell's worth of state. Key is whatever domain object the cell picks -
    //IAssembler for the Assembler group, IItem for Fuel - so PickAssembler/PickFuel can pattern-match it
    //straight back into the controller call without a parallel lookup table.
    //IsMissing: the entity itself was removed from the current preset - distinct from !IsAvailable, which
    //here means "not researched in this save" (choice grids only; module slots keep their own IsMissing wiring).
    public sealed record PickGridItem(object Key, string Name, string Subtitle, IImage? Icon, bool IsSelected, bool IsAvailable, bool IsMissing = false) {
        public string TooltipText => string.IsNullOrEmpty(Subtitle) ? Name : $"{Name}\n{Subtitle}";
    }
}
