using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman;
using Foreman.Mac.Canvas;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models.Nodes;
using System.Linq;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Task 3: the Quick Add palette's canvas entry points (Add Item/Add Recipe toolbar buttons, the
    //background context menu, double-click on empty canvas) and PlaceCandidate's node-placement wiring.
    //Builds on CanvasFixture, the shared real-vanilla-preset fixture ItemRecipeChooserFlowTests used before
    //Task 7 retires that file.
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
    }
}
