using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal static class ShellHost {
        private static NativeMenuItem[] noWindowItems = [];
        private static NativeMenuItem openRecentItem = new("Open Recent");

        public static ShellContext? Context { get; set; }

        public static bool HasLiveMainWindow => Context?.Window is { IsClosed: false };

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
            bool actionable = WelcomeLauncher.Current?.ActionsEnabled ?? true;
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

        internal static async Task<MainWindow?> LiveWindowAsync() {
            if (MainWindow.ActiveWindow is { IsClosed: false } live)
                return live;
            if (Context is not { } context)
                return null;
            if (WelcomeLauncher.Current is { } welcome) {
                if (!await welcome.NewGraphFromMenuAsync().ConfigureAwait(true))
                    return null;
            } else
                await WelcomeLauncher.NewGraphAsync(context).ConfigureAwait(true);
            return context.Window;
        }

        private static IActivatableLifetime? Lifetime => Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;

        internal static Action HideApplication { get; set; } = () => Lifetime?.TryEnterBackground();

        internal static void HideForeman() => HideApplication();

        internal static async Task ReopenAsync() {
            if (Context?.Window is { IsClosed: false } live) {
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
