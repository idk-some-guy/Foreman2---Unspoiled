using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
using System.Threading.Tasks;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Inspector {
    //Rate/Assembler/Fuel/priority/key-node coverage for the docked RecipeSegmentView (Task 5, part 1).
    //Modules/Beacon/quality live in the sibling files from Task 6. Lightweight custom-DataCache fixture (no
    //vanilla preset load needed) so this file's own assembler/fuel mix - one electric, one burner, one
    //preset-disabled - stays under direct control.
    public class RecipeSegmentRateAssemblerTests {
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

        //---- Standard fixture: an electric assembler, a burner assembler (with a producible fuel), and a
        //preset-disabled assembler. ----

        private sealed class RecipeFixture {
            public required Fixture Fx;
            public required RecipePrototype Recipe;
            public required AssemblerPrototype Electric;
            public required AssemblerPrototype Burner;
            public required AssemblerPrototype Disabled;
            public required ModulePrototype Module;
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
            electric.SpeedInternal[fx.Normal] = 2.0;
            electric.EnergyConsumptionInternal[fx.Normal] = 100.0;

            AssemblerPrototype burner = NewAssembler(fx, "burner-assembler", EnergySource.Burner);
            burner.FuelsInternal.Add(coal);

            //SetupFuelOptions/BuildFuelOptions only lists a fuel that's itself producible - give coal a
            //production recipe.
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

            return new RecipeFixture { Fx = fx, Recipe = recipe, Electric = electric, Burner = burner, Disabled = disabled, Module = module, Coal = coal };
        }

        private static (AvaloniaWindow Host, RecipeSegmentView View) NewSegmentView(RecipeFixture rf, IRecipeNodeViewModel node) {
            var view = new RecipeSegmentView();
            var host = new AvaloniaWindow { Content = view, Width = 400, Height = 600 };
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

        //A PickGrid whose ItemsSource was just reset (e.g. by a NodeValuesUpdated-driven Refresh() elsewhere
        //on the graph, not just this test's own edits) needs one more layout pass before its new containers
        //are real visual children - GetVisualDescendants would otherwise walk right past the still-empty
        //ContentPresenters mid-realization.
        private static Button GridCellFor(AvaloniaWindow window, PickGrid grid, object key) {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return grid.GetVisualDescendants().OfType<Button>().Single(b => Equals((b.DataContext as PickGridItem)?.Key, key));
        }

        //---- Rate row: Auto/Fixed toggle + fixed value ----

        [AvaloniaFact]
        public void RateTypeToggle_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
            Assert.Equal(RateType.Auto, node.RateType);

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(13);
            Assert.False(oracle.Resolved);

            view.FindControl<RadioButton>("RateFixed")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(oracle.Resolved);
            Assert.Equal(RateType.Manual, node.RateType);
            Assert.True(view.FindControl<CompactStepper>("AssemblerCountValue")!.IsEnabled);
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

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.True(view.FindControl<RadioButton>("RateFixed")!.IsChecked);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(27);
            Assert.False(oracle.Resolved);

            view.FindControl<CompactStepper>("AssemblerCountValue")!.Value = 5;
            Dispatcher.UIThread.RunJobs();

            Assert.True(oracle.Resolved);
            Assert.Equal(5, node.DesiredSetValue, 3);
        }

        [AvaloniaFact]
        public void FixedRateEdit_NoActualChange_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(5);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(31);
            Assert.False(oracle.Resolved);

            CompactStepper assemblerCount = view.FindControl<CompactStepper>("AssemblerCountValue")!;
            assemblerCount.Value = assemblerCount.Value;
            Dispatcher.UIThread.RunJobs();

            Assert.False(oracle.Resolved);
        }

        //---- Assembler picker: ordering / filtering / availability ----

        [AvaloniaFact]
        public void AssemblerOptions_MatchRecipeAssemblersEnumeration_ExcludingDisabled() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            var expected = rf.Recipe.Assemblers.Where(a => a.Enabled).ToList();
            Assert.Equal(expected, view.ViewModel!.AssemblerOptions.Select(i => i.Key));
            Assert.DoesNotContain(rf.Disabled, view.ViewModel!.AssemblerOptions.Select(i => i.Key));
        }

        [AvaloniaFact]
        public void AssemblerOptions_ColorUnavailableAssemblerAsError() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Burner.Available = false;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);

            PickGridItem burnerItem = view.ViewModel!.AssemblerOptions.Single(i => Equals(i.Key, rf.Burner));
            Assert.False(burnerItem.IsAvailable);

            PickGrid assemblerGrid = view.FindControl<PickGrid>("AssemblerGrid")!;
            Button burnerButton = GridCellFor(host, assemblerGrid, rf.Burner);
            Assert.Contains("unavailable", burnerButton.Classes);
        }

        //---- Assembler change: resolves ----

        [AvaloniaFact]
        public void AssemblerChange_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(42);
            Assert.False(oracle.Resolved);

            PickGrid assemblerGrid = view.FindControl<PickGrid>("AssemblerGrid")!;
            Click(host, GridCellFor(host, assemblerGrid, rf.Burner));

            Assert.True(oracle.Resolved);
            Assert.Equal(rf.Burner, node.SelectedAssembler.Assembler);
        }

        //---- Fuel picker: visible only for burner assemblers, resolves ----

        [AvaloniaFact]
        public void BurnerAssembler_ShowsFuelGroup_AndPickingFuelResolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Burner, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.True(view.ViewModel!.ShowsFuel);
            Assert.Single(view.ViewModel!.FuelOptions);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(17);
            Assert.False(oracle.Resolved);

            PickGrid fuelGrid = view.FindControl<PickGrid>("FuelGrid")!;
            Click(host, GridCellFor(host, fuelGrid, rf.Coal));

            Assert.True(oracle.Resolved);
            Assert.Equal(rf.Coal, node.Fuel);
        }

        [AvaloniaFact]
        public void NonBurnerAssembler_HidesFuelGroup() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.False(view.ViewModel!.ShowsFuel);
            Assert.Empty(view.ViewModel!.FuelOptions);
        }

        //---- Key node fields: never resolve ----

        [AvaloniaFact]
        public void KeyNodeTitleEdit_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetKeyNode(true);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(23);
            Assert.False(oracle.Resolved);

            view.FindControl<TextBox>("KeyNodeTitle")!.Text = "Renamed";
            Dispatcher.UIThread.RunJobs();

            Assert.False(oracle.Resolved);
            Assert.Equal("Renamed", node.KeyNodeTitle);
        }

        [AvaloniaFact]
        public void KeyNodeToggle_DoesNotResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(23);
            Assert.False(oracle.Resolved);

            view.FindControl<ToggleSwitch>("KeyNode")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(oracle.Resolved);
            Assert.True(node.KeyNode);
            Assert.True(view.FindControl<TextBox>("KeyNodeTitle")!.IsVisible);
        }

        //---- Low priority / neighbour / extra productivity: resolve ----

        [AvaloniaFact]
        public void LowPriorityToggle_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(5);
            Assert.False(oracle.Resolved);

            view.FindControl<ToggleSwitch>("LowPriority")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

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

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.True(view.FindControl<CompactStepper>("AvgNeighboursValue")!.IsVisible); //reactor selected -> field shown

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(6);
            Assert.False(oracle.Resolved);

            view.FindControl<CompactStepper>("AvgNeighboursValue")!.Value = 2;
            Dispatcher.UIThread.RunJobs();

            Assert.True(oracle.Resolved);
            Assert.Equal(2, node.NeighbourCount, 3);
        }

        //---- ShowsNeighbours / ShowsExtraProductivity visibility gating ----

        [AvaloniaFact]
        public void ReactorAssembler_ShowsNeighboursStepper() {
            RecipeFixture rf = NewRecipeFixture();
            AssemblerPrototype reactor = NewAssembler(rf.Fx, "reactor", EnergySource.Electric, EntityType.Reactor);
            LinkAssembler(rf.Recipe, reactor);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(reactor, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.True(view.ViewModel!.ShowsNeighbours);
            Assert.True(view.FindControl<CompactStepper>("AvgNeighboursValue")!.IsVisible);
        }

        [AvaloniaFact]
        public void PlainAssembler_HidesNeighboursStepper() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.False(view.ViewModel!.ShowsNeighbours);
            Assert.False(view.FindControl<CompactStepper>("AvgNeighboursValue")!.IsVisible);
        }

        [AvaloniaFact]
        public void ExtraProductivity_HiddenByDefault_ForNonMinerRecipeWithoutProductivityResearch() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.False(view.ViewModel!.ShowsExtraProductivity);
            Assert.False(view.FindControl<CompactStepper>("ExtraProductivityValue")!.IsVisible);
        }

        [AvaloniaFact]
        public void ExtraProductivity_VisibleWhenGraphEnablesItForNonMiners() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Fx.Control.Viewer.Graph.EnableExtraProductivityForNonMiners = true;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.True(view.ViewModel!.ShowsExtraProductivity);
            Assert.True(view.FindControl<CompactStepper>("ExtraProductivityValue")!.IsVisible);
        }

        [AvaloniaFact]
        public void ExtraProductivity_VisibleWhenRecipeHasProductivityResearch() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Recipe.HasProductivityResearch = true;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.True(view.ViewModel!.ShowsExtraProductivity);
            Assert.True(view.FindControl<CompactStepper>("ExtraProductivityValue")!.IsVisible);
        }

        //Neither gate is a one-way latch: the inspector keeps the same view model across many assembler
        //picks on the same node, so picking back out of a qualifying assembler must hide the field again.
        [AvaloniaFact]
        public void AssemblerSwitch_TogglesNeighboursVisibility_PlainToReactorToPlain() {
            RecipeFixture rf = NewRecipeFixture();
            AssemblerPrototype reactor = NewAssembler(rf.Fx, "reactor", EnergySource.Electric, EntityType.Reactor);
            LinkAssembler(rf.Recipe, reactor);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            CompactStepper neighboursValue = view.FindControl<CompactStepper>("AvgNeighboursValue")!;
            PickGrid assemblerGrid = view.FindControl<PickGrid>("AssemblerGrid")!;
            Assert.False(neighboursValue.IsVisible);

            Click(host, GridCellFor(host, assemblerGrid, reactor));
            Assert.True(neighboursValue.IsVisible);

            Click(host, GridCellFor(host, assemblerGrid, rf.Electric));
            Assert.False(neighboursValue.IsVisible);
        }

        [AvaloniaFact]
        public void AssemblerSwitch_TogglesExtraProductivityVisibility_PlainToMinerToPlain() {
            RecipeFixture rf = NewRecipeFixture();
            AssemblerPrototype miner = NewAssembler(rf.Fx, "miner", EnergySource.Electric, EntityType.Miner);
            LinkAssembler(rf.Recipe, miner);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            CompactStepper extraProductivityValue = view.FindControl<CompactStepper>("ExtraProductivityValue")!;
            PickGrid assemblerGrid = view.FindControl<PickGrid>("AssemblerGrid")!;
            Assert.False(extraProductivityValue.IsVisible);

            Click(host, GridCellFor(host, assemblerGrid, miner));
            Assert.True(extraProductivityValue.IsVisible);

            Click(host, GridCellFor(host, assemblerGrid, rf.Electric));
            Assert.False(extraProductivityValue.IsVisible);
        }

        [AvaloniaFact]
        public void ExtraProductivityEdit_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Fx.Control.Viewer.Graph.EnableExtraProductivityForNonMiners = true;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(8);
            Assert.False(oracle.Resolved);

            view.FindControl<CompactStepper>("ExtraProductivityValue")!.Value = 20;
            Dispatcher.UIThread.RunJobs();

            Assert.True(oracle.Resolved);
            Assert.Equal(0.2, node.ExtraProductivity, 3);
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

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            //By hand: assembler base speed 2.0, +50% from the one equipped module -> speed multiplier 1.5;
            //productivity multiplier = base 0 + 0.1 module bonus + 1 = 1.1.
            double expectedSpeedMultiplier = 1.5;
            double expectedProductivityMultiplier = 1.1;

            string stats = view.ViewModel!.AssemblerStats;
            Assert.Contains($"Speed {expectedSpeedMultiplier.ToString("P0", DisplayCulture.Format)}", stats);
            Assert.Contains($"Prod {expectedProductivityMultiplier.ToString("P0", DisplayCulture.Format)}", stats);
            Assert.Contains("/m", stats);
        }

        //Generator branch of BuildStats (RecipeNode.Display.cs's GetGeneratorMinimumTemperature/
        //GetGeneratorMaximumTemperature/GetGeneratorEffectivity): a small standalone assembler+recipe pair
        //on the shared fixture's cache, no preset needed. An unconnected generator node has no input links,
        //so GetGeneratorAverageTemperature's own zero-inputs branch returns the assembler's
        //OperationTemperature verbatim, making effectivity deterministically 100% by construction (average
        //temp == operation temp) - a clean hand-derivable case.
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

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            //By hand: min temp = max(steam.DefaultTemperature(15)+0.1, ingredient range min(100)) = 100;
            //max temp = ingredient range max = 500; unconnected node -> average temp == OperationTemperature
            //(500) -> effectivity = (500-15)/(500-15) = 1.0 = 100%; production = 1,000,000W * 1.0 = 1 MW.
            string stats = view.ViewModel!.AssemblerStats;
            Assert.Contains("100-500°c  (optimal: 500°c)", stats);
            Assert.Contains(1.0.ToString("P0", DisplayCulture.Format), stats);
            Assert.Contains(GraphicsStuff.DoubleToEnergy(1_000_000.0, "W"), stats);
        }
    }

    //InspectorView's own Recipe-segment wiring (SegmentHost swap, RefreshSegment forwarding, RequestRedraw)
    //over the shared vanilla-cache fixture - same shape as FlowSegmentTests' "brief step 1" section.
    public class RecipeSegmentInspectorWiringTests {
        [AvaloniaFact]
        public async Task SelectingRecipeNode_ShowsRecipeSegmentInSegmentHost() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            INodeViewModel node = window.GraphCanvas.Viewer.Session.View.Nodes.Single();
            InspectorTestSupport.Select(window, node);

            var view = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment");
            Assert.NotNull(view);
            Assert.NotNull(view!.ViewModel);
        }

        [AvaloniaFact]
        public async Task RefreshSegment_ForwardsToTheBoundRecipeSegment() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            INodeViewModel node = window.GraphCanvas.Viewer.Session.View.Nodes.Single();
            InspectorTestSupport.Select(window, node);
            var view = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;

            if (window.GraphCanvas.Viewer.Session.Editor.RequestNodeController(node.Id) is not RecipeNodeController controller)
                throw new Xunit.Sdk.XunitException("Recipe node has no controller.");
            controller.SetRateType(RateType.Manual);
            controller.SetDesiredSetValue(4);
            window.GraphCanvas.Viewer.Graph.UpdateNodeValues();

            window.Inspector.RefreshSegment();

            Assert.Equal(4, view.FindControl<CompactStepper>("AssemblerCountValue")!.Value);
        }

        [AvaloniaFact]
        public async Task LowPriorityToggle_RequestsCanvasRedraw() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            INodeViewModel node = window.GraphCanvas.Viewer.Session.View.Nodes.Single();

            int redrawCount = 0;
            window.Inspector.RequestRedraw = () => redrawCount++;
            InspectorTestSupport.Select(window, node);
            var view = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;

            view.FindControl<ToggleSwitch>("LowPriority")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(redrawCount > 0);
        }
    }
}
