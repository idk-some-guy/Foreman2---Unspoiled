using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Views;
using Foreman.Models.Solver;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    public static class ShellBootstrapper {
        public static Task<MainWindow?> BootAsync(SettingsService settingsService, AppSettings settings) =>
            BootAsync(settingsService, settings, new PresetResolver(), LoadPresetAsync, Dialogs.ShowWarningAsync, DefaultFatalExit);

        internal static Task<MainWindow?> BootAsync(
            SettingsService settingsService,
            AppSettings settings,
            PresetResolver presetResolver,
            Func<Preset, Preset?, bool, Task<(DataCache Cache, Preset Preset)?>> loadPreset,
            Func<Window, string, string, Task> showWarning,
            Action fatalExit) =>
            BootAsync(settingsService, settings, presetResolver, loadPreset, showWarning, fatalExit, SolverWarmup.RunAsync);

        // warmSolver is fired at boot and is never awaited: the first HiGHS
        // solve cold-loads its native dylib, and doing that on a background thread here beats
        // paying for it on the UI thread during the user's first UpdateNodeValues
        // (perf-packaging-reference.md §1c).
        internal static async Task<MainWindow?> BootAsync(
            SettingsService settingsService,
            AppSettings settings,
            PresetResolver presetResolver,
            Func<Preset, Preset?, bool, Task<(DataCache Cache, Preset Preset)?>> loadPreset,
            Func<Window, string, string, Task> showWarning,
            Action fatalExit,
            Func<Task> warmSolver) {
            _ = warmSolver(); //unobserved on purpose: the real SolverWarmup.RunAsync already catches and swallows its own exceptions internally, so this Task can never fault

            Preset? preset = await ResolveOrWarnAsync(presetResolver, settings.CurrentPresetName, showWarning).ConfigureAwait(true);
            if (preset is null) {
                fatalExit();
                return null;
            }

            var library = new GraphLibraryService(new GraphLibraryStore(settingsService.DatabasePath), TimeProvider.System);
            if (settings.ShowWelcomeAtLaunch) {
                settings.CurrentPresetName = preset.Name;
                var context = new ShellContext(library, settings, new DataCache(settings.UseRecipeBWfilters), settingsService, presetResolver) { CacheLoaded = false };
                ShellHost.Context = context;
                WelcomeLauncher.Show(context, preset);
                return null;
            }

            GraphRow? recent = library.All() is [var latest, ..] ? latest : null;
            Preset launchPreset = recent is null ? preset : presetResolver.BuildPresetList(settings.CurrentPresetName).FirstOrDefault(p => p.Name == recent.PresetName) ?? preset;
            var mainWindow = new MainWindow();
            mainWindow.Show();
            (DataCache Cache, Preset Preset)? loaded = await loadPreset(launchPreset, TryResolveDefault(presetResolver), settings.UseRecipeBWfilters).ConfigureAwait(true);
            if (loaded is not (DataCache cache, Preset loadedPreset)) {
                fatalExit();
                return null;
            }

            settings.CurrentPresetName = loadedPreset.Name;
            using (ShellHost.HoldBusy()) {
                Attach(mainWindow, new ShellContext(library, settings, cache, settingsService, presetResolver));
                if (recent is not null)
                    await mainWindow.OpenLibraryGraphAsync(recent.Id).ConfigureAwait(true);
                if (library.OpenGraphId is null)
                    await mainWindow.NewGraphAsync().ConfigureAwait(true);
            }
            await ShellHost.OpenPendingFilesAsync().ConfigureAwait(true);
            return mainWindow;
        }

        internal static MainWindow CreateWindow(ShellContext context, bool deferShow = false) {
            var mainWindow = new MainWindow { ShowDeferred = deferShow };
            if (!deferShow)
                mainWindow.Show();
            Attach(mainWindow, context);
            return mainWindow;
        }

        private static void Attach(MainWindow mainWindow, ShellContext context) {
            mainWindow.DataCache = context.Cache;
            mainWindow.Settings = context.Settings;
            mainWindow.PresetResolver = context.PresetResolver;
            mainWindow.SettingsService = context.SettingsService;
            mainWindow.Library = context.Library;
            mainWindow.ApplyLoadedSettings();
            if (context.SettingsService is { } settingsService)
                mainWindow.Closing += (_, _) => settingsService.Save(context.Settings);
            context.Window = mainWindow;
            mainWindow.ShellContext = context;
            ShellHost.Context = context;
            ShellHost.Sync();
        }

        internal static async Task<Preset?> ResolveOrWarnAsync(
            PresetResolver presetResolver, string? currentPresetName, Func<Window, string, string, Task> showWarning) {
            Preset? preset = TryResolve(presetResolver, currentPresetName, out DefaultPresetUnavailableException? failure);
            if (failure is null)
                return preset;
            await WarnUnavailableAsync(failure, showWarning).ConfigureAwait(true);
            return null;
        }

        internal static Preset? TryResolve(PresetResolver presetResolver, string? currentPresetName, out DefaultPresetUnavailableException? failure) {
            failure = null;
            try {
                return presetResolver.Resolve(currentPresetName);
            } catch (DefaultPresetUnavailableException ex) {
                failure = ex;
                return null;
            }
        }

        internal static async Task WarnUnavailableAsync(DefaultPresetUnavailableException ex, Func<Window, string, string, Task> showWarning) {
            var owner = new MainWindow();
            owner.Show();
            await showWarning(owner, string.Empty, ex.Message).ConfigureAwait(true);
            owner.Close();
        }

        private static Preset? TryResolveDefault(PresetResolver presetResolver) {
            try {
                return presetResolver.Resolve(PresetResolver.DefaultPresetName);
            } catch (DefaultPresetUnavailableException) {
                return null;
            }
        }

        internal static async Task<(DataCache Cache, Preset Preset)?> LoadPresetAsync(Preset preset, Preset? defaultPreset, bool filterRecipes) {
            var dataLoadWindow = new DataLoadWindow(preset, filterRecipes, DataLoadMode.AtLaunch, defaultPreset);
            dataLoadWindow.Show();
            await dataLoadWindow.LoadTask.ConfigureAwait(true);
            return dataLoadWindow is { Outcome: DataLoadOutcome.Loaded, Result: DataCache cache } ? (cache, dataLoadWindow.LoadedPreset) : null;
        }

        internal static void DefaultFatalExit() {
            if (Application.Current?.ApplicationLifetime is IControlledApplicationLifetime lifetime)
                lifetime.Shutdown();
        }
    }
}
