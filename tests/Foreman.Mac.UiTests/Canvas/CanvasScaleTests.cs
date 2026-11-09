using Avalonia.Headless.XUnit;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using System;
using System.Drawing;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    [Collection(ScaleTestGroup.Name)]
    public class CanvasScaleTests : IDisposable {
        public CanvasScaleTests() => WindowScaler.SetScale(1.0);

        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_RecipeNodeBoundsAreOnePointFiveTimesDefault() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement element = CreateRecipeNode(fx);
            int widthAtDefault = element.Bounds.Width;
            int heightAtDefault = element.Bounds.Height;

            WindowScaler.SetScale(1.5);
            element.RequestStateUpdate();
            element.PrePaint();

            Assert.Equal(widthAtDefault * 1.5, element.Bounds.Width, 0);
            Assert.Equal(heightAtDefault * 1.5, element.Bounds.Height, 0);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_APointJustPastTheDefaultEdgeMissesThenHitsAtTheScaledEdge() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement element = CreateRecipeNode(fx);
            var pointJustPastDefaultEdge = new DrawingPoint((element.Bounds.Width / 2) + 5, 0);
            Assert.False(element.ContainsPoint(pointJustPastDefaultEdge));

            WindowScaler.SetScale(1.5);
            element.RequestStateUpdate();
            element.PrePaint();

            Assert.True(element.ContainsPoint(pointJustPastDefaultEdge));
        }

        [AvaloniaFact]
        public void SetScale_BackToOneHundred_RestoresOriginalRecipeNodeBounds() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement element = CreateRecipeNode(fx);
            int widthAtDefault = element.Bounds.Width;
            int heightAtDefault = element.Bounds.Height;

            WindowScaler.SetScale(1.5);
            element.RequestStateUpdate();
            element.PrePaint();

            WindowScaler.SetScale(1.0);
            element.RequestStateUpdate();
            element.PrePaint();

            Assert.Equal(widthAtDefault, element.Bounds.Width);
            Assert.Equal(heightAtDefault, element.Bounds.Height);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_RecipeNodeFullPaintedBoundsAreOnePointFiveTimesDefault() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement element = CreateRecipeNode(fx);
            Rectangle envelopeAtDefault = FullPaintedBounds(element);

            WindowScaler.SetScale(1.5);
            element.RequestStateUpdate();
            element.PrePaint();

            Rectangle envelopeAtOneFifty = FullPaintedBounds(element);

            double expectedWidth = envelopeAtDefault.Width * 1.5;
            double expectedHeight = envelopeAtDefault.Height * 1.5;
            Assert.InRange(envelopeAtOneFifty.Width, expectedWidth - 8, expectedWidth + 8);
            Assert.InRange(envelopeAtOneFifty.Height, expectedHeight - 8, expectedHeight + 8);
        }

        private static Rectangle FullPaintedBounds(BaseNodeElement node) {
            Rectangle envelope = node.Bounds;
            foreach (var sub in node.SubElements) {
                Rectangle subBounds = sub.Bounds;
                subBounds.Offset(sub.Location);
                envelope = Rectangle.Union(envelope, subBounds);
            }
            return envelope;
        }

        private static RecipeNodeElement CreateRecipeNode(CanvasFixture fx) {
            IRecipe recipe = fx.Cache.Recipes["iron-gear-wheel"];
            var pair = new RecipeQualityPair(recipe, fx.Cache.DefaultQuality!);
            NodeId nodeId = fx.Viewer.Session.Editor.CreateRecipeNode(pair, new DrawingPoint(0, 0));
            var element = (RecipeNodeElement)fx.Viewer.NodeElementDictionary[nodeId];
            element.RequestStateUpdate();
            element.PrePaint();
            return element;
        }
    }
}
