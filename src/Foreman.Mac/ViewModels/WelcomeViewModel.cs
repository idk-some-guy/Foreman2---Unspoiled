using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.ViewModels {
    internal enum WelcomeLoadPhase { Idle, Loading, Failed }

    internal enum WelcomeLoadOutcome { None, Loaded, Failed, Cancelled }

    internal sealed class WelcomeRow(GraphRow graph, bool unavailable, string relativeTime, string absoluteTime) {
        public GraphRow Graph { get; } = graph;
        public Guid Id => Graph.Id;
        public string Name => Graph.Name;
        public string PresetName => Graph.PresetName;
        public int NodeCount => Graph.NodeCount;
        public byte[]? Thumbnail => Graph.Thumbnail;
        public bool Unavailable { get; } = unavailable;
        public string RelativeTime { get; } = relativeTime;
        public string AbsoluteTime { get; } = absoluteTime;
        public string PresetLabel => PresetName;
        public string UnavailableMeta => $"Preset “{PresetName}” isn't installed";
    }

    internal sealed partial class WelcomeViewModel : ObservableObject {
        internal const int FilterThreshold = 8;

        private readonly WelcomeHost host;
        private readonly List<WelcomeRow> all = [];
        private CancellationTokenSource? cts;
        private int attempt;
        private int stage;
        private Preset? lastAttempted;

        public WelcomeViewModel(WelcomeHost host) {
            this.host = host;
            Presets = host.InstalledPresets();
            Refresh();
            if (host.InitialPreset is { } initial) {
                LoadedPresetName = "";
                SelectedPreset = Presets.FirstOrDefault(p => p.Name == initial.Name) ?? initial;
                LoadTask = BeginLoadAsync(SelectedPreset);
                return;
            }
            LoadedPresetName = host.Settings.CurrentPresetName;
            SelectedPreset = Presets.FirstOrDefault(p => p.Name == LoadedPresetName);
            Ready = true;
        }

        public IReadOnlyList<Preset> Presets { get; }
        public ObservableCollection<WelcomeRow> Rows { get; } = [];
        public string LoadedPresetName { get; private set; }
        public Preset? SelectedPreset { get; private set; }
        public Task LoadTask { get; private set; } = Task.CompletedTask;
        public WelcomeLoadOutcome LastOutcome { get; private set; }
        public int TotalCount => all.Count;
        public bool ShowFilter => TotalCount > FilterThreshold;
        public bool IsEmpty => TotalCount == 0;
        public bool HasNoMatches => TotalCount > 0 && Rows.Count == 0;
        public bool ActionsEnabled => Ready && Phase == WelcomeLoadPhase.Idle;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActionsEnabled))]
        private WelcomeLoadPhase phase;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActionsEnabled))]
        private bool ready;

        [ObservableProperty]
        private string loadSentence = "";

        [ObservableProperty]
        private int loadPercent;

        [ObservableProperty]
        private string failedPresetName = "";

        [ObservableProperty]
        private string filterText = "";

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        public void Refresh() {
            HashSet<string> installed = [.. host.InstalledPresets().Select(p => p.Name)];
            DateTime now = host.Library.Now;
            all.Clear();
            foreach (GraphRow row in host.Library.All())
                all.Add(new WelcomeRow(row, !installed.Contains(row.PresetName), WelcomeTime.Relative(row.LastOpenedAt, now), WelcomeTime.Absolute(row.LastOpenedAt, now)));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ShowFilter));
            OnPropertyChanged(nameof(IsEmpty));
            ApplyFilter();
        }

        private void ApplyFilter() {
            string query = ShowFilter ? FilterText.Trim() : "";
            Rows.Clear();
            foreach (WelcomeRow row in all.Where(r => query.Length == 0 || r.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                Rows.Add(row);
            OnPropertyChanged(nameof(HasNoMatches));
        }

        public Task SelectPresetAsync(Preset preset) {
            SelectedPreset = preset;
            if (Ready && Phase == WelcomeLoadPhase.Idle && preset.Name == LoadedPresetName)
                return Task.CompletedTask;
            LoadTask = BeginLoadAsync(preset);
            return LoadTask;
        }

        public Task TryAgainAsync() {
            if (lastAttempted is null)
                return Task.CompletedTask;
            LoadTask = BeginLoadAsync(lastAttempted);
            return LoadTask;
        }

        public void CancelLoad() => cts?.Cancel();

        private async Task BeginLoadAsync(Preset preset) {
            cts?.Cancel();
            int id = ++attempt;
            lastAttempted = preset;
            var source = new CancellationTokenSource();
            cts = source;
            stage = 0;
            LoadPercent = 0;
            FailedPresetName = preset.Name;
            Ready = false;
            Phase = WelcomeLoadPhase.Loading;
            UpdateSentence(preset);
            var progress = new Progress<KeyValuePair<int, string>>(v => {
                if (id == attempt)
                    ApplyProgress(preset, v.Key);
            });

            bool loaded = false;
            try {
                loaded = await host.LoadPreset(preset, progress, source.Token).ConfigureAwait(true);
            } catch (OperationCanceledException) when (source.IsCancellationRequested) {
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, $"Failed to load preset '{preset.Name}'");
            }
            if (id != attempt)
                return;

            if (source.IsCancellationRequested) {
                LastOutcome = WelcomeLoadOutcome.Cancelled;
                Phase = WelcomeLoadPhase.Idle;
            } else if (loaded) {
                LoadedPresetName = preset.Name;
                host.Settings.CurrentPresetName = preset.Name;
                host.SaveSettings();
                LastOutcome = WelcomeLoadOutcome.Loaded;
                Phase = WelcomeLoadPhase.Idle;
                Ready = true;
            } else {
                LastOutcome = WelcomeLoadOutcome.Failed;
                Phase = WelcomeLoadPhase.Failed;
            }
        }

        private void ApplyProgress(Preset preset, int percent) {
            if (percent <= LoadPercent)
                return;
            LoadPercent = percent;
            int band = percent >= 98 ? 2 : percent >= 90 ? 1 : 0;
            if (band > stage)
                stage = band;
            UpdateSentence(preset);
        }

        private void UpdateSentence(Preset preset) {
            string step = DataLoadWindow.StageSentences[stage];
            LoadSentence = string.Format(CultureInfo.InvariantCulture, "Loading “{0}”: {1}{2}", Dialogs.MiddleTruncate(preset.Name), char.ToLowerInvariant(step[0]), step[1..]);
        }
    }

    internal static class WelcomeTime {
        public static string Relative(DateTime utc, DateTime nowUtc) {
            DateTime local = utc.ToLocalTime();
            DateTime now = nowUtc.ToLocalTime();
            int days = (now.Date - local.Date).Days;
            return days switch {
                <= 0 => "Today, " + local.ToString("t", CultureInfo.CurrentCulture),
                1 => "Yesterday",
                < 7 => $"{days} days ago",
                _ => local.Year == now.Year ? local.ToString("MMM d", CultureInfo.CurrentCulture) : local.ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
            };
        }

        public static string Absolute(DateTime utc, DateTime nowUtc) {
            DateTime local = utc.ToLocalTime();
            int days = (nowUtc.ToLocalTime().Date - local.Date).Days;
            string time = local.ToString("t", CultureInfo.CurrentCulture);
            return days switch {
                <= 0 => "today at " + time,
                1 => "yesterday at " + time,
                _ => local.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) + " at " + time,
            };
        }
    }
}
