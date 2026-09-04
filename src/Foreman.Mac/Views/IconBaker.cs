using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System.Collections.Generic;

namespace Foreman.Mac.Views {
    //Shared by InspectorView/SettingsWindow/PickGrid/GraphSummary/PresetComparator/SciencePacksWindow: draws
    //an SKBitmap onto a fresh Skia surface at a fixed size and snapshots it into an Avalonia Bitmap, off the
    //GPU lease every other frame-live control
    //takes (see IconButton's comment on nested Skia leases corrupting the frame on macOS). Each caller keeps
    //its own cache dictionary - bake size varies per call site, so a shared cache keyed only on the source
    //SKBitmap would collide.
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

        public static Bitmap SnapshotToBitmap(SKSurface surface) {
            using SKPixmap pixmap = surface.PeekPixels();
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixmap.Info.Width, pixmap.Info.Height), new Vector(96, 96), pixmap.RowBytes);
        }
    }
}
