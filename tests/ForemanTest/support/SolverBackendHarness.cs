using Foreman;
using Foreman.DataCaching;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Models.Solver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ForemanTest.support {
    // Re-implements GraphOptimisation.OptimizeGraphNodeValues (production, untouched by this task)
    // against an injected ILpSolver backend, so the same corpus fixtures can be run against a
    // non-default backend via the public GetConnectedNodeGroups seam instead of copy-pasting test
    // bodies. Task 5 owns the actual production wiring/default swap; this is test-only.
    internal static class SolverBackendHarness {
        internal static void Solve(ProductionGraph graph, Func<ILpSolver> lpSolverFactory) {
            foreach (var nodeGroup in graph.GetConnectedNodeGroups(false))
                SolveNodeGroup(graph, [.. nodeGroup], lpSolverFactory);
        }

        private static void SolveNodeGroup(ProductionGraph graph, List<BaseNode> nodeGroup, Func<ILpSolver> lpSolverFactory) {
            double minRatio = 0.1;
            foreach (RecipeNode node in nodeGroup.OfType<RecipeNode>())
                minRatio = Math.Min(minRatio, node.GetMinOutputRatio());

            var solver = new ProductionSolver(graph.PullOutputNodes, Math.Pow(10, graph.PullOutputNodesPower), minRatio, Math.Pow(10, Math.Min(graph.LowPriorityPower, 6)), lpSolverFactory());

            foreach (BaseNode node in nodeGroup)
                node.AddConstraints(solver);

            var solution = solver.Solve();

            if (solution == null)
                ErrorLogging.LogLine("Solver failed to find a solution for a connected node group");

            foreach (BaseNode node in nodeGroup) {
                node.SetSolvedRate(solution?.ActualRate(node) ?? 0);
                foreach (NodeLink link in node.OutputLinks)
                    link.ThroughputPerSec = solution?.Throughput(link) ?? 0;
            }
        }
    }
}
