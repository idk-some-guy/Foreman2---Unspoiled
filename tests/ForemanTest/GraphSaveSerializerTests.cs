using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Models.Nodes;
using Foreman.Serialization;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class GraphSaveCodecTests : ForemanTestBase {
        public TestContext? TestContext { get; set; }
        [TestMethod]
        public void SerializeProductionGraph_ProducesExpectedDocumentShape() {
            var data = BuildSimpleChain();
            JsonElement json = JsonDocument.Parse(
                GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false)).RootElement;

            Assert.AreEqual(GraphSaveFormat.SaveFormatVersion, json.GetProperty("Version").GetInt32());
            Assert.AreEqual(GraphSaveFormat.GraphObject, json.GetProperty("Object").GetString());
            Assert.AreEqual(JsonValueKind.Array, json.GetProperty("Nodes").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, json.GetProperty("NodeLinks").ValueKind);
            Assert.AreEqual(JsonValueKind.Array, json.GetProperty("IncludedItems").ValueKind);
            Assert.IsGreaterThanOrEqualTo(2, json.GetProperty("IncludedItems").GetArrayLength());
        }

        [TestMethod]
        public void GraphSaveCodec_BuildProductionGraph_MatchesJsonRoundTrip() {
            var data = BuildSimpleChain();
            ProductionGraphSaveDocument built = GraphSaveCodec.BuildProductionGraph(data.Graph);
            ProductionGraphSaveDocument? fromJson = GraphSaveCodec.ReadProductionGraph(
                GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false));

            Assert.IsNotNull(fromJson);
            Assert.HasCount(built.Nodes.Count, fromJson.Nodes);
            Assert.HasCount(built.Links.Count, fromJson.Links);
            Assert.HasCount(built.IncludedItems.Count, fromJson.IncludedItems);
        }

        [TestMethod]
        public void GraphSaveCodec_ReadProductionGraph_MatchesSerializedChain() {
            var data = BuildSimpleChain();
            string json = GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false);

            ProductionGraphSaveDocument? document = GraphSaveCodec.ReadProductionGraph(json);

            Assert.IsNotNull(document);
            Assert.HasCount(3, document.Nodes);
            Assert.HasCount(2, document.Links);
            Assert.Contains(n => n is RecipeNodeSaveData, document.Nodes);
            Assert.Contains(n => n is SupplierNodeSaveData, document.Nodes);
            Assert.Contains(n => n is ConsumerNodeSaveData, document.Nodes);
            Assert.IsNotNull(document.Solver);
            Assert.Contains("Ore", document.IncludedItems);
            Assert.Contains("Plate", document.IncludedItems);
        }

        [TestMethod]
        public void GraphSaveCodec_ReadProductionGraph_InvalidObject_ReturnsNull() {
            var data = BuildSimpleChain();
            var parsed = JsonNode.Parse(GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false));
            Assert.IsNotNull(parsed);
            JsonNode json = parsed;
            json["Object"] = "NotAProductionGraph";

            Assert.IsNull(GraphSaveCodec.ReadProductionGraph(json.ToJsonString()));
        }

        [TestMethod]
        public void GraphSaveCodec_Annotations_RoundTripThroughViewerDocument() {
            var data = BuildSimpleChain();
            var annotations = new List<AnnotationSaveData> {
                new TextAnnotationSaveData {
                    X = 100,
                    Y = 200,
                    Width = 150,
                    Height = 40,
                    Text = "Hello",
                    FontFamily = "Segoe UI",
                    FontSize = 14f,
                    TextColor = new ColorSaveData(255, 0, 0, 0),
                    BackColor = new ColorSaveData(0, 255, 255, 255),
                    TextAlign = 1
                },
                new ShapeAnnotationSaveData {
                    X = 50,
                    Y = 60,
                    Width = 200,
                    Height = 100,
                    ShapeType = "Ellipse",
                    FillColor = new ColorSaveData(80, 80, 160, 255),
                    BorderColor = new ColorSaveData(255, 60, 120, 220),
                    BorderWidth = 2
                }
            };
            GraphViewerSaveDocument original = new() {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(data.Graph),
                Annotations = annotations,
                AnnotationDpi = 120
            };

            string json = GraphSaveCodec.WriteViewerDocumentToString(original, writeIndented: false);
            GraphViewerSaveDocument? restored = GraphSaveCodec.ReadViewer(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(120, restored.AnnotationDpi);
            Assert.HasCount(2, restored.Annotations);

            var text = restored.Annotations.OfType<TextAnnotationSaveData>().Single();
            Assert.AreEqual("Hello", text.Text);
            Assert.AreEqual(100, text.X);

            var shape = restored.Annotations.OfType<ShapeAnnotationSaveData>().Single();
            Assert.AreEqual("Ellipse", shape.ShapeType);
            Assert.AreEqual(2, shape.BorderWidth);
        }

        [TestMethod]
        public void GraphSaveCodec_ReadGraphPayload_AcceptsViewerSaveFile() {
            var data = BuildSimpleChain();
            GraphViewerSaveDocument viewerDoc = new() {
                Version = GraphSaveFormat.SaveFormatVersion,
                SavedPresetName = data.Cache.PresetName,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(data.Graph)
            };
            string json = GraphSaveCodec.WriteViewerDocumentToString(viewerDoc, writeIndented: false);

            ProductionGraphSaveDocument? payload = GraphSaveCodec.ReadGraphPayload(json);
            Assert.IsNotNull(payload);
            Assert.HasCount(3, payload.Nodes);
        }

        [TestMethod]
        public void GraphSaveLoader_LoadFromDocument_MatchesInsertNodesFromFragment() {
            var data = BuildSimpleChain();
            ProductionGraphSaveDocument document = GraphSaveCodec.BuildProductionGraph(data.Graph);

            foreach (var node in data.Graph.Nodes.ToList())
                data.Graph.DeleteNode(node);

            var viaDocument = data.Graph.InsertNodesFromDocument(data.Cache, document, applySolverSettings: true);
            Assert.HasCount(3, viaDocument.NewNodes);
            Assert.HasCount(2, viaDocument.NewLinks);

            foreach (var node in data.Graph.Nodes.ToList())
                data.Graph.DeleteNode(node);

            string fragmentJson = GraphSaveCodec.WriteProductionGraphDocumentToString(document, writeIndented: false);
            var viaFragment = data.Graph.InsertNodesFromFragment(data.Cache, fragmentJson, applySolverSettings: true);
            Assert.HasCount(3, viaFragment.NewNodes);
            Assert.HasCount(2, viaFragment.NewLinks);
        }

        [TestMethod]
        public void SerializeProductionGraph_SecondSerializeMatchesFirst() {
            var data = BuildSimpleChain();
            string first = GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: true);
            string second = GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: true);
            Assert.AreEqual(first, second);
        }

        private static readonly JsonSerializerOptions opts = new() { WriteIndented = true };
        [TestMethod]
        public async Task Flowchart_LoadedGraphSerialize_IsStableAndDiffersFromRawFile() {
            string path = FlowchartSample.ResolvePath();
            Assert.IsNotNull(TestContext);
            string disk = await File.ReadAllTextAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            var cache = await SpaceAgeDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            GraphViewerSaveDocument? saveDocument = GraphSaveCodec.ReadViewer(disk);
            Assert.IsNotNull(saveDocument);

            var graph = new ProductionGraph();
            GraphSaveTestUi.ApplyViewerUiToGraph(saveDocument, cache, graph);
            GraphSaveLoader.LoadProductionGraph(graph, cache, saveDocument.ProductionGraph, applySolverSettings: true);
            graph.UpdateNodeValues();

            string once = GraphSaveCodec.WriteProductionGraphToString(graph, writeIndented: true);
            string twice = GraphSaveCodec.WriteProductionGraphToString(graph, writeIndented: true);
            Assert.AreEqual(once, twice, "In-memory graph serialization should be stable for dirty detection.");

            string diskGraph = saveDocument.ProductionGraph is not null
                ? JsonSerializer.Serialize(
                    JsonDocument.Parse(disk).RootElement.GetProperty("ProductionGraph"),
                    opts)
                : "";
            Assert.AreNotEqual(diskGraph, once,
                "On-disk graph JSON may differ in array ordering from a round-trip; MainForm compares to a post-load baseline, not the raw file.");
        }

        [TestMethod]
        public void SerializeProductionGraph_RoundTrip_RestoresNodesLinksAndSolverSettings() {
            var data = BuildSimpleChain();
            data.Graph.PullOutputNodes = true;
            data.Graph.PullOutputNodesPower = 42;
            data.Graph.LowPriorityPower = 7;

            ProductionGraphSaveDocument document = GraphSaveCodec.BuildProductionGraph(data.Graph);

            foreach (var node in data.Graph.Nodes.ToList())
                data.Graph.DeleteNode(node);

            var imported = data.Graph.InsertNodesFromDocument(data.Cache, document, applySolverSettings: true);

            Assert.HasCount(3, imported.NewNodes);
            Assert.HasCount(2, imported.NewLinks);
            Assert.IsNotEmpty(imported.NewNodes.OfType<ConsumerNode>());
            Assert.IsNotEmpty(imported.NewNodes.OfType<RecipeNode>());
            Assert.IsNotEmpty(imported.NewNodes.OfType<SupplierNode>());
            Assert.IsTrue(data.Graph.PullOutputNodes);
            Assert.AreEqual(42, data.Graph.PullOutputNodesPower);
            Assert.AreEqual(7, data.Graph.LowPriorityPower);
        }

        [TestMethod]
        public void SerializeProductionGraph_SubsetHonorsSerializeNodeIdSet() {
            var data = BuildSimpleChain();
            var recipeNode = data.Graph.Nodes.OfType<RecipeNode>().Single();

            data.Graph.SerializeNodeIdSet = [recipeNode.NodeID];
            JsonElement json = JsonDocument.Parse(
                GraphSaveCodec.WriteProductionGraphToString(data.Graph, writeIndented: false)).RootElement;
            data.Graph.SerializeNodeIdSet = null;

            Assert.AreEqual(1, json.GetProperty("Nodes").GetArrayLength());
            Assert.AreEqual(0, json.GetProperty("NodeLinks").GetArrayLength());
        }

        // Note: the domain-level NodeCopyOptions round-trip (constructing from an IRecipeNodeViewModel and
        // resolving back through a DataCache) stays a Foreman.Mac concern (Canvas/NodeClipboard.cs,
        // Foreman.Mac.UiTests/Canvas/ClipboardTests.cs) - NodeCopyOptions is a UI type, see
        // docs/upstream-divergences.md. WriteNodeCopyOptionsToString itself is Core-level and portable.
        [TestMethod]
        public void WriteNodeCopyOptionsToString_RoundTripsThroughReadNodeCopyOptions() {
            var document = new NodeCopyOptionsSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                AssemblerName = "assembling-machine-2",
                AssemblerQualityName = "normal",
                NeighbourCount = 2,
                ExtraProductivityBonus = 0.1,
                AssemblerModules = [new ModuleQualitySaveData("speed-module", "normal")],
                BeaconModules = [new ModuleQualitySaveData("speed-module-2", "normal")],
                FuelName = "coal",
                BeaconName = "beacon",
                BeaconQualityName = "normal",
                BeaconCount = 3,
                BeaconsPerAssembler = 1.5,
                BeaconsConst = 0.5
            };

            NodeCopyOptionsSaveDocument? restored = GraphSaveCodec.ReadNodeCopyOptions(
                GraphSaveCodec.WriteNodeCopyOptionsToString(document));

            Assert.IsNotNull(restored);
            Assert.AreEqual(document.AssemblerName, restored.AssemblerName);
            Assert.AreEqual(document.AssemblerQualityName, restored.AssemblerQualityName);
            Assert.AreEqual(document.NeighbourCount, restored.NeighbourCount);
            Assert.AreEqual(document.ExtraProductivityBonus, restored.ExtraProductivityBonus);
            Assert.HasCount(1, restored.AssemblerModules);
            Assert.AreEqual("speed-module", restored.AssemblerModules[0].ModuleName);
            Assert.HasCount(1, restored.BeaconModules);
            Assert.AreEqual("speed-module-2", restored.BeaconModules[0].ModuleName);
            Assert.AreEqual(document.FuelName, restored.FuelName);
            Assert.AreEqual(document.BeaconName, restored.BeaconName);
            Assert.AreEqual(document.BeaconQualityName, restored.BeaconQualityName);
            Assert.AreEqual(document.BeaconCount, restored.BeaconCount);
            Assert.AreEqual(document.BeaconsPerAssembler, restored.BeaconsPerAssembler);
            Assert.AreEqual(document.BeaconsConst, restored.BeaconsConst);
        }

        [TestMethod]
        public void SerializeKeyNodeClipboard_ParsesLegacyTupleKeys() {
            KeyNodeClipboardSaveData? document = GraphSaveCodec.ReadKeyNodeClipboard(
                GraphSaveCodec.WriteKeyNodeClipboardToString(true, "Main bus"));
            Assert.IsNotNull(document);
            Assert.IsTrue(document.KeyNode);
            Assert.AreEqual("Main bus", document.Title);
        }

        [TestMethod]
        public void ReadViewer_LegacySaveVersion_ReturnsNull() {
            string path = LegacySaveSample.ResolvePath();
            JsonElement save = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            Assert.AreNotEqual(GraphSaveFormat.SaveFormatVersion, save.GetProperty("Version").GetInt32());
            Assert.IsNull(GraphSaveCodec.ReadViewer(File.ReadAllText(path)));
        }

        [TestMethod]
        public void GraphSaveWireMapper_NullUi_PreservedThroughRoundTrip() {
            var data = BuildSimpleChain();
            var document = new GraphViewerSaveDocument {
                Version = GraphSaveFormat.SaveFormatVersion,
                ProductionGraph = GraphSaveCodec.BuildProductionGraph(data.Graph),
                Ui = null
            };

            string json = GraphSaveCodec.WriteViewerDocumentToString(document, writeIndented: false);
            GraphViewerSaveDocument? restored = GraphSaveCodec.ReadViewer(json);

            Assert.IsNotNull(restored);
            Assert.IsNull(restored.Ui, "Ui should remain null after wire round-trip when source Ui was null");
        }

        private static GraphBuilder.BuiltData BuildSimpleChain() {
            var builder = GraphBuilder.Create();
            builder.Link(
                builder.Supply("Ore"),
                builder.Recipe().Input("Ore", 1).Output("Plate", 1),
                builder.Consumer("Plate").Target(10));
            return builder.Build();
        }

    }
}
