using Avalonia.Headless.XUnit;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers PresetImportWindow's own wiring (io-reference.md §4, upstream PresetImportForm): pre-flight
    //validation, live name-color states, Cancel, and the hand-off into PresetImporter.ProcessPreset (which
    //is exercised for real against StubFactorioHarness in ForemanTest, not here - see SaveFileLoadWindowTests
    //for the established "this window's tests never touch a real factorio process" convention).
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
            var window = new PresetImportWindow([]);

            window.PresetNameTextBoxControl.Text = "Ab1";

            Assert.Equal(Avalonia.Media.Brushes.Moccasin, window.PresetNameTextBoxControl.Background);
        }

        [AvaloniaFact]
        public void PresetNameTextBox_UniqueValidName_IsLightGreen() {
            var window = new PresetImportWindow([]);

            window.PresetNameTextBoxControl.Text = "My New Preset";

            Assert.Equal(Avalonia.Media.Brushes.LightGreen, window.PresetNameTextBoxControl.Background);
        }

        [AvaloniaFact]
        public void PresetNameTextBox_CollidesWithExistingPreset_IsPink() {
            var window = new PresetImportWindow([new Preset("Taken Name", false, false)]);

            window.PresetNameTextBoxControl.Text = "taken name"; //case-insensitive collision

            Assert.Equal(Avalonia.Media.Brushes.Pink, window.PresetNameTextBoxControl.Background);
        }

        [AvaloniaFact]
        public void PresetNameTextBox_DisallowedCharacters_AreStrippedLive() {
            var window = new PresetImportWindow([]);

            window.PresetNameTextBoxControl.Text = "Ab1 (2)-_.!@#$";

            Assert.Equal("Ab1 (2)-_.", window.PresetNameTextBoxControl.Text);
        }

        //---- Factorio location picker (reference §4, upstream FactorioLocationComboBox) ---------------------

        [AvaloniaFact]
        public void FactorioLocationComboBox_MultipleCandidates_ListsAllAndIsVisible() {
            List<string> candidates = [NewTempDir(), NewTempDir(), NewTempDir()];

            var window = new PresetImportWindow([], candidates);

            Assert.True(window.FactorioLocationComboBoxControl.IsVisible);
            Assert.Equal(candidates, window.FactorioLocationComboBoxControl.ItemsSource);
        }

        [AvaloniaFact]
        public void FactorioLocationComboBox_SelectingACandidate_FillsTheTextBox() {
            List<string> candidates = [NewTempDir(), NewTempDir()];
            var window = new PresetImportWindow([], candidates);

            window.FactorioLocationComboBoxControl.SelectedItem = candidates[1];

            Assert.Equal(candidates[1], window.FactorioLocationTextBoxControl.Text);
        }

        [AvaloniaFact]
        public void FactorioLocationComboBox_SingleCandidate_IsHidden() {
            List<string> candidates = [NewTempDir()];

            var window = new PresetImportWindow([], candidates);

            Assert.False(window.FactorioLocationComboBoxControl.IsVisible);
            Assert.Equal(candidates[0], window.FactorioLocationTextBoxControl.Text);
        }

        [AvaloniaFact]
        public void FactorioLocationComboBox_NoCandidates_IsHidden() {
            var window = new PresetImportWindow([], []);

            Assert.False(window.FactorioLocationComboBoxControl.IsVisible);
            Assert.Equal("", window.FactorioLocationTextBoxControl.Text);
        }

        //---- Cancel (reference §4) -------------------------------------------------------------------------

        [AvaloniaFact]
        public void CancelButton_Click_ClearsNewPresetNameAndSetsDialogResultFalse() {
            var window = new PresetImportWindow([]);

            window.SimulateCancelClick();

            Assert.Equal("", window.NewPresetName);
            Assert.Equal(false, window.DialogResultValue);
        }

        //---- pre-flight validation matrix (reference §4 steps 1-7) ------------------------------------------

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioLocationDoesNotExist_ShowsVerbatimMessage() {
            var window = new PresetImportWindow([]);
            window.FactorioLocationTextBoxControl.Text = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            window.PresetNameTextBoxControl.Text = "Valid Name Here";
            string? captured = null;
            window.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await window.RunImportAsync();

            Assert.Equal("That directory doesn't seem to exist", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_NameTooShort_ShowsVerbatimMessage() {
            var window = new PresetImportWindow([]);
            window.FactorioLocationTextBoxControl.Text = NewTempDir();
            window.PresetNameTextBoxControl.Text = "Ab1";
            string? captured = null;
            window.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await window.RunImportAsync();

            Assert.Equal("Preset name has to be longer than 5!", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_DefaultPresetName_ShowsVerbatimMessage() {
            var window = new PresetImportWindow([]);
            window.FactorioLocationTextBoxControl.Text = NewTempDir();
            window.PresetNameTextBoxControl.Text = "Factorio 2.0 Vanilla";
            string? captured = null;
            window.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await window.RunImportAsync();

            Assert.Equal("Cant overwrite default preset!", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_ExistingNameDeclinedOverwrite_StopsWithoutFurtherMessages() {
            var window = new PresetImportWindow([new Preset("Existing Preset", false, false)]);
            window.FactorioLocationTextBoxControl.Text = NewTempDir();
            window.PresetNameTextBoxControl.Text = "Existing Preset";
            bool warned = false;
            window.WarningDialogStub = (_, _) => { warned = true; return Task.CompletedTask; };
            window.ConfirmDialogStub = (_, _) => Task.FromResult(false);

            await window.RunImportAsync();

            Assert.False(warned);
            Assert.Null(window.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioExecutableNotFound_ShowsAdaptedVerbatimMessage() {
            var window = new PresetImportWindow([]);
            window.FactorioLocationTextBoxControl.Text = NewTempDir(); //exists, but no factorio inside
            window.PresetNameTextBoxControl.Text = "Valid Name Here";
            string? captured = null;
            window.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await window.RunImportAsync();

            Assert.Equal("Couldnt find factorio (Contents/MacOS/factorio) - please select a valid Factorio install location", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioVersionTooOld_ShowsTheValidatorMessage() {
            var window = new PresetImportWindow([]);
            window.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall("1.1.0");
            window.PresetNameTextBoxControl.Text = "Valid Name Here";
            string? captured = null;
            window.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await window.RunImportAsync();

            Assert.Contains("Factorio Version below 2.0", captured);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_ModsFolderCannotBeAutoLocated_ShowsVerbatimMessage() {
            var window = new PresetImportWindow([]) { HomeOverride = NewTempDir() };
            window.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            window.PresetNameTextBoxControl.Text = "Valid Name Here";
            //no config-path.cfg, no default mac user-data folder under this fake home - auto-detect fails.
            string? captured = null;
            window.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await window.RunImportAsync();

            Assert.Equal("Couldnt auto-locate the mods folder - please manually locate the folder", captured);
        }

        //---- pipeline hand-off (ProcessPresetStub bypasses the real Factorio process entirely) -------------

        [AvaloniaFact]
        public async Task RunImportAsync_PipelineSucceeds_SetsDialogResultTrueAndNewPresetName() {
            var window = new PresetImportWindow([]);
            window.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            window.ModsLocationTextBoxControl.Text = WriteModsFolder();
            window.PresetNameTextBoxControl.Text = "Valid Name Here";
            window.ProcessPresetStub = (_, _, _, name, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Ok, NewPresetName = name });

            await window.RunImportAsync();

            Assert.Equal(true, window.DialogResultValue);
            Assert.Equal("Valid Name Here", window.NewPresetName);
            Assert.True(window.ImportStarted);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_PipelineFails_ShowsWarningAndReEnablesTheForm() {
            var window = new PresetImportWindow([]);
            window.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            window.ModsLocationTextBoxControl.Text = WriteModsFolder();
            window.PresetNameTextBoxControl.Text = "Valid Name Here";
            window.ProcessPresetStub = (_, _, _, _, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Failed, WarningMessage = "boom" });
            string? captured = null;
            window.WarningDialogStub = (_, m) => { captured = m; return Task.CompletedTask; };

            await window.RunImportAsync();

            Assert.Equal("boom", captured);
            Assert.Null(window.DialogResultValue);
            Assert.False(window.ProgressPanelVisible);
            Assert.Equal("", window.NewPresetName);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_PipelineCancelled_ShowsNoWarning() {
            var window = new PresetImportWindow([]);
            window.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            window.ModsLocationTextBoxControl.Text = WriteModsFolder();
            window.PresetNameTextBoxControl.Text = "Valid Name Here";
            window.ProcessPresetStub = (_, _, _, _, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Cancel });
            window.WarningDialogStub = (_, _) => throw new InvalidOperationException("should not warn on Cancel");

            await window.RunImportAsync();

            Assert.Null(window.DialogResultValue);
        }

        private static string WriteModsFolder() {
            string dir = NewTempDir();
            File.WriteAllText(Path.Combine(dir, "mod-list.json"), "{\"mods\":[]}");
            return dir;
        }

        //---- key-gesture buttons (upstream Designer sets AcceptButton/CancelButton to these two) -----------

        [AvaloniaFact]
        public void OKButton_IsDefault() {
            var window = new PresetImportWindow([]);

            Assert.True(window.OKButtonControl.IsDefault);
        }

        [AvaloniaFact]
        public void CancelButton_IsCancel() {
            var window = new PresetImportWindow([]);

            Assert.True(window.CancelButtonControl.IsCancel);
        }
    }
}
