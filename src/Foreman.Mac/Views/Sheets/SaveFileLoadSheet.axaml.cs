using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    //Ports SaveFileLoadForm(+.Designer.cs) (reference io-reference.md §5, upstream SavefileLoadForm.cs):
    //runs immediately once the sheet is presented - picks a save .zip, then hands it to SaveFileReader.Load
    //(the process pipeline, Foreman.Core) and applies the parsed result to the live enabled-objects set.
    //Cancellation is honest about FactorioBenchmarkRunner.Run not being preemptive (docs/io-reference.md §4
    //caution): the Cancel button completes the sheet immediately without waiting for an in-flight run to
    //actually stop. A failed read keeps the sheet open on its failure strip; Load (Try Again) rereads the same save.
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

        //Defaults to Cancel (not the enum's own Ok=0) - a completion that never explicitly sets Outcome
        //(nothing else has run yet) must still read as "nothing loaded" rather than falling through to Ok.
        internal SaveFileLoadOutcome Outcome { get; private set; } = SaveFileLoadOutcome.Cancel;
        internal SaveFileInfo? SaveFileInfo { get; set; }

        //Ports LoadSaveFile's LastSaveFileLocation write (reference §5 step 6): the caller (SettingsWindow)
        //owns actually persisting this into AppSettings, since this sheet has no SettingsService of its own.
        internal string? ResolvedSaveFileLocation { get; private set; }

        //Test-only seams (see ImageExportSheet's SaveFilePathStub for the established convention).
        internal Func<Task<string?>>? OpenSaveFilePathStub { get; set; }
        internal Func<string, string, Task<bool>>? ConfirmDialogStub { get; set; }
        //Lets a test replace SaveFileReader.Load with a scripted result, without spawning a real (or even
        //stub-script) process - SaveFileReader's own pipeline correctness is covered separately against
        //StubFactorioHarness under ForemanTest (Foreman.Core), where that harness actually lives.
        internal Func<string, CancellationToken, SaveFileReader.Result>? LoadPipelineStub { get; set; }
        internal Func<Task>? ShowLogStub { get; set; }

        //The save being read; set by the picker, kept for Try Again.
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

        //Ports CancellationButton_Click (reference §5): does not await the in-flight load - the run isn't
        //preemptive (docs/io-reference.md §4 caution, FactorioBenchmarkRunner.Run blocks on ReadToEnd until
        //the child exits), so this completes the sheet immediately and leaves that background task to finish
        //on its own. Overrides SheetContent<T>.Cancel so the Cancel button and SheetHost's own Escape
        //handling (which calls this same method) behave identically.
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

        //Ports the upstream "using var form = new SaveFileLoadForm(...)" disposal (reference §5's caller,
        //SettingsForm.cs:535): cts is the only disposable field, released once the sheet completes.
        public void Dispose() {
            if (disposed)
                return;
            disposed = true;
            cts.Dispose();
            GC.SuppressFinalize(this);
        }

        //Ports ProgressForm_Load (reference §5): file picker -> SaveFileReader.Load -> ProcessSaveData on
        //success.
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
            if (completed) //Cancel already completed the sheet while the run was in flight - discard the late result.
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

        //Ports ProcessSaveData (reference §5): mod-mismatch gate, then §§-pseudo-recipes always enabled
        //plus save-driven recipe enables, then the shared transitive assembler/beacon/module derivation
        //(EnabledObjectsDerivation, reused by SciencePacksSheet - io-reference.md risk 5).
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

        //One line per non-empty side, each capped at five names; the full lists go to the log.
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

        //-------------------------------------------------------------------------------------------------Test-only seams

        internal Button CancelButtonControl => cancelButton;
        internal Button LoadButtonControl => loadButton;
        internal Button ShowLogButtonControl => showLogButton;
        internal void SimulateCancelClick() => Cancel();
        internal bool Disposed => disposed;
    }
}
