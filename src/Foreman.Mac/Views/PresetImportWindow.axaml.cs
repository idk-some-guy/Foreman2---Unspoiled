using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Foreman;
using Foreman.DataCaching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Views {
    //Ports PresetImportForm(+.designer.cs) (reference io-reference.md §4, upstream PresetImportForm.cs): the
    //Import-preset-from-Factorio dialog. Pre-flight validation lives here; the process pipeline itself
    //(PresetImporter, Foreman.Core) is UI-independent and tested against StubFactorioHarness separately.
    //FactorioLocationComboBox is a real (non-editable) ComboBox again, listing every detected candidate; the
    //editable TextBox next to it stays the authoritative free-text value, since Avalonia's ComboBox has no
    //editable-freetext mode without pulling in AutoCompleteBox. ModsLocationComboBox stays a plain TextBox -
    //upstream never seeds it with any candidate at all (docs/upstream-divergences.md).
    public partial class PresetImportWindow : Window, IDisposable {
        private readonly List<Preset> existingPresets;
        private readonly CancellationTokenSource cts = new();
        private bool disposed;

        private readonly ComboBox factorioLocationComboBox;
        private readonly TextBox factorioLocationTextBox;
        private readonly Button factorioBrowseButton;
        private readonly TextBox presetNameTextBox;
        private readonly TextBox modsLocationTextBox;
        private readonly Button modsBrowseButton;
        private readonly Button okButton;
        private readonly Button cancelButton;
        private readonly StackPanel progressPanel;
        private readonly ProgressBar progressBar;
        private readonly TextBlock statusTextBlock;

        public string NewPresetName { get; private set; } = "";
        public bool ImportStarted { get; private set; }
        internal bool? DialogResultValue { get; private set; }

        //Test-only seam: overrides the home directory FactorioPathsProcessor.GetFactorioUserPath resolves
        //the mods-folder auto-detect fallback against - without this, a mods-folder auto-detect test would
        //silently reach into whatever real ~/Library/Application Support/factorio a dev machine happens to
        //have (a real, standing Factorio install is entirely plausible on this project's own dev machine).
        internal string? HomeOverride { get; set; }

        //Test-only seams (see ImageExportWindow's SaveFilePathStub for the established convention).
        internal Func<Task<string?>>? BrowseFactorioLocationStub { get; set; }
        internal Func<Task<string?>>? BrowseModsLocationStub { get; set; }
        internal Func<string, string, Task>? WarningDialogStub { get; set; }
        internal Func<string, string, Task<bool>>? ConfirmDialogStub { get; set; }
        //Lets a test replace PresetImporter.ProcessPreset with a scripted result, without spawning a real
        //(or stub-script) process - the pipeline's own correctness is covered separately in ForemanTest
        //against StubFactorioHarness, where that harness actually lives.
        internal Func<string, string, string, string, string, IProgress<KeyValuePair<int, string>>, Func<int, int, Task<bool>>, CancellationToken, Task<PresetImporter.Result>>? ProcessPresetStub { get; set; }

        public PresetImportWindow() : this([]) {
        }

        public PresetImportWindow(List<Preset> existingPresets) : this(existingPresets, null) {
        }

        //Test-only seam: lets a test seed the Factorio-location picker from a fixture list instead of the
        //real machine's FactorioPathsProcessor.GetFactorioInstallLocations() - needed here (unlike
        //HomeOverride below) because the picker has to be populated during construction, before a test gets
        //a chance to set any post-construction property.
        internal PresetImportWindow(List<Preset> existingPresets, List<string>? installLocationsOverride) {
            InitializeComponent();
            this.existingPresets = existingPresets;

            factorioLocationComboBox = this.FindControl<ComboBox>("FactorioLocationComboBox")!;
            factorioLocationTextBox = this.FindControl<TextBox>("FactorioLocationTextBox")!;
            factorioBrowseButton = this.FindControl<Button>("FactorioBrowseButton")!;
            presetNameTextBox = this.FindControl<TextBox>("PresetNameTextBox")!;
            modsLocationTextBox = this.FindControl<TextBox>("ModsLocationTextBox")!;
            modsBrowseButton = this.FindControl<Button>("ModsBrowseButton")!;
            okButton = this.FindControl<Button>("OKButton")!;
            cancelButton = this.FindControl<Button>("CancelButton")!;
            progressPanel = this.FindControl<StackPanel>("ProgressPanel")!;
            progressBar = this.FindControl<ProgressBar>("ImportProgressBar")!;
            statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock")!;

            List<string> installLocations = installLocationsOverride ?? FactorioPathsProcessor.GetFactorioInstallLocations();
            factorioLocationTextBox.Text = installLocations.FirstOrDefault() ?? "";
            factorioLocationComboBox.ItemsSource = installLocations;
            factorioLocationComboBox.IsVisible = installLocations.Count > 1;
            factorioLocationComboBox.SelectionChanged += (_, _) => {
                if (factorioLocationComboBox.SelectedItem is string selected)
                    factorioLocationTextBox.Text = selected;
            };

            factorioBrowseButton.Click += (_, _) => Async.Fire(BrowseFactorioLocationAsync(), nameof(BrowseFactorioLocationAsync));
            modsBrowseButton.Click += (_, _) => Async.Fire(BrowseModsLocationAsync(), nameof(BrowseModsLocationAsync));
            //TextChanging (not TextChanged) - fires synchronously per keystroke, matching upstream's
            //immediate per-keystroke PresetNameTextBox_TextChanged (reference §4 step 8). The seeded default
            //text above is never re-validated (upstream sets it before wiring TextChanged too), so this
            //stays Moccasin - the XAML default - until the user actually types.
            presetNameTextBox.TextChanging += (_, _) => OnPresetNameChanging();
            okButton.Click += (_, _) => Async.Fire(RunImportAsync(), nameof(RunImportAsync));
            cancelButton.Click += (_, _) => CancelImport();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Ports the upstream "using var form = new PresetImportForm()" disposal (reference §7's caller,
        //SettingsForm.cs:485): cts is the only disposable field, released once the window actually closes.
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

        //Ports PresetNameTextBox_TextChanged (reference §4 step 8) verbatim: filter to
        //letters/digits/"()-_. ", preserve caret position, then color Moccasin (<5 chars) / Pink (name
        //collision) / LightGreen (OK).
        private void OnPresetNameChanging() {
            int caret = presetNameTextBox.CaretIndex;
            string text = presetNameTextBox.Text ?? "";
            string filtered = PresetImporter.FilterName(text);
            if (filtered != text) {
                caret = Math.Max(caret + filtered.Length - text.Length, 0);
                presetNameTextBox.Text = filtered;
                presetNameTextBox.CaretIndex = caret;
            }

            presetNameTextBox.Background = filtered.Length < 5
                ? Brushes.Moccasin
                : existingPresets.Any(p => string.Equals(p.Name, filtered, StringComparison.OrdinalIgnoreCase))
                ? Brushes.Pink
                : Brushes.LightGreen;
        }

        private void EnableProgressBar(bool enabled) {
            progressPanel.IsVisible = enabled;
            factorioLocationTextBox.IsEnabled = !enabled;
            factorioBrowseButton.IsEnabled = !enabled;
            presetNameTextBox.IsEnabled = !enabled;
            modsLocationTextBox.IsEnabled = !enabled;
            modsBrowseButton.IsEnabled = !enabled;
            okButton.IsEnabled = !enabled;
        }

        //Ports FactorioBrowseButton_Click (reference §4): message adapted per platform (docs/upstream-divergences.md) -
        //upstream's own text names "bin"/"data"/"config-path.cfg", which don't exist on a mac install, and
        //Linux has no .app bundle wrapper at all.
        internal async Task BrowseFactorioLocationAsync() {
            string? selected = await (BrowseFactorioLocationStub?.Invoke() ?? RealPickFolderAsync()).ConfigureAwait(true);
            if (selected is null)
                return;

            if (FactorioPathsProcessor.TryNormalizeInstallPath(selected, out string installRoot))
                factorioLocationTextBox.Text = installRoot;
            else
                await ShowWarningAsync("", OperatingSystem.IsMacOS()
                    ? "Selected directory doesnt seem to be a factorio install folder (it should at the very least have a \"factorio.app/Contents/MacOS/factorio\" executable)"
                    : "Selected directory doesnt seem to be a factorio install folder (it should at the very least have a \"bin/x64/factorio\" executable)").ConfigureAwait(true);
        }

        //Ports ModsBrowseButton_Click (reference §4) verbatim - no OS-specific wording to adapt.
        internal async Task BrowseModsLocationAsync() {
            string? selected = await (BrowseModsLocationStub?.Invoke() ?? RealPickFolderAsync()).ConfigureAwait(true);
            if (selected is null)
                return;

            if (File.Exists(Path.Combine(selected, "mod-list.json")))
                modsLocationTextBox.Text = selected;
            else
                await ShowWarningAsync("",
                    "Selected directory doesnt seem to be a factorio mods folder (it should at the very least have \"mod-list.json\" file)").ConfigureAwait(true);
        }

        private async Task<string?> RealPickFolderAsync() {
            if (StorageProvider is not IStorageProvider storage)
                return null;

            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                AllowMultiple = false,
            }).ConfigureAwait(true);
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }

        //Ports CancelButton_Click (reference §4) verbatim.
        private void CancelImport() {
            cts.Cancel();
            DialogResultValue = false;
            NewPresetName = "";
            Close();
        }

        //Ports OKButton_Click's pre-flight validation + pipeline kickoff (reference §4 steps 1-7). Skips
        //upstream's four defensive CleanupFailedImport() calls in the pre-flight branches
        //(PresetImportForm.cs:87,92,99,103, before ProcessPreset ever runs) - those only mattered for debris
        //a PREVIOUS attempt in the same dialog might have left behind under upstream's own dead-parameter
        //cleanup bug (docs/upstream-divergences.md); this port's pipeline already cleans up completely on
        //every one of its own failure paths, so a fresh OK click has nothing left over to clean.
        internal async Task RunImportAsync() {
            NewPresetName = presetNameTextBox.Text ?? "";
            if (!Directory.Exists(factorioLocationTextBox.Text)) {
                await ShowWarningAsync("", "That directory doesn't seem to exist").ConfigureAwait(true);
                return;
            }
            if (NewPresetName.Length < 5) {
                await ShowWarningAsync("", "Preset name has to be longer than 5!").ConfigureAwait(true);
                return;
            }

            if (string.Equals(NewPresetName, Services.PresetResolver.DefaultPresetName, StringComparison.OrdinalIgnoreCase)) {
                await ShowWarningAsync("", "Cant overwrite default preset!").ConfigureAwait(true);
                return;
            }
            if (existingPresets.Any(p => string.Equals(p.Name, NewPresetName, StringComparison.OrdinalIgnoreCase))) {
                bool overwrite = await ShowConfirmAsync("Confirm Overwrite", "This preset name is already in use. Do you wish to overwrite?").ConfigureAwait(true);
                if (!overwrite)
                    return;
            }

            if (!FactorioPathsProcessor.TryNormalizeInstallPath(factorioLocationTextBox.Text ?? "", out string installPath)) {
                await ShowWarningAsync("", OperatingSystem.IsMacOS()
                    ? "Couldnt find factorio (Contents/MacOS/factorio) - please select a valid Factorio install location"
                    : "Couldnt find factorio (bin/x64/factorio) - please select a valid Factorio install location").ConfigureAwait(true);
                return;
            }

            string factorioExePath = FactorioPathsProcessor.GetExecutablePath(installPath);
            if (!FactorioInstallValidator.TryValidateExecutable(factorioExePath, out string? factorioVersionError)) {
                await ShowWarningAsync("", factorioVersionError).ConfigureAwait(true);
                return;
            }

            string modsPath = modsLocationTextBox.Text ?? "";
            if (string.IsNullOrEmpty(modsPath) || !File.Exists(Path.Combine(modsPath, "mod-list.json"))) {
                string userDataPath = FactorioPathsProcessor.GetFactorioUserPath(installPath, verboseFail: true, HomeOverride);
                if (string.IsNullOrEmpty(userDataPath)) {
                    await ShowWarningAsync("", "Couldnt auto-locate the mods folder - please manually locate the folder").ConfigureAwait(true);
                    return;
                }
                modsPath = Path.Combine(userDataPath, "mods");
            }

            EnableProgressBar(true);
            var progress = new Progress<KeyValuePair<int, string>>(value => {
                if (value.Key > progressBar.Value)
                    progressBar.Value = value.Key;
                if (!string.IsNullOrEmpty(value.Value) && value.Value != statusTextBlock.Text)
                    statusTextBlock.Text = value.Value;
            });

            ImportStarted = true;
            var process = ProcessPresetStub ?? PresetImporter.ProcessPreset;
            PresetImporter.Result result = await Task.Run(() => process(
                installPath, modsPath, AppPaths.ScratchDirectory, NewPresetName, AppPaths.UserPresetsDirectory,
                progress, ConfirmContinueWithMissingIconsAsync, cts.Token)).ConfigureAwait(true);

            if (result.Outcome == PresetImportOutcome.Ok) {
                NewPresetName = result.NewPresetName;
                DialogResultValue = true;
                Close();
            } else {
                NewPresetName = "";
                EnableProgressBar(false);
                if (result.WarningMessage is string message)
                    await ShowWarningAsync("", message).ConfigureAwait(true);
            }
        }

        //Ports the icon-partial-failure Yes/No prompt (reference §4 step 6, PresetImportForm.cs:373). Called
        //from PresetImporter's background execution (Task.Run above); marshals onto the UI thread since it
        //has to show a real dialog, then hands the awaited answer back to the pipeline's calling thread.
        private Task<bool> ConfirmContinueWithMissingIconsAsync(int failedCount, int totalCount) {
            var tcs = new TaskCompletionSource<bool>();
            string message = string.Format(DisplayCulture.Format,
                "{0}/{1} images that were processed for icons were not found and thus some icons are likely wrong/empty. Do you still wish to continue with the preset import?",
                failedCount, totalCount);
            Dispatcher.UIThread.Post(async () => tcs.SetResult(await ShowConfirmAsync("Confirm Preset Import", message).ConfigureAwait(true)));
            return tcs.Task;
        }

        private Task ShowWarningAsync(string title, string message) =>
            WarningDialogStub?.Invoke(title, message) ?? Dialogs.ShowWarningAsync(this, title, message);

        private Task<bool> ShowConfirmAsync(string title, string message) =>
            ConfirmDialogStub?.Invoke(title, message) ?? Dialogs.ShowConfirmAsync(this, title, message);

        //-------------------------------------------------------------------------------------------------Test-only seams

        internal ComboBox FactorioLocationComboBoxControl => factorioLocationComboBox;
        internal TextBox FactorioLocationTextBoxControl => factorioLocationTextBox;
        internal TextBox PresetNameTextBoxControl => presetNameTextBox;
        internal TextBox ModsLocationTextBoxControl => modsLocationTextBox;
        internal Button OKButtonControl => okButton;
        internal Button CancelButtonControl => cancelButton;
        internal ProgressBar ProgressBarControl => progressBar;
        internal bool ProgressPanelVisible => progressPanel.IsVisible;

        internal void SimulateCancelClick() => CancelImport();

        //Lets a SettingsWindow-level test drive the post-import wiring (add-to-list / RequireReload / switch
        //prompt) without running this window's own pre-flight validation and pipeline, which are covered on
        //their own in PresetImportWindowTests/PresetImporterTests.
        internal void SimulateImportSuccess(string presetName) {
            NewPresetName = presetName;
            ImportStarted = true;
            DialogResultValue = true;
        }
    }
}
