using Foreman;
using Foreman.DataCaching;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace ForemanTest {
    //Covers PresetImporter (io-reference.md §4, upstream PresetImportForm.ProcessPreset) - the
    //UI-independent pipeline: throwaway test save, the foremanexport mod run, marker-delimited output
    //parse, and the new preset's .pjson/.json/.dat write. Lives in Foreman.Core so it's driven against
    //StubFactorioHarness under plain MSTest, the same way SaveFileReader (§5) already is.
    [TestClass]
    [UnsupportedOSPlatform("windows")]
    public class PresetImporterTests {
        private static readonly Func<int, int, Task<bool>> AlwaysContinue = (_, _) => Task.FromResult(true);
        private static readonly Func<int, int, Task<bool>> NeverContinue = (_, _) => Task.FromResult(false);

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        //Builds a Contents-style install directory (installPath/MacOS/factorio, matching
        //FactorioPathsProcessor.GetExecutablePath) whose stub factorio script always touches temp-save.zip
        //in the caller's working directory (not next to itself) and emits the given marker sections - the
        //same script answers both the --create and the --benchmark call, since neither call site cares what
        //the OTHER call's output looked like.
        private static (string installPath, string modsPath, string scratchDir) BuildFixture(string lnSection, string p1Section, string p2Section) {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteExportScript(macOsDir, lnSection, p1Section, p2Section);
            string installPath = Path.GetDirectoryName(macOsDir)!;
            Directory.CreateDirectory(Path.Combine(installPath, "data"));
            return (installPath, NewTempDir(), NewTempDir());
        }

        private static string MacOsDirOf(string installPath) => Path.Combine(installPath, "MacOS");

        //---- live name filter (reference §4 step 8) -------------------------------------------------------

        [TestMethod]
        public void FilterName_StripsEverythingButLettersDigitsAndAllowedPunctuation() {
            string filtered = PresetImporter.FilterName("Ab1 (2)-_.!@#$%^&*");

            Assert.AreEqual("Ab1 (2)-_.", filtered);
        }

        //---- happy path (reference §4 steps 1-8) ----------------------------------------------------------

        [TestMethod]
        public async Task ProcessPreset_HappyPath_WritesThreeFilesToUserDirectory_AndResolvesLidToLocalisedName() {
            const string p2 = "{\"mods\":[],\"items\":[{\"name\":\"iron-plate\",\"lid\":\"$0\"}]}";
            var (installPath, modsPath, scratchDir) = BuildFixture(
                "$0<#~#>Unknown key: \"Iron Plate\"", "{}", p2);
            string userPresetsDir = NewTempDir();
            var progressValues = new List<KeyValuePair<int, string>>();
            var progress = new Progress<KeyValuePair<int, string>>(progressValues.Add);

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "My Test Preset", userPresetsDir, progress, AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Ok, result.Outcome);
            Assert.AreEqual("My Test Preset", result.NewPresetName);

            string basePath = Path.Combine(userPresetsDir, "My Test Preset");
            Assert.IsTrue(File.Exists(basePath + ".pjson"));
            Assert.IsTrue(File.Exists(basePath + ".json"));
            Assert.IsTrue(File.Exists(basePath + ".dat"));
            Assert.IsTrue(File.ReadAllText(basePath + ".pjson").Contains("\"Iron Plate\"", StringComparison.Ordinal));
            Assert.IsFalse(File.ReadAllText(basePath + ".pjson").Contains("\"lid\"", StringComparison.Ordinal));

            //no leftover scratch state: temp-save.zip gone from the scratch dir, bundled mod folder removed.
            Assert.IsFalse(File.Exists(Path.Combine(scratchDir, "temp-save.zip")));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "foremanexport_2.0.0")));

            //never touches the Factorio executable's own directory - the working-directory pin (io-reference.md
            //§4 step 1) keeps temp-save.zip out of there entirely.
            Assert.IsFalse(File.Exists(Path.Combine(MacOsDirOf(installPath), "temp-save.zip")));
        }

        // Red-first for the working-directory pin (io-reference.md §4 step 1): before FactorioBenchmarkRunner.Run
        // pinned ProcessStartInfo.WorkingDirectory to the caller-supplied scratch directory, the stub script
        // below would find no sentinel file (it isn't the process's real cwd), never touch temp-save.zip, and
        // the pipeline would fail with "did not create the test save" instead of succeeding.
        [TestMethod]
        public async Task ProcessPreset_RunsFactorioWithWorkingDirectoryPinnedToTheScratchDirectory() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string installPath = Path.GetDirectoryName(macOsDir)!;
            Directory.CreateDirectory(Path.Combine(installPath, "data"));
            string scratchDir = NewTempDir();
            File.WriteAllText(Path.Combine(scratchDir, "sentinel.txt"), "");
            string modsPath = NewTempDir();
            string userPresetsDir = NewTempDir();

            //Only touches temp-save.zip (and emits the marker output the pipeline needs) when the process's
            //own working directory is exactly scratchDir - proving the WorkingDirectory pin, not merely that
            //some writable cwd happened to be used.
            StubFactorioHarness.WriteScript(macOsDir,
                "test -f ./sentinel.txt && touch ./temp-save.zip\n" +
                "cat <<'FOREMAN_EOF'\n" +
                "<<<START-EXPORT-LN>>>\n\n<<<END-EXPORT-LN>>>\n" +
                "<<<START-EXPORT-P1>>>\n{}\n<<<END-EXPORT-P1>>>\n" +
                "<<<START-EXPORT-P2>>>\n{\"mods\":[]}\n<<<END-EXPORT-P2>>>\n" +
                "FOREMAN_EOF\n" +
                "exit 0\n");

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Cwd Pinned Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Ok, result.Outcome);
        }

        //(PresetResolver's own dual-directory read is covered where PresetResolver lives, in
        //Foreman.Mac.UiTests - this pipeline only owns writing the three files into userPresetsDir.)

        //---- failure branches: verbatim messages + zero-debris cleanup (reference §4 step 3, 8) -----------

        [TestMethod]
        public async Task ProcessPreset_AnotherInstanceRunningDuringCreate_ReturnsFailedWithVerbatimMessage_AndCleansUp() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteAnotherInstanceScript(macOsDir);
            string installPath = Path.GetDirectoryName(macOsDir)!;
            string modsPath = NewTempDir();
            string scratchDir = NewTempDir();
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Blocked Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
            Assert.AreEqual(
                "Foreman export could not be completed because this instance of Factorio is currently running. Please stop expanding the factory for just a brief moment and let the export commence in peace!",
                result.WarningMessage);
            AssertNoDebris(scratchDir, modsPath, userPresetsDir, "Blocked Preset");
        }

        [TestMethod]
        public async Task ProcessPreset_FactorioCrashesDuringCreate_ReturnsFailedWithVerbatimMessage_AndCleansUp() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteCrashScript(macOsDir);
            string installPath = Path.GetDirectoryName(macOsDir)!;
            string modsPath = NewTempDir();
            string scratchDir = NewTempDir();
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Crashed Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
            Assert.AreEqual(
                "Factorio crashed while creating the test save for preset export.\n\n" +
                "This is usually caused by a bug in one of your enabled mods, not by Foreman. " +
                "Open factorio-current.log in your Factorio user data folder for details, " +
                "then try disabling mods until Factorio can start a new game with the same mod list.",
                result.WarningMessage);
            AssertNoDebris(scratchDir, modsPath, userPresetsDir, "Crashed Preset");
        }

        [TestMethod]
        public async Task ProcessPreset_TempSaveMissingAfterCreate_ReturnsFailedWithVerbatimMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteEchoScript(macOsDir, "did nothing useful\n"); //never touches temp-save.zip
            string installPath = Path.GetDirectoryName(macOsDir)!;
            string modsPath = NewTempDir();
            string scratchDir = NewTempDir();
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "No Save Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
            Assert.AreEqual(
                "Factorio did not create the test save (temp-save.zip) needed for preset export.\n\n" +
                "Factorio may have crashed or exited early. Check factorio-current.log in your Factorio user data folder " +
                "and try disabling mods until you can create a new game with the same mod list.",
                result.WarningMessage);
            AssertNoDebris(scratchDir, modsPath, userPresetsDir, "No Save Preset");
        }

        [TestMethod]
        public async Task ProcessPreset_ExportMissingMarkers_ReturnsTheModConflictMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteScript(macOsDir, "touch ./temp-save.zip\nprintf 'nothing useful here\\n'\nexit 0\n");
            string installPath = Path.GetDirectoryName(macOsDir)!;
            string modsPath = NewTempDir();
            string scratchDir = NewTempDir();
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Conflict Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
            Assert.AreEqual(
                "Foreman export could not be completed - possible mod conflict detected. Please run Factorio and ensure it can successfully load to menu before retrying.",
                result.WarningMessage);
            AssertNoDebris(scratchDir, modsPath, userPresetsDir, "Conflict Preset");
        }

        [TestMethod]
        public async Task ProcessPreset_ExportMissingMarkers_TempSaveDoesNotExistVariant_ReturnsTheSaveSpecificMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteScript(macOsDir, "touch ./temp-save.zip\nprintf 'temp-save.zip does not exist\\n'\nexit 0\n");
            string installPath = Path.GetDirectoryName(macOsDir)!;
            string modsPath = NewTempDir();
            string scratchDir = NewTempDir();
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Save Missing Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
            Assert.AreEqual(
                "Foreman export could not finish because Factorio could not load the test save (temp-save.zip). " +
                "The save may not have been created in the previous step; check factorio-current.log for crashes or errors.",
                result.WarningMessage);
        }

        [TestMethod]
        public async Task ProcessPreset_JsonParsingFails_ReturnsVerbatimMessage_AndWritesDebugDumps() {
            var (installPath, modsPath, scratchDir) = BuildFixture("", "not valid json {{{", "{\"mods\":[]}");
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Bad Json Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
            Assert.AreEqual("Foreman export could not be completed - unknown json parsing error.\nSorry", result.WarningMessage);
            //Debug dumps land in Foreman's own scratch directory (matching upstream's Application.StartupPath,
            //Foreman's own directory - not Factorio's), never in the Factorio bundle itself.
            Assert.IsTrue(File.Exists(Path.Combine(scratchDir, "_iconJObjectOut.json")));
            Assert.IsTrue(File.Exists(Path.Combine(scratchDir, "_dataJObjectOut.json")));
            Assert.IsFalse(File.Exists(Path.Combine(MacOsDirOf(installPath), "_iconJObjectOut.json")));
            Assert.IsFalse(File.Exists(Path.Combine(MacOsDirOf(installPath), "_dataJObjectOut.json")));
        }

        //---- debris location: errorExporting.json + debug dumps land in scratch, never the Factorio bundle ----

        [TestMethod]
        public async Task ProcessPreset_TempSaveMissingAfterCreate_WritesErrorExportingJsonToScratchDirectory_NotExeDirectory() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteEchoScript(macOsDir, "did nothing useful\n");
            string installPath = Path.GetDirectoryName(macOsDir)!;
            string modsPath = NewTempDir();
            string scratchDir = NewTempDir();
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Debris Location Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
            Assert.IsTrue(File.Exists(Path.Combine(scratchDir, "errorExporting.json")));
            Assert.IsFalse(File.Exists(Path.Combine(MacOsDirOf(installPath), "errorExporting.json")));
        }

        //A scratch directory the process can't write into (simulating a failed log write) must not swallow or
        //replace the original TempSaveMissing failure message - a failure log is never allowed to mask the
        //failure it was trying to record.
        [TestMethod]
        public async Task ProcessPreset_ScratchDirectoryUnwritable_ErrorExportingJsonWriteFailureDoesNotMaskOriginalMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteEchoScript(macOsDir, "did nothing useful\n");
            string installPath = Path.GetDirectoryName(macOsDir)!;
            string modsPath = NewTempDir();
            string scratchDir = NewTempDir();
            string userPresetsDir = NewTempDir();
            File.SetUnixFileMode(scratchDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            try {
                PresetImporter.Result result = await PresetImporter.ProcessPreset(
                    installPath, modsPath, scratchDir, "Unwritable Scratch Preset", userPresetsDir,
                    new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

                Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
                Assert.AreEqual(
                    "Factorio did not create the test save (temp-save.zip) needed for preset export.\n\n" +
                    "Factorio may have crashed or exited early. Check factorio-current.log in your Factorio user data folder " +
                    "and try disabling mods until you can create a new game with the same mod list.",
                    result.WarningMessage);
            } finally {
                File.SetUnixFileMode(scratchDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        [TestMethod]
        public async Task ProcessPreset_ModInconsistency_ReturnsVerbatimMessage_AndCleansUp() {
            //a mod with no matching folder/zip in modsPath, and not one of the always-available core mods.
            const string p2 = "{\"mods\":[{\"name\":\"some-unmapped-mod\",\"version\":\"1.0.0\"}]}";
            var (installPath, modsPath, scratchDir) = BuildFixture("", "{}", p2);
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Inconsistent Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), AlwaysContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Failed, result.Outcome);
            Assert.AreEqual("Mod inconsistency detected. Try to see if launching Factorio gives an error?", result.WarningMessage);
            AssertNoDebris(scratchDir, modsPath, userPresetsDir, "Inconsistent Preset");
        }

        //---- icon partial-failure interactive gate (reference §4 step 6) ----------------------------------

        private const string IconWithOneUnresolvablePath =
            "{\"items\":[{\"icon_name\":\"widget\",\"icon_data\":{\"icon\":\"__base__/missing.png\",\"icon_size\":32}}]}";

        [TestMethod]
        public async Task ProcessPreset_IconsPartiallyMissing_UserDeclines_ReturnsCancel_AndRemovesThePartialPreset() {
            var (installPath, modsPath, scratchDir) = BuildFixture("", IconWithOneUnresolvablePath, "{\"mods\":[]}");
            string userPresetsDir = NewTempDir();

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Declined Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), NeverContinue, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Cancel, result.Outcome);
            AssertNoDebris(scratchDir, modsPath, userPresetsDir, "Declined Preset");
        }

        [TestMethod]
        public async Task ProcessPreset_IconsPartiallyMissing_UserConfirms_ReturnsOk_KeepsTheWrittenFiles() {
            var (installPath, modsPath, scratchDir) = BuildFixture("", IconWithOneUnresolvablePath, "{\"mods\":[]}");
            string userPresetsDir = NewTempDir();
            (int failed, int total) captured = default;
            Func<int, int, Task<bool>> confirm = (f, t) => { captured = (f, t); return Task.FromResult(true); };

            PresetImporter.Result result = await PresetImporter.ProcessPreset(
                installPath, modsPath, scratchDir, "Confirmed Preset", userPresetsDir,
                new Progress<KeyValuePair<int, string>>(), confirm, CancellationToken.None);

            Assert.AreEqual(PresetImportOutcome.Ok, result.Outcome);
            Assert.AreEqual((1, 1), captured);
            string basePath = Path.Combine(userPresetsDir, "Confirmed Preset");
            Assert.IsTrue(File.Exists(basePath + ".pjson"));
            Assert.IsTrue(File.Exists(basePath + ".dat"));
        }

        //---- CleanupFailedImport in isolation (reference §4 step 8) ----------------------------------------

        [TestMethod]
        public void CleanupFailedImport_RemovesTempSaveModFolderAndPartialPresetFiles() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string tempSavePath = Path.Combine(macOsDir, "temp-save.zip");
            File.WriteAllText(tempSavePath, "stand-in");
            string modsPath = NewTempDir();
            Directory.CreateDirectory(Path.Combine(modsPath, "foremanexport_2.0.0"));
            File.WriteAllText(Path.Combine(modsPath, "mod-list.json"), "{\"mods\":[{\"name\":\"foremanexport\",\"enabled\":true}]}");
            string presetBasePath = Path.Combine(NewTempDir(), "Half Written Preset");
            File.WriteAllText(presetBasePath + ".pjson", "{}");
            File.WriteAllText(presetBasePath + ".json", "{}");
            File.WriteAllText(presetBasePath + ".dat", "");

            PresetImporter.CleanupFailedImport(tempSavePath, modsPath, presetBasePath);

            Assert.IsFalse(File.Exists(tempSavePath));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "foremanexport_2.0.0")));
            Assert.IsFalse(File.Exists(presetBasePath + ".pjson"));
            Assert.IsFalse(File.Exists(presetBasePath + ".json"));
            Assert.IsFalse(File.Exists(presetBasePath + ".dat"));
        }

        private static void AssertNoDebris(string scratchDir, string modsPath, string userPresetsDir, string presetName) {
            Assert.IsFalse(File.Exists(Path.Combine(scratchDir, "temp-save.zip")));
            Assert.IsFalse(Directory.Exists(Path.Combine(modsPath, "foremanexport_2.0.0")));
            Assert.IsFalse(File.Exists(Path.Combine(userPresetsDir, presetName + ".pjson")));
            Assert.IsFalse(File.Exists(Path.Combine(userPresetsDir, presetName + ".json")));
            Assert.IsFalse(File.Exists(Path.Combine(userPresetsDir, presetName + ".dat")));
        }
    }
}
