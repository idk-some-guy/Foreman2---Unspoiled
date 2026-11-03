using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Views;
using Foreman.Mac.ViewModels;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal static class WelcomeLauncher {
        public static WelcomeWindow? Current { get; private set; }

        public static WelcomeWindow Show(ShellContext context, Preset? initialPreset = null) {
            if (Current is { } existing) {
                existing.Show();
                existing.Activate();
                return existing;
            }

            if (initialPreset is null && !context.CacheLoaded)
                initialPreset = CurrentPreset(context);
            WelcomeWindow? window = null;
            WelcomeHost host = BuildHost(context, () => window, initialPreset);
            window = new WelcomeWindow(host);
            Current = window;
            ShellHost.Sync();
            window.ViewModel.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(WelcomeViewModel.ActionsEnabled) && window.ViewModel.ActionsEnabled)
                    Async.Fire(ShellHost.OpenPendingFilesAsync(), nameof(ShellHost.OpenPendingFilesAsync));
            };
            window.Closed += (_, _) => {
                if (ReferenceEquals(Current, window))
                    Current = null;
                ShellHost.Sync();
            };
            window.Show();
            Async.Fire(ShellHost.OpenPendingFilesAsync(), nameof(ShellHost.OpenPendingFilesAsync));
            return window;
        }

        internal static Func<Preset, Task<DataCache?>>? CacheLoaderOverride { get; set; }

        internal static WelcomeHost BuildHost(ShellContext context, Func<WelcomeWindow?> welcome, Preset? initialPreset = null) => new() {
            InitialPreset = initialPreset,
            Library = context.Library,
            Settings = context.Settings,
            SaveSettings = () => context.SettingsService?.Save(context.Settings),
            InstalledPresets = () => context.PresetResolver?.BuildPresetList(context.Settings.CurrentPresetName) ?? [],
            LoadPreset = (preset, progress, ct) => LoadAsync(context, preset, progress, ct),
            NewGraph = () => NewGraphAsync(context),
            OpenFile = () => OpenFileAsync(context),
            OpenPath = path => OpenPathAsync(context, path),
            OpenGraph = (row, preset) => OpenGraphAsync(context, row, preset),
            ExportGraph = async row => {
                if (welcome() is not { } owner)
                    return;
                string suggested = row.Name.Replace('/', '-').Replace(':', '-') + ".fjson";
                if (await MainWindow.PickExportPathAsync(owner, suggested).ConfigureAwait(true) is string path)
                    Utf8File.WriteAllText(path, row.Data);
            },
            RatePresets = async row => {
                GraphViewerSaveDocument? document = GraphSaveCodec.ReadViewer(row.Data);
                if (document is null || context.PresetResolver is null)
                    return [];
                return await MainWindow.RatePresetsAsync(context.PresetResolver, context.Settings.CurrentPresetName, document).ConfigureAwait(true);
            },
            OpenRecentMenu = (open, clear) => MainWindow.BuildOpenRecentMenu(context.Library, context.Settings, context.PresetResolver, open, clear),
            ClearOpenRecent = () => ShellHost.ClearOpenRecent(context),
            HasOpenGraph = () => context.Window is { IsVisible: true } && context.Library.OpenGraphId is not null,
        };

        private static Preset? CurrentPreset(ShellContext context) {
            try {
                return context.PresetResolver?.Resolve(context.Settings.CurrentPresetName);
            } catch (DefaultPresetUnavailableException) {
                return null;
            }
        }

        internal static async Task<bool> EnsureLoadedAsync(ShellContext context) {
            if (context.CacheLoaded)
                return true;
            bool created = Current is null;
            if (created && CurrentPreset(context) is null)
                return false;
            using IDisposable busy = ShellHost.HoldBusy();
            WelcomeWindow welcome = Show(context);
            await welcome.ViewModel.LoadTask.ConfigureAwait(true);
            if (!context.CacheLoaded)
                return false;
            if (created && ReferenceEquals(Current, welcome))
                welcome.Close();
            return true;
        }

        internal static async Task<bool> NewGraphAsync(ShellContext context) {
            if (!await EnsureLoadedAsync(context).ConfigureAwait(true))
                return false;
            MainWindow window = EnsureWindow(context);
            await window.NewGraphAsync().ConfigureAwait(true);
            window.Activate();
            return true;
        }

        internal static async Task<bool> OpenFileAsync(ShellContext context) {
            if (!await EnsureLoadedAsync(context).ConfigureAwait(true))
                return false;
            MainWindow window = EnsureWindow(context);
            Guid? before = context.Library.OpenGraphId;
            await window.OpenGraphFileAsync().ConfigureAwait(true);
            bool opened = context.Library.OpenGraphId is not null && context.Library.OpenGraphId != before;
            if (opened)
                window.Activate();
            return opened;
        }

        internal static async Task<bool> OpenPathAsync(ShellContext context, string path) {
            if (!await EnsureLoadedAsync(context).ConfigureAwait(true))
                return false;
            string? json = null;
            WelcomeWindow? welcome = Current;
            if (welcome is not null && (json = await MainWindow.ReadGraphFileAsync(welcome, path).ConfigureAwait(true)) is null)
                return false;
            bool created = welcome is not null && context.Window is not { IsClosed: false };
            MainWindow window = EnsureWindow(context, deferShow: created);
            if (welcome is not null)
                window.AlertOwner = welcome;
            Guid? before = context.Library.OpenGraphId;
            if (json is null)
                await window.OpenFjsonPathAsync(path).ConfigureAwait(true);
            else
                await window.OpenFjsonAsync(json, path).ConfigureAwait(true);
            bool opened = context.Library.OpenGraphId is not null && context.Library.OpenGraphId != before;
            window.AlertOwner = null;
            if (opened) {
                window.RevealDeferred();
                window.Activate();
            } else if (created) {
                context.Window = null;
                window.Close();
            }
            return opened;
        }

        internal static async Task<bool> OpenGraphAsync(ShellContext context, GraphRow row, Preset? preset) {
            if (!await EnsureLoadedAsync(context).ConfigureAwait(true))
                return false;
            MainWindow window = EnsureWindow(context);
            await window.OpenLibraryGraphAsync(row.Id, preset).ConfigureAwait(true);
            bool opened = context.Library.OpenGraphId == row.Id;
            if (opened)
                window.Activate();
            return opened;
        }

        private static async Task<bool> LoadAsync(ShellContext context, Preset preset, IProgress<KeyValuePair<int, string>> progress, System.Threading.CancellationToken ct) {
            if (context.LoadTask is { IsCompleted: false } running && context.LoadingPresetName == preset.Name) {
                try {
                    return await running.WaitAsync(ct).ConfigureAwait(true);
                } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                    return false;
                }
            }
            int generation = ++context.LoadGeneration;
            Task<bool> load = LoadCoreAsync(context, preset, generation, progress, ct);
            context.LoadTask = load;
            context.LoadingPresetName = preset.Name;
            try {
                return await load.ConfigureAwait(true);
            } finally {
                ShellHost.Sync();
            }
        }

        private static async Task<bool> LoadCoreAsync(ShellContext context, Preset preset, int generation, IProgress<KeyValuePair<int, string>> progress, System.Threading.CancellationToken ct) {
            DataCache? cache;
            try {
                if (CacheLoaderOverride is { } loader)
                    cache = await loader(preset).ConfigureAwait(false);
                else {
                    cache = new DataCache(context.Settings.UseRecipeBWfilters);
                    await cache.LoadAllData(preset, progress, cancellationToken: ct).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, $"Failed to load preset '{preset.Name}'");
                return false;
            }
            if (cache is null || context.LoadGeneration != generation)
                return false;
            context.Cache = cache;
            context.CacheLoaded = true;
            if (context.Window is { IsClosed: false } window)
                window.PendingCache = cache;
            return true;
        }

        internal static MainWindow EnsureWindow(ShellContext context, bool deferShow = false) {
            if (context.Window is { IsClosed: false } existing)
                return existing;

            MainWindow window = ShellBootstrapper.CreateWindow(context, deferShow);
            context.Window = window;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = window;
            return window;
        }
    }
}
