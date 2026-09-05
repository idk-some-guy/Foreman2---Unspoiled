using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Models;
using System.Collections.Generic;
using System.Reflection;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.support {
    public sealed class CatalogFixture {
        public required DataCache Cache { get; init; }
        internal SubgroupPrototype Subgroup { get; init; } = null!;
        internal AssemblerPrototype Assembler { get; init; } = null!;

        private readonly Dictionary<string, ItemPrototype> itemsByName = [];
        private readonly Dictionary<string, RecipePrototype> recipesByName = [];
        private GraphCanvasControl? control;

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static CatalogFixture NewBase() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var group = new GroupPrototype(cache, "group", "Group", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "subgroup", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var assembler = new AssemblerPrototype(cache, "assembler", "Assembler", EntityType.Assembler, EnergySource.Electric) { Available = true };

            return new CatalogFixture { Cache = cache, Subgroup = subgroup, Assembler = assembler };
        }

        private ItemPrototype AddOrGetItem(string name) {
            if (!itemsByName.TryGetValue(name, out ItemPrototype? item)) {
                item = new ItemPrototype(Cache, name, name, Subgroup, "a") { Available = true };
                Store(Cache).Items[name] = item;
                itemsByName[name] = item;
            }
            return item;
        }

        private RecipePrototype AddRecipe(string name) {
            var recipe = new RecipePrototype(Cache, name, name, Subgroup, "a") { Available = true };
            recipe.AssemblersInternal.Add(Assembler);
            Store(Cache).Recipes[name] = recipe;
            recipesByName[name] = recipe;
            return recipe;
        }

        public static CatalogFixture WithItemsAndRecipes(IReadOnlyList<string> items, IReadOnlyList<string> recipes, IReadOnlyList<string>? hidden = null) {
            CatalogFixture fx = NewBase();
            foreach (string name in items)
                fx.AddOrGetItem(name);
            foreach (string name in recipes)
                fx.AddRecipe(name);
            foreach (string name in hidden ?? []) {
                if (fx.itemsByName.TryGetValue(name, out ItemPrototype? item))
                    item.Enabled = false;
                if (fx.recipesByName.TryGetValue(name, out RecipePrototype? recipe))
                    recipe.Enabled = false;
            }
            return fx;
        }

        //ore -> "Iron Plate" (produces plate), plate -> "Iron Gear" (consumes plate), unrelated "Copper Cable".
        public static CatalogFixture WithProductionChain() {
            CatalogFixture fx = NewBase();
            ItemPrototype ore = fx.AddOrGetItem("Iron Ore");
            ItemPrototype plate = fx.AddOrGetItem("Iron Plate");
            ItemPrototype gear = fx.AddOrGetItem("Iron Gear");
            ItemPrototype copperOre = fx.AddOrGetItem("Copper Ore");
            ItemPrototype cable = fx.AddOrGetItem("Copper Cable");

            RecipePrototype plateRecipe = fx.AddRecipe("Iron Plate");
            plateRecipe.InternalOneWayAddIngredient(ore, 1);
            plateRecipe.InternalOneWayAddProduct(plate, 1, 0);

            RecipePrototype gearRecipe = fx.AddRecipe("Iron Gear");
            gearRecipe.InternalOneWayAddIngredient(plate, 1);
            gearRecipe.InternalOneWayAddProduct(gear, 1, 0);

            RecipePrototype cableRecipe = fx.AddRecipe("Copper Cable");
            cableRecipe.InternalOneWayAddIngredient(copperOre, 1);
            cableRecipe.InternalOneWayAddProduct(cable, 1, 0);

            return fx;
        }

        public static CatalogFixture WithQualities(IReadOnlyList<string> qualityNames) {
            CatalogFixture fx = NewBase();
            DataCacheStore store = Store(fx.Cache);
            store.Qualities.Clear();
            QualityPrototype? first = null;
            foreach (string name in qualityNames) {
                var quality = new QualityPrototype(fx.Cache, name, Capitalize(name), "a");
                store.Qualities[name] = quality;
                first ??= quality;
            }
            store.DefaultQuality = first;
            return fx;
        }

        private static string Capitalize(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

        //Two groups, each with at least one recipe so both segments sort something into Groups[1].
        public static CatalogFixture WithGroups() {
            var cache = new DataCache(filterRecipes: true);
            DataCacheStore store = Store(cache);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var assembler = new AssemblerPrototype(cache, "assembler", "Assembler", EntityType.Assembler, EnergySource.Electric) { Available = true };

            var logistics = new GroupPrototype(cache, "logistics", "Logistics", "a");
            store.Groups[logistics.Name] = logistics;
            var storage = new SubgroupPrototype(cache, "Storage", "a");
            storage.MyGroupInternal = logistics;
            logistics.SubgroupsInternal.Add(storage);
            store.Subgroups[storage.Name] = storage;
            var belt = new SubgroupPrototype(cache, "Belt", "b");
            belt.MyGroupInternal = logistics;
            logistics.SubgroupsInternal.Add(belt);
            store.Subgroups[belt.Name] = belt;

            var woodenChest = new ItemPrototype(cache, "wooden-chest", "Wooden Chest", storage, "a") { Available = true };
            var ironChest = new ItemPrototype(cache, "iron-chest", "Iron Chest", storage, "b") { Available = true };
            var steelChest = new ItemPrototype(cache, "steel-chest", "Steel Chest", storage, "c") { Available = false };
            store.Items[woodenChest.Name] = woodenChest;
            store.Items[ironChest.Name] = ironChest;
            store.Items[steelChest.Name] = steelChest;

            var transportBelt = new ItemPrototype(cache, "transport-belt", "Transport Belt", belt, "a") { Available = true };
            store.Items[transportBelt.Name] = transportBelt;

            var beltRecipe = new RecipePrototype(cache, "transport-belt", "Transport Belt", belt, "a") { Available = true };
            beltRecipe.AssemblersInternal.Add(assembler);
            beltRecipe.InternalOneWayAddProduct(transportBelt, 1, 0);
            store.Recipes[beltRecipe.Name] = beltRecipe;

            var production = new GroupPrototype(cache, "production", "Production", "b");
            store.Groups[production.Name] = production;
            var assemblers = new SubgroupPrototype(cache, "Assemblers", "a");
            assemblers.MyGroupInternal = production;
            production.SubgroupsInternal.Add(assemblers);
            store.Subgroups[assemblers.Name] = assemblers;

            var assemblingMachine1 = new ItemPrototype(cache, "assembling-machine-1", "Assembling Machine 1", assemblers, "a") { Available = true };
            store.Items[assemblingMachine1.Name] = assemblingMachine1;

            var assemblerRecipe = new RecipePrototype(cache, "assembling-machine-1", "Assembling Machine 1", assemblers, "a") { Available = true };
            assemblerRecipe.AssemblersInternal.Add(assembler);
            assemblerRecipe.InternalOneWayAddProduct(assemblingMachine1, 1, 0);
            store.Recipes[assemblerRecipe.Name] = assemblerRecipe;

            var hiddenRecipe = new RecipePrototype(cache, "assembling-machine-2", "Assembling Machine 2", assemblers, "b") { Available = true, Enabled = false };
            hiddenRecipe.AssemblersInternal.Add(assembler);
            hiddenRecipe.InternalOneWayAddProduct(assemblingMachine1, 1, 0);
            store.Recipes[hiddenRecipe.Name] = hiddenRecipe;

            return new CatalogFixture { Cache = cache, Subgroup = storage, Assembler = assembler };
        }

        public AddLinkContext LinkContextFor(string itemName, NewNodeType nodeType) {
            ItemPrototype item = itemsByName[itemName];
            var pair = new ItemQualityPair(item, Store(Cache).DefaultQuality!);

            control ??= NewControl();
            NodeId nodeId = control.Viewer.Session.Editor.CreateSupplierNode(pair, new DrawingPoint(0, 0));
            BaseNodeElement originElement = Prime(control.Viewer.NodeElementDictionary[nodeId]);

            return new AddLinkContext(pair, nodeType, new FRange(0, 0, true), originElement, NodeDirection.Up);
        }

        private static GraphCanvasControl NewControl() {
            var canvas = new GraphCanvasControl();
            canvas.Viewport.SetSize(600, 600);
            var window = new AvaloniaWindow { Content = canvas, Width = 600, Height = 600 };
            window.Show();
            return canvas;
        }

        private static BaseNodeElement Prime(BaseNodeElement element) {
            element.RequestStateUpdate();
            element.PrePaint();
            return element;
        }
    }
}
