using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal static class ShellHost {
        private static NativeMenuItem[] noWindowItems = [];
        private static NativeMenuItem openRecentItem = new("Open Recent");

        public static ShellContext? Context { get; set; }

        public static bool HasLiveMainWindow => Context?.Window is { IsClosed: false, ShowDeferred: false };

        internal static IReadOnlyList<NativeMenuItemBase> BuildNoWindowItems() {
            NativeMenuItem Item(string header, Func<ShellContext, Task> action, KeyGesture gesture) => new(header) {
                Command = new MainWindow.DeferredMenuCommand(new RelayCommand(() => {
                    if (Context is { } context)
                        Async.Fire(action(context), header);
                })),
                Gesture = gesture,
            };

            openRecentItem = new NativeMenuItem("Open Recent") { Menu = [] };
            NativeMenuItem[] items = [
                Item("New Graph", NewGraphAsync, new KeyGesture(Key.N, PlatformModifiers.Primary)),
                Item("Open…", OpenFileAsync, new KeyGesture(Key.O, PlatformModifiers.Primary)),
                openRecentItem,
                Item("Welcome to Foreman", context => { WelcomeLauncher.Show(context); return Task.CompletedTask; }, new KeyGesture(Key.D1, PlatformModifiers.Primary | KeyModifiers.Shift)),
            ];
            noWindowItems = items;
            Sync();
            return [.. items, new NativeMenuItemSeparator()];
        }

        internal static void Sync() {
            bool visible = Context is not null && !HasLiveMainWindow && WelcomeLauncher.Current is null;
            bool actionable = (WelcomeLauncher.Current?.ActionsEnabled ?? true) && Context is not { Loading: true };
            foreach (NativeMenuItem item in noWindowItems) {
                item.IsVisible = visible;
                item.IsEnabled = actionable || item.Header == "Welcome to Foreman";
            }
            if (Context is { } context) {
                openRecentItem.Menu = MainWindow.BuildOpenRecentMenu(context.Library, context.Settings, context.PresetResolver,
                    id => OpenRecent(context, id), () => ClearOpenRecent(context));
                openRecentItem.IsEnabled = actionable && context.Library.All().Count > 0;
            }
        }

        private static async Task NewGraphAsync(ShellContext context) {
            if (WelcomeLauncher.Current is { } welcome)
                await welcome.NewGraphFromMenuAsync().ConfigureAwait(true);
            else
                await WelcomeLauncher.NewGraphAsync(context).ConfigureAwait(true);
        }

        private static async Task OpenFileAsync(ShellContext context) {
            if (WelcomeLauncher.Current is { } welcome)
                await welcome.OpenFileFromMenuAsync().ConfigureAwait(true);
            else
                await WelcomeLauncher.OpenFileAsync(context).ConfigureAwait(true);
        }

        private static void OpenRecent(ShellContext context, Guid id) {
            if (context.Library.Find(id) is not { } row)
                return;
            if (WelcomeLauncher.Current is { } welcome)
                Async.Fire(welcome.OpenGraphFromMenuAsync(row), nameof(WelcomeWindow.OpenGraphFromMenuAsync));
            else
                Async.Fire(WelcomeLauncher.OpenGraphAsync(context, row, null), nameof(WelcomeLauncher.OpenGraphAsync));
        }

        internal static void ClearOpenRecent(ShellContext context) {
            context.Settings.OpenRecentClearedAt = context.Library.Now;
            context.SettingsService?.Save(context.Settings);
            Sync();
        }

        private static readonly List<string> pendingFiles = [];

        private static int busyDepth;
        private static int busyGeneration;

        internal static bool Busy => busyDepth > 0;

        internal static IDisposable HoldBusy() {
            busyDepth++;
            return new BusyScope(busyGeneration);
        }

        internal static void ResetQueue() {
            pendingFiles.Clear();
            busyDepth = 0;
            busyGeneration++;
        }

        private sealed class BusyScope(int generation) : IDisposable {
            private bool disposed;

            public void Dispose() {
                if (disposed)
                    return;
                disposed = true;
                if (generation != busyGeneration)
                    return;
                busyDepth--;
                if (busyDepth == 0 && pendingFiles.Count > 0)
                    Dispatcher.UIThread.Post(() => Async.Fire(OpenPendingFilesAsync(), nameof(OpenPendingFilesAsync)));
            }
        }

        private static bool ShellReady => Context is { CacheLoaded: true } && !Busy && (WelcomeLauncher.Current?.CanOpenFiles ?? true);

        internal static async Task OpenFilesAsync(IEnumerable<string> paths) {
            pendingFiles.AddRange(paths.Where(path => string.Equals(Path.GetExtension(path), ".fjson", StringComparison.OrdinalIgnoreCase)));
            if (!ShellReady)
                return;
            using IDisposable busy = HoldBusy();
            while (pendingFiles.Count > 0) {
                string path = pendingFiles[0];
                pendingFiles.RemoveAt(0);
                if (WelcomeLauncher.Current is { } welcome)
                    await welcome.OpenPathFromMenuAsync(path).ConfigureAwait(true);
                else
                    await WelcomeLauncher.OpenPathAsync(Context!, path).ConfigureAwait(true);
            }
        }

        internal static Task OpenPendingFilesAsync() => OpenFilesAsync([]);

        internal static async Task<MainWindow?> LiveWindowAsync() {
            if (MainWindow.ActiveWindow is { IsClosed: false, ShowDeferred: false } live)
                return live;
            if (Context is not { } context)
                return null;
            if (WelcomeLauncher.Current is { } welcome) {
                if (!await welcome.NewGraphFromMenuAsync().ConfigureAwait(true))
                    return null;
            } else if (!await WelcomeLauncher.NewGraphAsync(context).ConfigureAwait(true))
                return null;
            return context.Window;
        }

        private static IActivatableLifetime? Lifetime => Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;

        internal static Action HideApplication { get; set; } = () => Lifetime?.TryEnterBackground();

        internal static void HideForeman() => HideApplication();

        internal static async Task ReopenAsync() {
            if (Context?.Window is { IsClosed: false, ShowDeferred: false } live) {
                if (!live.IsVisible)
                    live.Show();
                live.Activate();
                return;
            }
            if (WelcomeLauncher.Current is { } welcome) {
                welcome.Show();
                welcome.Activate();
                return;
            }
            if (Context is not { } context)
                return;
            if (context.Settings.ShowWelcomeAtLaunch)
                WelcomeLauncher.Show(context);
            else
                await WelcomeLauncher.NewGraphAsync(context).ConfigureAwait(true);
        }
    }
}
