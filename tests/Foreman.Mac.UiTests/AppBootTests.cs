using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class AppBootTests {
        private static string NewTempHome() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(home);
            return home;
        }

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [AvaloniaFact]
        public async Task BootAndShowAsync_BootShellThrows_LogsAndRunsFatalExitInsteadOfPropagating() {
            var app = (App)Application.Current!;
            var desktop = new ClassicDesktopStyleApplicationLifetime();
            var settingsService = new SettingsService(NewTempHome());
            using var logIsolation = ErrorLogging.UseIsolatedLogDirectory(NewTempDir());
            ErrorLogging.ClearLog();
            bool fatalExitCalled = false;

            await app.BootAndShowAsync(
                desktop,
                settingsService,
                (_, _) => throw new InvalidOperationException("boom"),
                () => fatalExitCalled = true);

            Assert.True(fatalExitCalled);
            Assert.Null(desktop.MainWindow);
            Assert.Contains("boom", File.ReadAllText(ErrorLogging.LogFilePath));
        }

        [AvaloniaFact]
        public void ApplicationName_IsSetToForeman2() {
            Assert.Equal("Foreman 2", Application.Current!.Name);
        }

        [AvaloniaFact]
        public async Task BootAndShowAsync_BootShellSucceeds_AssignsMainWindowAndSwitchesShutdownMode() {
            var app = (App)Application.Current!;
            var desktop = new ClassicDesktopStyleApplicationLifetime();
            var settingsService = new SettingsService(NewTempHome());
            var window = new MainWindow();
            bool fatalExitCalled = false;

            await app.BootAndShowAsync(
                desktop,
                settingsService,
                (_, _) => Task.FromResult<MainWindow?>(window),
                () => fatalExitCalled = true);

            Assert.False(fatalExitCalled);
            Assert.Same(window, desktop.MainWindow);
            Assert.Equal(ShutdownMode.OnMainWindowClose, desktop.ShutdownMode);
        }
    }
}
