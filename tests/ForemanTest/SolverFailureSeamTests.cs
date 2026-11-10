using Foreman;
using Foreman.Models.Nodes;
using Foreman.Models.Solver;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ForemanTest {
    [TestClass]
    public class SolverFailureSeamTests : ForemanTestBase {
        private sealed class StubVariable : ILpVariable {
            public double SolutionValue() => 0;
        }

        private sealed class StubConstraint : ILpConstraint {
            public void SetCoefficient(ILpVariable variable, double coefficient) { }
        }

        private sealed class StubSolver(LpSolveStatus status) : ILpSolver {
            public ILpVariable MakeNumVar(double low, double high) => new StubVariable();
            public ILpConstraint MakeConstraint(double low, double high) => new StubConstraint();
            public void SetObjectiveCoefficient(ILpVariable variable, double coefficient) { }
            public void SetMinimization() { }
            public LpSolveStatus Solve() => status;
        }

        private static GraphBuilder.BuiltData BuildChain() {
            var builder = GraphBuilder.Create();
            builder.Link(
                builder.Supply("Ore"),
                builder.Recipe().Input("Ore", 1).Output("Plate", 1),
                builder.Consumer("Plate").Target(10)
            );
            return builder.Build();
        }

        private static void FailWith(GraphBuilder.BuiltData data, LpSolveStatus status, ConsumerNode? consumer = null) {
            (consumer ?? data.Graph.Nodes.OfType<ConsumerNode>().First()).DesiredRatePerSec = 20;
            data.Graph.LpSolverFactory = () => new StubSolver(status);
            data.Solve();
        }

        private static (GraphBuilder.BuiltData Data, BaseNode[] Dirtied, BaseNode[] Clean) BuildTwoChains() {
            var builder = GraphBuilder.Create();
            var dirtiedSupply = builder.Supply("Ore");
            var dirtiedRecipe = builder.Recipe().Input("Ore", 1).Output("Plate", 1);
            var dirtiedConsumer = builder.Consumer("Plate").Target(10);
            builder.Link(dirtiedSupply, dirtiedRecipe, dirtiedConsumer);
            var cleanSupply = builder.Supply("Coal");
            var cleanRecipe = builder.Recipe().Input("Coal", 1).Output("Gear", 1);
            var cleanConsumer = builder.Consumer("Gear").Target(10);
            builder.Link(cleanSupply, cleanRecipe, cleanConsumer);
            GraphBuilder.BuiltData data = builder.Build();
            return (data, [dirtiedSupply.BuiltNode, dirtiedRecipe.BuiltNode, dirtiedConsumer.BuiltNode], [cleanSupply.BuiltNode, cleanRecipe.BuiltNode, cleanConsumer.BuiltNode]);
        }

        [TestMethod]
        public void InfeasibleGroup_ReportsItsNodeIds_AndReasonInfeasible() {
            (GraphBuilder.BuiltData data, BaseNode[] dirtied, _) = BuildTwoChains();
            data.Solve();

            FailWith(data, LpSolveStatus.Infeasible, (ConsumerNode)dirtied[2]);

            SolveFailure failure = data.Graph.LastSolveFailures.Single();
            Assert.AreEqual(SolveFailureReason.Infeasible, failure.Reason);
            CollectionAssert.AreEquivalent(dirtied.Select(n => n.NodeID).ToList(), failure.NodeIds.ToList());
        }

        [TestMethod]
        public void FailedGroup_DoesNotTouchACleanGroup_AndTheFailedOneGoesStale() {
            (GraphBuilder.BuiltData data, BaseNode[] dirtied, BaseNode[] clean) = BuildTwoChains();
            data.Solve();
            double[] dirtiedRates = [.. dirtied.Select(n => n.ActualRatePerSec)];
            double[] cleanRates = [.. clean.Select(n => n.ActualRatePerSec)];
            Assert.IsTrue(cleanRates.All(r => r > 0));

            FailWith(data, LpSolveStatus.Infeasible, (ConsumerNode)dirtied[2]);

            Assert.IsTrue(clean.All(n => n.SolveState == NodeSolveState.Current));
            CollectionAssert.AreEqual(cleanRates, clean.Select(n => n.ActualRatePerSec).ToList());
            Assert.IsTrue(dirtied.All(n => n.SolveState == NodeSolveState.Stale));
            CollectionAssert.AreEqual(dirtiedRates, dirtied.Select(n => n.ActualRatePerSec).ToList());
        }

        [TestMethod]
        public void UnboundedGroup_ReportsReasonUnbounded() {
            GraphBuilder.BuiltData data = BuildChain();

            FailWith(data, LpSolveStatus.Unbounded);

            Assert.AreEqual(SolveFailureReason.Unbounded, data.Graph.LastSolveFailures.Single().Reason);
        }

        [TestMethod]
        public void NotOptimalGroup_ReportsReasonUnknown() {
            GraphBuilder.BuiltData data = BuildChain();

            FailWith(data, LpSolveStatus.NotOptimal);

            Assert.AreEqual(SolveFailureReason.Unknown, data.Graph.LastSolveFailures.Single().Reason);
        }

        [TestMethod]
        public void FailedGroup_KeepsLastGoodRatesAndThroughput_AndMarksStale() {
            GraphBuilder.BuiltData data = BuildChain();
            data.Solve();
            double[] rates = [.. data.Graph.Nodes.Select(n => n.ActualRatePerSec)];
            double[] throughputs = [.. data.Graph.Nodes.SelectMany(n => n.OutputLinks).Select(l => l.ThroughputPerSec)];
            Assert.IsTrue(rates.All(r => r > 0));
            Assert.IsTrue(data.Graph.Nodes.All(n => n.SolveState == NodeSolveState.Current));

            FailWith(data, LpSolveStatus.Infeasible);

            CollectionAssert.AreEqual(rates, data.Graph.Nodes.Select(n => n.ActualRatePerSec).ToList());
            CollectionAssert.AreEqual(throughputs, data.Graph.Nodes.SelectMany(n => n.OutputLinks).Select(l => l.ThroughputPerSec).ToList());
            Assert.IsTrue(data.Graph.Nodes.All(n => n.SolveState == NodeSolveState.Stale));
        }

        [TestMethod]
        public void NeverSolvedNode_StaysNeverSolved_OnFailure() {
            GraphBuilder.BuiltData data = BuildChain();
            Assert.IsTrue(data.Graph.Nodes.All(n => n.SolveState == NodeSolveState.NeverSolved));

            FailWith(data, LpSolveStatus.Infeasible);

            Assert.IsTrue(data.Graph.Nodes.All(n => n.SolveState == NodeSolveState.NeverSolved));
            Assert.IsTrue(data.Graph.Nodes.All(n => n.ActualRatePerSec == 0));
        }

        [TestMethod]
        public void SuccessfulSolve_MarksEveryNodeCurrent_AndClearsTheFailures() {
            GraphBuilder.BuiltData data = BuildChain();
            FailWith(data, LpSolveStatus.Infeasible);
            Assert.AreEqual(1, data.Graph.LastSolveFailures.Count);

            data.Graph.LpSolverFactory = HighsSolver.Create;
            data.Solve();

            Assert.AreEqual(0, data.Graph.LastSolveFailures.Count);
            Assert.IsTrue(data.Graph.Nodes.All(n => n.SolveState == NodeSolveState.Current));
        }

        [TestMethod]
        public void FailedGroup_StaysDirty_SoTheNextSolveRetriesIt() {
            GraphBuilder.BuiltData data = BuildChain();
            FailWith(data, LpSolveStatus.Infeasible);

            Assert.IsTrue(data.Graph.Nodes.All(n => !n.IsClean));
            data.Graph.LpSolverFactory = HighsSolver.Create;
            data.Solve();

            Assert.IsTrue(data.Graph.Nodes.All(n => n.IsClean));
        }

        [TestMethod]
        public void FailedGroup_RaisesNodeValuesChanged_SoViewsRedrawTheStaleRates() {
            GraphBuilder.BuiltData data = BuildChain();
            data.Solve();
            int raised = 0;
            foreach (BaseNode node in data.Graph.Nodes)
                node.NodeValuesChanged += (_, _) => raised++;

            FailWith(data, LpSolveStatus.Infeasible);

            Assert.AreEqual(data.Graph.Nodes.Count(), raised);
        }

        [TestMethod]
        public void SolveStartedAndEnded_BracketEveryOptimize_EvenWhenTheSolveFails() {
            GraphBuilder.BuiltData data = BuildChain();
            List<string> events = [];
            data.Graph.SolveStarted += (_, _) => events.Add("started");
            data.Graph.SolveEnded += (_, _) => events.Add(data.Graph.LastSolveFailures.Count == 0 ? "ended" : "ended-failed");

            data.Solve();
            FailWith(data, LpSolveStatus.Infeasible);

            CollectionAssert.AreEqual(new[] { "started", "ended", "started", "ended-failed" }, events);
        }

        [TestMethod]
        public void SolveEnded_StillRaises_WhenTheSolveThrows() {
            GraphBuilder.BuiltData data = BuildChain();
            data.Graph.Nodes.OfType<ConsumerNode>().First().DesiredRatePerSec = 20;
            data.Graph.LpSolverFactory = () => throw new OverflowException();
            int ended = 0;
            data.Graph.SolveEnded += (_, _) => ended++;

            Assert.ThrowsExactly<OverflowException>(data.Solve);

            Assert.AreEqual(1, ended);
        }
    }
}
