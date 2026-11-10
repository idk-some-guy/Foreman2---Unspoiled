using Avalonia;
using Avalonia.Headless.XUnit;
using Foreman.Mac.Kit;
using Foreman.Mac.UiTests.support;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class AppearanceIsolationTests {
        [AvaloniaFact]
        public void FreshInstall_UsesTheFixedAppearance_NotTheMachinesAccessibilitySettings() {
            var app = (App)Application.Current!;
            app.InstallAppearance(null);

            Assert.IsType<FixedAppearance>(app.Appearance);
            Assert.False(Motion.ReduceMotion);
        }
    }
}
