using Foreman;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;

namespace ForemanTest {
    [TestClass]
    [UnsupportedOSPlatform("windows")]
    public class StubFactorioHarnessTests {
        [TestMethod]
        public void WriteScript_ProducesAnExecutableFileAtTheFactorioBinaryPath() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();

            string exePath = StubFactorioHarness.WriteScript(macOsDir, "exit 0\n");

            Assert.AreEqual(Path.Combine(macOsDir, "factorio"), exePath);
            Assert.IsTrue(File.Exists(exePath));
        }

        [TestMethod]
        public void WriteExportScript_RunOutput_ContainsAllSixMarkersAndCreatesTempSaveZip() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteExportScript(macOsDir, "$0\tiron-plate", "{\"p1\":1}", "{\"p2\":2}");

            FactorioRunResult result = FactorioBenchmarkRunner.Run(exePath, "--benchmark temp-save.zip", macOsDir, CancellationToken.None);

            Assert.IsTrue(File.Exists(Path.Combine(macOsDir, "temp-save.zip")));
            foreach (string marker in new[] {
                "<<<START-EXPORT-LN>>>", "<<<END-EXPORT-LN>>>",
                "<<<START-EXPORT-P1>>>", "<<<END-EXPORT-P1>>>",
                "<<<START-EXPORT-P2>>>", "<<<END-EXPORT-P2>>>" })
                Assert.IsTrue(result.Output.Contains(marker, System.StringComparison.Ordinal), marker);
            Assert.IsTrue(result.Output.Contains("{\"p1\":1}", System.StringComparison.Ordinal));
            Assert.IsTrue(result.Output.Contains("{\"p2\":2}", System.StringComparison.Ordinal));
        }

        [TestMethod]
        public void WriteExportScript_CanOmitTempSaveZip() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteExportScript(macOsDir, "$0\tiron-plate", "{}", "{}", createTempSave: false);

            FactorioBenchmarkRunner.Run(exePath, "--benchmark temp-save.zip", macOsDir, CancellationToken.None);

            Assert.IsFalse(File.Exists(Path.Combine(macOsDir, "temp-save.zip")));
        }

        // Proves the script is CWD-faithful, not script-location-faithful: temp-save.zip lands wherever
        // Run's workingDirectory points, even though the script itself lives somewhere else entirely -
        // matching the real "--create temp-save.zip" contract (io-reference.md §4 step 1).
        [TestMethod]
        public void WriteExportScript_TempSaveZipLandsInTheGivenWorkingDirectory_NotNextToTheScript() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteExportScript(macOsDir, "$0\tiron-plate", "{}", "{}");
            string workingDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(workingDirectory);

            FactorioBenchmarkRunner.Run(exePath, "--benchmark temp-save.zip", workingDirectory, CancellationToken.None);

            Assert.IsTrue(File.Exists(Path.Combine(workingDirectory, "temp-save.zip")));
            Assert.IsFalse(File.Exists(Path.Combine(macOsDir, "temp-save.zip")));
        }

        [TestMethod]
        public void WriteSaveReadScript_RunOutput_ContainsP0Markers() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteSaveReadScript(macOsDir, "{\"recipes\":{}}");

            FactorioRunResult result = FactorioBenchmarkRunner.Run(exePath, "--benchmark save.zip", macOsDir, CancellationToken.None);

            Assert.IsTrue(result.Output.Contains("<<<START-EXPORT-P0>>>", System.StringComparison.Ordinal));
            Assert.IsTrue(result.Output.Contains("<<<END-EXPORT-P0>>>", System.StringComparison.Ordinal));
        }

        [TestMethod]
        public void WriteVersionScript_RunOutput_ContainsTheGivenVersion() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteVersionScript(macOsDir, "2.0.28");

            FactorioRunResult result = FactorioBenchmarkRunner.Run(exePath, "--version", macOsDir, CancellationToken.None);

            Assert.IsTrue(result.Output.Contains("2.0.28", System.StringComparison.Ordinal));
            Assert.AreEqual(0, result.ExitCode);
        }
    }
}
