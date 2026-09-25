using Foreman.DataCaching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace ForemanTest {
    //Covers PresetProcessor.GetPresetPath's file-location policy directly (docs/upstream-divergences.md,
    //the file-location-policy entry): the user directory wins on a name collision by content, not merely by
    //existing - a stale bundle-shipped preset sharing a name with a freshly imported user preset must never
    //shadow it.
    [TestClass]
    public class PresetProcessorTests {
        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [TestMethod]
        public void GetPresetPath_SameNameInUserAndBundleDirectories_UserDirectoryWinsOnContent() {
            string bundleDir = NewTempDir();
            string userDir = NewTempDir();
            File.WriteAllText(Path.Combine(bundleDir, "Shared Preset.pjson"), "{\"source\":\"bundle\"}");
            File.WriteAllText(Path.Combine(userDir, "Shared Preset.pjson"), "{\"source\":\"user\"}");

            string resolvedPath = PresetProcessor.GetPresetPath("Shared Preset", ".pjson", userDir, bundleDir);

            Assert.AreEqual(Path.Combine(userDir, "Shared Preset.pjson"), resolvedPath);
            Assert.AreEqual("{\"source\":\"user\"}", File.ReadAllText(resolvedPath));
        }

        [TestMethod]
        public void GetPresetPath_OnlyInBundleDirectory_FallsBackToBundleContent() {
            string bundleDir = NewTempDir();
            string userDir = NewTempDir();
            File.WriteAllText(Path.Combine(bundleDir, "Bundle Only.pjson"), "{\"source\":\"bundle\"}");

            string resolvedPath = PresetProcessor.GetPresetPath("Bundle Only", ".pjson", userDir, bundleDir);

            Assert.AreEqual(Path.Combine(bundleDir, "Bundle Only.pjson"), resolvedPath);
            Assert.AreEqual("{\"source\":\"bundle\"}", File.ReadAllText(resolvedPath));
        }

        //GetUserPresetPath's default must go through ResolveUserDataDirectory's test-assembly override
        //(set in ForemanTestAssemblySetup / UiTestAssemblySetup), the one seam every other no-override
        //caller in this class already goes through. A caller with no explicit override must land under
        //that same seam, not under the real user directory.
        [TestMethod]
        public void GetUserPresetPath_NoOverride_ResolvesUnderResolveUserDataDirectory() {
            string expected = Path.Combine(PresetProcessor.ResolveUserDataDirectory(), "Presets", "Some Preset.pjson");

            string resolvedPath = PresetProcessor.GetUserPresetPath("Some Preset", ".pjson");

            Assert.AreEqual(expected, resolvedPath);
        }
    }
}
