using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Foreman.DataCaching;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public partial class PresetImportSheet : SheetContent<bool?>, IDisposable {
        private readonly List<Preset> existingPresets;
        private readonly CancellationTokenSource cts = new();
        private bool disposed;

        private readonly FormRow factorioLocationRow;
        private readonly FormRow modsLocationRow;
        private readonly PopUp factorioLocationComboBox;
        private readonly TextBox factorioLocationTextBox;
        private readonly Button factorioBrowseButton;
        private readonly FormRow presetNameRow;
        private readonly TextBox presetNameTextBox;
        private readonly TextBox modsLocationTextBox;
        private readonly Button modsBrowseButton;
        private readonly Button okButton;
        private readonly Button cancelButton;
        private readonly Button showLogButton;

        public string NewPresetName { get; private set; } = "";
        public bool ImportStarted { get; private set; }
        internal bool? DialogResultValue { get; private set; }

        internal string? HomeOverride { get; set; }

        internal Func<Task<string?>>? BrowseFactorioLocationStub { get; set; }
        internal Func<Task<string?>>? BrowseModsLocationStub { get; set; }
        internal Func<string, string, Task<bool>>? ConfirmDialogStub { get; set; }
        internal Func<Task>? ShowLogStub { get; set; }
        internal Func<string, string, string, string, string, IProgress<KeyValuePair<int, string>>, Func<int, int, Task<bool>>, CancellationToken, Task<PresetImporter.Result>>? ProcessPresetStub { get; set; }

        public PresetImportSheet() : this([]) {
        }

        public PresetImportSheet(List<Preset> existingPresets) : this(existingPresets, null) {
        }

        internal PresetImportSheet(List<Preset> existingPresets, List<string>? installLocationsOverride) {
            InitializeComponent();
            this.existingPresets = existingPresets;
            WidthClass = SheetWidthClass.Wide;
            Title = "Import a preset";

            factorioLocationRow = this.FindControl<FormRow>("FactorioLocationRow")!;
            modsLocationRow = this.FindControl<FormRow>("ModsLocationRow")!;
            factorioLocationComboBox = this.FindControl<PopUp>("FactorioLocationComboBox")!;
            factorioLocationTextBox = this.FindControl<TextBox>("FactorioLocationTextBox")!;
            factorioBrowseButton = this.FindControl<Button>("FactorioBrowseButton")!;
            presetNameRow = this.FindControl<FormRow>("PresetNameRow")!;
            presetNameTextBox = this.FindControl<TextBox>("PresetNameTextBox")!;
            modsLocationTextBox = this.FindControl<TextBox>("ModsLocationTextBox")!;
            modsBrowseButton = this.FindControl<Button>("ModsBrowseButton")!;
            showLogButton = SheetButtons.Make("Show Log…", ButtonRowRole.Helper);
            showLogButton.IsVisible = false;
            cancelButton = SheetButtons.Make("Cancel", ButtonRowRole.Cancel);
            okButton = SheetButtons.Make("Import", ButtonRowRole.Default);
            Buttons = SheetButtons.Row(showLogButton, cancelButton, okButton);
            DefaultButton = okButton;

            List<string> installLocations = installLocationsOverride ?? FactorioPathsProcessor.GetFactorioInstallLocations();
            factorioLocationTextBox.Text = installLocations.FirstOrDefault() ?? "";
            foreach (string location in installLocations)
                factorioLocationComboBox.Items.Add(location);
            factorioLocationComboBox.SelectedItem = installLocations.FirstOrDefault();
            factorioLocationComboBox.IsVisible = installLocations.Count > 1;
            factorioLocationComboBox.PropertyChanged += (_, e) => {
                if (e.Property == PopUp.SelectedItemProperty && factorioLocationComboBox.SelectedItem is string selected)
                    factorioLocationTextBox.Text = selected;
            };

            factorioBrowseButton.Click += (_, _) => Async.Fire(BrowseFactorioLocationAsync(), nameof(BrowseFactorioLocationAsync));
            modsBrowseButton.Click += (_, _) => Async.Fire(BrowseModsLocationAsync(), nameof(BrowseModsLocationAsync));
            //The seeded default text is never re-validated; its row shows only the naming rule until the user types.
            presetNameTextBox.TextChanging += (_, _) => OnPresetNameChanging();
            okButton.Click += (_, _) => Async.Fire(RunImportAsync(), nameof(RunImportAsync));
            cancelButton.Click += (_, _) => Cancel();
            showLogButton.Click += (_, _) => Async.Fire(ShowLogAsync(), nameof(ShowLogAsync));
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        public void Dispose() {
            if (disposed)
                return;
            disposed = true;
            cts.Dispose();
            GC.SuppressFinalize(this);
        }

        private void OnPresetNameChanging() {
            int caret = presetNameTextBox.CaretIndex;
            string text = presetNameTextBox.Text ?? "";
            string filtered = PresetImporter.FilterName(text);
            if (filtered != text) {
                caret = Math.Max(caret + filtered.Length - text.Length, 0);
                presetNameTextBox.Text = filtered;
                presetNameTextBox.CaretIndex = caret;
            }

            presetNameRow.NoteIsWarning = filtered.Length < 5;
            if (filtered.Length < 5)
                SetNameProblem(null, warning: false);
            else if (Services.PresetResolver.BundledPresetNameMatching(filtered) is { } builtIn)
                SetNameProblem(BuiltInNameSentence(builtIn), warning: false);
            else if (existingPresets.Any(p => string.Equals(p.Name, filtered, StringComparison.OrdinalIgnoreCase)))
                SetNameProblem("This name is in use. Importing replaces that preset.", warning: true);
            else
                SetNameProblem(null, warning: false);
        }

        private static string BuiltInNameSentence(string name) => $"“{name}” is built in and can’t be replaced. Choose another name.";

        private void SetNameProblem(string? sentence, bool warning) {
            presetNameRow.ErrorIsWarning = warning;
            presetNameRow.Error = sentence;
        }

        private static void ShowFieldProblem(FormRow row, Control field, string sentence) {
            row.ErrorIsWarning = false;
            row.Error = sentence;
            field.Focus();
        }

        private void EnableProgressBar(bool enabled) {
            factorioLocationComboBox.IsEnabled = !enabled;
            factorioLocationTextBox.IsEnabled = !enabled;
            factorioBrowseButton.IsEnabled = !enabled;
            presetNameTextBox.IsEnabled = !enabled;
            modsLocationTextBox.IsEnabled = !enabled;
            modsBrowseButton.IsEnabled = !enabled;
        }

        internal async Task BrowseFactorioLocationAsync() {
            string? selected = await (BrowseFactorioLocationStub?.Invoke() ?? RealPickFolderAsync()).ConfigureAwait(true);
            if (selected is null)
                return;

            if (FactorioPathsProcessor.TryNormalizeInstallPath(selected, out string installRoot)) {
                factorioLocationTextBox.Text = installRoot;
                factorioLocationRow.Error = null;
            } else {
                ShowFieldProblem(factorioLocationRow, factorioLocationTextBox, FailureSentences.NotAFactorioFolder);
            }
        }

        internal async Task BrowseModsLocationAsync() {
            string? selected = await (BrowseModsLocationStub?.Invoke() ?? RealPickFolderAsync()).ConfigureAwait(true);
            if (selected is null)
                return;

            if (File.Exists(Path.Combine(selected, "mod-list.json"))) {
                modsLocationTextBox.Text = selected;
                modsLocationRow.Error = null;
            } else {
                ShowFieldProblem(modsLocationRow, modsLocationTextBox, "This isn’t a Factorio mods folder. Choose the folder that holds mod-list.json.");
            }
        }

        private async Task<string?> RealPickFolderAsync() {
            if (TopLevel.GetTopLevel(this)?.StorageProvider is not IStorageProvider storage)
                return null;

            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                AllowMultiple = false,
            }).ConfigureAwait(true);
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }

        public override void Cancel() {
            cts.Cancel();
            DialogResultValue = false;
            NewPresetName = "";
            base.Cancel();
            Dispose();
        }

        internal async Task RunImportAsync() {
            ClearActivity();
            showLogButton.IsVisible = false;
            factorioLocationRow.Error = null;
            modsLocationRow.Error = null;
            NewPresetName = presetNameTextBox.Text ?? "";
            if (!Directory.Exists(factorioLocationTextBox.Text)) {
                ShowFieldProblem(factorioLocationRow, factorioLocationTextBox, "This folder doesn’t exist. Check the path, or click Browse… to choose it.");
                return;
            }
            if (NewPresetName.Length < 5) {
                ShowFieldProblem(presetNameRow, presetNameTextBox, "Preset names need at least 5 characters.");
                return;
            }

            if (Services.PresetResolver.BundledPresetNameMatching(NewPresetName) is { } builtIn) {
                ShowFieldProblem(presetNameRow, presetNameTextBox, BuiltInNameSentence(builtIn));
                return;
            }
            if (existingPresets.Any(p => string.Equals(p.Name, NewPresetName, StringComparison.OrdinalIgnoreCase))) {
                bool overwrite = await ShowConfirmAsync($"Replace the preset “{Dialogs.MiddleTruncate(NewPresetName)}”?",
                    "A preset with this name already exists. Importing replaces its items, recipes, and icons. You can’t undo this action.",
                    "Replace", destructive: true).ConfigureAwait(true);
                if (!overwrite)
                    return;
            }

            if (!FactorioPathsProcessor.TryNormalizeInstallPath(factorioLocationTextBox.Text ?? "", out string installPath)) {
                ShowFieldProblem(factorioLocationRow, factorioLocationTextBox, FailureSentences.NotAFactorioFolder);
                return;
            }

            string factorioExePath = FactorioPathsProcessor.GetExecutablePath(installPath);
            FactorioInstallCheck installCheck = FactorioInstallValidator.CheckExecutable(factorioExePath);
            if (installCheck.Problem != FactorioInstallProblem.None) {
                ShowFieldProblem(factorioLocationRow, factorioLocationTextBox, FailureSentences.Install(installCheck));
                return;
            }

            string modsPath = modsLocationTextBox.Text ?? "";
            if (string.IsNullOrEmpty(modsPath) || !File.Exists(Path.Combine(modsPath, "mod-list.json"))) {
                string userDataPath = FactorioPathsProcessor.GetFactorioUserPath(installPath, verboseFail: true, HomeOverride);
                if (string.IsNullOrEmpty(userDataPath)) {
                    ShowFieldProblem(modsLocationRow, modsLocationTextBox, "Foreman couldn’t find your mods folder. Click Browse… to choose it.");
                    return;
                }
                modsPath = Path.Combine(userDataPath, "mods");
            }

            EnableProgressBar(true);
            string quoted = $"“{Dialogs.MiddleTruncate(NewPresetName, 24)}”";
            string importing = $"Importing “{Dialogs.MiddleTruncate(NewPresetName, 40)}”";
            int percent = 0;
            ReportProgress(StageSentence(importing, 0), 0);
            var progress = new Progress<KeyValuePair<int, string>>(value => {
                if (disposed || Activity?.Kind != SheetActivityKind.InProgress)
                    return;
                percent = Math.Max(percent, value.Key);
                ReportProgress(StageSentence(importing, percent), percent / 100.0);
            });

            ImportStarted = true;
            Func<string, string, string, string, string, IProgress<KeyValuePair<int, string>>, Func<int, int, Task<bool>>, CancellationToken, Task<PresetImporter.Result>> defaultProcess =
                (a, b, c, d, e, f, g, h) => PresetImporter.ProcessPreset(a, installCheck.GameVersion!.Value, b, c, d, e, f, g, h);
            var process = ProcessPresetStub ?? defaultProcess;
            PresetImporter.Result result = await Task.Run(() => process(
                installPath, modsPath, AppPaths.ScratchDirectory, NewPresetName, AppPaths.UserPresetsDirectory,
                progress, ConfirmContinueWithMissingIconsAsync, cts.Token)).ConfigureAwait(true);
            if (disposed)
                return;

            ClearActivity();
            EnableProgressBar(false);
            if (result.Outcome == PresetImportOutcome.Ok) {
                NewPresetName = result.NewPresetName;
                DialogResultValue = true;
                Complete(true);
                Dispose();
            } else {
                NewPresetName = "";
                if (result.Outcome == PresetImportOutcome.Failed) {
                    ErrorLogging.LogLine("Preset import failed: " + result.WarningMessage);
                    showLogButton.IsVisible = true;
                    ReportFailure($"Couldn’t import {quoted}. {FailureSentences.Reason(result.Failure, result.CrashPhase, result.InstallCheck)}");
                }
            }
        }

        private static string StageSentence(string importing, int percent) => percent switch {
            < 10 => $"{importing}.",
            < 20 => $"{importing} — starting Factorio (1 of 4).",
            < 25 => $"{importing} — reading recipes and items from Factorio (2 of 4).",
            < 30 => $"{importing} — saving the preset (3 of 4).",
            _ => $"{importing} — creating icons (4 of 4).",
        };

        private Task ShowLogAsync() {
            if (ShowLogStub is not null)
                return ShowLogStub();
            return TopLevel.GetTopLevel(this)?.Launcher.LaunchFileInfoAsync(new FileInfo(ErrorLogging.LogFilePath)) ?? Task.CompletedTask;
        }

        //Called from the importer's background thread; the dialog must show on the UI thread.
        private Task<bool> ConfirmContinueWithMissingIconsAsync(int failedCount, int totalCount) {
            var tcs = new TaskCompletionSource<bool>();
            string message = string.Create(DisplayCulture.Format,
                $"{failedCount} of {totalCount} icon images weren’t found, so some icons may be wrong or empty.");
            Dispatcher.UIThread.Post(async () => tcs.SetResult(await ShowConfirmAsync("Continue importing with missing icons?", message, "Continue").ConfigureAwait(true)));
            return tcs.Task;
        }

        private Task<bool> ShowConfirmAsync(string title, string message, string verb, bool destructive = false) {
            if (ConfirmDialogStub is not null)
                return ConfirmDialogStub(title, message);
            var owner = (Window)TopLevel.GetTopLevel(this)!;
            return destructive ? Dialogs.ShowDestructiveConfirmAsync(owner, title, message, verb) : Dialogs.ShowConfirmAsync(owner, title, message, verb);
        }

        internal PopUp FactorioLocationComboBoxControl => factorioLocationComboBox;
        internal TextBox FactorioLocationTextBoxControl => factorioLocationTextBox;
        internal FormRow PresetNameRowControl => presetNameRow;
        internal TextBox PresetNameTextBoxControl => presetNameTextBox;
        internal TextBox ModsLocationTextBoxControl => modsLocationTextBox;
        internal Button OKButtonControl => okButton;
        internal Button CancelButtonControl => cancelButton;
        internal Button ShowLogButtonControl => showLogButton;
        internal bool Disposed => disposed;

        internal void SimulateCancelClick() => Cancel();

        internal void SimulateImportSuccess(string presetName) {
            NewPresetName = presetName;
            ImportStarted = true;
            DialogResultValue = true;
        }
    }
}
