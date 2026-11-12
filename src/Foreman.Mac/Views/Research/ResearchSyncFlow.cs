using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Foreman.DataCaching;
using Foreman.Mac.Services;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Research {
    internal sealed class ResearchSyncFlow(Func<ProductionGraph> graph, Func<DataCache> cache, Func<AppSettings?> settings) {
        private Window? owner;

        public bool IsRunning { get; private set; }

        public event EventHandler? RunningChanged;

        internal Func<string?, Task<string?>> ChooseSavePath { get => field ?? PickSaveAsync; set; }

        internal Func<ResearchSyncSheet, Task<ResearchSyncOutcome>>? SheetStub { get; set; }

        internal Action? ShowResearch { get; set; }

        internal Action<string>? Announce { get; set; }

        internal Func<object?>? DocumentIdentity { get; set; }

        public async Task RunAsync(Window owner, bool chooseAnother) {
            if (IsRunning)
                return;
            this.owner = owner;
            SetRunning(true);
            try {
                string? stored = graph().Research.Source?.SavePath;
                string? path = stored;
                bool freshlyChosen = false;
                if (path is null || chooseAnother) {
                    path = await ChooseAsync(stored is null ? null : Path.GetDirectoryName(stored)).ConfigureAwait(true);
                    freshlyChosen = true;
                }

                while (path is not null) {
                    ResearchSyncOutcome outcome = await ShowSheetAsync(owner, path, missing: !freshlyChosen && !File.Exists(path)).ConfigureAwait(true);
                    if (outcome == ResearchSyncOutcome.ShowResearch)
                        ShowResearch?.Invoke();
                    if (outcome != ResearchSyncOutcome.ChooseAnotherSave)
                        return;
                    path = await ChooseAsync(Path.GetDirectoryName(stored ?? path)).ConfigureAwait(true);
                    freshlyChosen = true;
                }
            } finally {
                SetRunning(false);
            }
        }

        private Task<string?> ChooseAsync(string? startFolder) => ChooseSavePath(startFolder);

        private Task<ResearchSyncOutcome> ShowSheetAsync(Window host, string path, bool missing) {
            var sheet = new ResearchSyncSheet(graph(), cache(), path) { StoredSaveMissing = missing, Announce = Announce, DocumentIdentity = DocumentIdentity };
            return SheetStub is { } stub ? stub(sheet) : SheetHost.For(host).ShowAsync(sheet);
        }

        private void SetRunning(bool running) {
            if (IsRunning == running)
                return;
            IsRunning = running;
            RunningChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task<string?> PickSaveAsync(string? startFolder) {
            if (owner?.StorageProvider is not IStorageProvider storage)
                return null;

            string folder = startFolder ?? SaveFileReader.ResolveDefaultSaveFileLocation(settings()?.LastSaveFileLocation);
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Choose a Factorio Save",
                AllowMultiple = false,
                SuggestedStartLocation = string.IsNullOrEmpty(folder)
                    ? null
                    : await storage.TryGetFolderFromPathAsync(new Uri(folder)).ConfigureAwait(true),
                FileTypeFilter = [new FilePickerFileType("factorio saves") { Patterns = ["*.zip"] }],
            }).ConfigureAwait(true);
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }
    }
}
