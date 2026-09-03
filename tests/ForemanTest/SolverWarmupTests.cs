using Foreman.Models.Solver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class SolverWarmupTests {
        [TestMethod]
        public void RunAsync_RunsTheGivenWarmupOffTheCallingThread() {
            int callingThreadId = Environment.CurrentManagedThreadId;
            int warmupThreadId = -1;
            var completed = new ManualResetEventSlim(false);

            _ = SolverWarmup.RunAsync(() => {
                warmupThreadId = Environment.CurrentManagedThreadId;
                completed.Set();
            });

            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)), "warmup delegate never ran");
            Assert.AreNotEqual(callingThreadId, warmupThreadId);
        }

        [TestMethod]
        public async Task RunAsync_WarmupThrows_ExceptionIsSwallowedNotPropagated() {
            Task task = SolverWarmup.RunAsync(() => throw new InvalidOperationException("boom"));

            await task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(task.IsCompletedSuccessfully);
        }

        [TestMethod]
        public async Task RunAsync_DefaultOverload_WarmsTheRealSolverWithoutThrowing() {
            await SolverWarmup.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));

            // A cold solve still has to work after warmup - the warmup is a discard-the-result
            // throwaway, not a replacement for the real solver instance a solve creates.
            Assert.IsNotNull(GoogleSolver.Create());
        }
    }
}
