//#define VERBOSEDEBUG

using Foreman.DataCaching;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Models.Solver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;


namespace Foreman {
    public partial class ProductionGraph {
        private int updateCounter;

        public IReadOnlyList<SolveFailure> LastSolveFailures { get; internal set; } = [];
        public event EventHandler? SolveStarted;
        public event EventHandler<SolveEndedEventArgs>? SolveEnded;

        public SynchronizationContext? SolveContext { get; set; }
        public GraphSolveScheduler Solver { get; }

        internal Func<ILpSolver> LpSolverFactory { get; set; } = () => HighsSolver.Create();

        internal void RaiseSolveStarted() => SolveStarted?.Invoke(this, EventArgs.Empty);
        internal void RaiseSolveEnded(SolveEndKind kind, IReadOnlyList<SolveFailure> failures) => SolveEnded?.Invoke(this, new SolveEndedEventArgs(kind, failures));

        public void SolveNow() => Solver.SolveNow();

        public void OptimizeGraphNodeValues() {
            SolveNow();
            Debug.WriteLine("UPDATE #" + updateCounter++);
        }

        internal List<HashSet<BaseNode>> DirtyComponents() => GetConnectedComponents(false);

        internal SolveGroup BuildSolveGroup(IEnumerable<BaseNode> nodeGroup) {
            List<BaseNode> nodes = [.. nodeGroup];
            double minRatio = 0.1;
            foreach (RecipeNode node in nodes.Where(n => n is RecipeNode))
                minRatio = Math.Min(minRatio, node.GetMinOutputRatio());

            var solver = new ProductionSolver(PullOutputNodes, Math.Pow(10, PullOutputNodesPower), minRatio, Math.Pow(10, Math.Min(LowPriorityPower, 6)), LpSolverFactory());

            foreach (BaseNode node in nodes)
                node.AddConstraints(solver);

            return new SolveGroup(solver, nodes);
        }

        internal static SolveFailure? ApplyGroupResult(SolveGroup group) {
            ProductionSolver solver = group.Solver;
            List<BaseNode> nodes = group.Nodes;
            var solution = group.Solved ? solver.BuildSolution() : null;

#if VERBOSEDEBUG
        Debug.WriteLine(solver.ToString());
#endif

            if (solution == null) {
                //Cyclic recipes with 'not enough provided' can lead to no-solution. Cyclic recipes with 'extra left' lead to an over-supply (solution found)
                //using the pulloutputnodes option can result in an unbound solution (also null).

                //ErrorLogging.LogLine(solver.ToString(CultureInfo.InvariantCulture));
                //Console.WriteLine(solver.ToString(CultureInfo.InvariantCulture));
                ErrorLogging.LogLine("Solver failed to find a solution for a connected node group");

                foreach (BaseNode node in nodes)
                    node.MarkSolveStale();
                return new SolveFailure([.. nodes.Select(n => n.NodeID)], !group.Solved ? SolveFailureReason.Unknown : solver.LastStatus switch {
                    LpSolveStatus.Infeasible => SolveFailureReason.Infeasible,
                    LpSolveStatus.Unbounded => SolveFailureReason.Unbounded,
                    _ => SolveFailureReason.Unknown,
                });
            }

            foreach (BaseNode node in nodes) {
                node.SolveState = NodeSolveState.Current;
                node.SetSolvedRate(solution.ActualRate(node));
                foreach (NodeLink link in node.OutputLinks)
                    link.ThroughputPerSec = solution.Throughput(link);
            }
            return null;
        }
    }

    internal sealed class SolveGroup(ProductionSolver solver, List<BaseNode> nodes) {
        public ProductionSolver Solver { get; } = solver;
        public List<BaseNode> Nodes { get; } = nodes;
        public bool Solved { get; set; }
    }

    // Using partial classes here to group all the constraints related code into this file so it's
    // easy to understand as a whole.
    public abstract partial class BaseNode {
        internal virtual void SetSolvedRate(double rate) {
            //this is for all nodes but the recipe node. IRecipe node overwrites this to set the factory count instead (as that is what the solver was solving for)
            ActualRatePerSec = rate;
            NodeValuesChanged?.Invoke(this, EventArgs.Empty);
            IsClean = true;
        }

        internal void AddConstraints(ProductionSolver solver) {
            if (this is RecipeNode rNode)
                solver.AddRecipeNode(rNode, rNode.FactoryRate()); //add node with minimization requirement on number of buildings
            else
                solver.AddNode(this); //add node without any minimization requirements

            if (this is ConsumerNode cNode && rateType == RateType.Auto)
                solver.AddOutputObjective(cNode); //pull up consumer node
            else if (RateType == RateType.Manual)
                solver.AddTarget(this, DesiredRatePerSec); //set manual requrement

            //add in the connections from the inputs of the node to any links connected to those inputs, grouped by item. There is no errors allowed here -> sum of link throughputs MUST equal the amount consumed.
            foreach (var itemInputs in InputLinks.GroupBy(x => x.Item)) {
                ItemQualityPair item = itemInputs.Key;
                solver.AddInputRatio(this, item, itemInputs, inputRateFor(item));
            }

            //add in the connections for the outputs of the node to any links connected to those outputs, grouped by item. Errors are only allowed for recipe nodes (too much produced -> accumulating in node), though it will be marked as 'overproducing'. All other nodes allow no errors (sum of link thorughputs MUST equal the amount produced)
            foreach (var itemOutputs in OutputLinks.GroupBy(x => x.Item)) {
                ItemQualityPair item = itemOutputs.Key;
                solver.AddOutputRatio(this, item, itemOutputs, outputRateFor(item));
            }

            //add in a forced 0 for passthrough nodes that have no inputs or no outputs (prevents such nodes from acting as 'free' inputs or outputs)
            if (this is PassthroughNode pNode && (InputLinks.Count == 0 || OutputLinks.Count == 0))
                solver.SetZero(pNode);
        }

        internal abstract double inputRateFor(ItemQualityPair item);
        internal abstract double outputRateFor(ItemQualityPair item);
    }
}
