using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using System;
using System.Threading.Tasks;

namespace Foreman.Mac {
    public partial class App : Application {
        //Set from Program.Main before StartWithClassicDesktopLifetime runs, ahead of this instance even
        //existing - a dev-only switch to the gallery boot path (`--gallery`) instead of the normal shell.
        public static bool GalleryMode { get; set; }

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted() {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                BootAndShow(desktop);

            base.OnFrameworkInitializationCompleted();
        }

        //The load-window self-closes before MainWindow is assigned; without this, the framework
        //would see zero open windows and shut down before the shell ever appears.
        private async void BootAndShow(IClassicDesktopStyleApplicationLifetime desktop) {
            if (GalleryMode)
                await BootGalleryAsync(desktop).ConfigureAwait(true);
            else
                await BootAndShowAsync(desktop, new SettingsService(), ShellBootstrapper.BootAsync, ShellBootstrapper.DefaultFatalExit).ConfigureAwait(true);
        }

        //Mirrors BootAndShowAsync's shape (preset resolve -> real DataCache load -> assign window) but skips
        //straight to GalleryWindow instead of MainWindow - same corrupt-preset/fatal-exit fallback, no
        //settings persistence since the gallery never mutates AppSettings.
        internal async Task BootGalleryAsync(IClassicDesktopStyleApplicationLifetime desktop) {
            try {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var settingsService = new SettingsService();
                AppSettings settings = settingsService.Load();
                ApplyTheme(settings.FlagDarkMode);

                Preset preset = new PresetResolver().Resolve(settings.CurrentPresetName);
                DataCache? cache = await ShellBootstrapper.LoadPresetAsync(preset, settings.UseRecipeBWfilters).ConfigureAwait(true);
                if (cache is null) {
                    ShellBootstrapper.DefaultFatalExit();
                    return;
                }

                var window = new GalleryWindow(cache, settings);
                window.Show();
                desktop.MainWindow = window;
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Unhandled exception during gallery boot");
                ShellBootstrapper.DefaultFatalExit();
            }
        }

        //A failure that escapes ShellBootstrapper.BootAsync (anything beyond the corrupt-preset
        //paths it already handles) would otherwise kill the process with no diagnostic; catch,
        //log, and exit through the same fatal path BootAsync itself uses.
        internal async Task BootAndShowAsync(
            IClassicDesktopStyleApplicationLifetime desktop,
            SettingsService settingsService,
            Func<SettingsService, AppSettings, Task<MainWindow?>> bootShell,
            Action fatalExit) {
            try {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var settings = settingsService.Load();
                ApplyTheme(settings.FlagDarkMode);
                var window = await bootShell(settingsService, settings).ConfigureAwait(true);
                if (window is not null) {
                    desktop.MainWindow = window;
                    desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                }
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Unhandled exception during startup boot");
                fatalExit();
            }
        }

        public void ApplyTheme(ThemeMode mode) {
            RequestedThemeVariant = mode switch {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
