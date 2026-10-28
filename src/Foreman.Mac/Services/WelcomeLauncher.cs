using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Views;
using Foreman.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal static class WelcomeLauncher {
        public static WelcomeWindow? Current { get; private set; }

        public static WelcomeWindow Show(ShellContext context) {
            if (Current is { } existing) {
                existing.Show();
                existing.Activate();
                return existing;
            }

            WelcomeWindow? window = null;
            WelcomeHost host = BuildHost(context, () => window);
            window = new WelcomeWindow(host);
            Current = window;
            ShellHost.Sync();
            window.Closed += (_, _) => {
                if (ReferenceEquals(Current, window))
                    Current = null;
                ShellHost.Sync();
            };
            window.Show();
            return window;
        }

        internal static Func<Preset, Task<DataCache?>>? CacheLoaderOverride { get; set; }

        internal static WelcomeHost BuildHost(ShellContext context, Func<WelcomeWindow?> welcome) => new() {
            Library = context.Library,
            Settings = context.Settings,
            SaveSettings = () => context.SettingsService?.Save(context.Settings),
            InstalledPresets = () => context.PresetResolver?.BuildPresetList(context.Settings.CurrentPresetName) ?? [],
            LoadPreset = (preset, progress, ct) => LoadAsync(context, preset, progress, ct),
            NewGraph = () => NewGraphAsync(context),
            OpenFile = () => OpenFileAsync(context),
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

        internal static async Task<bool> NewGraphAsync(ShellContext context) {
            MainWindow window = EnsureWindow(context);
            await window.NewGraphAsync().ConfigureAwait(true);
            window.Activate();
            return true;
        }

        internal static async Task<bool> OpenFileAsync(ShellContext context) {
            MainWindow window = EnsureWindow(context);
            Guid? before = context.Library.OpenGraphId;
            await window.OpenGraphFileAsync().ConfigureAwait(true);
            bool opened = context.Library.OpenGraphId is not null && context.Library.OpenGraphId != before;
            if (opened)
                window.Activate();
            return opened;
        }

        internal static async Task<bool> OpenGraphAsync(ShellContext context, GraphRow row, Preset? preset) {
            MainWindow window = EnsureWindow(context);
            await window.OpenLibraryGraphAsync(row.Id, preset).ConfigureAwait(true);
            bool opened = context.Library.OpenGraphId == row.Id;
            if (opened)
                window.Activate();
            return opened;
        }

        private static async Task<bool> LoadAsync(ShellContext context, Preset preset, IProgress<KeyValuePair<int, string>> progress, System.Threading.CancellationToken ct) {
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
            if (cache is null)
                return false;
            context.Cache = cache;
            if (context.Window is { IsClosed: false } window)
                window.PendingCache = cache;
            return true;
        }

        internal static MainWindow EnsureWindow(ShellContext context) {
            if (context.Window is { IsClosed: false } existing)
                return existing;

            MainWindow window = ShellBootstrapper.CreateWindow(context);
            context.Window = window;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = window;
            return window;
        }
    }
}
