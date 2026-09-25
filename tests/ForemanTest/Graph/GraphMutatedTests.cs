using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Models.Nodes;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using System.Linq;

namespace ForemanTest.Graph {
    // ProductionGraphSession.GraphMutated drives dirty tracking. Each test below covers one mutation
    // entry point and asserts it raises the event exactly once, plus the negative case (a solve with no
    // mutation raises none).
    [TestClass]
    public class GraphMutatedTests : ForemanTestBase {
        [TestMethod]
        public void CreateSupplierNode_RaisesGraphMutatedOnce() {
            var ctx = GraphSessionTestHelper.CreateContext();
            var session = GraphSessionTestHelper.AttachSession(ctx.NewGraph());
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            session.Editor.CreateSupplierNode(ctx.Item("iron"), Point.Empty);

            Assert.AreEqual(1, count);
        }

        // Regression for the recipe-node creation path specifically: CreateRecipeNode calls
        // AutoSetAssembler/AutoSetAssemblerModules on the new node before it's added to the graph, so
        // those setter calls must not sneak in an extra raise ahead of the one NodeAdded triggers.
        [TestMethod]
        public void CreateRecipeNode_AutoSelectsAssemblerButRaisesGraphMutatedOnce() {
            GraphBuilder.BuiltData built = GraphSessionTestHelper.BuildSimpleChain();
            var session = GraphSessionTestHelper.AttachSession(built.Graph);
            var ctx = GraphSessionTestHelper.CreateContext(built.Cache);
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            session.Editor.CreateRecipeNode(new RecipeQualityPair(RecipePrototypeFor(built), ctx.Quality), new Point(300, 0));

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void DeleteNode_RaisesGraphMutatedOnce() {
            var ctx = GraphSessionTestHelper.CreateContext();
            var session = GraphSessionTestHelper.AttachSession(ctx.NewGraph());
            NodeId id = session.Editor.CreateSupplierNode(ctx.Item("iron"), Point.Empty);
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            session.Editor.DeleteNode(id);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void CreateLink_RaisesGraphMutatedOnce() {
            var ctx = GraphSessionTestHelper.CreateContext();
            var session = GraphSessionTestHelper.AttachSession(ctx.NewGraph());
            ItemQualityPair iron = ctx.Item("iron");
            NodeId supplierId = session.Editor.CreateSupplierNode(iron, Point.Empty);
            NodeId consumerId = session.Editor.CreateConsumerNode(iron, new Point(100, 0));
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            session.Editor.CreateLink(supplierId, consumerId, iron);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void DeleteLink_RaisesGraphMutatedOnce() {
            var ctx = GraphSessionTestHelper.CreateContext();
            var session = GraphSessionTestHelper.AttachSession(ctx.NewGraph());
            ItemQualityPair iron = ctx.Item("iron");
            NodeId supplierId = session.Editor.CreateSupplierNode(iron, Point.Empty);
            NodeId consumerId = session.Editor.CreateConsumerNode(iron, new Point(100, 0));
            LinkId linkId = session.Editor.CreateLink(supplierId, consumerId, iron);
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            session.Editor.DeleteLink(linkId);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void SetAssembler_RaisesGraphMutatedOnce() {
            GraphBuilder.BuiltData built = GraphSessionTestHelper.BuildSimpleChain();
            var session = GraphSessionTestHelper.AttachSession(built.Graph);
            IRecipeNodeViewModel recipeVm = session.View.Nodes.OfType<IRecipeNodeViewModel>().First();
            RecipeNodeController controller = session.Editor.RequestNodeController(recipeVm.Id) as RecipeNodeController
                ?? throw new AssertFailedException("Recipe node should have a RecipeNodeController.");
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            controller.SetAssembler(recipeVm.SelectedAssembler);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void SetRateType_RaisesGraphMutatedOnce() {
            var ctx = GraphSessionTestHelper.CreateContext();
            var session = GraphSessionTestHelper.AttachSession(ctx.NewGraph());
            NodeId id = session.Editor.CreateSupplierNode(ctx.Item("iron"), Point.Empty);
            BaseNodeController controller = session.Editor.RequestNodeController(id)
                ?? throw new AssertFailedException("Supplier node should have a controller.");
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            controller.SetRateType(RateType.Manual);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void SetRateType_SameValue_RaisesNoGraphMutated() {
            var ctx = GraphSessionTestHelper.CreateContext();
            var session = GraphSessionTestHelper.AttachSession(ctx.NewGraph());
            NodeId id = session.Editor.CreateSupplierNode(ctx.Item("iron"), Point.Empty);
            BaseNodeController controller = session.Editor.RequestNodeController(id)
                ?? throw new AssertFailedException("Supplier node should have a controller.");
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            controller.SetRateType(RateType.Auto); //already Auto - no-op

            Assert.AreEqual(0, count);
        }

        // SetLocation drives live node-drag rendering, called on every pointer move - it must not raise
        // GraphMutated itself, or a single drag would fire dozens of times. The UI commits the mutation
        // once at drag end via the same internal NotifyMutated() this test calls directly.
        [TestMethod]
        public void SetLocation_NeverRaisesGraphMutated_CommitIsTheCallersJob() {
            var ctx = GraphSessionTestHelper.CreateContext();
            var session = GraphSessionTestHelper.AttachSession(ctx.NewGraph());
            NodeId id = session.Editor.CreateSupplierNode(ctx.Item("iron"), Point.Empty);
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            session.Editor.SetLocation(id, new Point(10, 0));
            session.Editor.SetLocation(id, new Point(20, 0));
            session.Editor.SetLocation(id, new Point(30, 0));
            Assert.AreEqual(0, count);

            session.NotifyMutated();

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void Solve_WithoutMutation_RaisesNoGraphMutated() {
            GraphBuilder.BuiltData built = GraphSessionTestHelper.BuildSimpleChain();
            var session = GraphSessionTestHelper.AttachSession(built.Graph);
            int count = 0;
            session.GraphMutated += (_, _) => count++;

            built.Graph.UpdateNodeValues();

            Assert.AreEqual(0, count);
        }

        private static RecipePrototype RecipePrototypeFor(GraphBuilder.BuiltData built) =>
            built.Graph.Nodes.OfType<RecipeNode>().First().BaseRecipe.Recipe as RecipePrototype
                ?? throw new AssertFailedException("Simple chain should have a recipe node with a real recipe.");
    }
}
