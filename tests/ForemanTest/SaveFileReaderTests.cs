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
        }

        [TestMethod]
        public void Load_ExportOutputMissesTheP0Marker_ReturnsAbortWithNoMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = WriteFactorioStub(macOsDir, "2.0.28", "printf 'nothing useful here\\n'; exit 0");
            string savePath = BuildSaveFixture(exePath);

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Abort, result.Outcome);
            Assert.IsNull(result.WarningMessage);
        }

        [TestMethod]
        public void Load_FactorioVersionTooOld_ReturnsCancelWithTheValidatorMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = WriteFactorioStub(macOsDir, "1.1.0", "exit 0");
            string savePath = BuildSaveFixture(exePath);

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Cancel, result.Outcome);
            Assert.IsTrue(result.WarningMessage?.Contains("Factorio Version below 2.0", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Load_MissingFactorioCurrentLog_ReturnsAbort() {
            string savePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "saves", "mysave.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            File.WriteAllText(savePath, "stand-in save");

            SaveFileReader.Result result = SaveFileReader.Load(savePath, CancellationToken.None);

            Assert.AreEqual(SaveFileLoadOutcome.Abort, result.Outcome);
        }
    }
}
