using Foreman.DataCaching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace ForemanTest {
    //Mirrors FactorioPathsProcessorMacTests but for the Linux branch (docs/upstream-divergences.md, phase 8
    //Task 2), exercised via the isMacOsOverride seam since this box only ever runs on macOS.
    [TestClass]
    public class FactorioPathsProcessorLinuxTests {
        [TestMethod]
        public void FindsStandaloneFactorioAtDotFactorioUnderHome() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string installDir = Path.Combine(home, ".factorio");
            Directory.CreateDirectory(Path.Combine(installDir, "bin", "x64"));
            File.WriteAllText(FactorioPathsProcessor.GetExecutablePath(installDir, isMacOsOverride: false), "stand-in executable");

            var candidates = FactorioPathsProcessor.GetFactorioInstallLocations(home, isMacOsOverride: false);

            CollectionAssert.Contains(candidates, installDir);
        }

        [TestMethod]
        public void FindsSteamFactorioFromNativeLocalShareSteamLibraryFoldersVdf() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string lib = Path.Combine(home, "ExtLib");
            string vdfDir = Path.Combine(home, ".local", "share", "Steam", "steamapps");
            Directory.CreateDirectory(vdfDir);
            string factorioDir = Path.Combine(lib, "steamapps", "common", "Factorio");
            Directory.CreateDirectory(Path.Combine(factorioDir, "bin", "x64"));
            File.WriteAllText(FactorioPathsProcessor.GetExecutablePath(factorioDir, isMacOsOverride: false), "stand-in executable");
            File.WriteAllText(Path.Combine(vdfDir, "libraryfolders.vdf"),
                "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + lib + "\"\n\t}\n}\n");

            var candidates = FactorioPathsProcessor.GetFactorioInstallLocations(home, isMacOsOverride: false);

            CollectionAssert.Contains(candidates, factorioDir);
        }

        [TestMethod]
        public void FindsSteamFactorioFromDotSteamRootLibraryFoldersVdf() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string lib = Path.Combine(home, "ExtLib");
            string vdfDir = Path.Combine(home, ".steam", "root", "steamapps");
            Directory.CreateDirectory(vdfDir);
            //"Factorio" (not "factorio"): the candidate loop tries this casing first, and this dev box's
            //case-insensitive filesystem would otherwise match a lowercase-only directory under this name too.
            string factorioDir = Path.Combine(lib, "steamapps", "common", "Factorio");
            Directory.CreateDirectory(Path.Combine(factorioDir, "bin", "x64"));
            File.WriteAllText(FactorioPathsProcessor.GetExecutablePath(factorioDir, isMacOsOverride: false), "stand-in executable");
            File.WriteAllText(Path.Combine(vdfDir, "libraryfolders.vdf"),
                "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + lib + "\"\n\t}\n}\n");

            var candidates = FactorioPathsProcessor.GetFactorioInstallLocations(home, isMacOsOverride: false);

            CollectionAssert.Contains(candidates, factorioDir);
        }

        [TestMethod]
        public void ResolvesUserDataPathFromLinuxDefaultLocationWhenConfigPathCfgIsAbsent() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string installPath = Path.Combine(home, ".factorio");
            Directory.CreateDirectory(installPath);
            string defaultConfigDir = Path.Combine(home, ".factorio", "config");
            Directory.CreateDirectory(defaultConfigDir);
            File.WriteAllText(Path.Combine(defaultConfigDir, "config.ini"), "write-data=__PATH__system-write-data__\n");

            string userPath = FactorioPathsProcessor.GetFactorioUserPath(installPath, homeOverride: home, isMacOsOverride: false);

            Assert.AreEqual(Path.Combine(home, ".factorio"), userPath);
        }

        [TestMethod]
        public void ResolvesUserDataPathFromConfigPathCfgAndConfigIni_UsingLinuxExecutableFolder() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string installPath = Path.Combine(home, "Factorio");
            Directory.CreateDirectory(installPath);
            File.WriteAllText(Path.Combine(installPath, "config-path.cfg"), "write-data= __PATH__executable__/../../ConfigLoc\n");
            string configIniDir = Path.Combine(installPath, "ConfigLoc");
            Directory.CreateDirectory(configIniDir);
            File.WriteAllText(Path.Combine(configIniDir, "config.ini"), "write-data=.factorio/UserData\n");

            string userPath = FactorioPathsProcessor.GetFactorioUserPath(installPath, homeOverride: home, isMacOsOverride: false);

            Assert.AreEqual(Path.Combine(installPath, "UserData"), userPath);
        }

        [TestMethod]
        public void GetExecutablePath_ResolvesBinX64FactorioNoAppBundle() {
            string installPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            string exePath = FactorioPathsProcessor.GetExecutablePath(installPath, isMacOsOverride: false);

            Assert.AreEqual(Path.Combine(installPath, "bin", "x64", "factorio"), exePath);
        }

        [TestMethod]
        public void TryNormalizeInstallPath_NoAppBundleFallback_FailsWhenExecutableMissing() {
            string selected = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(selected, "Contents", "MacOS"));
            File.WriteAllText(Path.Combine(selected, "Contents", "MacOS", "factorio"), "stand-in executable");

            bool found = FactorioPathsProcessor.TryNormalizeInstallPath(selected, out string installRoot, isMacOsOverride: false);

            Assert.IsFalse(found); //Linux never falls back to a Contents/MacOS bundle layout
            Assert.AreEqual(selected, installRoot);
        }

        [TestMethod]
        public void TryNormalizeInstallPath_FindsBinX64FactorioDirectly() {
            string selected = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(selected, "bin", "x64"));
            File.WriteAllText(Path.Combine(selected, "bin", "x64", "factorio"), "stand-in executable");

            bool found = FactorioPathsProcessor.TryNormalizeInstallPath(selected, out string installRoot, isMacOsOverride: false);

            Assert.IsTrue(found);
            Assert.AreEqual(selected, installRoot);
        }
    }
}
