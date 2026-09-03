using Foreman.Models.Solver;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ForemanTest {
    // HiGHS writes its solve banner/iteration table straight to the process's stdout file
    // descriptor via native C stdio, bypassing Console.SetOut entirely - so verifying silence needs
    // real fd-level redirection, not the managed Console API. At scale (a large graph decomposes
    // into hundreds of independent connected components, each solved separately) an unsilenced
    // solver means hundreds of banners per pass, not just console noise.
    [TestClass]
    public class HighsSolverTest {
        [DllImport("libc", SetLastError = true)]
        private static extern int dup(int fd);

        [DllImport("libc", SetLastError = true)]
        private static extern int dup2(int fd, int fd2);

        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [TestMethod]
        public void Solve_DoesNotWriteToStdout() {
            var solver = new HighsSolver();
            ILpVariable x = solver.MakeNumVar(0, double.PositiveInfinity, "x");
            ILpConstraint constraint = solver.MakeConstraint(10, 10);
            constraint.SetCoefficient(x, 1);
            solver.SetObjectiveCoefficient(x, 1);
            solver.SetMinimization();

            string capturePath = Path.Combine(Path.GetTempPath(), $"highs-stdout-{Guid.NewGuid():N}.txt");
            LpSolveStatus status;
            string capturedOutput;
            int savedStdout = dup(1);
            try {
                using (var captureFile = new FileStream(capturePath, FileMode.Create, FileAccess.Write)) {
                    Console.Out.Flush();
                    _ = dup2((int)captureFile.SafeFileHandle.DangerousGetHandle(), 1);
                    status = solver.Solve();
                    Console.Out.Flush();
                }
                _ = dup2(savedStdout, 1);
                capturedOutput = File.ReadAllText(capturePath);
            } finally {
                _ = close(savedStdout);
                File.Delete(capturePath);
            }

            Assert.AreEqual(LpSolveStatus.Optimal, status);
            Assert.AreEqual(string.Empty, capturedOutput, "HiGHS must not print its solve banner/log to stdout by default.");
        }
    }
}
