using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Views;
using Foreman.Models.Solver;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    public static class ShellBootstrapper {
        public static Task<MainWindow?> BootAsync(SettingsService settingsService, AppSettings settings) =>
            BootAsync(settingsService, settings, new PresetResolver(), LoadPresetAsync, Dialogs.ShowWarningAsync, DefaultFatalExit);

        internal static Task<MainWindow?> BootAsync(
            SettingsService settingsService,
            AppSettings settings,
            PresetResolver presetResolver,
            Func<Preset, bool, Task<DataCache?>> loadPreset,
            Func<Window, string, string, Task> showWarning,
            Action fatalExit) =>
            BootAsync(settingsService, settings, presetResolver, loadPreset, showWarning, fatalExit, SolverWarmup.RunAsync);

        // warmSolver is fired right after the window shows and is never awaited: the first
        // Solver_t.CreateSolver("GLOP") call cold-loads OR-Tools' ~55MB native tree, and doing that
        // on a background thread here beats paying for it on the UI thread during the user's first
        // UpdateNodeValues (perf-packaging-reference.md §1c).
        internal static async Task<MainWindow?> BootAsync(
            SettingsService settingsService,
            AppSettings settings,
            PresetResolver presetResolver,
            Func<Preset, bool, Task<DataCache?>> loadPreset,
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

            settings.CurrentPresetName = preset.Name;
            DataCache? cache = await loadPreset(preset, settings.UseRecipeBWfilters).ConfigureAwait(true);

            if (cache is null) {
                await showWarning(mainWindow, string.Empty, string.Format(
                    CultureInfo.InvariantCulture,
                    "The current preset ({0}) is corrupt. Switching to the default preset ({1})",
                    preset.Name, PresetResolver.DefaultPresetName)).ConfigureAwait(true);

                Preset? defaultPreset = await ResolveOrWarnAsync(presetResolver, PresetResolver.DefaultPresetName, mainWindow, showWarning).ConfigureAwait(true);
                if (defaultPreset is null) {
                    fatalExit();
                    return null;
                }

                settings.CurrentPresetName = defaultPreset.Name;
                cache = await loadPreset(defaultPreset, settings.UseRecipeBWfilters).ConfigureAwait(true);

                if (cache is null) {
                    await showWarning(mainWindow, string.Empty, string.Format(
                        CultureInfo.InvariantCulture,
                        "The default preset ({0}) is corrupt. No Preset is loaded!",
                        defaultPreset.Name)).ConfigureAwait(true);
                    fatalExit();
                    return null;
                }
            }

            mainWindow.DataCache = cache;
            mainWindow.Settings = settings;
            mainWindow.PresetResolver = presetResolver;
            mainWindow.SettingsService = settingsService;
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

        internal static async Task<DataCache?> LoadPresetAsync(Preset preset, bool filterRecipes) {
            var dataLoadWindow = new DataLoadWindow(preset, filterRecipes);
            dataLoadWindow.Show();
            await dataLoadWindow.LoadTask.ConfigureAwait(true);
            return dataLoadWindow.Result;
        }

        internal static void DefaultFatalExit() {
            if (Application.Current?.ApplicationLifetime is IControlledApplicationLifetime lifetime)
                lifetime.Shutdown();
        }
    }
}
