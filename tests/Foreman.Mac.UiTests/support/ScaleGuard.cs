using Foreman.Mac.Services;
using Xunit;

namespace Foreman.Mac.UiTests.support {
    internal static class ScaleGuard {
        internal static void AssertDefault() =>
            Assert.True(WindowScaler.CurrentScale == 1.0,
                $"WindowScaler.CurrentScale was {WindowScaler.CurrentScale}, not the 1.0 default - a scale-changing test left it behind or is running concurrently.");
    }
}
