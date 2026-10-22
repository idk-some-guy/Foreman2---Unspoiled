using Foreman.Models.Solver;

namespace Foreman.Mac.UiTests.support {
    internal sealed class FailingLpSolver(LpSolveStatus status = LpSolveStatus.Infeasible) : ILpSolver {
        private sealed class Variable : ILpVariable {
            public double SolutionValue() => 0;
        }

        private sealed class Constraint : ILpConstraint {
            public void SetCoefficient(ILpVariable variable, double coefficient) { }
        }

        public ILpVariable MakeNumVar(double low, double high) => new Variable();
        public ILpConstraint MakeConstraint(double low, double high) => new Constraint();
        public void SetObjectiveCoefficient(ILpVariable variable, double coefficient) { }
        public void SetMinimization() { }
        public LpSolveStatus Solve() => status;
    }
}
