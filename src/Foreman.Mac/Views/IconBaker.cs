using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System.Collections.Generic;

namespace Foreman.Mac.Views {
    //A nested GPU lease under a composited control corrupts the frame on macOS's Metal renderer.
    internal static class IconBaker {
        public static Bitmap Bake(SKBitmap icon, int size) {
            using SKSurface surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var paint = new SKPaint { IsAntialias = true };
            surface.Canvas.DrawBitmap(icon, new SKRect(0, 0, size, size), paint);
            return SnapshotToBitmap(surface);
        }

        public static Bitmap GetOrBake(Dictionary<SKBitmap, Bitmap> cache, SKBitmap icon, int size) {
            if (!cache.TryGetValue(icon, out Bitmap? baked)) {
                baked = Bake(icon, size);
                cache[icon] = baked;
            }
            return baked;
        }

        public static Bitmap GetOrBake(Dictionary<(SKBitmap, int), Bitmap> cache, SKBitmap icon, int size) {
            var key = (icon, size);
            if (!cache.TryGetValue(key, out Bitmap? baked)) {
                baked = Bake(icon, size);
                cache[key] = baked;
            }
            return baked;
        }

        public static Bitmap SnapshotToBitmap(SKSurface surface) {
            using SKPixmap pixmap = surface.PeekPixels();
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixmap.Info.Width, pixmap.Info.Height), new Vector(96, 96), pixmap.RowBytes);
        }
    }
}
