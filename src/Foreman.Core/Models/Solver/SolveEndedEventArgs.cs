using System;
using System.Collections.Generic;

namespace Foreman.Models.Solver {
    public enum SolveEndKind { Applied, Failed, Superseded, Cancelled }

    public sealed class SolveEndedEventArgs(SolveEndKind kind, IReadOnlyList<SolveFailure> failures) : EventArgs {
        public SolveEndKind Kind { get; } = kind;
        public IReadOnlyList<SolveFailure> Failures { get; } = failures;
    }
}
