using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Views;
using Foreman.Mac.ViewModels;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal static class WelcomeLauncher {
        public static WelcomeWindow? Current { get; private set; }

        internal static Func<Window, string, string, Task>? WarningOverride { get; set; }

        private static Func<Window, string, string, Task> Warning => WarningOverride ?? Dialogs.ShowWarningAsync;

        public static WelcomeWindow? Show(ShellContext context, Preset? initialPreset = null) {
            if (Current is { } existing) {
                existing.Show();
                existing.Activate();
                return existing;
            }

            initialPreset ??= RunningPick(context);
            if (initialPreset is null && !context.CacheLoaded && context.PresetResolver is { } resolver) {
                initialPreset = ShellBootstrapper.TryResolve(resolver, context.Settings.CurrentPresetName, out DefaultPresetUnavailableException? failure);
                if (failure is not null) {
                    if (!context.WarningPending)
                        Async.Fire(WarnOnceAsync(context, failure), nameof(ShellBootstrapper.WarnUnavailableAsync));
                    return null;
                }
            }
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

        private static Preset? RunningPick(ShellContext context) =>
            context.LoadTask is { IsCompleted: false } && context.LoadingPresetName is { } name
                ? context.PresetResolver?.BuildPresetList(context.Settings.CurrentPresetName).FirstOrDefault(p => p.Name == name)
                : null;

        private static async Task WarnOnceAsync(ShellContext context, DefaultPresetUnavailableException failure) {
            context.WarningPending = true;
            try {
                await ShellBootstrapper.WarnUnavailableAsync(failure, Warning).ConfigureAwait(true);
            } finally {
                context.WarningPending = false;
            }
        }

        private static async Task<Preset?> ResolveOrWarnOnceAsync(ShellContext context, PresetResolver resolver) {
            if (context.WarningPending)
                return null;
            context.WarningPending = true;
            try {
                return await ShellBootstrapper.ResolveOrWarnAsync(resolver, context.Settings.CurrentPresetName, Warning).ConfigureAwait(true);
            } finally {
                context.WarningPending = false;
            }
        }

        internal static async Task<bool> EnsureLoadedAsync(ShellContext context) {
            if (context.CacheLoaded)
                return true;
            bool created = Current is null;
            Preset? initial = null;
            if (created) {
                initial = RunningPick(context);
                if (initial is null && context.PresetResolver is { } resolver)
                    initial = await ResolveOrWarnOnceAsync(context, resolver).ConfigureAwait(true);
                if (initial is null)
                    return false;
            }
            using IDisposable busy = ShellHost.HoldBusy();
            WelcomeWindow? welcome = Show(context, initial);
            if (welcome is null)
                return false;
            bool closed = false;
            if (created)
                welcome.Closed += (_, _) => closed = true;
            await welcome.ViewModel.LoadTask.ConfigureAwait(true);
            if (!context.CacheLoaded || created && closed)
                return false;
            if (created && ReferenceEquals(Current, welcome))
                welcome.Close();
            return true;
        }

        internal static async Task<bool> NewGraphAsync(ShellContext context) {
            if (context.OpenPending)
                return false;
            if (!await EnsureLoadedAsync(context).ConfigureAwait(true))
                return false;
            MainWindow window = EnsureWindow(context);
            window.RevealDeferred();
            await window.NewGraphAsync().ConfigureAwait(true);
            window.Activate();
            return true;
        }

        internal static Task<bool> OpenFileAsync(ShellContext context) =>
            OpenOnWindowAsync(context, window => window.OpenGraphFileAsync(), (before, after) => after is not null && after != before);

        internal static async Task<bool> OpenPathAsync(ShellContext context, string path) {
            if (!await EnsureLoadedAsync(context).ConfigureAwait(true))
                return false;
            string? json = null;
            if (Current is { } welcome && (json = await MainWindow.ReadGraphFileAsync(welcome, path).ConfigureAwait(true)) is null)
                return false;
            return await OpenOnWindowAsync(context, window => json is null ? window.OpenFjsonPathAsync(path) : window.OpenFjsonAsync(json, path),
                (before, after) => after is not null && after != before).ConfigureAwait(true);
        }

        internal static Task<bool> OpenGraphAsync(ShellContext context, GraphRow row, Preset? preset) =>
            OpenOnWindowAsync(context, window => window.OpenLibraryGraphAsync(row.Id, preset), (_, after) => after == row.Id);

        internal static async Task<bool> OpenOnWindowAsync(ShellContext context, Func<MainWindow, Task> open, Func<Guid?, Guid?, bool> openedGraph) {
            if (context.OpenPending)
                return false;
            context.OpenPending = true;
            using IDisposable busy = ShellHost.HoldBusy();
            WelcomeWindow? welcome = null;
            EventHandler? welcomeClosed = null;
            try {
                if (!await EnsureLoadedAsync(context).ConfigureAwait(true))
                    return false;
                welcome = Current;
                bool created = context.Window is not { IsClosed: false };
                MainWindow window = EnsureWindow(context, deferShow: created);
                window.AlertOwner = welcome;
                if (welcome is not null) {
                    welcomeClosed = (_, _) => window.AlertOwner = null;
                    welcome.Closed += welcomeClosed;
                }
                Guid? before = context.Library.OpenGraphId;
                bool opened;
                try {
                    await open(window).ConfigureAwait(true);
                    opened = openedGraph(before, context.Library.OpenGraphId);
                } catch {
                    AbandonDeferred(context, window, created);
                    throw;
                } finally {
                    window.AlertOwner = null;
                }
                if (opened) {
                    window.RevealDeferred();
                    window.Activate();
                } else {
                    AbandonDeferred(context, window, created);
                }
                return opened;
            } finally {
                if (welcome is not null && welcomeClosed is not null)
                    welcome.Closed -= welcomeClosed;
                context.OpenPending = false;
            }
        }

        private static void AbandonDeferred(ShellContext context, MainWindow window, bool created) {
            if (!created)
                return;
            if (ReferenceEquals(context.Window, window))
                context.Window = null;
            window.CloseAbandoned();
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
            Dispatcher.UIThread.Post(() => {
                if (context.LoadGeneration != generation)
                    return;
                if (AlertOwnerFor(context) is { } owner)
                    OutdatedPresetAlert.Present(owner, preset, cache, Warning);
                else if (PresetExportFormat.IsOutdated(cache))
                    context.PendingOutdatedAlert = (preset, cache);
            }, DispatcherPriority.Background);
            return true;
        }

        private static Window? AlertOwnerFor(ShellContext context) =>
            Current is { IsVisible: true } welcome ? welcome : context.Window is { IsClosed: false, ShowDeferred: false } main ? main : null;

        internal static void PresentPending(ShellContext context) {
            if (context.PendingOutdatedAlert is not var (preset, cache) || AlertOwnerFor(context) is not { } owner)
                return;
            context.PendingOutdatedAlert = null;
            OutdatedPresetAlert.Present(owner, preset, cache, Warning);
        }

        internal static MainWindow EnsureWindow(ShellContext context, bool deferShow = false) {
            if (context.Window is { IsClosed: false } existing)
                return existing;

            MainWindow window = ShellBootstrapper.CreateWindow(context, deferShow);
            context.Window = window;
            PresentPending(context);
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = window;
            return window;
        }
    }
}
