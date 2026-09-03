using Foreman;
using Foreman.Models.Nodes;
using Foreman.Models.Solver;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace ForemanTest {
    // Proves the constructor-injected ILpSolver seam works end to end: ProductionSolver built with an
    // explicit HiGHS-backed solver must match SolverTest.cs's simplest case (TestBasicSolve).
    [TestClass]
    public class ProductionSolverSeamTest : ForemanTestBase {
        [TestMethod]
        public void ExplicitHighsBackedSolver_MatchesBasicSolve() {
            var builder = GraphBuilder.Create();

            builder.Link(
                builder.Supply("Plate"),
                builder.Consumer("Plate").Target(10)
            );

            var data = builder.Build();

            var solver = new ProductionSolver(pullOutputNodes: false, pullPower: 0, minRecipeOutRate: 1e-2, lowPriorityMultiplier: 1, lpSolver: new HighsSolver());

            foreach (BaseNode node in data.Graph.Nodes)
                node.AddConstraints(solver);

            var solution = solver.Solve();

            Assert.IsNotNull(solution);
            var supplier = data.Graph.Nodes.OfType<SupplierNode>().First();
            Assert.AreEqual(10, solution!.ActualRate(supplier), 0.0001);
        }
    }
}
