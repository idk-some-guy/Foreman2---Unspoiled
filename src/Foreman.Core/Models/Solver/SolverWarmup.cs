using System;
using System.Threading.Tasks;

namespace Foreman.Models.Solver {
    // Forces HiGHS's native dylib to load on a background thread instead of on whatever thread
    // makes the first real solve call. HiGHS's cold-load is far cheaper than OR-Tools' was, but
    // still measures 84-140ms fresh-process, above the threshold worth
    // warming off the UI thread. HighsSolver.Create() alone is pure managed allocation - the
    // dylib only loads once Solve() constructs a HighsLpSolver, so warmup has to actually solve.
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
