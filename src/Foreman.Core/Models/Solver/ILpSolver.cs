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

    // The exact set of LP operations ProductionSolver needs: variables, constraints with bounds,
    // objective coefficients, solving, and reading back the status and solution values.
    public interface ILpSolver {
        ILpVariable MakeNumVar(double low, double high);
        ILpConstraint MakeConstraint(double low, double high);
        void SetObjectiveCoefficient(ILpVariable variable, double coefficient);
        void SetMinimization();
        LpSolveStatus Solve();
        LpSolveStatus Solve(CancellationToken ct) => Solve();
    }
}
