using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using System.Linq;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    public class CanvasEmptyStateTests {
        private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        [AvaloniaFact]
        public void EmptyGraph_ShowsStatePanelEmpty_AddItemAndAddRecipe_AddRecipeIsDefault_GoneAfterFirstNode() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            Dispatcher.UIThread.RunJobs();

            StatePanel panel = Assert.Single(fx.Control.GetVisualDescendants().OfType<StatePanel>());
            Assert.True(panel.IsVisible);
            Assert.Equal(StatePanelShape.Empty, panel.Shape);
            Assert.Equal("This graph is empty", panel.Headline);
            Assert.Equal("Add an item or a recipe to begin, or double-click anywhere on the canvas.", panel.Sentence);
            Button[] buttons = [.. panel.Buttons!.Items.OfType<Button>()];
            Assert.Equal(["Add Item…", "Add Recipe…"], buttons.Select(b => b.Content));
            Assert.Same(buttons[1], panel.Buttons.ResolveDefaultButton());

            KeyboardRig.AddSupplier(fx, "iron-plate", new DrawingPoint(0, 0));

            Assert.False(panel.IsVisible);
        }

        [AvaloniaFact]
        public void EmptyGraph_PanelIsCentredInTheViewport_AndStaysPutWhenTheViewZooms() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            Dispatcher.UIThread.RunJobs();
            StatePanel panel = fx.Control.EmptyState;
            double centreBefore = panel.Bounds.Center.X;

            fx.Control.Viewport.ViewScale = 2;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fx.Control.Bounds.Width / 2, panel.Bounds.Center.X, 1);
            Assert.Equal(centreBefore, panel.Bounds.Center.X, 1);
            Assert.Equal(fx.Control.Bounds.Height / 2, panel.Bounds.Center.Y, 1);
        }

        [AvaloniaFact]
        public void EmptyGraph_BothButtonsOpenThePaletteAtTheViewportCentreInTheirMode() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            Dispatcher.UIThread.RunJobs();
            Button[] buttons = [.. fx.Control.EmptyState.Buttons!.Items.OfType<Button>()];
            DrawingPoint centre = fx.Control.Viewport.ScreenToGraph(new Avalonia.Point(fx.Control.Viewport.Width / 2, fx.Control.Viewport.Height / 2));

            Click(buttons[0]);
            Assert.Equal(AddScope.Items, fx.Control.ActiveAddRequest!.Scope);
            Assert.Equal(centre, fx.Control.ActiveAddRequest.Location);

            fx.Control.ClosePanels();
            Click(buttons[1]);
            Assert.Equal(AddScope.Recipes, fx.Control.ActiveAddRequest!.Scope);
            Assert.Equal(centre, fx.Control.ActiveAddRequest.Location);
        }

        [AvaloniaFact]
        public void EmptyGraph_ReturnsWhenTheLastNodeIsDeleted() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            Foreman.Mac.Canvas.Elements.BaseNodeElement node = KeyboardRig.AddSupplier(fx, "iron-plate", new DrawingPoint(0, 0));
            Assert.False(fx.Control.EmptyState.IsVisible);

            fx.Viewer.SetSelection([node]);
            fx.Control.DeleteSelection();

            Assert.True(fx.Control.EmptyState.IsVisible);
        }
    }
}
