using Avalonia.Headless.XUnit;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using System;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    [Collection(ScaleTestGroup.Name)]
    public class ScaleGuardTests : IDisposable {
        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        [AvaloniaFact]
        public void AssertDefault_AtDefaultScale_Passes() {
            WindowScaler.SetScale(1.0);
            ScaleGuard.AssertDefault();
        }

        [AvaloniaFact]
        public void AssertDefault_AtNonDefaultScale_Fails() {
            WindowScaler.SetScale(1.5);

            Exception? ex = Record.Exception(ScaleGuard.AssertDefault);

            Assert.NotNull(ex);
        }
    }
}
