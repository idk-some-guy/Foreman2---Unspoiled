using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Foreman.Mac.Canvas;
using Foreman.Mac.Kit;
using Foreman.Mac.UiTests.support;
using System;
using System.Drawing;
using Xunit;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaWindow = Avalonia.Controls.Window;

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

        [Fact]
        public void SetAbsoluteZoom_SetsExactViewScale() {
            var viewport = new Viewport(800, 600);
            viewport.ZoomAt(new AvaloniaPoint(400, 300), zoomIn: true);

            viewport.SetAbsoluteZoom(new AvaloniaPoint(400, 300), 1f);

            Assert.Equal(1f, viewport.ViewScale);
        }

        [Fact]
        public void SetAbsoluteZoom_KeepsAnchorGraphPointFixed() {
            var viewport = new Viewport(800, 600);
            SetScaleAndOffset(viewport, 0.5f, new Point(50, -30));
            var anchor = new AvaloniaPoint(300, 200);

            Point graphUnderAnchorBefore = viewport.ScreenToGraph(anchor);
            viewport.SetAbsoluteZoom(anchor, 2f);
            Point graphUnderAnchorAfter = viewport.ScreenToGraph(anchor);

            Assert.InRange(graphUnderAnchorAfter.X - graphUnderAnchorBefore.X, -1, 1);
            Assert.InRange(graphUnderAnchorAfter.Y - graphUnderAnchorBefore.Y, -1, 1);
        }

        [Fact]
        public void SetAbsoluteZoom_ClampsToMinAndMaxViewScale() {
            var viewport = new Viewport(800, 600);

            viewport.SetAbsoluteZoom(new AvaloniaPoint(400, 300), 999f);
            Assert.Equal(Viewport.MaxViewScale, viewport.ViewScale);

            viewport.SetAbsoluteZoom(new AvaloniaPoint(400, 300), 0f);
            Assert.Equal(Viewport.MinViewScale, viewport.ViewScale);
        }

        [Fact]
        public void FitToBounds_ScalesSoBoundsFitInsideViewport() {
            var viewport = new Viewport(800, 600);

            viewport.FitToBounds(new Rectangle(-100, -100, 400, 200));

            Assert.True(Math.Abs(viewport.ViewScale - 2f) < 0.0001f);
        }

        [Fact]
        public void FitToBounds_CentersBoundsInViewport() {
            var viewport = new Viewport(800, 600);

            viewport.FitToBounds(new Rectangle(-100, -100, 400, 200));
            AvaloniaPoint boundsCenterOnScreen = viewport.GraphToScreen(new Point(100, 0));

            Assert.InRange(boundsCenterOnScreen.X - 400, -1, 1);
            Assert.InRange(boundsCenterOnScreen.Y - 300, -1, 1);
        }

        [Fact]
        public void FitToBounds_EmptyBounds_LeavesViewportUnchanged() {
            var viewport = new Viewport(800, 600);
            SetScaleAndOffset(viewport, 1.5f, new Point(10, 20));

            viewport.FitToBounds(Rectangle.Empty);

            Assert.Equal(1.5f, viewport.ViewScale);
            Assert.Equal(new Point(10, 20), viewport.ViewOffset);
        }

        [Fact]
        public void FitToBounds_ClampsToMaxViewScale() {
            var viewport = new Viewport(800, 600);

            viewport.FitToBounds(new Rectangle(0, 0, 1, 1));

            Assert.Equal(Viewport.MaxViewScale, viewport.ViewScale);
        }

        [Fact]
        public void PanIntoView_MatchWithinTheMarginOfAnEdge_PansTheShortestDistance() {
            var viewport = new Viewport(800, 600);
            var nodeBounds = new Rectangle(300, -50, 40, 40);

            viewport.PanIntoView(nodeBounds, margin: 80);

            Assert.Equal(new Point(-20, 0), viewport.ViewOffset);
            Rectangle inset = Rectangle.Inflate(viewport.VisibleGraphBounds, -80, -80);
            Assert.True(inset.Contains(nodeBounds));
        }

        [Fact]
        public void PanIntoView_MatchFullyInsideTheMarginInset_DoesNotPan() {
            var viewport = new Viewport(800, 600);
            Point offsetBefore = viewport.ViewOffset;

            viewport.PanIntoView(new Rectangle(-100, -100, 50, 50), margin: 80);

            Assert.Equal(offsetBefore, viewport.ViewOffset);
        }

        [AvaloniaFact]
        public void PanIntoView_OnTheCanvas_AnimatesTheOffset_AndSettlesOnTheRuledTarget() {
            bool wasReduced = Motion.ReduceMotion;
            Motion.ReduceMotion = false;
            try {
                GraphCanvasControl control = ShownCanvas();
                var nodeBounds = new Rectangle(control.Viewport.VisibleGraphBounds.Right - 60, -20, 40, 40);
                Point start = control.Viewport.ViewOffset;
                Point target = Assert.IsType<Point>(control.Viewport.PanIntoViewTarget(nodeBounds, 80));
                Assert.NotEqual(start, target);

                control.PanIntoView(nodeBounds, 80);

                Assert.Equal(start, control.Viewport.ViewOffset);
                AnimationClock.Settle(400);
                Assert.Equal(target, control.Viewport.ViewOffset);
            } finally {
                Motion.ReduceMotion = wasReduced;
            }
        }

        [AvaloniaFact]
        public void PanIntoView_UnderReduceMotion_JumpsInOneStep_AndTweensAgainWhenTheFlagClears() {
            bool wasReduced = Motion.ReduceMotion;
            try {
                GraphCanvasControl control = ShownCanvas();
                Motion.ReduceMotion = true;
                var rightEdge = new Rectangle(control.Viewport.VisibleGraphBounds.Right - 60, -20, 40, 40);
                Point jumpTarget = Assert.IsType<Point>(control.Viewport.PanIntoViewTarget(rightEdge, 80));

                control.PanIntoView(rightEdge, 80);

                Assert.Equal(jumpTarget, control.Viewport.ViewOffset);

                Motion.ReduceMotion = false;
                var leftEdge = new Rectangle(control.Viewport.VisibleGraphBounds.Left + 20, -20, 40, 40);
                Point start = control.Viewport.ViewOffset;
                Point tweenTarget = Assert.IsType<Point>(control.Viewport.PanIntoViewTarget(leftEdge, 80));

                control.PanIntoView(leftEdge, 80);

                Assert.Equal(start, control.Viewport.ViewOffset);
                AnimationClock.Settle(400);
                Assert.Equal(tweenTarget, control.Viewport.ViewOffset);
            } finally {
                Motion.ReduceMotion = wasReduced;
            }
        }

        private static GraphCanvasControl ShownCanvas() {
            var control = new GraphCanvasControl();
            var window = new AvaloniaWindow { Width = 800, Height = 600, Content = control };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return control;
        }

        [Fact]
        public void PanIntoView_MarginScalesWithViewScale() {
            var viewport = new Viewport(800, 600);
            SetScaleAndOffset(viewport, 2f, new Point(0, 0));

            viewport.PanIntoView(new Rectangle(150, -20, 20, 20), margin: 80);

            Assert.Equal(new Point(-10, 0), viewport.ViewOffset);
        }
    }
}
