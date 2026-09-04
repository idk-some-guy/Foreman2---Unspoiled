using Avalonia.Media;

namespace Foreman.Mac.ViewModels {
    //Backs PickGrid (Task 5): one cell's worth of state. Key is whatever domain object the cell picks -
    //IAssembler for the Assembler group, IItem for Fuel - so PickAssembler/PickFuel can pattern-match it
    //straight back into the controller call without a parallel lookup table.
    public sealed record PickGridItem(object Key, string Name, string Subtitle, IImage? Icon, bool IsSelected, bool IsAvailable) {
        public string TooltipText => string.IsNullOrEmpty(Subtitle) ? Name : $"{Name}\n{Subtitle}";
    }
}
