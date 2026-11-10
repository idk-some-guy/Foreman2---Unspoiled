using System;
using System.Threading.Tasks;

namespace Foreman.Models.Solver {
    public static class SolverWarmup {
        public static Task RunAsync() => RunAsync(() => WarmupSolve());

        internal static Task RunAsync(Action warmup) =>
            Task.Run(() => {
                try {
                    warmup();
                } catch (Exception) {
                    // A failed warmup must stay invisible - the real solve still works cold.
                }
            });

        internal static LpSolveStatus WarmupSolve() {
            var solver = HighsSolver.Create();
            ILpVariable variable = solver.MakeNumVar(0.0, 1.0);
            ILpConstraint constraint = solver.MakeConstraint(1.0, 1.0);
            constraint.SetCoefficient(variable, 1.0);
            solver.SetMinimization();
            return solver.Solve();
        }
    }
}
