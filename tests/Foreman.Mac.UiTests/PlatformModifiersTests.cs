using Avalonia.Input;
using Foreman.Mac;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Exercises PlatformModifiers.Primary's platform fork directly (docs/upstream-divergences.md, phase 8
    //Task 2) - the UseIsMacOs seam forces each branch without depending on the host OS, since this box only
    //ever runs the tests on macOS.
    public class PlatformModifiersTests {
        [Fact]
        public void UseIsMacOs_True_ResolvesPrimaryAsMeta() {
            using (PlatformModifiers.UseIsMacOs(true))
                Assert.Equal(KeyModifiers.Meta, PlatformModifiers.Primary);
        }

        [Fact]
        public void UseIsMacOs_False_ResolvesPrimaryAsControl() {
            using (PlatformModifiers.UseIsMacOs(false))
                Assert.Equal(KeyModifiers.Control, PlatformModifiers.Primary);
        }

        [Fact]
        public void UseIsMacOs_Disposed_RestoresThePreviousOverride() {
            using (PlatformModifiers.UseIsMacOs(true)) {
                using (PlatformModifiers.UseIsMacOs(false))
                    Assert.Equal(KeyModifiers.Control, PlatformModifiers.Primary);

                Assert.Equal(KeyModifiers.Meta, PlatformModifiers.Primary);
            }
        }
    }
}
