using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Foreman.Mac.Kit.Tests.TestAppBuilder))]

namespace Foreman.Mac.Kit.Tests {
    public static class TestAppBuilder {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
