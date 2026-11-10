using Foreman.Graph;
using Foreman.Models.Research;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace ForemanTest.Research {
    [TestClass]
    public class ProductionGraphResearchTests : ForemanTestBase {
        private static ResearchState Mined(double bonus) => ResearchState.None.WithValue(ResearchField.MiningProductivity, bonus);

        [TestMethod]
        public void Research_Set_RaisesOneSessionMutation() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            ProductionGraphSession session = GraphSessionTestHelper.AttachSession(data.Graph);
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            data.Graph.Research = Mined(0.2);

            Assert.AreEqual(1, count);
            Assert.AreEqual(0.2, data.Graph.Research.MiningProductivity);
        }

        [TestMethod]
        public void Research_Set_RequestsExactlyOneSolve() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = SolveWiring.Wire(data.Graph);

            data.Graph.Research = Mined(0.2);

            CollectionAssert.AreEqual(new[] { "started" }, events);

            worker.RunAll();
            ctx.RunAll();

            CollectionAssert.AreEqual(new[] { "started", "ended:Applied" }, events);
        }

        [TestMethod]
        public void Research_SetEqualState_DoesNothing() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            data.Graph.Research = Mined(0.2);
            (ManualTaskScheduler worker, ManualSyncContext ctx, List<string> events) = SolveWiring.Wire(data.Graph);
            long version = data.Graph.Version;

            data.Graph.Research = Mined(0.2);

            Assert.AreEqual(version, data.Graph.Version);
            CollectionAssert.AreEqual(Array.Empty<string>(), events);
        }
    }
}
