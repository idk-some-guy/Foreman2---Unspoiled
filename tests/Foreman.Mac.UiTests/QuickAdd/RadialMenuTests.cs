using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Foreman.Mac.Canvas;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.QuickAdd;
using Foreman.Models.Nodes;
using System;
using System.Linq;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaVector = Avalonia.Vector;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Task 4: the link-drag release chooser is a three-slice radial (Add/Passthrough/Cancel) - builds on
    //CanvasFixture's drag helpers (DragFrom/MoveTo/ClickAt/ReleaseAt), themselves new for this task.
    public sealed class RadialMenuTests {
        [AvaloniaFact]
        public void ReleasingALinkDragOnEmptyCanvasOpensTheRadialCentredOnTheReleasePoint() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            var release = new AvaloniaPoint(400, 300);
            fx.DragFrom(outputTab).To(release).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            Assert.Equal(release.X, radial.Bounds.Center.X, 1);
            Assert.Equal(release.Y, radial.Bounds.Center.Y, 1);
            Assert.Empty(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
        }

        [AvaloniaFact]
        public void HoveringAndClickingAddOpensTheLinkScopedPalette() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            RadialMenu radial = fx.Control.OpenRadial!;
            fx.MoveTo(radial.Bounds.Center + new AvaloniaVector(0, -64)); //top slice
            Assert.Equal(RadialChoice.Add, radial.HoveredSlice);

            fx.ClickAt(radial.Bounds.Center + new AvaloniaVector(0, -64));
            AddRequest request = fx.Control.ActiveAddRequest!;
            Assert.NotNull(request.Link);
            Assert.Equal(NewNodeType.Consumer, request.Link!.NodeType);
            Assert.IsType<QuickAddPalette>(fx.Control.FloatingPanelHost.Content);
        }

        [AvaloniaFact]
        public void ClickingPassthroughDropsALinkedPassthroughNode() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();

            AvaloniaPoint center = fx.Control.OpenRadial!.Bounds.Center;
            fx.ClickAt(center + new AvaloniaVector(55, 32)); //bottom-right slice

            Assert.Single(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
            Assert.Single(fx.Viewer.Graph.NodeLinks);
            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
        }

        [AvaloniaFact]
        public void CancelSlice_OutsideClick_AndEscape_AllDisposeTheDragWithoutANode() {
            foreach (string how in new[] { "cancel", "outside", "escape" }) {
                using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
                fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
                AvaloniaPoint center = fx.Control.OpenRadial!.Bounds.Center;

                if (how == "cancel")
                    fx.ClickAt(center + new AvaloniaVector(-55, 32)); //bottom-left slice
                else if (how == "outside")
                    fx.ClickAt(center + new AvaloniaVector(150, 150));
                else
                    CanvasKeys.PressOnCanvas(fx.Control, Key.Escape);

                Assert.False(fx.Control.FloatingPanelHost.IsOpen);
                Assert.Single(fx.Viewer.Graph.Nodes);
                Assert.Null(fx.Control.DraggedLink);
            }
        }

        [AvaloniaFact]
        public void CmdHeldAtReleaseStillDropsAPassthroughWithoutTheRadial() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release(KeyModifiers.Meta);

            Assert.Null(fx.Control.OpenRadial);
            Assert.Single(fx.Viewer.Graph.Nodes.OfType<PassthroughNode>());
        }

        [AvaloniaFact]
        public void EscapeDuringALinkDragCancelsIt() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)); //no release
            CanvasKeys.PressOnCanvas(fx.Control, Key.Escape);
            Assert.Null(fx.Control.DraggedLink);

            fx.ReleaseAt(new AvaloniaPoint(400, 300));
            Assert.Null(fx.Control.OpenRadial);
            Assert.Single(fx.Viewer.Graph.Nodes);
        }

        [AvaloniaFact]
        public void PaletteFromTheRadialPlacesALinkedNode_AndTheNextOneToTheRight() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            fx.ClickAt(fx.Control.OpenRadial!.Bounds.Center + new AvaloniaVector(0, -64));

            var box = fx.Control.OpenPalette!.FindControl<TextBox>("SearchBox")!;
            CanvasKeys.KeyDownOn(box, Key.Enter); //top row: the dragged item itself (Output role for a Consumer request)
            CanvasKeys.KeyDownOn(box, Key.Enter);

            Assert.Equal(2, fx.Viewer.Graph.NodeLinks.Count());
            var placed = fx.Viewer.Graph.Nodes.Where(n => n is not SupplierNode || n.Location.Y != 0).OrderBy(n => n.Location.X).ToList();
            Assert.Equal(120, placed[1].Location.X - placed[0].Location.X, 0.5);
        }

        [Fact]
        public void ResolveChoice_IsNullInTheHubAndBeyondTheRim_AndPicksBySliceAngleInBetween() {
            for (int radius = 0; radius <= 35; radius++)
                foreach (double angle in SampleAngles)
                    Assert.Null(RadialMenu.ResolveChoice(PointAt(radius, angle)));

            for (int radius = 37; radius <= 91; radius++)
                foreach (double angle in SampleAngles)
                    Assert.Equal(ExpectedSlice(angle), RadialMenu.ResolveChoice(PointAt(radius, angle)));

            for (int radius = 93; radius <= 150; radius += 10)
                foreach (double angle in SampleAngles)
                    Assert.Null(RadialMenu.ResolveChoice(PointAt(radius, angle)));
        }

        [AvaloniaFact]
        public void AddSlicePathBoundsMatchThe92RadiusExtents() {
            var radial = new RadialMenu();
            Path addSlice = radial.Children.OfType<Path>().First(p => p.Classes.Contains("foreman-radial-slice"));
            Avalonia.Rect bounds = addSlice.Data!.Bounds;

            Assert.Equal(20.3, bounds.Left, 1);
            Assert.Equal(179.7, bounds.Right, 1);
            Assert.Equal(8, bounds.Top, 1);
        }

        private static readonly double[] SampleAngles = [.. Enumerable.Range(0, 24).Select(i => 7.5 + (i * 15.0))]; //offset off the slice boundaries (multiples of 30)

        private static AvaloniaPoint PointAt(double radius, double angleDeg) {
            double angleRad = angleDeg * Math.PI / 180;
            return new AvaloniaPoint(100 + (radius * Math.Cos(angleRad)), 100 + (radius * Math.Sin(angleRad)));
        }

        private static RadialChoice ExpectedSlice(double angleDeg) {
            double normalized = ((angleDeg % 360) + 360) % 360;
            if (normalized is >= 210 and < 330)
                return RadialChoice.Add;
            if (normalized is >= 90 and < 210)
                return RadialChoice.Cancel;
            return RadialChoice.Passthrough;
        }
    }
}
