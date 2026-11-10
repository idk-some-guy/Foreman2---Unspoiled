using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using KitProgressBar = Foreman.Mac.Kit.Controls.ProgressBar;

namespace Foreman.Mac.Views {
    public enum DataLoadMode { AfterLaunch, AtLaunch }

    public enum DataLoadOutcome { Cancelled, Loaded, ChooseAnother, Quit }

    public readonly record struct DataLoadResult(DataCache? Cache, DataLoadOutcome Outcome);

    public partial class DataLoadWindow : Window, IThemeAware {
        private const double LoadingHeight = 110;
        private const double FailureHeight = 232;
        private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(2);
        private const string StillLoadingSentence = "This window closes by itself when the preset is ready. To stop now, quit Foreman.";
        internal static readonly string[] StageSentences = [
            "Reading icons (1 of 3).",
            "Reading items, recipes, and buildings (2 of 3).",
            "Finishing (3 of 3).",
        ];

        private readonly bool filterRecipes;
        private readonly DataLoadMode mode;
        private readonly Preset? defaultPreset;
        private readonly Func<Preset, DataCache, IProgress<KeyValuePair<int, string>>, CancellationToken, Task> load;
        private readonly Func<Window, string, string, Task> showAlert;
        private readonly Panel root;
        private readonly StackPanel loadingLayout;
        private readonly TextBlock headlineText;
        private readonly TextBlock stageText;
        private readonly KitProgressBar progressBar;
        private readonly Button cancelButton;
        private readonly TextBlock stageAnnouncer;
        private readonly Func<TimeSpan> clock;
        private readonly Func<TimeSpan, Action, IDisposable> schedule;
        private IDisposable? pendingAnnouncement;
        private TimeSpan lastAnnounced;
        private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly WindowMinSizeScale minSizeScale;
        private Preset currentPreset;
        private StatePanel? failurePanel;
        private Action? requestCancel;
        private int attemptId;
        private int currentPercent;
        private int stage;
        private bool attemptPending;
        private bool cancelRequested;
        private bool alertOpen;
        private bool finishing;
        private bool isClosed;

        public Task LoadTask => finished.Task;
        internal Task AttemptTask { get; private set; }
        public DataCache? Result { get; private set; }
        public DataLoadOutcome Outcome { get; private set; }
        public Preset LoadedPreset => currentPreset;
        internal bool PresentsOutdatedAlert { get; init; } = true;
        internal Task? OutdatedAlertTask { get; private set; }
        public int Progress => currentPercent;

        internal TextBlock StageTextControl => stageText;
        internal KitProgressBar ProgressBarControl => progressBar;
        internal Button CancelButtonControl => cancelButton;
        internal StatePanel? FailurePanelControl => failurePanel;

        public DataLoadWindow() : this(new Preset(string.Empty, false, false)) {
        }

        public DataLoadWindow(Preset preset, bool filterRecipes = true, DataLoadMode mode = DataLoadMode.AfterLaunch, Preset? defaultPreset = null)
            : this(preset, filterRecipes, mode, defaultPreset, null, null) {
        }

        internal DataLoadWindow(Preset preset, bool filterRecipes, DataLoadMode mode, Preset? defaultPreset,
                Func<Preset, DataCache, IProgress<KeyValuePair<int, string>>, CancellationToken, Task>? load,
                Func<Window, string, string, Task>? showAlert, Func<TimeSpan>? clock = null, Func<TimeSpan, Action, IDisposable>? schedule = null) {
            InitializeComponent();
            this.load = load ?? ((p, cache, progress, ct) => cache.LoadAllData(p, progress, cancellationToken: ct));
            this.showAlert = showAlert ?? Dialogs.ShowWarningAsync;
            this.filterRecipes = filterRecipes;
            this.mode = mode;
            this.defaultPreset = defaultPreset;
            this.clock = clock ?? (() => Stopwatch.GetElapsedTime(0));
            this.schedule = schedule ?? ((delay, action) => DispatcherTimer.RunOnce(action, delay));
            currentPreset = preset;
            root = this.FindControl<Panel>("Root")!;
            loadingLayout = this.FindControl<StackPanel>("LoadingLayout")!;
            headlineText = this.FindControl<TextBlock>("HeadlineText")!;
            stageText = this.FindControl<TextBlock>("StageText")!;
            progressBar = this.FindControl<KitProgressBar>("LoadProgress")!;
            cancelButton = this.FindControl<Button>("CancelButton")!;
            stageAnnouncer = this.FindControl<TextBlock>("StageAnnouncer")!;
            cancelButton.IsVisible = mode == DataLoadMode.AfterLaunch;
            cancelButton.Click += (_, _) => RequestCancel();
            AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
            minSizeScale = new WindowMinSizeScale(this);
            AttemptTask = StartAttempt();

            AppearanceHub.Current?.Subscribe(this);
            Closed += (_, _) => {
                CancelPendingAnnouncement();
                AppearanceHub.Current?.Unsubscribe(this);
            };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void RefreshTheme() => minSizeScale.Apply();

        private Task StartAttempt() {
            int id = ++attemptId;
            currentPercent = 0;
            stage = 0;
            progressBar.Value = 0;
            ApplyStage();
            AnnounceStage();
            headlineText.Text = string.Format(CultureInfo.InvariantCulture, "Loading “{0}”", Dialogs.MiddleTruncate(currentPreset.Name));
            if (failurePanel is not null) {
                root.Children.Remove(failurePanel);
                failurePanel = null;
            }
            loadingLayout.IsVisible = true;
            cancelButton.IsEnabled = true;
            Height = LoadingHeight;
            cancelRequested = false;
            attemptPending = true;
            return RunLoadAsync(id, currentPreset);
        }

        private async Task RunLoadAsync(int id, Preset preset) {
            using var cts = new CancellationTokenSource();
            requestCancel = cts.Cancel;
            CancellationToken ct = cts.Token;
            var progress = new Progress<KeyValuePair<int, string>>(v => {
                if (id == attemptId)
                    ApplyProgress(v.Key);
            });
            var cache = new DataCache(filterRecipes);
            bool loaded = false;
            try {
                await load(preset, cache, progress, ct).ConfigureAwait(false);
                loaded = true;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, string.Format(CultureInfo.InvariantCulture, "Failed to load preset '{0}'", preset.Name));
            }
            await Dispatcher.UIThread.InvokeAsync(() => {
                attemptPending = false;
                requestCancel = null;
                if (cancelRequested)
                    Finish(DataLoadOutcome.Cancelled, null);
                else if (loaded)
                    Finish(DataLoadOutcome.Loaded, cache);
                else
                    ShowFailure();
            });
        }

        private void ApplyProgress(int percent) {
            if (percent <= currentPercent)
                return;
            currentPercent = percent;
            progressBar.Value = percent;
            int band = percent >= 98 ? 2 : percent >= 90 ? 1 : 0;
            if (band > stage) {
                stage = band;
                ApplyStage();
                AnnouncePendingStage();
            }
        }

        private void ApplyStage() {
            stageText.Text = StageSentences[stage];
            AutomationProperties.SetHelpText(progressBar, StageSentences[stage]);
        }

        private void CancelPendingAnnouncement() {
            pendingAnnouncement?.Dispose();
            pendingAnnouncement = null;
        }

        private void AnnouncePendingStage() {
            CancelPendingAnnouncement();
            if (stageAnnouncer.Text == StageSentences[stage])
                return;
            TimeSpan wait = lastAnnounced + AnnounceInterval - clock();
            if (wait <= TimeSpan.Zero) {
                AnnounceStage();
                return;
            }
            pendingAnnouncement = schedule(wait, AnnouncePendingStage);
        }

        private void AnnounceStage() {
            CancelPendingAnnouncement();
            stageAnnouncer.Text = StageSentences[stage];
            lastAnnounced = clock();
        }

        private void Finish(DataLoadOutcome outcome, DataCache? cache) {
            Outcome = outcome;
            Result = cache;
            finishing = true;
            Window? alertOwner = Owner as Window ?? (MainWindow.MostRecentlyOpened is { IsVisible: true, IsClosed: false } main ? main : null);
            Preset loaded = currentPreset;
            finished.TrySetResult();
            if (!isClosed)
                Close();
            if (outcome == DataLoadOutcome.Loaded && cache is not null && PresentsOutdatedAlert)
                Dispatcher.UIThread.Post(() => OutdatedAlertTask = OutdatedPresetAlert.Present(alertOwner, loaded, cache, showAlert), DispatcherPriority.Background);
        }

        private void RequestCancel() {
            if (!attemptPending) {
                Finish(DataLoadOutcome.Cancelled, null);
                return;
            }
            cancelRequested = true;
            cancelButton.IsEnabled = false;
            requestCancel?.Invoke();
        }

        private void ShowStillLoadingAlert() {
            if (alertOpen)
                return;
            alertOpen = true;
            string headline = string.Format(CultureInfo.InvariantCulture, "Foreman is still loading “{0}”.", Dialogs.MiddleTruncate(currentPreset.Name));
            Async.Fire(AwaitAlertAsync(showAlert(this, headline, StillLoadingSentence)), nameof(ShowStillLoadingAlert));
        }

        private async Task AwaitAlertAsync(Task alert) {
            try {
                await alert.ConfigureAwait(true);
            } finally {
                alertOpen = false;
            }
        }

        private void ShowFailure() {
            bool defaultFailed = mode == DataLoadMode.AtLaunch && (defaultPreset is null || defaultPreset.Name == currentPreset.Name);
            Button tryAgain = SheetButtons.Make("Try Again", ButtonRowRole.Default);
            tryAgain.IsDefault = true;
            tryAgain.Click += (_, _) => AttemptTask = StartAttempt();
            ButtonRow buttons;
            if (mode == DataLoadMode.AfterLaunch) {
                Button chooseAnother = SheetButtons.Make("Choose Another Preset…", ButtonRowRole.Other);
                chooseAnother.Click += (_, _) => Finish(DataLoadOutcome.ChooseAnother, null);
                buttons = SheetButtons.Row(chooseAnother, tryAgain);
            } else {
                Button quit = SheetButtons.Make("Quit Foreman", ButtonRowRole.Other);
                quit.Click += (_, _) => Finish(DataLoadOutcome.Quit, null);
                if (defaultFailed) {
                    buttons = SheetButtons.Row(quit, tryAgain);
                } else {
                    Button useDefault = SheetButtons.Make("Use Default Preset", ButtonRowRole.Other);
                    useDefault.Click += (_, _) => {
                        currentPreset = defaultPreset!;
                        AttemptTask = StartAttempt();
                    };
                    buttons = SheetButtons.Row(quit, useDefault, tryAgain);
                }
            }

            failurePanel = new StatePanel {
                Theme = (ControlTheme)this.FindResource("Kit.StatePanel")!,
                Buttons = buttons,
                Headline = defaultFailed ? "Couldn’t load the default preset" : "Couldn’t load this preset",
                Sentence = defaultFailed
                    ? "Its data file is missing or unreadable. Reinstall Foreman to restore it."
                    : "Its data file is missing or unreadable, so nothing was loaded.",
                Shape = StatePanelShape.Error,
                MovesFocusOnAppear = true,
            };
            CancelPendingAnnouncement();
            loadingLayout.IsVisible = false;
            root.Children.Add(failurePanel);
            Height = FailureHeight;
            tryAgain.Focus();
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e) {
            bool primary = e.KeyModifiers == KeyModifiers.Meta;
            bool escape = e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None;
            bool commandPeriod = e.Key == Key.OemPeriod && primary;
            bool commandW = e.Key == Key.W && primary;
            if (!escape && !commandPeriod && !commandW) {
                if (e.Key == Key.Enter && attemptPending)
                    e.Handled = true;
                return;
            }
            e.Handled = true;
            if (mode == DataLoadMode.AfterLaunch)
                RequestCancel();
            else if (attemptPending)
                ShowStillLoadingAlert();
            else if (commandW)
                Finish(DataLoadOutcome.Quit, null);
        }

        protected override void OnClosing(WindowClosingEventArgs e) {
            if (finishing) {
                base.OnClosing(e);
                return;
            }
            e.Cancel = true;
            if (mode == DataLoadMode.AfterLaunch)
                RequestCancel();
            else if (attemptPending)
                ShowStillLoadingAlert();
            else
                Finish(DataLoadOutcome.Quit, null);
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            isClosed = true;
            finished.TrySetResult();
        }
    }
}
