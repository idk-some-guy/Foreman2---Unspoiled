using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using Foreman.Models.Nodes;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Foreman.Mac.UiTests {
    //Find node ruling §01: MainWindow.DrawnName/MatchesSearchQuery are internal test seams (MainWindow.axaml.cs) so each node kind's matching rule can be pinned without a full live window.
    public class MainWindowSearchMatchingTests {
        private sealed class Fixture {
            public required DataCache Cache { get; init; }
            public required SubgroupPrototype Subgroup { get; init; }
            public required IQuality Quality { get; init; }
            public required ProductionGraph Graph { get; init; }
            public required ProductionGraphSession Session { get; init; }
            public required NodeElementContext Context { get; init; }

            public ItemPrototype NewItem(string name, string friendlyName) {
                var item = new ItemPrototype(Cache, name, friendlyName, Subgroup, "z", isMissing: false);
                Store(Cache).Items[name] = item;
                return item;
            }

            public ItemQualityPair Pair(IItem item) => new(item, Quality);
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static Fixture NewFixture() {
            var cache = new DataCache(filterRecipes: true);
            var subgroup = new SubgroupPrototype(cache, "§§test:subgroup", "z");
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var graph = new ProductionGraph { DefaultAssemblerQuality = quality };
            var session = new ProductionGraphSession(graph);
            session.Attach();
            var context = new NodeElementContext(session.View, new Viewport(400, 400));

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Graph = graph, Session = session, Context = context };
        }

        [Fact]
        public void SupplierNode_Matches_TheSuppliedItemsFriendlyName() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-plate", "Iron Plate");
            fx.Graph.CreateSupplierNode(fx.Pair(item), Point.Empty);
            var viewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single();
            var element = new SupplierNodeElement(fx.Context, viewModel);

            Assert.True(MainWindow.MatchesSearchQuery(element, "iron pl"));
            Assert.False(MainWindow.MatchesSearchQuery(element, "steel"));
        }

        [Fact]
        public void ConsumerNode_Matches_TheConsumedItemsFriendlyName() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("copper-plate", "Copper Plate");
            fx.Graph.CreateConsumerNode(fx.Pair(item), Point.Empty);
            var viewModel = fx.Session.View.Nodes.OfType<IConsumerNodeViewModel>().Single();
            var element = new ConsumerNodeElement(fx.Context, viewModel);

            Assert.True(MainWindow.MatchesSearchQuery(element, "COPPER"));
        }

        [Fact]
        public void PassthroughNode_Matches_ThePassthroughItemsFriendlyName() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("steel-plate", "Steel Plate");
            fx.Graph.CreatePassthroughNode(fx.Pair(item), Point.Empty);
            var viewModel = fx.Session.View.Nodes.OfType<IPassthroughNodeViewModel>().Single();
            var element = new PassthroughNodeElement(fx.Context, viewModel);

            Assert.True(MainWindow.MatchesSearchQuery(element, "steel"));
        }

        [Fact]
        public void RecipeNode_Matches_TheRecipesFriendlyName() {
            Fixture fx = NewFixture();
            var recipe = new RecipePrototype(fx.Cache, "smelt-iron", "Smelt Iron Plate", fx.Subgroup, "z");
            var assembler = new AssemblerPrototype(fx.Cache, "§§test:assembler", "Test Assembler", EntityType.Assembler, EnergySource.Electric);
            recipe.AssemblersInternal.Add(assembler);
            Store(fx.Cache).Recipes[recipe.Name] = recipe;
            fx.Graph.CreateRecipeNode(new RecipeQualityPair(recipe, fx.Quality), Point.Empty);
            var viewModel = fx.Session.View.Nodes.OfType<IRecipeNodeViewModel>().Single();
            var element = new RecipeNodeElement(fx.Context, viewModel);

            Assert.True(MainWindow.MatchesSearchQuery(element, "Smelt Iron"));
        }

        [Fact]
        public void SpoilNode_Matches_TheInputItemsNamePlusSpoilageSuffix() {
            Fixture fx = NewFixture();
            ItemPrototype input = fx.NewItem("fresh-fish", "Fresh Fish");
            ItemPrototype output = fx.NewItem("rotten-fish", "Rotten Fish");
            input.SpoilResult = output;
            fx.Graph.CreateSpoilNode(fx.Pair(input), output, Point.Empty);
            var viewModel = fx.Session.View.Nodes.OfType<ISpoilNodeViewModel>().Single();
            var element = new SpoilNodeElement(fx.Context, viewModel);

            Assert.True(MainWindow.MatchesSearchQuery(element, "Fresh Fish Spoilage"));
            Assert.False(MainWindow.MatchesSearchQuery(element, "Rotten Fish"));
        }

        [Fact]
        public void PlantNode_Matches_TheSeedsNamePlusPlantingSuffix() {
            Fixture fx = NewFixture();
            ItemPrototype seed = fx.NewItem("yumako-seed", "Yumako Seed");
            ItemPrototype product = fx.NewItem("yumako", "Yumako");
            var process = new PlantProcessPrototype(fx.Cache, "plant-yumako-seed");
            process.InternalOneWayAddProduct(product, 1);
            process.Seed = seed;
            seed.PlantResult = process;
            fx.Graph.CreatePlantNode(process, fx.Quality, Point.Empty);
            var viewModel = fx.Session.View.Nodes.OfType<IPlantNodeViewModel>().Single();
            var element = new PlantNodeElement(fx.Context, viewModel);

            Assert.True(MainWindow.MatchesSearchQuery(element, "Yumako Seed Planting"));
        }

        //KeyNodeTitle matches even when it shares nothing with the node's own drawn name.
        [Fact]
        public void KeyNode_Matches_ItsKeyNodeTitle_RegardlessOfDrawnName() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-plate", "Iron Plate");
            var node = fx.Graph.CreateSupplierNode(fx.Pair(item), Point.Empty);
            BaseNodeController controller = fx.Graph.RequestNodeController(node)!;
            controller.SetKeyNode(true);
            controller.SetKeyNodeTitle("Main Feed Line");
            var viewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single();
            var element = new SupplierNodeElement(fx.Context, viewModel);

            Assert.True(MainWindow.MatchesSearchQuery(element, "Main Feed"));
        }

        //§01 "Not matched": a type banner is chrome, not a name - each assertion here would flip true if the exclusion were dropped.
        [Fact]
        public void TypeBanner_NeverMatches() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-plate", "Iron Plate");
            fx.Graph.CreateSupplierNode(fx.Pair(item), Point.Empty); //default RateType.Auto draws "Infinite Source:"
            var viewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single();
            var element = new SupplierNodeElement(fx.Context, viewModel);

            Assert.False(MainWindow.MatchesSearchQuery(element, "Infinite Source"));
        }

        //§01 "Not matched": an item-tab name is never in NodeElements, so a recipe's own ingredient never matches.
        [Fact]
        public void ItemTabName_OnARecipeNode_NeverMatches() {
            Fixture fx = NewFixture();
            var recipe = new RecipePrototype(fx.Cache, "smelt-copper", "Smelt Copper Plate", fx.Subgroup, "z");
            var assembler = new AssemblerPrototype(fx.Cache, "§§test:assembler", "Test Assembler", EntityType.Assembler, EnergySource.Electric);
            recipe.AssemblersInternal.Add(assembler);
            Store(fx.Cache).Recipes[recipe.Name] = recipe;
            ItemPrototype ingredient = fx.NewItem("tin-ore", "Tin Ore");
            recipe.InternalOneWayAddIngredient(ingredient, 1);
            fx.Graph.CreateRecipeNode(new RecipeQualityPair(recipe, fx.Quality), Point.Empty);
            var viewModel = fx.Session.View.Nodes.OfType<IRecipeNodeViewModel>().Single();
            var element = new RecipeNodeElement(fx.Context, viewModel);

            Assert.False(MainWindow.MatchesSearchQuery(element, "Tin Ore"));
        }

        //The KeyNode guard matches a title only while KeyNode is true - the model's own setter leaves a stale title behind when it flips off.
        [Fact]
        public void NonKeyNode_WithAStaleKeyNodeTitle_MatchesOnlyItsDrawnName() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-plate", "Iron Plate");
            var node = fx.Graph.CreateSupplierNode(fx.Pair(item), Point.Empty);
            node.KeyNode = true;
            node.KeyNodeTitle = "Stale Title";
            node.KeyNode = false;
            var viewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single();
            var element = new SupplierNodeElement(fx.Context, viewModel);

            Assert.False(MainWindow.MatchesSearchQuery(element, "Stale Title"));
            Assert.True(MainWindow.MatchesSearchQuery(element, "Iron Plate"));
        }

        //§01: an all-whitespace query is treated as empty - it must not fall through to Contains("") and match every node.
        [Fact]
        public void WhitespaceOnlyQuery_NeverMatches() {
            Fixture fx = NewFixture();
            ItemPrototype item = fx.NewItem("iron-plate", "Iron Plate");
            fx.Graph.CreateSupplierNode(fx.Pair(item), Point.Empty);
            var viewModel = fx.Session.View.Nodes.OfType<ISupplierNodeViewModel>().Single();
            var element = new SupplierNodeElement(fx.Context, viewModel);

            Assert.False(MainWindow.MatchesSearchQuery(element, "   "));
        }
    }
}
