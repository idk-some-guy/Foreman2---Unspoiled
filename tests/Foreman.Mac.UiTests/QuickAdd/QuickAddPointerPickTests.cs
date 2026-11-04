using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models.Nodes;
using System.Linq;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    public sealed class QuickAddPointerPickTests {
        [AvaloniaFact]
        public void OptionPickingARow_PlacesTheNodeAndKeepsThePaletteOpen_AndAPressThatDismissesItNeverStartsALasso() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddRecipeAsync(new DrawingPoint(0, 0));
            QuickAddPalette palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron gear";
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilReflowsSettled(palette);
            PaletteRow row = palette.GetVisualDescendants().OfType<PaletteRow>().First();
            AvaloniaPoint rowPoint = row.TranslatePoint(new AvaloniaPoint(row.Bounds.Width / 2, row.Bounds.Height / 2), fx.Window)!.Value;

            fx.Window.MouseDown(rowPoint, MouseButton.Left, RawInputModifiers.Alt);
            fx.Window.MouseUp(rowPoint, MouseButton.Left, RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(fx.Viewer.Graph.Nodes.OfType<RecipeNode>());
            Assert.NotNull(fx.Control.OpenPalette);

            var away = new AvaloniaPoint(8, 8);
            fx.Window.MouseDown(away, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(away + new AvaloniaPoint(30, 30), RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(away + new AvaloniaPoint(40, 40), RawInputModifiers.LeftMouseButton);

            Assert.Null(fx.Control.OpenPalette);
            Assert.Null(fx.Viewer.SelectionZone);
            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);
            fx.Window.MouseUp(away + new AvaloniaPoint(40, 40), MouseButton.Left, RawInputModifiers.None);

            var next = new AvaloniaPoint(12, 12);
            fx.Window.MouseDown(next, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(next + new AvaloniaPoint(30, 30), RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(next + new AvaloniaPoint(40, 40), RawInputModifiers.LeftMouseButton);

            Assert.Equal(next, fx.Control.Viewport.GraphToScreen(fx.Viewer.SelectionZone!.Value.Location));
        }

        [AvaloniaFact]
        public void PointerPickingARow_PlacesTheNode_AndClosesThePalette() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddRecipeAsync(new DrawingPoint(0, 0));
            QuickAddPalette palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron gear";
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilReflowsSettled(palette);
            PaletteRow row = palette.GetVisualDescendants().OfType<PaletteRow>().First();
            AvaloniaPoint rowPoint = row.TranslatePoint(new AvaloniaPoint(row.Bounds.Width / 2, row.Bounds.Height / 2), fx.Window)!.Value;

            fx.Window.MouseDown(rowPoint, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseUp(rowPoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(fx.Viewer.Graph.Nodes.OfType<RecipeNode>());
            Assert.Null(fx.Control.OpenPalette);
            Assert.Null(fx.Viewer.SelectionZone);
            Assert.Equal(GraphCanvasControl.DragOperation.None, fx.Control.CurrentDragOperation);
        }

        [AvaloniaFact]
        public void ADismissingPressWithoutARelease_DoesNotSwallowTheNextDrag() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddRecipeAsync(new DrawingPoint(0, 0));
            QuickAddPalette palette = fx.Control.OpenPalette!;

            var away = new AvaloniaPoint(8, 8);
            fx.Window.MouseDown(away, MouseButton.Left, RawInputModifiers.None);
            Assert.Null(fx.Control.OpenPalette);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.UntilReflowsSettled(palette);

            var next = new AvaloniaPoint(12, 12);
            fx.Window.MouseDown(next, MouseButton.Left, RawInputModifiers.None);
            fx.Window.MouseMove(next + new AvaloniaPoint(30, 30), RawInputModifiers.LeftMouseButton);
            fx.Window.MouseMove(next + new AvaloniaPoint(40, 40), RawInputModifiers.LeftMouseButton);

            Assert.Equal(GraphCanvasControl.DragOperation.Selection, fx.Control.CurrentDragOperation);
            Assert.Equal(next, fx.Control.Viewport.GraphToScreen(fx.Viewer.SelectionZone!.Value.Location));
            fx.Window.MouseUp(next + new AvaloniaPoint(40, 40), MouseButton.Left, RawInputModifiers.None);
        }
    }
}
