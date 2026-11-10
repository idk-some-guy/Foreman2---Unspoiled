using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Serialization;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class TwoOnePresetTests : ForemanTestBase {
        public TestContext? TestContext { get; set; }

        private static JsonNode ReadRaw(string presetName) =>
            JsonNode.Parse(File.ReadAllText(Path.Combine(PresetDataCacheFixture.PresetsDirectory, presetName + ".pjson")))!;

        [TestMethod]
        public async Task SpaceAge21_Loads_GameVersionIs2_1_24() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            Assert.AreEqual(new FactorioGameVersion(2, 1, 24), cache.GameVersion);
        }

        [TestMethod]
        public void SpaceAge21_Qualities_CarryChainProbability() {
            JsonNode normal = ReadRaw(PresetDataCacheFixture.SpaceAge21)["qualities"]!.AsArray().Single(q => (string?)q!["name"] == "normal")!;
            Assert.AreEqual(0.1, (double)normal["chain_probability"]!, 0);
            Assert.AreEqual(1, (double)normal["next_probability"]!, 0);
        }

        [TestMethod]
        public void SpaceAge21_Recipes_HaveNoLegacyCategoryNames() {
            JsonNode raw = ReadRaw(PresetDataCacheFixture.SpaceAge21);
            string[] legacy = ["basic-crafting", "chemistry-or-cryogenics"];
            string[] offenders = [.. raw["recipes"]!.AsArray()
                .Where(r => r!["categories"]!.AsArray().Any(c => legacy.Contains((string?)c)))
                .Select(r => (string)r!["name"]!)];
            Assert.IsEmpty(offenders, "Recipes in legacy categories: " + string.Join(", ", offenders));
        }

        [TestMethod]
        public async Task SpaceAge21_IronOreMelting_Exists_AndMoltenIronDoesNot() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            Assert.IsTrue(cache.Recipes.ContainsKey("iron-ore-melting"));
            Assert.IsFalse(cache.Recipes.ContainsKey("molten-iron"));
        }

        [TestMethod]
        public async Task SpaceAge21_RecyclingRecipes_Load_WithRecyclerIcons() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(
                new Preset(PresetDataCacheFixture.SpaceAge21, true, true),
                NullProgress.Instance,
                loadIcons: true,
                cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);

            IRecipe[] recycling = [.. cache.Recipes.Values.Where(r => r.Name.EndsWith("-recycling", StringComparison.Ordinal))];
            Assert.IsNotEmpty(recycling);
            string[] withoutOwnIcon = [.. recycling
                .Where(r => ReferenceEquals(r.Icon, DataCache.UnknownIcon) || ReferenceEquals(r.Icon, r.IngredientList[0].Icon))
                .Select(r => r.Name)];
            Assert.IsEmpty(withoutOwnIcon, "Recycling recipes without their own icon: " + string.Join(", ", withoutOwnIcon));
        }

        [TestMethod]
        public async Task Vanilla21_Loads_WithBaseOnly() {
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.Vanilla21).ConfigureAwait(false);
            Assert.AreEqual(new FactorioGameVersion(2, 1, 24), cache.GameVersion);
            CollectionAssert.AreEquivalent(new[] { "core", "base" }, cache.IncludedMods.Keys.ToArray());
            Assert.IsFalse(cache.Recipes.ContainsKey("iron-ore-melting"));
        }

        [TestMethod]
        public void SpaceAge21_Recipes_CarryCategoryBesideCategories() {
            foreach (JsonNode? recipe in ReadRaw(PresetDataCacheFixture.SpaceAge21)["recipes"]!.AsArray()) {
                var categories = recipe!["categories"]!.AsArray();
                Assert.IsNotEmpty(categories, (string)recipe["name"]!);
                Assert.AreEqual((string?)categories[0], (string?)recipe["category"], (string)recipe["name"]!);
            }
        }

        [TestMethod]
        public async Task Flowchart_SpaceAge20Graph_OnSpaceAge21Preset_FlagsMissingRecipesWithoutThrowing() {
            GraphViewerSaveDocument saveDocument = GraphSaveTestUi.ReadViewer(
                await File.ReadAllTextAsync(FlowchartSample.ResolvePath(), TestContext!.CancellationToken).ConfigureAwait(false))!;
            DataCache cache = await PresetDataCacheFixture.GetLoadedAsync(PresetDataCacheFixture.SpaceAge21).ConfigureAwait(false);
            var graph = new ProductionGraph();
            GraphSaveTestUi.ApplyViewerUiToGraph(saveDocument, cache, graph);

            GraphSaveLoader.LoadProductionGraph(graph, cache, saveDocument.ProductionGraph, applySolverSettings: true);
            graph.UpdateNodeValues();

            Assert.IsTrue(graph.Nodes.OfType<RecipeNode>().Any(n => (n.ErrorSet & RecipeNode.Errors.RecipeIsMissing) != 0));
        }
    }
}
