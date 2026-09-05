using Avalonia.Media;
using SkiaSharp;
using System;
using System.Runtime.CompilerServices;

namespace Foreman.Mac.Views {
    //Derives a text tint from an icon's dominant saturated pixel; qualities carry no colour of their own.
    internal static class IconTint {
        private const byte AlphaThreshold = 128;
        private const double SaturationThreshold = 0.2;
        private static readonly ConditionalWeakTable<SKBitmap, object> ScanCache = [];

        //test-only scan counter
        internal static int ScanCount;

        public static Color? DominantSaturatedColor(SKBitmap icon) {
            if (ScanCache.TryGetValue(icon, out object? cached))
                return (Color?)cached;

            Color? result = Scan(icon);
            ScanCache.Add(icon, result!);
            return result;
        }

        private static Color? Scan(SKBitmap icon) {
            ScanCount++;
            long r = 0, g = 0, b = 0;
            int count = 0;

            for (int y = 0; y < icon.Height; y++) {
                for (int x = 0; x < icon.Width; x++) {
                    SKColor pixel = icon.GetPixel(x, y);
                    if (pixel.Alpha <= AlphaThreshold)
                        continue;

                    byte max = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
                    byte min = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
                    double saturation = max == 0 ? 0 : (max - min) / (double)max;
                    if (saturation <= SaturationThreshold)
                        continue;

                    r += pixel.Red;
                    g += pixel.Green;
                    b += pixel.Blue;
                    count++;
                }
            }

            return count == 0 ? null : Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }
    }
}
