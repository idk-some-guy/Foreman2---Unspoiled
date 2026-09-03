using Foreman;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace ForemanTest {
    //Exercises AppPaths' platform fork (docs/upstream-divergences.md, phase 8 Task 2): macOS keeps
    //~/Documents and ~/Library/Application Support, Linux honors XDG_DOCUMENTS_DIR/XDG_DATA_HOME with
    //~/Documents and ~/.local/share fallbacks. The isMacOsOverride seam exercises both branches on this
    //macOS-only box; nothing here reads or writes a real user path.
    [TestClass]
    public class AppPathsTests {
        [TestCleanup]
        public void ClearOverrides() {
            AppPaths.SetIsMacOsOverride(null);
            Environment.SetEnvironmentVariable("XDG_DOCUMENTS_DIR", null);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
        }

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        [TestMethod]
        public void OnMacOs_SavedGraphsDirectory_UnderHomeDocuments() {
            AppPaths.SetIsMacOsOverride(true);

            Assert.AreEqual(Path.Combine(Home, "Documents", "Foreman", "Saved Graphs"), AppPaths.SavedGraphsDirectory);
        }

        [TestMethod]
        public void OnMacOs_UserDataDirectory_UnderLibraryApplicationSupport() {
            AppPaths.SetIsMacOsOverride(true);

            Assert.AreEqual(Path.Combine(Home, "Library", "Application Support", "Foreman"), AppPaths.UserDataDirectory);
        }

        [TestMethod]
        public void OnLinux_NoXdgVarsSet_FallsBackToHomeDocumentsAndLocalShare() {
            AppPaths.SetIsMacOsOverride(false);

            Assert.AreEqual(Path.Combine(Home, "Documents", "Foreman", "Exported Graphs"), AppPaths.ExportedGraphsDirectory);
            Assert.AreEqual(Path.Combine(Home, ".local", "share", "Foreman"), AppPaths.UserDataDirectory);
        }

        [TestMethod]
        public void OnLinux_XdgDocumentsDirSet_SavedGraphsDirectoryFollowsIt() {
            AppPaths.SetIsMacOsOverride(false);
            string xdgDocuments = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Environment.SetEnvironmentVariable("XDG_DOCUMENTS_DIR", xdgDocuments);

            Assert.AreEqual(Path.Combine(xdgDocuments, "Foreman", "Saved Graphs"), AppPaths.SavedGraphsDirectory);
        }

        [TestMethod]
        public void OnLinux_XdgDataHomeSet_UserDataDirectoryFollowsIt() {
            AppPaths.SetIsMacOsOverride(false);
            string xdgDataHome = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", xdgDataHome);

            Assert.AreEqual(Path.Combine(xdgDataHome, "Foreman"), AppPaths.UserDataDirectory);
            Assert.AreEqual(Path.Combine(xdgDataHome, "Foreman", "Presets"), AppPaths.UserPresetsDirectory);
            Assert.AreEqual(Path.Combine(xdgDataHome, "Foreman", "Scratch"), AppPaths.ScratchDirectory);
        }
    }
}
