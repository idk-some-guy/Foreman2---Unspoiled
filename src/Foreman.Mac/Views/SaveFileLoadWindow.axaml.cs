using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    //Ports SaveFileLoadForm(+.Designer.cs) (reference io-reference.md §5, upstream SavefileLoadForm.cs):
    //runs immediately on show - picks a save .zip, then hands it to SaveFileReader.Load (the process
    //pipeline, Foreman.Core) and applies the parsed result to the live enabled-objects set. Cancellation
    //is honest about FactorioBenchmarkRunner.Run not being preemptive (docs/io-reference.md §4 caution):
    //the Cancel button closes the window immediately without waiting for an in-flight run to actually stop.
    public partial class SaveFileLoadWindow : Window, IDisposable {
        private readonly DataCache dCache;
        private readonly HashSet<IDataObjectBase> enabledObjects;
        private readonly CancellationTokenSource cts = new();
        private readonly string defaultSaveFileLocation;
        private readonly Button cancelButton;
        private bool closed;
        private bool disposed;

        internal SaveFileLoadOutcome Outcome { get; private set; }
        internal SaveFileInfo? SaveFileInfo { get; set; }

        //Ports LoadSaveFile's LastSaveFileLocation write (reference §5 step 6): the caller (SettingsWindow)
        //owns actually persisting this into AppSettings, since this window has no SettingsService of its own.
        internal string? ResolvedSaveFileLocation { get; private set; }

        //Test-only seams (see ImageExportWindow's SaveFilePathStub/WarningDialogStub for the established
        //convention).
        internal Func<Task<string?>>? OpenSaveFilePathStub { get; set; }
        internal Func<string, string, Task>? WarningDialogStub { get; set; }
        internal Func<string, string, Task<bool>>? ConfirmDialogStub { get; set; }
        //Lets a test replace SaveFileReader.Load with a scripted result, without spawning a real (or even
        //stub-script) process - SaveFileReader's own pipeline correctness is covered separately against
        //StubFactorioHarness under ForemanTest (Foreman.Core), where that harness actually lives.
        internal Func<string, CancellationToken, SaveFileReader.Result>? LoadPipelineStub { get; set; }

        public SaveFileLoadWindow() : this(new DataCache(false), []) {
        }

        public SaveFileLoadWindow(DataCache cache, HashSet<IDataObjectBase> enabledObjects, string? lastSaveFileLocation = null) {
            InitializeComponent();
            dCache = cache;
            this.enabledObjects = enabledObjects;
            cancelButton = this.FindControl<Button>("CancelButton")!;
            defaultSaveFileLocation = SaveFileReader.ResolveDefaultSaveFileLocation(lastSaveFileLocation);

            cancelButton.Click += (_, _) => CancelLoad();
            Opened += (_, _) => Async.Fire(RunAsync(), nameof(RunAsync));
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Ports CancellationButton_Click (reference §5): does not await the in-flight load - the run isn't
        //preemptive (docs/io-reference.md §4 caution, FactorioBenchmarkRunner.Run blocks on ReadToEnd until
        //the child exits), so this closes the window immediately and leaves that background task to finish
        //on its own.
        private void CancelLoad() {
            cts.Cancel();
            Outcome = SaveFileLoadOutcome.Cancel;
            SaveFileInfo = null;
            CloseOnce();
        }

        private void CloseOnce() {
            if (closed)
                return;
            closed = true;
            Close();
        }

        //Ports the upstream "using var form = new SaveFileLoadForm(...)" disposal (reference §5's caller,
        //SettingsForm.cs:535): cts is the only disposable field, released once the window actually closes.
        public void Dispose() {
            if (disposed)
                return;
            disposed = true;
            cts.Dispose();
            GC.SuppressFinalize(this);
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            Dispose();
        }

        //Ports ProgressForm_Load (reference §5): file picker -> SaveFileReader.Load -> ProcessSaveData on
        //success.
        internal async Task RunAsync() {
            string? path = await (OpenSaveFilePathStub?.Invoke() ?? RealPickSaveFileAsync()).ConfigureAwait(true);
            if (path is null) {
                Outcome = SaveFileLoadOutcome.Cancel;
                SaveFileInfo = null;
                CloseOnce();
                return;
            }

            CancellationToken token = cts.Token;
            Func<string, CancellationToken, SaveFileReader.Result> load = LoadPipelineStub ?? SaveFileReader.Load;
            SaveFileReader.Result result = await Task.Run(() => load(path, token)).ConfigureAwait(true);
            if (closed) //Cancel already closed the window while the run was in flight - discard the late result.
                return;

            Outcome = result.Outcome;
            SaveFileInfo = result.SaveFileInfo;
            if (result.WarningMessage is string message)
                await ShowWarningAsync("", message).ConfigureAwait(true);

            if (result.Outcome == SaveFileLoadOutcome.Ok) {
                ResolvedSaveFileLocation = Path.GetDirectoryName(path);
                await ProcessSaveDataAsync().ConfigureAwait(true);
            }
            CloseOnce();
        }

        private async Task<string?> RealPickSaveFileAsync() {
            if (StorageProvider is not IStorageProvider storage)
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
        //(EnabledObjectsDerivation, reused by SciencePacksWindow once it exists - io-reference.md risk 5).
        internal async Task ProcessSaveDataAsync() {
            string missingMods = "\nMissing Mods: ";
            string wrongVersionMods = "\nWrong Version Mods: ";
            string newMods = "\nAdded Mods: ";

            foreach (KeyValuePair<string, string> mod in dCache.IncludedMods) {
                if (mod.Key is "foremanexport" or "foremansavereader" or "core")
                    continue;
                if (SaveFileInfo?.Mods.ContainsKey(mod.Key) is false)
                    missingMods += mod.Key + ", ";
                else if (SaveFileInfo?.Mods[mod.Key] != mod.Value)
                    wrongVersionMods += mod.Key + ", ";
            }
            foreach (KeyValuePair<string, string> mod in SaveFileInfo?.Mods ?? [])
                if (mod.Key is not ("foremanexport" or "foremansavereader" or "core") && !dCache.IncludedMods.ContainsKey(mod.Key))
                    newMods += mod.Key + ", ";

            missingMods = missingMods[..^2];
            if (missingMods == "\nMissing Mods")
                missingMods = "";
            wrongVersionMods = wrongVersionMods[..^2];
            if (wrongVersionMods == "\nWrong Version Mods")
                wrongVersionMods = "";
            newMods = newMods[..^2];
            if (newMods == "\nAdded Mods")
                newMods = "";

            if (!string.IsNullOrEmpty(missingMods) || !string.IsNullOrEmpty(wrongVersionMods) || !string.IsNullOrEmpty(newMods)) {
                bool proceed = await ShowConfirmAsync("Save file mod inconsistencies found!",
                    "selected save file mods do not match preset mods; out of {0} mods:" + missingMods + wrongVersionMods + newMods +
                    "\nAre you sure you wish to use this save file?").ConfigureAwait(true);
                if (!proceed)
                    return;
            }

            EnabledObjectsDerivation.ResetToPlayerAssembler(dCache, enabledObjects);
            foreach (IRecipe recipe in dCache.Recipes.Values)
                if (recipe.Name.StartsWith("§§", StringComparison.Ordinal) || (SaveFileInfo?.Recipes.ContainsKey(recipe.Name) is true && SaveFileInfo.Recipes[recipe.Name]))
                    enabledObjects.Add(recipe);
            EnabledObjectsDerivation.DeriveAssemblersBeaconsModules(dCache, enabledObjects);
        }

        private Task ShowWarningAsync(string title, string message) =>
            WarningDialogStub?.Invoke(title, message) ?? Dialogs.ShowWarningAsync(this, title, message);

        private Task<bool> ShowConfirmAsync(string title, string message) =>
            ConfirmDialogStub?.Invoke(title, message) ?? Dialogs.ShowConfirmAsync(this, title, message);

        //-------------------------------------------------------------------------------------------------Test-only seams

        internal Button CancelButtonControl => cancelButton;
        internal void SimulateCancelClick() => CancelLoad();
    }
}
