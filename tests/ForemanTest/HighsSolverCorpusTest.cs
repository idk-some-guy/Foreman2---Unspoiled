using Foreman.Models.Solver;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ForemanTest {
    // Runs the entire solver corpus (SolverTest, GraphOptionsSolverFieldsTest, SolverDefaultsTest)
    // against HighsSolver by inheriting each class's [TestMethod]s and overriding how the graph gets
    // solved. Every inherited case must hit the same expected values GLOP does, within the same
    // tolerance; nothing here duplicates a test body.
    [TestClass]
    public class SolverTest_Highs : SolverTest {
        protected override void SolveGraph(GraphBuilder.BuiltData data) =>
            SolverBackendHarness.Solve(data.Graph, () => new HighsSolver());
    }

    [TestClass]
    public class GraphOptionsSolverFieldsTest_Highs : GraphOptionsSolverFieldsTest {
        protected override void SolveGraph(GraphBuilder.BuiltData data) =>
            SolverBackendHarness.Solve(data.Graph, () => new HighsSolver());
    }

    [TestClass]
    public class SolverDefaultsTest_Highs : SolverDefaultsTest {
        protected override void SolveGraph(GraphBuilder.BuiltData data) =>
            SolverBackendHarness.Solve(data.Graph, () => new HighsSolver());
    }
}
