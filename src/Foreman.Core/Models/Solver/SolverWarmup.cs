using System;
using System.Threading.Tasks;

namespace Foreman.Models.Solver {
    // Forces the OR-Tools native dylib tree to load on a background thread instead of on whatever
    // thread makes the first real solve call.
    public static class SolverWarmup {
        public static Task RunAsync() => RunAsync(() => GoogleSolver.Create());

        internal static Task RunAsync(Action warmup) =>
            Task.Run(() => {
                try {
                    warmup();
                } catch (Exception) {
                    // A failed warmup must stay invisible - the real solve still works cold.
                }
            });
    }
}
