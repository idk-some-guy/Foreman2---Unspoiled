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
        public void ShowOutdatedWarningIfNeeded_OldVersion_RaisesMessage() {
            var dc = new DataCache(false);
            ErrorLogging.ClearLog();
            PresetExportFormat.ShowOutdatedWarningIfNeeded(dc);
            string log = File.ReadAllText(ErrorLogging.LogFilePath);
            Assert.Contains("older version of Foreman", log);
            Assert.Contains("Settings menu", log);
        }

        [TestMethod]
        public void ShowOutdatedWarningIfNeeded_CurrentVersion_DoesNotRaiseMessage() {
            var dc = new DataCache(false);
            dc.GetType().GetProperty("Version")!.SetMethod!.Invoke(dc, [1]);
            ErrorLogging.ClearLog();
            PresetExportFormat.ShowOutdatedWarningIfNeeded(dc);
            string log = File.ReadAllText(ErrorLogging.LogFilePath);
            Assert.DoesNotContain("older version of Foreman", log);
        }
    }
}
