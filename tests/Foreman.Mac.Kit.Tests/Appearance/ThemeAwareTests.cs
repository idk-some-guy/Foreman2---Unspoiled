using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Foreman.Mac.Kit.Appearance;
using System;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Appearance {
    public class ThemeAwareTests : IDisposable {
        private sealed class FakeAppearance : IPlatformAppearance {
            private EventHandler? changed;

            public Color? AccentColor { get; set; }
            public ColorContrastPreference ContrastPreference { get; set; } = ColorContrastPreference.NoPreference;
            public bool ReduceTransparency { get; set; }
            public bool ReduceMotion { get; set; }
            public PlatformThemeVariant ThemeVariant { get; set; } = PlatformThemeVariant.Light;

            public event EventHandler? Changed { add => changed += value; remove => changed -= value; }
        }

        private readonly AppearanceHub? previousCurrent = AppearanceHub.Current;

        public void Dispose() {
            AppearanceHub.Current = previousCurrent;
            GC.SuppressFinalize(this);
        }

        [AvaloniaFact]
        public void Attach_HostAttachedToVisualTree_HubRefreshInvokesAction() {
            var hub = new AppearanceHub((Application)Application.Current!, new FakeAppearance());
            AppearanceHub.Current = hub;
            int refreshCount = 0;
            var host = new Border();
            ThemeAware.Attach(host, () => refreshCount++);

            var window = new Window { Content = host };
            window.Show();

            hub.Refresh();

            Assert.Equal(1, refreshCount);
            hub.Dispose();
        }

        [AvaloniaFact]
        public void Attach_HostDetached_HubRefreshNoLongerInvokesAction() {
            var hub = new AppearanceHub((Application)Application.Current!, new FakeAppearance());
            AppearanceHub.Current = hub;
            int refreshCount = 0;
            var host = new Border();
            ThemeAware.Attach(host, () => refreshCount++);

            var window = new Window { Content = host };
            window.Show();
            window.Content = null;

            hub.Refresh();

            Assert.Equal(0, refreshCount);
            hub.Dispose();
        }
    }
}
