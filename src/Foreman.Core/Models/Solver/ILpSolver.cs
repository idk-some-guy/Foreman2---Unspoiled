using System.Threading;

namespace Foreman.Models.Solver {
    public enum LpSolveStatus {
        Optimal,
        NotOptimal,
        Infeasible,
        Unbounded,
    }

    public interface ILpVariable {
        double SolutionValue();
    }

    public interface ILpConstraint {
        void SetCoefficient(ILpVariable variable, double coefficient);
    }

    public interface ILpSolver {
        ILpVariable MakeNumVar(double low, double high);
        ILpConstraint MakeConstraint(double low, double high);
        void SetObjectiveCoefficient(ILpVariable variable, double coefficient);
        void SetMinimization();
        LpSolveStatus Solve();
        LpSolveStatus Solve(CancellationToken ct) => Solve();
    }
}
