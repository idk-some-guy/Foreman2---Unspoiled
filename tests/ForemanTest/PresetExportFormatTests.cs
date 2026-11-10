using Foreman;
using Foreman.DataCaching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace ForemanTest {
    [TestClass]
    public class PresetExportFormatTests {
        [TestMethod]
        public void ReadVersion_MissingProperty_ReturnsZero() {
            var dc = new DataCache(false);
            Assert.AreEqual(0, PresetExportFormat.ReadVersion(dc));
            Assert.IsTrue(PresetExportFormat.IsOutdated(dc));
        }

        [TestMethod]
        public void ReadVersion_CurrentVersion_IsNotOutdated() {
            var dc = new DataCache(false);
            dc.GetType().GetProperty("Version")!.SetMethod!.Invoke(dc, [1]);
            Assert.AreEqual(PresetExportFormat.CurrentVersion, PresetExportFormat.ReadVersion(dc));
            Assert.IsFalse(PresetExportFormat.IsOutdated(dc));
        }

        [TestMethod]
        public void ShowOutdatedWarningIfNeeded_OldVersion_ReturnsSentence() {
            var dc = new DataCache(false);
            dc.GetType().GetProperty("PresetName")!.SetMethod!.Invoke(dc, ["Old Modpack"]);
            ErrorLogging.ClearLog();

            string? sentence = PresetExportFormat.ShowOutdatedWarningIfNeeded(dc);

            Assert.AreEqual("To get the latest fixes, import it again from Factorio in Settings > Presets.", sentence);
            string log = File.ReadAllText(ErrorLogging.LogFilePath);
            Assert.Contains("Preset \"Old Modpack\" was made by an older version of Foreman. To get the latest fixes, import it again from Factorio in Settings > Presets.", log);
        }

        [TestMethod]
        public void ShowOutdatedWarningIfNeeded_CurrentVersion_ReturnsNull() {
            var dc = new DataCache(false);
            dc.GetType().GetProperty("Version")!.SetMethod!.Invoke(dc, [1]);
            ErrorLogging.ClearLog();

            Assert.IsNull(PresetExportFormat.ShowOutdatedWarningIfNeeded(dc));
            Assert.DoesNotContain("older version of Foreman", File.ReadAllText(ErrorLogging.LogFilePath));
        }

        [TestMethod]
        public void ExportMod_TwoZero_WritesTheBaseModVersionAsGameVersion() {
            string lua = File.ReadAllText(Path.Combine(AppPaths.ExecutableDirectory, "Mods", "foremanexport_2.0.0", "instrument-control.lua"));

            Assert.Contains("etable['game_version'] = script.active_mods['base']", lua);
        }
    }
}
