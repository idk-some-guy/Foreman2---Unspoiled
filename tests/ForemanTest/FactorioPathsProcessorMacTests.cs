using Foreman.DataCaching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace ForemanTest {
    [TestClass]
    public class FactorioPathsProcessorMacTests {
        [TestMethod]
        public void FindsSteamFactorioFromLibraryFoldersVdf() {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string lib = Path.Combine(root, "ExtLib");
            string vdfDir = Path.Combine(root, "Library", "Application Support", "Steam", "steamapps");
            Directory.CreateDirectory(vdfDir);
            string factorioDir = Path.Combine(lib, "steamapps", "common", "Factorio", "factorio.app", "Contents");
            Directory.CreateDirectory(Path.Combine(factorioDir, "MacOS"));
            File.WriteAllText(FactorioPathsProcessor.GetExecutablePath(factorioDir), "stand-in executable");
            File.WriteAllText(Path.Combine(vdfDir, "libraryfolders.vdf"),
                "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + lib + "\"\n\t}\n}\n");

            var candidates = FactorioPathsProcessor.GetFactorioInstallLocations(root);

            CollectionAssert.Contains(candidates, factorioDir);
        }

        [TestMethod]
        public void ResolvesUserDataPathFromConfigPathCfgAndConfigIni() {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string installPath = Path.Combine(root, "factorio.app", "Contents");
            Directory.CreateDirectory(installPath);
            File.WriteAllText(Path.Combine(installPath, "config-path.cfg"), "write-data= __PATH__executable__/../ConfigLoc\n");
            string configIniDir = Path.Combine(installPath, "ConfigLoc");
            Directory.CreateDirectory(configIniDir);
            File.WriteAllText(Path.Combine(configIniDir, "config.ini"), "write-data=.factorio/UserData\n");

            string userPath = FactorioPathsProcessor.GetFactorioUserPath(installPath);

            Assert.AreEqual(Path.Combine(installPath, "UserData"), userPath);
        }

        [TestMethod]
        public void ResolvesUserDataPathFromMacOsDefaultLocationWhenConfigPathCfgIsAbsent() {
            string home = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string installPath = Path.Combine(home, "SteamLibrary", "steamapps", "common", "Factorio", "factorio.app", "Contents");
            Directory.CreateDirectory(installPath);
            string defaultConfigDir = Path.Combine(home, "Library", "Application Support", "factorio", "config");
            Directory.CreateDirectory(defaultConfigDir);
            File.WriteAllText(Path.Combine(defaultConfigDir, "config.ini"), "write-data=__PATH__system-write-data__\n");

            string userPath = FactorioPathsProcessor.GetFactorioUserPath(installPath, homeOverride: home);

            Assert.AreEqual(Path.Combine(home, "Library", "Application Support", "factorio"), userPath);
        }

        [TestMethod]
        public void RealMachine_DetectsInstalledSteamFactorioAndResolvesUserData() {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string steamFactorioContents = Path.Combine(
                home, "Library", "Application Support", "Steam", "steamapps", "common", "Factorio", "factorio.app", "Contents");
            if (!File.Exists(FactorioPathsProcessor.GetExecutablePath(steamFactorioContents)))
                Assert.Inconclusive($"No Factorio install found at {steamFactorioContents}.");

            var candidates = FactorioPathsProcessor.GetFactorioInstallLocations();
            CollectionAssert.Contains(candidates, steamFactorioContents);

            string userPath = FactorioPathsProcessor.GetFactorioUserPath(steamFactorioContents);
            Assert.AreEqual(Path.Combine(home, "Library", "Application Support", "factorio"), userPath);
        }
    }
}
