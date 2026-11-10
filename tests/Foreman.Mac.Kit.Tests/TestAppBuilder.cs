using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Foreman.Mac.Kit.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Foreman.Mac.Kit.Tests {
    public static class TestAppBuilder {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
