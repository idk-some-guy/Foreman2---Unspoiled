using Foreman.Mac.Canvas;
using SkiaSharp;
using System.Drawing;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Ports MainForm.SetDarkMode/SetLightMode's canvas half (upstream MainForm.cs:36-46): the
    //ProductionGraphViewer's own BackColor swap, ported here as GraphViewer.BackgroundColor since this
    //control has no WinForms BackColor to inherit from.
    public class GraphThemeTests {
        private const int Size = 100;

        private static SKColor SamplePixel(SKSurface surface, int x, int y) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(x, y);
        }

        //Phase 7 task 2 (perf-packaging-reference.md §1b): pins GraphViewer.cs:432/458's per-frame `new
        //SKPaint` call sites' exact pixel output so converting them to cached paints (a fixed static one for
        //PaintPausedBorder, a mutate-reset per-viewer one for the lasso's ViewScale-dependent stroke width)
        //can't drift either.

        [Fact]
        public void PaintPausedBorder_DrawsFixedRedStrokeAlongTheEdge() {
            var viewer = new GraphViewer(new Viewport(Size, Size), new GridManager());

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size));
            viewer.PaintPausedBorder(surface.Canvas);

            Assert.Equal(new SKColor(255, 80, 80), SamplePixel(surface, 1, Size / 2));
        }

        [Fact]
        public void Paint_ActiveSelectionZone_DrawsLassoStrokeInPlace() {
            var viewer = new GraphViewer(new Viewport(Size, Size), new GridManager());
            viewer.Viewport.ViewScale = 1f;
            viewer.SelectionZone = new Rectangle(10, 10, 50, 50);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size));
            viewer.Paint(surface.Canvas);

            Assert.Equal(new SKColor(100, 100, 200), SamplePixel(surface, Size / 2 + 10, Size / 2 + 35));
        }

        [Fact]
        public void ApplyTheme_Dark_PaintClearsToUpstreamDarkBackground() {
            var viewer = new GraphViewer(new Viewport(Size, Size), new GridManager());
            viewer.ApplyTheme(dark: true);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size));
            viewer.Paint(surface.Canvas);

            Assert.Equal(new SKColor(23, 23, 23), SamplePixel(surface, 5, 5));
        }

        [Fact]
        public void ApplyTheme_Light_PaintClearsToWhiteBackground() {
            var viewer = new GraphViewer(new Viewport(Size, Size), new GridManager());
            viewer.ApplyTheme(dark: true);
            viewer.ApplyTheme(dark: false);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(Size, Size));
            viewer.Paint(surface.Canvas);

            Assert.Equal(SKColors.White, SamplePixel(surface, 5, 5));
        }
    }
}
