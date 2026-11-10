using Avalonia.Media;

namespace Foreman.Mac.ViewModels {
    public sealed record PickGridItem(object Key, string Name, string Subtitle, IImage? Icon, bool IsSelected, bool IsAvailable, bool IsMissing = false) {
        public string TooltipText => string.IsNullOrEmpty(Subtitle) ? Name : $"{Name}\n{Subtitle}";
    }
}
