using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit.Composites;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public partial class SaveFileLoadSheet : SheetContent<SaveFileLoadOutcome?>, IDisposable {
        private readonly DataCache dCache;
        private readonly HashSet<IDataObjectBase> enabledObjects;
        private readonly CancellationTokenSource cts = new();
        private readonly string defaultSaveFileLocation;
        private readonly Button cancelButton;
        private readonly Button loadButton;
        private readonly Button showLogButton;
        private bool running;
        private bool completed;
        private bool disposed;

        //Defaults to Cancel so a completion that never sets Outcome reads as nothing loaded.
        internal SaveFileLoadOutcome Outcome { get; private set; } = SaveFileLoadOutcome.Cancel;
        internal SaveFileInfo? SaveFileInfo { get; set; }

        internal string? ResolvedSaveFileLocation { get; private set; }

        internal Func<Task<string?>>? OpenSaveFilePathStub { get; set; }
        internal Func<string, string, Task<bool>>? ConfirmDialogStub { get; set; }
        internal Func<string, CancellationToken, SaveFileReader.Result>? LoadPipelineStub { get; set; }
        internal Func<Task>? ShowLogStub { get; set; }

        internal string? SavePath { get; set; }

        public SaveFileLoadSheet() : this(new DataCache(false), []) {
        }

        public SaveFileLoadSheet(DataCache cache, HashSet<IDataObjectBase> enabledObjects, string? lastSaveFileLocation = null) {
            InitializeComponent();
            dCache = cache;
            this.enabledObjects = enabledObjects;
            Title = "Load a Factorio save";
            showLogButton = SheetButtons.Make("Show Log…", ButtonRowRole.Helper);
            showLogButton.IsVisible = false;
            cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            loadButton = SheetButtons.Make("Load", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(showLogButton, cancelButton, loadButton);
            DefaultButton = loadButton;
            defaultSaveFileLocation = SaveFileReader.ResolveDefaultSaveFileLocation(lastSaveFileLocation);

            cancelButton.Click += (_, _) => Cancel();
            loadButton.Click += (_, _) => {
                if (!running)
                    Async.Fire(RunAsync(), nameof(RunAsync));
            };
            showLogButton.Click += (_, _) => Async.Fire(ShowLogAsync(), nameof(ShowLogAsync));
            Loaded += (_, _) => Async.Fire(RunAsync(), nameof(RunAsync));
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //FactorioBenchmarkRunner.Run is not preemptive, so Cancel completes without awaiting the in-flight load.
        public override void Cancel() {
            cts.Cancel();
            Outcome = SaveFileLoadOutcome.Cancel;
            SaveFileInfo = null;
            CompleteOnce();
        }

        private void CompleteOnce() {
            if (completed)
                return;
            completed = true;
            Complete(Outcome);
            Dispose();
        }

        public void Dispose() {
            if (disposed)
                return;
            disposed = true;
            cts.Dispose();
            GC.SuppressFinalize(this);
        }

        internal async Task RunAsync() {
            running = true;
            try {
                await RunOnceAsync().ConfigureAwait(true);
            } finally {
                running = false;
            }
        }

        private async Task RunOnceAsync() {
            string? path = SavePath ?? await (OpenSaveFilePathStub?.Invoke() ?? RealPickSaveFileAsync()).ConfigureAwait(true);
            if (path is null) {
                Outcome = SaveFileLoadOutcome.Cancel;
                SaveFileInfo = null;
                CompleteOnce();
                return;
            }
            SavePath = path;

            CancellationToken token = cts.Token;
            Func<string, CancellationToken, SaveFileReader.Result> load = LoadPipelineStub ?? SaveFileReader.Load;
            showLogButton.IsVisible = false;
            string quoted = $"“{Dialogs.MiddleTruncate(SaveName, 24)}”";
            ReportProgress($"Reading {quoted} — running Factorio.");
            SaveFileReader.Result result = await Task.Run(() => load(path, token)).ConfigureAwait(true);
            if (completed)
                return;
            ClearActivity();

            Outcome = result.Outcome;
            SaveFileInfo = result.SaveFileInfo;
            if (result.Outcome == SaveFileLoadOutcome.Abort || result.Failure != FactorioRunFailure.None) {
                ErrorLogging.LogLine("Save file read failed: " + (result.WarningMessage ?? result.Failure.ToString()));
                showLogButton.IsVisible = true;
                ReportFailure($"Couldn’t read {quoted}. {FailureSentences.Reason(result.Failure, result.CrashPhase, result.InstallCheck)}");
                return;
            }

            if (result.Outcome == SaveFileLoadOutcome.Ok) {
                ResolvedSaveFileLocation = Path.GetDirectoryName(path);
                await ProcessSaveDataAsync().ConfigureAwait(true);
            }
            CompleteOnce();
        }

        internal string SaveName => Path.GetFileNameWithoutExtension(SavePath ?? "");

        private Task ShowLogAsync() {
            if (ShowLogStub is not null)
                return ShowLogStub();
            return TopLevel.GetTopLevel(this)?.Launcher.LaunchFileInfoAsync(new FileInfo(ErrorLogging.LogFilePath)) ?? Task.CompletedTask;
        }

        private async Task<string?> RealPickSaveFileAsync() {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not IStorageProvider storage)
                return null;

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Load Factorio Save",
                AllowMultiple = false,
                SuggestedStartLocation = string.IsNullOrEmpty(defaultSaveFileLocation)
                    ? null
                    : await storage.TryGetFolderFromPathAsync(new Uri(defaultSaveFileLocation)).ConfigureAwait(true),
                FileTypeFilter = [new FilePickerFileType("factorio saves") { Patterns = ["*.zip"] }],
            }).ConfigureAwait(true);
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }

        internal async Task ProcessSaveDataAsync() {
            var notInSave = new List<string>();
            var differentVersion = new List<string>();
            var notInPreset = new List<string>();

            foreach (KeyValuePair<string, string> mod in dCache.IncludedMods) {
                if (mod.Key is "foremanexport" or "foremansavereader" or "core")
                    continue;
                if (SaveFileInfo?.Mods.ContainsKey(mod.Key) is false)
                    notInSave.Add(mod.Key);
                else if (SaveFileInfo?.Mods[mod.Key] != mod.Value)
                    differentVersion.Add(mod.Key);
            }
            foreach (KeyValuePair<string, string> mod in SaveFileInfo?.Mods ?? [])
                if (mod.Key is not ("foremanexport" or "foremansavereader" or "core") && !dCache.IncludedMods.ContainsKey(mod.Key))
                    notInPreset.Add(mod.Key);

            string lines = MismatchLines(notInSave, differentVersion, notInPreset);
            if (lines.Length > 0) {
                ErrorLogging.LogLine("Save file mods differ from the preset. Not in the save: " + string.Join(", ", notInSave) +
                    "; different version: " + string.Join(", ", differentVersion) + "; not in the preset: " + string.Join(", ", notInPreset));
                bool proceed = await ShowConfirmAsync("Use this save anyway?",
                    $"The mods in “{Dialogs.MiddleTruncate(SaveName)}” don’t match the preset “{Dialogs.MiddleTruncate(dCache.PresetName ?? "")}”. Only recipes the preset contains are enabled." + lines).ConfigureAwait(true);
                if (!proceed)
                    return;
            }

            EnabledObjectsDerivation.ResetToPlayerAssembler(dCache, enabledObjects);
            foreach (IRecipe recipe in dCache.Recipes.Values)
                if (recipe.Name.StartsWith("§§", StringComparison.Ordinal) || (SaveFileInfo?.Recipes.ContainsKey(recipe.Name) is true && SaveFileInfo.Recipes[recipe.Name]))
                    enabledObjects.Add(recipe);
            EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(dCache, enabledObjects);
        }

        internal static string MismatchLines(IReadOnlyList<string> notInSave, IReadOnlyList<string> differentVersion, IReadOnlyList<string> notInPreset) {
            var lines = new StringBuilder();
            foreach ((string label, IReadOnlyList<string> mods) in new[] { ("Not in the save: ", notInSave), ("Different version: ", differentVersion), ("Not in the preset: ", notInPreset) })
                if (mods.Count > 0)
                    lines.Append('\n').Append(label).Append(NameList(mods));
            return lines.ToString();
        }

        private static string NameList(IReadOnlyList<string> names) {
            const int Shown = 5;
            List<string> items = [.. names.Take(Shown)];
            if (names.Count > Shown)
                items.Add(string.Create(DisplayCulture.Format, $"{names.Count - Shown} more"));
            return items.Count switch {
                1 => items[0],
                2 => items[0] + " and " + items[1],
                _ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1],
            };
        }

        private Task<bool> ShowConfirmAsync(string title, string message) =>
            ConfirmDialogStub?.Invoke(title, message)
            ?? Dialogs.ShowConfirmAsync((Window)TopLevel.GetTopLevel(this)!, title, message, "Use Save", SheetWidthClass.Standard);

        internal Button CancelButtonControl => cancelButton;
        internal Button LoadButtonControl => loadButton;
        internal Button ShowLogButtonControl => showLogButton;
        internal void SimulateCancelClick() => Cancel();
        internal bool Disposed => disposed;
    }
}
