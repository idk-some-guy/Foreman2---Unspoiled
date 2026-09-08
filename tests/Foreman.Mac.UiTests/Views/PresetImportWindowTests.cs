using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers PresetImportSheet's own wiring (io-reference.md §4, upstream PresetImportForm, moved onto
    //SheetHost by C2c Task 2): pre-flight validation, live name-color states, Cancel, and the hand-off into
    //PresetImporter.ProcessPreset (which is exercised for real against StubFactorioHarness in ForemanTest,
    //not here - see SaveFileLoadWindowTests for the established "this sheet's tests never touch a real
    //factorio process" convention).
    [UnsupportedOSPlatform("windows")]
    public class PresetImportWindowTests {
        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        //Answers just enough of the CLI contract (--version) for FactorioInstallValidator's pre-flight gate
        //to pass - the marker-delimited export pipeline itself is replaced by ProcessPresetStub below, so it
        //never needs to run for real here.
        private static string NewValidatingFactorioInstall(string version = "2.0.28") {
            string macOsDir = Path.Combine(NewTempDir(), "factorio.app", "Contents", "MacOS");
            Directory.CreateDirectory(macOsDir);
            string exePath = Path.Combine(macOsDir, "factorio");
            File.WriteAllText(exePath, "#!/bin/sh\nprintf 'Version: " + version + " (build 1, mac64, headless)\\n'\nexit 0\n");
            File.SetUnixFileMode(exePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return Path.GetDirectoryName(macOsDir)!; //the Contents dir - installPath
        }

        //---- live name validation (reference §4 step 8) ---------------------------------------------------

        [AvaloniaFact]
        public void PresetNameTextBox_FewerThanFiveChars_IsMoccasin() {
            var sheet = new PresetImportSheet([]);

            sheet.PresetNameTextBoxControl.Text = "Ab1";

            Assert.Equal(Avalonia.Media.Brushes.Moccasin, sheet.PresetNameTextBoxControl.Background);
        }

        [AvaloniaFact]
        public void PresetNameTextBox_UniqueValidName_IsLightGreen() {
            var sheet = new PresetImportSheet([]);

            sheet.PresetNameTextBoxControl.Text = "My New Preset";

            Assert.Equal(Avalonia.Media.Brushes.LightGreen, sheet.PresetNameTextBoxControl.Background);
        }

        [AvaloniaFact]
        public void PresetNameTextBox_CollidesWithExistingPreset_IsPink() {
            var sheet = new PresetImportSheet([new Preset("Taken Name", false, false)]);

            sheet.PresetNameTextBoxControl.Text = "taken name"; //case-insensitive collision

            Assert.Equal(Avalonia.Media.Brushes.Pink, sheet.PresetNameTextBoxControl.Background);
        }

        [AvaloniaFact]
        public void PresetNameTextBox_DisallowedCharacters_AreStrippedLive() {
            var sheet = new PresetImportSheet([]);

            sheet.PresetNameTextBoxControl.Text = "Ab1 (2)-_.!@#$";

            Assert.Equal("Ab1 (2)-_.", sheet.PresetNameTextBoxControl.Text);
        }

        //---- Factorio location picker (reference §4, upstream FactorioLocationComboBox) ---------------------

        [AvaloniaFact]
        public void FactorioLocationComboBox_MultipleCandidates_ListsAllAndIsVisible() {
            List<string> candidates = [NewTempDir(), NewTempDir(), NewTempDir()];

            var sheet = new PresetImportSheet([], candidates);

            Assert.True(sheet.FactorioLocationComboBoxControl.IsVisible);
            Assert.Equal(candidates, sheet.FactorioLocationComboBoxControl.ItemsSource);
        }

        [AvaloniaFact]
        public void FactorioLocationComboBox_SelectingACandidate_FillsTheTextBox() {
            List<string> candidates = [NewTempDir(), NewTempDir()];
            var sheet = new PresetImportSheet([], candidates);

            sheet.FactorioLocationComboBoxControl.SelectedItem = candidates[1];

            Assert.Equal(candidates[1], sheet.FactorioLocationTextBoxControl.Text);
        }

        [AvaloniaFact]
        public void FactorioLocationComboBox_SingleCandidate_IsHidden() {
            List<string> candidates = [NewTempDir()];

            var sheet = new PresetImportSheet([], candidates);

            Assert.False(sheet.FactorioLocationComboBoxControl.IsVisible);
            Assert.Equal(candidates[0], sheet.FactorioLocationTextBoxControl.Text);
        }

        [AvaloniaFact]
        public void FactorioLocationComboBox_NoCandidates_IsHidden() {
            var sheet = new PresetImportSheet([], []);

            Assert.False(sheet.FactorioLocationComboBoxControl.IsVisible);
            Assert.Equal("", sheet.FactorioLocationTextBoxControl.Text);
        }

        //---- Cancel (reference §4) -------------------------------------------------------------------------

        [AvaloniaFact]
        public void CancelButton_Click_ClearsNewPresetNameAndSetsDialogResultFalse() {
            var sheet = new PresetImportSheet([]);

            sheet.SimulateCancelClick();

            Assert.Equal("", sheet.NewPresetName);
            Assert.Equal(false, sheet.DialogResultValue);
        }

        //---- pre-flight validation matrix (reference §4 steps 1-7) ------------------------------------------

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioLocationDoesNotExist_ShowsVerbatimMessage() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            string? captured = null;
            sheet.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await sheet.RunImportAsync();

            Assert.Equal("That directory doesn't seem to exist", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_NameTooShort_ShowsVerbatimMessage() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewTempDir();
            sheet.PresetNameTextBoxControl.Text = "Ab1";
            string? captured = null;
            sheet.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await sheet.RunImportAsync();

            Assert.Equal("Preset name has to be longer than 5!", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_DefaultPresetName_ShowsVerbatimMessage() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewTempDir();
            sheet.PresetNameTextBoxControl.Text = "Factorio 2.0 Vanilla";
            string? captured = null;
            sheet.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await sheet.RunImportAsync();

            Assert.Equal("Cant overwrite default preset!", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_ExistingNameDeclinedOverwrite_StopsWithoutFurtherMessages() {
            var sheet = new PresetImportSheet([new Preset("Existing Preset", false, false)]);
            sheet.FactorioLocationTextBoxControl.Text = NewTempDir();
            sheet.PresetNameTextBoxControl.Text = "Existing Preset";
            bool warned = false;
            sheet.WarningDialogStub = (_, _) => { warned = true; return Task.CompletedTask; };
            sheet.ConfirmDialogStub = (_, _) => Task.FromResult(false);

            await sheet.RunImportAsync();

            Assert.False(warned);
            Assert.Null(sheet.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioExecutableNotFound_ShowsAdaptedVerbatimMessage() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewTempDir(); //exists, but no factorio inside
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            string? captured = null;
            sheet.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await sheet.RunImportAsync();

            Assert.Equal("Couldnt find factorio (Contents/MacOS/factorio) - please select a valid Factorio install location", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioVersionTooOld_ShowsTheValidatorMessage() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall("1.1.0");
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            string? captured = null;
            sheet.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await sheet.RunImportAsync();

            Assert.Contains("Factorio Version below 2.0", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_ModsFolderCannotBeAutoLocated_ShowsVerbatimMessage() {
            var sheet = new PresetImportSheet([]) { HomeOverride = NewTempDir() };
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            //no config-path.cfg, no default mac user-data folder under this fake home - auto-detect fails.
            string? captured = null;
            sheet.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await sheet.RunImportAsync();

            Assert.Equal("Couldnt auto-locate the mods folder - please manually locate the folder", captured);
        }

        //---- pipeline hand-off (ProcessPresetStub bypasses the real Factorio process entirely) -------------

        [AvaloniaFact]
        public async Task RunImportAsync_PipelineSucceeds_SetsDialogResultTrueAndNewPresetName() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet.ProcessPresetStub = (_, _, _, name, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Ok, NewPresetName = name });

            await sheet.RunImportAsync();

            Assert.Equal(true, sheet.DialogResultValue);
            Assert.Equal("Valid Name Here", sheet.NewPresetName);
            Assert.True(sheet.ImportStarted);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_PipelineFails_ShowsWarningAndReEnablesTheForm() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet.ProcessPresetStub = (_, _, _, _, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Failed, WarningMessage = "boom" });
            string? captured = null;
            sheet.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await sheet.RunImportAsync();

            Assert.Equal("boom", captured);
            Assert.Null(sheet.DialogResultValue);
            Assert.False(sheet.ProgressPanelVisible);
            Assert.Equal("", sheet.NewPresetName);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_PipelineCancelled_ShowsNoWarning() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet.ProcessPresetStub = (_, _, _, _, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Cancel });
            sheet.WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn on Cancel");

            await sheet.RunImportAsync();

            Assert.Null(sheet.DialogResultValue);
        }

        private static string WriteModsFolder() {
            string dir = NewTempDir();
            File.WriteAllText(Path.Combine(dir, "mod-list.json"), "{\"mods\":[]}");
            return dir;
        }

        //---- SheetHost contract (C2c Task 2): width from the sheets table, Enter maps to the former Import
        //(OK/AcceptButton) result. Escape resolves through SheetContent<T>'s own (non-virtual) Cancel(), not
        //through this sheet's CancelImport() - see the task report for that gap versus the Cancel button,
        //which still runs the full cts.Cancel()/DialogResultValue/NewPresetName bookkeeping. -----------------

        [AvaloniaFact]
        public void SheetWidth_Is460() {
            var sheet = new PresetImportSheet([]);

            Assert.Equal(460, sheet.SheetWidth);
        }

        [AvaloniaFact]
        public async Task Enter_TriggersImport_AndEscape_ResolvesWithNoOutcome() {
            var window = new Window { Width = 900, Height = 700 };
            window.Show();
            SheetHost host = SheetHost.For(window);

            var sheet1 = new PresetImportSheet([]);
            sheet1.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet1.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet1.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet1.ProcessPresetStub = (_, _, _, name, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Ok, NewPresetName = name });
            Task<bool?> task1 = host.ShowAsync(sheet1);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet1.PresetNameTextBoxControl, Key.Enter);
            Assert.True(await task1);
            AnimationClock.Settle();

            var sheet2 = new PresetImportSheet([]);
            Task<bool?> task2 = host.ShowAsync(sheet2);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet2.PresetNameTextBoxControl, Key.Escape);
            Assert.Null(await task2);
        }

        //Fix round 1, item 1: SheetHost's Escape handling calls the exact same (now virtual) Cancel() the
        //Cancel button calls, so a real Escape keypress on a presented sheet must cancel the in-flight
        //import pipeline's token too, not just resolve the Task.
        [AvaloniaFact]
        public async Task Escape_WhilePipelineInFlight_CancelsTheTokenAndDisposesTheSheet() {
            var pipelineStarted = new SemaphoreSlim(0, 1);
            var pipelineGate = new TaskCompletionSource();
            CancellationToken capturedToken = default;
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet.ProcessPresetStub = async (_, _, _, name, _, _, _, token) => {
                capturedToken = token;
                pipelineStarted.Release();
                await pipelineGate.Task;
                return new PresetImporter.Result { Outcome = PresetImportOutcome.Ok, NewPresetName = name };
            };
            var window = new Window { Width = 900, Height = 700 };
            window.Show();
            SheetHost host = SheetHost.For(window);

            Task<bool?> task = host.ShowAsync(sheet);
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(sheet.PresetNameTextBoxControl, Key.Enter);
            await pipelineStarted.WaitAsync();

            CanvasKeys.KeyDownOn(sheet.PresetNameTextBoxControl, Key.Escape);

            Assert.Null(await task);
            Assert.True(capturedToken.IsCancellationRequested);
            Assert.True(sheet.Disposed);

            pipelineGate.TrySetResult(); //let the still-awaiting background pipeline finish so nothing leaks past the test.
        }

        //---- key-gesture buttons (upstream Designer sets AcceptButton/CancelButton to these two) -----------

        [AvaloniaFact]
        public void OKButton_IsTheDefaultButton() {
            var sheet = new PresetImportSheet([]);

            Assert.Same(sheet.OKButtonControl, sheet.DefaultButton);
        }
    }
}
