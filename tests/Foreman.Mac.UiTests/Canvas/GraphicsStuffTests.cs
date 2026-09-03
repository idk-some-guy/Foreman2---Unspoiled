using Foreman;
using Foreman.Mac.Canvas;
using SkiaSharp;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class GraphicsStuffTests {
        [Theory]
        [InlineData(0, "0")]
        [InlineData(0.05, "0.######")]
        [InlineData(-0.05, "0.######")]
        [InlineData(0.5, "0.###")]
        [InlineData(5, "0.###")]
        [InlineData(15.6789, "0.##")]
        [InlineData(156.789, "0.#")]
        [InlineData(-156.789, "0.#")]
        [InlineData(15678.9, "0")]
        [InlineData(10000, "0")]
        [InlineData(99999, "0")]
        [InlineData(100000, "0.00e0")]
        [InlineData(156789.123, "0.00e0")]
        public void DoubleToString_UsesBranchMatchingMagnitude(double value, string expectedFormat) {
            string expected = value.ToString(expectedFormat, DisplayCulture.Format);

            Assert.Equal(expected, GraphicsStuff.DoubleToString(value));
        }

        [Fact]
        public void DoubleToEnergy_BelowKilo_UsesRawValue() {
            Assert.Equal(500d.ToString("0.##", DisplayCulture.Format) + " W", GraphicsStuff.DoubleToEnergy(500, "W"));
        }

        [Fact]
        public void DoubleToEnergy_Kilo_DividesByThousand() {
            Assert.Equal(1.5d.ToString("0.##", DisplayCulture.Format) + " KW", GraphicsStuff.DoubleToEnergy(1500, "W"));
        }

        [Fact]
        public void DoubleToEnergy_Mega_DividesByMillion() {
            Assert.Equal(2.5d.ToString("0.##", DisplayCulture.Format) + " MW", GraphicsStuff.DoubleToEnergy(2500000, "W"));
        }

        [Fact]
        public void DoubleToEnergy_Giga_DividesByBillion() {
            Assert.Equal(3d.ToString("0.##", DisplayCulture.Format) + " GW", GraphicsStuff.DoubleToEnergy(3000000000, "W"));
        }

        [Fact]
        public void DoubleToEnergy_Peta_DividesByTrillion() {
            Assert.Equal(4d.ToString("0.##", DisplayCulture.Format) + " PW", GraphicsStuff.DoubleToEnergy(4000000000000, "W"));
        }

        [Fact]
        public void BuildingQuantityToText_Zero_ReturnsZero() {
            Assert.Equal("0", GraphicsStuff.BuildingQuantityToText(0, roundAssemblerCount: false));
        }

        [Fact]
        public void BuildingQuantityToText_BelowOneTenth_ReturnsLessThanOneTenth() {
            Assert.Equal("<0.1", GraphicsStuff.BuildingQuantityToText(0.05, roundAssemblerCount: false));
        }

        [Fact]
        public void BuildingQuantityToText_RegularRange_UsesOneDecimal() {
            Assert.Equal(3.456.ToString("0.#", DisplayCulture.Format), GraphicsStuff.BuildingQuantityToText(3.456, roundAssemblerCount: false));
        }

        [Fact]
        public void BuildingQuantityToText_RoundAssemblerCount_CeilsToInteger() {
            Assert.Equal("4", GraphicsStuff.BuildingQuantityToText(3.1, roundAssemblerCount: true));
        }

        [Fact]
        public void BuildingQuantityToText_AtOrAboveTenThousand_UsesScientificNotation() {
            Assert.Equal(12345.6.ToString("0.##e0", DisplayCulture.Format), GraphicsStuff.BuildingQuantityToText(12345.6, roundAssemblerCount: true));
        }

        [Theory]
        [InlineData("Short name")]
        [InlineData("A rather long node name that will not fit at the starting font size")]
        public void DrawText_MultiLine_ShrinksUntilTextFitsInsideBox(string text) {
            using var surface = SKSurface.Create(new SKImageInfo(300, 100));
            var textbox = new Rectangle(10, 10, 120, 40);

            int width = GraphicsStuff.DrawText(surface.Canvas, SKColors.Black, SKTypeface.Default, 24f, textbox, text);

            Assert.True(width <= textbox.Width);
        }

        [Fact]
        public void DrawText_SingleLine_ShrinksUntilWidthFitsInsideBox() {
            using var surface = SKSurface.Create(new SKImageInfo(300, 100));
            var textbox = new Rectangle(10, 10, 60, 40);

            int width = GraphicsStuff.DrawText(surface.Canvas, SKColors.Black, SKTypeface.Default, 24f, textbox, "A rather long single line of text", singleLine: true);

            Assert.True(width <= textbox.Width);
        }

        [Fact]
        public void DrawText_ReturnsNonNegativeWidth() {
            using var surface = SKSurface.Create(new SKImageInfo(300, 100));
            var textbox = new Rectangle(0, 0, 100, 40);

            int width = GraphicsStuff.DrawText(surface.Canvas, SKColors.Black, SKTypeface.Default, 12f, textbox, "Recipe Node");

            Assert.True(width >= 0);
        }

        [Fact]
        public void DrawText_VerticalNear_PlacesTextAtTopOfBox() {
            using var surface = SKSurface.Create(new SKImageInfo(200, 100));
            surface.Canvas.Clear(SKColors.White);
            var textbox = new Rectangle(0, 0, 200, 100);

            GraphicsStuff.DrawText(surface.Canvas, SKColors.Black, SKTypeface.Default, 16f, textbox, "Hi", verticalAlign: TextVerticalAlign.Near);

            Assert.True(AnyInkInRegion(surface, 0, 0, 200, 20));
            Assert.False(AnyInkInRegion(surface, 0, 60, 200, 40));
        }

        [Fact]
        public void DrawText_VerticalCenter_PlacesTextInMiddleOfBox() {
            using var surface = SKSurface.Create(new SKImageInfo(200, 100));
            surface.Canvas.Clear(SKColors.White);
            var textbox = new Rectangle(0, 0, 200, 100);

            GraphicsStuff.DrawText(surface.Canvas, SKColors.Black, SKTypeface.Default, 16f, textbox, "Hi", verticalAlign: TextVerticalAlign.Center);

            Assert.False(AnyInkInRegion(surface, 0, 0, 200, 20));
            Assert.True(AnyInkInRegion(surface, 0, 30, 200, 40));
        }

        [Fact]
        public void DrawText_VerticalFar_PlacesTextAtBottomOfBox() {
            using var surface = SKSurface.Create(new SKImageInfo(200, 100));
            surface.Canvas.Clear(SKColors.White);
            var textbox = new Rectangle(0, 0, 200, 100);

            GraphicsStuff.DrawText(surface.Canvas, SKColors.Black, SKTypeface.Default, 16f, textbox, "Hi", verticalAlign: TextVerticalAlign.Far);

            Assert.False(AnyInkInRegion(surface, 0, 0, 200, 60));
            Assert.True(AnyInkInRegion(surface, 0, 80, 200, 20));
        }

        [Fact]
        public void DrawText_HorizontalNear_PlacesTextAtLeftOfBox() {
            using var surface = SKSurface.Create(new SKImageInfo(200, 60));
            surface.Canvas.Clear(SKColors.White);
            var textbox = new Rectangle(0, 0, 200, 60);

            GraphicsStuff.DrawText(surface.Canvas, SKColors.Black, SKTypeface.Default, 16f, textbox, "Hi", horizontalAlign: TextHorizontalAlign.Near);

            Assert.True(AnyInkInRegion(surface, 0, 0, 20, 60));
            Assert.False(AnyInkInRegion(surface, 150, 0, 50, 60));
        }

        [Fact]
        public void DrawText_HorizontalCenter_KeepsTextAwayFromLeftEdge() {
            using var surface = SKSurface.Create(new SKImageInfo(200, 60));
            surface.Canvas.Clear(SKColors.White);
            var textbox = new Rectangle(0, 0, 200, 60);

            GraphicsStuff.DrawText(surface.Canvas, SKColors.Black, SKTypeface.Default, 16f, textbox, "Hi", horizontalAlign: TextHorizontalAlign.Center);

            Assert.False(AnyInkInRegion(surface, 0, 0, 20, 60));
        }

        //Task 2b: DrawText caches its SKPaint/SKFont per thread ([ThreadStatic]) instead of allocating fresh
        //ones per call, and every call fully overwrites color/typeface/size before drawing. This proves the
        //reuse doesn't leak a stale color from one call into the next on the same thread.
        [Fact]
        public void DrawText_SequentialCallsWithDifferentColors_DoNotLeakBetweenCalls() {
            using var surface = SKSurface.Create(new SKImageInfo(200, 60));
            surface.Canvas.Clear(SKColors.White);

            GraphicsStuff.DrawText(surface.Canvas, SKColors.Red, SKTypeface.Default, 40f, new Rectangle(0, 0, 100, 60), "R");
            GraphicsStuff.DrawText(surface.Canvas, SKColors.Blue, SKTypeface.Default, 40f, new Rectangle(100, 0, 100, 60), "B");

            Assert.True(ContainsColor(surface, 0, 0, 100, 60, SKColors.Red));
            Assert.False(ContainsColor(surface, 0, 0, 100, 60, SKColors.Blue));
            Assert.True(ContainsColor(surface, 100, 0, 100, 60, SKColors.Blue));
            Assert.False(ContainsColor(surface, 100, 0, 100, 60, SKColors.Red));
        }

        //Task 2b: DrawText's [ThreadStatic] cache is the fix for the two real render paths - the Avalonia
        //compositor's render thread for the live canvas and the UI thread for ImageExportWindow's direct
        //call - potentially drawing at the same time. Each thread gets its own paint/font instance, so
        //concurrent draws with different colors must never bleed into each other's surface.
        [Fact]
        public async Task DrawText_ConcurrentCallsFromTwoThreads_EachThreadKeepsItsOwnColor() {
            using var redSurface = SKSurface.Create(new SKImageInfo(100, 60));
            using var blueSurface = SKSurface.Create(new SKImageInfo(100, 60));
            redSurface.Canvas.Clear(SKColors.White);
            blueSurface.Canvas.Clear(SKColors.White);
            var textbox = new Rectangle(0, 0, 100, 60);
            using var barrier = new Barrier(2);

            void DrawRepeatedly(SKSurface surface, SKColor color) {
                for (int i = 0; i < 200; i++) {
                    barrier.SignalAndWait();
                    GraphicsStuff.DrawText(surface.Canvas, color, SKTypeface.Default, 40f, textbox, "X");
                    barrier.SignalAndWait();
                }
            }

            CancellationToken ct = TestContext.Current.CancellationToken;
            Task redTask = Task.Run(() => DrawRepeatedly(redSurface, SKColors.Red), ct);
            Task blueTask = Task.Run(() => DrawRepeatedly(blueSurface, SKColors.Blue), ct);
            await Task.WhenAll(redTask, blueTask);

            Assert.True(ContainsColor(redSurface, 0, 0, 100, 60, SKColors.Red));
            Assert.False(ContainsColor(redSurface, 0, 0, 100, 60, SKColors.Blue));
            Assert.True(ContainsColor(blueSurface, 0, 0, 100, 60, SKColors.Blue));
            Assert.False(ContainsColor(blueSurface, 0, 0, 100, 60, SKColors.Red));
        }

        private static bool ContainsColor(SKSurface surface, int x, int y, int width, int height, SKColor color) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            for (int px = x; px < x + width; px++)
                for (int py = y; py < y + height; py++)
                    if (pixmap.GetPixelColor(px, py) == color)
                        return true;
            return false;
        }

        private static bool AnyInkInRegion(SKSurface surface, int x, int y, int width, int height) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            for (int px = x; px < x + width; px++)
                for (int py = y; py < y + height; py++)
                    if (pixmap.GetPixelColor(px, py) != SKColors.White)
                        return true;
            return false;
        }
    }
}
