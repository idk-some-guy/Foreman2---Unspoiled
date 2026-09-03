using Google.OrTools.LinearSolver;
using System.Collections.Generic;
using System.Text;

using Solver_t = Google.OrTools.LinearSolver.Solver;

namespace Foreman.Models.Solver {
    // A super thin wrapper around OrTools.LinearSolver to make up for its deficiences as a generated class.
    public class GoogleSolver : ILpSolver {
        private readonly Solver_t solver;
        private readonly Objective objective;
        private readonly List<Variable> variables;
        private readonly List<Constraint> constraints;

        public static GoogleSolver Create() {
            return new GoogleSolver();
        }

        public GoogleSolver() {
            solver = Solver_t.CreateSolver("GLOP");
            objective = solver.Objective();
            variables = [];
            constraints = [];
        }

        public override string ToString() {
            var desc = new StringBuilder();
            desc.AppendLine("== Constraints");

            foreach (var constraint in constraints) {
                var line = new List<string>();
                foreach (var variable in variables) {
                    var coefficient = constraint.GetCoefficient(variable);
                    if (coefficient != 0.0) {
                        line.Add(coefficient + " * " + variable.Name());
                    }
                }
                desc.AppendFormat(CultureInfo.InvariantCulture, "{0} → ({1}, {2})\n", string.Join(" + ", line), constraint.Lb(), constraint.Ub());
            }
            desc.AppendLine("");
            desc.AppendLine("");
            desc.AppendLine("== Variables");

            foreach (var variable in variables) {
                desc.AppendFormat(CultureInfo.InvariantCulture, "{0} = {1}\n", variable.Name(), variable.SolutionValue());
            }

            return desc.ToString();
        }

        public ILpVariable MakeNumVar(double low, double high, string name) {
            var variable = solver.MakeNumVar(low, high, name);
            variables.Add(variable);
            return new GoogleLpVariable(variable);
        }

        public ILpConstraint MakeConstraint(double low, double high) {
            var constraint = solver.MakeConstraint(low, high);
            constraints.Add(constraint);
            return new GoogleLpConstraint(constraint);
        }

        public void SetObjectiveCoefficient(ILpVariable variable, double coefficient) {
            objective.SetCoefficient(GoogleLpVariable.Unwrap(variable), coefficient);
        }

        public void SetMinimization() {
            objective.SetMinimization();
        }

        public LpSolveStatus Solve() {
            return solver.Solve() == Solver_t.ResultStatus.OPTIMAL ? LpSolveStatus.Optimal : LpSolveStatus.NotOptimal;
        }
    }

    // Adapters over OrTools' generated Variable/Constraint types, which can't implement our
    // interfaces directly.
    internal sealed class GoogleLpVariable(Variable variable) : ILpVariable {
        internal Variable Variable { get; } = variable;

        public double SolutionValue() => Variable.SolutionValue();

        internal static Variable Unwrap(ILpVariable variable) => ((GoogleLpVariable)variable).Variable;
    }

    internal sealed class GoogleLpConstraint(Constraint constraint) : ILpConstraint {
        public void SetCoefficient(ILpVariable variable, double coefficient) =>
            constraint.SetCoefficient(GoogleLpVariable.Unwrap(variable), coefficient);
    }
}
