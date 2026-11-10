using Avalonia.Threading;
using Foreman.Mac;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    public sealed class SettingsSession {
        private static readonly TimeSpan CoalesceDelay = TimeSpan.FromMilliseconds(300);

        private static readonly HashSet<string> SolverAffectingProperties = [
            nameof(ViewModels.SettingsViewModel.AssemblerSelectorStyleIndex),
            nameof(ViewModels.SettingsViewModel.ShowProductivityBonusOnAllChecked),
            "EnabledObjects",
            "PresetSwitch",
        ];

        private readonly AppSettings settings;
        private readonly SettingsService store;
        private readonly App app;
        private readonly Func<Task> resolve;
        private readonly Func<ThemeMode, Task> applyTheme;
        private readonly Action<double> applyScale;

        private DispatcherTimer? saveTimer;
        private DispatcherTimer? resolveTimer;

        public SettingsSession(AppSettings settings, SettingsService store, App app, Func<Task> resolve,
            Func<ThemeMode, Task> applyTheme, Action<double> applyScale) {
            this.settings = settings;
            this.store = store;
            this.app = app;
            this.resolve = resolve;
            this.applyTheme = applyTheme;
            this.applyScale = applyScale;
        }

        internal App App => app;
        internal int SaveCallCount { get; private set; }
        internal int ResolveCallCount { get; private set; }

        public void Apply(string propertyName) {
            if (propertyName == nameof(ViewModels.SettingsViewModel.FlagDarkMode))
                Async.Fire(applyTheme(settings.FlagDarkMode), nameof(applyTheme));
            else if (propertyName == nameof(ViewModels.SettingsViewModel.UiScaleLabel))
                applyScale(settings.UiScale);

            saveTimer = Restart(saveTimer, OnSaveTimerElapsed);
            if (SolverAffectingProperties.Contains(propertyName))
                resolveTimer = Restart(resolveTimer, OnResolveTimerElapsed);
        }

        //DispatcherTimer, not Task.Delay: a delay outliving its window fires on another test's UI thread
        private static DispatcherTimer Restart(DispatcherTimer? timer, EventHandler onElapsed) {
            if (timer is null) {
                timer = new DispatcherTimer { Interval = CoalesceDelay };
                timer.Tick += onElapsed;
            }
            timer.Stop();
            timer.Start();
            return timer;
        }

        private void OnSaveTimerElapsed(object? sender, EventArgs e) {
            saveTimer!.Stop();
            Async.Fire(RunSaveAsync(), nameof(RunSaveAsync));
        }

        private void OnResolveTimerElapsed(object? sender, EventArgs e) {
            resolveTimer!.Stop();
            Async.Fire(RunResolveAsync(), nameof(RunResolveAsync));
        }

        private Task RunSaveAsync() {
            store.Save(settings);
            SaveCallCount++;
            return Task.CompletedTask;
        }

        private async Task RunResolveAsync() {
            await resolve().ConfigureAwait(true);
            ResolveCallCount++;
        }

        public async Task FlushAsync() {
            if (saveTimer is { IsEnabled: true } save) {
                save.Stop();
                await RunSaveAsync().ConfigureAwait(true);
            }
            if (resolveTimer is { IsEnabled: true } solve) {
                solve.Stop();
                await RunResolveAsync().ConfigureAwait(true);
            }
        }
    }
}
