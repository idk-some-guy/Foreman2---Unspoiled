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
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.UiTests.support;
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
using StatePanel = Foreman.Mac.Kit.Composites.StatePanel;
using StatePanelShape = Foreman.Mac.Kit.Composites.StatePanelShape;
using ButtonRow = Foreman.Mac.Kit.Composites.ButtonRow;
using ButtonRowRole = Foreman.Mac.Kit.Composites.ButtonRowRole;
using KitPickGrid = Foreman.Mac.Kit.Composites.PickGrid;
using PickCell = Foreman.Mac.Kit.Composites.PickCell;
using FormRow = Foreman.Mac.Kit.Composites.FormRow;
using InspectorGroup = Foreman.Mac.Kit.Composites.InspectorGroup;

namespace Foreman.Mac.UiTests.Inspector {
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

        private static ModulePrototype NewModule(Fixture fx, string name, double speedBonus = 0, double consumptionBonus = 0, double productivityBonus = 0) {
            NewItem(fx, name);
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

        private static void Click(AvaloniaWindow window, Control control) {
            AnimationClock.UntilReflowsSettled(window);
            Avalonia.Point center = new(control.Bounds.Width / 2, control.Bounds.Height / 2);
            Avalonia.Point screen = Avalonia.VisualExtensions.TranslatePoint(control, center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        //A choice grid whose Slots were just rebuilt needs one more layout pass before its new cells are visual children.
        private static PickCell ChoiceCellFor(AvaloniaWindow window, KitPickGrid grid, string name) {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return grid.GetVisualDescendants().OfType<PickCell>().Single(c => c.AccessibleName == name);
        }

        [AvaloniaFact]
        public void RateTypeToggle_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
            Assert.Equal(RateType.Auto, node.RateType);

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Segmented mode = view.FindControl<Segmented>("RateMode")!;
            AnimationClock.UntilReflowsSettled(host);
            Assert.Equal(mode.DesiredSize.Width, mode.Bounds.Width, 0.5);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(13);
            Assert.False(oracle.Resolved);

            mode.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Assert.True(oracle.Resolved);
            Assert.Equal(RateType.Manual, node.RateType);
            Assert.True(view.FindControl<Stepper>("AssemblerCountValue")!.IsEnabled);
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
            Assert.Equal(1, view.FindControl<Segmented>("RateMode")!.SelectedIndex);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(27);
            Assert.False(oracle.Resolved);

            view.FindControl<Stepper>("AssemblerCountValue")!.Value = 5;
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

            Stepper assemblerCount = view.FindControl<Stepper>("AssemblerCountValue")!;
            assemblerCount.Value = assemblerCount.Value;
            Dispatcher.UIThread.RunJobs();

            Assert.False(oracle.Resolved);
        }

        [AvaloniaFact]
        public void RateUnitPopUp_ListsAllSixValues_AndReportsThePickInsteadOfWritingTheGraph() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            PopUp rateUnitPopUp = view.FindControl<PopUp>("RateUnitPopUp")!;
            Assert.Equal(MainWindow.RateUnitOptions, rateUnitPopUp.Items);
            Assert.Equal("per second", rateUnitPopUp.SelectedItem);

            ProductionGraph.RateUnit? reported = null;
            int solves = 0;
            view.RateUnitChanged += (_, unit) => reported = unit;
            rf.Fx.Control.Viewer.Session.NodeValuesUpdated += (_, _) => solves++;

            rateUnitPopUp.Command!.Execute("per minute");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ProductionGraph.RateUnit.Per1Min, reported);
            Assert.Equal(ProductionGraph.RateUnit.Per1Sec, rf.Fx.Control.Viewer.Graph.SelectedRateUnit);
            Assert.Equal(0, solves);
        }

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
        public void AssemblerOptions_MarksAnUnavailableAssembler_IsAvailableFalse() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Burner.Available = false;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            PickGridItem burnerItem = view.ViewModel!.AssemblerOptions.Single(i => Equals(i.Key, rf.Burner));
            Assert.False(burnerItem.IsAvailable);
        }

        [AvaloniaFact]
        public void SelectedAssemblerIconAndName_MirrorSelectedAssembler_AndUpdateOnChange() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal(view.ViewModel!.SelectedAssembler!.Icon, view.ViewModel!.SelectedAssemblerIcon);
            Assert.Equal(view.ViewModel!.SelectedAssembler!.Name, view.ViewModel!.SelectedAssemblerName);

            KitPickGrid assemblerGrid = view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!;
            Click(host, ChoiceCellFor(host, assemblerGrid, rf.Burner.FriendlyName));

            Assert.Equal(view.ViewModel!.SelectedAssembler!.Icon, view.ViewModel!.SelectedAssemblerIcon);
            Assert.Equal(view.ViewModel!.SelectedAssembler!.Name, view.ViewModel!.SelectedAssemblerName);
        }

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

            KitPickGrid assemblerGrid = view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!;
            Click(host, ChoiceCellFor(host, assemblerGrid, rf.Burner.FriendlyName));

            Assert.True(oracle.Resolved);
            Assert.Equal(rf.Burner, node.SelectedAssembler.Assembler);
        }

        [AvaloniaFact]
        public void BurnerAssembler_SingleFuel_ShowsValueLineOnly_AndARefreshDoesNotAutoCommitOrResolve() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Burner, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();
            IItem? fuelBefore = node.Fuel;

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.True(view.ViewModel!.ShowsFuel);
            Assert.Single(view.ViewModel!.FuelOptions);
            Assert.False(view.FindControl<KitPickGrid>("FuelChoiceGrid")!.IsVisible);
            StackPanel valueLine = view.FindControl<StackPanel>("FuelValueLine")!;
            Assert.True(valueLine.IsVisible);
            Assert.Equal(rf.Coal.FriendlyName, view.FindControl<TextBlock>("FuelValueLineText")!.Text);
            Assert.Null(view.FindControl<FormRow>("FuelRow")!.Note);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(19);

            view.Refresh();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fuelBefore, node.Fuel);
            Assert.False(oracle.Resolved);
        }

        [AvaloniaFact]
        public void BurnerAssembler_MultipleFuels_ChoosingAFuelCell_SetsFuelAndResolves() {
            RecipeFixture rf = NewRecipeFixture();
            ItemPrototype gas = NewItem(rf.Fx, "gas");
            rf.Burner.FuelsInternal.Add(gas);
            var mineGas = new RecipePrototype(rf.Fx.Cache, "mine-gas", "Mine Gas", rf.Fx.Subgroup, "a") { Available = true };
            mineGas.InternalOneWayAddProduct(gas, 1, 0);
            gas.ProductionRecipesInternal.Add(mineGas);
            LinkAssembler(mineGas, rf.Electric);
            Store(rf.Fx.Cache).Recipes[mineGas.Name] = mineGas;

            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Burner, rf.Fx.Normal));
            controller.SetFuel(rf.Coal);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal(2, view.ViewModel!.FuelOptions.Count);
            KitPickGrid fuelGrid = view.FindControl<KitPickGrid>("FuelChoiceGrid")!;
            Assert.True(fuelGrid.IsVisible);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(37);
            Assert.False(oracle.Resolved);

            Click(host, ChoiceCellFor(host, fuelGrid, gas.FriendlyName));

            Assert.True(oracle.Resolved);
            Assert.Equal(gas, node.Fuel);
            Assert.Equal(gas.FriendlyName, view.FindControl<FormRow>("FuelRow")!.Note);
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

        [AvaloniaFact]
        public void BurnerAssembler_NoProducibleFuel_ShowsWarningLine_WithTheRatifiedCopy() {
            RecipeFixture rf = NewRecipeFixture();
            ItemPrototype deadFuel = NewItem(rf.Fx, "dead-fuel");
            AssemblerPrototype starvedBurner = NewAssembler(rf.Fx, "starved-burner", EnergySource.Burner);
            starvedBurner.FuelsInternal.Add(deadFuel);
            LinkAssembler(rf.Recipe, starvedBurner);

            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(starvedBurner, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.True(view.ViewModel!.ShowsFuel);
            Assert.Empty(view.ViewModel!.FuelOptions);

            Assert.False(view.FindControl<KitPickGrid>("FuelChoiceGrid")!.IsVisible);
            FormRow row = view.FindControl<FormRow>("FuelRow")!;
            TextBlock errorText = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ErrorText");
            Assert.Equal("No enabled fuel burns in this building.", errorText.Text);
            Assert.True(row.ErrorIsWarning);
        }

        [AvaloniaFact]
        public void Building_FourCandidates_IsAChoiceGridOfFourCells() {
            RecipeFixture rf = NewRecipeFixture();
            AssemblerPrototype third = NewAssembler(rf.Fx, "third-assembler", EnergySource.Electric);
            AssemblerPrototype fourth = NewAssembler(rf.Fx, "fourth-assembler", EnergySource.Electric);
            LinkAssembler(rf.Recipe, third);
            LinkAssembler(rf.Recipe, fourth);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal(4, view.ViewModel!.AssemblerOptions.Count);

            KitPickGrid assemblerGrid = view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!;
            Assert.True(assemblerGrid.IsVisible);
            Assert.True(assemblerGrid.IsChoiceMode);
            Assert.Equal(4, assemblerGrid.GetVisualDescendants().OfType<PickCell>().Count());
            Assert.False(view.FindControl<StackPanel>("AssemblerValueLine")!.IsVisible);
            Assert.False(view.FindControl<PopUp>("AssemblerFallbackPopUp")!.IsVisible);
        }

        [AvaloniaFact]
        public void Building_OneCandidate_ShowsAnIconAndNameValueLine_NotAControl() {
            RecipeFixture rf = NewRecipeFixture();
            RecipePrototype soloRecipe = NewRecipe(rf.Fx, "solo-smelt", NewItem(rf.Fx, "solo-ore"), NewItem(rf.Fx, "solo-plate"));
            LinkAssembler(soloRecipe, rf.Electric);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, soloRecipe, rf.Fx.Normal, new Point(0, 0));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Single(view.ViewModel!.AssemblerOptions);
            string expectedStats = view.ViewModel!.AssemblerStats;
            Assert.False(string.IsNullOrEmpty(expectedStats));

            Assert.False(view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!.IsVisible);
            Assert.False(view.FindControl<PopUp>("AssemblerFallbackPopUp")!.IsVisible);
            StackPanel valueLine = view.FindControl<StackPanel>("AssemblerValueLine")!;
            Assert.True(valueLine.IsVisible);
            TextBlock text = view.FindControl<TextBlock>("AssemblerValueLineText")!;
            Assert.Equal(rf.Electric.FriendlyName, text.Text);
            Assert.NotNull(view.FindControl<Image>("AssemblerValueLineIcon")!.Source);

            FormRow row = view.FindControl<FormRow>("AssemblerRow")!;
            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            TextBlock suffix = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_NoteSuffix");
            Assert.Equal("The only building that crafts this recipe.", note.Text);
            Assert.True(suffix.IsVisible);
            Assert.Equal($" · {expectedStats}", suffix.Text);
        }

        [AvaloniaFact]
        public void Building_DroppingToOneCandidate_ClearsTheGrid_AndATokenReemissionCannotResurrectIt() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            KitPickGrid grid = view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!;
            Assert.True(grid.IsVisible);
            Assert.Equal(2, grid.Slots.Count);

            rf.Burner.Enabled = false;
            view.Refresh();
            Dispatcher.UIThread.RunJobs();

            Assert.False(grid.IsVisible);
            Assert.Empty(grid.Slots);

            try {
                ScaleContract.Apply(1.5);
                Dispatcher.UIThread.RunJobs();

                Assert.False(grid.IsVisible);
                Assert.Empty(grid.Slots);
            } finally {
                ScaleContract.Apply(1.0);
            }
        }

        [AvaloniaFact]
        public void Building_ZeroEnabledCandidates_DimsTheSelectedValueLine_AndShowsAWarningLine() {
            RecipeFixture rf = NewRecipeFixture();
            RecipePrototype soloRecipe = NewRecipe(rf.Fx, "disabled-only-smelt", NewItem(rf.Fx, "d-ore"), NewItem(rf.Fx, "d-plate"));
            LinkAssembler(soloRecipe, rf.Disabled);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, soloRecipe, rf.Fx.Normal, new Point(0, 0));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Empty(view.ViewModel!.AssemblerOptions);

            Assert.False(view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!.IsVisible);
            StackPanel valueLine = view.FindControl<StackPanel>("AssemblerValueLine")!;
            Assert.True(valueLine.IsVisible);
            Avalonia.Application.Current!.TryGetResource("Foreman.Opacity.Unavailable", Avalonia.Application.Current.ActualThemeVariant, out object? unavailable);
            Assert.Equal(Assert.IsType<double>(unavailable), valueLine.Opacity);
            Assert.Equal(rf.Disabled.FriendlyName, view.FindControl<TextBlock>("AssemblerValueLineText")!.Text);

            FormRow row = view.FindControl<FormRow>("AssemblerRow")!;
            Assert.Equal("No enabled building crafts this recipe.", row.Error);
            Assert.True(row.ErrorIsWarning);
        }

        [AvaloniaFact]
        public void Building_SelectedAssemblerNotAmongTheEnabledOptions_AppendsAMissingCellSelected_AndWarns() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Disabled, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal(2, view.ViewModel!.AssemblerOptions.Count);

            KitPickGrid grid = view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!;
            Assert.True(grid.IsVisible);
            PickCell[] cells = [.. grid.GetVisualDescendants().OfType<PickCell>()];
            Assert.Equal(3, cells.Length);
            PickCell selected = Assert.Single(cells, c => c.IsSelected);
            Assert.True(selected.IsMissing);

            FormRow row = view.FindControl<FormRow>("AssemblerRow")!;
            Assert.True(row.NoteIsWarning);
            Assert.Contains(rf.Disabled.FriendlyName, row.Note);
        }

        [AvaloniaFact]
        public void Beacon_NoEnabledBeacon_ShowsTheNoneValueLine_WithNoNoteAndNoWarning() {
            RecipeFixture rf = NewRecipeFixture();
            rf.Electric.AllowBeacons = true;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Empty(view.ViewModel!.BeaconOptions);

            view.FindControl<InspectorGroup>("BeaconGroup")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            Assert.False(view.FindControl<KitPickGrid>("BeaconChoiceGrid")!.IsVisible);
            Assert.False(view.FindControl<PopUp>("BeaconFallbackPopUp")!.IsVisible);
            StackPanel valueLine = view.FindControl<StackPanel>("BeaconValueLine")!;
            Assert.True(valueLine.IsVisible);
            Assert.Equal("No beacon", view.FindControl<TextBlock>("BeaconValueLineText")!.Text);
            Assert.Null(view.FindControl<Image>("BeaconValueLineIcon")!.Source);

            FormRow row = view.FindControl<FormRow>("BeaconRow")!;
            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            Assert.True(string.IsNullOrEmpty(note.Text));
            Assert.False(row.GetVisualDescendants().OfType<Panel>().Single(x => x.Name == "PART_NoteRow").IsVisible);
            Assert.False(row.GetVisualDescendants().OfType<Panel>().Single(x => x.Name == "PART_ErrorRow").IsVisible);
        }

        [AvaloniaFact]
        public void Building_TenCandidates_FallsBackToAPopUpCarryingA16ptImage() {
            RecipeFixture rf = NewRecipeFixture();
            for (int i = 0; i < 8; i++)
                LinkAssembler(rf.Recipe, NewAssembler(rf.Fx, $"extra-assembler-{i}", EnergySource.Electric));
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal(10, view.ViewModel!.AssemblerOptions.Count);
            string expectedStats = view.ViewModel!.AssemblerStats;
            Assert.False(string.IsNullOrEmpty(expectedStats));

            Assert.False(view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!.IsVisible);
            Assert.False(view.FindControl<StackPanel>("AssemblerValueLine")!.IsVisible);
            PopUp fallback = view.FindControl<PopUp>("AssemblerFallbackPopUp")!;
            Assert.True(fallback.IsVisible);
            Assert.NotNull(fallback.Icon);
            Assert.Equal(10, fallback.Items.Count);
            Assert.Equal(rf.Electric.FriendlyName, fallback.SelectedItem);

            FormRow row = view.FindControl<FormRow>("AssemblerRow")!;
            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            Assert.Equal(expectedStats, note.Text);
            Avalonia.Application.Current!.TryGetResource("Foreman.Color.TextTertiary", Avalonia.Application.Current.ActualThemeVariant, out object? tertiary);
            Assert.Equal(Assert.IsType<Avalonia.Media.Color>(tertiary),
                Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(note.Foreground).Color);

            Dispatcher.UIThread.RunJobs();
            Border iconFrame = fallback.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_IconFrame");
            Avalonia.Application.Current!.TryGetResource("Foreman.Size.PopUpLeadingIcon", Avalonia.Application.Current.ActualThemeVariant, out object? iconSize);
            Assert.Equal(Assert.IsType<double>(iconSize), iconFrame.Bounds.Width);
            Assert.Equal(Assert.IsType<double>(iconSize), iconFrame.Bounds.Height);
        }

        [AvaloniaFact]
        public void Building_CrossingTheChoiceCap_AnimatesTheRowHeight() {
            RecipeFixture rf = NewRecipeFixture();
            for (int i = 0; i < 7; i++)
                LinkAssembler(rf.Recipe, NewAssembler(rf.Fx, $"cap-assembler-{i}", EnergySource.Electric));
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal(9, view.ViewModel!.AssemblerOptions.Count);
            FormRow row = view.FindControl<FormRow>("AssemblerRow")!;
            Assert.True(view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!.IsVisible);
            AnimationClock.Settle((int)Motion.InspectorReflowDuration.TotalMilliseconds + 100);
            Assert.True(double.IsNaN(row.Height));

            LinkAssembler(rf.Recipe, NewAssembler(rf.Fx, "tenth-assembler", EnergySource.Electric));
            view.Refresh();
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.FindControl<PopUp>("AssemblerFallbackPopUp")!.IsVisible);
            Assert.False(double.IsNaN(row.Height));

            AnimationClock.Settle((int)Motion.InspectorReflowDuration.TotalMilliseconds + 100);
            Assert.True(double.IsNaN(row.Height));
        }

        [AvaloniaFact]
        public void Building_CrossingTheChoiceCap_ReduceMotion_HeightChangeIsInstant() {
            Motion.ReduceMotion = true;
            try {
                RecipeFixture rf = NewRecipeFixture();
                for (int i = 0; i < 7; i++)
                    LinkAssembler(rf.Recipe, NewAssembler(rf.Fx, $"cap-assembler-{i}", EnergySource.Electric));
                IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
                ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
                rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

                (_, RecipeSegmentView view) = NewSegmentView(rf, node);
                FormRow row = view.FindControl<FormRow>("AssemblerRow")!;

                LinkAssembler(rf.Recipe, NewAssembler(rf.Fx, "tenth-assembler", EnergySource.Electric));
                view.Refresh();
                Dispatcher.UIThread.RunJobs();

                Assert.True(view.FindControl<PopUp>("AssemblerFallbackPopUp")!.IsVisible);
                Assert.True(double.IsNaN(row.Height));
            } finally {
                Motion.ReduceMotion = false;
            }
        }

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

            view.FindControl<CheckBox>("KeyNode")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(oracle.Resolved);
            Assert.True(node.KeyNode);
            Assert.True(view.FindControl<TextBox>("KeyNodeTitle")!.IsVisible);
        }

        [AvaloniaFact]
        public void LowPriorityToggle_Resolves() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(5);
            Assert.False(oracle.Resolved);

            view.FindControl<CheckBox>("LowPriority")!.IsChecked = true;
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
            Assert.True(view.FindControl<Stepper>("AvgNeighboursValue")!.IsVisible);

            Oracle oracle = NewOracle(rf.Fx);
            oracle.GoStale(6);
            Assert.False(oracle.Resolved);

            view.FindControl<Stepper>("AvgNeighboursValue")!.Value = 2;
            Dispatcher.UIThread.RunJobs();

            Assert.True(oracle.Resolved);
            Assert.Equal(2, node.NeighbourCount, 3);
        }

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
            Assert.True(view.FindControl<Stepper>("AvgNeighboursValue")!.IsVisible);
        }

        [AvaloniaFact]
        public void PlainAssembler_HidesNeighboursStepper() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.False(view.ViewModel!.ShowsNeighbours);
            Assert.False(view.FindControl<Stepper>("AvgNeighboursValue")!.IsVisible);
        }

        [AvaloniaFact]
        public void ExtraProductivity_HiddenByDefault_ForNonMinerRecipeWithoutProductivityResearch() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.False(view.ViewModel!.ShowsExtraProductivity);
            Assert.False(view.FindControl<Stepper>("ExtraProductivityValue")!.IsVisible);
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
            Assert.True(view.FindControl<Stepper>("ExtraProductivityValue")!.IsVisible);
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
            Assert.True(view.FindControl<Stepper>("ExtraProductivityValue")!.IsVisible);
        }

        [AvaloniaFact]
        public void AssemblerSwitch_TogglesNeighboursVisibility_PlainToReactorToPlain() {
            RecipeFixture rf = NewRecipeFixture();
            AssemblerPrototype reactor = NewAssembler(rf.Fx, "reactor", EnergySource.Electric, EntityType.Reactor);
            LinkAssembler(rf.Recipe, reactor);
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(rf.Electric, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (AvaloniaWindow host, RecipeSegmentView view) = NewSegmentView(rf, node);
            Stepper neighboursValue = view.FindControl<Stepper>("AvgNeighboursValue")!;
            KitPickGrid assemblerGrid = view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!;
            Assert.False(neighboursValue.IsVisible);

            Click(host, ChoiceCellFor(host, assemblerGrid, reactor.FriendlyName));
            Assert.True(neighboursValue.IsVisible);

            Click(host, ChoiceCellFor(host, assemblerGrid, rf.Electric.FriendlyName));
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
            Stepper extraProductivityValue = view.FindControl<Stepper>("ExtraProductivityValue")!;
            KitPickGrid assemblerGrid = view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!;
            Assert.False(extraProductivityValue.IsVisible);

            Click(host, ChoiceCellFor(host, assemblerGrid, miner.FriendlyName));
            Assert.True(extraProductivityValue.IsVisible);

            Click(host, ChoiceCellFor(host, assemblerGrid, rf.Electric.FriendlyName));
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

            view.FindControl<Stepper>("ExtraProductivityValue")!.Value = 20;
            Dispatcher.UIThread.RunJobs();

            Assert.True(oracle.Resolved);
            Assert.Equal(0.2, node.ExtraProductivity, 3);
        }

        [AvaloniaFact]
        public void AssemblerRow_RenderedNoteCarriesNameThenStats_FuelRowCarriesNameAlone() {
            RecipeFixture rf = NewRecipeFixture();
            ItemPrototype gas = NewItem(rf.Fx, "gas");
            rf.Burner.FuelsInternal.Add(gas);
            var mineGas = new RecipePrototype(rf.Fx.Cache, "mine-gas", "Mine Gas", rf.Fx.Subgroup, "a") { Available = true };
            mineGas.InternalOneWayAddProduct(gas, 1, 0);
            gas.ProductionRecipesInternal.Add(mineGas);
            LinkAssembler(mineGas, rf.Electric);
            Store(rf.Fx.Cache).Recipes[mineGas.Name] = mineGas;

            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));
            RecipeNodeController controller = ControllerFor(rf.Fx, node);
            controller.SetAssembler(new AssemblerQualityPair(rf.Burner, rf.Fx.Normal));
            controller.SetFuel(rf.Coal);
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);
            Assert.Equal(2, view.ViewModel!.AssemblerOptions.Count);
            string expectedStats = view.ViewModel!.AssemblerStats;
            Assert.False(string.IsNullOrEmpty(expectedStats));

            FormRow assemblerRow = view.FindControl<FormRow>("AssemblerRow")!;
            TextBlock note = assemblerRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            TextBlock suffix = assemblerRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_NoteSuffix");
            Assert.Equal(rf.Burner.FriendlyName, note.Text);
            Assert.True(suffix.IsVisible);
            Assert.Equal($" · {expectedStats}", suffix.Text);

            FormRow fuelRow = view.FindControl<FormRow>("FuelRow")!;
            Assert.Equal(rf.Coal.FriendlyName, fuelRow.Note);
            Assert.Null(fuelRow.NoteSuffix);
        }

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

            double expectedSpeedMultiplier = 1.5;
            double expectedProductivityMultiplier = 1.1;

            string stats = view.ViewModel!.AssemblerStats;
            Assert.Contains($"Speed {expectedSpeedMultiplier.ToString("P0", DisplayCulture.Format)}", stats);
            Assert.Contains($"Prod {expectedProductivityMultiplier.ToString("P0", DisplayCulture.Format)}", stats);
            Assert.Contains("/m", stats);
        }

        [AvaloniaFact]
        public void GeneratorStatReadout_MatchesHandDerivedValues() {
            RecipeFixture rf = NewRecipeFixture();
            var steam = new FluidPrototype(rf.Fx.Cache, "steam", "Steam", rf.Fx.Subgroup, "a") { Available = true, IsTemperatureDependent = true, DefaultTemperature = 15 };
            Store(rf.Fx.Cache).Items[steam.Name] = steam;

            var generatorRecipe = new RecipePrototype(rf.Fx.Cache, "generate-power", "Generate Power", rf.Fx.Subgroup, "a") { Available = true };
            generatorRecipe.InternalOneWayAddIngredient(steam, 1, 100, 500);
            steam.ConsumptionRecipesInternal.Add(generatorRecipe);
            Store(rf.Fx.Cache).Recipes[generatorRecipe.Name] = generatorRecipe;

            AssemblerPrototype generator = NewAssembler(rf.Fx, "generator", EnergySource.Electric, EntityType.Generator);
            generator.OperationTemperature = 500;
            generator.EnergyProductionInternal[rf.Fx.Normal] = 1_000_000.0;
            LinkAssembler(generatorRecipe, generator);

            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, generatorRecipe, rf.Fx.Normal, new Point(0, 0));
            ControllerFor(rf.Fx, node).SetAssembler(new AssemblerQualityPair(generator, rf.Fx.Normal));
            rf.Fx.Control.Viewer.Graph.UpdateNodeValues();

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            string stats = view.ViewModel!.AssemblerStats;
            Assert.Contains("100-500°c  (optimal: 500°c)", stats);
            Assert.Contains(1.0.ToString("P0", DisplayCulture.Format), stats);
            Assert.Contains(GraphicsStuff.DoubleToEnergy(1_000_000.0, "W"), stats);

            FormRow row = view.FindControl<FormRow>("AssemblerRow")!;
            TextBlock note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
            TextBlock suffix = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_NoteSuffix");
            Assert.Equal("The only building that crafts this recipe.", note.Text);
            Assert.True(suffix.IsVisible);
            Assert.Equal($" · {stats}", suffix.Text);
        }

        [AvaloniaFact]
        public void MissingRecipe_ShowsStatePanelError_WithVerbatimCopyAndTwoButtons_AndCardHidden() {
            RecipeFixture rf = NewRecipeFixture();
            var missingRecipe = new RecipePrototype(rf.Fx.Cache, "missing-recipe", "Missing Recipe", rf.Fx.Subgroup, "a", isMissing: true) { Available = true };
            LinkAssembler(missingRecipe, rf.Electric);
            Store(rf.Fx.Cache).Recipes[missingRecipe.Name] = missingRecipe;
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, missingRecipe, rf.Fx.Normal, new Point(0, 0));

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.True(view.ViewModel!.HasRecipeError);
            Assert.False(view.ViewModel!.HasCard);

            StatePanel errorPanel = view.FindControl<StatePanel>("RecipeErrorPanel")!;
            Assert.True(errorPanel.IsVisible);
            Assert.Equal(StatePanelShape.Error, errorPanel.Shape);
            Assert.Equal("Recipe missing from this preset", errorPanel.Headline);
            Assert.Equal("The current preset has no recipe by this name, so this node cannot be solved.", errorPanel.Sentence);

            var buttons = errorPanel.Buttons!.Items.OfType<Button>().ToList();
            Assert.Equal(2, buttons.Count);
            Assert.Equal("Open Presets…", buttons[0].Content);
            Assert.Equal("Pick Another Recipe…", buttons[1].Content);
            Assert.Equal(ButtonRowRole.Default, ButtonRow.GetRole(buttons[1]));

            Assert.True(view.FindControl<Segmented>("RateMode")!.IsEnabled);
            Assert.True(view.FindControl<KitPickGrid>("AssemblerChoiceGrid")!.IsEnabled);
        }

        [AvaloniaFact]
        public void NormalRecipe_HidesStatePanelError_AndShowsCard() {
            RecipeFixture rf = NewRecipeFixture();
            IRecipeNodeViewModel node = CreateRecipeNode(rf.Fx, rf.Recipe, rf.Fx.Normal, new Point(0, 0));

            (_, RecipeSegmentView view) = NewSegmentView(rf, node);

            Assert.False(view.ViewModel!.HasRecipeError);
            Assert.True(view.ViewModel!.HasCard);
            Assert.False(view.FindControl<StatePanel>("RecipeErrorPanel")!.IsVisible);
        }
    }

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

            Assert.Equal(4, view.FindControl<Stepper>("AssemblerCountValue")!.Value);
        }

        [AvaloniaFact]
        public async Task LowPriorityToggle_RequestsCanvasRedraw() {
            MainWindow window = await InspectorTestSupport.OpenWindowWithGraphAsync();
            INodeViewModel node = window.GraphCanvas.Viewer.Session.View.Nodes.Single();

            int redrawCount = 0;
            window.Inspector.RequestRedraw = () => redrawCount++;
            InspectorTestSupport.Select(window, node);
            var view = window.Inspector.FindControl<RecipeSegmentView>("RecipeSegment")!;

            view.FindControl<CheckBox>("LowPriority")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(redrawCount > 0);
        }
    }
}
