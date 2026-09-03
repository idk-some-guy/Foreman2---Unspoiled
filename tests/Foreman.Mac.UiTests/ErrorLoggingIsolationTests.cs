using Foreman.DataCaching;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests {
    public class ErrorLoggingIsolationTests {
        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void UseIsolatedLogDirectory_RedirectsLogFilePathAndRestoresOnDispose() {
            string defaultPath = ErrorLogging.LogFilePath;
            string isolatedDir = NewTempDir();

            using (ErrorLogging.UseIsolatedLogDirectory(isolatedDir))
                Assert.Equal(Path.Combine(isolatedDir, "errorlog.txt"), ErrorLogging.LogFilePath);

            Assert.Equal(defaultPath, ErrorLogging.LogFilePath);
        }

        [Fact]
        public async Task UseIsolatedLogDirectory_ConcurrentFlows_DoNotClobberEachOthersLog() {
            string dirA = NewTempDir();
            string dirB = NewTempDir();
            var barrier = new Barrier(2);

            async Task RunFlow(string dir, string marker) {
                using (ErrorLogging.UseIsolatedLogDirectory(dir)) {
                    ErrorLogging.ClearLog();
                    barrier.SignalAndWait();
                    await Task.Yield();
                    ErrorLogging.LogLine(marker);
                    barrier.SignalAndWait();
                    Assert.Contains(marker, File.ReadAllText(ErrorLogging.LogFilePath));
                }
            }

            CancellationToken ct = TestContext.Current.CancellationToken;
            await Task.WhenAll(Task.Run(() => RunFlow(dirA, "marker-a"), ct), Task.Run(() => RunFlow(dirB, "marker-b"), ct));
        }
    }
}
