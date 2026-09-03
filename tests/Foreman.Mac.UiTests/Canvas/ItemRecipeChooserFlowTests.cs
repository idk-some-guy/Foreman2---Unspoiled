using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;
using InputPointer = Avalonia.Input.Pointer;

namespace Foreman.Mac.UiTests.Canvas {
    //Task 3: real ItemChooserPanel/RecipeChooserPanel wired into GraphCanvasControl (docs/panels-reference.md
    //§2/§8), replacing the deleted Views/PlaceholderChooserWindow. Covers the two-stage AddItem->node-type
    //flow, the footer alt-node buttons, the spoil/plant multi-origin sub-pickers, QualityPicker population,
    //and the link-drag flow's real temperature-aware RecipeMatchesKeyItem filter (task-3 report's risk-2
    //trace). ChooserPanelTests.cs already covers the shared IRChooserPanel base in isolation; this file
    //exercises the real subclasses plus GraphCanvasControl's wiring around them.
    public class ItemRecipeChooserFlowTests {
        private sealed class Fixture {
            public required DataCache Cache;
            public required SubgroupPrototype Subgroup;
            public required IQuality Quality;
            public required AssemblerPrototype Assembler;
            public required GraphCanvasControl Control;
            public required AvaloniaWindow Window;
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static Fixture NewFixture() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var group = new GroupPrototype(cache, "logistics", "Logistics", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "sg", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var assembler = new AssemblerPrototype(cache, "asm", "Assembler", EntityType.Assembler, EnergySource.Electric) { Available = true };

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 700);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = quality;
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 700 };
            window.Show();

            return new Fixture { Cache = cache, Subgroup = subgroup, Quality = quality, Assembler = assembler, Control = control, Window = window };
        }

        private static ItemPrototype NewItem(Fixture fx, string name) {
            var item = new ItemPrototype(fx.Cache, name, name, fx.Subgroup, "a") { Available = true };
            Store(fx.Cache).Items[name] = item;
            return item;
        }

        private static FluidPrototype NewFluid(Fixture fx, string name, double defaultTemperature) {
            var fluid = new FluidPrototype(fx.Cache, name, name, fx.Subgroup, "a") { Available = true, IsTemperatureDependent = true, DefaultTemperature = defaultTemperature };
            Store(fx.Cache).Items[name] = fluid;
            return fluid;
        }

        private static RecipePrototype NewRecipe(Fixture fx, string name, ItemPrototype? ingredient, ItemPrototype? product,
            double ingredientMinTemp = double.NaN, double ingredientMaxTemp = double.NaN, double productTemp = double.NaN) {
            var recipe = new RecipePrototype(fx.Cache, name, name, fx.Subgroup, "a") { Available = true };
            if (ingredient is not null) {
                recipe.InternalOneWayAddIngredient(ingredient, 1, ingredientMinTemp, ingredientMaxTemp);
                ingredient.ConsumptionRecipesInternal.Add(recipe);
            }
            if (product is not null) {
                recipe.InternalOneWayAddProduct(product, 1, 0, productTemp);
                product.ProductionRecipesInternal.Add(recipe);
            }
            recipe.AssemblersInternal.Add(fx.Assembler);
            fx.Assembler.RecipesInternal.Add(recipe);
            Store(fx.Cache).Recipes[name] = recipe;
            return recipe;
        }

        //Unlike NewRecipe, doesn't auto-add fx.Assembler - the assembler/fuel auto-selection tests need exact
        //control over which assemblers a recipe carries (docs/panels-reference.md §8's ApplyAssemblerFuelAutoSelection).
        private static RecipePrototype NewBareRecipe(Fixture fx, string name, ItemPrototype? ingredient, ItemPrototype? product) {
            var recipe = new RecipePrototype(fx.Cache, name, name, fx.Subgroup, "a") { Available = true };
            if (ingredient is not null) {
                recipe.InternalOneWayAddIngredient(ingredient, 1);
                ingredient.ConsumptionRecipesInternal.Add(recipe);
            }
            if (product is not null) {
                recipe.InternalOneWayAddProduct(product, 1, 0);
                product.ProductionRecipesInternal.Add(recipe);
            }
            Store(fx.Cache).Recipes[name] = recipe;
            return recipe;
        }

        private static void AddAssembler(RecipePrototype recipe, AssemblerPrototype assembler) {
            recipe.AssemblersInternal.Add(assembler);
            assembler.RecipesInternal.Add(recipe);
        }

        private static void WireSpoil(ItemPrototype fresh, ItemPrototype spoiled) {
            fresh.SpoilResult = spoiled;
            spoiled.SpoilOriginsInternal.Add(fresh);
        }

        private static void WirePlant(Fixture fx, ItemPrototype seed, ItemPrototype grown, string processName) {
            var process = new PlantProcessPrototype(fx.Cache, processName) { Seed = seed };
            process.InternalOneWayAddProduct(grown, 1);
            seed.PlantResult = process;
            grown.PlantOriginsInternal.Add(seed);
        }

        //IRChooserPanel.startingGroup is process-wide static (docs/panels-reference.md §2) - reset per test.
        public ItemRecipeChooserFlowTests() {
            FieldInfo field = typeof(IRChooserPanel).GetField("startingGroup", BindingFlags.Static | BindingFlags.NonPublic)!;
            field.SetValue(null, null);
        }

        private static void Click(Control control, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) {
            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            PointerUpdateKind updateKind = button == MouseButton.Left ? PointerUpdateKind.LeftButtonReleased : PointerUpdateKind.RightButtonReleased;
            var properties = new PointerPointProperties(RawInputModifiers.None, updateKind);
            var args = new PointerReleasedEventArgs(control, pointer, control, default, 0, properties, modifiers, button);
            control.RaiseEvent(args);
        }

        private static IconButton FindCell(Control panel, IDataObjectBase target) =>
            panel.GetVisualDescendants().OfType<IconButton>().First(b => Equals(b.DataObject, target));

        private static Button FindFooterButton(Control panel, string caption) =>
            panel.GetVisualDescendants().OfType<Button>().First(b => b.Content is string s && s == caption);

        //Opens Add Item's ItemChooserPanel, picks the named item, and returns the resulting stage-2
        //RecipeChooserPanel (reference §2/§8's two-stage flow) - shared by every footer-button/plain-recipe
        //test below.
        private static RecipeChooserPanel OpenTwoStageRecipeChooser(Fixture fx, ItemPrototype item, Point graphPoint) {
            fx.Control.AddItemAsync(graphPoint);
            var itemPanel = Assert.IsType<ItemChooserPanel>(fx.Control.FloatingPanelHost.Content);
            IconButton cell = FindCell(itemPanel, item);
            Click(cell);
            return Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
        }

        //---- QualityPicker population (task 2's flagged-unverified item) ----

        [AvaloniaFact]
        public void ItemChooserPanel_QualityPicker_PopulatesFromAvailableEnabledQualities() {
            Fixture fx = NewFixture();
            var epic = new QualityPrototype(fx.Cache, "epic", "Epic", "b") { Available = true };
            Store(fx.Cache).Qualities[epic.Name] = epic;
            var disabledQuality = new QualityPrototype(fx.Cache, "legendary", "Legendary", "c") { Available = true, Enabled = false };
            Store(fx.Cache).Qualities[disabledQuality.Name] = disabledQuality;

            var panel = new ItemChooserPanel(fx.Cache, new AppSettings());
            panel.Initialize();

            var picker = panel.GetVisualDescendants().OfType<QualityPicker>().Single();
            Assert.True(picker.IsVisible);
            List<string> shown = [.. picker.Selector.ItemsSource!.Cast<string>()];
            Assert.Equal(["Normal", "Epic"], shown);
            Assert.True(picker.Selector.IsEnabled);
        }

        //---- RISK-2: RecipeMatchesKeyItem's fluid temperature-range clauses ----

        [AvaloniaFact]
        public void RecipeChooserPanel_TemperatureFilter_ExcludesOutOfRange_IncludesInRange() {
            Fixture fx = NewFixture();
            FluidPrototype steam = NewFluid(fx, "steam", defaultTemperature: 165);
            RecipePrototype compatible = NewRecipe(fx, "turbine-compatible", ingredient: steam, product: null, ingredientMinTemp: 100, ingredientMaxTemp: 200);
            RecipePrototype incompatible = NewRecipe(fx, "turbine-incompatible", ingredient: steam, product: null, ingredientMinTemp: 500, ingredientMaxTemp: 1000);

            var keyItem = new ItemQualityPair(steam, fx.Quality);
            var tempRange = new FRange(165, 165, false); //steam arriving at exactly its default temperature
            var panel = new RecipeChooserPanel(fx.Cache, new AppSettings(), keyItem, tempRange, NewNodeType.Consumer);
            panel.Initialize();

            List<IDataObjectBase?> populated = [.. panel.GetVisualDescendants().OfType<IconButton>().Select(b => b.DataObject)];
            Assert.Contains(compatible, populated);
            Assert.DoesNotContain(incompatible, populated);
        }

        [AvaloniaFact]
        public void RecipeChooserPanel_TemperatureIgnoreRange_IncludesEveryMatchingIngredientRecipe() {
            Fixture fx = NewFixture();
            FluidPrototype steam = NewFluid(fx, "steam", defaultTemperature: 165);
            RecipePrototype anyTemp = NewRecipe(fx, "turbine-any", ingredient: steam, product: null, ingredientMinTemp: 500, ingredientMaxTemp: 1000);

            var keyItem = new ItemQualityPair(steam, fx.Quality);
            var panel = new RecipeChooserPanel(fx.Cache, new AppSettings(), keyItem, new FRange(0, 0, true), NewNodeType.Disconnected);
            panel.Initialize();

            List<IDataObjectBase?> populated = [.. panel.GetVisualDescendants().OfType<IconButton>().Select(b => b.DataObject)];
            Assert.Contains(anyTemp, populated);
        }

        //---- link-drag flow: the real LinkChecker-derived range replaces task 7's producers/consumers approximation ----

        [AvaloniaFact]
        public void HandleNewNodeRequestedAsync_TemperatureDependentFluid_FiltersChooserByLinkCheckerRange() {
            Fixture fx = NewFixture();
            FluidPrototype steam = NewFluid(fx, "steam", defaultTemperature: 165);
            RecipePrototype originRecipe = NewRecipe(fx, "produce-steam", ingredient: null, product: steam, productTemp: 165);
            RecipePrototype compatible = NewRecipe(fx, "turbine-compatible", ingredient: steam, product: null, ingredientMinTemp: 100, ingredientMaxTemp: 200);
            RecipePrototype incompatible = NewRecipe(fx, "turbine-incompatible", ingredient: steam, product: null, ingredientMinTemp: 500, ingredientMaxTemp: 1000);

            NodeId originId = fx.Control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(originRecipe, fx.Quality), new Point(0, 0));
            BaseNodeElement originElement = fx.Control.Viewer.NodeElementDictionary[originId];

            var request = new NewNodeLinkDragRequest(NewNodeType.Consumer, new ItemQualityPair(steam, fx.Quality), default(AvaloniaPoint), new Point(200, 0), originElement, NodeDirection.Up);
            fx.Control.NewNodeRequested!.Invoke(request);

            var panel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            List<IDataObjectBase?> populated = [.. panel.GetVisualDescendants().OfType<IconButton>().Select(b => b.DataObject)];
            Assert.Contains(compatible, populated);
            Assert.DoesNotContain(incompatible, populated);
        }

        //---- link-drag tab-level Y-offset (review finding 2): upstream FinalizeNodePosition's
        //offsetLocationToItemTabLevel, reference §3 ----

        //Consumer + Up: yoffset = -Height/2 * 1 = -Height/2 (upstream ProductionGraphViewer.cs:413-414) - the
        //new node's center lands half its height ABOVE the drop point, so its top-edge input tab (an Up node's
        //input tabs sit at -Height/2, per BaseNodeElement.UpdateTabOrder) reaches the drop point instead.
        [AvaloniaFact]
        public void HandleNewNodeRequestedAsync_ConsumerUp_OffsetsNodeLocationToTabLevel() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget-tab-a");
            RecipePrototype originRecipe = NewRecipe(fx, "produce-widget-tab-a", ingredient: null, product: widget);
            RecipePrototype consumerRecipe = NewRecipe(fx, "consume-widget-tab-a", ingredient: widget, product: null);

            NodeId originId = fx.Control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(originRecipe, fx.Quality), new Point(0, 0));
            BaseNodeElement originElement = fx.Control.Viewer.NodeElementDictionary[originId];
            var dropPoint = new Point(200, 0);

            var request = new NewNodeLinkDragRequest(NewNodeType.Consumer, new ItemQualityPair(widget, fx.Quality), default(AvaloniaPoint), dropPoint, originElement, NodeDirection.Up);
            fx.Control.NewNodeRequested!.Invoke(request);
            var panel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Click(FindCell(panel, consumerRecipe));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements, n => n != originElement);
            Point expected = new(dropPoint.X, dropPoint.Y - created.Height / 2);
            Assert.Equal(expected, created.ViewModel.Location);
        }

        //Supplier + Up: yoffset = +Height/2 * 1 = +Height/2 - the opposite sign from Consumer+Up above, so
        //the new node's center lands half its height BELOW the drop point instead (an Up node's output tabs
        //sit at +Height/2), exercising both the NodeType branch and confirming it isn't a fixed-sign bug.
        [AvaloniaFact]
        public void HandleNewNodeRequestedAsync_SupplierUp_OffsetsNodeLocationToTabLevel_OppositeSign() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget-tab-b");
            RecipePrototype originRecipe = NewRecipe(fx, "consume-widget-tab-b", ingredient: widget, product: null);
            RecipePrototype supplierRecipe = NewRecipe(fx, "produce-widget-tab-b", ingredient: null, product: widget);

            NodeId originId = fx.Control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(originRecipe, fx.Quality), new Point(0, 0));
            BaseNodeElement originElement = fx.Control.Viewer.NodeElementDictionary[originId];
            var dropPoint = new Point(200, 0);

            var request = new NewNodeLinkDragRequest(NewNodeType.Supplier, new ItemQualityPair(widget, fx.Quality), default(AvaloniaPoint), dropPoint, originElement, NodeDirection.Up);
            fx.Control.NewNodeRequested!.Invoke(request);
            var panel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Click(FindCell(panel, supplierRecipe));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements, n => n != originElement);
            Point expected = new(dropPoint.X, dropPoint.Y + created.Height / 2);
            Assert.Equal(expected, created.ViewModel.Location);
        }

        //Add Item's two-stage flow never sets OffsetLocationToItemTabLevel (upstream leaves it false outside
        //the two DraggedLinkElement call sites) - the created node stays centered on the drop point exactly
        //like every other test in this file already assumes, confirmed explicitly here against finding 2.
        [AvaloniaFact]
        public void AddItemAsync_TwoStageFlow_DoesNotOffsetNodeLocation() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget-tab-c");
            NewRecipe(fx, "recipe-produce-widget-tab-c", ingredient: null, product: widget);
            var graphPoint = new Point(50, 50);

            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, widget, graphPoint);
            Click(FindFooterButton(panel, "Source"));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.Equal(graphPoint, created.ViewModel.Location);
        }

        //---- two-stage AddItem flow: footer alt-node buttons + plain recipe click, one per NodeType ----

        [AvaloniaFact]
        public void TwoStageFlow_SourceButton_CreatesSupplierNode() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget");
            NewRecipe(fx, "recipe-produce-widget", ingredient: null, product: widget);
            NewRecipe(fx, "recipe-consume-widget", ingredient: widget, product: null);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, widget, new Point(50, 50));

            Click(FindFooterButton(panel, "Source"));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsType<SupplierNodeElement>(created);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void TwoStageFlow_OutputButton_CreatesConsumerNode() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget");
            NewRecipe(fx, "recipe-produce-widget", ingredient: null, product: widget);
            NewRecipe(fx, "recipe-consume-widget", ingredient: widget, product: null);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, widget, new Point(50, 50));

            Click(FindFooterButton(panel, "Output"));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsType<ConsumerNodeElement>(created);
        }

        [AvaloniaFact]
        public void TwoStageFlow_PassThroughButton_CreatesPassthroughNode() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget");
            NewRecipe(fx, "recipe-produce-widget", ingredient: null, product: widget);
            NewRecipe(fx, "recipe-consume-widget", ingredient: widget, product: null);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, widget, new Point(50, 50));

            Click(FindFooterButton(panel, "Pass-Through"));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsType<PassthroughNodeElement>(created);
        }

        [AvaloniaFact]
        public void TwoStageFlow_PlainRecipeClick_CreatesRecipeNode() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget");
            RecipePrototype recipe = NewRecipe(fx, "recipe-produce-widget", ingredient: null, product: widget);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, widget, new Point(50, 50));

            Click(FindCell(panel, recipe));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsType<RecipeNodeElement>(created);
        }

        //---- ApplyAssemblerFuelAutoSelection (review finding 1): upstream ProcessNodeRequest's branchy
        //assembler/fuel auto-pick for a plain recipe click, reference §8 ----

        //KeyItem (coal) is neither ingredient nor product of the picked recipe, only a valid fuel for one of
        //its two assemblers - proves auto-selection picks the burner assembler specifically (not just "the
        //first assembler" or the electric one) and sets Fuel to the picked item.
        [AvaloniaFact]
        public void TwoStageFlow_PlainRecipeClick_ItemOnlyValidAsFuel_AutoSelectsBurnerAssemblerAndFuel() {
            Fixture fx = NewFixture();
            ItemPrototype coal = NewItem(fx, "coal");
            NewRecipe(fx, "recipe-produce-coal", ingredient: null, product: coal);
            ItemPrototype ore = NewItem(fx, "ore");
            ItemPrototype ironPlate = NewItem(fx, "iron-plate-smelt");
            RecipePrototype smelt = NewBareRecipe(fx, "smelt-iron", ingredient: ore, product: ironPlate);
            var electricFurnace = new AssemblerPrototype(fx.Cache, "furnace-electric", "Electric Furnace", EntityType.Assembler, EnergySource.Electric) { Available = true };
            var burnerFurnace = new AssemblerPrototype(fx.Cache, "furnace-burner", "Burner Furnace", EntityType.Assembler, EnergySource.Burner) { Available = true };
            burnerFurnace.FuelsInternal.Add(coal);
            coal.FuelsEntitiesInternal.Add(burnerFurnace);
            AddAssembler(smelt, electricFurnace);
            AddAssembler(smelt, burnerFurnace);

            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, coal, new Point(50, 50));
            Click(FindCell(panel, smelt));

            RecipeNodeElement created = Assert.IsType<RecipeNodeElement>(Assert.Single(fx.Control.Viewer.NodeElements));
            var vm = (IRecipeNodeViewModel)created.ViewModel;
            Assert.Same(burnerFurnace, vm.SelectedAssembler.Assembler);
            Assert.Same(coal, vm.Fuel);
        }

        //KeyItem (iron-plate) is literally the picked recipe's own product - auto-selection has nothing to do
        //(the item is already part of the recipe), so the recipe's single electric assembler stays untouched
        //and no fuel gets forced. Discriminates against a bug that would auto-pick/force a fuel regardless of
        //whether the recipe actually needed the key item wired in as fuel.
        [AvaloniaFact]
        public void TwoStageFlow_PlainRecipeClick_ItemAlreadyInRecipe_LeavesElectricAssemblerAndNoFuel() {
            Fixture fx = NewFixture();
            ItemPrototype ore = NewItem(fx, "ore-b");
            ItemPrototype ironPlate = NewItem(fx, "iron-plate-smelt-b");
            RecipePrototype smelt = NewBareRecipe(fx, "smelt-iron-b", ingredient: ore, product: ironPlate);
            var electricFurnace = new AssemblerPrototype(fx.Cache, "furnace-electric-b", "Electric Furnace", EntityType.Assembler, EnergySource.Electric) { Available = true };
            AddAssembler(smelt, electricFurnace);

            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, ironPlate, new Point(50, 50));
            Click(FindCell(panel, smelt));

            RecipeNodeElement created = Assert.IsType<RecipeNodeElement>(Assert.Single(fx.Control.Viewer.NodeElements));
            var vm = (IRecipeNodeViewModel)created.ViewModel;
            Assert.Same(electricFurnace, vm.SelectedAssembler.Assembler);
            Assert.Null(vm.Fuel);
        }

        [AvaloniaFact]
        public void TwoStageFlow_SpoilButton_CreatesSpoilNode_UpDirection() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget");
            ItemPrototype spoiled = NewItem(fx, "widget-spoiled");
            WireSpoil(widget, spoiled);
            NewRecipe(fx, "recipe-produce-widget", ingredient: null, product: widget);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, widget, new Point(50, 50));

            Click(FindFooterButton(panel, "Spoil"));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsAssignableFrom<ISpoilNodeViewModel>(created.ViewModel);
        }

        [AvaloniaFact]
        public void TwoStageFlow_UnspoilButton_SingleOrigin_CreatesSpoilNode_DownDirection() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget");
            ItemPrototype fresh = NewItem(fx, "widget-fresh");
            WireSpoil(fresh, widget);
            NewRecipe(fx, "recipe-consume-widget", ingredient: widget, product: null);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, widget, new Point(50, 50));

            Click(FindFooterButton(panel, "UnSpoil"));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsAssignableFrom<ISpoilNodeViewModel>(created.ViewModel);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void TwoStageFlow_UnspoilButton_MultiOrigin_OpensItemSubPicker_ThenCreatesSpoilNode() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "multi-widget");
            ItemPrototype freshA = NewItem(fx, "fresh-a");
            ItemPrototype freshB = NewItem(fx, "fresh-b");
            WireSpoil(freshA, widget);
            WireSpoil(freshB, widget);
            NewRecipe(fx, "recipe-consume-multi-widget", ingredient: widget, product: null);
            NewRecipe(fx, "recipe-produce-fresh-a", ingredient: null, product: freshA);
            NewRecipe(fx, "recipe-produce-fresh-b", ingredient: null, product: freshB);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, widget, new Point(50, 50));

            Click(FindFooterButton(panel, "UnSpoil"));

            var subPicker = Assert.IsType<ItemChooserPanel>(fx.Control.FloatingPanelHost.Content);
            List<IDataObjectBase?> offered = [.. subPicker.GetVisualDescendants().OfType<IconButton>().Select(b => b.DataObject)];
            Assert.Contains(freshA, offered);
            Assert.Contains(freshB, offered);
            Assert.Empty(fx.Control.Viewer.NodeElements); //no node created yet - still mid sub-pick

            Click(FindCell(subPicker, freshA));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsAssignableFrom<ISpoilNodeViewModel>(created.ViewModel);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void TwoStageFlow_PlantButton_CreatesPlantNode_UpDirection() {
            Fixture fx = NewFixture();
            ItemPrototype seed = NewItem(fx, "seed");
            ItemPrototype grown = NewItem(fx, "grown");
            WirePlant(fx, seed, grown, "grow-process");
            NewRecipe(fx, "recipe-produce-seed", ingredient: null, product: seed);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, seed, new Point(50, 50));

            Click(FindFooterButton(panel, "Plant"));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsAssignableFrom<IPlantNodeViewModel>(created.ViewModel);
        }

        [AvaloniaFact]
        public void TwoStageFlow_UnplantButton_MultiOrigin_OpensItemSubPicker_ThenCreatesPlantNode() {
            Fixture fx = NewFixture();
            ItemPrototype grown = NewItem(fx, "multi-grown");
            ItemPrototype seedA = NewItem(fx, "seed-a");
            ItemPrototype seedB = NewItem(fx, "seed-b");
            WirePlant(fx, seedA, grown, "grow-a");
            WirePlant(fx, seedB, grown, "grow-b");
            NewRecipe(fx, "recipe-consume-multi-grown", ingredient: grown, product: null);
            NewRecipe(fx, "recipe-produce-seed-a", ingredient: null, product: seedA);
            NewRecipe(fx, "recipe-produce-seed-b", ingredient: null, product: seedB);
            RecipeChooserPanel panel = OpenTwoStageRecipeChooser(fx, grown, new Point(50, 50));

            Click(FindFooterButton(panel, "UnPlant"));

            var subPicker = Assert.IsType<ItemChooserPanel>(fx.Control.FloatingPanelHost.Content);
            List<IDataObjectBase?> offered = [.. subPicker.GetVisualDescendants().OfType<IconButton>().Select(b => b.DataObject)];
            Assert.Contains(seedA, offered);
            Assert.Contains(seedB, offered);

            Click(FindCell(subPicker, seedA));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsAssignableFrom<IPlantNodeViewModel>(created.ViewModel);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        //---- AddRecipeAsync: single-stage, empty KeyItem, no footer buttons (reference §4a/§10) ----

        [AvaloniaFact]
        public void AddRecipeAsync_OpensRecipeChooserWithNoKeyItem_FooterButtonsHidden() {
            Fixture fx = NewFixture();
            ItemPrototype widget = NewItem(fx, "widget");
            RecipePrototype recipe = NewRecipe(fx, "recipe-produce-widget", ingredient: null, product: widget);

            fx.Control.AddRecipeAsync(new Point(0, 0));

            var panel = Assert.IsType<RecipeChooserPanel>(fx.Control.FloatingPanelHost.Content);
            Assert.Contains(recipe, panel.GetVisualDescendants().OfType<IconButton>().Select(b => b.DataObject));
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), b => b.Content is string s && s == "Source" && b.IsVisible);

            Click(FindCell(panel, recipe));

            BaseNodeElement created = Assert.Single(fx.Control.Viewer.NodeElements);
            Assert.IsType<RecipeNodeElement>(created);
        }

        //---- placeholder fully gone: no stub seam, no type left in the assembly to route through ----

        [Fact]
        public void PlaceholderChooserWindow_NoLongerExistsInTheBuiltAssembly() {
            Assert.Null(typeof(GraphCanvasControl).Assembly.GetType("Foreman.Mac.Views.PlaceholderChooserWindow"));
            Assert.Null(typeof(GraphCanvasControl).GetProperty("ChooserDialogStub", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
            Assert.Null(typeof(GraphCanvasControl).GetMethod("ShowChooserAsync", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        }

        //---- task deliverable: offscreen render of the real RecipeChooserPanel filtered to a key item ----

        [AvaloniaFact]
        public async Task Render_RecipeChooserPanel_FilteredToKeyItem_ProducesNonEmptyPngInSddWorkspace() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset("Factorio 2.0 Vanilla", true, true), new Progress<KeyValuePair<int, string>>());
            IItem keyItem = cache.Items["iron-plate"];
            var keyItemPair = new ItemQualityPair(keyItem, cache.DefaultQuality!);

            var panel = new RecipeChooserPanel(cache, new AppSettings(), keyItemPair, new FRange(0, 0, true), NewNodeType.Disconnected);
            panel.Initialize();
            Assert.NotEmpty(panel.GetVisualDescendants().OfType<IconButton>().Select(b => b.DataObject).OfType<IDataObjectBase>());

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 700);
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 700 };
            window.Show();
            control.FloatingPanelHost.Show(panel, new Point(50, 50));

            string sddDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                ".superpowers", "sdd", "2026-09-02-phase5a-floating-panels");
            Directory.CreateDirectory(sddDir);
            string outPath = Path.Combine(sddDir, "task-3-recipe-chooser-keyitem-render.png");

            using SKSurface surface = SKSurface.Create(new SKImageInfo(900, 700));
            panel.RenderOffscreen(surface.Canvas, 900, 700);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(outPath, data.ToArray());

            Assert.True(new FileInfo(outPath).Length > 0);
        }
    }
}
