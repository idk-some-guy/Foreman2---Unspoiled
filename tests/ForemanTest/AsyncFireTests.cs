using Foreman;
using Foreman.DataCaching;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class AsyncFireTests : ForemanTestBase {
        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [TestMethod]
        public void Fire_FaultedTask_LogsExceptionInsteadOfLosingIt() {
            string dir = NewTempDir();
            using (ErrorLogging.UseIsolatedLogDirectory(dir)) {
                ErrorLogging.ClearLog();
                var tcs = new TaskCompletionSource();

                Async.Fire(tcs.Task, "test context");
                tcs.SetException(new InvalidOperationException("boom")); //ExecuteSynchronously runs the log write inline here

                string log = File.ReadAllText(ErrorLogging.LogFilePath);
                Assert.Contains("test context", log);
                Assert.Contains("InvalidOperationException", log);
                Assert.Contains("boom", log);
            }
        }

        [TestMethod]
        public void Fire_CompletedTask_LogsNothing() {
            string dir = NewTempDir();
            using (ErrorLogging.UseIsolatedLogDirectory(dir)) {
                ErrorLogging.ClearLog();
                var tcs = new TaskCompletionSource();

                Async.Fire(tcs.Task, "test context");
                tcs.SetResult();

                Assert.IsFalse(File.Exists(ErrorLogging.LogFilePath) && File.ReadAllText(ErrorLogging.LogFilePath).Length > 0);
            }
        }

        //A canceled fire-and-forget task used to vanish the same way a faulted one did (OnlyOnFaulted
        //excludes IsCanceled) - worth knowing about even without an exception to log.
        [TestMethod]
        public void Fire_CanceledTask_LogsCancellation() {
            string dir = NewTempDir();
            using (ErrorLogging.UseIsolatedLogDirectory(dir)) {
                ErrorLogging.ClearLog();
                var tcs = new TaskCompletionSource();

                Async.Fire(tcs.Task, "test context");
                tcs.SetCanceled(); //ExecuteSynchronously runs the log write inline here

                string log = File.ReadAllText(ErrorLogging.LogFilePath);
                Assert.Contains("test context", log);
                Assert.Contains("canceled", log);
            }
        }
    }
}
