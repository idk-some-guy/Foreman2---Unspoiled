using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Models;
using Foreman.Models.Nodes;
using Foreman.Serialization;
using ForemanTest.Graph;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class CoreEndToEndTests : ForemanTestBase {
        [TestInitialize]
        public void TestInitialize() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");
        }

        [TestMethod]
        public async Task VanillaPreset_IronGearGraph_SolvesAndRoundTrips() {
            DataCache cache = await VanillaDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);
            IRecipe recipe = cache.Recipes["iron-gear-wheel"];
            IItem inputItem = cache.Items["iron-plate"];
            IItem outputItem = cache.Items["iron-gear-wheel"];
            GraphSessionTestHelper.TestContext ctx = GraphSessionTestHelper.CreateContext(cache);

            ProductionGraph graph = ctx.NewGraph();
            ItemQualityPair inputPair = new(inputItem, ctx.Quality);
            ItemQualityPair outputPair = new(outputItem, ctx.Quality);

            BaseNode supplier = graph.CreateSupplierNode(inputPair, Point.Empty);
            RecipeNode recipeNode = graph.CreateRecipeNode(new RecipeQualityPair(recipe, ctx.Quality), new Point(100, 0));
            BaseNode consumer = graph.CreateConsumerNode(outputPair, new Point(200, 0));

            graph.CreateLink(supplier, recipeNode, inputPair);
            graph.CreateLink(recipeNode, consumer, outputPair);

            if (graph.RequestNodeController(consumer) is BaseNodeController consumerController) {
                consumerController.SetRateType(RateType.Manual);
                consumerController.SetDesiredSetValue(10);
            }

            graph.UpdateNodeValues();

            Assert.IsGreaterThan(0, recipeNode.ActualRate, "iron-gear-wheel recipe node should solve to a positive rate.");
            Assert.IsGreaterThan(0, recipeNode.ActualSetValue, "iron-gear-wheel recipe node should solve to a positive building count.");

            string firstSave = GraphSaveCodec.WriteProductionGraphToString(graph);
            ProductionGraphSaveDocument? firstDocument = GraphSaveCodec.ReadProductionGraph(firstSave);
            Assert.IsNotNull(firstDocument);

            ProductionGraph reloaded = ctx.NewGraph();
            GraphSaveLoader.LoadProductionGraph(reloaded, cache, firstDocument, applySolverSettings: true);
            reloaded.UpdateNodeValues();

            Assert.AreEqual(graph.Nodes.Count(), reloaded.Nodes.Count());

            string secondSave = GraphSaveCodec.WriteProductionGraphToString(reloaded);
            ProductionGraphSaveDocument? secondDocument = GraphSaveCodec.ReadProductionGraph(secondSave);
            Assert.IsNotNull(secondDocument);

            Assert.AreEqual(firstDocument.Nodes.Count, secondDocument.Nodes.Count);

            ProductionGraph reloadedAgain = ctx.NewGraph();
            GraphSaveLoader.LoadProductionGraph(reloadedAgain, cache, secondDocument, applySolverSettings: true);
            reloadedAgain.UpdateNodeValues();
            string thirdSave = GraphSaveCodec.WriteProductionGraphToString(reloadedAgain);

            Assert.AreEqual(secondSave, thirdSave, "A second save -> load -> save cycle should be a no-op on the JSON text.");
        }
    }
}
