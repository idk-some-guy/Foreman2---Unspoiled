using Avalonia;
using Avalonia.Styling;

namespace Foreman.Mac.Services {
    public sealed class ThemeService {
        private readonly Application app;

        public ThemeService(Application app) {
            this.app = app;
        }

        public void Apply(ThemeMode mode) {
            switch (mode) {
                case ThemeMode.Light:
                    app.RequestedThemeVariant = ThemeVariant.Light;
                    return;
                case ThemeMode.Dark:
                    app.RequestedThemeVariant = ThemeVariant.Dark;
                    return;
                default:
                    app.RequestedThemeVariant = ThemeVariant.Default;
                    return;
            }
        }
    }
}
