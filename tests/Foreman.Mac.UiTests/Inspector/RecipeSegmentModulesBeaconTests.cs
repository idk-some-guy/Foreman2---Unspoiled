using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using KitPickGrid = Foreman.Mac.Kit.Composites.PickGrid;

namespace Foreman.Mac.UiTests.Inspector {
    //Module/beacon coverage for RecipeSegmentView's Modules and Beacon groups.
    public class RecipeSegmentModulesBeaconTests {
        private sealed class Fixture {
            public required DataCache Cache;
            public required IQuality Normal;
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
                supplierController.SetDesiredSetValue(value);
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
            DataCacheStore store = Store(cache);
            store.Qualities[normal.Name] = normal;
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

            return new Fixture { Cache = cache, Normal = normal, Subgroup = subgroup, Control = control, Window = window };
        }

        private static ItemPrototype NewItem(Fixture fx, string name) {
            var item = new ItemPrototype(fx.Cache, name, name, fx.Subgroup, "a") { Available = true };
            Store(fx.Cache).Items[name] = item;
            return item;
        }

        private static ModulePrototype NewModule(Fixture fx, string name, double speedBonus = 0) {
            NewItem(fx, name);
            var module = new ModulePrototype(fx.Cache, name, name) { SpeedBonus = speedBonus, Category = "production" };
            Store(fx.Cache).Modules[name] = module;
            return module;
        }

        private static AssemblerPrototype NewAssembler(Fixture fx, string name, EnergySource source) =>
            new(fx.Cache, name, name, EntityType.Assembler, source) { Available = true, Enabled = true };

        private static BeaconPrototype NewBeacon(Fixture fx, string name) {
            var beacon = new BeaconPrototype(fx.Cache, name, name, EnergySource.Electric) { Available = true, ModuleSlots = 2 };
            Store(fx.Cache).Beacons[name] = beacon;
            return beacon;
        }

        private static RecipePrototype NewRecipe(Fixture fx, string name, ItemPrototype ingredient, ItemPrototype product) {
            var recipe = new RecipePrototype(fx.Cache, name, name, fx.Subgroup, "a") { Available = true, Time = 2.0 };
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

        //---- Standard fixture: one module-and-beacon-capable electric assembler, a beacon, one shared module ----

        private sealed class RecipeFixture {
            public required Fixture Fx;
            public required RecipePrototype Recipe;
            public required AssemblerPrototype Electric;
            public required AssemblerPrototype Burner;
            public required ModulePrototype Module;
            public required BeaconPrototype Beacon;
        }

        private static RecipeFixture NewRecipeFixture() {
            Fixture fx = NewFixture();
            ItemPrototype ore = NewItem(fx, "ore");
            ItemPrototype plate = NewItem(fx, "plate");
            RecipePrototype recipe = NewRecipe(fx, "smelt", ore, plate);

            AssemblerPrototype electric = NewAssembler(fx, "electric-assembler", EnergySource.Electric);
            electric.ModuleSlots = 2;
            electric.AllowModules = true;
            electric.AllowBeacons = true;
            electric.SpeedInternal[fx.Normal] = 2.0;
            electric.EnergyConsumptionInternal[fx.Normal] = 100.0;

            AssemblerPrototype burner = NewAssembler(fx, "burner-assembler", EnergySource.Burner);

            LinkAssembler(recipe, electric);
            LinkAssembler(recipe, burner);

            ModulePrototype module = NewModule(fx, "speed-module", speedBonus: 0.5);
            electric.ModulesInternal.Add(module);
            recipe.AssemblerModulesInternal.Add(module);
            recipe.BeaconModulesInternal.Add(module);

            BeaconPrototype beacon = NewBeacon(fx, "beacon-1");
            beacon.ModulesInternal.Add(module);

            return new RecipeFixture { Fx = fx, Recipe = recipe, Electric = electric, Burner = burner, Module = module, Beacon = beacon };
        }

        private static (AvaloniaWindow Host, RecipeSegmentView View) NewSegmentView(RecipeFixture rf, IRecipeNodeViewModel node) {
            var view = new RecipeSegmentView();
            var host = new AvaloniaWindow { Content = view, Width = 400, Height = 900 };
            host.Show();
            view.Bind(node, rf.Fx.Control.Viewer, rf.Fx.Cache);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return (host, view);
        }

        //Headless MouseDown pumps the render clock before it hit-tests, so a point read mid-reflow misses a target that moves.
        private static void Click(AvaloniaWindow window, Control control) {
            AnimationClock.UntilReflowsSettled(window);
            window.UpdateLayout();
            Avalonia.Point center = new(control.Bounds.Width / 2, control.Bounds.Height / 2);
            Avalonia.Point screen = Avalonia.VisualExtensions.TranslatePoint(control, center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        //Kit.PickGrid's slots are a plain rebuilt list (no per-item Button to click); choosing a slot is
        //select-then-Return, clearing it is select-then-Delete, matching the grid's own tested keyboard path.
        private static int IndexOf(IReadOnlyList<PickGridItem> items, object key) {
            for (int i = 0; i < items.Count; i++)
                if (Equals(items[i].Key, key))
                    return i;
            return -1;
        }

        //Return/Delete act on the focused slot (2026-09-17 arming ruling), not the armed one.
        private static void ChooseSlot(KitPickGrid grid, int index) {
            grid.FocusedIndex = index;
            CanvasKeys.KeyDownOn(grid, Key.Return);
        }

        private static void ClearSlot(KitPickGrid grid, int index) {
            grid.FocusedIndex = index;
            CanvasKeys.KeyDownOn(grid, Key.Delete);
        }

        private static PickCell ChoiceCellFor(AvaloniaWindow window, KitPickGrid grid, string name) {
            AnimationClock.UntilReflowsSettled(window);
            window.UpdateLayout();
            PickCell cell = grid.GetVisualDescendants().OfType<PickCell>().Single(c => c.AccessibleName == name);
            Assert.True(cell.Bounds.Width > 0 && cell.Bounds.Height > 0, $"'{name}' resolved to a zero-size cell - a click would land nowhere.");
            return cell;
        }

        //InspectorGroup's title row - scoped inside `group` so each group's own header resolves
        //unambiguously even though every InspectorGroup names its header the same "PART_HeaderRow".
        private static Border GroupHeaderOf(InspectorGroup group) =>
            group.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HeaderRow");

        private static PickCell KitCellAt(KitPickGrid grid, int index) =>
            grid.GetVisualDescendants().OfType<PickCell>().ElementAt(index);

        private static PickCell[] CellsOf(KitPickGrid grid) =>
            [.. grid.GetVisualDescendants().OfType<PickCell>()];

        //---- Modules: add fills the next slot, tap-to-remove, incompatible modules excluded ----

        [AvaloniaFact]
        public void AddModule_FillsNextSlot_AndUpdatesModulesTitle() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal("Modules (0/2)", view.ViewModel!.ModulesTitle);

            KitPickGrid optionsGrid = view.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            ChooseSlot(optionsGrid, IndexOf(view.ViewModel!.ModuleOptions, rf.Module));

            Assert.Equal("Modules (1/2)", view.ViewModel!.ModulesTitle);
            Assert.Single(node.AssemblerModules);
        }

        [AvaloniaFact]
        public void TappingEquippedModule_RemovesIt() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal("Modules (1/2)", view.ViewModel!.ModulesTitle);

            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            ClearSlot(equippedGrid, IndexOf(view.ViewModel!.EquippedModules, rf.Module));

            Assert.Equal("Modules (0/2)", view.ViewModel!.ModulesTitle);
            Assert.Empty(node.AssemblerModules);
        }

        [AvaloniaFact]
        public void ModuleOptions_ExcludeModulesTheAssemblerCannotTake() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Burner, rf.Fx.Normal)); //burner-assembler never got AllowModules
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.Empty(view.ViewModel!.ModuleOptions);
        }

        //A25: a building with no module slots at all draws no grid and no row - the group is removed, not
        //shown with "0 slots" (burner-assembler never set ModuleSlots, so it defaults to 0).
        [AvaloniaFact]
        public void AssemblerWithZeroModuleSlots_HidesTheModulesGroupEntirely() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Burner, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.False(view.ViewModel!.HasModuleSlots);
            InspectorGroup modulesGroup = view.FindControl<InspectorGroup>("ModulesGroup")!;
            Assert.False(modulesGroup.IsVisible);
        }

        //---- Mouse path: clicking an option equips it, clicking an equipped slot only selects it ----

        [AvaloniaFact]
        public void ClickingAModuleOption_EquipsItIntoTheFirstEmptySlot_WhenNoFilledSlotIsSelected() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid optionsGrid = view.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            int optionIndex = IndexOf(view.ViewModel!.ModuleOptions, rf.Module);

            Click(host, KitCellAt(optionsGrid, optionIndex));

            Assert.Single(node.AssemblerModules);
            Assert.Equal(rf.Module, node.AssemblerModules[0].Module);
        }

        //Selecting a cell on click is kit-tested (ClickingACell_SelectsItAndFocusesTheGrid); this covers only
        //the app-level half - a click on an equipped slot never itself mutates the node.
        [AvaloniaFact]
        public void ClickingAnEquippedSlot_DoesNotMutateTheNode() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            int index = IndexOf(view.ViewModel!.EquippedModules, rf.Module);

            Click(host, KitCellAt(equippedGrid, index));

            Assert.Single(node.AssemblerModules);
            Assert.Equal(rf.Module, node.AssemblerModules[0].Module);
        }

        //Regression: SelectedIndex used to default to 0, so a click destroyed the first equipped module.
        [AvaloniaFact]
        public void ClickingAModuleOption_OnAFullyEquippedGridWithNoSelection_DoesNothing() {
            RecipeFixture rf = NewRecipeFixture();
            ModulePrototype other = NewModule(rf.Fx, "efficiency-module");
            rf.Electric.ModulesInternal.Add(other);
            rf.Recipe.AssemblerModulesInternal.Add(other);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.AddAssemblerModule(new ModuleQualityPair(other, rf.Fx.Normal)); //fills both of Electric's two slots
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid optionsGrid = view.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            PickCell optionCell = KitCellAt(optionsGrid, IndexOf(view.ViewModel!.ModuleOptions, rf.Module));
            Assert.Equal("All slots full — select a slot to replace", ToolTip.GetTip(optionCell));
            Assert.Equal("All slots full — select a slot to replace", AutomationProperties.GetHelpText(optionCell));

            Click(host, optionCell);

            Assert.Equal(2, node.AssemblerModules.Count);
            Assert.Equal(rf.Module, node.AssemblerModules[0].Module);
            Assert.Equal(other, node.AssemblerModules[1].Module);
        }

        //Arriving at a slot by keyboard is not arming it, so an option click still finds nothing armed.
        [AvaloniaFact]
        public void TabThroughTheEquippedGrid_ClickingAnOption_AppendsRatherThanReplacing() {
            RecipeFixture rf = NewRecipeFixture();
            ModulePrototype other = NewModule(rf.Fx, "efficiency-module");
            rf.Electric.ModulesInternal.Add(other);
            rf.Recipe.AssemblerModulesInternal.Add(other);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            KitPickGrid optionsGrid = view.FindControl<KitPickGrid>("ModuleOptionsGrid")!;

            equippedGrid.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(-1, equippedGrid.SelectedIndex);

            Click(host, KitCellAt(optionsGrid, IndexOf(view.ViewModel!.ModuleOptions, other)));

            Assert.Equal(2, node.AssemblerModules.Count);
            Assert.Equal(rf.Module, node.AssemblerModules[0].Module);
            Assert.Equal(other, node.AssemblerModules[1].Module);
        }

        [AvaloniaFact]
        public void ClickingAModuleOption_WithAMiddleSlotSelected_ReplacesOnlyThatSlot_KeepingOrder() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Electric.ModuleSlots = 3;
            ModulePrototype second = NewModule(rf.Fx, "efficiency-module");
            ModulePrototype third = NewModule(rf.Fx, "productivity-module");
            ModulePrototype replacement = NewModule(rf.Fx, "speed-module-2");
            foreach (ModulePrototype m in new[] { second, third, replacement }) {
                rf.Electric.ModulesInternal.Add(m);
                rf.Recipe.AssemblerModulesInternal.Add(m);
            }
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.AddAssemblerModule(new ModuleQualityPair(second, rf.Fx.Normal));
            controller.AddAssemblerModule(new ModuleQualityPair(third, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            KitPickGrid optionsGrid = view.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            equippedGrid.SelectedIndex = IndexOf(view.ViewModel!.EquippedModules, second);

            Click(host, KitCellAt(optionsGrid, IndexOf(view.ViewModel!.ModuleOptions, replacement)));

            Assert.Equal(3, node.AssemblerModules.Count);
            Assert.Equal(rf.Module, node.AssemblerModules[0].Module);
            Assert.Equal(replacement, node.AssemblerModules[1].Module);
            Assert.Equal(third, node.AssemblerModules[2].Module);
            Assert.Equal(-1, equippedGrid.SelectedIndex);
        }

        [AvaloniaFact]
        public void ClickingABeaconModuleOption_WithASlotSelected_ReplacesThatSlot_KeepingOrder() {
            RecipeFixture rf = NewRecipeFixture();
            ModulePrototype other = NewModule(rf.Fx, "efficiency-module");
            ModulePrototype replacement = NewModule(rf.Fx, "speed-module-2");
            foreach (ModulePrototype m in new[] { other, replacement }) {
                rf.Beacon.ModulesInternal.Add(m);
                rf.Recipe.BeaconModulesInternal.Add(m);
            }
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            controller.AddBeaconModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.AddBeaconModule(new ModuleQualityPair(other, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedBeaconGrid = view.FindControl<KitPickGrid>("EquippedBeaconGrid")!;
            KitPickGrid beaconOptionsGrid = view.FindControl<KitPickGrid>("BeaconModuleOptionsGrid")!;
            equippedBeaconGrid.SelectedIndex = IndexOf(view.ViewModel!.EquippedBeaconModules, rf.Module);

            Click(host, KitCellAt(beaconOptionsGrid, IndexOf(view.ViewModel!.BeaconModuleOptions, replacement)));

            Assert.Equal(2, node.BeaconModules.Count);
            Assert.Equal(replacement, node.BeaconModules[0].Module);
            Assert.Equal(other, node.BeaconModules[1].Module);
            Assert.Equal(-1, equippedBeaconGrid.SelectedIndex);
        }

        //---- Solve storm: SyncSlots diffs by index instead of Slots.Clear() + rebuild ----

        [AvaloniaFact]
        public void SolveStormWithUnchangedModulesAndBeacons_RecreatesNoPickCells() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            controller.AddBeaconModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            KitPickGrid optionsGrid = view.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            KitPickGrid equippedBeaconGrid = view.FindControl<KitPickGrid>("EquippedBeaconGrid")!;
            KitPickGrid beaconOptionsGrid = view.FindControl<KitPickGrid>("BeaconModuleOptionsGrid")!;

            PickCell[] equippedBefore = CellsOf(equippedGrid);
            PickCell[] optionsBefore = CellsOf(optionsGrid);
            PickCell[] equippedBeaconBefore = CellsOf(equippedBeaconGrid);
            PickCell[] beaconOptionsBefore = CellsOf(beaconOptionsGrid);

            for (int i = 0; i < 30; i++) {
                rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(equippedBefore, CellsOf(equippedGrid));
            Assert.Equal(optionsBefore, CellsOf(optionsGrid));
            Assert.Equal(equippedBeaconBefore, CellsOf(equippedBeaconGrid));
            Assert.Equal(beaconOptionsBefore, CellsOf(beaconOptionsGrid));
        }

        //Cell identity across a positional replace is kit-tested (ReplacingASlot_UpdatesTheExistingCellInPlace);
        //this covers only the app's own step on top of that - SyncSlots' diff-by-index actually reaching the cell.
        [AvaloniaFact]
        public void SolveThatSwapsOneEquippedModule_UpdatesTheCellContent() {
            RecipeFixture rf = NewRecipeFixture();
            ModulePrototype other = NewModule(rf.Fx, "efficiency-module");
            rf.Electric.ModulesInternal.Add(other);
            rf.Recipe.AssemblerModulesInternal.Add(other);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;

            controller.RemoveAssemblerModule(0);
            controller.AddAssemblerModule(new ModuleQualityPair(other, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(other.FriendlyName, CellsOf(equippedGrid)[0].AccessibleName);
        }

        [AvaloniaFact]
        public void EquippedGrid_KeepsKeyboardFocus_ThroughASolveThatDoesNotChangeItsSlots() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            equippedGrid.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(equippedGrid.IsFocused);

            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
            Dispatcher.UIThread.RunJobs();

            Assert.True(equippedGrid.IsFocused);
        }

        //Pumping (composites addendum): a notification storm where nothing actually changed must trigger zero
        //PickGrid rebuilds - SyncSlots only ever writes to Slots when a slot's content differs, so
        //Slots.CollectionChanged is the direct proxy for "did the grid rebuild" (an unbounded
        //Slots.Clear()+rebuild on every solve, which this replaced, fires that event on every iteration).
        [AvaloniaFact]
        public void SolveStormWithUnchangedModules_TriggersNoSlotRebuildsOnTheEquippedGrid() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;

            int rebuilds = 0;
            equippedGrid.Slots.CollectionChanged += (_, _) => rebuilds++;

            for (int i = 0; i < 30; i++) {
                rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(0, rebuilds);
        }

        //---- Beacon: the outer group starts collapsed with no beacon, pick shows its module grid, clear hides it ----

        [AvaloniaFact]
        public void BeaconGroup_CollapsedWithNoBeacon_ExpandsAfterPickBeacon() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            InspectorGroup beaconGroup = view.FindControl<InspectorGroup>("BeaconGroup")!;
            Assert.False(beaconGroup.IsExpanded);

            Click(host, GroupHeaderOf(beaconGroup));
            Assert.True(beaconGroup.IsExpanded);

            KitPickGrid beaconChoiceGrid = view.FindControl<KitPickGrid>("BeaconChoiceGrid")!;
            Click(host, ChoiceCellFor(host, beaconChoiceGrid, rf.Beacon.FriendlyName));

            Assert.True(beaconGroup.IsExpanded);
        }

        //A manual header click must not stop later HasBeacon flips from reaching IsExpanded - the view
        //only re-syncs IsExpanded on an actual flip (see RecipeSegmentView.OnRecipeViewModelPropertyChanged),
        //so a hand click's own toggle stays independent of that tracking.
        [AvaloniaFact]
        public void BeaconGroup_ManualHeaderClick_DoesNotBreakLaterHasBeaconFlips() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            InspectorGroup beaconGroup = view.FindControl<InspectorGroup>("BeaconGroup")!;

            Click(host, GroupHeaderOf(beaconGroup)); //expand by hand
            Assert.True(beaconGroup.IsExpanded);
            Click(host, GroupHeaderOf(beaconGroup)); //collapse by hand again
            Assert.False(beaconGroup.IsExpanded);

            view.ViewModel!.PickBeacon(view.ViewModel!.BeaconOptions[0]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(beaconGroup.IsExpanded); //HasBeacon flipped true - still tracked after the hand click

            view.ViewModel!.ClearBeacon();
            Dispatcher.UIThread.RunJobs();
            Assert.False(beaconGroup.IsExpanded); //HasBeacon flipped false - collapses on its own too
        }

        //A live {Binding HasBeacon} used to re-push IsExpanded on every RefreshComputed, snapping
        //a manual collapse back open on the next unrelated solve.
        [AvaloniaFact]
        public void BeaconGroup_ManualCollapse_SurvivesASolveWithBeaconUnchanged() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            InspectorGroup beaconGroup = view.FindControl<InspectorGroup>("BeaconGroup")!;
            Assert.True(beaconGroup.IsExpanded); //seeded expanded at Bind(): HasBeacon was already true

            Click(host, GroupHeaderOf(beaconGroup)); //collapse by hand
            Assert.False(beaconGroup.IsExpanded);

            rf.Fx.Control.Viewer.Graph.UpdateNodeValues(); //a plain re-solve, beacon unchanged
            Dispatcher.UIThread.RunJobs();

            Assert.False(beaconGroup.IsExpanded);
        }

        [AvaloniaFact]
        public void BeaconDetail_VisibleOnlyWithABeacon() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            StackPanel beaconDetail = view.FindControl<StackPanel>("BeaconDetail")!;
            Assert.False(beaconDetail.IsVisible);

            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
            Dispatcher.UIThread.RunJobs();

            Assert.True(beaconDetail.IsVisible);
        }

        [AvaloniaFact]
        public void PickBeacon_ShowsItsModuleGrid_AndResolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.False(view.ViewModel!.HasBeacon);
            Assert.Single(view.ViewModel!.BeaconOptions);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(9);
            Assert.False(oracle.Resolved);

            InspectorGroup beaconGroup = view.FindControl<InspectorGroup>("BeaconGroup")!;
            Click(host, GroupHeaderOf(beaconGroup)); //collapsed by default - expand to reach the picker

            KitPickGrid beaconChoiceGrid = view.FindControl<KitPickGrid>("BeaconChoiceGrid")!;
            Click(host, ChoiceCellFor(host, beaconChoiceGrid, rf.Beacon.FriendlyName));

            Assert.True(oracle.Resolved);
            Assert.True(view.ViewModel!.HasBeacon);
            Assert.Equal(rf.Beacon, node.SelectedBeacon.Beacon);
            Assert.Single(view.ViewModel!.BeaconModuleOptions);
        }

        //ClearBeaconButton is gone (2026-09-17 ruling: "none" is a member of the choice set, not a command) -
        //clearing is choosing the grid's own leading None cell.
        [AvaloniaFact]
        public void PickingTheNoneCell_HidesModuleGrid_AndCallsController() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.True(view.ViewModel!.HasBeacon);

            KitPickGrid beaconChoiceGrid = view.FindControl<KitPickGrid>("BeaconChoiceGrid")!;
            Click(host, ChoiceCellFor(host, beaconChoiceGrid, "No beacon"));

            Assert.False(view.ViewModel!.HasBeacon);
            Assert.False(node.SelectedBeacon);
            Assert.Empty(view.ViewModel!.BeaconModuleOptions);
        }

        //---- Beacon row (2026-09-17 building-picker ruling): the leading None cell and its two directions ----

        [AvaloniaFact]
        public void BeaconRow_TheFirstCellIsNone_SelectedIffHasBeaconIsFalse() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.False(view.ViewModel!.HasBeacon);

            InspectorGroup beaconGroup = view.FindControl<InspectorGroup>("BeaconGroup")!;
            Click(host, GroupHeaderOf(beaconGroup)); //collapsed by default - expand to reach the row

            KitPickGrid beaconChoiceGrid = view.FindControl<KitPickGrid>("BeaconChoiceGrid")!;
            PickCell first = CellsOf(beaconChoiceGrid)[0];
            Assert.Equal("No beacon", first.AccessibleName);
            Assert.True(first.IsSelected);
        }

        [AvaloniaFact]
        public void NoControlNamedClearBeaconButton_ExistsAnywhereInTheTree() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), c => c.Name == "ClearBeaconButton");
        }

        [AvaloniaFact]
        public void BeaconRow_ChoosingNone_ClearsTheBeacon_AndHidesTheDetailRows() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            StackPanel beaconDetail = view.FindControl<StackPanel>("BeaconDetail")!;
            Assert.True(beaconDetail.IsVisible);

            KitPickGrid beaconChoiceGrid = view.FindControl<KitPickGrid>("BeaconChoiceGrid")!;
            Click(host, ChoiceCellFor(host, beaconChoiceGrid, "No beacon"));

            Assert.False(view.ViewModel!.HasBeacon);
            Assert.False(beaconDetail.IsVisible);
        }

        //---- Beacon steppers: only the count re-solves the graph ----

        [AvaloniaFact]
        public void BeaconCountEdit_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(11);
            Assert.False(oracle.Resolved);

            view.FindControl<Stepper>("BeaconCountValue")!.Value = 3;
            Dispatcher.UIThread.RunJobs();

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

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(11);
            Assert.False(oracle.Resolved);

            view.FindControl<Stepper>("BeaconsPerAssemblerValue")!.Value = 2;
            Dispatcher.UIThread.RunJobs();

            Assert.False(oracle.Resolved);
            Assert.Equal(2, node.BeaconsPerAssembler, 3);
        }

        [AvaloniaFact]
        public void BeaconsTotalEdit_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(11);
            Assert.False(oracle.Resolved);

            view.FindControl<Stepper>("BeaconsTotalValue")!.Value = 4;
            Dispatcher.UIThread.RunJobs();

            Assert.False(oracle.Resolved);
            Assert.Equal(4, node.BeaconsConst, 3);
        }

        //Kit.PickGrid's own keyboard contract (composites addendum §03): Delete empties a filled slot, Return
        //opens the chooser for the focused slot - here that's the options grid right below the equipped
        //one, so Return moves focus onto it rather than acting on the slot itself.
        [AvaloniaFact]
        public void EquippedModuleSlot_DeleteEmptiesIt_AndReturnFocusesTheOptionsChooser() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            KitPickGrid optionsGrid = view.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            int index = IndexOf(view.ViewModel!.EquippedModules, rf.Module);

            equippedGrid.FocusedIndex = index;
            CanvasKeys.KeyDownOn(equippedGrid, Key.Return);
            Assert.True(optionsGrid.IsFocused);
            Assert.Equal(index, equippedGrid.SelectedIndex);

            ClearSlot(equippedGrid, index);
            Assert.Empty(node.AssemblerModules);
        }

        //Core's module list compacts on removal, so the arm belongs on the first empty slot rather than the hole Delete made.
        private static (RecipeSegmentView View, IRecipeNodeViewModel Node, KitPickGrid Equipped, KitPickGrid Options,
            ModulePrototype A, ModulePrototype B, ModulePrototype C, ModulePrototype D) ThreeFilledModuleSlots() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Electric.ModuleSlots = 3;
            ModulePrototype b = NewModule(rf.Fx, "b-module");
            ModulePrototype c = NewModule(rf.Fx, "c-module");
            ModulePrototype d = NewModule(rf.Fx, "d-module");
            foreach (ModulePrototype m in new[] { b, c, d }) {
                rf.Electric.ModulesInternal.Add(m);
                rf.Recipe.AssemblerModulesInternal.Add(m);
            }
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.AddAssemblerModule(new ModuleQualityPair(b, rf.Fx.Normal));
            controller.AddAssemblerModule(new ModuleQualityPair(c, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            return (view, node, view.FindControl<KitPickGrid>("EquippedGrid")!,
                view.FindControl<KitPickGrid>("ModuleOptionsGrid")!, rf.Module, b, c, d);
        }

        private static void AssertEquipped(IRecipeNodeViewModel node, params ModulePrototype[] expected) {
            Assert.Equal(expected.Length, node.AssemblerModules.Count);
            for (int i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], node.AssemblerModules[i].Module);
        }

        [AvaloniaFact]
        public void DeletingAMiddleEquippedSlot_ArmsTheFirstEmptySlot_SoTheSurvivorsKeepOrder() {
            var m = ThreeFilledModuleSlots();

            ClearSlot(m.Equipped, IndexOf(m.View.ViewModel!.EquippedModules, m.B)); //the middle slot
            Assert.Equal(2, m.Equipped.SelectedIndex);

            ChooseSlot(m.Options, IndexOf(m.View.ViewModel!.ModuleOptions, m.D));

            AssertEquipped(m.Node, m.A, m.C, m.D);
        }

        //The host re-arms the slot the arm already named, which the grid must not read as "the host left it alone".
        [AvaloniaFact]
        public void DeletingASlotWithTheLastFilledSlotArmed_StillFillsTheFreedSlot() {
            var m = ThreeFilledModuleSlots();
            m.Equipped.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();

            ClearSlot(m.Equipped, IndexOf(m.View.ViewModel!.EquippedModules, m.A));
            Assert.Equal(2, m.Equipped.SelectedIndex);

            ChooseSlot(m.Options, IndexOf(m.View.ViewModel!.ModuleOptions, m.D));

            AssertEquipped(m.Node, m.B, m.C, m.D);
        }

        [AvaloniaFact]
        public void DeletingASlotWithADifferentSlotArmed_RetargetsTheArmToTheFreedSlot() {
            var m = ThreeFilledModuleSlots();
            m.Equipped.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            ClearSlot(m.Equipped, IndexOf(m.View.ViewModel!.EquippedModules, m.A));
            Assert.Equal(2, m.Equipped.SelectedIndex);

            ChooseSlot(m.Options, IndexOf(m.View.ViewModel!.ModuleOptions, m.D));

            AssertEquipped(m.Node, m.B, m.C, m.D);
        }

        [AvaloniaFact]
        public void DeletingTheLastFilledSlot_ArmsIt_AndTheNextOptionFillsIt() {
            var m = ThreeFilledModuleSlots();

            ClearSlot(m.Equipped, IndexOf(m.View.ViewModel!.EquippedModules, m.C));
            Assert.Equal(2, m.Equipped.SelectedIndex);

            ChooseSlot(m.Options, IndexOf(m.View.ViewModel!.ModuleOptions, m.D));

            AssertEquipped(m.Node, m.A, m.B, m.D);
        }

        //---- Rebind: InspectorView re-points one view instance at each selected node ----

        //Regression: the equipped grid's selection used to survive the switch, so the first option click on
        //the newly selected node replaced a slot the user had never selected on it.
        [AvaloniaFact]
        public void RebindingToAnotherNode_ClearsEveryGridsSelection_SoTheNextOptionClickFillsTheFirstEmptySlot() {
            RecipeFixture rf = NewRecipeFixture();
            ModulePrototype other = NewModule(rf.Fx, "efficiency-module");
            rf.Electric.ModulesInternal.Add(other);
            rf.Recipe.AssemblerModulesInternal.Add(other);

            IRecipeNodeViewModel first = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController firstController = ControllerFor(rf.Fx, first);
            firstController.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            firstController.RemoveAssemblerModules();
            firstController.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));

            IRecipeNodeViewModel second = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(200, 0));
            RecipeNodeController secondController = ControllerFor(rf.Fx, second);
            secondController.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            secondController.RemoveAssemblerModules();
            secondController.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, first);
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            KitPickGrid optionsGrid = view.FindControl<KitPickGrid>("ModuleOptionsGrid")!;
            equippedGrid.SelectedIndex = 0;
            optionsGrid.SelectedIndex = 0;

            view.Bind(second, rf.Fx.Control.Viewer, rf.Fx.Cache);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            Assert.Equal(-1, equippedGrid.SelectedIndex);
            Assert.Equal(-1, optionsGrid.SelectedIndex);
            Assert.Equal(-1, view.FindControl<KitPickGrid>("EquippedBeaconGrid")!.SelectedIndex);
            Assert.Equal(-1, view.FindControl<KitPickGrid>("BeaconModuleOptionsGrid")!.SelectedIndex);

            Click(host, KitCellAt(optionsGrid, IndexOf(view.ViewModel!.ModuleOptions, other)));

            Assert.Equal(2, second.AssemblerModules.Count);
            Assert.Equal(rf.Module, second.AssemblerModules[0].Module);
            Assert.Equal(other, second.AssemblerModules[1].Module);
            Assert.Single(first.AssemblerModules);
        }

        //The kit owns the grid -> cell hop; this owns the app's own wording - all four grids name the module
        //noun, so an empty slot reads the §03 string rather than the kit's nounless default.
        [AvaloniaFact]
        public void EmptyModuleSlots_ReadTheRatifiedModuleTooltip_OnEveryPickGrid() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            foreach (string name in new[] { "EquippedGrid", "ModuleOptionsGrid", "EquippedBeaconGrid", "BeaconModuleOptionsGrid" })
                Assert.Equal("module", view.FindControl<KitPickGrid>(name)!.EmptyItemNoun);

            foreach (string name in new[] { "EquippedGrid", "EquippedBeaconGrid" })
                Assert.All(CellsOf(view.FindControl<KitPickGrid>(name)!),
                    cell => Assert.Equal("Empty slot — press Return to choose a module", ToolTip.GetTip(cell)));
        }

        //The kit computes the four strings; this pins the app's own Note binding that gets them onto screen.
        [AvaloniaFact]
        public void EquippedRow_NoteTracksTheArmingState_ThroughTheRealBinding() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            controller.AddAssemblerModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            FormRow row = view.FindControl<FormRow>("ModulesEquippedRow")!;
            KitPickGrid equippedGrid = view.FindControl<KitPickGrid>("EquippedGrid")!;
            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");

            Assert.Equal("1 of 2 slots filled. The next module fills slot 2.", note.Text);

            equippedGrid.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Replacing slot 1. Press Escape to cancel.", note.Text);
        }

        //The same note binding on the beacon's own module row, which the assembler row's test never covers.
        [AvaloniaFact]
        public void BeaconEquippedRow_NoteTracksTheArmingState_ThroughTheRealBinding() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            controller.AddBeaconModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            FormRow row = view.FindControl<FormRow>("BeaconModulesEquippedRow")!;
            KitPickGrid equippedBeaconGrid = view.FindControl<KitPickGrid>("EquippedBeaconGrid")!;
            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");

            Assert.Equal("1 of 2 slots filled. The next module fills slot 2.", note.Text);

            equippedBeaconGrid.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Replacing slot 1. Press Escape to cancel.", note.Text);
        }

        [AvaloniaFact]
        public void BeaconModuleOptionsGrid_NamesTheFullState_WhenEveryBeaconSlotIsFilledAndUnarmed() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            controller.AddBeaconModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal));
            controller.AddBeaconModule(new ModuleQualityPair(rf.Module, rf.Fx.Normal)); //fills both of the beacon's two slots
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid beaconOptionsGrid = view.FindControl<KitPickGrid>("BeaconModuleOptionsGrid")!;
            PickCell optionCell = KitCellAt(beaconOptionsGrid, IndexOf(view.ViewModel!.BeaconModuleOptions, rf.Module));

            Assert.Equal("All slots full — select a slot to replace", ToolTip.GetTip(optionCell));
        }
    }
}
