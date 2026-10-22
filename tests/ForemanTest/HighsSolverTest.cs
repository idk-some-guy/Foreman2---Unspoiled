using ForemanTest.support;
using Foreman.Models.Solver;
using Highs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Reflection;
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
            ILpVariable x = solver.MakeNumVar(0, double.PositiveInfinity);
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

        [TestMethod]
        [DataRow(HighsStatus.kOk, HighsModelStatus.kOptimal, true)]
        [DataRow(HighsStatus.kWarning, HighsModelStatus.kOptimal, true)]
        [DataRow(HighsStatus.kError, HighsModelStatus.kOptimal, false)]
        [DataRow(HighsStatus.kOk, HighsModelStatus.kInfeasible, false)]
        public void IsUsableSolution_AcceptsKWarningAlongsideKOk_RejectsOnlyKErrorOrNonOptimalModel(
                HighsStatus runStatus, HighsModelStatus modelStatus, bool expected) {
            // kWarning can still carry an optimal solution (e.g. postsolve residuals) - discarding
            // it would zero out a whole connected component for no reason. Only kError means there
            // is nothing to read back.
            Assert.AreEqual(expected, HighsSolver.IsUsableSolution(runStatus, modelStatus));
        }

        [TestMethod]
        [DataRow(HighsStatus.kOk, HighsModelStatus.kOptimal, LpSolveStatus.Optimal)]
        [DataRow(HighsStatus.kWarning, HighsModelStatus.kOptimal, LpSolveStatus.Optimal)]
        [DataRow(HighsStatus.kOk, HighsModelStatus.kInfeasible, LpSolveStatus.Infeasible)]
        [DataRow(HighsStatus.kOk, HighsModelStatus.kUnbounded, LpSolveStatus.Unbounded)]
        [DataRow(HighsStatus.kOk, HighsModelStatus.kUnboundedOrInfeasible, LpSolveStatus.NotOptimal)]
        [DataRow(HighsStatus.kOk, HighsModelStatus.kIterationLimit, LpSolveStatus.NotOptimal)]
        [DataRow(HighsStatus.kError, HighsModelStatus.kInfeasible, LpSolveStatus.NotOptimal)]
        public void StatusFor_MapsInfeasibleAndUnbounded_AndEverythingElseToNotOptimal(
                HighsStatus runStatus, HighsModelStatus modelStatus, LpSolveStatus expected) {
            Assert.AreEqual(expected, HighsSolver.StatusFor(runStatus, modelStatus));
        }

        [TestMethod]
        public void Solve_ContradictoryConstraint_ReportsInfeasible() {
            var solver = new HighsSolver();
            ILpVariable x = solver.MakeNumVar(0, double.PositiveInfinity);
            ILpConstraint constraint = solver.MakeConstraint(-1, -1);
            constraint.SetCoefficient(x, 1);
            solver.SetMinimization();

            Assert.AreEqual(LpSolveStatus.Infeasible, solver.Solve());
        }

        [TestMethod]
        public void SolutionValueFor_ColumnBeyondTheSolutionArray_ReturnsZeroInsteadOfThrowing() {
            var solver = new HighsSolver();
            FieldInfo solutionField = ReflectionTestHelper.RequireField(typeof(HighsSolver), "solution", BindingFlags.NonPublic | BindingFlags.Instance);
            solutionField.SetValue(solver, new HighsSolution(numcol: 1, numrow: 0));

            Assert.AreEqual(0.0, solver.SolutionValueFor(5));
        }
    }
}
