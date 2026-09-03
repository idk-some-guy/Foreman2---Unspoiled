using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.Panels;
using Foreman.Models;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises Task 6's real click-to-edit wiring (docs/panels-reference.md §8/§9 step 6): BaseNodeElement.
    //MouseUpLeft's fallback now calls Context.EditNode (GraphCanvasControl.EditNode), which dispatches by node
    //type to a real EditRecipePanel/EditFlowPanel instead of the retired P4 no-op stub. EditFlowPanelTests/
    //EditRecipePanelTests already cover each panel's own field behavior once constructed directly - this file
    //covers the dispatch itself: which node type opens which panel, that a tab click doesn't, that the panel
    //this dispatch opens still obeys Task 1's click-outside-closes-and-falls-through rule, and that the
    //RequestRedraw/RequestReposition callbacks this task wires actually reach GraphCanvasControl.
    public class ClickToEditTests {
        private const int Half = 200;

        private sealed class Fixture {
            public required DataCache Cache;
            public required IQuality Quality;
            public required RecipePrototype Recipe;
            public required AssemblerPrototype AssemblerA;
            public required AssemblerPrototype AssemblerB;
            public required ItemPrototype Plate;
            public required ItemPrototype PlainItem;
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

            var group = new GroupPrototype(cache, "production", "Production", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "production-sub", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var ore = new ItemPrototype(cache, "ore", "ore", subgroup, "a") { Available = true };
            var plate = new ItemPrototype(cache, "plate", "plate", subgroup, "a") { Available = true };
            var plainItem = new ItemPrototype(cache, "widget", "widget", subgroup, "a") { Available = true };
            store.Items[ore.Name] = ore;
            store.Items[plate.Name] = plate;
            store.Items[plainItem.Name] = plainItem;

            var recipe = new RecipePrototype(cache, "smelt", "smelt", subgroup, "a") { Available = true, Time = 2.0 };
            recipe.InternalOneWayAddIngredient(ore, 1);
            ore.ConsumptionRecipesInternal.Add(recipe);
            recipe.InternalOneWayAddProduct(plate, 1, 0);
            plate.ProductionRecipesInternal.Add(recipe);
            store.Recipes[recipe.Name] = recipe;

            var assemblerA = new AssemblerPrototype(cache, "assembler-a", "assembler-a", EntityType.Assembler, EnergySource.Electric) { Available = true, Enabled = true };
            var assemblerB = new AssemblerPrototype(cache, "assembler-b", "assembler-b", EntityType.Assembler, EnergySource.Electric) { Available = true, Enabled = true };
            recipe.AssemblersInternal.Add(assemblerA);
            assemblerA.RecipesInternal.Add(recipe);
            recipe.AssemblersInternal.Add(assemblerB);
            assemblerB.RecipesInternal.Add(recipe);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(2 * Half, 2 * Half);
            control.Viewer.Context.DCache = cache;
            control.Viewer.Graph.DefaultAssemblerQuality = quality;
            var window = new AvaloniaWindow { Content = control, Width = 2 * Half, Height = 2 * Half };
            window.Show();

            return new Fixture {
                Cache = cache, Quality = quality, Recipe = recipe, AssemblerA = assemblerA, AssemblerB = assemblerB,
                Plate = plate, PlainItem = plainItem, Control = control, Window = window,
            };
        }

        private static RecipeNodeElement AddRecipeNode(Fixture fx, Point location) {
            NodeId id = fx.Control.Viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(fx.Recipe, fx.Quality), location);
            var element = (RecipeNodeElement)fx.Control.NodeElements.Single(n => n.ViewModel.Id == id);
            if (fx.Control.Viewer.Session.Editor.RequestNodeController(id) is RecipeNodeController controller)
                controller.SetAssembler(new AssemblerQualityPair(fx.AssemblerA, fx.Quality));
            fx.Control.Viewer.Graph.UpdateNodeValues();
            return element;
        }

        //A fresh non-recipe node's item tabs sit unpositioned at the node's own centre until something runs
        //UpdateState (RecipeNodeElement's own ctor calls it unconditionally; the base ctor doesn't, relying
        //on the next real paint pass's PrePaint instead) - PrePaint here stands in for that first paint,
        //same as SelectionTests/FloatingPanelHostTests' own AddSupplier helpers already do, so a body click
        //doesn't misfire MouseUpLeft's tab check through no fault of EditNode.
        private static BaseNodeElement AddSupplierNode(Fixture fx, Point location) {
            NodeId id = fx.Control.Viewer.Session.Editor.CreateSupplierNode(new ItemQualityPair(fx.PlainItem, fx.Quality), location);
            BaseNodeElement element = fx.Control.NodeElements.Single(n => n.ViewModel.Id == id);
            element.RequestStateUpdate();
            element.PrePaint();
            return element;
        }

        private static BaseNodeElement AddConsumerNode(Fixture fx, Point location) {
            NodeId id = fx.Control.Viewer.Session.Editor.CreateConsumerNode(new ItemQualityPair(fx.PlainItem, fx.Quality), location);
            BaseNodeElement element = fx.Control.NodeElements.Single(n => n.ViewModel.Id == id);
            element.RequestStateUpdate();
            element.PrePaint();
            return element;
        }

        private static BaseNodeElement AddPassthroughNode(Fixture fx, Point location) {
            NodeId id = fx.Control.Viewer.Session.Editor.CreatePassthroughNode(new ItemQualityPair(fx.PlainItem, fx.Quality), location);
            BaseNodeElement element = fx.Control.NodeElements.Single(n => n.ViewModel.Id == id);
            element.RequestStateUpdate();
            element.PrePaint();
            return element;
        }

        //Plain click: MouseDown+MouseUp at the same screen point, so the drag-threshold check in
        //OnPointerMoved never fires and OnPointerReleased's CurrentDragOperation.None branch runs.
        private static void ClickScreenPoint(AvaloniaWindow window, AvaloniaPoint screenPoint) {
            window.MouseDown(screenPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screenPoint, MouseButton.Left, RawInputModifiers.None);
        }

        private static void ClickNodeBody(Fixture fx, BaseNodeElement node) =>
            ClickScreenPoint(fx.Window, fx.Control.Viewport.GraphToScreen(node.Location));

        //---- dispatch-by-node-type (reference §8: RecipeNodeElement -> EditRecipePanel, everything else -> EditFlowPanel) ----

        [AvaloniaFact]
        public void ClickRecipeNodeBody_OpensEditRecipePanel() {
            Fixture fx = NewFixture();
            RecipeNodeElement node = AddRecipeNode(fx, Point.Empty);

            ClickNodeBody(fx, node);

            Assert.IsType<EditRecipePanel>(fx.Control.FloatingPanelHost.Content);
        }

        [AvaloniaFact]
        public void ClickSupplierNodeBody_OpensEditFlowPanel() {
            Fixture fx = NewFixture();
            BaseNodeElement node = AddSupplierNode(fx, Point.Empty);

            ClickNodeBody(fx, node);

            Assert.IsType<EditFlowPanel>(fx.Control.FloatingPanelHost.Content);
        }

        [AvaloniaFact]
        public void ClickConsumerNodeBody_OpensEditFlowPanel() {
            Fixture fx = NewFixture();
            BaseNodeElement node = AddConsumerNode(fx, Point.Empty);

            ClickNodeBody(fx, node);

            Assert.IsType<EditFlowPanel>(fx.Control.FloatingPanelHost.Content);
        }

        [AvaloniaFact]
        public void ClickPassthroughNodeBody_OpensEditFlowPanel() {
            Fixture fx = NewFixture();
            BaseNodeElement node = AddPassthroughNode(fx, Point.Empty);

            ClickNodeBody(fx, node);

            Assert.IsType<EditFlowPanel>(fx.Control.FloatingPanelHost.Content);
        }

        //---- tab vs body (reference §4/§8: a tab claims the point before EditNode ever runs) ----

        [AvaloniaFact]
        public void ClickOutputTab_DoesNotOpenEditPanel() {
            Fixture fx = NewFixture();
            RecipeNodeElement node = AddRecipeNode(fx, Point.Empty);
            var plateItem = new ItemQualityPair(fx.Plate, fx.Quality);
            ItemTabElement tab = node.GetOutputLineItemTab(plateItem);

            ClickScreenPoint(fx.Window, fx.Control.Viewport.GraphToScreen(node.LocalToGraph(tab.Location)));

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        //---- panel closes on canvas click, same click still acts on canvas (Task 1 semantics, reference §7) ----

        [AvaloniaFact]
        public void OpenedThroughRealClick_ClosesOnLaterCanvasClick() {
            Fixture fx = NewFixture();
            RecipeNodeElement node = AddRecipeNode(fx, new Point(-150, -150));
            ClickNodeBody(fx, node);
            Assert.True(fx.Control.FloatingPanelHost.IsOpen);

            //Far corner of the viewport, away from both the panel (anchored near the node) and the node itself.
            ClickScreenPoint(fx.Window, new AvaloniaPoint(2 * Half - 10, 2 * Half - 10));

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        //---- RequestRedraw wired (reference §8's Task 4/5 note: EditFlowPanel/EditRecipePanel expose the
        //callback, Task 6 sets it) ----

        [AvaloniaFact]
        public void EditFlowPanel_FixedRateEdit_RequestsRedrawOnRealControl() {
            Fixture fx = NewFixture();
            BaseNodeElement node = AddSupplierNode(fx, Point.Empty);
            ClickNodeBody(fx, node);
            var panel = Assert.IsType<EditFlowPanel>(fx.Control.FloatingPanelHost.Content);
            int before = fx.Control.RedrawRequestCount;

            panel.FixedOption.IsChecked = true;

            Assert.True(fx.Control.RedrawRequestCount > before);
        }

        [AvaloniaFact]
        public void EditRecipePanel_FixedRateEdit_RequestsRedrawOnRealControl() {
            Fixture fx = NewFixture();
            RecipeNodeElement node = AddRecipeNode(fx, Point.Empty);
            ClickNodeBody(fx, node);
            var panel = Assert.IsType<EditRecipePanel>(fx.Control.FloatingPanelHost.Content);
            int before = fx.Control.RedrawRequestCount;

            panel.FixedAssemblersOption.IsChecked = true;

            Assert.True(fx.Control.RedrawRequestCount > before);
        }
    }
}
