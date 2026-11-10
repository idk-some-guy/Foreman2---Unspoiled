using Foreman.DataCaching.Loading;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ForemanTest {
    [TestClass]
    public class PresetCraftingCompatibilityTests : ForemanTestBase {
        [TestMethod]
        public void CollectRecipeCraftingCategories_MergesExportedCategoryArrays() {
            var recipe = MinimalRecipeJson(
                primaryCategory: "chemistry",
                craftingCategories: ["chemistry", "advanced-chemistry"],
                additionalCategories: ["smelting"]);

            var categories = PresetCraftingCompatibility.CollectRecipeCraftingCategories(recipe, "chemistry").ToArray();
            ICollection<string> expected = ["chemistry", "advanced-chemistry", "smelting"];
            CollectionAssert.AreEquivalent(expected as ICollection, categories);
        }

        [TestMethod]
        public void CollectRecipeCraftingCategories_IncludesPrimaryAndAdditionalWhenNoCraftingCategoriesArray() {
            var recipe = MinimalRecipeJson(
                primaryCategory: "crafting",
                additionalCategories: ["advanced-crafting"]);

            var categories = PresetCraftingCompatibility.CollectRecipeCraftingCategories(recipe, "crafting").ToArray();
            ICollection<string> expected = ["crafting", "advanced-crafting"];
            CollectionAssert.AreEquivalent(expected as ICollection, categories);
        }

        [TestMethod]
        public void CollectRecipeCraftingCategories_CategoriesArrayOnly_ReturnsEveryEntry() {
            var recipe = new JsonObject {
                ["categories"] = new JsonArray("crafting", "hand-crafting"),
            };

            var categories = PresetCraftingCompatibility.CollectRecipeCraftingCategories(recipe).ToArray();
            ICollection<string> expected = ["crafting", "hand-crafting"];
            CollectionAssert.AreEquivalent(expected as ICollection, categories);
        }

        [TestMethod]
        public void CollectRecipeCraftingCategories_LegacyCategoryAndAdditional_StillMerged() {
            var recipe = MinimalRecipeJson(
                primaryCategory: "crafting",
                additionalCategories: ["advanced-crafting"]);

            var categories = PresetCraftingCompatibility.CollectRecipeCraftingCategories(recipe).ToArray();
            ICollection<string> expected = ["crafting", "advanced-crafting"];
            CollectionAssert.AreEquivalent(expected as ICollection, categories);
        }

        [TestMethod]
        public void ResolvePrimaryCategory_PrefersCategoryThenCategoriesThenCraftingCategories() {
            Assert.AreEqual("a", PresetCraftingCompatibility.ResolvePrimaryCategory(
                new JsonObject { ["category"] = "a", ["categories"] = new JsonArray("b"), ["crafting_categories"] = new JsonArray("c") }));
            Assert.AreEqual("b", PresetCraftingCompatibility.ResolvePrimaryCategory(
                new JsonObject { ["categories"] = new JsonArray("b", "x"), ["crafting_categories"] = new JsonArray("c") }));
            Assert.AreEqual("c", PresetCraftingCompatibility.ResolvePrimaryCategory(
                new JsonObject { ["crafting_categories"] = new JsonArray("c") }));
            Assert.IsNull(PresetCraftingCompatibility.ResolvePrimaryCategory(new JsonObject()));
        }

        private static JsonObject MinimalRecipeJson(
            string primaryCategory,
            string[]? craftingCategories = null,
            string[]? additionalCategories = null) {
            var recipe = new JsonObject {
                ["name"] = "test-recipe",
                ["localised_name"] = "Test",
                ["subgroup"] = "raw-resource",
                ["order"] = "a",
                ["category"] = primaryCategory,
                ["energy"] = 1,
                ["ingredients"] = new JsonArray(),
                ["products"] = new JsonArray(),
            };

            if (craftingCategories is { Length: > 0 })
                recipe["crafting_categories"] = new JsonArray(craftingCategories.Select(c => JsonValue.Create(c)).ToArray());
            if (additionalCategories is { Length: > 0 })
                recipe["additional_categories"] = new JsonArray(additionalCategories.Select(c => JsonValue.Create(c)).ToArray());

            return recipe;
        }
    }
}
