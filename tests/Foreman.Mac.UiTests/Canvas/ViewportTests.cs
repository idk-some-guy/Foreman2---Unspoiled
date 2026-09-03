using Foreman.Mac.Canvas;
using System;
using System.Drawing;
using Xunit;
using AvaloniaPoint = Avalonia.Point;

namespace Foreman.Mac.UiTests.Canvas {
    public class ViewportTests {
        [Theory]
        [InlineData(400, 300, 1f, 0, 0, 0, 0)]
        [InlineData(400, 300, 1f, 0, 0, 123, -45)]
        [InlineData(800, 600, 2f, 100, -50, 999, 999)]
        [InlineData(800, 600, 0.5f, -37, 89, -500, 250)]
        [InlineData(1024, 768, 0.25f, 0, 0, -1000, -1000)]
        public void ScreenToGraph_GraphToScreen_RoundTrips(double width, double height, float scale, int offsetX, int offsetY, int graphX, int graphY) {
            var viewport = new Viewport(width, height);
            SetScaleAndOffset(viewport, scale, new Point(offsetX, offsetY));

            var graphPoint = new Point(graphX, graphY);
            AvaloniaPoint screenPoint = viewport.GraphToScreen(graphPoint);
            Point roundTripped = viewport.ScreenToGraph(screenPoint);

            Assert.InRange(roundTripped.X - graphPoint.X, -1, 1);
            Assert.InRange(roundTripped.Y - graphPoint.Y, -1, 1);
        }

        [Fact]
        public void ZoomAt_KeepsCursorGraphPointFixed() {
            var viewport = new Viewport(800, 600);
            SetScaleAndOffset(viewport, 1f, new Point(50, -30));
            var cursor = new AvaloniaPoint(300, 200);

            Point graphUnderCursorBefore = viewport.ScreenToGraph(cursor);
            viewport.ZoomAt(cursor, zoomIn: true);
            Point graphUnderCursorAfter = viewport.ScreenToGraph(cursor);

            Assert.InRange(graphUnderCursorAfter.X - graphUnderCursorBefore.X, -1, 1);
            Assert.InRange(graphUnderCursorAfter.Y - graphUnderCursorBefore.Y, -1, 1);
        }

        [Fact]
        public void ZoomAt_ZoomIn_MultipliesViewScaleByStepFactor() {
            var viewport = new Viewport(800, 600);

            viewport.ZoomAt(new AvaloniaPoint(400, 300), zoomIn: true);

            Assert.True(Math.Abs(viewport.ViewScale - 1f * Viewport.ZoomStepFactor) < 0.0001f);
        }

        [Fact]
        public void ZoomAt_ZoomOut_DividesViewScaleByStepFactor() {
            var viewport = new Viewport(800, 600);

            viewport.ZoomAt(new AvaloniaPoint(400, 300), zoomIn: false);

            Assert.True(Math.Abs(viewport.ViewScale - 1f / Viewport.ZoomStepFactor) < 0.0001f);
        }

        [Fact]
        public void ZoomAt_ClampsToMinAndMaxViewScale() {
            var zoomedOutFully = new Viewport(800, 600);
            for (int i = 0; i < 100; i++)
                zoomedOutFully.ZoomAt(new AvaloniaPoint(400, 300), zoomIn: false);
            Assert.Equal(Viewport.MinViewScale, zoomedOutFully.ViewScale);

            var zoomedInFully = new Viewport(800, 600);
            for (int i = 0; i < 100; i++)
                zoomedInFully.ZoomAt(new AvaloniaPoint(400, 300), zoomIn: true);
            Assert.Equal(Viewport.MaxViewScale, zoomedInFully.ViewScale);
        }

        [Fact]
        public void PanTo_KeepsDragOriginUnderMovingCursor() {
            var viewport = new Viewport(800, 600);
            var pressPoint = new AvaloniaPoint(200, 150);
            Point dragOrigin = viewport.ScreenToGraph(pressPoint);

            var movePoint = new AvaloniaPoint(260, 210);
            viewport.PanTo(movePoint, dragOrigin);

            Point graphUnderMovePoint = viewport.ScreenToGraph(movePoint);
            Assert.InRange(graphUnderMovePoint.X - dragOrigin.X, -1, 1);
            Assert.InRange(graphUnderMovePoint.Y - dragOrigin.Y, -1, 1);
        }

        [Fact]
        public void UpdateGraphBounds_ComputesVisibleGraphBoundsFromSizeScaleAndOffset() {
            var viewport = new Viewport(400, 200);
            SetScaleAndOffset(viewport, 2f, new Point(10, 20));

            Rectangle bounds = viewport.VisibleGraphBounds;

            Assert.Equal(new Rectangle(-110, -70, 200, 100), bounds);
        }

        private static void SetScaleAndOffset(Viewport viewport, float scale, Point offset) {
            viewport.ViewScale = scale;
            viewport.ViewOffset = offset;
            viewport.UpdateGraphBounds();
        }
    }
}
