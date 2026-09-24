using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using KitProgressBar = Foreman.Mac.Kit.Controls.ProgressBar;

namespace Foreman.Mac.UiTests {
    public class DataLoadWindowTests {
        private const string VanillaPresetName = "Factorio 2.0 Vanilla";
        private const string SpaceAgePresetName = "Factorio 2.0.55 — Space Age";

        private static readonly Preset Vanilla = new(VanillaPresetName, true, true);
        private static readonly Preset SpaceAge = new(SpaceAgePresetName, false, false);

        private sealed class Loader {
            public readonly List<Preset> Calls = [];
            public Func<int, Preset, IProgress<KeyValuePair<int, string>>, CancellationToken, Task> Next = (_, _, _, _) => Task.CompletedTask;
            public Task Load(Preset preset, DataCache _, IProgress<KeyValuePair<int, string>> progress, CancellationToken ct) {
                Calls.Add(preset);
                return Next(Calls.Count, preset, progress, ct);
            }
        }

        private sealed class Alerts {
            public readonly List<(string Title, string Message)> Shown = [];
            public Task Show(Window _, string title, string message) {
                Shown.Add((title, message));
                return Task.CompletedTask;
            }
        }

        private static DataLoadWindow New(Preset preset, Loader loader, DataLoadMode mode = DataLoadMode.AfterLaunch, Preset? defaultPreset = null, Alerts? alerts = null,
                Func<TimeSpan>? clock = null, Func<TimeSpan, Action, IDisposable>? schedule = null) =>
            new(preset, filterRecipes: true, mode, defaultPreset, loader.Load, (alerts ?? new Alerts()).Show, clock, schedule);

        private sealed class Scheduled(TimeSpan due, Action fire) : IDisposable {
            public TimeSpan Due { get; } = due;
            public Action Fire { get; } = fire;
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }

        private static Task Hold(CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);

        private static Loader FailingFirst(Func<int, Preset, IProgress<KeyValuePair<int, string>>, CancellationToken, Task>? after = null) {
            var loader = new Loader();
            loader.Next = (n, p, progress, ct) => n == 1 ? throw new InvalidOperationException("unreadable") : (after?.Invoke(n, p, progress, ct) ?? Task.CompletedTask);
            return loader;
        }

        private static async Task<DataLoadWindow> ShownFailedAsync(Loader loader, DataLoadMode mode = DataLoadMode.AfterLaunch, Preset? preset = null, Preset? defaultPreset = null) {
            DataLoadWindow window = New(preset ?? SpaceAge, loader, mode, defaultPreset);
            window.Show();
            await window.AttemptTask;
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static string[] ButtonLabels(DataLoadWindow window) =>
            [.. window.FailurePanelControl!.Buttons!.Items.OfType<Button>().Select(b => (string)b.Content!)];

        private static string[] AllTexts(Window window) =>
            [.. window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

        [AvaloniaFact]
        public async Task Load_RealPreset_PopulatesResultAndReportsProgress() {
            var window = new DataLoadWindow(Vanilla);

            await window.LoadTask;

            Assert.Equal(DataLoadOutcome.Loaded, window.Outcome);
            Assert.NotNull(window.Result);
            Assert.Equal(100, window.Progress);
            Assert.Equal("Finishing (3 of 3).", window.StageTextControl.Text);
        }

        [AvaloniaFact]
        public async Task Load_FilterRecipesFlag_ReachesDataCache() {
            var filtered = new DataLoadWindow(Vanilla, filterRecipes: true);
            await filtered.LoadTask;

            var unfiltered = new DataLoadWindow(Vanilla, filterRecipes: false);
            await unfiltered.LoadTask;

            int filteredCount = filtered.Result!.AvailableRecipes.Count();
            int unfilteredCount = unfiltered.Result!.AvailableRecipes.Count();

            Assert.True(unfilteredCount > filteredCount);
        }

        [AvaloniaFact]
        public async Task Load_ManyConcurrentRealPresetLoads_AllReachProgress100() {
            var windows = new List<DataLoadWindow>();
            for (int i = 0; i < 8; i++)
                windows.Add(new DataLoadWindow(Vanilla));

            await Task.WhenAll(windows.Select(w => w.LoadTask));

            Assert.All(windows, w => Assert.Equal(100, w.Progress));
        }

        [AvaloniaFact]
        public async Task Loading_Is420By110_WithHeadlineKitBarAndCancel_ItsStagesFollowThePercentBands_AndAreAnnouncedAtMostEveryTwoSeconds() {
            var release = new TaskCompletionSource();
            IProgress<KeyValuePair<int, string>>? reporter = null;
            var loader = new Loader { Next = (_, _, progress, _) => { reporter = progress; return release.Task; } };
            TimeSpan now = TimeSpan.Zero;
            var scheduled = new List<Scheduled>();
            DataLoadWindow window = New(SpaceAge, loader, clock: () => now, schedule: (due, fire) => {
                var entry = new Scheduled(due, fire);
                scheduled.Add(entry);
                return entry;
            });
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Loading Preset", window.Title);
            Assert.Equal(new Size(420, 110), window.ClientSize);
            Assert.False(window.CanResize);
            Assert.Equal("Loading \u201CFactorio 2.0.55 — Space Age\u201D", window.HeadlineTextControl.Text);
            KitProgressBar bar = Assert.Single(window.GetVisualDescendants().OfType<KitProgressBar>());
            Assert.Equal(6, bar.Bounds.Height);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ProgressBar>(), b => b is not KitProgressBar);
            Assert.True(window.CancelButtonControl.IsEffectivelyVisible);
            Assert.False(window.CancelButtonControl.IsDefault);

            AutomationPeer live = ControlAutomationPeer.CreatePeerForElement(window.StageAnnouncerControl);
            Assert.Equal(AutomationLiveSetting.Polite, live.GetLiveSetting());
            Assert.Equal("Reading icons (1 of 3).", live.GetName());
            var announced = new List<string?>();
            live.PropertyChanged += (_, e) => {
                if (e.Property == AutomationElementIdentifiers.NameProperty)
                    announced.Add(live.GetName());
            };

            var seen = new List<string> { window.StageTextControl.Text! };
            void Report(int percent, string text) {
                reporter!.Report(new KeyValuePair<int, string>(percent, text));
                Dispatcher.UIThread.RunJobs();
                if (seen[^1] != window.StageTextControl.Text)
                    seen.Add(window.StageTextControl.Text!);
                Assert.DoesNotContain(AllTexts(window), t => t.Contains("...") || t.Contains("Done!") || t.Contains("Loading Icons") || t.Contains("Preparing Foreman"));
            }

            Report(0, "");
            now = TimeSpan.FromSeconds(0.5);
            Report(43, "Loading Icons...");
            Assert.Equal(43, window.ProgressBarControl.Value);
            now = TimeSpan.FromSeconds(1);
            Report(90, "Processing Data...");
            Report(95, "");
            Assert.Equal("Reading items, recipes, and buildings (2 of 3).", window.StageTextControl.Text);
            now = TimeSpan.FromSeconds(1.5);
            Report(98, "Finalizing...");
            Report(100, "Done!");
            Report(40, "Loading Icons...");
            Assert.Empty(announced);
            Scheduled armed = Assert.Single(scheduled, e => !e.Disposed);
            Assert.Equal(TimeSpan.FromSeconds(0.5), armed.Due);

            now = TimeSpan.FromSeconds(2);
            armed.Fire();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["Reading icons (1 of 3).", "Reading items, recipes, and buildings (2 of 3).", "Finishing (3 of 3)."], seen);
            Assert.Equal(100, window.ProgressBarControl.Value);
            Assert.Equal(["Finishing (3 of 3)."], announced);
            release.SetResult();
            await window.LoadTask;
            Assert.False(window.IsVisible);
        }

        [AvaloniaFact]
        public async Task AfterLaunch_CancelEscCommandPeriodAndClose_EachEndTheLoadAsCancelled() {
            Action<DataLoadWindow>[] triggers = [
                w => SheetFixture.Click(w, w.CancelButtonControl),
                w => w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None),
                w => w.KeyPressQwerty(PhysicalKey.Period, RawInputModifiers.Meta),
                w => w.Close(),
            ];
            foreach (Action<DataLoadWindow> trigger in triggers) {
                CancellationToken seen = default;
                var alerts = new Alerts();
                var loader = new Loader { Next = (_, _, _, ct) => { seen = ct; return Hold(ct); } };
                DataLoadWindow window = New(SpaceAge, loader, alerts: alerts);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                trigger(window);
                await window.LoadTask;
                Dispatcher.UIThread.RunJobs();

                Assert.True(seen.IsCancellationRequested);
                Assert.Equal(DataLoadOutcome.Cancelled, window.Outcome);
                Assert.Null(window.Result);
                Assert.False(window.IsVisible);
                Assert.Empty(alerts.Shown);
            }
        }

        [AvaloniaFact]
        public async Task AtLaunch_HasNoCancel_AndACloseRequestShowsTheStillLoadingAlert_WhileLoadingContinues() {
            var alerts = new Alerts();
            var release = new TaskCompletionSource();
            var loader = new Loader { Next = (_, _, _, _) => release.Task };
            DataLoadWindow window = New(Vanilla, loader, DataLoadMode.AtLaunch, Vanilla, alerts);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.CancelButtonControl.IsVisible);
            Control content = (Control)window.Content!;
            Point barRight = window.ProgressBarControl.TranslatePoint(new Point(window.ProgressBarControl.Bounds.Width, 0), content)!.Value;
            Assert.Equal(content.Bounds.Width - 20, barRight.X, 0.5);

            window.Close();
            Dispatcher.UIThread.RunJobs();

            (string title, string message) = Assert.Single(alerts.Shown);
            Assert.Equal("Foreman is still loading “Factorio 2.0 Vanilla”.", title);
            Assert.Equal("This window closes by itself when the preset is ready. To stop now, quit Foreman.", message);
            Assert.True(window.IsVisible);
            Assert.False(window.LoadTask.IsCompleted);
            Assert.False(loader.Calls.Count > 1);

            release.SetResult();
            await window.LoadTask;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DataLoadOutcome.Loaded, window.Outcome);
            Assert.False(window.IsVisible);
        }

        [AvaloniaFact]
        public async Task DataLoad_Failure_ShowsTheStatePanelError_WithARetryAction() {
            var window = new DataLoadWindow(new Preset("Nonexistent Preset", true, true));
            window.Show();
            await window.AttemptTask;
            Dispatcher.UIThread.RunJobs();

            StatePanel panel = window.FailurePanelControl!;
            Assert.True(panel.IsEffectivelyVisible);
            Assert.Equal(StatePanelShape.Error, panel.Shape);
            Assert.Equal("Couldn\u2019t load this preset", panel.Headline);
            Assert.Equal("Its data file is missing or unreadable, so nothing was loaded.", panel.Sentence);
            Assert.Equal(["Choose Another Preset\u2026", "Try Again"], ButtonLabels(window));
            Assert.Equal([false, true], panel.Buttons!.Items.OfType<Button>().Select(b => b.IsDefault));
            Assert.Equal("Loading Preset", window.Title);
            Assert.False(window.LoadTask.IsCompleted);

            SheetFixture.Click(window, panel.Buttons.Items.OfType<Button>().First());
            await window.LoadTask;

            Assert.Equal(DataLoadOutcome.ChooseAnother, window.Outcome);
            Assert.Null(window.Result);
            Assert.False(window.IsVisible);
        }

        [AvaloniaFact]
        public async Task DataLoad_Failure_GrowsTheWindowToFitTheFullPanel() {
            var release = new TaskCompletionSource();
            Loader retried = FailingFirst((_, _, _, _) => release.Task);
            DataLoadWindow afterLaunch = await ShownFailedAsync(retried);
            DataLoadWindow launchOther = await ShownFailedAsync(FailingFirst(), DataLoadMode.AtLaunch, SpaceAge, Vanilla);
            DataLoadWindow launchDefault = await ShownFailedAsync(FailingFirst(), DataLoadMode.AtLaunch, Vanilla, Vanilla);

            foreach (DataLoadWindow window in new[] { afterLaunch, launchOther, launchDefault }) {
                window.UpdateLayout();
                Assert.Equal(new Size(420, 232), window.ClientSize);
                Assert.False(window.FailurePanelControl!.IsCompact);
                Button only = Assert.Single(window.FailurePanelControl.Buttons!.Items.OfType<Button>(), b => b.IsDefault);
                Assert.Equal("Try Again", only.Content);
            }
            Assert.Equal(["Quit Foreman", "Use Default Preset", "Try Again"], ButtonLabels(launchOther));
            Assert.Equal("Couldn\u2019t load this preset", launchOther.FailurePanelControl!.Headline);
            Assert.Equal(["Quit Foreman", "Try Again"], ButtonLabels(launchDefault));
            Assert.Equal("Couldn\u2019t load the default preset", launchDefault.FailurePanelControl!.Headline);
            Assert.Equal("Its data file is missing or unreadable. Reinstall Foreman to restore it.", launchDefault.FailurePanelControl.Sentence);

            SheetFixture.Click(afterLaunch, afterLaunch.FailurePanelControl!.Buttons!.Items.OfType<Button>().Last());

            Assert.Equal(2, retried.Calls.Count);
            Assert.Equal(new Size(420, 110), afterLaunch.ClientSize);
            Assert.Equal("Reading icons (1 of 3).", afterLaunch.StageTextControl.Text);
            Assert.Null(afterLaunch.FailurePanelControl);
            release.SetResult();
            await afterLaunch.LoadTask;
            Assert.Equal(DataLoadOutcome.Loaded, afterLaunch.Outcome);
            launchOther.Close();
            launchDefault.Close();
        }

        [AvaloniaFact]
        public async Task AtLaunch_AfterAFailure_UseDefaultLoadsTheDefault_EscDoesNothing_QuitAndCloseEndWithQuit() {
            Loader loader = FailingFirst();
            DataLoadWindow useDefault = await ShownFailedAsync(loader, DataLoadMode.AtLaunch, SpaceAge, Vanilla);
            SheetFixture.Click(useDefault, useDefault.FailurePanelControl!.Buttons!.Items.OfType<Button>().ElementAt(1));
            await useDefault.LoadTask;
            Assert.Equal([SpaceAge, Vanilla], loader.Calls);
            Assert.Equal(DataLoadOutcome.Loaded, useDefault.Outcome);
            Assert.Same(Vanilla, useDefault.LoadedPreset);

            DataLoadWindow quit = await ShownFailedAsync(FailingFirst(), DataLoadMode.AtLaunch, SpaceAge, Vanilla);
            quit.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(quit.IsVisible);
            SheetFixture.Click(quit, quit.FailurePanelControl!.Buttons!.Items.OfType<Button>().First());
            await quit.LoadTask;
            Assert.Equal(DataLoadOutcome.Quit, quit.Outcome);

            DataLoadWindow closed = await ShownFailedAsync(FailingFirst(), DataLoadMode.AtLaunch, Vanilla, Vanilla);
            closed.Close();
            await closed.LoadTask;
            Assert.Equal(DataLoadOutcome.Quit, closed.Outcome);
            Assert.Null(closed.Result);
        }
    }
}
