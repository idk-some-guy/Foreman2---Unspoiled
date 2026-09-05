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
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.Inspector;
using Foreman.Models;
using Foreman.Models.Nodes;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

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

        private static void Click(AvaloniaWindow window, Button button) {
            Avalonia.Point center = new(button.Bounds.Width / 2, button.Bounds.Height / 2);
            Avalonia.Point screen = Avalonia.VisualExtensions.TranslatePoint(button, center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        private static Button GridCellFor(AvaloniaWindow window, PickGrid grid, object key) {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return grid.GetVisualDescendants().OfType<Button>().Single(b => Equals((b.DataContext as PickGridItem)?.Key, key));
        }

        //InspectorGroup's title row (Task 6) - scoped inside `group` so each group's own header resolves
        //unambiguously even though every InspectorGroup names its header the same "GroupHeader".
        private static Button GroupHeaderOf(InspectorGroup group) =>
            group.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GroupHeader");

        //---- Modules: add fills the next slot, tap-to-remove, incompatible modules excluded ----

        [AvaloniaFact]
        public void AddModule_FillsNextSlot_AndUpdatesModulesTitle() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.RemoveAssemblerModules();
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal("Modules (0/2)", view.ViewModel!.ModulesTitle);

            PickGrid optionsGrid = view.FindControl<PickGrid>("ModuleOptionsGrid")!;
            Click(host, GridCellFor(host, optionsGrid, rf.Module));

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

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal("Modules (1/2)", view.ViewModel!.ModulesTitle);

            PickGrid equippedGrid = view.FindControl<PickGrid>("EquippedGrid")!;
            Click(host, GridCellFor(host, equippedGrid, rf.Module));

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

            PickGrid beaconOptions = view.FindControl<PickGrid>("BeaconOptions")!;
            Click(host, GridCellFor(host, beaconOptions, rf.Beacon));

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

        //Fix round: a live {Binding HasBeacon} used to re-push IsExpanded on every RefreshComputed, snapping
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

            PickGrid beaconOptions = view.FindControl<PickGrid>("BeaconOptions")!;
            Click(host, GridCellFor(host, beaconOptions, rf.Beacon));

            Assert.True(oracle.Resolved);
            Assert.True(view.ViewModel!.HasBeacon);
            Assert.Equal(rf.Beacon, node.SelectedBeacon.Beacon);
            Assert.Single(view.ViewModel!.BeaconModuleOptions);
        }

        [AvaloniaFact]
        public void ClearBeacon_HidesModuleGrid_AndCallsController() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.True(view.ViewModel!.HasBeacon);

            Button clearButton = view.FindControl<Button>("ClearBeaconButton")!;
            Click(host, clearButton);

            Assert.False(view.ViewModel!.HasBeacon);
            Assert.False(node.SelectedBeacon);
            Assert.Empty(view.ViewModel!.BeaconModuleOptions);
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

            view.FindControl<CompactStepper>("BeaconCountValue")!.Value = 3;
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

            view.FindControl<CompactStepper>("BeaconsPerAssemblerValue")!.Value = 2;
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

            view.FindControl<CompactStepper>("BeaconsTotalValue")!.Value = 4;
            Dispatcher.UIThread.RunJobs();

            Assert.False(oracle.Resolved);
            Assert.Equal(4, node.BeaconsConst, 3);
        }

        //Deliberate WinForms-parity divergence, not chrome: BeaconCountValue keeps a bare-integer display
        //for whole counts (dropping the trailing ".00") while the other two beacon fields stay at their own
        //two-decimal display, matching the deleted EditRecipePanel's own explicit split between "0.##" and
        //"N2"-equivalent formatting.
        [AvaloniaFact]
        public void BeaconCountInput_WholeValue_DisplaysBareInteger() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetBeacon(new BeaconQualityPair(rf.Beacon, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            CompactStepper beaconCount = view.FindControl<CompactStepper>("BeaconCountValue")!;
            CompactStepper beaconsPerAssembler = view.FindControl<CompactStepper>("BeaconsPerAssemblerValue")!;
            CompactStepper beaconsTotal = view.FindControl<CompactStepper>("BeaconsTotalValue")!;

            beaconCount.Value = 1;
            Assert.Equal("1", beaconCount.Text);

            beaconCount.Value = 1.5;
            Assert.Equal("1.5", beaconCount.Text);

            beaconsPerAssembler.Value = 2;
            Assert.Equal("2.00", beaconsPerAssembler.Text);

            beaconsTotal.Value = 3;
            Assert.Equal("3.00", beaconsTotal.Text);
        }
    }
}
