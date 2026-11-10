using Avalonia.Media;
using Avalonia.Platform;
using Foreman.Mac.Kit.Appearance;
using System;

namespace Foreman.Mac.UiTests.support {
    internal sealed class FixedAppearance : IPlatformAppearance {
        public Color? AccentColor => null;
        public ColorContrastPreference ContrastPreference => ColorContrastPreference.NoPreference;
        public bool ReduceTransparency => false;
        public bool ReduceMotion => false;
        public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;

        public event EventHandler? Changed { add { } remove { } }
    }
}
