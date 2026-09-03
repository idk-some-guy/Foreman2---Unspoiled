using Foreman.Mac.Canvas;
using SkiaSharp;
using System.Drawing;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class GridRenderTests {
        private const int HalfSize = 50;

        private static SKSurface NewCenteredSurface() {
            SKSurface surface = SKSurface.Create(new SKImageInfo(2 * HalfSize, 2 * HalfSize));
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Translate(HalfSize, HalfSize);
            return surface;
        }

        private static SKColor SamplePixel(SKSurface surface, int graphX, int graphY) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(HalfSize + graphX, HalfSize + graphY);
        }

        private static bool ColorPresentNear(SKSurface surface, int graphX, int graphY, SKColor expected, int radius = 1) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    if (pixmap.GetPixelColor(HalfSize + graphX + dx, HalfSize + graphY + dy) == expected)
                        return true;
            return false;
        }

        [Fact]
        public void Paint_ShowGridFalse_LeavesCanvasUntouched() {
            using SKSurface surface = NewCenteredSurface();
            var grid = new GridManager { ShowGrid = false, CurrentGridUnit = 20 };

            grid.Paint(surface.Canvas, 1f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize));

            Assert.Equal(SKColors.White, SamplePixel(surface, 0, 0));
        }

        [Fact]
        public void Paint_MinorGrid_DrawsLinesAtCurrentGridUnitMultiples() {
            using SKSurface surface = NewCenteredSurface();
            var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 20 };

            grid.Paint(surface.Canvas, 1f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize));

            Assert.True(ColorPresentNear(surface, 20, 5, new SKColor(230, 230, 230)));
            Assert.True(ColorPresentNear(surface, 5, -20, new SKColor(230, 230, 230)));
            Assert.Equal(SKColors.White, SamplePixel(surface, 10, 10));
        }

        [Fact]
        public void Paint_MajorGrid_DrawsDistinctColorAtCurrentMajorGridUnitMultiples() {
            using SKSurface surface = NewCenteredSurface();
            var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 10, CurrentMajorGridUnit = 40 };

            grid.Paint(surface.Canvas, 1f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize));

            Assert.True(ColorPresentNear(surface, 40, 5, new SKColor(200, 200, 200)));
            Assert.True(ColorPresentNear(surface, 10, 5, new SKColor(230, 230, 230)));
        }

        [Fact]
        public void Paint_ZeroAxis_DrawnThroughGraphOrigin() {
            using SKSurface surface = NewCenteredSurface();
            var grid = new GridManager { ShowGrid = true, ShowZeroAxis = true, CurrentGridUnit = 0 };

            grid.Paint(surface.Canvas, 1f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize));

            Assert.True(ColorPresentNear(surface, 0, 0, new SKColor(140, 140, 140)));
            Assert.Equal(SKColors.White, SamplePixel(surface, 30, 15));
        }

        [Fact]
        public void Paint_ViewScaleTooSmallForGridUnit_FillsSolidBackgroundInstead() {
            using SKSurface surface = NewCenteredSurface();
            var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 20 };

            grid.Paint(surface.Canvas, 0.1f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize));

            Assert.Equal(new SKColor(240, 240, 240), SamplePixel(surface, 0, 0));
        }

        [Fact]
        public void AlignToGrid_SnapsToNearestGridUnitMultiple() {
            var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 20 };

            Assert.Equal(20, grid.AlignToGrid(24));
            Assert.Equal(0, grid.AlignToGrid(9));
            Assert.Equal(-20, grid.AlignToGrid(-24));
        }

        [Fact]
        public void AlignToGrid_ShowGridFalse_ReturnsOriginalValue() {
            var grid = new GridManager { ShowGrid = false, CurrentGridUnit = 20 };

            Assert.Equal(24, grid.AlignToGrid(24));
        }

        //Ports MainForm.SetDarkMode's ChangeTheme(bg, fg, this) call into ProductionGraphViewer
        //(upstream MainForm.cs:60-63): GridManager.SetGridColors(bg, fg) recolors the fallback fill
        //(_gridFillPaint) and major-gridline pen (_gridMajorPaint) - per-instance since fix-round item 2, so
        //each GridManager needs its own call and no cross-test restore is needed.
        [Fact]
        public void SetGridColors_RecolorsFallbackFillAndMajorGridPaint() {
            using SKSurface fillSurface = NewCenteredSurface();
            var fillGrid = new GridManager { ShowGrid = true, CurrentGridUnit = 20 };
            fillGrid.SetGridColors(new SKColor(23, 23, 23), new SKColor(124, 124, 124));
            fillGrid.Paint(fillSurface.Canvas, 0.1f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize));
            Assert.Equal(new SKColor(23, 23, 23), SamplePixel(fillSurface, 0, 0));

            using SKSurface majorSurface = NewCenteredSurface();
            var majorGrid = new GridManager { ShowGrid = true, CurrentGridUnit = 10, CurrentMajorGridUnit = 40 };
            majorGrid.SetGridColors(new SKColor(23, 23, 23), new SKColor(124, 124, 124));
            majorGrid.Paint(majorSurface.Canvas, 1f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize));
            Assert.True(ColorPresentNear(majorSurface, 40, 5, new SKColor(124, 124, 124)));
        }

        //Reviewer fold-in (fix-round item 2, task-2-report.md): GridPaint/GridMajorPaint/ZeroAxisPaint/
        //LockedAxisPaint used to be static, with StrokeWidth mutated per Paint() call from the caller's
        //ViewScale - shared across every GridManager, so ImageExportWindow's direct-call paint and the
        //canvas's own compositor-thread paint could corrupt each other's stroke width. LockedAxisPaint's
        //width is only reassigned inside the `if (ShowGrid)` branch, so a ShowGrid=false instance never
        //touches its own width at all - it has to fall back on whatever it was constructed with. Under the
        //old shared statics, that fallback was really "whatever the last GridManager to paint with ShowGrid
        //true left behind"; per-instance fields make it that GridManager's own untouched construction-time
        //default instead.
        [Fact]
        public void Paint_TwoInstancesDifferentViewScales_LockedAxisWidthStaysPerInstance() {
            var narrowScaleGrid = new GridManager { ShowGrid = true, CurrentGridUnit = 0, LockDragToAxis = true };
            using SKSurface narrowSurface = NewCenteredSurface();
            narrowScaleGrid.Paint(narrowSurface.Canvas, 2f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize), draggedNodeActive: true);
            int narrowWidth = CountMatchingPixelsInRow(narrowSurface, 20, new SKColor(180, 80, 80));

            var untouchedGrid = new GridManager { ShowGrid = false, LockDragToAxis = true };
            using SKSurface untouchedSurface = NewCenteredSurface();
            untouchedGrid.Paint(untouchedSurface.Canvas, 1f, new Rectangle(-HalfSize, -HalfSize, 2 * HalfSize, 2 * HalfSize), draggedNodeActive: true);
            int untouchedWidth = CountMatchingPixelsInRow(untouchedSurface, 20, new SKColor(180, 80, 80));

            //narrowScaleGrid's Paint() call (ShowGrid=true) narrows LockedAxisPaint's StrokeWidth to 3/2=1.5.
            //untouchedGrid never reassigns its own (ShowGrid=false), so it must keep its construction-time
            //default of 4 - clearly wider than narrowScaleGrid's line, regardless of paint order.
            Assert.True(untouchedWidth > narrowWidth,
                $"Expected the untouched-default instance's locked-axis line ({untouchedWidth}px) wider than the " +
                $"narrowed instance's ({narrowWidth}px) - a shared static would leave both the same width.");
        }

        private static int CountMatchingPixelsInRow(SKSurface surface, int graphY, SKColor expected) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            int count = 0;
            for (int dx = -HalfSize; dx < HalfSize; dx++)
                if (pixmap.GetPixelColor(HalfSize + dx, HalfSize + graphY) == expected)
                    count++;
            return count;
        }
    }
}
