using Avalonia.Controls;

namespace Foreman.Mac.Services {
    //A window's own registration for WindowScaler.SetScale: captures the base minimum size once, at
    //construction, and recomputes it whenever the owner calls Apply - from RefreshTheme, on the same
    //AppearanceHub notification every other scale/theme consumer already answers to.
    public sealed class WindowMinSizeScale {
        private readonly Window window;
        private readonly double baseWidth;
        private readonly double baseHeight;
        private readonly (double Width, double Height)? maxSize;

        public WindowMinSizeScale(Window window, (double Width, double Height)? maxSize = null) {
            this.window = window;
            this.maxSize = maxSize;
            baseWidth = window.MinWidth;
            baseHeight = window.MinHeight;
            Apply();
        }

        public void Apply() {
            window.MinWidth = WindowScaler.Interpolate(baseWidth, maxSize?.Width);
            window.MinHeight = WindowScaler.Interpolate(baseHeight, maxSize?.Height);
        }
    }
}
