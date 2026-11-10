using Avalonia;
using Avalonia.Headless;
using Foreman.Mac;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Foreman.Mac.UiTests.TestAppBuilder))]
//Parallel collections race Dispatcher's static UI-thread slot against each [AvaloniaFact]'s teardown/rebuild.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Foreman.Mac.UiTests {
    public static class TestAppBuilder {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
