using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;

namespace Foreman.Mac {
    public partial class App : Application {
        //Set from Program.Main before StartWithClassicDesktopLifetime runs, ahead of this instance even
        //existing - a dev-only switch to the gallery boot path (`--gallery`) instead of the normal shell.
        public static bool GalleryMode { get; set; }

        public ThemeService Theme { get; private set; }

        //one hub per app: every window subscribes to it, ThemeService keeps the ThemeMode mapping
        public AppearanceHub Hub { get; private set; }

        //Rescaled is a static event: replace the previous App's handler or the list grows per test
        private static EventHandler? rescaledHandler;

        public App() {
            InstallAppearance(null);
        }

        //The OS colour event reaches the platform appearance leg, wired to the hub; a null argument takes
        //the production seam.
        [MemberNotNull(nameof(Hub), nameof(Theme))]
        internal void InstallAppearance(IPlatformAppearance? appearance) {
            Hub?.Dispose();

            Hub = new AppearanceHub(this, appearance ?? (IPlatformAppearance?)PlatformAppearance.TryCreate(this) ?? new NullPlatformAppearance());
            AppearanceHub.Current = Hub;
            Hub.Changed += (_, _) => TokenBridge.Refresh();
            Theme = new ThemeService(this);

            if (rescaledHandler is not null)
                WindowScaler.Rescaled -= rescaledHandler;
            rescaledHandler = (_, _) => Hub.Refresh();
            WindowScaler.Rescaled += rescaledHandler;
        }

        //AvaloniaNativeMenuExporter reads Application's NativeMenu exactly once, from its own constructor,
        //which AvaloniaNativePlatformExtensions.UseAvaloniaNative runs via AfterSetup - after Initialize()
        //but before OnFrameworkInitializationCompleted (AppBuilder.Setup). Setting it any later (MainWindow's
        //constructor, which only runs from OnFrameworkInitializationCompleted) is invisible to the exporter,
        //which falls back to its own "About Avalonia" menu and never looks again.
        public override void Initialize() {
            AvaloniaXamlLoader.Load(this);
            //The XAML load replaces the resources the hub wrote from the constructor.
            Hub.Refresh();
            NativeMenu.SetMenu(this, MainWindow.BuildAppMenu());
        }

        public override void OnFrameworkInitializationCompleted() {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                BootAndShow(desktop);
            if (!GalleryMode && this.TryGetFeature<IActivatableLifetime>() is { } activatable)
                activatable.Activated += (_, e) => {
                    if (e.Kind == ActivationKind.Reopen)
                        Async.Fire(ShellHost.ReopenAsync(), nameof(ShellHost.ReopenAsync));
                    else if (e is FileActivatedEventArgs files)
                        Async.Fire(ShellHost.OpenFilesAsync(files.Files.Select(f => f.TryGetLocalPath()).OfType<string>()), nameof(ShellHost.OpenFilesAsync));
                };

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
                (DataCache Cache, Preset Preset)? loaded = await ShellBootstrapper.LoadPresetAsync(preset, null, settings.UseRecipeBWfilters).ConfigureAwait(true);
                if (loaded is not (DataCache cache, _)) {
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
                if (window is not null)
                    desktop.MainWindow = window;
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, "Unhandled exception during startup boot");
                fatalExit();
            }
        }

        public void ApplyTheme(ThemeMode mode) => Theme.Apply(mode);

        //for environments without PlatformSettings
        private sealed class NullPlatformAppearance : IPlatformAppearance {
            public Color? AccentColor => null;
            public ColorContrastPreference ContrastPreference => ColorContrastPreference.NoPreference;
            public bool ReduceTransparency => false;
            public bool ReduceMotion => false;
            public PlatformThemeVariant ThemeVariant => PlatformThemeVariant.Light;
            public event EventHandler? Changed { add { } remove { } }
        }
    }
}
