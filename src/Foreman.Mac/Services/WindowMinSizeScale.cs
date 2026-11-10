using Avalonia.Controls;

namespace Foreman.Mac.Services {
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
