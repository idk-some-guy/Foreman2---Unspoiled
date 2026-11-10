using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Views.Research;
using Foreman.Models.Research;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public enum ResearchSyncOutcome { Cancelled = 0, Synced, ShowResearch, ChooseAnotherSave }

    public partial class ResearchSyncSheet : SheetContent<ResearchSyncOutcome>, IDisposable {
        private const int MaxRows = 6;
        private const string RunningLede = "Foreman runs Factorio in the background to read the save. This can take a minute.";
        private static readonly TimeSpan ReannounceInterval = TimeSpan.FromSeconds(2);

        private enum Phase { Running, Failed, Missing, Synced }

        private readonly ProductionGraph graph;
        private readonly DataCache cache;
        private readonly string savePath;
        private readonly CancellationTokenSource cts = new();
        private readonly TextBlock ledeBlock;
        private readonly StackPanel changeList;
        private readonly TextBlock moreBlock;
        private readonly Border mismatchBlock;
        private readonly TextBlock mismatchSentenceBlock;
        private readonly TextBlock mismatchLinesBlock;
        private readonly TextBlock versionNoteBlock;
        private readonly Button showLogButton;
        private readonly Button showResearchButton;
        private readonly Button cancelButton;
        private readonly Button defaultButton;
        private readonly List<(string Name, string Trailing, bool ReplacedEdit)> changeRows = [];
        private ITimer? announceTimer;
        private Phase phase = Phase.Running;
        private string headline = "";
        private bool running;
        private bool completed;
        private bool disposed;

        public ResearchSyncSheet() : this(new ProductionGraph(), new DataCache(false), "") {
        }

        public ResearchSyncSheet(ProductionGraph graph, DataCache cache, string savePath) {
            InitializeComponent();
            this.graph = graph;
            this.cache = cache;
            this.savePath = savePath;
            WidthClass = SheetWidthClass.Wide;
            Title = "Sync research";

            ledeBlock = this.FindControl<TextBlock>("LedeBlock")!;
            changeList = this.FindControl<StackPanel>("ChangeList")!;
            moreBlock = this.FindControl<TextBlock>("MoreBlock")!;
            mismatchBlock = this.FindControl<Border>("MismatchBlock")!;
            mismatchSentenceBlock = this.FindControl<TextBlock>("MismatchSentenceBlock")!;
            mismatchLinesBlock = this.FindControl<TextBlock>("MismatchLinesBlock")!;
            versionNoteBlock = this.FindControl<TextBlock>("VersionNoteBlock")!;
            ledeBlock.Text = RunningLede;

            showLogButton = SheetButtons.Make("Show Log…", ButtonRowRole.Helper);
            showLogButton.IsVisible = false;
            showResearchButton = SheetButtons.Make("Show Research", ButtonRowRole.Helper);
            showResearchButton.IsVisible = false;
            cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            defaultButton = SheetButtons.Make("Sync", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(showLogButton, showResearchButton, cancelButton, defaultButton);
            DefaultButton = defaultButton;

            cancelButton.Click += (_, _) => Cancel();
            defaultButton.Click += (_, _) => OnDefaultClicked();
            showResearchButton.Click += (_, _) => Finish(ResearchSyncOutcome.ShowResearch);
            showLogButton.Click += (_, _) => Async.Fire(ShowLogAsync(), nameof(ShowLogAsync));
            Loaded += (_, _) => Async.Fire(RunAsync(), nameof(RunAsync));
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        internal Func<string, CancellationToken, SaveFileReader.Result>? LoadPipelineStub { get; set; }
        internal Func<FirstSyncNoticeSheet, Task<FirstSyncChoice>>? FirstSyncStub { get; set; }
        internal Func<Task>? ShowLogStub { get; set; }
        internal TimeProvider Clock { get; set; } = TimeProvider.System;
        internal Action<string>? Announce { get; set; }
        internal bool StoredSaveMissing { get; set; }
        internal ResearchSyncReport? Report { get; private set; }
        internal Task LastRun { get; private set; } = Task.CompletedTask;

        internal string SaveName => Path.GetFileNameWithoutExtension(savePath);

        public override void Cancel() {
            cts.Cancel();
            Finish(phase == Phase.Synced ? ResearchSyncOutcome.Synced : ResearchSyncOutcome.Cancelled);
        }

        private void Finish(ResearchSyncOutcome outcome) {
            if (completed)
                return;
            completed = true;
            Complete(outcome);
            Dispose();
        }

        public void Dispose() {
            if (disposed)
                return;
            disposed = true;
            StopAnnouncing();
            cts.Dispose();
            GC.SuppressFinalize(this);
        }

        internal Task RunAsync() => LastRun = RunGuardedAsync();

        private async Task RunGuardedAsync() {
            if (running || completed || phase == Phase.Synced)
                return;
            running = true;
            try {
                await RunOnceAsync().ConfigureAwait(true);
            } finally {
                running = false;
            }
        }

        private async Task RunOnceAsync() {
            string quoted = $"“{Dialogs.MiddleTruncate(SaveName)}”";
            if (StoredSaveMissing) {
                phase = Phase.Missing;
                defaultButton.Content = "Choose Save…";
                ReportFailure($"Couldn’t find {quoted}. It was moved or deleted after the last sync.", offersRetry: false);
                return;
            }

            phase = Phase.Running;
            showLogButton.IsVisible = false;
            bool firstSync = graph.Research.Source is null;
            CancellationToken token = cts.Token;
            string progress = $"Reading {quoted} — running Factorio.";
            ReportProgress(progress);
            StartAnnouncing(progress);

            ResearchRead read;
            try {
                read = await ResearchSync.ReadAsync(savePath, token, LoadPipelineStub, Clock).ConfigureAwait(true);
            } finally {
                StopAnnouncing();
            }
            if (completed)
                return;
            ClearActivity();

            if (!read.Succeeded) {
                if (read.Outcome == SaveFileLoadOutcome.Cancel && read.Failure == FactorioRunFailure.None) {
                    Finish(ResearchSyncOutcome.Cancelled);
                    return;
                }
                phase = Phase.Failed;
                ErrorLogging.LogLine("Research sync failed: " + read.Failure);
                showLogButton.IsVisible = true;
                ReportFailure($"Couldn’t read {quoted}, so research is unchanged. {FailureSentences.Reason(read.Failure, read.CrashPhase, read.InstallCheck)}");
                return;
            }

            bool clearTyped = false;
            IReadOnlyList<RecipeNode> typed = ResearchSync.NodesWithTypedProductivity(graph);
            if (firstSync && typed.Count > 0) {
                var notice = new FirstSyncNoticeSheet([.. typed.Select(node => node.BaseRecipe.Recipe?.FriendlyName ?? "")]);
                FirstSyncChoice choice = await AskFirstSyncAsync(notice).ConfigureAwait(true);
                if (completed)
                    return;
                if (choice == FirstSyncChoice.Cancel) {
                    Finish(ResearchSyncOutcome.Cancelled);
                    return;
                }
                clearTyped = choice == FirstSyncChoice.Clear;
            }

            Report = ResearchSync.Apply(graph, read.State!, clearTyped);
            phase = Phase.Synced;
            ShowSuccess(read, Report);
            Announce?.Invoke(headline);
        }

        private Task<FirstSyncChoice> AskFirstSyncAsync(FirstSyncNoticeSheet notice) {
            if (FirstSyncStub is not null)
                return FirstSyncStub(notice);
            if (TopLevel.GetTopLevel(this) is not Window window)
                return Task.FromResult(FirstSyncChoice.Cancel);
            return SheetHost.For(window).ShowStackedAsync(notice);
        }

        private void OnDefaultClicked() {
            switch (phase) {
                case Phase.Synced:
                    Finish(ResearchSyncOutcome.Synced);
                    break;
                case Phase.Missing:
                    Finish(ResearchSyncOutcome.ChooseAnotherSave);
                    break;
                case Phase.Failed:
                    Async.Fire(RunAsync(), nameof(RunAsync));
                    break;
            }
        }

        private void StartAnnouncing(string text) {
            Announce?.Invoke(text);
            announceTimer = Clock.CreateTimer(_ => {
                ITimer? timer = announceTimer;
                if (timer is null)
                    return;
                timer.Change(ReannounceInterval, Timeout.InfiniteTimeSpan);
                Dispatcher.UIThread.Post(() => {
                    if (announceTimer is not null)
                        Announce?.Invoke(text);
                });
            }, null, ReannounceInterval, Timeout.InfiniteTimeSpan);
        }

        private void StopAnnouncing() {
            announceTimer?.Dispose();
            announceTimer = null;
        }

        private void ShowSuccess(ResearchRead read, ResearchSyncReport report) {
            string save = Dialogs.MiddleTruncate(SaveName);
            headline = $"Research synced from “{save}”.";
            SetTitle(headline);
            ledeBlock.Text = Lede(report, graph.Research.Source?.TicksPlayed);

            changeRows.Clear();
            changeList.Children.Clear();
            foreach (ResearchChange change in report.Changes.Take(MaxRows)) {
                (string name, string trailing) = Describe(change);
                changeRows.Add((name, trailing, change.ReplacedEdit));
                changeList.Children.Add(BuildRow(name, change));
            }
            changeList.IsVisible = changeRows.Count > 0;
            int more = report.Changes.Count - MaxRows;
            moreBlock.Text = more > 0 ? string.Create(DisplayCulture.Format, $"and {more} more") : "";
            moreBlock.IsVisible = more > 0;

            string preset = Dialogs.MiddleTruncate(cache.PresetName ?? "");
            (IReadOnlyList<string> notInSave, IReadOnlyList<string> differentVersion, IReadOnlyList<string> notInPreset) =
                SaveModComparison.Compare(cache.IncludedMods, read.SaveMods);
            string lines = SaveModComparison.MismatchLines(notInSave, differentVersion, notInPreset).TrimStart('\n');
            bool mismatch = lines.Length > 0;
            if (mismatch)
                ErrorLogging.LogLine("Save file mods differ from the preset. Not in the save: " + string.Join(", ", notInSave) +
                    "; different version: " + string.Join(", ", differentVersion) + "; not in the preset: " + string.Join(", ", notInPreset));
            mismatchSentenceBlock.Text = mismatch
                ? $"The mods in “{save}” don’t match the preset “{preset}”. Research for recipes the preset doesn’t have is kept but has no effect."
                : "";
            mismatchLinesBlock.Text = lines;
            mismatchBlock.IsVisible = mismatch;
            mismatchLinesBlock.IsVisible = mismatch;

            bool newer = read.SaveGameVersion is { } saveVersion && cache.GameVersion is { } presetVersion && saveVersion > presetVersion;
            versionNoteBlock.Text = newer
                ? $"“{save}” comes from Factorio {read.SaveGameVersion}; the preset “{preset}” is from {cache.GameVersion}. To match them, import the preset again in Settings > Presets."
                : "";
            versionNoteBlock.IsVisible = newer;

            cancelButton.IsVisible = false;
            showLogButton.IsVisible = false;
            showResearchButton.IsVisible = true;
            defaultButton.Content = "Done";
        }

        private static string Lede(ResearchSyncReport report, long? ticks) {
            if (report.FirstSync)
                return string.Create(DisplayCulture.Format, $"Read {report.BonusCount} bonuses, {report.RecipeCount} recipe productivity values, and {report.TechnologyCount} technologies.");
            string played = ResearchText.PlayTime(ticks) is { } time ? time + " played. " : "";
            int changed = report.Changes.Count;
            if (changed == 0)
                return played + "Nothing changed since the last sync.";
            string values = ResearchText.CountWords(changed, true) + (changed == 1 ? " value changed" : " values changed");
            int edits = report.ReplacedEditCount;
            string tail = edits switch {
                0 => ".",
                1 => ", and one of them was an edit made by hand.",
                _ => $", and {ResearchText.CountWords(edits, false)} of them were edits made by hand.",
            };
            return played + values + tail;
        }

        private (string Name, string Trailing) Describe(ResearchChange change) {
            if (change.Field is { } field)
                return (ResearchText.FieldLabel(field), ResearchText.Value(field, change.Old) + " → " + ResearchText.Value(field, change.New));
            string recipe = cache.Recipes.TryGetValue(change.Recipe!, out IRecipe? known) ? known.FriendlyName : change.Recipe!;
            return (recipe + " productivity", RecipeValue(change.Old) + " → " + RecipeValue(change.New));
        }

        private static string RecipeValue(double? value) => value is { } known ? ResearchText.Percent(known) : "none";

        private static Grid BuildRow(string name, ResearchChange change) {
            var row = new Grid { Height = 24, ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Text(name, "callout"));
            if (change.ReplacedEdit) {
                TextBlock note = Text("replaced your edit", "caption");
                note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("Foreman.Color.AccentText"));
                label.Children.Add(note);
            }
            var values = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            (string old, string current) = Sides(change);
            values.Children.Add(Text(old, "callout", tabular: true));
            values.Children.Add(Text("→", "callout tertiary"));
            values.Children.Add(Text(current, "callout", tabular: true));
            Grid.SetColumn(values, 1);
            row.Children.Add(label);
            row.Children.Add(values);
            return row;
        }

        private static (string Old, string New) Sides(ResearchChange change) =>
            change.Field is { } field
                ? (ResearchText.Value(field, change.Old), ResearchText.Value(field, change.New))
                : (RecipeValue(change.Old), RecipeValue(change.New));

        private static TextBlock Text(string text, string classes, bool tabular = false) {
            Application.Current!.TryGetResource("Kit.Text", null, out object? theme);
            var block = new TextBlock { Text = text, Theme = theme as ControlTheme };
            block.Classes.AddRange(classes.Split(' '));
            if (tabular)
                block.FontFeatures = FontFeatureCollection.Parse("tnum");
            return block;
        }

        private Task ShowLogAsync() {
            if (ShowLogStub is not null)
                return ShowLogStub();
            return TopLevel.GetTopLevel(this)?.Launcher.LaunchFileInfoAsync(new FileInfo(ErrorLogging.LogFilePath)) ?? Task.CompletedTask;
        }

        internal string HeadlineText => headline;
        internal string LedeText => ledeBlock.Text ?? "";
        internal string StripText => Activity?.Sentence ?? "";
        internal IReadOnlyList<(string Name, string Trailing, bool ReplacedEdit)> ChangeRows => changeRows;
        internal string MoreText => moreBlock.Text ?? "";
        internal string MismatchText => mismatchSentenceBlock.Text ?? "";
        internal string MismatchLinesText => mismatchLinesBlock.Text ?? "";
        internal string VersionNoteText => versionNoteBlock.Text ?? "";

        internal Button CancelButtonControl => cancelButton;
        internal Button DefaultButtonControl => defaultButton;
        internal Button ShowLogButtonControl => showLogButton;
        internal Button ShowResearchButtonControl => showResearchButton;
    }
}
