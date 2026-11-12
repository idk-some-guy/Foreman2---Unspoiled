using Foreman;
using Foreman.DataCaching;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;

namespace ForemanTest {
    //Covers SaveFileReader (io-reference.md §5, upstream SavefileLoadForm.LoadSaveFile) - the UI-independent
    //half of SaveFileLoadWindow's pipeline: exe discovery from the save's own factorio-current.log, the
    //foremansavereader mod copy/enable, the benchmark run, and the P0 export parse. Lives in Foreman.Core so
    //it can be driven against StubFactorioHarness under plain MSTest, the same way FactorioBenchmarkRunner
    //and FactorioInstallValidator already are.
    [TestClass]
    [UnsupportedOSPlatform("windows")]
    public class SaveFileReaderTests {
        private static string WriteFactorioStub(string macOsDir, string version, string benchmarkBranchBody) =>
            StubFactorioHarness.WriteScript(macOsDir,
                "case \"$*\" in\n" +
                "  *--version*) printf 'Version: " + version + " (build 1, mac64, headless)\\n'; exit 0 ;;\n" +
                "  *) " + benchmarkBranchBody + " ;;\n" +
                "esac\n");

        private static string BuildSaveFixture(string factorioExePath) {
            string userData = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string savesDir = Path.Combine(userData, "saves");
            Directory.CreateDirectory(savesDir);
            Directory.CreateDirectory(Path.Combine(userData, "mods"));
            File.WriteAllText(Path.Combine(userData, "factorio-current.log"),
                "0.000 Factorio initialised\n" +
                "0.001 Program arguments: \"" + factorioExePath + "\" \"--some-flag\"\n");
            string savePath = Path.Combine(savesDir, "mysave.zip");
            File.WriteAllText(savePath, "stand-in save");
            return savePath;
        }

        //---- exe discovery from the save's own factorio-current.log (reference §5 step 2) ---------------

        [TestMethod]
        public void FindFactorioExecutablePathFromLog_ExtractsTheQuotedFirstArgument() {
            string[] lines = [
                "0.000 Factorio initialised",
                "0.001 Program arguments: \"/Applications/factorio.app/Contents/MacOS/factorio\" \"--some-flag\"",
            ];

            string? path = SaveFileReader.FindFactorioExecutablePathFromLog(lines);

            Assert.AreEqual("/Applications/factorio.app/Contents/MacOS/factorio", path);
        }

        [TestMethod]
        public void FindFactorioExecutablePathFromLog_SeveralMatchingLines_TheLastOneWins() {
            string[] lines = [
                "0.001 Program arguments: \"/old/factorio\" \"--x\"",
                "5.002 Program arguments: \"/new/factorio\" \"--y\"",
            ];

            string? path = SaveFileReader.FindFactorioExecutablePathFromLog(lines);

            Assert.AreEqual("/new/factorio", path);
        }

        [TestMethod]
        public void FindFactorioExecutablePathFromLog_NoMatchingLine_ReturnsNull() {
            string[] lines = ["0.000 Factorio initialised", "0.010 Loading mod core"];

            Assert.IsNull(SaveFileReader.FindFactorioExecutablePathFromLog(lines));
        }

        //---- P0 export parse (reference §5 step 5) -------------------------------------------------------

        [TestMethod]
        public void ParseP0Export_ParsesModsTechnologiesAndRecipes() {
            string output =
                "some preamble noise\n" +
                "<<<START-EXPORT-P0>>>\n" +
                "{\"mods\":[{\"name\":\"base\",\"version\":\"2.0.76\"}]," +
                "\"technologies\":[{\"name\":\"automation\",\"enabled\":true}]," +
                "\"recipes\":[{\"name\":\"iron-plate\",\"enabled\":true},{\"name\":\"copper-plate\",\"enabled\":false}]}\n" +
                "<<<END-EXPORT-P0>>>\n" +
                "trailer noise";

            SaveFileInfo info = SaveFileReader.ParseP0Export(output);

            Assert.AreEqual("2.0.76", info.Mods["base"]);
            Assert.IsTrue(info.Technologies["automation"]);
            Assert.IsTrue(info.Recipes["iron-plate"]);
            Assert.IsFalse(info.Recipes["copper-plate"]);
        }

        static readonly string[] ResearchKeys = [
            "mining_drill_productivity_bonus", "laboratory_speed_modifier", "laboratory_productivity_bonus",
            "inserter_stack_size_bonus", "bulk_inserter_capacity_bonus", "belt_stack_size_bonus",
            "worker_robots_speed_modifier", "worker_robots_storage_bonus", "worker_robots_battery_modifier",
            "train_braking_force_bonus",
        ];

        [TestMethod]
        [DataRow("2.0")]
        [DataRow("2.1")]
        public void ParseP0Export_ResearchedFixture_ReadsBonusesAndLevels(string line) {
            SaveFileInfo info = SaveFileReader.ParseP0Export(File.ReadAllText(TestAssets.ResolvePath($"save-reader-{line}-researched.txt")));

            Assert.IsTrue(info.ResearchBonuses["mining_drill_productivity_bonus"] > 0);
            Assert.IsTrue(info.RecipeProductivity["steel-plate"] > 0);
            Assert.AreEqual(new SaveTechnologyLevel(Level: 5, MinLevel: 3, MaxLevel: 4294967295), info.TechnologyLevels["mining-productivity-3"]);
            Assert.IsNotNull(info.TicksPlayed);
            Assert.IsTrue(info.TicksPlayed.Value >= 0);
        }

        [TestMethod]
        [DataRow("2.0")]
        [DataRow("2.1")]
        public void ParseP0Export_FreshFixture_HasEveryKeyAndNoRecipeProductivity(string line) {
            SaveFileInfo info = SaveFileReader.ParseP0Export(File.ReadAllText(TestAssets.ResolvePath($"save-reader-{line}-fresh.txt")));

            foreach (string key in ResearchKeys)
                Assert.IsTrue(info.ResearchBonuses.ContainsKey(key), key);
            Assert.AreEqual(0, info.RecipeProductivity.Count);
        }

        [TestMethod]
        public void ParseP0Export_MissingResearchKey_IsAbsent() {
            string output =
                "<<<START-EXPORT-P0>>>\n" +
                "{\"research\":{\"mining_drill_productivity_bonus\":0.1,\"laboratory_speed_modifier\":0,\"laboratory_productivity_bonus\":0," +
                "\"inserter_stack_size_bonus\":1,\"bulk_inserter_capacity_bonus\":1,\"worker_robots_speed_modifier\":0," +
                "\"worker_robots_storage_bonus\":0,\"worker_robots_battery_modifier\":0,\"train_braking_force_bonus\":0}," +
                "\"mods\":[],\"technologies\":[],\"recipes\":[]}\n" +
                "<<<END-EXPORT-P0>>>\n";

            SaveFileInfo info = SaveFileReader.ParseP0Export(output);

            Assert.IsFalse(info.ResearchBonuses.ContainsKey("belt_stack_size_bonus"));
            Assert.AreEqual(9, info.ResearchBonuses.Count);
        }

        [TestMethod]
        public void ParseP0Export_ExportWithoutResearch_ParsesAsToday() {
            string output =
                "<<<START-EXPORT-P0>>>\n" +
                "{\"mods\":[{\"name\":\"base\",\"version\":\"2.0.76\"}]," +
                "\"technologies\":[{\"name\":\"automation\",\"enabled\":true}]," +
                "\"recipes\":[{\"name\":\"iron-plate\",\"enabled\":true},{\"name\":\"copper-plate\",\"enabled\":false}]}\n" +
                "<<<END-EXPORT-P0>>>\n";

            SaveFileInfo info = SaveFileReader.ParseP0Export(output);

            Assert.AreEqual(0, info.ResearchBonuses.Count);
            Assert.AreEqual(0, info.RecipeProductivity.Count);
            Assert.AreEqual(0, info.TechnologyLevels.Count);
            Assert.IsNull(info.TicksPlayed);
        }

        //Regression guard (docs/upstream-divergences.md): upstream's own marker slicing only produces valid
        //JSON on CRLF-terminated output, a Windows-Factorio assumption. ParseP0Export instead finds the
        //marker's actual length and trims surrounding whitespace, which must keep working under CRLF too -
        //not just the LF the stub harness and real macOS Factorio both produce.
        [TestMethod]
        public void ParseP0Export_CrlfTerminatedExport_ParsesModsTechnologiesAndRecipes() {
            string output =
                "some preamble noise\r\n" +
                "<<<START-EXPORT-P0>>>\r\n" +
                "{\"mods\":[{\"name\":\"base\",\"version\":\"2.0.76\"}]," +
                "\"technologies\":[{\"name\":\"automation\",\"enabled\":true}]," +
                "\"recipes\":[{\"name\":\"iron-plate\",\"enabled\":true},{\"name\":\"copper-plate\",\"enabled\":false}]}\r\n" +
                "<<<END-EXPORT-P0>>>\r\n" +
                "trailer noise";

            SaveFileInfo info = SaveFileReader.ParseP0Export(output);

            Assert.AreEqual("2.0.76", info.Mods["base"]);
            Assert.IsTrue(info.Technologies["automation"]);
            Assert.IsTrue(info.Recipes["iron-plate"]);
            Assert.IsFalse(info.Recipes["copper-plate"]);
        }

        //---- default saves-folder resolution (reference §5 step 1, the stale-path fallback walk) --------

        [TestMethod]
        public void ResolveDefaultSaveFileLocation_ValidLastLocation_ReturnsTheSavesFolderUnchanged() {
            string userData = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string savesDir = Path.Combine(userData, "saves");
            Directory.CreateDirectory(savesDir);
            File.WriteAllText(Path.Combine(userData, "factorio-current.log"), "log");

            string result = SaveFileReader.ResolveDefaultSaveFileLocation(savesDir);

            Assert.AreEqual(savesDir, result);
        }

        [TestMethod]
        public void ResolveDefaultSaveFileLocation_StaleLastLocation_FallsBackToEmptyWhenNoInstallIsFound() {
            string staleSaves = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "saves");
            string emptyHome = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyHome);

            string result = SaveFileReader.ResolveDefaultSaveFileLocation(staleSaves, factorioHomeOverride: emptyHome);

            Assert.AreEqual("", result);
        }

        //---- full pipeline against the stub executable (reference §5 steps 2-5) -------------------------

        [TestMethod]
        public void Load_HappyPath_ReturnsOkWithParsedSaveFileInfo() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = WriteFactorioStub(macOsDir, "2.0.28",
                "cat <<'FOREMAN_EOF'\n<<<START-EXPORT-P0>>>\n" +
                "{\"mods\":[],\"technologies\":[],\"recipes\":[{\"name\":\"iron-plate\",\"enabled\":true}]}\n" +
                "<<<END-EXPORT-P0>>>\nFOREMAN_EOF\n     exit 0");
            string savePath = BuildSaveFixture(exePath);

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Ok, result.Outcome);
            Assert.IsNotNull(result.SaveFileInfo);
            Assert.IsTrue(result.SaveFileInfo!.Recipes["iron-plate"]);
            Assert.IsNull(result.WarningMessage);
            Assert.AreEqual(FactorioRunFailure.None, result.Failure);
        }

        [TestMethod]
        public void Load_FactorioCrashes_ReturnsAbortWithTheVerbatimCrashMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = WriteFactorioStub(macOsDir, "2.0.28", "printf 'Received SIGSEGV\\n'; exit 1");
            string savePath = BuildSaveFixture(exePath);

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Abort, result.Outcome);
            Assert.AreEqual(
                "Factorio crashed while reading the save file.\n\n" +
                "This is usually caused by a mod bug. See factorio-current.log in your Factorio user data folder.",
                result.WarningMessage);
            Assert.AreEqual(FactorioRunFailure.Crashed, result.Failure);
            Assert.AreEqual(FactorioCrashPhase.ReadingSave, result.CrashPhase);
        }

        [TestMethod]
        public void Load_AnotherInstanceRunning_ReturnsCancelWithTheVerbatimMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = WriteFactorioStub(macOsDir, "2.0.28", "printf 'Is another instance already running?\\n'; exit 1");
            string savePath = BuildSaveFixture(exePath);

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Cancel, result.Outcome);
            Assert.AreEqual(
                "File read could not be completed because this instance of Factorio is currently running. Please stop expanding the factory for just a brief moment...",
                result.WarningMessage);
            Assert.AreEqual(FactorioRunFailure.AnotherInstanceRunning, result.Failure);
        }

        [TestMethod]
        public void Load_ExportOutputMissesTheP0Marker_ReturnsAbortWithNoMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = WriteFactorioStub(macOsDir, "2.0.28", "printf 'nothing useful here\\n'; exit 0");
            string savePath = BuildSaveFixture(exePath);

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Abort, result.Outcome);
            Assert.IsNull(result.WarningMessage);
            Assert.AreEqual(FactorioRunFailure.Unmapped, result.Failure);
        }

        [TestMethod]
        public void Load_FactorioVersionTooOld_ReturnsCancelWithTheValidatorMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = WriteFactorioStub(macOsDir, "1.1.0", "exit 0");
            string savePath = BuildSaveFixture(exePath);

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Cancel, result.Outcome);
            Assert.IsTrue(result.WarningMessage?.Contains("Factorio Version below 2.0", StringComparison.Ordinal));
            Assert.AreEqual(FactorioRunFailure.InvalidInstall, result.Failure);
            Assert.AreEqual(FactorioInstallProblem.TooOld, result.InstallCheck?.Problem);
            Assert.AreEqual("1.1.0", result.InstallCheck?.VersionText);
        }

        [TestMethod]
        public void Load_AFileSquatsTheSaveReaderModsFolderName_IsHelperModCopyFailed() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = WriteFactorioStub(macOsDir, "2.0.28", "exit 0");
            string savePath = BuildSaveFixture(exePath);
            string modsPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(savePath)!)!, "mods");
            File.WriteAllText(Path.Combine(modsPath, "foremansavereader_2.0.0"), "not a folder");

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Abort, result.Outcome);
            Assert.AreEqual(FactorioRunFailure.HelperModCopyFailed, result.Failure);
        }

        private const string EmptySaveReport =
            "cat <<'FOREMAN_EOF'\n<<<START-EXPORT-P0>>>\n{\"mods\":[]}\n<<<END-EXPORT-P0>>>\nFOREMAN_EOF\nexit 0";

        private static string ModsFolderOf(string savePath) =>
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(savePath)!)!, "mods");

        private static string RewriteStubToListMods(string macOsDir, string savePath, string seenFile, string version) =>
            WriteFactorioStub(macOsDir, version, "ls \"" + ModsFolderOf(savePath) + "\" > \"" + seenFile + "\"\n" + EmptySaveReport);

        [TestMethod]
        public void ReadSave_TwoZeroInstall_CopiesTwoZeroSaveReader() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string savePath = BuildSaveFixture(WriteFactorioStub(macOsDir, "2.0.76", "exit 0"));
            string seenFile = Path.Combine(Path.GetDirectoryName(savePath)!, "seen.txt");
            RewriteStubToListMods(macOsDir, savePath, seenFile, "2.0.76");

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Ok, result.Outcome);
            Assert.AreEqual("foremansavereader_2.0.0", File.ReadAllText(seenFile).Trim().Split('\n')[0]);
            Assert.IsFalse(Directory.Exists(Path.Combine(ModsFolderOf(savePath), "foremansavereader_2.0.0")));
        }

        [TestMethod]
        public void ReadSave_LeftoverSaveReaderOfOtherVersion_IsRemovedBeforeRun() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string savePath = BuildSaveFixture(WriteFactorioStub(macOsDir, "2.0.76", "exit 0"));
            string seenFile = Path.Combine(Path.GetDirectoryName(savePath)!, "seen.txt");
            RewriteStubToListMods(macOsDir, savePath, seenFile, "2.0.76");
            string leftover = Path.Combine(ModsFolderOf(savePath), "foremansavereader_2.1.0", "nested");
            Directory.CreateDirectory(leftover);
            File.WriteAllText(Path.Combine(leftover, "control.lua"), "stale");

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Ok, result.Outcome);
            string seen = File.ReadAllText(seenFile);
            Assert.Contains("foremansavereader_2.0.0", seen);
            Assert.DoesNotContain("foremansavereader_2.1.0", seen);
            Assert.IsFalse(Directory.Exists(Path.Combine(ModsFolderOf(savePath), "foremansavereader_2.1.0")));
        }

        [TestMethod]
        public void Load_MissingFactorioCurrentLog_ReturnsAbort() {
            string savePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "saves", "mysave.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            File.WriteAllText(savePath, "stand-in save");

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Abort, result.Outcome);
            Assert.AreEqual(FactorioRunFailure.Unmapped, result.Failure);
        }
    }
}
