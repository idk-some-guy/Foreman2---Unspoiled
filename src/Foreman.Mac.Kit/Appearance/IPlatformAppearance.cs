using Avalonia.Media;
using Avalonia.Platform;
using System;

namespace Foreman.Mac.Kit.Appearance {
    //Fakeable seam over platform accent/contrast/transparency/motion - Avalonia guards its own IPlatformSettings against test doubles.
    public interface IPlatformAppearance {
        Color? AccentColor { get; }
        ColorContrastPreference ContrastPreference { get; }
        bool ReduceTransparency { get; }
        bool ReduceMotion { get; }
        PlatformThemeVariant ThemeVariant { get; }
        event EventHandler? Changed;
    }
}
