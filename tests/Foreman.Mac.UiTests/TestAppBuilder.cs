using Avalonia;
using Avalonia.Headless;
using Foreman.Mac;

[assembly: AvaloniaTestApplication(typeof(Foreman.Mac.UiTests.TestAppBuilder))]

namespace Foreman.Mac.UiTests {
    public static class TestAppBuilder {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
