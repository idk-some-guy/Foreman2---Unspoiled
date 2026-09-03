using Foreman.Mac.Canvas.Elements;
using SkiaSharp;
using System.Drawing;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class GraphElementTests {
        private sealed class TestElement : GraphElement {
            public int DrawCallCount;
            public NodeDrawingStyle? LastStyle;

            public TestElement(GraphElement? parent = null) : base(parent) { }

            protected override void Draw(SKCanvas canvas, NodeDrawingStyle style) {
                DrawCallCount++;
                LastStyle = style;
            }
        }

        [Fact]
        public void Bounds_CenteredOnWidthAndHeight() {
            var element = new TestElement { Width = 100, Height = 40 };

            Assert.Equal(new Rectangle(-50, -20, 100, 40), element.Bounds);
        }

        [Fact]
        public void GraphToLocal_LocalToGraph_RoundTrip() {
            var element = new TestElement { Location = new Point(30, -10) };

            Point local = element.GraphToLocal(new Point(50, 20));
            Point roundTripped = element.LocalToGraph(local);

            Assert.Equal(new Point(20, 30), local);
            Assert.Equal(new Point(50, 20), roundTripped);
        }

        [Fact]
        public void GraphToLocal_ChainsThroughParentLocation() {
            var parent = new TestElement { Location = new Point(100, 100) };
            var child = new TestElement(parent) { Location = new Point(10, 10) };

            Point local = child.GraphToLocal(new Point(115, 120));

            Assert.Equal(new Point(5, 10), local);
        }

        [Fact]
        public void UpdateVisibility_OutsideZone_SetsVisibleFalse() {
            var element = new TestElement { Width = 10, Height = 10, Location = new Point(1000, 1000) };

            element.UpdateVisibility(new Rectangle(-50, -50, 100, 100));

            Assert.False(element.Visible);
        }

        [Fact]
        public void UpdateVisibility_InsideZone_SetsVisibleTrue() {
            var element = new TestElement { Width = 10, Height = 10, Location = new Point(0, 0) };

            element.UpdateVisibility(new Rectangle(-50, -50, 100, 100));

            Assert.True(element.Visible);
        }

        [Fact]
        public void Paint_VisibleElement_CallsDrawOnSelfAndSubElements() {
            var parent = new TestElement();
            var child = new TestElement(parent);
            using var surface = SKSurface.Create(new SKImageInfo(10, 10));

            parent.Paint(surface.Canvas, NodeDrawingStyle.Regular);

            Assert.Equal(1, parent.DrawCallCount);
            Assert.Equal(1, child.DrawCallCount);
            Assert.Equal(NodeDrawingStyle.Regular, child.LastStyle);
        }

        [Fact]
        public void Paint_InvisibleElement_SkipsDrawEntirely() {
            var element = new TestElement();
            element.UpdateVisibility(new Rectangle(1000, 1000, 1, 1));
            using var surface = SKSurface.Create(new SKImageInfo(10, 10));

            element.Paint(surface.Canvas, NodeDrawingStyle.Regular);

            Assert.Equal(0, element.DrawCallCount);
        }
    }
}
