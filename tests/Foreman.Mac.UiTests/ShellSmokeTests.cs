using Avalonia.Headless.XUnit;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class ShellSmokeTests {
        [AvaloniaFact]
        public void MainWindow_Opens_WithTitle() {
            var window = new MainWindow();
            window.Show();

            Assert.Equal("Foreman 2", window.Title);
            Assert.True(window.IsVisible);
        }
    }
}
