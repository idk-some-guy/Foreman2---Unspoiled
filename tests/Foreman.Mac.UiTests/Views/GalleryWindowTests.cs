using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Task: dev-only gallery smoke test (docs entry: "--gallery" mode). Builds a small fixture cache with the
    //same shape as EditRecipePanelTests' RecipeFixture - a recipe with an electric assembler (modules +
    //beacon) alongside a burner alternative (fuel) - so GalleryWindow's showcase-recipe search has real
    //content to populate the EditRecipePanel tile with, then proves every expected tile actually gets built.
    public class GalleryWindowTests {
        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static DataCache NewFixtureCache() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var group = new GroupPrototype(cache, "production", "Production", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "production-sub", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var ore = new ItemPrototype(cache, "ore", "Ore", subgroup, "a") { Available = true };
            var plate = new ItemPrototype(cache, "plate", "Plate", subgroup, "a") { Available = true };
            var coal = new ItemPrototype(cache, "coal", "Coal", subgroup, "a") { Available = true };
            store.Items[ore.Name] = ore;
            store.Items[plate.Name] = plate;
            store.Items[coal.Name] = coal;

            var recipe = new RecipePrototype(cache, "smelt", "Smelt", subgroup, "a") { Available = true };
            recipe.InternalOneWayAddIngredient(ore, 1);
            ore.ConsumptionRecipesInternal.Add(recipe);
            recipe.InternalOneWayAddProduct(plate, 1, 0);
            plate.ProductionRecipesInternal.Add(recipe);
            store.Recipes[recipe.Name] = recipe;

            var mineCoal = new RecipePrototype(cache, "mine-coal", "Mine Coal", subgroup, "a") { Available = true };
            mineCoal.InternalOneWayAddProduct(coal, 1, 0);
            coal.ProductionRecipesInternal.Add(mineCoal);
            store.Recipes[mineCoal.Name] = mineCoal;

            var electric = new AssemblerPrototype(cache, "electric-assembler", "Electric Assembler", EntityType.Assembler, EnergySource.Electric) {
                Available = true, Enabled = true, ModuleSlots = 2, AllowModules = true, AllowBeacons = true,
            };
            var burner = new AssemblerPrototype(cache, "burner-assembler", "Burner Assembler", EntityType.Assembler, EnergySource.Burner) {
                Available = true, Enabled = true,
            };
            burner.FuelsInternal.Add(coal);

            recipe.AssemblersInternal.Add(electric);
            electric.RecipesInternal.Add(recipe);
            recipe.AssemblersInternal.Add(burner);
            burner.RecipesInternal.Add(recipe);
            mineCoal.AssemblersInternal.Add(electric);
            electric.RecipesInternal.Add(mineCoal);

            //ModulePrototype.Available derives from an Item of the same name existing in the cache.
            var moduleItem = new ItemPrototype(cache, "speed-module", "Speed Module", subgroup, "a") { Available = true };
            store.Items[moduleItem.Name] = moduleItem;
            var module = new ModulePrototype(cache, "speed-module", "Speed Module") { Category = "production" };
            store.Modules[module.Name] = module;
            electric.ModulesInternal.Add(module);
            recipe.AssemblerModulesInternal.Add(module);
            recipe.BeaconModulesInternal.Add(module);

            var beacon = new BeaconPrototype(cache, "beacon-1", "Beacon", EnergySource.Electric) { Available = true, ModuleSlots = 2 };
            store.Beacons[beacon.Name] = beacon;
            beacon.ModulesInternal.Add(module);

            return cache;
        }

        [AvaloniaFact]
        public void Construction_BuildsATilePerExpectedPanel() {
            DataCache cache = NewFixtureCache();
            var settings = new AppSettings();

            var window = new GalleryWindow(cache, settings);
            window.Show();

            string[] captions = [.. window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

            Assert.Contains("ItemChooserPanel", captions);
            Assert.Contains("RecipeChooserPanel", captions);
            Assert.Contains("EditFlowPanel", captions);
            Assert.Contains("EditRecipePanel", captions);
            Assert.Contains("QualityPicker", captions);
        }

        //Every tile must show its panel the way it actually renders in the real app - floating in its own
        //dark chrome bubble over the graph canvas's white backdrop (GraphViewer.Paint's canvas.Clear(
        //SKColors.White)), not lost against an equally black window.
        [AvaloniaFact]
        public void EveryTile_PanelHasNonTransparentChromeBackground() {
            DataCache cache = NewFixtureCache();
            var settings = new AppSettings();

            var window = new GalleryWindow(cache, settings);
            window.Show();

            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);

            Control[] panels = [
                window.GetVisualDescendants().OfType<ItemChooserPanel>().Single(),
                window.GetVisualDescendants().OfType<RecipeChooserPanel>().Single(),
                window.GetVisualDescendants().OfType<EditFlowPanel>().Single(),
                window.GetVisualDescendants().OfType<EditRecipePanel>().Single(),
                //IRChooserPanel and EditRecipePanel each embed their own QualityPicker too; the standalone
                //gallery tile is the one ChromeWrap made a direct Border child, not one of those.
                window.GetVisualDescendants().OfType<QualityPicker>().Single(p => p.Parent is Border),
            ];

            foreach (Control panel in panels) {
                Border chrome = panel as Border ?? panel.GetVisualAncestors().OfType<Border>().First();
                Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(chrome.Background).Color);
            }
        }

        //Finding B1 (2026-09-02 gallery review): the real app floats EditRecipePanel next to its own
        //RecipePanel (upstream EditRecipeNode's editPanel+recipePanel pair) rather than embedding the recipe
        //card inside the edit panel - the gallery tile should show the same pairing, not the edit panel alone.
        [AvaloniaFact]
        public void EditRecipePanelTile_AlsoShowsItsCompanionRecipePanel() {
            DataCache cache = NewFixtureCache();
            var settings = new AppSettings();

            var window = new GalleryWindow(cache, settings);
            window.Show();

            Assert.Single(window.GetVisualDescendants().OfType<EditRecipePanel>());
            Assert.Single(window.GetVisualDescendants().OfType<RecipePanel>());
        }
    }
}
