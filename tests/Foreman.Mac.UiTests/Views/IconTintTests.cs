using Avalonia.Media;
using Foreman.Mac.Views;
using SkiaSharp;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Synthetic bitmaps stand in for a quality's icon, isolating IconTint from DataCache.
    public class IconTintTests {
        private static SKBitmap NewUniformBitmap(byte r, byte g, byte b, byte a) {
            var bitmap = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(r, g, b, a));
            return bitmap;
        }

        [Fact]
        public void AllGreenOpaqueBitmap_ReturnsGreen() {
            SKBitmap bitmap = NewUniformBitmap(r: 0, g: 255, b: 0, a: 255);

            Color? result = IconTint.DominantSaturatedColor(bitmap);

            Assert.Equal(Color.FromRgb(0, 255, 0), result);
        }

        [Fact]
        public void GreyOnlyBitmap_ReturnsNull() {
            SKBitmap bitmap = NewUniformBitmap(r: 128, g: 128, b: 128, a: 255);

            Color? result = IconTint.DominantSaturatedColor(bitmap);

            Assert.Null(result);
        }

        [Fact]
        public void MostlyTransparentSaturatedBitmap_IgnoresTheTransparentPixels() {
            var bitmap = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            bitmap.SetPixel(0, 0, new SKColor(0, 255, 0, 10));
            bitmap.SetPixel(1, 0, new SKColor(255, 0, 0, 255));

            Color? result = IconTint.DominantSaturatedColor(bitmap);

            Assert.Equal(Color.FromRgb(255, 0, 0), result);
        }

        [Fact]
        public void SameBitmap_CalledTwice_ReturnsSameValue_AndScansOnce() {
            SKBitmap bitmap = NewUniformBitmap(r: 0, g: 255, b: 0, a: 255);
            int before = IconTint.ScanCount;

            Color? first = IconTint.DominantSaturatedColor(bitmap);
            Color? second = IconTint.DominantSaturatedColor(bitmap);

            Assert.Equal(first, second);
            Assert.Equal(before + 1, IconTint.ScanCount);
        }
    }
}
