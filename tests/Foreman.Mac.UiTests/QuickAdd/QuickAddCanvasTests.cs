using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
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
    public sealed class QuickAddCanvasTests {
        [AvaloniaFact]
        public void AddItem_OpensAnItemScopedPaletteAtTheGraphPoint() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(300, 200));
            Assert.NotNull(fx.Control.OpenPalette);
            Assert.Equal(AddScope.Items, fx.Control.ActiveAddRequest!.Scope);
        }

        [AvaloniaFact]
        public void PickingAnItemPlacesASupplierNode_AndTheSecondPickLandsBelow() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(300, 200));
            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter, KeyModifiers.Alt);
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter, KeyModifiers.Alt);
            var nodes = fx.Viewer.Graph.Nodes.OfType<SupplierNode>().OrderBy(n => n.Location.Y).ToList();
            Assert.Equal(2, nodes.Count);
            Assert.Equal(200, nodes[0].Location.Y, 0.5);
            Assert.Equal(320, nodes[1].Location.Y, 0.5);
            Assert.NotNull(fx.Control.OpenPalette);
        }

        [AvaloniaFact]
        public void APlainReturnPlacesTheNodeAndClosesThePalette() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(300, 200));
            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);
            Assert.Single(fx.Viewer.Graph.Nodes.OfType<SupplierNode>());
            Assert.Null(fx.Control.OpenPalette);
        }

        [AvaloniaFact]
        public void PickingARecipePlacesARecipeNode_UsingTheGraphDefaultQuality() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddRecipeAsync(new DrawingPoint(0, 0));
            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron gear";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);
            var node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<RecipeNode>());
            Assert.Equal("iron-gear-wheel", node.BaseRecipe.Recipe!.Name);
        }

        [AvaloniaFact]
        public void DoubleClickOnEmptyCanvasOpensTheAllScopedPalette_ButNotOnANode() {
            using var fx = CanvasFixture.WithVanillaAndOneNode(out AvaloniaPoint nodeScreenPoint);
            fx.DoubleClick(new AvaloniaPoint(50, 50));
            Assert.Equal(AddScope.All, fx.Control.ActiveAddRequest!.Scope);
            fx.Control.ClosePanels();
            fx.DoubleClick(nodeScreenPoint);
            Assert.False(fx.Control.IsPanelOpen);
        }

        [AvaloniaFact]
        public void PointerCaptureLostMidSelectionDrag_ResetsState_SoALaterMoveArmsNoRectangle() {
            using var fx = CanvasFixture.WithVanilla();
            var start = new AvaloniaPoint(100, 100);
            var armed = start + new AvaloniaPoint(40, 40);
            fx.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            //The first move only arms Selection; the second computes the zone.
            fx.Window.MouseMove(armed, RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(armed, RawInputModifiers.LeftMouseButton);
            Assert.Equal(GraphCanvasControl.DragOperation.Selection, fx.Control.CurrentDragOperation);
            Assert.NotNull(fx.Control.Viewer.SelectionZone);

            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            fx.Control.RaiseEvent(new PointerCaptureLostEventArgs(fx.Control, pointer));

            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);
            Assert.Null(fx.Control.Viewer.SelectionZone);

            var noButtons = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);
            var moveArgs = new PointerEventArgs(InputElement.PointerMovedEvent, fx.Control, pointer, fx.Control, new AvaloniaPoint(300, 300), 0, noButtons, KeyModifiers.None);
            fx.Control.RaiseEvent(moveArgs);

            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);
            Assert.Null(fx.Control.Viewer.SelectionZone);
        }

        [AvaloniaFact]
        public void DoubleClickThenBrowse_LeavesNoArmedDragOperation() {
            MainWindow window = OpenWindow(out _);
            Dispatcher.UIThread.RunJobs();
            GraphCanvasControl canvas = window.GraphCanvas;
            var clickPoint = new AvaloniaPoint(400, 300);

            window.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(clickPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.None);
            Assert.NotNull(canvas.OpenPalette);

            var box = canvas.OpenPalette!.Search;
            CanvasKeys.KeyDownOn(box, Key.Enter, KeyModifiers.Meta);
            Assert.True(window.BrowserWindow!.IsVisible);

            Assert.Equal(GraphCanvasControl.DragOperation.None, canvas.CurrentDragOperation);
            Assert.Null(canvas.Viewer.SelectionZone);
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
            var box = fx.Control.OpenPalette!.Search;
            ((QuickAddViewModel)fx.Control.OpenPalette!.DataContext!).Query = "gear";
            CanvasKeys.KeyDownOn(box, Key.Enter, KeyModifiers.Meta);
            Assert.Equal("gear", browse!.SeedText); Assert.Equal(AddScope.Items, browse.Scope);
            Assert.Null(fx.Control.OpenPalette);
        }

        [AvaloniaFact]
        public void LinkDragFromAFuelOnlyItem_PickingACompatibleRecipe_AutoSelectsTheBurnerAssemblerAndFuel() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplierOf("coal", out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            fx.MoveAndClickAt(fx.Control.OpenRadial!.Ring.Bounds.Center + new AvaloniaVector(0, -64));

            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);

            var node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<RecipeNode>());
            IItem coal = fx.Cache.Items["coal"];
            Assert.Same(coal, node.Fuel);
            Assert.Contains(coal, node.SelectedAssembler.Assembler.Fuels);
        }

        [AvaloniaFact]
        public void LinkDragFromAnItemAlreadyInTheRecipe_PickingItsProducer_LeavesTheDefaultAssemblerAndNoFuel() {
            using var fx = CanvasFixture.WithVanillaAndOneConsumerOf("iron-plate", out AvaloniaPoint inputTab);
            fx.DragFrom(inputTab).To(new AvaloniaPoint(400, 300)).Release();
            fx.MoveAndClickAt(fx.Control.OpenRadial!.Ring.Bounds.Center + new AvaloniaVector(0, -64));

            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";
            Dispatcher.UIThread.RunJobs();
            TextBox box = palette.Search;
            CanvasKeys.KeyDownOn(box, Key.Enter);

            var node = Assert.Single(fx.Viewer.Graph.Nodes.OfType<RecipeNode>());
            Assert.Null(node.Fuel);
            Assert.Equal("electric-furnace", node.SelectedAssembler.Assembler.Name);
        }

        [AvaloniaFact]
        public void LinkDragFromAnOutputTab_PlacingAPick_CentersTheNodeAboveTheDropPoint() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplierOf("iron-plate", out AvaloniaPoint outputTab);
            var before = fx.Viewer.NodeElements.ToList();
            var dropScreenPoint = outputTab + new AvaloniaVector(0, -200);
            fx.DragFrom(outputTab).To(dropScreenPoint).Release();
            fx.MoveAndClickAt(fx.Control.OpenRadial!.Ring.Bounds.Center + new AvaloniaVector(0, -64));

            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron gear wheel";
            Dispatcher.UIThread.RunJobs();
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);

            BaseNodeElement created = fx.Viewer.NodeElements.Except(before).Single();
            DrawingPoint dropGraphPoint = fx.Control.Viewport.ScreenToGraph(dropScreenPoint);
            Assert.Equal(dropGraphPoint.Y - (created.Height / 2), created.ViewModel.Location.Y);
        }

        [AvaloniaFact]
        public void LinkDragFromAnInputTab_PlacingAPick_CentersTheNodeBelowTheDropPoint() {
            using var fx = CanvasFixture.WithVanillaAndOneConsumerOf("iron-plate", out AvaloniaPoint inputTab);
            var before = fx.Viewer.NodeElements.ToList();
            var dropScreenPoint = inputTab + new AvaloniaVector(0, 200);
            fx.DragFrom(inputTab).To(dropScreenPoint).Release();
            fx.MoveAndClickAt(fx.Control.OpenRadial!.Ring.Bounds.Center + new AvaloniaVector(0, -64));

            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron plate";
            Dispatcher.UIThread.RunJobs();
            TextBox box = palette.Search;
            CanvasKeys.KeyDownOn(box, Key.Enter);

            BaseNodeElement created = fx.Viewer.NodeElements.Except(before).Single();
            DrawingPoint dropGraphPoint = fx.Control.Viewport.ScreenToGraph(dropScreenPoint);
            Assert.Equal(dropGraphPoint.Y + (created.Height / 2), created.ViewModel.Location.Y);
        }

        [AvaloniaFact]
        public void SpaceTypedIntoTheSearchBox_InsertsASpace_AndLeavesGridlinesAlone() {
            MainWindow window = OpenWindow(out _);
            bool gridlinesBefore = window.GraphCanvas.Grid.ShowGrid;

            window.GraphCanvas.AddItemAsync(new DrawingPoint(0, 0));
            var box = window.GraphCanvas.OpenPalette!.Search;
            CanvasKeys.KeyDownOn(box, Key.Space);
            window.KeyTextInput(" ");

            Assert.Equal(gridlinesBefore, window.GraphCanvas.Grid.ShowGrid);
            Assert.Equal(" ", ((QuickAddViewModel)window.GraphCanvas.OpenPalette!.DataContext!).Query);
        }

        //A handled KeyDown never reaches TextInput; only window.KeyPressQwerty exposes a swallowed key.
        [AvaloniaFact]
        public void LetterTypedIntoTheSearchBox_ThroughTheRealWindowKeyboardPath_IsNotMarkedHandled() {
            MainWindow window = OpenWindow(out _);
            window.GraphCanvas.AddItemAsync(new DrawingPoint(0, 0));
            window.GraphCanvas.OpenPalette!.Search.Focus();

            bool? handledAfterRouting = null;
            window.AddHandler(InputElement.KeyDownEvent,
                (object? _, KeyEventArgs e) => handledAfterRouting = e.Handled,
                Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.None);

            Assert.False(handledAfterRouting);
        }

        [AvaloniaFact]
        public void TypingIronThroughTheRealWindowKeyboardPath_FillsTheQueryBox() {
            MainWindow window = OpenWindow(out _);
            window.GraphCanvas.AddItemAsync(new DrawingPoint(0, 0));
            var palette = window.GraphCanvas.OpenPalette!;
            palette.Search.Focus();

            foreach (char letter in "iron") {
                window.KeyPressQwerty(CanvasKeys.PhysicalKeyFor(letter), RawInputModifiers.None);
                window.KeyTextInput(letter.ToString());
            }

            Assert.Equal("iron", ((QuickAddViewModel)palette.DataContext!).Query);
        }

        [AvaloniaFact]
        public void LettersAndArrowsTypedIntoTheSearchBox_DoNotTriggerCanvasHotkeys() {
            MainWindow window = OpenWindow(out _);

            window.GraphCanvas.AddItemAsync(new DrawingPoint(0, 0));
            var box = window.GraphCanvas.OpenPalette!.Search;
            DrawingPoint offsetBefore = window.GraphCanvas.Viewport.ViewOffset;

            CanvasKeys.KeyDownOn(box, Key.G);
            CanvasKeys.KeyDownOn(box, Key.S);
            CanvasKeys.KeyDownOn(box, Key.Down);

            Assert.Equal(offsetBefore, window.GraphCanvas.Viewport.ViewOffset);
        }

        [AvaloniaFact]
        public void NonEscapeKeyDown_WhilePaletteIsOpen_ReachesSubscribersButDoesNotActOnCanvasHotkeys() {
            MainWindow window = OpenWindow(out _);
            bool sawIt = false;
            window.GraphCanvas.KeyDown += (_, _) => sawIt = true;
            bool gridlinesBefore = window.GraphCanvas.Grid.ShowGrid;

            window.GraphCanvas.AddItemAsync(new DrawingPoint(0, 0));
            var box = window.GraphCanvas.OpenPalette!.Search;
            CanvasKeys.KeyDownOn(box, Key.Space);

            Assert.True(sawIt);
            Assert.Equal(gridlinesBefore, window.GraphCanvas.Grid.ShowGrid);
        }

        private static MainWindow OpenWindow(out DataCache cache) {
            CatalogFixture fx = CatalogFixture.WithItemsAndRecipes(items: ["Iron Plate"], recipes: []);
            cache = fx.Cache;
            var window = new MainWindow();
            window.Show();
            window.DataCache = fx.Cache;
            window.Settings = new AppSettings();
            window.ApplyLoadedSettings();
            return window;
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
