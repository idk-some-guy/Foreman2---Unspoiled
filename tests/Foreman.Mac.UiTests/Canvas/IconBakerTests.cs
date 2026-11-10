using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Foreman.Mac.Views;
using SkiaSharp;
using System.Collections.Generic;
using Xunit;

namespace Foreman.Mac.UiTests.Canvas {
    public class IconBakerTests {
        [AvaloniaFact]
        public void GetOrBake_SameBitmapDifferentSizes_BakesTwoDistinctInstances() {
            using var icon = new SKBitmap(32, 32);
            var cache = new Dictionary<(SKBitmap, int), Bitmap>();

            Bitmap small = IconBaker.GetOrBake(cache, icon, 16);
            Bitmap large = IconBaker.GetOrBake(cache, icon, 40);

            Assert.NotSame(small, large);
            Assert.Equal(2, cache.Count);
        }

        [AvaloniaFact]
        public void GetOrBake_SameBitmapAndSize_ReturnsTheCachedInstance() {
            using var icon = new SKBitmap(32, 32);
            var cache = new Dictionary<(SKBitmap, int), Bitmap>();

            Bitmap first = IconBaker.GetOrBake(cache, icon, 16);
            Bitmap second = IconBaker.GetOrBake(cache, icon, 16);

            Assert.Same(first, second);
        }
    }
}
