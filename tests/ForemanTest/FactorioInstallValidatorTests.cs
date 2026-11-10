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

        private static FactorioInstallCheck Check(string exePath) => FactorioInstallValidator.CheckExecutable(exePath);

        [TestMethod]
        public void CheckExecutable_MacBundleWith2xVersion_Passes() {
            string contents = WriteBundle("2.0.28");

            FactorioInstallCheck check = Check(FactorioPathsProcessor.GetExecutablePath(contents));

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.None, "2.0.28"), check);
            Assert.IsNull(FactorioInstallValidator.UserMessage(check));
        }

        [TestMethod]
        public void CheckExecutable_MacBundleWith1xVersion_IsTooOld() {
            string contents = WriteBundle("1.1.110");

            FactorioInstallCheck check = Check(FactorioPathsProcessor.GetExecutablePath(contents));

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.TooOld, "1.1.110"), check);
            Assert.Contains("2.0", FactorioInstallValidator.UserMessage(check)!);
        }

        [TestMethod]
        public void CheckExecutable_StdoutVersion2x_Passes() {
            string exePath = StubFactorioHarness.WriteVersionScript(StubFactorioHarness.CreateExecutableDirectory(), "2.0.28");

            FactorioInstallCheck check = Check(exePath);

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.None, "2.0.28"), check);
            Assert.IsNull(FactorioInstallValidator.UserMessage(check));
        }

        [TestMethod]
        public void CheckExecutable_StdoutVersion1x_IsTooOld_AndTheLogSaysBelow2Point0() {
            string exePath = StubFactorioHarness.WriteVersionScript(StubFactorioHarness.CreateExecutableDirectory(), "1.1.110");

            FactorioInstallCheck check = Check(exePath);

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.TooOld, "1.1.110"), check);
            Assert.Contains("below 2.0", FactorioInstallValidator.UserMessage(check)!);
        }

        [TestMethod]
        public void CheckExecutable_StdoutVersion3x_IsTooNew() {
            string exePath = StubFactorioHarness.WriteVersionScript(StubFactorioHarness.CreateExecutableDirectory(), "3.0.0");

            FactorioInstallCheck check = Check(exePath);

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.TooNew, "3.0.0"), check);
            Assert.Contains("3.x+", FactorioInstallValidator.UserMessage(check)!);
        }

        [TestMethod]
        public void CheckExecutable_StdoutVersionBelow2Point0Point7_IsTooOld() {
            string exePath = StubFactorioHarness.WriteVersionScript(StubFactorioHarness.CreateExecutableDirectory(), "2.0.6");

            FactorioInstallCheck check = Check(exePath);

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.TooOld, "2.0.6"), check);
            string message = FactorioInstallValidator.UserMessage(check)!;
            Assert.Contains("2.0.6", message);
            Assert.Contains("2.0.7", message);
        }

        [TestMethod]
        public void CheckExecutable_MissingExecutable_IsExecutableMissing() {
            string exePath = Path.Combine(StubFactorioHarness.CreateExecutableDirectory(), "factorio");

            FactorioInstallCheck check = Check(exePath);

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.ExecutableMissing, ""), check);
            Assert.AreEqual("Could not find factorio.exe. Please select a valid Factorio install location.", FactorioInstallValidator.UserMessage(check));
        }

        [TestMethod]
        public void CheckExecutable_StdoutVersion_TakesPriorityOverInfoPlist() {
            string contents = WriteBundle("1.0.0"); // plist alone would reject this bundle
            StubFactorioHarness.WriteScript(Path.Combine(contents, "MacOS"), "printf 'Version: 2.0.28 (build 1, mac64, headless)\\n'\nexit 0\n");

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.None, "2.0.28"), Check(FactorioPathsProcessor.GetExecutablePath(contents)));
        }

        [TestMethod]
        public void CheckExecutable_StdoutHasDecoyVersionLineBeforeRealBanner_UsesFirstLineStartingWithVersion() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            StubFactorioHarness.WriteScript(macOsDir,
                "printf 'noise version: 9.9.9 not a real banner line\\nVersion: 2.0.28 (build 1, mac64, headless)\\n'\nexit 0\n");

            Assert.AreEqual(new FactorioInstallCheck(FactorioInstallProblem.None, "2.0.28"), Check(Path.Combine(macOsDir, "factorio")));
        }
    }
}
