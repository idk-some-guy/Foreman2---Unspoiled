using Foreman;
using Foreman.DataCaching;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.Versioning;

namespace ForemanTest {
    [TestClass]
    [UnsupportedOSPlatform("windows")]
    public class FactorioInstallValidatorTests {
        private static string WriteBundle(string version) {
            string contents = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "factorio.app", "Contents");
            Directory.CreateDirectory(Path.Combine(contents, "MacOS"));
            File.WriteAllText(FactorioPathsProcessor.GetExecutablePath(contents), "stand-in executable");
            File.WriteAllText(Path.Combine(contents, "Info.plist"),
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                "<plist version=\"1.0\"><dict>\n" +
                "<key>CFBundleShortVersionString</key><string>" + version + "</string>\n" +
                "<key>CFBundleVersion</key><string>" + version + "</string>\n" +
                "</dict></plist>\n");
            return contents;
        }

        [TestMethod]
        public void TryValidateExecutable_MacBundleWith2xVersion_Validates() {
            string contents = WriteBundle("2.0.28");

            bool valid = FactorioInstallValidator.TryValidateExecutable(
                FactorioPathsProcessor.GetExecutablePath(contents), out string? userMessage);

            Assert.IsTrue(valid);
            Assert.IsNull(userMessage);
        }

        [TestMethod]
        public void TryValidateExecutable_MacBundleWith1xVersion_Rejects() {
            string contents = WriteBundle("1.1.110");

            bool valid = FactorioInstallValidator.TryValidateExecutable(
                FactorioPathsProcessor.GetExecutablePath(contents), out string? userMessage);

            Assert.IsFalse(valid);
            Assert.IsNotNull(userMessage);
            Assert.Contains("2.0", userMessage);
        }

        [TestMethod]
        public void TryValidateExecutable_StdoutVersion2x_Validates() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteVersionScript(macOsDir, "2.0.28");

            bool valid = FactorioInstallValidator.TryValidateExecutable(exePath, out string? userMessage);

            Assert.IsTrue(valid);
            Assert.IsNull(userMessage);
        }

        [TestMethod]
        public void TryValidateExecutable_StdoutVersion1x_RejectsBelow2Point0() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteVersionScript(macOsDir, "1.1.110");

            bool valid = FactorioInstallValidator.TryValidateExecutable(exePath, out string? userMessage);

            Assert.IsFalse(valid);
            Assert.IsNotNull(userMessage);
            Assert.Contains("below 2.0", userMessage);
        }

        [TestMethod]
        public void TryValidateExecutable_StdoutVersion3x_RejectsAsUnsupported() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteVersionScript(macOsDir, "3.0.0");

            bool valid = FactorioInstallValidator.TryValidateExecutable(exePath, out string? userMessage);

            Assert.IsFalse(valid);
            Assert.IsNotNull(userMessage);
            Assert.Contains("3.x+", userMessage);
        }

        [TestMethod]
        public void TryValidateExecutable_StdoutVersionBelow2Point0Point7_RejectsAsTooOld() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteVersionScript(macOsDir, "2.0.6");

            bool valid = FactorioInstallValidator.TryValidateExecutable(exePath, out string? userMessage);

            Assert.IsFalse(valid);
            Assert.IsNotNull(userMessage);
            Assert.Contains("2.0.6", userMessage);
            Assert.Contains("2.0.7", userMessage);
        }

        [TestMethod]
        public void TryValidateExecutable_MissingExecutable_RejectsWithNotFoundMessage() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = Path.Combine(macOsDir, "factorio");

            bool valid = FactorioInstallValidator.TryValidateExecutable(exePath, out string? userMessage);

            Assert.IsFalse(valid);
            Assert.AreEqual("Could not find factorio.exe. Please select a valid Factorio install location.", userMessage);
        }

        [TestMethod]
        public void TryValidateExecutable_StdoutVersion_TakesPriorityOverInfoPlist() {
            string contents = WriteBundle("1.0.0"); // plist alone would reject this bundle
            StubFactorioHarness.WriteScript(Path.Combine(contents, "MacOS"), "printf 'Version: 2.0.28 (build 1, mac64, headless)\\n'\nexit 0\n");

            bool valid = FactorioInstallValidator.TryValidateExecutable(
                FactorioPathsProcessor.GetExecutablePath(contents), out string? userMessage);

            Assert.IsTrue(valid, userMessage);
        }

        [TestMethod]
        public void TryValidateExecutable_StdoutHasDecoyVersionLineBeforeRealBanner_UsesFirstLineStartingWithVersion() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteScript(macOsDir,
                "printf 'noise version: 9.9.9 not a real banner line\\nVersion: 2.0.28 (build 1, mac64, headless)\\n'\nexit 0\n");
            string exePath = Path.Combine(macOsDir, "factorio");

            bool valid = FactorioInstallValidator.TryValidateExecutable(exePath, out string? userMessage);

            Assert.IsTrue(valid, userMessage);
        }
    }
}
