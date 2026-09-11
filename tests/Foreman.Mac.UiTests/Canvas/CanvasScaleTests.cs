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
    //Task 7 fix round: canvas painters read KitSkia.TokenBridge.Current directly, no ambient transform.
    public class CanvasScaleTests : IDisposable {
        public CanvasScaleTests() => WindowScaler.SetScale(1.0);

        public void Dispose() {
            WindowScaler.SetScale(1.0);
            GC.SuppressFinalize(this);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_NodeFontSizesAndMeasuredTextHeightScale() {
            Assert.Equal(9.2f, BaseNodeElement.TitleFontSize, 3);
            Assert.Equal(10f, BaseNodeElement.BaseFontSize, 3);
            Assert.Equal(14f, BaseNodeElement.CounterBaseFontSize, 3);

            using var fontAtDefault = new SKFont(SKTypeface.Default, BaseNodeElement.TitleFontSize);
            float heightAtDefault = fontAtDefault.Metrics.Descent - fontAtDefault.Metrics.Ascent;

            WindowScaler.SetScale(1.5);

            Assert.Equal(9.2f * 1.5f, BaseNodeElement.TitleFontSize, 3);
            Assert.Equal(10f * 1.5f, BaseNodeElement.BaseFontSize, 3);
            Assert.Equal(14f * 1.5f, BaseNodeElement.CounterBaseFontSize, 3);

            using var fontScaled = new SKFont(SKTypeface.Default, BaseNodeElement.TitleFontSize);
            float heightScaled = fontScaled.Metrics.Descent - fontScaled.Metrics.Ascent;
            Assert.Equal(heightAtDefault * 1.5f, heightScaled, 2);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_ItemTabIconSizeIsExactlyOnePointFiveTimesDefault() {
            Assert.Equal(32, ItemTabElement.IconSize);

            WindowScaler.SetScale(1.5);

            Assert.Equal(48, ItemTabElement.IconSize);
        }

        //Proves UpdateValues re-derives Bounds from the current scale, so paint and hit-testing agree.
        [AvaloniaFact]
        public void SetScale_AtOneFifty_ARealItemTabsMeasuredBoundsGrow() {
            using CanvasFixture fx = CanvasFixture.WithVanillaAndOneSupplier(out _);
            BaseNodeElement element = fx.Viewer.NodeElementDictionary.Values.First();
            ItemTabElement tab = element.SubElements.OfType<ItemTabElement>().First(t => t.LinkType == LinkType.Output);
            int widthAtDefault = tab.Bounds.Width;
            int heightAtDefault = tab.Bounds.Height;

            WindowScaler.SetScale(1.5);
            element.RequestStateUpdate();
            element.PrePaint();

            Assert.True(tab.Bounds.Width > widthAtDefault);
            Assert.True(tab.Bounds.Height > heightAtDefault);
        }

        [AvaloniaFact]
        public void SetScale_AtOneFifty_CanvasTokenBridgeScaleFactorFollows() {
            Assert.Equal(1.0, TokenBridge.Current.Scale);

            WindowScaler.SetScale(1.5);

            Assert.Equal(1.5, TokenBridge.Current.Scale);
        }

        [AvaloniaFact]
        public void DefaultScale_NodeFontSizesAndItemTabIconSize_MatchTodaysConstants() {
            Assert.Equal(1.0, TokenBridge.Current.Scale);
            Assert.Equal(9.2f, BaseNodeElement.TitleFontSize, 3);
            Assert.Equal(10f, BaseNodeElement.BaseFontSize, 3);
            Assert.Equal(14f, BaseNodeElement.CounterBaseFontSize, 3);
            Assert.Equal(32, ItemTabElement.IconSize);
            Assert.Equal(3, ItemTabElement.Border);
        }

        //Task 7 fix round 2: a node's own overall Width/Height, baked once at construction with no live
        //recompute path before this round, now follow UpdateState the same way tab geometry already did.
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

        //Round 1 left AssemblerElement's own Width/Height at their construction-time size (concerns #2):
        //RecipeNodeElement.UpdateState now calls AssemblerElement.UpdateSize the same way it already
        //recomputes its own Width/Height, so the icon rect (Height mirrors AssemblerIconSize exactly) scales.
        [AvaloniaFact]
        public void SetScale_AtOneFifty_AssemblerIconRectIsOnePointFiveTimesDefault() {
            using CanvasFixture fx = CanvasFixture.WithVanilla();
            RecipeNodeElement element = CreateRecipeNode(fx);
            int heightAtDefault = element.AssemblerElement.Height;
            Assert.Equal(54, heightAtDefault);

            WindowScaler.SetScale(1.5);
            element.RequestStateUpdate();
            element.PrePaint();

            Assert.Equal(81, element.AssemblerElement.Height);
            Assert.Equal(heightAtDefault * 1.5, element.AssemblerElement.Height, 0);
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

        //Fix round 3: module icon size/spacing inside the assembler, previously fixed regardless of scale
        //(a 1x module row inside a now-1.5x assembler icon), now scale through the same UpdateSize path.
        [AvaloniaFact]
        public void SetScale_AtOneFifty_AssemblerModuleGridScalesAndRepositions() {
            int spacingAtDefault = AssemblerElement.ModuleSpacing;
            int iconSizeAtDefault = AssemblerElement.ModuleIconSize;
            DrawingPoint firstLocationAtDefault = AssemblerElement.ModuleLocations[0];
            DrawingPoint offsetAtDefault = AssemblerElement.ModuleOffset;
            Assert.Equal(12, spacingAtDefault);
            Assert.Equal(new DrawingPoint(12, 0), firstLocationAtDefault);

            WindowScaler.SetScale(1.5);

            //ModuleSpacing (12) divides cleanly by 1.5, so both it and the location it drives multiply exactly.
            Assert.Equal(18, AssemblerElement.ModuleSpacing);
            Assert.Equal(new DrawingPoint(18, 0), AssemblerElement.ModuleLocations[0]);
            //ModuleIconSize/ModuleOffset (13, 5) don't divide as cleanly - assert they grew, not a stale value.
            Assert.True(AssemblerElement.ModuleIconSize > iconSizeAtDefault);
            Assert.True(AssemblerElement.ModuleOffset.Y > offsetAtDefault.Y);

            WindowScaler.SetScale(1.0);
            Assert.Equal(spacingAtDefault, AssemblerElement.ModuleSpacing);
            Assert.Equal(firstLocationAtDefault, AssemblerElement.ModuleLocations[0]);
            Assert.Equal(iconSizeAtDefault, AssemblerElement.ModuleIconSize);
            Assert.Equal(offsetAtDefault, AssemblerElement.ModuleOffset);
        }

        //Fix round 4: the module-location arrays are read every Draw call, so they are cached per scale
        //rather than rebuilt (and reallocated) on each read.
        [AvaloniaFact]
        public void ModuleLocations_AreCachedPerScaleAndRefreshedOnScaleChange() {
            DrawingPoint[] assemblerAtDefault = AssemblerElement.ModuleLocations;
            DrawingPoint[] beaconAtDefault = BeaconElement.ModuleLocations;
            Assert.Same(assemblerAtDefault, AssemblerElement.ModuleLocations);
            Assert.Same(beaconAtDefault, BeaconElement.ModuleLocations);

            WindowScaler.SetScale(1.5);

            DrawingPoint[] assemblerAtOneFifty = AssemblerElement.ModuleLocations;
            DrawingPoint[] beaconAtOneFifty = BeaconElement.ModuleLocations;
            Assert.NotSame(assemblerAtDefault, assemblerAtOneFifty);
            Assert.NotSame(beaconAtDefault, beaconAtOneFifty);
            Assert.Equal(new DrawingPoint(18, 0), assemblerAtOneFifty[0]);
            Assert.True(beaconAtOneFifty[0].X > beaconAtDefault[0].X);
            Assert.Same(assemblerAtOneFifty, AssemblerElement.ModuleLocations);
            Assert.Same(beaconAtOneFifty, BeaconElement.ModuleLocations);

            WindowScaler.SetScale(1.0);
            Assert.Equal(assemblerAtDefault[0], AssemblerElement.ModuleLocations[0]);
            Assert.Equal(beaconAtDefault[0], BeaconElement.ModuleLocations[0]);
        }

        //Fix round 3: the full visual footprint of a recipe node - its own box plus every sub-element that
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
