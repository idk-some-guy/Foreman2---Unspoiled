using Avalonia.Headless.XUnit;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Models;
using SkiaSharp;
using System;
using System.Drawing;
using System.Linq;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    //Canvas painters read KitSkia.TokenBridge.Current directly, no ambient transform.
    [Collection(ScaleTestGroup.Name)]
    public class CanvasScaleTests : IDisposable {
        public CanvasScaleTests() => WindowScaler.SetScale(1.0);

        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        //A node's own overall Width/Height follows UpdateState the same way tab geometry already did.
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

        //Proves hit-testing reads the same multiplied Bounds paint uses, not a value stuck at construction:
        //a point just past the 100% edge misses, then hits once the box has genuinely grown around it.
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

        //Switching back to 100% must restore the exact original bounds, not merely shrink toward them.
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

        //The full visual footprint of a recipe node - its own box plus every sub-element that
        //paints outside it (assembler/beacon icons offset to the side, item tabs above/below) - must scale
        //together, or the pieces drift apart relative to each other at higher scale even though each one's
        //own size is individually correct.
        [AvaloniaFact]
        public void SetScale_AtOneFifty_RecipeNodeFullPaintedBoundsAreOnePointFiveTimesDefault() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement element = CreateRecipeNode(fx);
            Rectangle envelopeAtDefault = FullPaintedBounds(element);

            WindowScaler.SetScale(1.5);
            element.RequestStateUpdate();
            element.PrePaint();

            Rectangle envelopeAtOneFifty = FullPaintedBounds(element);

            //A tolerance, not exact equality: five-plus sub-elements (assembler, beacon, error notice, each
            //item tab) each independently truncate their own Width/2 and Height/2 to ints, so the union's
            //extreme edges compound a few pixels off an exact 1.5x multiple even though every individual
            //element's own size scales precisely.
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
