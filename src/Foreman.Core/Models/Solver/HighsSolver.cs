using Highs;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Models.Solver {
    // ILpSolver calls arrive one variable/constraint/coefficient at a time, but Highs.Native's
    // HighsLpSolver only takes a whole HighsModel via passLp. We accumulate the model here and
    // materialize it at Solve() time.
    public class HighsSolver : ILpSolver {
        // HiGHS rejects objective coefficients >= 1e20 outright (solve-time kError/kUnknown, no
        // solution) and warns above 1e10; see docs/solver-eval.md §e. The app's own ceiling is 1e6,
        // so this only guards a caller bypassing that ceiling.
        private const double MaxObjectiveCoefficient = 1e9;

        private readonly List<(double Low, double High)> columns = [];
        private readonly List<double> objective = [];
        private readonly List<(double Low, double High, Dictionary<int, double> Coefficients)> rows = [];
        private HighsObjectiveSense sense = HighsObjectiveSense.kMinimize;
        private HighsSolution? solution;

        public static HighsSolver Create() => new();

        public ILpVariable MakeNumVar(double low, double high, string name) {
            int index = columns.Count;
            columns.Add((low, high));
            objective.Add(0.0);
            return new HighsLpVariable(this, index);
        }

        public ILpConstraint MakeConstraint(double low, double high) {
            int index = rows.Count;
            rows.Add((low, high, []));
            return new HighsLpConstraint(this, index);
        }

        public void SetObjectiveCoefficient(ILpVariable variable, double coefficient) =>
            objective[HighsLpVariable.Unwrap(variable)] = Clamp(coefficient);

        public void SetMinimization() => sense = HighsObjectiveSense.kMinimize;

        public LpSolveStatus Solve() {
            using var solver = new HighsLpSolver();
            _ = solver.setBoolOptionValue("output_flag", 0);
            _ = solver.passLp(BuildModel());
            var runStatus = solver.run();
            var modelStatus = solver.GetModelStatus();

            if (runStatus != HighsStatus.kOk || modelStatus != HighsModelStatus.kOptimal)
                return LpSolveStatus.NotOptimal;

            solution = solver.getSolution();
            return LpSolveStatus.Optimal;
        }

        internal double SolutionValueFor(int column) => solution?.colvalue[column] ?? 0.0;

        internal void SetCoefficient(int row, int column, double coefficient) => rows[row].Coefficients[column] = coefficient;

        private static double Clamp(double coefficient) => coefficient switch {
            >= MaxObjectiveCoefficient => MaxObjectiveCoefficient,
            <= -MaxObjectiveCoefficient => -MaxObjectiveCoefficient,
            _ => coefficient,
        };

        private HighsModel BuildModel() {
            List<int> astart = [];
            List<int> aindex = [];
            List<double> avalue = [];

            foreach (var row in rows) {
                astart.Add(aindex.Count);
                foreach (var (column, coefficient) in row.Coefficients) {
                    if (coefficient == 0.0)
                        continue;
                    aindex.Add(column);
                    avalue.Add(coefficient);
                }
            }
            astart.Add(aindex.Count);

            return new HighsModel {
                sense = sense,
                colcost = [.. objective],
                offset = 0.0,
                collower = [.. columns.Select(c => c.Low)],
                colupper = [.. columns.Select(c => c.High)],
                rowlower = [.. rows.Select(r => r.Low)],
                rowupper = [.. rows.Select(r => r.High)],
                a_format = HighsMatrixFormat.kRowwise,
                astart = [.. astart],
                aindex = [.. aindex],
                avalue = [.. avalue],
            };
        }
    }

    // Adapters over Highs.Native's plain-data model: HighsSolver only exposes column/row indices
    // internally, so these wrap the index and dispatch back through it.
    internal sealed class HighsLpVariable(HighsSolver solver, int index) : ILpVariable {
        internal int Index { get; } = index;

        public double SolutionValue() => solver.SolutionValueFor(Index);

        internal static int Unwrap(ILpVariable variable) => ((HighsLpVariable)variable).Index;
    }

    internal sealed class HighsLpConstraint(HighsSolver solver, int index) : ILpConstraint {
        public void SetCoefficient(ILpVariable variable, double coefficient) =>
            solver.SetCoefficient(index, HighsLpVariable.Unwrap(variable), coefficient);
    }
}
