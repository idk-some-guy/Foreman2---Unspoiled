using System.Collections.Generic;

namespace Foreman.Models.Solver {
    public enum SolveFailureReason { Unknown, Infeasible, Unbounded }

    public sealed record SolveFailure(IReadOnlyList<int> NodeIds, SolveFailureReason Reason);
}
