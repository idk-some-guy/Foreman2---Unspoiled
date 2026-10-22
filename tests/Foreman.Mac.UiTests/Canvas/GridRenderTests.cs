using Avalonia.Headless.XUnit;
using Foreman.Mac.Canvas;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class GridRenderTests {
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

        [AvaloniaFact]
        public void Grid_PaintsAnyViewportSizeWithoutPerDotWork() {
            using var grid = new GridManager { ShowGrid = true, CurrentGridUnit = 6, CurrentMajorGridUnit = 24 };
            using SKSurface surface = SKSurface.Create(new SKImageInfo(200, 200));
            var bounds = new Rectangle(0, 0, 5000, 5000);
            grid.Paint(surface.Canvas, 1.5f, bounds);

            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
                grid.Paint(surface.Canvas, 1.5f, bounds);

            Assert.True(clock.Elapsed.TotalSeconds < 2, $"10 frames took {clock.Elapsed.TotalSeconds:F2} s");
        }
    }
}
