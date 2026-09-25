using Foreman.DataCaching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace ForemanTest.support {
    [TestClass]
    public static class ForemanTestAssemblySetup {
        //PresetExportFormatTests/IconPipelineTests read ErrorLogging.LogFilePath without
        //wrapping every call in UseIsolatedLogDirectory. Pointing the process-wide default at a per-run
        //temp directory here means no test in this assembly can reach the real errorlog.txt path even
        //without that per-test wrapping - an explicit UseIsolatedLogDirectory flow still overrides this.
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _) {
            string directory = Path.Combine(Path.GetTempPath(), "foreman-tests-errorlog-" + Guid.NewGuid().ToString("N"));
            ErrorLogging.SetDefaultLogDirectory(directory);

            //PresetProcessor/PresetImporter open a db (foreman.db) whenever a caller
            //omits its own directory override - same reasoning as the log directory above, pointed at a
            //per-run temp directory instead of the real AppPaths.UserDataDirectory.
            string dbDirectory = Path.Combine(Path.GetTempPath(), "foreman-tests-db-" + Guid.NewGuid().ToString("N"));
            PresetProcessor.SetDefaultUserDataDirectoryOverride(dbDirectory);
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup() {
        }
    }
}
