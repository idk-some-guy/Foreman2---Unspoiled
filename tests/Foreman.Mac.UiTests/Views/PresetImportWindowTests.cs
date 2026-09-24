using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.Sheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Covers PresetImportSheet's own wiring (io-reference.md §4): the live name checks, the pre-flight problems
    //as row errors, Cancel, the failure strip, and the hand-off into PresetImporter.ProcessPreset (run for real
    //against StubFactorioHarness in ForemanTest, never here).
    [UnsupportedOSPlatform("windows")]
    public class PresetImportWindowTests {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

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

        private const string NameRule = "5–40 characters: letters, numbers, spaces, and ( ) . - _";
        private const string BuiltInName = "“Factorio 2.0 Vanilla” is built in and can’t be replaced. Choose another name.";

        [AvaloniaFact]
        public void PresetName_FewerThanFiveChars_TurnsTheRuleNoteToAWarning_UntilTheNameIsUsable() {
            var sheet = new PresetImportSheet([]);

            sheet.PresetNameTextBoxControl.Text = "Ab1";

            Assert.True(string.IsNullOrEmpty(sheet.PresetNameRowControl.Error));
            Assert.Equal(NameRule, sheet.PresetNameRowControl.Note);
            Assert.True(sheet.PresetNameRowControl.NoteIsWarning);

            sheet.PresetNameTextBoxControl.Text = "Ab1 My Preset";

            Assert.False(sheet.PresetNameRowControl.NoteIsWarning);
        }

        [AvaloniaFact]
        public void PresetName_UniqueValidName_ShowsOnlyTheRule() {
            var sheet = new PresetImportSheet([]);

            sheet.PresetNameTextBoxControl.Text = "My New Preset";

            Assert.True(string.IsNullOrEmpty(sheet.PresetNameRowControl.Error));
            Assert.Equal(NameRule, sheet.PresetNameRowControl.Note);
        }

        [AvaloniaFact]
        public void PresetName_CollidesWithExistingPreset_WarnsThatImportReplacesIt_AndImportStaysEnabled() {
            var sheet = new PresetImportSheet([new Preset("Taken Name", false, false)]);

            sheet.PresetNameTextBoxControl.Text = "taken name"; //case-insensitive collision

            Assert.Equal("This name is in use. Importing replaces that preset.", sheet.PresetNameRowControl.Error);
            Assert.True(sheet.PresetNameRowControl.ErrorIsWarning);
            Assert.True(sheet.OKButtonControl.IsEnabled);
        }

        [AvaloniaFact]
        public void PresetName_TheBuiltInPreset_IsARowError() {
            var sheet = new PresetImportSheet([]);

            sheet.PresetNameTextBoxControl.Text = "factorio 2.0 vanilla";

            Assert.Equal(BuiltInName, sheet.PresetNameRowControl.Error);
            Assert.False(sheet.PresetNameRowControl.ErrorIsWarning);
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
            Assert.Equal(candidates, sheet.FactorioLocationComboBoxControl.Items);
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

        //---- pre-flight problems are row errors with focus on the field, never an alert (reference §4 steps 1-7) ----

        private static FormRow RowLabelled(PresetImportSheet sheet, string label) =>
            sheet.GetVisualDescendants().OfType<FormRow>().Single(r => r.Label == label);

        private static (PresetImportSheet Sheet, SheetHost Host) Presented(List<Preset>? existing = null) {
            var sheet = new PresetImportSheet(existing ?? [], []);
            (_, SheetHost host, _) = SheetFixture.Present(sheet);
            return (sheet, host);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioLocationDoesNotExist_IsTheInstallRowError() {
            (PresetImportSheet sheet, SheetHost host) = Presented();
            sheet.FactorioLocationTextBoxControl.Text = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";

            await sheet.RunImportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("This folder doesn’t exist. Check the path, or click Browse… to choose it.", RowLabelled(sheet, "Factorio install").Error);
            Assert.True(sheet.FactorioLocationTextBoxControl.IsFocused);
            Assert.False(sheet.ImportStarted);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_NameTooShort_IsTheNameRowError_AndFocusesTheName() {
            (PresetImportSheet sheet, SheetHost host) = Presented();
            sheet.FactorioLocationTextBoxControl.Text = NewTempDir();
            sheet.PresetNameTextBoxControl.Text = "Ab1";

            await sheet.RunImportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("Preset names need at least 5 characters.", sheet.PresetNameRowControl.Error);
            Assert.False(sheet.PresetNameRowControl.ErrorIsWarning);
            Assert.True(sheet.PresetNameTextBoxControl.IsFocused);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_DefaultPresetName_IsTheBuiltInRowError_AndFocusesTheName() {
            (PresetImportSheet sheet, SheetHost host) = Presented();
            sheet.FactorioLocationTextBoxControl.Text = NewTempDir();
            sheet.PresetNameTextBoxControl.Text = "Factorio 2.0 Vanilla";

            await sheet.RunImportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal(BuiltInName, sheet.PresetNameRowControl.Error);
            Assert.True(sheet.PresetNameTextBoxControl.IsFocused);
            Assert.False(sheet.ImportStarted);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_ExistingNameDeclinedOverwrite_StopsWithoutFurtherMessages() {
            (PresetImportSheet sheet, SheetHost host) = Presented([new Preset("Existing Preset", false, false)]);
            sheet.FactorioLocationTextBoxControl.Text = NewTempDir();
            sheet.PresetNameTextBoxControl.Text = "Existing Preset";
            string? title = null;
            string? message = null;
            sheet.ConfirmDialogStub = (t, m) => { title = t; message = m; return Task.FromResult(false); };

            await sheet.RunImportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("Replace the preset “Existing Preset”?", title);
            Assert.Equal("A preset with this name already exists. Importing replaces its items, recipes, and icons. You can’t undo this action.", message);
            Assert.Null(sheet.DialogResultValue);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioExecutableNotFound_IsTheInstallRowError() {
            (PresetImportSheet sheet, SheetHost host) = Presented();
            sheet.FactorioLocationTextBoxControl.Text = NewTempDir(); //exists, but no factorio inside
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";

            await sheet.RunImportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("This folder doesn’t contain Factorio. Choose the folder that holds factorio.app.", RowLabelled(sheet, "Factorio install").Error);
            Assert.True(sheet.FactorioLocationTextBoxControl.IsFocused);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioVersionTooOld_IsTheInstallRowError() {
            (PresetImportSheet sheet, SheetHost host) = Presented();
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall("1.1.0");
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";

            await sheet.RunImportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("Factorio 1.1.0 is too old for this Foreman. Use Factorio 2.0.7 or later.", RowLabelled(sheet, "Factorio install").Error);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_FactorioVersionTooNew_IsTheInstallRowError() {
            (PresetImportSheet sheet, SheetHost host) = Presented();
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall("3.0.1");
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";

            await sheet.RunImportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("Factorio 3.0.1 is newer than this Foreman supports. Check for a Foreman update.", RowLabelled(sheet, "Factorio install").Error);
        }

        [AvaloniaFact]
        public async Task RunImportAsync_ModsFolderCannotBeAutoLocated_IsTheModsRowError() {
            var sheet = new PresetImportSheet([], []) { HomeOverride = NewTempDir() };
            (_, SheetHost host, _) = SheetFixture.Present(sheet);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            //no config-path.cfg, no default mac user-data folder under this fake home - auto-detect fails.

            await sheet.RunImportAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("Foreman couldn’t find your mods folder. Click Browse… to choose it.", RowLabelled(sheet, "Mods folder").Error);
            Assert.True(sheet.ModsLocationTextBoxControl.IsFocused);
            Assert.Null(RowLabelled(sheet, "Factorio install").Error);
        }

        [AvaloniaFact]
        public async Task Browse_NotAFactorioOrModsFolder_IsThatRowsError_AndAGoodPickClearsIt() {
            (PresetImportSheet sheet, SheetHost host) = Presented();
            string plain = NewTempDir();
            sheet.BrowseFactorioLocationStub = () => Task.FromResult<string?>(plain);
            sheet.BrowseModsLocationStub = () => Task.FromResult<string?>(plain);

            await sheet.BrowseFactorioLocationAsync().WaitAsync(Timeout);
            await sheet.BrowseModsLocationAsync().WaitAsync(Timeout);

            SheetFixture.AssertNoAlert(host);
            Assert.Equal("This folder doesn’t contain Factorio. Choose the folder that holds factorio.app.", RowLabelled(sheet, "Factorio install").Error);
            Assert.Equal("This isn’t a Factorio mods folder. Choose the folder that holds mod-list.json.", RowLabelled(sheet, "Mods folder").Error);

            string mods = WriteModsFolder();
            sheet.BrowseModsLocationStub = () => Task.FromResult<string?>(mods);
            await sheet.BrowseModsLocationAsync().WaitAsync(Timeout);
            Assert.Null(RowLabelled(sheet, "Mods folder").Error);
            Assert.Equal(mods, sheet.ModsLocationTextBoxControl.Text);
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

            await sheet.RunImportAsync().WaitAsync(Timeout);

            Assert.Equal(true, sheet.DialogResultValue);
            Assert.Equal("Valid Name Here", sheet.NewPresetName);
            Assert.True(sheet.ImportStarted);
        }

        [AvaloniaFact]
        public async Task PresetImport_Failure_ShowsTheStatePanelError_AndKeepsThePrimaryEnabled() {
            var reported = new TaskCompletionSource();
            var pipelineGate = new TaskCompletionSource();
            var sheet = new PresetImportSheet([]);
            (_, SheetHost host, Task<bool?> result) = SheetFixture.Present(sheet);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet.ProcessPresetStub = async (_, _, _, _, _, progress, _, _) => {
                progress.Report(new(20, "Running Factorio - foreman export scripts."));
                reported.TrySetResult();
                await pipelineGate.Task;
                return new PresetImporter.Result {
                    Outcome = PresetImportOutcome.Failed,
                    Failure = FactorioRunFailure.AnotherInstanceRunning,
                    WarningMessage = "Foreman export could not be completed because this instance of Factorio is currently running.",
                };
            };

            Task run = sheet.RunImportAsync();
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(10));
            AnimationClock.Settle(500);
            StatePanel loading = SheetFixture.StripOf(host);
            Assert.Equal(StatePanelShape.Loading, loading.Shape);
            Assert.Equal("Importing “Valid Name Here” — running Factorio - foreman export scripts.", loading.Sentence);
            Assert.Equal(0.2, loading.Progress!.Value, 3);
            Assert.False(sheet.OKButtonControl.IsEnabled);
            Assert.True(sheet.CancelButtonControl.IsEffectivelyEnabled);
            Assert.False(sheet.PresetNameTextBoxControl.IsEffectivelyEnabled);

            pipelineGate.TrySetResult();
            await run;
            AnimationClock.Settle();

            Assert.False(result.IsCompleted);
            StatePanel strip = SheetFixture.StripOf(host);
            Assert.Equal(StatePanelShape.Error, strip.Shape);
            SheetFixture.AssertNoAlert(host);
            Assert.Equal("Couldn’t import “Valid Name Here”. Factorio is already running. Quit Factorio, then try again.", strip.Sentence);
            Assert.Equal("Try Again", sheet.OKButtonControl.Content);
            Assert.True(sheet.OKButtonControl.IsEffectivelyEnabled);
            Assert.True(sheet.CancelButtonControl.IsEffectivelyEnabled);
            Assert.True(sheet.ShowLogButtonControl.IsEffectivelyVisible);
            Assert.Equal(ButtonRowRole.Helper, ButtonRow.GetRole(sheet.ShowLogButtonControl));
            Assert.True(sheet.PresetNameTextBoxControl.IsEffectivelyEnabled);
            Assert.True(sheet.FactorioLocationTextBoxControl.IsEffectivelyVisible);
            Assert.Null(sheet.DialogResultValue);
            Assert.Equal("", sheet.NewPresetName);

            sheet.ProcessPresetStub = (_, _, _, name, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Ok, NewPresetName = name });
            sheet.OKButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await result);
            Assert.False(sheet.ShowLogButtonControl.IsVisible);
        }

        [AvaloniaFact]
        public async Task PresetImport_UnmappedFailure_PointsAtTheLog_AndShowLogOpensIt() {
            var sheet = new PresetImportSheet([], []);
            (Window window, SheetHost host, Task<bool?> result) = SheetFixture.Present(sheet);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet.ProcessPresetStub = (_, _, _, _, _, _, _, _) => Task.FromResult(new PresetImporter.Result {
                Outcome = PresetImportOutcome.Failed,
                Failure = FactorioRunFailure.Unmapped,
                WarningMessage = "Factorio crashed while doing something new.\n\nIt went badly.",
            });
            int logOpened = 0;
            sheet.ShowLogStub = () => { logOpened++; return Task.CompletedTask; };

            await sheet.RunImportAsync().WaitAsync(Timeout);
            AnimationClock.Settle();

            StatePanel strip = SheetFixture.StripOf(host);
            Assert.Equal("Couldn’t import “Valid Name Here”. The details are in Foreman’s log.", strip.Sentence);
            Assert.DoesNotContain(" — factorio", strip.Sentence!);
            Assert.True(sheet.ShowLogButtonControl.IsEffectivelyVisible);
            SheetFixture.Click(window, sheet.ShowLogButtonControl);
            Assert.Equal(1, logOpened);
            Assert.False(result.IsCompleted);
        }

        [Fact]
        public void FailureReasons_AreOneSentenceOfAtMost110Characters_InTheSheetsCopyStyle() {
            foreach (FactorioRunFailure failure in Enum.GetValues<FactorioRunFailure>()) {
                foreach (FactorioCrashPhase phase in Enum.GetValues<FactorioCrashPhase>()) {
                    string reason = FailureSentences.Reason(failure, phase, new FactorioInstallCheck(FactorioInstallProblem.TooNew, "3.10.100"));
                    Assert.InRange(reason.Length, 1, 110);
                    Assert.EndsWith(".", reason);
                    Assert.DoesNotContain("!", reason);
                    Assert.DoesNotContain("'", reason);
                    Assert.DoesNotContain(" — ", reason);
                }
            }
            Assert.Equal("Factorio crashed while running the preset export scripts. A mod is the usual cause; see factorio-current.log.",
                FailureSentences.Reason(FactorioRunFailure.Crashed, FactorioCrashPhase.RunningExportScripts, null));
        }

        [AvaloniaFact]
        public async Task RunImportAsync_PipelineCancelled_ShowsNoWarning() {
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet.ProcessPresetStub = (_, _, _, _, _, _, _, _) =>
                Task.FromResult(new PresetImporter.Result { Outcome = PresetImportOutcome.Cancel });

            await sheet.RunImportAsync().WaitAsync(Timeout);

            Assert.Null(sheet.DialogResultValue);
            Assert.Null(sheet.Activity);
        }

        private static string WriteModsFolder() {
            string dir = NewTempDir();
            File.WriteAllText(Path.Combine(dir, "mod-list.json"), "{\"mods\":[]}");
            return dir;
        }

        //---- the kit sheet: the wide width, the three window-column rows, notes inside the sheet, Escape cancels the run ----

        [AvaloniaFact]
        public void PresetImport_Width_IsTheNamedSheetWidth() {
            var sheet = new PresetImportSheet([], []);
            (_, SheetHost host, _) = SheetFixture.Present(sheet);

            Assert.Equal(SheetFixture.Token("Foreman.Metrics.SheetWidthWide"), SheetFixture.PanelOf(host, sheet).Bounds.Width, 0.5);
            Assert.Equal("Import a preset", SheetFixture.FrameOf(host, sheet).Title);
            Assert.Equal(["Show Log…", "Cancel", "Import"], sheet.Buttons!.Items.Cast<Button>().Select(b => b.Content));
            Assert.False(sheet.ShowLogButtonControl.IsVisible);
            FormRow[] rows = [.. sheet.GetVisualDescendants().OfType<FormRow>()];
            Assert.Equal(["Factorio install", "Preset name", "Mods folder"], rows.Select(r => r.Label));
            Assert.All(rows, r => Assert.Equal(FormRowLabelColumn.Window, r.LabelColumn));
            Assert.All(sheet.GetVisualDescendants().OfType<TextBlock>(), t => Assert.DoesNotContain("!", t.Text ?? ""));
            Assert.Equal("Language, mods, and mod settings come from Factorio. Change them there before importing.", rows[0].Note);
            Assert.Equal(NameRule, rows[1].Note);
            Assert.Equal("Leave blank to detect it automatically.", rows[2].Note);
            Border panel = SheetFixture.PanelOf(host, sheet);
            double contentRight = sheet.TranslatePoint(new Avalonia.Point(sheet.Bounds.Width, 0), panel)!.Value.X;
            foreach (FormRow row in rows) {
                TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
                Assert.True(note.TranslatePoint(new Avalonia.Point(note.Bounds.Width, 0), panel)!.Value.X <= contentRight + 0.5, row.Label);
            }
        }

        //Escape and the Cancel button share Cancel(), so Escape cancels the in-flight pipeline's token too.
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

        //A pipeline result landing after Cancel disposed the sheet must not overwrite the cancelled state.
        [AvaloniaFact]
        public async Task RunImportAsync_LateResultAfterCancel_DoesNotOverwriteCancelledState() {
            var pipelineStarted = new SemaphoreSlim(0, 1);
            var pipelineGate = new TaskCompletionSource();
            var sheet = new PresetImportSheet([]);
            sheet.FactorioLocationTextBoxControl.Text = NewValidatingFactorioInstall();
            sheet.ModsLocationTextBoxControl.Text = WriteModsFolder();
            sheet.PresetNameTextBoxControl.Text = "Valid Name Here";
            sheet.ProcessPresetStub = async (_, _, _, name, _, _, _, _) => {
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

            pipelineGate.TrySetResult(); //the pipeline resolves Ok only after the sheet already cancelled+disposed
            await Task.Delay(50); //give RunImportAsync's continuation a chance to run (and, pre-fix, corrupt state)

            Assert.Equal("", sheet.NewPresetName);
            Assert.Equal(false, sheet.DialogResultValue);
        }

        //---- key-gesture buttons (upstream Designer sets AcceptButton/CancelButton to these two) -----------

        [AvaloniaFact]
        public void OKButton_IsTheDefaultButton() {
            var sheet = new PresetImportSheet([]);

            Assert.Same(sheet.OKButtonControl, sheet.DefaultButton);
            Assert.Same(sheet.OKButtonControl, sheet.Buttons!.ResolveDefaultButton());
        }
    }
}
