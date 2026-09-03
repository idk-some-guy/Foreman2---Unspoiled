using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using System;

namespace Foreman.Mac.Services {
    //Thin seam over IPlatformSettings' color-scheme half - Avalonia guards IPlatformSettings against
    //third-party implementations (including test doubles), so ThemeService depends on this instead to
    //stay unit-testable. Production always goes through PlatformSettingsColorSource, below.
    internal interface IPlatformColorSource {
        bool IsDark { get; }
        event EventHandler? Changed;
    }

    internal sealed class PlatformSettingsColorSource : IPlatformColorSource {
        private readonly IPlatformSettings settings;

        public PlatformSettingsColorSource(IPlatformSettings settings) {
            this.settings = settings;
            settings.ColorValuesChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool IsDark => settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        public event EventHandler? Changed;
    }

    //Owns the FlagDarkMode tri-state -> Avalonia RequestedThemeVariant mapping (upstream MainForm.
    //SetDarkMode/SetLightMode, reference docs/upstream-divergences.md). Light/Dark force a concrete
    //variant; System hands RequestedThemeVariant to Avalonia's own OS-follow (Default) and additionally
    //tracks the platform's color scheme itself, since non-resource-bound surfaces (Skia clear colors, row
    //DTOs built once in code) have no DynamicResource to pick up a live OS flip on their own.
    public sealed class ThemeService : IDisposable {
        private readonly Application app;
        private readonly IPlatformColorSource? colorSource;
        private EventHandler? changedHandler;

        public ThemeService(Application app) : this(app, ResolveColorSource(app)) {
        }

        internal ThemeService(Application app, IPlatformColorSource? colorSource) {
            this.app = app;
            this.colorSource = colorSource;
        }

        private static PlatformSettingsColorSource? ResolveColorSource(Application app) =>
            app.PlatformSettings is IPlatformSettings settings ? new PlatformSettingsColorSource(settings) : null;

        public bool IsDark { get; private set; }

        public void Apply(ThemeMode mode) {
            Unsubscribe();
            switch (mode) {
                case ThemeMode.Light:
                    IsDark = false;
                    app.RequestedThemeVariant = ThemeVariant.Light;
                    return;
                case ThemeMode.Dark:
                    IsDark = true;
                    app.RequestedThemeVariant = ThemeVariant.Dark;
                    return;
                default:
                    app.RequestedThemeVariant = ThemeVariant.Default;
                    ApplySystem();
                    return;
            }
        }

        private void ApplySystem() {
            if (colorSource is null)
                return;

            IsDark = colorSource.IsDark;
            changedHandler = (_, _) => IsDark = colorSource.IsDark;
            colorSource.Changed += changedHandler;
        }

        private void Unsubscribe() {
            if (colorSource is not null && changedHandler is not null)
                colorSource.Changed -= changedHandler;
            changedHandler = null;
        }

        public void Dispose() => Unsubscribe();
    }
}
