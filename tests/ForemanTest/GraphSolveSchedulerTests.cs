using Foreman;
using Foreman.Models.Nodes;
using Foreman.Models.Solver;
using Foreman.Serialization;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace ForemanTest {
    [TestClass]
    public class GraphSolveSchedulerTests : ForemanTestBase {
        private sealed class StubVariable : ILpVariable {
            public double SolutionValue() => 0;
        }

        private sealed class StubConstraint : ILpConstraint {
            public void SetCoefficient(ILpVariable variable, double coefficient) { }
        }

        private sealed class ThrowingSolver : ILpSolver {
            public ILpVariable MakeNumVar(double low, double high) => new StubVariable();
            public ILpConstraint MakeConstraint(double low, double high) => new StubConstraint();
            public void SetObjectiveCoefficient(ILpVariable variable, double coefficient) { }
            public void SetMinimization() { }
            public LpSolveStatus Solve() => throw new InvalidOperationException("worker failure");
        }

        private static (GraphBuilder.BuiltData Data, BaseNode[] First, BaseNode[] Second) BuildTwoChains() {
            var builder = GraphBuilder.Create();
            var firstSupply = builder.Supply("Ore");
            var firstRecipe = builder.Recipe().Input("Ore", 1).Output("Plate", 1);
            var firstConsumer = builder.Consumer("Plate").Target(10);
            builder.Link(firstSupply, firstRecipe, firstConsumer);
            var secondSupply = builder.Supply("Coal");
            var secondRecipe = builder.Recipe().Input("Coal", 1).Output("Gear", 1);
            var secondConsumer = builder.Consumer("Gear").Target(10);
            builder.Link(secondSupply, secondRecipe, secondConsumer);
            return (builder.Build(), [firstSupply.BuiltNode, firstRecipe.BuiltNode, firstConsumer.BuiltNode], [secondSupply.BuiltNode, secondRecipe.BuiltNode, secondConsumer.BuiltNode]);
        }

        private static (ManualTaskScheduler Worker, ManualSyncContext Context, List<string> Events) Wire(GraphBuilder.BuiltData data) {
            var worker = new ManualTaskScheduler();
            var context = new ManualSyncContext();
            data.Graph.SolveContext = context;
            data.Graph.Solver.Worker = worker;
            List<string> events = [];
            data.Graph.SolveStarted += (_, _) => events.Add("started");
            data.Graph.SolveEnded += (_, e) => events.Add("ended:" + e.Kind);
            return (worker, context, events);
        }

        [TestMethod]
        public void Request_BuildsOnCaller_SolvesOnWorker_AppliesOnContext_InOrder() {
            (GraphBuilder.BuiltData data, _, _) = BuildTwoChains();
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = Wire(data);

            data.Graph.UpdateNodeValues();
            CollectionAssert.AreEqual(new[] { "started" }, events);
            Assert.IsTrue(data.Graph.Solver.IsSolving);
            Assert.AreEqual(NodeSolveState.NeverSolved, data.Graph.Nodes.First().SolveState);

            worker.RunAll();
            CollectionAssert.AreEqual(new[] { "started" }, events);
            Assert.AreEqual(NodeSolveState.NeverSolved, data.Graph.Nodes.First().SolveState);

            ctx.RunAll();
            CollectionAssert.AreEqual(new[] { "started", "ended:Applied" }, events);
            Assert.IsFalse(data.Graph.Solver.IsSolving);
            Assert.AreEqual(NodeSolveState.Current, data.Graph.Nodes.First().SolveState);
        }

        [TestMethod]
        public void EditDuringSolve_DropsTheStaleResult_AndRequestsAgain() {
            (GraphBuilder.BuiltData data, BaseNode[] first, _) = BuildTwoChains();
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = Wire(data);

            data.Graph.UpdateNodeValues();
            worker.RunAll();
            BaseNode added = data.Graph.CreateSupplierNode(((SupplierNode)first[0]).SuppliedItem, new Point(0, 0));
            ctx.RunAll();

            CollectionAssert.AreEqual(new[] { "started", "ended:Superseded", "started" }, events);
            Assert.AreNotEqual(NodeSolveState.Current, first[0].SolveState);

            worker.RunAll();
            ctx.RunAll();
            CollectionAssert.AreEqual(new[] { "started", "ended:Superseded", "started", "ended:Applied" }, events);
            Assert.AreEqual(NodeSolveState.Current, added.SolveState);
            Assert.AreEqual(NodeSolveState.Current, first[0].SolveState);
        }

        [TestMethod]
        public void Cancel_EndsCancelled_LeavesRatesAndStatesUntouched() {
            (GraphBuilder.BuiltData data, _, _) = BuildTwoChains();
            data.Solve();
            foreach (ConsumerNode consumer in data.Graph.Nodes.OfType<ConsumerNode>())
                consumer.DesiredRatePerSec = 20;
            double[] rates = [.. data.Graph.Nodes.Select(n => n.ActualRatePerSec)];
            NodeSolveState[] states = [.. data.Graph.Nodes.Select(n => n.SolveState)];
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = Wire(data);

            data.Graph.UpdateNodeValues();
            data.Graph.Solver.Cancel();
            ctx.RunAll();
            CollectionAssert.AreEqual(new[] { "started", "ended:Cancelled" }, events);
            Assert.IsFalse(data.Graph.Solver.IsSolving);

            worker.RunAll();
            ctx.RunAll();
            CollectionAssert.AreEqual(new[] { "started", "ended:Cancelled" }, events);
            CollectionAssert.AreEqual(rates, data.Graph.Nodes.Select(n => n.ActualRatePerSec).ToList());
            CollectionAssert.AreEqual(states, data.Graph.Nodes.Select(n => n.SolveState).ToList());
        }

        [TestMethod]
        public void EveryMutation_BumpsVersion() {
            (GraphBuilder.BuiltData data, BaseNode[] first, _) = BuildTwoChains();
            ProductionGraph graph = data.Graph;
            var supply = (SupplierNode)first[0];
            SupplierNode? newSupply = null;
            ConsumerNode? newConsumer = null;
            NodeLink? link = null;
            var document = GraphSaveCodec.BuildProductionGraph(graph);

            (string Name, Action Act)[] mutations = [
                ("CreateSupplierNode", () => newSupply = graph.CreateSupplierNode(supply.SuppliedItem, new Point(1, 1))),
                ("CreateConsumerNode", () => newConsumer = graph.CreateConsumerNode(supply.SuppliedItem, new Point(1, 1))),
                ("CreatePassthroughNode", () => graph.CreatePassthroughNode(supply.SuppliedItem, new Point(1, 1))),
                ("CreateLink", () => link = graph.CreateLink(newSupply!, newConsumer!, supply.SuppliedItem)),
                ("DeleteLink", () => graph.DeleteLink(link!)),
                ("DeleteNode", () => graph.DeleteNode(newConsumer!)),
                ("NodeMutated", () => graph.NotifyNodeMutated()),
                ("CreateRecipeNode", () => graph.CreateRecipeNode(((RecipeNode)first[1]).BaseRecipe, new Point(1, 1))),
                ("UpdateNodeStates", () => graph.UpdateNodeStates(true)),
                ("UpdateNodeMaxQualities", graph.UpdateNodeMaxQualities),
                ("InsertNodesFromDocument", () => graph.InsertNodesFromDocument(data.Cache, document, applySolverSettings: false)),
                ("InsertNodesFromFragment", () => graph.InsertNodesFromFragment(data.Cache, "not json", applySolverSettings: false)),
                ("SelectedRateUnit", () => graph.SelectedRateUnit = ProductionGraph.RateUnit.Per1Hour),
                ("PullOutputNodes", () => graph.PullOutputNodes = !graph.PullOutputNodes),
                ("PullOutputNodesPower", () => graph.PullOutputNodesPower += 1),
                ("LowPriorityPower", () => graph.LowPriorityPower += 1),
                ("ClearGraph", graph.ClearGraph),
            ];

            foreach ((string name, Action act) in mutations) {
                long before = graph.Version;
                act();
                Assert.IsGreaterThan(before, graph.Version, name);
            }
        }

        [TestMethod]
        public void SolveNow_WithNoContext_IsSynchronous_AndUpdateNodeValuesUsesIt() {
            (GraphBuilder.BuiltData data, _, _) = BuildTwoChains();
            List<string> events = [];
            data.Graph.SolveStarted += (_, _) => events.Add("started");
            data.Graph.SolveEnded += (_, e) => events.Add("ended:" + e.Kind);
            data.Graph.NodeValuesUpdated += (_, _) => events.Add("values");

            Assert.IsNull(data.Graph.SolveContext);
            data.Graph.UpdateNodeValues();

            CollectionAssert.AreEqual(new[] { "started", "ended:Applied", "values" }, events);
            Assert.IsFalse(data.Graph.Solver.IsSolving);
            Assert.AreEqual(0, data.Graph.LastSolveFailures.Count);
            Assert.IsTrue(data.Graph.Nodes.All(n => n.SolveState == NodeSolveState.Current));
        }

        [TestMethod]
        public void WorkerThrow_EndsFailed_WithUnknownForUnsolvedGroups_AndKeepsLastGoodRates() {
            (GraphBuilder.BuiltData data, BaseNode[] first, BaseNode[] second) = BuildTwoChains();
            data.Solve();
            foreach (ConsumerNode consumer in data.Graph.Nodes.OfType<ConsumerNode>())
                consumer.DesiredRatePerSec = 20;
            double[] secondRates = [.. second.Select(n => n.ActualRatePerSec)];
            double[] firstRates = [.. first.Select(n => n.ActualRatePerSec)];
            int created = 0;
            data.Graph.LpSolverFactory = () => ++created == 2 ? new ThrowingSolver() : HighsSolver.Create();
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = Wire(data);

            data.Graph.UpdateNodeValues();
            worker.RunAll();
            ctx.RunAll();

            CollectionAssert.AreEqual(new[] { "started", "ended:Failed" }, events);
            SolveFailure failure = data.Graph.LastSolveFailures.Single();
            Assert.AreEqual(SolveFailureReason.Unknown, failure.Reason);
            BaseNode[] failed = first.All(n => failure.NodeIds.Contains(n.NodeID)) ? first : second;
            BaseNode[] solved = ReferenceEquals(failed, first) ? second : first;
            Assert.IsTrue(solved.All(n => n.SolveState == NodeSolveState.Current));
            Assert.IsTrue(failed.All(n => n.SolveState == NodeSolveState.Stale));
            double[] failedRates = ReferenceEquals(failed, first) ? firstRates : secondRates;
            CollectionAssert.AreEqual(failedRates, failed.Select(n => n.ActualRatePerSec).ToList());
        }

        [TestMethod]
        public void UpdateNodeStates_BumpsVersionOnlyWhenMarkingAllDirty() {
            (GraphBuilder.BuiltData data, _, _) = BuildTwoChains();
            long before = data.Graph.Version;
            data.Graph.UpdateNodeStates(false);
            Assert.AreEqual(before, data.Graph.Version);
            data.Graph.UpdateNodeStates(true);
            Assert.IsGreaterThan(before, data.Graph.Version);
        }

        [TestMethod]
        public void Cancel_OnTheContextThread_EndsSynchronously_BeforeTheNextStart() {
            (GraphBuilder.BuiltData data, _, _) = BuildTwoChains();
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = Wire(data);
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new ManualSyncContext());
            try {
                data.Graph.UpdateNodeValues();
                data.Graph.Solver.Cancel();
                CollectionAssert.AreEqual(new[] { "started", "ended:Cancelled" }, events);
                Assert.IsFalse(data.Graph.Solver.IsSolving);

                data.Graph.UpdateNodeValues();
                CollectionAssert.AreEqual(new[] { "started", "ended:Cancelled", "started" }, events);
                Assert.IsTrue(data.Graph.Solver.IsSolving);

                worker.RunAll();
                ctx.RunAll();
                CollectionAssert.AreEqual(new[] { "started", "ended:Cancelled", "started", "ended:Applied" }, events);
                Assert.IsFalse(data.Graph.Solver.IsSolving);
                Assert.AreEqual(4, events.Count);
            } finally {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [TestMethod]
        public void ApplyThrow_EndsFailed_AndNothingEscapesTheContext() {
            (GraphBuilder.BuiltData data, _, _) = BuildTwoChains();
            data.Solve();
            foreach (ConsumerNode consumer in data.Graph.Nodes.OfType<ConsumerNode>())
                consumer.DesiredRatePerSec = 20;
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = Wire(data);
            var throwing = data.Graph.Nodes.OfType<ConsumerNode>().First();
            throwing.NodeValuesChanged += (_, _) => throw new OverflowException("apply");
            SolveEndedEventArgs? ended = null;
            data.Graph.SolveEnded += (_, e) => ended = e;

            data.Graph.UpdateNodeValues();
            worker.RunAll();
            ctx.RunAll();
            CollectionAssert.AreEqual(data.Graph.LastSolveFailures.ToList(), ended!.Failures.ToList());

            CollectionAssert.AreEqual(new[] { "started", "ended:Failed" }, events);
            Assert.IsFalse(data.Graph.Solver.IsSolving);
            Assert.IsNotEmpty(data.Graph.LastSolveFailures);
            var unknown = data.Graph.LastSolveFailures.Where(f => f.Reason == SolveFailureReason.Unknown).ToList();
            Assert.IsNotEmpty(unknown);
            Assert.IsTrue(unknown.Any(f => f.NodeIds.Contains(throwing.NodeID)));
            var listed = unknown.SelectMany(f => f.NodeIds).ToHashSet();
            Assert.IsTrue(data.Graph.Nodes.All(n => n.SolveState == NodeSolveState.Current || (n.SolveState == NodeSolveState.Stale && listed.Contains(n.NodeID))));
        }

        [TestMethod]
        public void NewRequestWhileInFlight_EndsTheOldRunSuperseded_ThenSolveNowRunsToApplied() {
            (GraphBuilder.BuiltData data, _, _) = BuildTwoChains();
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = Wire(data);

            data.Graph.UpdateNodeValues();
            data.Graph.UpdateNodeValues();
            CollectionAssert.AreEqual(new[] { "started", "ended:Superseded", "started" }, events);
            Assert.IsTrue(data.Graph.Solver.IsSolving);

            worker.RunAll();
            ctx.RunAll();
            CollectionAssert.AreEqual(new[] { "started", "ended:Superseded", "started", "ended:Applied" }, events);
            Assert.IsTrue(data.Graph.Nodes.All(n => n.SolveState == NodeSolveState.Current));

            events.Clear();
            data.Graph.UpdateNodeValues();
            data.Graph.Solver.SolveNow();
            CollectionAssert.AreEqual(new[] { "started", "ended:Superseded", "started", "ended:Applied" }, events);
            Assert.IsFalse(data.Graph.Solver.IsSolving);

            worker.RunAll();
            ctx.RunAll();
            Assert.AreEqual(4, events.Count);
        }
    }
}
