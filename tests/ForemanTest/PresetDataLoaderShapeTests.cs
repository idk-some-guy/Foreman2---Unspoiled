using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class PresetDataLoaderShapeTests : ForemanTestBase {
        [TestMethod]
        public void Recipe_WithCategoriesAndNoCategory_IsLoaded() {
            var (cache, store, session, loader) = NewLoader();
            TestDataCacheHelper.GetOrCreateItem(cache, store.MissingSubgroup!, "dummy-product");

            loader.ProcessRecipe(RecipeJson(r => r["categories"] = new JsonArray("crafting", "hand-crafting")), []);

            Assert.IsTrue(cache.Recipes.ContainsKey("shape-recipe"));
            Assert.IsTrue(session.CraftingCategories.ContainsKey("crafting"));
            Assert.IsTrue(session.CraftingCategories.ContainsKey("hand-crafting"));
        }

        [TestMethod]
        public void Recipe_WithCategoryAndCategories_UsesCategoryAsPrimary() {
            var (cache, store, session, loader) = NewLoader();
            TestDataCacheHelper.GetOrCreateItem(cache, store.MissingSubgroup!, "dummy-product");

            loader.ProcessRecipe(RecipeJson(r => {
                r["category"] = "chemistry";
                r["categories"] = new JsonArray("crafting");
            }), []);

            var recipe = (RecipePrototype)cache.Recipes["shape-recipe"];
            CollectionAssert.AreEquivalent(new[] { "chemistry", "crafting" }, recipe.CraftingCategoryKeysInternal.ToArray());
            Assert.IsTrue(session.CraftingCategories["chemistry"].Contains(recipe));
        }

        [TestMethod]
        public void Item_FuelCategoriesArray_JoinsEachCategory() {
            var (_, _, session, loader) = NewLoader();

            loader.ProcessItem(ItemJson(i => i["fuel_categories"] = new JsonArray("chemical", "nutrients")), []);

            Assert.AreEqual("shape-fuel", session.FuelCategories["chemical"].Single().Name);
            Assert.AreEqual("shape-fuel", session.FuelCategories["nutrients"].Single().Name);
        }

        [TestMethod]
        public void Item_LegacyFuelCategory_StillLoads() {
            var (cache, _, session, loader) = NewLoader();

            loader.ProcessItem(ItemJson(i => i["fuel_category"] = "chemical"), []);

            Assert.AreEqual("shape-fuel", session.FuelCategories["chemical"].Single().Name);
            Assert.AreEqual(4, cache.Items["shape-fuel"].FuelValue);
        }

        [TestMethod]
        public void Entity_AgriculturalTower_LoadsAsAgriculturalTowerWithModuleSlots() {
            var (cache, store, session, loader) = NewLoader();
            AddProductivityModule(cache, store, session);

            new EntityDataLoader(cache, store, session).ProcessEntity(TowerJson(), []);

            var tower = cache.Assemblers["shape-tower"];
            Assert.AreEqual(EntityType.AgriculturalTower, tower.EntityType);
            Assert.AreEqual(4, tower.ModuleSlots);
            Assert.IsNotEmpty(tower.Modules);
        }

        [TestMethod]
        public void Entity_AgriculturalTower_IsNotOfferedForAnyRecipe() {
            var (cache, store, session, _) = NewLoader();
            AddProductivityModule(cache, store, session);

            new EntityDataLoader(cache, store, session).ProcessEntity(TowerJson(), []);

            Assert.IsEmpty(cache.Assemblers["shape-tower"].Recipes);
            Assert.IsFalse(session.CraftingCategoryMachines.Values.Any(names => names.Contains("shape-tower")));
        }

        [TestMethod]
        public void Entity_AgriculturalTower_HasTowerTypeName() {
            var (cache, store, session, _) = NewLoader();

            new EntityDataLoader(cache, store, session).ProcessEntity(TowerJson(), []);

            var tower = cache.Assemblers["shape-tower"];
            Assert.AreEqual("Agricultural Tower", tower.GetEntityTypeName(false));
            Assert.AreEqual("Agricultural Towers", tower.GetEntityTypeName(true));
        }

        [TestMethod]
        public void Entity_FluidFuelTemperatures_LoadFromKeys() {
            var (cache, store, session, _) = NewLoader();

            new EntityDataLoader(cache, store, session).ProcessEntity(new JsonObject {
                ["name"] = "shape-fluid-burner",
                ["localised_name"] = "Shape fluid burner",
                ["type"] = "assembling-machine",
                ["fuel_type"] = "fluid",
                ["burns_fluid"] = false,
                ["minimum_fuel_temperature"] = 15.0,
                ["maximum_fuel_temperature"] = 250.0,
            }, []);

            var range = cache.Assemblers["shape-fluid-burner"].FluidFuelTemperatureRange;
            Assert.AreEqual(15.0, range.Min, 0);
            Assert.AreEqual(250.0, range.Max, 0);
        }

        private static void AddProductivityModule(DataCache cache, DataCacheStore store, PresetLoadSession session) {
            var loader = new PresetDataLoader(cache, store, session);
            loader.ProcessModule(new JsonObject {
                ["name"] = "shape-productivity-module",
                ["localised_name"] = "Shape module",
                ["tier"] = 1,
                ["category"] = "productivity",
                ["module_effects"] = new JsonObject { ["productivity"] = 0.04, ["consumption"] = 0.4 },
            }, []);
        }

        private static JsonObject TowerJson() => new() {
            ["name"] = "shape-tower",
            ["localised_name"] = "Shape tower",
            ["type"] = "agricultural-tower",
            ["fuel_type"] = "electricity",
            ["module_inventory_size"] = 4,
            ["uses_module_effects"] = true,
            ["allowed_effects"] = new JsonObject { ["productivity"] = true, ["speed"] = true, ["consumption"] = true, ["pollution"] = true, ["quality"] = true },
        };

        [TestMethod]
        public void Item_FuelCategoriesAndLegacyFuelCategory_ArrayWins() {
            var (_, _, session, loader) = NewLoader();

            loader.ProcessItem(ItemJson(i => {
                i["fuel_categories"] = new JsonArray("nutrients");
                i["fuel_category"] = "chemical";
            }), []);

            Assert.AreEqual("shape-fuel", session.FuelCategories["nutrients"].Single().Name);
            Assert.IsFalse(session.FuelCategories.ContainsKey("chemical"));
        }

        [TestMethod]
        public async Task LoadAllData_CategoriesOnlyRecipeAndTower_SurvivePostLoad() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try {
                var preset = new JsonObject {
                    ["mods"] = new JsonArray(new JsonObject { ["name"] = "base", ["version"] = "2.1.24" }),
                    ["items"] = new JsonArray(ItemJson(i => i["name"] = "dummy-product")),
                    ["recipes"] = new JsonArray(RecipeJson(r => r["categories"] = new JsonArray("crafting"))),
                    ["entities"] = new JsonArray(
                        new JsonObject {
                            ["name"] = "shape-assembler",
                            ["localised_name"] = "Shape assembler",
                            ["type"] = "assembling-machine",
                            ["fuel_type"] = "electricity",
                            ["speed"] = 1,
                            ["crafting_categories"] = new JsonArray("crafting"),
                        },
                        TowerJson()),
                };
                File.WriteAllText(Path.Combine(dir, "Shape Preset.pjson"), preset.ToJsonString());

                var cache = new DataCache(filterRecipes: false);
                await cache.LoadAllData(new Preset("Shape Preset", true, true), NullProgress.Instance, loadIcons: false, userPresetsDirectoryOverride: dir);

                Assert.IsTrue(cache.Recipes.ContainsKey("shape-recipe"));
                Assert.AreEqual(EntityType.AgriculturalTower, cache.Assemblers["shape-tower"].EntityType);
                Assert.IsEmpty(cache.Assemblers["shape-tower"].Recipes);
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static (DataCache Cache, DataCacheStore Store, PresetLoadSession Session, PresetDataLoader Loader) NewLoader() {
            var cache = new DataCache(filterRecipes: false);
            DataCacheStore store = TestDataCacheHelper.RequireStore(cache);
            var session = new PresetLoadSession();
            return (cache, store, session, new PresetDataLoader(cache, store, session));
        }

        private static JsonObject ItemJson(System.Action<JsonObject> shape) {
            var item = new JsonObject {
                ["name"] = "shape-fuel",
                ["localised_name"] = "Shape fuel",
                ["order"] = "z",
                ["stack_size"] = 50,
                ["weight"] = 1,
                ["ingredient_to_weight_coefficient"] = 1,
                ["fuel_value"] = 4,
            };
            shape(item);
            return item;
        }

        private static JsonObject RecipeJson(System.Action<JsonObject> shape) {
            var recipe = new JsonObject {
                ["name"] = "shape-recipe",
                ["localised_name"] = "Shape recipe",
                ["order"] = "a",
                ["energy"] = 1,
                ["products"] = new JsonArray {
                    new JsonObject { ["name"] = "dummy-product", ["type"] = "item", ["amount"] = 1, ["p_amount"] = 1 }
                },
                ["ingredients"] = new JsonArray(),
            };
            shape(recipe);
            return recipe;
        }
    }
}
