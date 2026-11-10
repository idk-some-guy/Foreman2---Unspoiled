using Avalonia;
using Avalonia.Headless.XUnit;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Appearance {
    public class AppearanceIsolationTests {
        [AvaloniaFact]
        public void TheSuite_HostsAPlainApplication_SoNoPlatformAppearanceReadsTheMachine() {
            Assert.Equal(typeof(Application), Application.Current!.GetType());
            Assert.False(Motion.ReduceMotion);

            var fake = new FakePlatformAppearance();
            using var hub = new AppearanceHub(Application.Current, fake);

            Assert.False(Motion.ReduceMotion);
        }
    }
}
