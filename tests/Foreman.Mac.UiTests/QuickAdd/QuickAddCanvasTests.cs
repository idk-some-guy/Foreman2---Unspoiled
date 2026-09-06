using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models.Nodes;
using System.Linq;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaVector = Avalonia.Vector;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    //The Quick Add palette's canvas entry points (Add Item/Add Recipe toolbar buttons, the background
    //context menu, double-click on empty canvas) and PlaceCandidate's node-placement wiring.
    public sealed class QuickAddCanvasTests {
        [AvaloniaFact]
        public void AddItem_OpensAnItemScopedPaletteAtTheGraphPoint() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(300, 200));
            Assert.True(fx.Control.FloatingPanelHost.IsOpen);
            Assert.Equal(AddScope.Items, fx.Control.ActiveAddRequest!.Scope);
            Assert.IsType<QuickAddPalette>(fx.Control.FloatingPanelHost.Content);
        }

        [AvaloniaFact]
        public void PickingAnItemPlacesASupplierNode_AndTheSecondPickLandsBelow() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(300, 200));
            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter);
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter);
            var nodes = fx.Viewer.Graph.Nodes.OfType<SupplierNode>().OrderBy(n => n.Location.Y).ToList();
            Assert.Equal(2, nodes.Count);
            Assert.Equal(200, nodes[0].Location.Y, 0.5);
            Assert.Equal(320, nodes[1].Location.Y, 0.5);
            Assert.True(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void PickingARecipePlacesARecipeNode_UsingTheGraphDefaultQuality() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddRecipeAsync(new DrawingPoint(0, 0));
            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron gear";
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter);
            var node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<RecipeNode>());
            Assert.Equal("iron-gear-wheel", node.BaseRecipe.Recipe!.Name);
        }

        [AvaloniaFact]
        public void DoubleClickOnEmptyCanvasOpensTheAllScopedPalette_ButNotOnANode() {
            using var fx = CanvasFixture.WithVanillaAndOneNode(out AvaloniaPoint nodeScreenPoint);
            fx.DoubleClick(new AvaloniaPoint(50, 50));
            Assert.Equal(AddScope.All, fx.Control.ActiveAddRequest!.Scope);
            fx.Control.FloatingPanelHost.Close();
            fx.DoubleClick(nodeScreenPoint);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void BackgroundContextMenuStillOffersAddItemAndAddRecipe_OpeningScopedPalettes() {
            using var fx = CanvasFixture.WithVanilla();
            fx.RightClick(new AvaloniaPoint(40, 40));
            var captions = fx.Control.LastContextMenuEntries!.Select(e => e.Caption).ToList();
            Assert.Equal(["Add Item", "Add Recipe"], captions.Take(2));
            fx.Control.LastContextMenuEntries![1].Invoke!();
            Assert.Equal(AddScope.Recipes, fx.Control.ActiveAddRequest!.Scope);
        }

        [AvaloniaFact]
        public void EscapeClosesThePalette_AndCmdEnterRaisesBrowseRequested() {
            using var fx = CanvasFixture.WithVanilla();
            AddRequest? browse = null; fx.Control.BrowseRequested += (_, r) => browse = r;
            fx.Control.AddItemAsync(new DrawingPoint(0, 0));
            var box = fx.Control.OpenPalette!.FindControl<TextBox>("SearchBox")!;
            ((QuickAddViewModel)fx.Control.OpenPalette!.DataContext!).Query = "gear";
            CanvasKeys.KeyDownOn(box, Key.Enter, KeyModifiers.Meta);
            Assert.Equal("gear", browse!.SeedText); Assert.Equal(AddScope.Items, browse.Scope);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        //Ports ItemRecipeChooserFlowTests' retired TwoStageFlow_PlainRecipeClick_ItemOnlyValidAsFuel_
        //AutoSelectsBurnerAssemblerAndFuel through the real palette path: coal isn't an ingredient of the
        //vanilla smelting recipe, only a valid fuel for its burner assemblers.
        [AvaloniaFact]
        public void LinkDragFromAFuelOnlyItem_PickingACompatibleRecipe_AutoSelectsTheBurnerAssemblerAndFuel() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplierOf("coal", out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            fx.ClickAt(fx.Control.OpenRadial!.Bounds.Center + new AvaloniaVector(0, -64)); //top slice: Add

            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter);

            var node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<RecipeNode>());
            IItem coal = fx.Cache.Items["coal"];
            Assert.Same(coal, node.Fuel);
            Assert.Contains(coal, node.SelectedAssembler.Assembler.Fuels);
        }

        //Ports the retired TwoStageFlow_PlainRecipeClick_ItemAlreadyInRecipe_LeavesElectricAssemblerAndNoFuel:
        //iron-plate is the picked recipe's own product, so auto-selection has nothing to do.
        [AvaloniaFact]
        public void LinkDragFromAnItemAlreadyInTheRecipe_PickingItsProducer_LeavesTheDefaultAssemblerAndNoFuel() {
            using var fx = CanvasFixture.WithVanillaAndOneConsumerOf("iron-plate", out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();
            fx.ClickAt(fx.Control.OpenRadial!.Bounds.Center + new AvaloniaVector(0, -64)); //top slice: Add

            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";
            TextBox box = palette.FindControl<TextBox>("SearchBox")!;
            CanvasKeys.KeyDownOn(box, Key.Down); //row 0 is the linked item itself; the recipe is row 1
            CanvasKeys.KeyDownOn(box, Key.Enter);

            var node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<RecipeNode>());
            Assert.Null(node.Fuel);
            Assert.Equal("electric-furnace", node.SelectedAssembler.Assembler.Name);
        }

        //Ports the retired HandleNewNodeRequestedAsync_ConsumerUp_OffsetsNodeLocationToTabLevel: a Consumer
        //link's new node centers half its height ABOVE the drop point, so its top-edge input tab lands there.
        [AvaloniaFact]
        public void LinkDragFromAnOutputTab_PlacingAPick_CentersTheNodeAboveTheDropPoint() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplierOf("iron-plate", out AvaloniaPoint outputTab);
            var before = fx.Viewer.NodeElements.ToList();
            var dropScreenPoint = new AvaloniaPoint(400, 300);
            fx.DragFrom(outputTab).To(dropScreenPoint).Release();
            fx.ClickAt(fx.Control.OpenRadial!.Bounds.Center + new AvaloniaVector(0, -64));

            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron gear wheel"; //consumes iron-plate
            CanvasKeys.KeyDownOn(palette.FindControl<TextBox>("SearchBox")!, Key.Enter);

            BaseNodeElement created = fx.Viewer.NodeElements.Except(before).Single();
            DrawingPoint dropGraphPoint = fx.Control.Viewport.ScreenToGraph(dropScreenPoint);
            Assert.Equal(dropGraphPoint.Y - (created.Height / 2), created.ViewModel.Location.Y);
        }

        //Ports HandleNewNodeRequestedAsync_SupplierUp_OffsetsNodeLocationToTabLevel_OppositeSign: a Supplier
        //link's new node centers half its height BELOW the drop point instead.
        [AvaloniaFact]
        public void LinkDragFromAnInputTab_PlacingAPick_CentersTheNodeBelowTheDropPoint() {
            using var fx = CanvasFixture.WithVanillaAndOneConsumerOf("iron-plate", out AvaloniaPoint inputTab);
            var before = fx.Viewer.NodeElements.ToList();
            var dropScreenPoint = new AvaloniaPoint(400, 300);
            fx.DragFrom(inputTab).To(dropScreenPoint).Release();
            fx.ClickAt(fx.Control.OpenRadial!.Bounds.Center + new AvaloniaVector(0, -64));

            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate"; //produces iron-plate
            TextBox box = palette.FindControl<TextBox>("SearchBox")!;
            CanvasKeys.KeyDownOn(box, Key.Down); //row 0 is the linked item itself; the recipe is row 1
            CanvasKeys.KeyDownOn(box, Key.Enter);

            BaseNodeElement created = fx.Viewer.NodeElements.Except(before).Single();
            DrawingPoint dropGraphPoint = fx.Control.Viewport.ScreenToGraph(dropScreenPoint);
            Assert.Equal(dropGraphPoint.Y + (created.Height / 2), created.ViewModel.Location.Y);
        }
    }
}
