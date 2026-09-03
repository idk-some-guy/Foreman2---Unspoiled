using System;
using System.Threading.Tasks;

namespace Foreman.Models.Solver {
    // Forces HiGHS's native dylib to load on a background thread instead of on whatever thread
    // makes the first real solve call. HiGHS's cold-load is far cheaper than OR-Tools' was, but
    // still measures 84-140ms fresh-process (docs/solver-eval.md), above the threshold worth
    // warming off the UI thread.
    public static class SolverWarmup {
        public static Task RunAsync() => RunAsync(() => HighsSolver.Create());

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
