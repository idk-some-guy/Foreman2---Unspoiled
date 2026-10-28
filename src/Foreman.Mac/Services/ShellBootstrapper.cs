using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Views;
using Foreman.Models.Solver;
using System;
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

        // warmSolver is fired right after the window shows and is never awaited: the first HiGHS
        // solve cold-loads its native dylib, and doing that on a background thread here beats
        // paying for it on the UI thread during the user's first UpdateNodeValues
        // (perf-packaging-reference.md §1c, docs/solver-eval.md).
        internal static async Task<MainWindow?> BootAsync(
            SettingsService settingsService,
            AppSettings settings,
            PresetResolver presetResolver,
            Func<Preset, Preset?, bool, Task<(DataCache Cache, Preset Preset)?>> loadPreset,
            Func<Window, string, string, Task> showWarning,
            Action fatalExit,
            Func<Task> warmSolver) {
            var mainWindow = new MainWindow();
            mainWindow.Show();
            _ = warmSolver(); //unobserved on purpose: the real SolverWarmup.RunAsync already catches and swallows its own exceptions internally, so this Task can never fault

            Preset? preset = await ResolveOrWarnAsync(presetResolver, settings.CurrentPresetName, mainWindow, showWarning).ConfigureAwait(true);
            if (preset is null) {
                fatalExit();
                return null;
            }

            (DataCache Cache, Preset Preset)? loaded = await loadPreset(preset, TryResolveDefault(presetResolver), settings.UseRecipeBWfilters).ConfigureAwait(true);
            if (loaded is not (DataCache cache, Preset loadedPreset)) {
                fatalExit();
                return null;
            }

            settings.CurrentPresetName = loadedPreset.Name;
            mainWindow.DataCache = cache;
            mainWindow.Settings = settings;
            mainWindow.PresetResolver = presetResolver;
            mainWindow.SettingsService = settingsService;
            mainWindow.Library = new GraphLibraryService(new GraphLibraryStore(settingsService.DatabasePath), TimeProvider.System);
            mainWindow.ApplyLoadedSettings();
            mainWindow.Closing += (_, _) => settingsService.Save(settings);
            return mainWindow;
        }

        private static async Task<Preset?> ResolveOrWarnAsync(
            PresetResolver presetResolver, string? currentPresetName, MainWindow owner, Func<Window, string, string, Task> showWarning) {
            try {
                return presetResolver.Resolve(currentPresetName);
            } catch (DefaultPresetUnavailableException ex) {
                await showWarning(owner, string.Empty, ex.Message).ConfigureAwait(true);
                return null;
            }
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
