using System;
using System.Linq;
using Avalonia;
using Foreman.DataCaching;

namespace Foreman.Mac {
    public static class Program {
        [STAThread]
        public static void Main(string[] args) {
            ErrorLogging.ClearLog();
            App.GalleryMode = args.Contains("--gallery");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                //Without this, Avalonia appends its own Hide/Hide Others/Show All/Quit on top of ours, and Quit skips our unsaved-changes close check.
                .With(new MacOSPlatformOptions { DisableDefaultApplicationMenuItems = true })
                .WithInterFont()
                .LogToTrace();
    }
}
