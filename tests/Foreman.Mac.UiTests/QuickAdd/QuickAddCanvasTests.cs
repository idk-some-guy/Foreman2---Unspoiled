using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models.Nodes;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaVector = Avalonia.Vector;
using AvaloniaWindow = Avalonia.Controls.Window;
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
            CanvasKeys.KeyDownOn(box, Key.Enter); //row 0 is the recipe (the radial's Source/Output slices own the item row now)

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
            CanvasKeys.KeyDownOn(box, Key.Enter); //row 0 is the recipe (the radial's Source/Output slices own the item row now)

            BaseNodeElement created = fx.Viewer.NodeElements.Except(before).Single();
            DrawingPoint dropGraphPoint = fx.Control.Viewport.ScreenToGraph(dropScreenPoint);
            Assert.Equal(dropGraphPoint.Y + (created.Height / 2), created.ViewModel.Location.Y);
        }

        //Gate fix 1, item 1: the palette opens with zero rows (SeedText is always empty), then must grow
        //as Query fills in rows and shrink back once it empties - through the real FloatingPanelHost, not
        //a bare Window, since that's what pinned the height in the first place.
        [AvaloniaFact]
        public void TypingAMatch_GrowsThePaletteToFitTheNewRows_AndShrinksBackWhenTheQueryEmpties() {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: ["Row One", "Row Two", "Row Three"], recipes: []);
            (GraphCanvasControl canvas, _) = NewCanvas(fx.Cache);

            canvas.ShowQuickAdd(new AddRequest(new DrawingPoint(0, 0), AddScope.All));
            QuickAddPalette palette = canvas.OpenPalette!;
            var vm = (QuickAddViewModel)palette.DataContext!;
            double emptyHeight = canvas.FloatingPanelHost.Bounds.Height;

            vm.Query = "row";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, vm.Rows.Count);
            double grownHeight = canvas.FloatingPanelHost.Bounds.Height;
            Assert.True(grownHeight > emptyHeight, $"grown height ({grownHeight}) did not exceed the empty height ({emptyHeight})");

            var results = palette.FindControl<ItemsControl>("Results")!;
            for (int i = 0; i < 3; i++) {
                Control container = results.ContainerFromIndex(i)!;
                Point bottomRight = container.TranslatePoint(new Point(container.Bounds.Width, container.Bounds.Height), palette) ?? new Point(double.NaN, double.NaN);
                Assert.True(bottomRight.Y <= palette.Bounds.Height + 0.5, $"row {i} bottom ({bottomRight.Y}) is outside the palette ({palette.Bounds.Height})");
            }

            vm.Query = "";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(vm.Rows);
            Assert.Equal(emptyHeight, canvas.FloatingPanelHost.Bounds.Height, 0.5);
        }

        [AvaloniaFact]
        public void TypingATwelveRowMatch_ShowsExactlySixRows_AndScrolls() {
            List<string> rowNames = [.. Enumerable.Range(1, 12).Select(i => $"Row {i:00}")];
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: rowNames, recipes: []);
            (GraphCanvasControl canvas, _) = NewCanvas(fx.Cache);

            canvas.ShowQuickAdd(new AddRequest(new DrawingPoint(0, 0), AddScope.All));
            QuickAddPalette palette = canvas.OpenPalette!;
            var vm = (QuickAddViewModel)palette.DataContext!;
            vm.Query = "row";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(12, vm.Rows.Count);

            var results = palette.FindControl<ItemsControl>("Results")!;
            var scrollViewer = results.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height, "the list did not scroll for a 12-row match");

            Control sixthRow = results.ContainerFromIndex(5)!;
            Point sixthBottom = sixthRow.TranslatePoint(new Point(0, sixthRow.Bounds.Height), scrollViewer) ?? new Point(double.NaN, double.NaN);
            Assert.True(sixthBottom.Y <= scrollViewer.Viewport.Height + 0.5, $"the sixth row's bottom ({sixthBottom.Y}) is below the viewport ({scrollViewer.Viewport.Height})");

            Control seventhRow = results.ContainerFromIndex(6)!;
            Point seventhTop = seventhRow.TranslatePoint(new Point(0, 0), scrollViewer) ?? new Point(double.NaN, double.NaN);
            Assert.True(seventhTop.Y >= scrollViewer.Viewport.Height - 0.5, $"the seventh row's top ({seventhTop.Y}) is above the viewport ({scrollViewer.Viewport.Height}) - more than six rows are visible");
        }

        private static (GraphCanvasControl, AvaloniaWindow) NewCanvas(DataCache cache) {
            var canvas = new GraphCanvasControl();
            canvas.Viewport.SetSize(900, 700);
            canvas.Viewer.Context.DCache = cache;
            var window = new AvaloniaWindow { Content = canvas, Width = 900, Height = 700 };
            window.Show();
            return (canvas, window);
        }
    }
}
