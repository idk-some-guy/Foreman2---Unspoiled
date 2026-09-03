using Foreman;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace ForemanTest {
    [TestClass]
    public class FactorioBundledModHelperTests {
        [TestMethod]
        public void CopyToModsFolder_ResolvesSourceAgainstExecutableDirectory_NotCurrentDirectory() {
            string originalCwd = Environment.CurrentDirectory;
            string unrelatedCwd = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(unrelatedCwd);
            string modsPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(modsPath);

            try {
                Environment.CurrentDirectory = unrelatedCwd;
                FactorioBundledModHelper.CopyToModsFolder("foremanexport_2.0.0", modsPath, "info.json");
            } finally {
                Environment.CurrentDirectory = originalCwd;
            }

            Assert.IsTrue(File.Exists(Path.Combine(modsPath, "foremanexport_2.0.0", "info.json")));
        }

        [TestMethod]
        public void CopyToModsFolder_ExportMod_LandsAllThreeFilesInOutputDir() {
            string modsPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(modsPath);

            FactorioBundledModHelper.CopyToModsFolder("foremanexport_2.0.0", modsPath,
                "info.json", "instrument-after-data.lua", "instrument-control.lua");

            string destDir = Path.Combine(modsPath, "foremanexport_2.0.0");
            string sourceDir = Path.Combine(AppPaths.ExecutableDirectory, "Mods", "foremanexport_2.0.0");
            foreach (string file in new[] { "info.json", "instrument-after-data.lua", "instrument-control.lua" }) {
                Assert.IsTrue(File.Exists(Path.Combine(destDir, file)));
                Assert.AreEqual(File.ReadAllText(Path.Combine(sourceDir, file)), File.ReadAllText(Path.Combine(destDir, file)));
            }
        }

        [TestMethod]
        public void CopyToModsFolder_SaveReaderMod_LandsBothFilesInOutputDir() {
            string modsPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(modsPath);

            FactorioBundledModHelper.CopyToModsFolder("foremansavereader_2.0.0", modsPath, "info.json", "instrument-control.lua");

            string destDir = Path.Combine(modsPath, "foremansavereader_2.0.0");
            Assert.IsTrue(File.Exists(Path.Combine(destDir, "info.json")));
            Assert.IsTrue(File.Exists(Path.Combine(destDir, "instrument-control.lua")));
        }
    }
}
