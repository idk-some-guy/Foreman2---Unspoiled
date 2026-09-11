using Foreman.Mac.Services;
using Xunit;

namespace Foreman.Mac.UiTests.support {
    //WindowScaler is process-wide; a fixture built while a scale test elsewhere holds a non-default scale
    //gets silently wrong geometry instead of a clear failure - call this at fixture-creation time to convert
    //that into a named assertion.
    internal static class ScaleGuard {
        internal static void AssertDefault() =>
            Assert.True(WindowScaler.CurrentScale == 1.0,
                $"WindowScaler.CurrentScale was {WindowScaler.CurrentScale}, not the 1.0 default - a scale-changing test left it behind or is running concurrently.");
    }
}
