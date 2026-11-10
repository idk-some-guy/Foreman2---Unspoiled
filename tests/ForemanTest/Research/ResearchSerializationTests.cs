using Foreman;
using Foreman.Models;
using Foreman.Models.Research;
using Foreman.Serialization;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace ForemanTest.Research {
    [TestClass]
    public class ResearchSerializationTests : ForemanTestBase {
        private static ResearchState SyncedAndEdited() {
            ResearchSource source = new("Megabase", "/tmp/saves/Megabase.zip", 12345, new DateTime(2026, 11, 10, 14, 5, 0, DateTimeKind.Utc));
            ResearchState state = new(
                source,
                new Dictionary<ResearchField, double> { [ResearchField.MiningProductivity] = 1.2, [ResearchField.RobotSpeed] = 0.5 },
                new Dictionary<ResearchField, double> { [ResearchField.MiningProductivity] = 1.0 },
                new Dictionary<string, double> { ["steel-plate"] = 0.3 },
                new Dictionary<string, double> { ["steel-plate"] = 0.2 },
                [
                    new ResearchTechnology("mining-productivity-3", false, 2, 3, ResearchTechnology.Unlimited),
                    new ResearchTechnology("steel-processing", true, 1, 1, 1)
                ]);
            return state;
        }

        private static ProductionGraph LoadInto(ProductionGraph target, GraphBuilder.BuiltData data, string json, bool applySolverSettings = true) {
            target.InsertNodesFromFragment(data.Cache, json, applySolverSettings);
            return target;
        }

        [TestMethod]
        public void RoundTrip_SyncedAndHandEditedState_IsEqual() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            ResearchState original = SyncedAndEdited();
            data.Graph.Research = original;

            string json = GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false);
            ProductionGraph loaded = LoadInto(new ProductionGraph(), data, json);

            Assert.IsTrue(original.Equals(loaded.Research));
            Assert.AreEqual(DateTimeKind.Utc, loaded.Research.Source!.SyncedAtUtc.Kind);
            Assert.AreEqual(ResearchTechnology.Unlimited, loaded.Research.Technologies[0].MaxLevel);
        }

        [TestMethod]
        public void Load_GraphWithoutResearchBlock_HasNone() {
            string path = FlowchartSample.ResolvePath();
            GraphViewerSaveDocument? viewer = GraphSaveTestUi.ReadViewer(File.ReadAllText(path));
            Assert.IsNotNull(viewer);
            Assert.IsNull(viewer.ProductionGraph.Research);

            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            string json = GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false);
            ProductionGraph loaded = LoadInto(new ProductionGraph(), data, json);

            Assert.AreEqual(ResearchState.None, loaded.Research);
        }

        [TestMethod]
        public void Write_NoResearch_OmitsTheBlock() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();

            JsonObject json = JsonNode.Parse(GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false))!.AsObject();

            Assert.IsFalse(json.ContainsKey("Research"));
        }

        [TestMethod]
        public void Write_SourcelessState_LeavesOutSourceKeysAndNullTicks() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            data.Graph.Research = ResearchState.None.WithValue(ResearchField.LabSpeed, 0.4);

            JsonObject research = JsonNode.Parse(GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false))!["Research"]!.AsObject();

            Assert.IsFalse(research.ContainsKey("SaveName"));
            Assert.IsFalse(research.ContainsKey("SavePath"));
            Assert.IsFalse(research.ContainsKey("TicksPlayed"));
            Assert.IsFalse(research.ContainsKey("SyncedAtUtc"));
            Assert.AreEqual(0.4, (double)research["Values"]!["LabSpeed"]!);
        }

        [TestMethod]
        public void Paste_FragmentFromAnotherGraph_LeavesResearchUnchanged() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            ResearchState mine = ResearchState.None.WithValue(ResearchField.MiningProductivity, 0.2);
            data.Graph.Research = mine;
            string fragment = GraphSaveCodec.WriteProductionGraphToString(OtherGraphWith(data, SyncedAndEdited()), writeIndented: false);
            Assert.IsTrue(JsonNode.Parse(fragment)!.AsObject().ContainsKey("Research"));

            data.Graph.InsertNodesFromFragment(data.Cache, fragment, applySolverSettings: false);

            Assert.AreEqual(mine, data.Graph.Research);
        }

        [TestMethod]
        public void Duplicate_ByWriteThenLoad_CarriesResearch() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            data.Graph.Research = SyncedAndEdited();

            ProductionGraphSaveDocument? document = GraphSaveCodec.ReadProductionGraph(GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: true));
            Assert.IsNotNull(document);
            ProductionGraph copy = new();
            copy.InsertNodesFromDocument(data.Cache, document, applySolverSettings: true);

            Assert.AreEqual(data.Graph.Research, copy.Research);
        }

        [TestMethod]
        public void Load_UnknownFieldKey_IsIgnored() {
            GraphBuilder.BuiltData data = GraphSessionTestHelper.BuildSimpleChain();
            data.Graph.Research = SyncedAndEdited();
            JsonObject doc = JsonNode.Parse(GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false))!.AsObject();
            doc["Research"]!["Values"]!["FutureThing"] = 1;
            doc["Research"]!["SyncedValues"]!["AnotherFutureThing"] = 2;

            ProductionGraph loaded = LoadInto(new ProductionGraph(), data, doc.ToJsonString());

            Assert.AreEqual(data.Graph.Research, loaded.Research);
        }

        private static ProductionGraph OtherGraphWith(GraphBuilder.BuiltData data, ResearchState state) {
            ProductionGraph graph = new();
            graph.InsertNodesFromFragment(data.Cache, GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false), applySolverSettings: true);
            graph.Research = state;
            return graph;
        }
    }
}
