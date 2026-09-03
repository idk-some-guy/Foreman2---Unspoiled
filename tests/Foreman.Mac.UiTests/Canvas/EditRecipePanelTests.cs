using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Panels;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaVisual = Avalonia.Visual;
using AvaloniaWindow = Avalonia.Controls.Window;
using InputPointer = Avalonia.Input.Pointer;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises Task 5's real EditRecipePanel (docs/panels-reference.md §3): assembler/fuel/beacon/module
    //pickers, the exact per-field re-solve map (RISK 1 - among the three beacon fields only BeaconCountInput
    //re-solves, KeyNodeTitleInput never re-solves), quality-selector option-list rebuilds, and the
    //always-visible stat readout card. Assembler-picker ordering is asserted against the recipe's own
    //Assemblers enumeration order rather than AssemblerSelector - that class only governs auto-selection at
    //node-creation time (RecipeNode.AutoSetAssembler); upstream's own SetupAssemblerOptions enumerates
    //baseRecipe.Assemblers.Where(a => a.Enabled) with no sorting pass at all.
    public class EditRecipePanelTests {
        private sealed class Fixture {
            public required DataCache Cache;
            public required IQuality Normal;
            public required IQuality Legendary;
            public required SubgroupPrototype Subgroup;
            public required GraphCanvasControl Control;
            public required AvaloniaWindow Window;
        }

        private sealed class Oracle {
            private readonly BaseNodeController supplierController;
            private readonly INodeViewModel consumer;
            private double staleValue;

            public Oracle(BaseNodeController supplierController, INodeViewModel consumer) {
                this.supplierController = supplierController;
                this.consumer = consumer;
            }

            public void GoStale(double value) {
                staleValue = value;
                supplierController.SetDesiredSetValue(value); //diverges DesiredSetValue from ActualSetValue without resolving
            }

            public bool Resolved => Math.Abs(consumer.ActualSetValue - staleValue) < 0.0001;
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static Fixture NewFixture() {
            var cache = new DataCache(filterRecipes: true);
            var normal = new QualityPrototype(cache, "normal", "Normal", "a");
            var legendary = new QualityPrototype(cache, "legendary", "Legendary", "b") { Level = 1 };
            DataCacheStore store = Store(cache);
            store.Qualities[normal.Name] = normal;
            store.Qualities[legendary.Name] = legendary;
            store.DefaultQuality = normal;

            var group = new GroupPrototype(cache, "production", "Production", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "production-sub", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 900);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = normal;
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 900 };
            window.Show();

            return new Fixture { Cache = cache, Normal = normal, Legendary = legendary, Subgroup = subgroup, Control = control, Window = window };
        }

        private static ItemPrototype NewItem(Fixture fx, string name) {
            var item = new ItemPrototype(fx.Cache, name, name, fx.Subgroup, "a") { Available = true };
            Store(fx.Cache).Items[name] = item;
            return item;
        }

        private static ModulePrototype NewModule(Fixture fx, string name, double speedBonus = 0, double consumptionBonus = 0, double productivityBonus = 0) {
            NewItem(fx, name); //ModulePrototype.Available derives from an Item of the same name
            var module = new ModulePrototype(fx.Cache, name, name) {
                SpeedBonus = speedBonus,
                ConsumptionBonus = consumptionBonus,
                ProductivityBonus = productivityBonus,
                Category = "production",
            };
            Store(fx.Cache).Modules[name] = module;
            return module;
        }

        private static AssemblerPrototype NewAssembler(Fixture fx, string name, EnergySource source, EntityType type = EntityType.Assembler, bool enabled = true) =>
            new(fx.Cache, name, name, type, source) { Available = true, Enabled = enabled };

        private static BeaconPrototype NewBeacon(Fixture fx, string name) {
            var beacon = new BeaconPrototype(fx.Cache, name, name, EnergySource.Electric) { Available = true, ModuleSlots = 2 };
            Store(fx.Cache).Beacons[name] = beacon;
            return beacon;
        }

        private static RecipePrototype NewRecipe(Fixture fx, string name, ItemPrototype ingredient, ItemPrototype product, double time = 2.0) {
            var recipe = new RecipePrototype(fx.Cache, name, name, fx.Subgroup, "a") { Available = true, Time = time };
            recipe.InternalOneWayAddIngredient(ingredient, 1);
            ingredient.ConsumptionRecipesInternal.Add(recipe);
            recipe.InternalOneWayAddProduct(product, 1, 0);
            product.ProductionRecipesInternal.Add(recipe);
            Store(fx.Cache).Recipes[name] = recipe;
            return recipe;
        }

        private static void LinkAssembler(RecipePrototype recipe, AssemblerPrototype assembler) {
            recipe.AssemblersInternal.Add(assembler);
            assembler.RecipesInternal.Add(recipe);
        }

        private static IRecipeNodeViewModel CreateRecipeNode(Fixture fx, RecipePrototype recipe, IQuality quality, Point location) {
            NodeId id = fx.Control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, quality), location);
            Assert.True(fx.Control.Viewer.Session.View.TryGetNode(id, out INodeViewModel? node));
            return (IRecipeNodeViewModel)node!;
        }

        private static RecipeNodeController ControllerFor(Fixture fx, INodeViewModel node) {
            if (fx.Control.Viewer.Session.Editor.RequestNodeController(node.Id) is not RecipeNodeController controller)
                throw new Xunit.Sdk.XunitException("Recipe node has no controller.");
            return controller;
        }

        private static Oracle NewOracle(Fixture fx) {
            ItemPrototype oracleItem = NewItem(fx, "oracle-item-" + Guid.NewGuid().ToString("N"));
            var pair = new ItemQualityPair(oracleItem, fx.Normal);
            NodeId supplierId = fx.Control.Viewer.Session.Editor.CreateSupplierNode(pair, new Point(-900, -900));
            NodeId consumerId = fx.Control.Viewer.Session.Editor.CreateConsumerNode(pair, new Point(-900, -800));
            fx.Control.Viewer.Session.Editor.CreateLink(supplierId, consumerId, pair);

            if (fx.Control.Viewer.Session.Editor.RequestNodeController(supplierId) is not BaseNodeController supplierController)
                throw new Xunit.Sdk.XunitException("Oracle supplier has no controller.");
            Assert.True(fx.Control.Viewer.Session.View.TryGetNode(consumerId, out INodeViewModel? consumer));

            supplierController.SetRateType(RateType.Manual);
            supplierController.SetDesiredSetValue(1);
            fx.Control.Viewer.Graph.UpdateNodeValues();

            return new Oracle(supplierController, consumer!);
        }

        private static void Click(Control control, MouseButton button = MouseButton.Left) {
            var pointer = new InputPointer(InputPointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            PointerUpdateKind updateKind = button == MouseButton.Left ? PointerUpdateKind.LeftButtonReleased : PointerUpdateKind.RightButtonReleased;
            var properties = new PointerPointProperties(RawInputModifiers.None, updateKind);
            var args = new PointerReleasedEventArgs(control, pointer, control, default, 0, properties, KeyModifiers.None, button);
            control.RaiseEvent(args);
        }

        //Standard fixture: a smeltable recipe with three candidate assemblers (electric, burner, disabled),
        //one assembler-and-beacon compatible module, and a beacon - covers the assembler/fuel/module/beacon
        //surface every field test below needs.
        private sealed class RecipeFixture {
            public required Fixture Fx;
            public required RecipePrototype Recipe;
            public required AssemblerPrototype Electric;
            public required AssemblerPrototype Burner;
            public required AssemblerPrototype Disabled;
            public required ModulePrototype Module;
            public required BeaconPrototype Beacon;
            public required ItemPrototype Coal;
        }

        private static RecipeFixture NewRecipeFixture() {
            Fixture fx = NewFixture();
            ItemPrototype ore = NewItem(fx, "ore");
            ItemPrototype plate = NewItem(fx, "plate");
            ItemPrototype coal = NewItem(fx, "coal");
            RecipePrototype recipe = NewRecipe(fx, "smelt", ore, plate, time: 2.0);

            AssemblerPrototype electric = NewAssembler(fx, "electric-assembler", EnergySource.Electric);
            electric.ModuleSlots = 2;
            electric.AllowModules = true;
            electric.AllowBeacons = true;
            electric.SpeedInternal[fx.Normal] = 2.0;
            electric.EnergyConsumptionInternal[fx.Normal] = 100.0;

            AssemblerPrototype burner = NewAssembler(fx, "burner-assembler", EnergySource.Burner);
            burner.FuelsInternal.Add(coal);

            //SetupFuelOptions only lists a fuel that's itself producible - give coal a production recipe.
            var mineCoal = new RecipePrototype(fx.Cache, "mine-coal", "Mine Coal", fx.Subgroup, "a") { Available = true };
            mineCoal.InternalOneWayAddProduct(coal, 1, 0);
            coal.ProductionRecipesInternal.Add(mineCoal);

            AssemblerPrototype disabled = NewAssembler(fx, "disabled-assembler", EnergySource.Electric, enabled: false);

            LinkAssembler(recipe, electric);
            LinkAssembler(recipe, burner);
            LinkAssembler(recipe, disabled);
            LinkAssembler(mineCoal, electric);
            Store(fx.Cache).Recipes[mineCoal.Name] = mineCoal;

            ModulePrototype module = NewModule(fx, "speed-module", speedBonus: 0.5, consumptionBonus: 0.2, productivityBonus: 0.1);
            electric.ModulesInternal.Add(module);
            recipe.AssemblerModulesInternal.Add(module);
            recipe.BeaconModulesInternal.Add(module);

            BeaconPrototype beacon = NewBeacon(fx, "beacon-1");
            beacon.ModulesInternal.Add(module);

            return new RecipeFixture { Fx = fx, Recipe = recipe, Electric = electric, Burner = burner, Disabled = disabled, Module = module, Beacon = beacon, Coal = coal };
        }

        //---- Rate row: Auto/Fixed toggle + fixed value (upstream L638-667) ----

        [AvaloniaFact]
        public void RateTypeToggle_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
            Assert.Equal(RateType.Auto, node.RateType);

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(13);
            Assert.False(oracle.Resolved);

            panel.FixedAssemblersOption.IsChecked = true;

            Assert.True(oracle.Resolved);
            Assert.Equal(RateType.Manual, node.RateType);
            Assert.True(panel.FixedAssemblerInput.IsEnabled);
        }

        [AvaloniaFact]
        public void FixedRateEdit_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(1);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Assert.True(panel.FixedAssemblersOption.IsChecked);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(27);
            Assert.False(oracle.Resolved);

            panel.FixedAssemblerInput.Value = 5m;

            Assert.True(oracle.Resolved);
            Assert.Equal(5, node.DesiredSetValue, 3);
        }

        //Upstream's changed-value gate (SetFixedRate: `if (nodeData.DesiredSetValue != (double)FixedAssemblerInput.Value)`)
        //means re-assigning the NumericUpDown's own current value is a no-op both in Avalonia (no property
        //change, so ValueChanged never fires) and in upstream's own guard if it somehow did - same observable
        //outcome either way: no resolve.
        [AvaloniaFact]
        public void FixedRateEdit_NoActualChange_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(5);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(31);
            Assert.False(oracle.Resolved);

            panel.FixedAssemblerInput.Value = panel.FixedAssemblerInput.Value;

            Assert.False(oracle.Resolved);
        }

        //---- Assembler picker: ordering / filtering ----

        [AvaloniaFact]
        public void AssemblerOptions_MatchRecipeAssemblersEnumeration_ExcludingDisabled() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);

            var expected = rf.Recipe.Assemblers.Where(a => a.Enabled).ToList();
            Assert.Equal(expected, panel.AssemblerOptions.Select(b => b.DataObject));
            Assert.DoesNotContain(rf.Disabled, panel.AssemblerOptions.Select(b => b.DataObject));
        }

        [AvaloniaFact]
        public void AssemblerOptions_ColorUnavailableAssemblerAsError() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Burner.Available = false;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);

            IconButton burnerButton = panel.AssemblerOptions.Single(b => Equals(b.DataObject, rf.Burner));
            Assert.Equal(Avalonia.Media.Colors.DarkRed, burnerButton.FillColor);
        }

        //---- Assembler change: resolves + rebuilds module options ----

        [AvaloniaFact]
        public void AssemblerChange_ResolvesAndRebuildsModuleOptions() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Assert.NotEmpty(panel.AModuleOptions); //electric-assembler allows the shared module

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(42);
            Assert.False(oracle.Resolved);

            IconButton burnerButton = panel.AssemblerOptions.Single(b => Equals(b.DataObject, rf.Burner));
            Click(burnerButton);

            Assert.True(oracle.Resolved);
            Assert.Equal(rf.Burner, node.SelectedAssembler.Assembler);
            Assert.Empty(panel.AModuleOptions); //burner-assembler doesn't allow modules
        }

        //---- Fuel picker: resolves ----

        [AvaloniaFact]
        public void FuelChange_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Burner, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Assert.Single(panel.FuelOptions);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(17);
            Assert.False(oracle.Resolved);

            Click(panel.FuelOptions[0]);

            Assert.True(oracle.Resolved);
            Assert.Equal(rf.Coal, node.Fuel);
        }

        //---- Beacon fields: RISK 1's exact per-field re-solve map ----

        [AvaloniaFact]
        public void BeaconCountEdit_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(11);
            Assert.False(oracle.Resolved);

            panel.BeaconCountInput.Value = 3m;

            Assert.True(oracle.Resolved);
            Assert.Equal(3, node.BeaconCount, 3);
        }

        [AvaloniaFact]
        public void BeaconsPerAssemblerEdit_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(11);
            Assert.False(oracle.Resolved);

            panel.BeaconsPerAssemblerInput.Value = 2m;

            Assert.False(oracle.Resolved);
            Assert.Equal(2, node.BeaconsPerAssembler, 3);
        }

        [AvaloniaFact]
        public void ConstantBeaconEdit_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(11);
            Assert.False(oracle.Resolved);

            panel.ConstantBeaconInput.Value = 4m;

            Assert.False(oracle.Resolved);
            Assert.Equal(4, node.BeaconsConst, 3);
        }

        [AvaloniaFact]
        public void BeaconButtonClick_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Assert.Single(panel.BeaconOptions);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(9);
            Assert.False(oracle.Resolved);

            Click(panel.BeaconOptions[0]);

            Assert.True(oracle.Resolved);
            Assert.Equal(rf.Beacon, node.SelectedBeacon.Beacon);
        }

        //---- Key node fields: never resolve ----

        [AvaloniaFact]
        public void KeyNodeTitleEdit_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetKeyNode(true);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(23);
            Assert.False(oracle.Resolved);

            panel.KeyNodeTitleInput.Text = "Renamed";

            Assert.False(oracle.Resolved);
            Assert.Equal("Renamed", node.KeyNodeTitle);
        }

        [AvaloniaFact]
        public void KeyNodeCheckboxToggle_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(23);
            Assert.False(oracle.Resolved);

            panel.KeyNodeCheckBox.IsChecked = true;

            Assert.False(oracle.Resolved);
            Assert.True(node.KeyNode);
            Assert.True(panel.KeyNodeTitleInput.IsVisible);
        }

        //---- Quality selector: rebuilds option lists, doesn't itself resolve ----

        [AvaloniaFact]
        public void QualitySelectorChange_RebuildsOptionListsButDoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal)); //so beacon module options are populated too
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            var priorAssemblerButtons = panel.AssemblerOptions.ToList();
            var priorAModuleButtons = panel.AModuleOptions.ToList();
            var priorBeaconButtons = panel.BeaconOptions.ToList();
            var priorBModuleButtons = panel.BModuleOptions.ToList();
            Assert.NotEmpty(priorAssemblerButtons);
            Assert.NotEmpty(priorAModuleButtons);
            Assert.NotEmpty(priorBeaconButtons);
            Assert.NotEmpty(priorBModuleButtons);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(31);
            Assert.False(oracle.Resolved);

            panel.QualitySelector.Selector.SelectedIndex = 1; //legendary

            Assert.False(oracle.Resolved);
            Assert.Equal(rf.Fx.Legendary, panel.QualitySelector.SelectedQuality);
            Assert.Equal(rf.Fx.Legendary, rf.Fx.Control.Viewer.Graph.DefaultAssemblerQuality);
            //Task 5 review rider: the brief named all three option lists as rebuilt on quality change -
            //assert each was actually replaced (new button instances), not just the assembler one.
            Assert.NotSame(priorAModuleButtons[0], panel.AModuleOptions[0]);
            Assert.NotSame(priorBeaconButtons[0], panel.BeaconOptions[0]);
            Assert.NotSame(priorBModuleButtons[0], panel.BModuleOptions[0]);
            Assert.NotSame(priorAssemblerButtons[0], panel.AssemblerOptions[0]); //rebuilt, not mutated in place
        }

        //---- Low priority / neighbour / extra productivity: resolve ----

        [AvaloniaFact]
        public void LowPriorityToggle_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(5);
            Assert.False(oracle.Resolved);

            panel.LowPriorityCheckBox.IsChecked = true;

            Assert.True(oracle.Resolved);
            Assert.True(node.LowPriority);
        }

        [AvaloniaFact]
        public void NeighbourBonusEdit_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            AssemblerPrototype reactor = NewAssembler(rf.Fx, "reactor", EnergySource.Electric, EntityType.Reactor);
            LinkAssembler(rf.Recipe, reactor);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(reactor, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Assert.True(panel.NeighbourInput.IsVisible); //reactor selected -> field shown

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(6);
            Assert.False(oracle.Resolved);

            panel.NeighbourInput.Value = 2m;

            Assert.True(oracle.Resolved);
            Assert.Equal(2, node.NeighbourCount, 3);
        }

        [AvaloniaFact]
        public void ExtraProductivityEdit_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Fx.Control.Viewer.Graph.EnableExtraProductivityForNonMiners = true; //makes the field visible-eligible per §3's gate
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(8);
            Assert.False(oracle.Resolved);

            panel.ExtraProductivityInput.Value = 20m;

            Assert.True(oracle.Resolved);
            Assert.Equal(0.2, node.ExtraProductivity, 3);
        }

        //---- Module slots: add/remove respects assembler slot count ----

        [AvaloniaFact]
        public void ModuleOptionClick_AddsModule_UntilSlotsFull() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            Assert.Equal("Modules (0/2):", panel.AModulesLabel.Text);
            Assert.True(panel.AModuleOptions[0].IsEnabled);

            Click(panel.AModuleOptions[0]);
            Assert.Equal("Modules (1/2):", panel.AModulesLabel.Text);

            Click(panel.AModuleOptions[0]);
            Assert.Equal("Modules (2/2):", panel.AModulesLabel.Text);
            Assert.False(panel.AModuleOptions[0].IsEnabled); //slots full

            Click(panel.AssemblerModules[0]);
            Assert.Equal("Modules (1/2):", panel.AModulesLabel.Text);
        }

        //Live bug: the human filled an assembler's module slots (options correctly go gray), then removed
        //modules back down from full - the option cells stayed gray. UpdateAssemblerModules already
        //recomputes IsEnabled correctly on every add/remove (proven by ModuleOptionClick_AddsModule_UntilSlotsFull
        //above), so a property-only assertion can't see this bug. IconButton.Render() caches a composited
        //bitmap keyed off its own private dirty flag, and a bare `button.IsEnabled = ...` (as opposed to
        //SetPopulated/SetFillColor/SetEmpty) never marks that flag or calls InvalidateVisual - the cell's
        //painted frame goes stale even though the property underneath it is correct. LiveBitmapRebuildCount
        //observes whether the real Render() pass actually re-baked the frame.
        [AvaloniaFact]
        public void ModuleOptionClick_ViaRealClickPath_RepaintsWhenSlotsFillAndFree() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            IconButton option = panel.AModuleOptions[0];

            Click(option); //1/2
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Click(option); //2/2 - slots full, option should gray out
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.False(option.IsEnabled);
            int rebuildsAtFull = option.LiveBitmapRebuildCount;
            Assert.True(rebuildsAtFull > 0, "expected the option cell to have painted at least once by now.");

            Click(panel.AssemblerModules[0]); //remove one via the real equipped-cell gesture - back to 1/2
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            Assert.True(option.IsEnabled);
            Assert.True(option.LiveBitmapRebuildCount > rebuildsAtFull,
                "option cell re-enabled but never repainted - it stays visually gray.");
        }

        //Same staleness, beacon side: UpdateBeaconModules has the identical `button.IsEnabled = ...` pattern.
        [AvaloniaFact]
        public void BeaconModuleOptionClick_ViaRealClickPath_RepaintsWhenSlotsFillAndFree() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            IconButton option = panel.BModuleOptions[0];

            Click(option); //1/2
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Click(option); //2/2 - slots full
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.False(option.IsEnabled);
            int rebuildsAtFull = option.LiveBitmapRebuildCount;
            Assert.True(rebuildsAtFull > 0, "expected the option cell to have painted at least once by now.");

            Click(panel.BeaconModules[0]); //remove one via the real equipped-cell gesture - back to 1/2
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            Assert.True(option.IsEnabled);
            Assert.True(option.LiveBitmapRebuildCount > rebuildsAtFull,
                "option cell re-enabled but never repainted - it stays visually gray.");
        }

        //Side-nit from the same live report: a hover tooltip that didn't seem to update. IconButton.SetPopulated
        //already calls ToolTip.SetTip(this, populated.FriendlyName) per cell at creation (upstream's own
        //hover text), so each module option cell carries its own module's name rather than a shared/stale one.
        //Pins that wiring rather than changing it.
        [AvaloniaFact]
        public void ModuleOptionCell_ShowsOwnModuleFriendlyNameAsTooltip() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);

            IconButton option = panel.AModuleOptions[0];
            Assert.Equal(rf.Module.FriendlyName, ToolTip.GetTip(option));
        }

        [AvaloniaFact]
        public void AssemblerWithNoModuleSlots_HidesModuleSection() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Burner, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);

            Assert.False(panel.AModulesLabel.IsVisible);
            Assert.False(panel.AModulesChoicePanel.IsVisible);
            Assert.Empty(panel.AModuleOptions);
        }

        //Live-gate finding 3: upstream's SelectedAModulesPanel is a fixed-size Panel (Designer.cs
        //Size(180, 109), AutoScroll) placed beside AModulesChoicePanel rather than above it - the equipped
        //row growing never moves the click target. Our port used to stack both in one vertical StackPanel,
        //so equipping a module grew the row above AModulesChoicePanel and pushed the very button the user
        //just clicked down the screen, defeating repeat-click-same-spot. Reproduces via the real
        //PointerReleased handler path (Click helper), not a direct controller call.
        [AvaloniaFact]
        public void ModuleOptionClick_ViaRealClickPath_DoesNotShiftClickTargetOrEnableRepeatClicksAtSameSpot() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            IconButton option = panel.AModuleOptions[0];
            Avalonia.Point before = AbsoluteTopLeft(option, panel);

            Click(option);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("Modules (1/2):", panel.AModulesLabel.Text);
            Assert.Equal(before, AbsoluteTopLeft(option, panel));

            //Second click at the exact same on-screen spot must land on the same button and add a second
            //module - the concrete "click twice, get two modules" proof from the live symptom report.
            Click(option);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("Modules (2/2):", panel.AModulesLabel.Text);
            Assert.Equal(before, AbsoluteTopLeft(option, panel));
        }

        //Same coordinate-accumulation technique as ChooserPanelTests.AbsoluteTopLeft: sums each ancestor's
        //own laid-out Bounds.X/Y from `descendant` up to (not including past) `root`.
        private static Avalonia.Point AbsoluteTopLeft(Avalonia.Visual descendant, Avalonia.Visual root) {
            double x = 0, y = 0;
            for (Avalonia.Visual? current = descendant; current is not null; current = current.GetVisualParent()) {
                x += current.Bounds.X;
                y += current.Bounds.Y;
                if (ReferenceEquals(current, root))
                    break;
            }
            return new Avalonia.Point(x, y);
        }

        //Review finding 1: upstream declares these labels' Font at 10pt (Designer.cs) - Bold for the
        //assembler/fuel/beacon titles, Regular for the section labels below the pickers, and no override at
        //all (ambient) for the plain stat-readout titles. Avalonia's FontSize is device-independent px at 96
        //DPI, so the point sizes need the 96/72 conversion instead of being left at the panel's ambient size.
        [AvaloniaFact]
        public void HeaderLabels_UseUpstreamsTenPointSize_WithUpstreamsBoldOrRegularWeight() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            const double expectedSize = 10.0 * 96.0 / 72.0;

            Assert.Equal(Avalonia.Media.FontWeight.Bold, panel.AssemblerTitle.FontWeight);
            Assert.Equal(expectedSize, panel.AssemblerTitle.FontSize, 3);
            Assert.Equal(Avalonia.Media.FontWeight.Bold, panel.BeaconTitle.FontWeight);
            Assert.Equal(expectedSize, panel.BeaconTitle.FontSize, 3);

            Assert.NotEqual(Avalonia.Media.FontWeight.Bold, panel.NeighboursLabel.FontWeight);
            Assert.Equal(expectedSize, panel.NeighboursLabel.FontSize, 3);
            Assert.NotEqual(Avalonia.Media.FontWeight.Bold, panel.AModulesLabel.FontWeight);
            Assert.Equal(expectedSize, panel.AModulesLabel.FontSize, 3);

            //Upstream sets no explicit Font at all here - stays at the panel's ambient size.
            Assert.NotEqual(Avalonia.Media.FontWeight.Bold, panel.AssemblerSpeedTitleLabel.FontWeight);
            Assert.NotEqual(expectedSize, panel.AssemblerSpeedTitleLabel.FontSize, 3);
        }

        //Review finding 2: upstream's AssemblerTable/BeaconTable/module rows place the picker grid beside its
        //stat readout and the equipped-module column beside the available-module column (Designer.cs row
        //layouts, panels-reference.md §3) rather than stacking every piece vertically. Proven structurally via
        //real laid-out Bounds (AbsoluteTopLeft, same technique as the finding-3 regression test above) rather
        //than by reading BuildLayout()'s source, so this can't pass by construction.
        [AvaloniaFact]
        public void AssemblerBeaconAndModuleSections_AreLaidOutSideBySide_NotStackedVertically() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            //Assembler picker (icon) beside the stat readout (Energy: is its first row).
            Avalonia.Point assemblerIconTop = AbsoluteTopLeft(panel.SelectedAssemblerIcon, panel);
            Avalonia.Point energyTop = AbsoluteTopLeft(panel.AssemblerEnergyLabel, panel);
            Assert.Equal(assemblerIconTop.Y, energyTop.Y, 1);
            Assert.True(energyTop.X > assemblerIconTop.X);

            //Beacon picker (icon) beside its stat readout (Energy: is its first row).
            Avalonia.Point beaconIconTop = AbsoluteTopLeft(panel.SelectedBeaconIcon, panel);
            Avalonia.Point beaconEnergyTop = AbsoluteTopLeft(panel.BeaconEnergyLabel, panel);
            Assert.Equal(beaconIconTop.Y, beaconEnergyTop.Y, 1);
            Assert.True(beaconEnergyTop.X > beaconIconTop.X);

            //Assembler modules: equipped-column label beside available-column label.
            Avalonia.Point aModulesTop = AbsoluteTopLeft(panel.AModulesLabel, panel);
            Avalonia.Point aModuleOptionsTop = AbsoluteTopLeft(panel.AModuleOptionsLabel, panel);
            Assert.Equal(aModulesTop.Y, aModuleOptionsTop.Y, 1);
            Assert.True(aModuleOptionsTop.X > aModulesTop.X);

            //Beacon modules: same pairing.
            Avalonia.Point bModulesTop = AbsoluteTopLeft(panel.BModulesLabel, panel);
            Avalonia.Point bModuleOptionsTop = AbsoluteTopLeft(panel.BModuleOptionsLabel, panel);
            Assert.Equal(bModulesTop.Y, bModuleOptionsTop.Y, 1);
            Assert.True(bModuleOptionsTop.X > bModulesTop.X);
        }

        //---- Stat readout: hand-derived from a known configuration ----

        [AvaloniaFact]
        public void StatReadout_MatchesHandDerivedValuesForKnownConfiguration() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);

            //By hand: assembler base speed 2.0, +50% from the one equipped module -> speed multiplier 1.5,
            //assembler speed = 2.0 * 1.5 = 3.0; recipe time 2.0s, rate multiplier 1 (Per1Sec default) ->
            //total crafts = 3.0 * 1 / 2.0 = 1.5. Energy: base 100W * (1 + 0.2 consumption bonus) = 120W.
            double expectedSpeedMultiplier = 1.5;
            double expectedConsumptionMultiplier = 1.2;
            double expectedProductivityMultiplier = 1.1; //base 0 + 0.1 module bonus + 1
            double expectedAssemblerSpeed = 3.0;
            double expectedEnergy = 120.0;

            Assert.Equal(expectedSpeedMultiplier.ToString("P0", DisplayCulture.Format), panel.AssemblerSpeedPercentLabel.Text);
            Assert.Equal(expectedConsumptionMultiplier.ToString("P0", DisplayCulture.Format), panel.AssemblerEnergyPercentLabel.Text);
            Assert.Equal(expectedProductivityMultiplier.ToString("P0", DisplayCulture.Format), panel.AssemblerProductivityPercentLabel.Text);
            Assert.Equal(GraphicsStuff.DoubleToEnergy(expectedEnergy, "W"), panel.AssemblerEnergyLabel.Text);
            Assert.Contains(expectedAssemblerSpeed.ToString("0.##", DisplayCulture.Format), panel.AssemblerSpeedLabel.Text);
            Assert.Contains("1.5 crafts", panel.AssemblerSpeedLabel.Text);
        }

        //Generator branch of UpdateAssemblerInfo (RecipeNode.Display.cs's GetGeneratorMinimumTemperature/
        //GetGeneratorMaximumTemperature/GetGeneratorEffectivity): mirrors the Reactor fixture's approach
        //above - a small standalone assembler+recipe pair on the shared fixture's cache, no preset needed.
        //An unconnected generator node has no input links, so GetGeneratorAverageTemperature's own
        //zero-inputs branch returns the assembler's OperationTemperature verbatim, making effectivity
        //deterministically 100% by construction (average temp == operation temp) - a clean hand-derivable case.
        [AvaloniaFact]
        public void GeneratorStatReadout_MatchesHandDerivedValues() {
            RecipeFixture rf = NewRecipeFixture();
            var steam = new FluidPrototype(rf.Fx.Cache, "steam", "Steam", rf.Fx.Subgroup, "a") { Available = true, IsTemperatureDependent = true, DefaultTemperature = 15 };
            Store(rf.Fx.Cache).Items[steam.Name] = steam;

            var generatorRecipe = new RecipePrototype(rf.Fx.Cache, "generate-power", "Generate Power", rf.Fx.Subgroup, "a") { Available = true };
            generatorRecipe.InternalOneWayAddIngredient(steam, 1, 100, 500); //ingredient temperature range: 100-500 c
            steam.ConsumptionRecipesInternal.Add(generatorRecipe);
            Store(rf.Fx.Cache).Recipes[generatorRecipe.Name] = generatorRecipe;

            AssemblerPrototype generator = NewAssembler(rf.Fx, "generator", EnergySource.Electric, EntityType.Generator);
            generator.OperationTemperature = 500;
            generator.EnergyProductionInternal[rf.Fx.Normal] = 1_000_000.0; //1 MW
            LinkAssembler(generatorRecipe, generator);

            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, generatorRecipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(generator, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);

            //By hand: min temp = max(steam.DefaultTemperature(15)+0.1, ingredient range min(100)) = 100;
            //max temp = ingredient range max = 500; unconnected node -> average temp == OperationTemperature
            //(500) -> effectivity = (500-15)/(500-15) = 1.0 = 100%; production = 1,000,000W * 1.0 = 1 MW.
            Assert.True(panel.GeneratorTemperatureLabel.IsVisible);
            Assert.True(panel.GeneratorTemperatureRangeLabel.IsVisible);
            Assert.Equal("100-500°c  (optimal: 500°c)", panel.GeneratorTemperatureRangeLabel.Text);
            Assert.Equal(1.0.ToString("P0", DisplayCulture.Format), panel.AssemblerEnergyPercentLabel.Text);
            Assert.Equal(GraphicsStuff.DoubleToEnergy(1_000_000.0, "W"), panel.AssemblerEnergyLabel.Text);
        }

        //Item 11 (final fix wave): FloatingPanelHost.Reposition (via EditPanelViewportLayout.Apply) pins the
        //panel's Width/Height to fixed pixel values sized for whatever viewport last hosted it live. Task
        //5's original render test never noticed because it rendered at the same 900x900 viewport it showed
        //the panel at; a render at any other size left those old fixed values in place, and Stretch alignment
        //centers the resulting oversized panel around the smaller render rect - a negative Bounds.X/Y - so
        //rows further down the panel painted shifted from where the recipe info card at the top painted (the
        //"~110px left shift" the review found in task-5-recipe-panel-render.png).
        [AvaloniaFact]
        public void RenderOffscreen_AtSizeSmallerThanLastRealShow_PaintsFromOriginNotShiftedByOldFixedSize() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            //Shows through the real 900x900-viewport host first, exactly like the task-5 render test does -
            //this is what pins Width/Height to a fixed size wider than the 420px render below.
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));

            using SKSurface surface = SKSurface.Create(new SKImageInfo(420, 900));
            panel.RenderOffscreen(surface.Canvas, 420, 900);

            Assert.Equal(0, panel.Bounds.X);
            Assert.Equal(0, panel.Bounds.Y);
        }

        //---- Nested viewport: overflow triggers scrolling ----

        [AvaloniaFact]
        public void FitToViewport_ClampsScrollHost_SoOverflowScrollsInsteadOfClipping() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));

            panel.FitToViewport(300, 40); //far smaller than the panel's natural content height

            Assert.Equal(40, panel.ScrollHost.MaxHeight);
            panel.Measure(new Avalonia.Size(300, 900));
            panel.Arrange(new Avalonia.Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));
            Assert.True(panel.ScrollHost.Extent.Height > panel.ScrollHost.Viewport.Height);
        }

        //Phase 5a's own final review confirmed GraphCanvasControl.OnPointerWheelChanged's IsOpen guard
        //(reference §7's ProductionGraphViewer_MouseWheel focus guard port) can't starve a real Avalonia
        //ScrollViewer of wheel input - it's a plain override, so a descendant that already marked the wheel
        //event Handled (ScrollViewer's own bubble-Handled routing) never reaches it - but that was never
        //pinned down with a real wheel event, only reasoned about. Mirrors ChooserPanelTests.
        //WheelOverPanel_DoesNotZoomCanvas_ButScrollsGridUnderneath's real-input shape for the recipe panel's
        //own ScrollHost instead of the chooser's icon grid.
        [AvaloniaFact]
        public void WheelOverScrollHost_ScrollsPanelContent_AndDoesNotZoomCanvasUnderneath() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            panel.FitToViewport(300, 40); //far smaller than the panel's natural content height - guarantees real overflow to scroll
            float scaleBefore = rf.Fx.Control.Viewport.ViewScale;

            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); //flushes the deferred Arrange pass so ScrollHost.Bounds is real before we compute a screen point from it
            Assert.True(panel.ScrollHost.Extent.Height > panel.ScrollHost.Viewport.Height);
            Assert.Equal(0, panel.ScrollHost.Offset.Y);

            Avalonia.Point scrollHostTopLeft = AbsoluteTopLeft(panel.ScrollHost, panel);
            var wheelPoint = new Avalonia.Point(scrollHostTopLeft.X + 10, scrollHostTopLeft.Y + 10);
            rf.Fx.Window.MouseWheel(wheelPoint, new Avalonia.Vector(0, -1), RawInputModifiers.None);

            Assert.True(panel.ScrollHost.Offset.Y > 0); //the panel's own ScrollViewer, not GraphCanvasControl, consumed the wheel
            Assert.Equal(scaleBefore, rf.Fx.Control.Viewport.ViewScale); //and the canvas underneath never saw it
        }

        //---- Task deliverable: offscreen render of a real vanilla recipe node (assembler+modules+beacon) ----

        [AvaloniaFact]
        public void Render_EditRecipePanel_BoundToVanillaRecipeNode_ProducesNonEmptyPngInSddWorkspace() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            controller.SetBeaconCount(2);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));

            const int width = 420;
            const int height = 900;
            string sddDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                ".superpowers", "sdd", "2026-09-02-phase5a-floating-panels");
            Directory.CreateDirectory(sddDir);
            string outPath = Path.Combine(sddDir, "task-5-recipe-panel-render.png");

            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
            panel.RenderOffscreen(surface.Canvas, width, height);
            using SKImage renderedImage = surface.Snapshot();
            using SKData data = renderedImage.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(outPath, data.ToArray());

            Assert.True(new FileInfo(outPath).Length > 0);
        }

        //Review findings 1+2: a wide-enough render (unlike the 420px-narrow task-5 render above, which is
        //deliberately kept at its original narrow size to keep covering the origin-shift regression it was
        //built for) shows the restored side-by-side sections and the enlarged section-header text at their
        //real rendered proportions, for visual comparison against upstream's screenshots.
        [AvaloniaFact]
        public void Render_EditRecipePanel_AtFullViewportWidth_ShowsSideBySideSectionsForVisualReview() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            controller.SetBeaconCount(2);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));

            const int width = 900;
            const int height = 900;
            string sddDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                ".superpowers", "sdd", "2026-09-02-phase5a-floating-panels");
            Directory.CreateDirectory(sddDir);
            string outPath = Path.Combine(sddDir, "task-5b-recipe-panel-review-render.png");

            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
            panel.RenderOffscreen(surface.Canvas, width, height);
            using SKImage renderedImage = surface.Snapshot();
            using SKData data = renderedImage.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(outPath, data.ToArray());

            Assert.True(new FileInfo(outPath).Length > 0);
        }

        //---- Finding B1 (2026-09-02 gallery review): editPanel/recipePanel are upstream's paired floating
        //controls (ProductionGraphViewer.cs 538-576), not one panel with the recipe card embedded inline ----

        [AvaloniaFact]
        public void Construction_DoesNotEmbedARecipePanelInItsOwnVisualTree() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));

            Assert.Empty(panel.GetVisualDescendants().OfType<RecipePanel>());
        }

        //---- Finding B2: section chrome - a near-black header band over a mid-gray body, matching
        //EditRecipePanel.Designer.cs's Color.FromArgb(40,40,40)/(65,65,65) constants, not flat black ----

        [AvaloniaFact]
        public void Construction_AssemblerAndBeaconSections_UseUpstreamBodyBackColor() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);

            Assert.Equal(EditRecipePanel.SectionBodyColor, Assert.IsAssignableFrom<ISolidColorBrush>(panel.AssemblerSection.Background).Color);
            Assert.Equal(EditRecipePanel.SectionBodyColor, Assert.IsAssignableFrom<ISolidColorBrush>(panel.BeaconSection.Background).Color);
        }

        [AvaloniaFact]
        public void Construction_SectionHeaderLabels_UseUpstreamHeaderBackColor() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);

            Assert.Equal(EditRecipePanel.SectionHeaderColor, Assert.IsAssignableFrom<ISolidColorBrush>(panel.AModulesLabel.Background).Color);
            Assert.Equal(EditRecipePanel.SectionHeaderColor, Assert.IsAssignableFrom<ISolidColorBrush>(panel.AModuleOptionsLabel.Background).Color);
        }

        //---- Finding B3: the three beacon spinners used to stretch to their row's full height (a bare
        //NumericUpDown next to a tall icon-list sibling in a horizontal StackPanel stretches by default) -
        //upstream stacks them as three compact labeled rows instead (Designer.cs BeaconValuesTable) ----

        [AvaloniaFact]
        public void BeaconValueInputs_StackVerticallyWithSaneCompactHeight() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            panel.Measure(new Avalonia.Size(900, 900));
            panel.Arrange(new Avalonia.Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));

            Assert.True(panel.BeaconCountInput.Bounds.Width > 0);
            Assert.True(panel.BeaconCountInput.Bounds.Height > 0);
            Assert.True(panel.BeaconCountInput.Bounds.Height < 40,
                $"expected a compact spinner row, got height {panel.BeaconCountInput.Bounds.Height}");

            //Bounds is parent-relative, and each input's immediate parent is its own labeled row - accumulate
            //up to a shared ancestor (BeaconValuesPanel) before comparing positions across rows.
            AvaloniaPoint countPos = PositionWithin(panel.BeaconCountInput, panel.BeaconValuesPanel);
            AvaloniaPoint perAssemblerPos = PositionWithin(panel.BeaconsPerAssemblerInput, panel.BeaconValuesPanel);
            AvaloniaPoint additionalPos = PositionWithin(panel.ConstantBeaconInput, panel.BeaconValuesPanel);

            Assert.Equal(countPos.X, perAssemblerPos.X, 1);
            Assert.True(perAssemblerPos.Y > countPos.Y);
            Assert.True(additionalPos.Y > perAssemblerPos.Y);

            //Finding B4: the spinner chevrons in Avalonia's NumericUpDown template sit side by side and eat
            //~68px on their own, so an input narrow enough for the chevrons alone left no room for the value
            //itself. Each input's own width must clear that spinner cost with margin to spare.
            foreach (NumericUpDown input in new[] { panel.BeaconCountInput, panel.BeaconsPerAssemblerInput, panel.ConstantBeaconInput }) {
                TextBox valueBox = TemplateChild<TextBox>(input, "PART_TextBox");
                Assert.True(valueBox.Bounds.Width > 40,
                    $"expected the {input.Name ?? "beacon"} value box to fit two digits, got width {valueBox.Bounds.Width}");
            }
        }

        //---- Human nit: upstream's WinForms BeaconCountInput hardcodes DecimalPlaces=2, so it always shows
        //"1.00" even for whole beacon counts. The other two fields have no DecimalPlaces set upstream (WinForms
        //default 0), so they already render bare integers there. We match that intent with FormatString instead
        //of DecimalPlaces: "0.##" for BeaconCountInput keeps genuine fractional counts visible while dropping
        //the trailing ".00" on whole numbers, and "0" for the other two matches their upstream integer-only
        //behavior exactly ----

        [AvaloniaFact]
        public void BeaconCountInput_WholeValue_DisplaysBareInteger() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            panel.Measure(new Avalonia.Size(900, 900));
            panel.Arrange(new Avalonia.Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));

            panel.BeaconCountInput.Value = 1m;
            Assert.Equal("1", panel.BeaconCountInput.Text);

            panel.BeaconCountInput.Value = 1.5m;
            Assert.Equal("1.5", panel.BeaconCountInput.Text);

            panel.BeaconsPerAssemblerInput.Value = 2m;
            Assert.Equal("2", panel.BeaconsPerAssemblerInput.Text);

            panel.ConstantBeaconInput.Value = 3m;
            Assert.Equal("3", panel.ConstantBeaconInput.Text);
        }

        //---- Human nit: NeighbourInput/ExtraProductivityInput hit the same Finding-B4 spinner squish as the
        //beacon values (Avalonia's side-by-side spinner buttons eat ~68px), but neither went through
        //BeaconValueInput's width fix - the "Extra Productivity Bonus (%):" field rendered as chevrons alone,
        //no visible digits ----

        [AvaloniaFact]
        public void NeighbourInput_WideEnoughForSpinnerButtons() {
            RecipeFixture rf = NewRecipeFixture();
            AssemblerPrototype reactor = NewAssembler(rf.Fx, "reactor", EnergySource.Electric, EntityType.Reactor);
            LinkAssembler(rf.Recipe, reactor);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(reactor, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            panel.Measure(new Avalonia.Size(900, 900));
            panel.Arrange(new Avalonia.Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));

            TextBox valueBox = TemplateChild<TextBox>(panel.NeighbourInput, "PART_TextBox");
            Assert.True(valueBox.Bounds.Width > 40, $"expected the neighbour value box to fit two digits, got width {valueBox.Bounds.Width}");
        }

        [AvaloniaFact]
        public void ExtraProductivityInput_WideEnoughForSpinnerButtons() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Fx.Control.Viewer.Graph.EnableExtraProductivityForNonMiners = true;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            panel.Measure(new Avalonia.Size(900, 900));
            panel.Arrange(new Avalonia.Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));

            TextBox valueBox = TemplateChild<TextBox>(panel.ExtraProductivityInput, "PART_TextBox");
            Assert.True(valueBox.Bounds.Width > 40, $"expected the extra-productivity value box to fit two digits, got width {valueBox.Bounds.Width}");
        }

        private static T TemplateChild<T>(AvaloniaVisual root, string partName) where T : AvaloniaVisual {
            return root.GetVisualDescendants()
                .OfType<T>()
                .First(v => (v as Avalonia.StyledElement)?.Name == partName);
        }

        //---- Task deliverable: the paired composition (finding B1) side by side, for visual review against
        //the upstream reference screenshots (images "3" and "5") ----

        [AvaloniaFact]
        public void Render_PairedEditAndRecipePanels_ForVisualReviewAgainstUpstream() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            controller.SetBeaconCount(2);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var editPanel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(editPanel, new Point(0, 0));
            var recipePanel = new RecipePanel([rf.Recipe], rf.Fx.Control.Viewer.Context.AbbreviateSciPacks);

            const int editWidth = 900;
            const int editHeight = 900;
            using SKSurface editSurface = SKSurface.Create(new SKImageInfo(editWidth, editHeight));
            editPanel.RenderOffscreen(editSurface.Canvas, editWidth, editHeight);

            int totalWidth = editWidth + 16 + Math.Max(1, (int)Math.Ceiling(recipePanel.Width));
            int totalHeight = Math.Max(editHeight, recipePanel.Height > 0 ? (int)recipePanel.Height : 1);
            using SKSurface pairSurface = SKSurface.Create(new SKImageInfo(totalWidth, totalHeight));
            using (var backgroundPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill })
                pairSurface.Canvas.DrawRect(0, 0, totalWidth, totalHeight, backgroundPaint);
            pairSurface.Canvas.DrawImage(editSurface.Snapshot(), 0, 0);
            pairSurface.Canvas.Save();
            pairSurface.Canvas.Translate(editWidth + 16, 0);
            recipePanel.PaintOnto(pairSurface.Canvas);
            pairSurface.Canvas.Restore();

            string sddDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                ".superpowers", "sdd", "2026-09-02-phase5a-floating-panels");
            Directory.CreateDirectory(sddDir);
            string outPath = Path.Combine(sddDir, "task-5c-paired-panels-review-render.png");

            using SKImage renderedImage = pairSurface.Snapshot();
            using SKData data = renderedImage.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(outPath, data.ToArray());

            Assert.True(new FileInfo(outPath).Length > 0);
        }

        //Root cause (review finding, module options rendering flat gray): SetupAssemblerModuleOptions/
        //SetupBeaconModuleOptions fill every option cell's opaque backdrop with IconButton.EmptyFillColor
        //(105,105,105) - the same swatch assembler/fuel/beacon pickers use, none of which ever go
        //IsEnabled=false. Module option cells are the only ones that do (UpdateAssemblerModules/
        //UpdateBeaconModules disable them once slots are full), and IconButton.PaintOnto's 40%-alpha
        //grayscale filter then composites over that bright opaque backdrop instead of upstream's real
        //one - upstream's AModulesChoiceTable/BModulesChoiceTable never set their own BackColor, so a
        //full-slots (but still valid) module inherits AssemblerTable/BeaconTable's dark (65,65,65)
        //SectionBodyColor and dims into it. Ours instead composited onto the brighter 105-gray swatch,
        //baking a loud, uniformly bright gray block - measured #FF5B5B5B before this fix.
        [AvaloniaFact]
        public void ModuleOptionCell_FullSlots_DimsIntoSectionBody_NotBrightGrayBlock() {
            RecipeFixture rf = NewRecipeFixture();
            using var solidRed = new SKBitmap(32, 32);
            solidRed.Erase(new SKColor(220, 30, 30, 255));
            rf.Module.SetIconAndColor(new IconColorPair(solidRed, System.Drawing.Color.Red));

            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.AddAssemblerModules(new ModuleQualityPair(rf.Module, rf.Fx.Normal)); //fills both slots
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            IconButton fullSlotsOption = panel.AModuleOptions[0];
            Assert.False(fullSlotsOption.IsEnabled); //slots full - dimmed, but the module itself is still valid

            using SKSurface surface = SKSurface.Create(new SKImageInfo(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul));
            fullSlotsOption.PaintOnto(surface.Canvas, new SKRect(0, 0, 32, 32));
            using SKImage img = surface.Snapshot();
            using SKBitmap baked = SKBitmap.FromImage(img);
            SKColor pixel = baked.GetPixel(16, 16);

            //220/30/30's luminance (.2126*220 + .7152*30 + .0722*30 = 70.394) at 40% alpha over
            //EditRecipePanel.SectionBodyColor (65,65,65): round(70.394*.4 + 65*.6) = 67.
            Assert.Equal(67, pixel.Red);
            Assert.Equal(67, pixel.Green);
            Assert.Equal(67, pixel.Blue);
            Assert.Equal(255, pixel.Alpha);
        }

        //Companion checks: a still-addable option (slots open) stays fully saturated, and a genuinely
        //disallowed module (Available == false) still bakes its distinct ErrorColor tint - neither should
        //move when the full-slots backdrop above is fixed.
        [AvaloniaFact]
        public void ModuleOptionCell_OpenSlot_StaysSaturated_DisallowedModule_StaysErrorColor() {
            RecipeFixture rf = NewRecipeFixture();
            using var solidRed = new SKBitmap(32, 32);
            solidRed.Erase(new SKColor(220, 30, 30, 255));
            rf.Module.SetIconAndColor(new IconColorPair(solidRed, System.Drawing.Color.Red));

            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            IconButton openOption = panel.AModuleOptions[0];
            Assert.True(openOption.IsEnabled);

            using SKSurface openSurface = SKSurface.Create(new SKImageInfo(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul));
            openOption.PaintOnto(openSurface.Canvas, new SKRect(0, 0, 32, 32));
            using SKImage openImg = openSurface.Snapshot();
            using SKBitmap openBaked = SKBitmap.FromImage(openImg);
            SKColor openPixel = openBaked.GetPixel(16, 16);
            Assert.Equal(new SKColor(220, 30, 30, 255), openPixel);

            ((ItemPrototype)Store(rf.Fx.Cache).Items[rf.Module.Name]).Available = false; //ModulePrototype.Available derives from this
            var disallowedPanel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            IconButton disallowedOption = disallowedPanel.AModuleOptions[0];
            Assert.Equal(Colors.DarkRed, disallowedOption.FillColor);
        }

        //---- Human nit: FixedAssemblerInput's 120px width, minus the ~68px the spinner buttons reserve
        //(Finding B4), leaves room for only about four digits - too tight for the six-digit assembler
        //counts a large production line can hit. Widen it enough to show six digits without clipping ----

        [AvaloniaFact]
        public void FixedAssemblerInput_WideEnoughForSixDigits() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetRateType(RateType.Manual);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            var panel = new EditRecipePanel(node, rf.Fx.Control.Viewer);
            rf.Fx.Control.FloatingPanelHost.Show(panel, new Point(0, 0));
            panel.Measure(new Avalonia.Size(900, 900));
            panel.Arrange(new Avalonia.Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));

            TextBox valueBox = TemplateChild<TextBox>(panel.FixedAssemblerInput, "PART_TextBox");
            double neededWidth = SixDigitTextWidth(valueBox);
            Assert.True(valueBox.Bounds.Width >= neededWidth,
                $"expected the fixed-assembler value box to fit six digits ({neededWidth}px), got width {valueBox.Bounds.Width}");
        }

        //Measures "999999" against the value box's own font/padding rather than hardcoding a pixel count,
        //so the assertion tracks whatever font Avalonia's Fluent theme actually renders with.
        private static double SixDigitTextWidth(TextBox valueBox) {
            var typeface = new Avalonia.Media.Typeface(valueBox.FontFamily, valueBox.FontStyle, valueBox.FontWeight);
            var sixDigits = new Avalonia.Media.FormattedText("999999", System.Globalization.CultureInfo.InvariantCulture,
                Avalonia.Media.FlowDirection.LeftToRight, typeface, valueBox.FontSize, null);
            return sixDigits.Width + valueBox.Padding.Left + valueBox.Padding.Right;
        }

        private static AvaloniaPoint PositionWithin(AvaloniaVisual descendant, AvaloniaVisual ancestor) {
            double x = 0, y = 0;
            for (AvaloniaVisual? current = descendant; current is not null && current != ancestor; current = current.GetVisualParent()) {
                x += current.Bounds.X;
                y += current.Bounds.Y;
            }
            return new AvaloniaPoint(x, y);
        }
    }
}
