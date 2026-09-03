using Foreman;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Versioning;
using System.Threading;

namespace ForemanTest {
    [TestClass]
    [UnsupportedOSPlatform("windows")]
    public class FactorioBenchmarkRunnerTests : ForemanTestBase {
        [TestMethod]
        public void IsCrashOutput_DetectsSigSegvAndCrashHandler() {
            Assert.IsTrue(FactorioBenchmarkRunner.IsCrashOutput("Error CrashHandler.cpp:641: Received SIGSEGV"));
            Assert.IsTrue(FactorioBenchmarkRunner.IsCrashOutput("Factorio crashed. Generating symbolized stacktrace"));
            Assert.IsTrue(FactorioBenchmarkRunner.IsCrashOutput("CrashDump success"));
        }

        [TestMethod]
        public void IsCrashOutput_NormalExportOutput_IsFalse() {
            Assert.IsFalse(FactorioBenchmarkRunner.IsCrashOutput("<<<END-EXPORT-P1>>>\n<<<END-EXPORT-P2>>>"));
            Assert.IsFalse(FactorioBenchmarkRunner.IsCrashOutput(""));
        }

        [TestMethod]
        public void Run_CapturesStdoutFromTheStubExecutable() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteEchoScript(macOsDir, "<<<END-EXPORT-P1>>>\n<<<END-EXPORT-P2>>>\n");

            FactorioRunResult result = FactorioBenchmarkRunner.Run(exePath, "--benchmark temp-save.zip", macOsDir, CancellationToken.None);

            Assert.AreEqual("<<<END-EXPORT-P1>>>\n<<<END-EXPORT-P2>>>\n", result.Output);
            Assert.AreEqual(0, result.ExitCode);
            Assert.IsFalse(result.Crashed);
        }

        [TestMethod]
        public void Run_StubEmitsSigSegv_ReportsCrashed() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteCrashScript(macOsDir);

            FactorioRunResult result = FactorioBenchmarkRunner.Run(exePath, "--benchmark temp-save.zip", macOsDir, CancellationToken.None);

            Assert.IsTrue(result.Crashed);
            Assert.AreNotEqual(0, result.ExitCode);
        }

        [TestMethod]
        public void Run_StubEmitsAnotherInstanceMessage_IsDetectedByOutput() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteAnotherInstanceScript(macOsDir);

            FactorioRunResult result = FactorioBenchmarkRunner.Run(exePath, "--create temp-save.zip", macOsDir, CancellationToken.None);

            Assert.IsTrue(FactorioBenchmarkRunner.IsAnotherInstanceRunning(result.Output));
            Assert.IsFalse(result.Crashed);
        }

        // Pre-cancellation only: the loop still runs the child to completion (ReadToEnd blocks
        // until it exits), then discards the real output because the token was already cancelled.
        // A token cancelled mid-run does NOT preempt the process the same way.
        [TestMethod]
        public void Run_TokenAlreadyCancelledBeforeCall_ReturnsCancelledSentinel_DiscardingTheRealOutput() {
            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string exePath = StubFactorioHarness.WriteSleepThenEchoScript(macOsDir, sleepSeconds: 0.3, stdout: "should never surface");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            bool cancelledCallbackFired = false;

            FactorioRunResult result = FactorioBenchmarkRunner.Run(exePath, "", macOsDir, cts.Token, onCancelled: () => cancelledCallbackFired = true);

            Assert.IsTrue(cancelledCallbackFired);
            Assert.AreEqual(string.Empty, result.Output);
            Assert.AreEqual(-1, result.ExitCode);
            Assert.IsFalse(result.Crashed);
        }
    }
}
