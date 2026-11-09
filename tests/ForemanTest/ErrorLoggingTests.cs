using Foreman;
using Foreman.DataCaching;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace ForemanTest {
    [TestClass]
    public class ErrorLoggingTests : ForemanTestBase {
        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [TestMethod]
        public void LogException_AndLogLine_DoNotThrow() {
            string dir = NewTempDir();
            using (ErrorLogging.UseIsolatedLogDirectory(dir)) {
                string logPath = Path.Combine(dir, "errorlog.txt");
                ErrorLogging.ClearLog();
                ErrorLogging.LogLine("test line");
                ErrorLogging.LogException(new InvalidOperationException("inner"), "test context");
                Assert.IsTrue(File.Exists(logPath));
                string log = File.ReadAllText(logPath);
                Assert.Contains("test line", log);
                Assert.Contains("test context", log);
                Assert.Contains("InvalidOperationException", log);
            }
        }

        //File-location policy (docs/upstream-divergences.md): errorlog.txt was the one write still landing
        //next to the executable - a signed .app bundle's own directory isn't writable, matching every other
        //write this port already moved (settings.json, saved graphs, exports, imported presets). Clears the
        //process-wide test default (ForemanTestAssemblySetup) for the duration of the assertion, since with
        //neither seam active is exactly the production fallback this proves.
        [TestMethod]
        public void LogFilePath_NoOverridesActive_DefaultsUnderUserDataDirectory_NotExecutableDirectory() {
            string? savedDefault = ErrorLogging.DefaultLogDirectory;
            try {
                ErrorLogging.SetDefaultLogDirectory(null);
                Assert.AreEqual(Path.Combine(AppPaths.UserDataDirectory, "errorlog.txt"), ErrorLogging.LogFilePath);
            } finally {
                ErrorLogging.SetDefaultLogDirectory(savedDefault);
            }
        }

        //Final-review C2 guard: the process-wide default (ForemanTestAssemblySetup.AssemblyInitialize) must
        //already be active by the time any test runs, so a bare, unwrapped LogFilePath read - exactly what
        //PresetExportFormatTests/IconPipelineTests do - can never resolve under the real user profile.
        [TestMethod]
        public void LogFilePath_UnderTestAssembly_IsNotUnderRealUserProfile() {
            Assert.IsFalse(ErrorLogging.LogFilePath.StartsWith(AppPaths.UserDataDirectory, StringComparison.Ordinal));
        }

        //UserDataDirectory (~/Library/Application Support/Foreman) isn't guaranteed to exist yet on a fresh
        //install - SettingsService.Save already creates it defensively before writing settings.json, and a
        //log write needs the same guard or it silently vanishes into LogLine's own catch-and-Trace fallback
        //on every single run until something else happens to create the directory first.
        [TestMethod]
        public void LogLine_UserDataDirectoryDoesNotYetExist_CreatesItAndWritesTheLog() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()); //deliberately not created
            using (ErrorLogging.UseIsolatedLogDirectory(dir)) {
                ErrorLogging.LogLine("fresh install log line");
                Assert.IsTrue(File.Exists(Path.Combine(dir, "errorlog.txt")));
            }
        }
    }
}
