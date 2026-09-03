using Foreman.DataCaching;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Foreman.Mac.UiTests.support {
    //Final-review C2: xUnit has no built-in assembly-initialize hook, so a module initializer stands in -
    //it runs once when the runtime loads this assembly, before any test method. Mirrors ForemanTest's
    //AssemblyInitialize: points ErrorLogging's process-wide default at a per-run temp directory so no test
    //in this assembly can reach the real errorlog.txt path, even one that reads LogFilePath without
    //wrapping it in UseIsolatedLogDirectory.
    internal static class UiTestAssemblySetup {
        [ModuleInitializer]
        internal static void Initialize() {
            string directory = Path.Combine(Path.GetTempPath(), "foreman-uitests-errorlog-" + Guid.NewGuid().ToString("N"));
            ErrorLogging.SetDefaultLogDirectory(directory);
        }
    }
}
