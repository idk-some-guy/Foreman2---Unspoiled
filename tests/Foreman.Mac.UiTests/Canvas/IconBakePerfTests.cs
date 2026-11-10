using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Foreman.Mac.Views;
using SkiaSharp;
using System;
using System.Diagnostics;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    //Phase 7 task 3 (perf-packaging-reference.md §1b, IconButton.Bake's PNG-encode round-trip): times an
    //80-cell chooser-grid-sized fill of IconButton.Bake calls, the shape task-3-report.md's before/after
    //numbers were measured against. 24x24 matches SettingsWindow's own grid cell size.
    public class IconBakePerfTests {
        private const int CellCount = 80;
        private const int CellSize = 24;

        [AvaloniaFact]
        public void Bake_80CellFill_CompletesWithSaneMargin() {
            var icons = new SKBitmap[CellCount];
            try {
                for (int i = 0; i < CellCount; i++) {
                    var iconBitmap = new SKBitmap(32, 32);
                    using (var canvas = new SKCanvas(iconBitmap))
                        canvas.Clear(new SKColor((byte)(i * 3), (byte)(255 - i * 2), 80, 255));
                    icons[i] = iconBitmap;
                }

                var stopwatch = Stopwatch.StartNew();
                foreach (SKBitmap icon in icons)
                    using (Bitmap baked = IconBaker.Bake(icon, CellSize))
                        Assert.NotNull(baked);
                stopwatch.Stop();

                //Generous margin, not a tight regression gate - wall-clock timing is inherently noisy in CI.
                //This only needs to catch a gross regression (e.g. an accidental revert to the PNG
                //round-trip), which measured roughly twice as slow in manual runs (task-3-report.md).
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"80-cell bake fill took {stopwatch.Elapsed}");
            } finally {
                foreach (SKBitmap? icon in icons)
                    icon?.Dispose();
            }
        }
    }
}
