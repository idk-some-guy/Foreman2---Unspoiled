using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas.Panels;
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
            var cache = new DataCache(filterRecipes: true);
            var group = new GroupPrototype(cache, "logistics", "Logistics", "a");
            var subgroup = new SubgroupPrototype(cache, "sg", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);

            //Icons must outlive their button - each SKBitmap here backs an IconButton.Icon reference that
            //Bake's PaintOnto reads from, so they're all kept alive (and disposed together) past the fill.
            var icons = new SKBitmap[CellCount];
            var buttons = new IconButton[CellCount];
            try {
                for (int i = 0; i < CellCount; i++) {
                    var iconBitmap = new SKBitmap(32, 32);
                    using (var canvas = new SKCanvas(iconBitmap))
                        canvas.Clear(new SKColor((byte)(i * 3), (byte)(255 - i * 2), 80, 255));
                    icons[i] = iconBitmap;

                    var item = new ItemPrototype(cache, $"widget{i}", $"Widget {i}", subgroup, "a") { Available = true };
                    item.SetIconAndColor(new IconColorPair(iconBitmap, System.Drawing.Color.White));

                    var button = new IconButton();
                    button.SetPopulated(item, Color.FromRgb(60, 120, 60));
                    buttons[i] = button;
                }

                var stopwatch = Stopwatch.StartNew();
                foreach (IconButton button in buttons)
                    using (Bitmap baked = button.Bake(CellSize, CellSize))
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
