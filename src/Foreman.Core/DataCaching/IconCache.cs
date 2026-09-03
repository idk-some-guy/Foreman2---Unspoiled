using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.DataCaching {
    public record struct IconColorPair(SKBitmap? Icon, Color Color);

    public static class IconCache {
        private static readonly SKSamplingOptions BilinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

        public static SKBitmap UnknownIcon {
            get {
                field ??= GetIcon(Path.Combine("Graphics", "UnknownIcon.png"), 32);
                return field;
            }
        }
        public static SKBitmap? SpoilageIcon {
            get {
                field ??= GetIcon(Path.Combine("Graphics", "SpoilAssembler.png"), 96);
                return field;
            }
        }
        public static SKBitmap? PlantingIcon {
            get {
                field ??= GetIcon(Path.Combine("Graphics", "PlantAssembler.png"), 96);
                return field;
            }
        }

        //A relative `path` (every Foreman-bundled Graphics/*.png caller in this file and DataCacheBootstrap.cs)
        //resolves against AppPaths.ExecutableDirectory here, not SKBitmap.Decode's own default of the
        //process's current working directory - matching Application.StartupPath, which is what upstream's
        //own relative Graphics path resolves against on Windows. Path.Combine leaves an already-rooted path
        //untouched, so an absolute `path` (none of today's callers pass one) still works unchanged.
        public static SKBitmap GetIcon(string path, int size) {
            string resolvedPath = Path.Combine(AppPaths.ExecutableDirectory, path);
            SKBitmap? bmp = null;
            try {
                using SKBitmap image = SKBitmap.Decode(resolvedPath) ?? throw new InvalidDataException($"Could not decode image at '{resolvedPath}'.");
                bmp = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(bmp))
                using (var sourceImage = SKImage.FromBitmap(image))
                    canvas.DrawImage(sourceImage, new SKRect(0, 0, size * image.Width / image.Height, size), BilinearSampling);
                return bmp;
            } catch (Exception ex) {
                bmp?.Dispose();
                ErrorLogging.LogException(ex, string.Format(CultureInfo.InvariantCulture, "IconCache.GetIcon failed for '{0}' (size {1})", path, size));
                return new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
            }
        }

        public static SKBitmap CombineIcons(SKBitmap? aIcon, SKBitmap? bIcon, int size, bool diagonalSlice = true) {
            var result = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(result)) {
                using (var tlPath = new SKPath()) {
                    tlPath.MoveTo(0, 0);
                    tlPath.LineTo(0, size);
                    tlPath.LineTo(size, 0);
                    tlPath.Close();
                    if (diagonalSlice) {
                        canvas.Save();
                        canvas.ClipPath(tlPath);
                    }
                    if (aIcon != null)
                        using (var sourceImage = SKImage.FromBitmap(aIcon))
                            canvas.DrawImage(sourceImage, new SKRect(0, 0, size, size), BilinearSampling);
                    if (diagonalSlice)
                        canvas.Restore();
                }

                using var trPath = new SKPath();
                trPath.MoveTo(size, size);
                trPath.LineTo(0, size);
                trPath.LineTo(size, 0);
                trPath.Close();
                if (diagonalSlice) {
                    canvas.Save();
                    canvas.ClipPath(trPath);
                }
                if (bIcon != null)
                    using (var sourceImage = SKImage.FromBitmap(bIcon))
                        canvas.DrawImage(sourceImage, new SKRect(0, 0, size, size), BilinearSampling);
                if (diagonalSlice)
                    canvas.Restore();
            }
            return result;
        }

        public static Task SaveIconCacheAsync(string path, Dictionary<string, IconColorPair> iconCache, CancellationToken cancellationToken = default) =>
            ForemanIconCacheFile.WriteAsync(path, iconCache, cancellationToken);

        public static async Task<Dictionary<string, IconColorPair>> LoadIconCache(string path, IProgress<KeyValuePair<int, string>> progress, int startingPercent, int endingPercent) {
            try {
                if (!File.Exists(path)) {
                    ErrorLogging.LogLine($"Icon cache not found at \"{path}\" - loading with no icons.");
                    return [];
                }
                if (!ForemanIconCacheFile.IsFoicFile(path))
                    throw new InvalidDataException("Unrecognized icon cache format.");

                int lastReportedPercent = startingPercent - 1;
                var iconProgress = new Progress<(int Decoded, int Total)>(state => {
                    int percent = startingPercent + (int)((endingPercent - startingPercent) * (double)state.Decoded / Math.Max(state.Total, 1));
                    if (percent <= lastReportedPercent)
                        return;
                    lastReportedPercent = percent;
                    progress.Report(new(percent, "Loading Icons..."));
                });
                return await ForemanIconCacheFile.ReadAsync(path, iconProgress).ConfigureAwait(false);
            } catch (Exception ex) {
                ErrorLogging.LogException(ex, $"Failed to load icon cache from {path}");
                ErrorLogging.LogLine($"Icon cache unreadable: the icon cache \"{Path.GetFileName(path)}\" could not be read. Re-import the preset to rebuild the cache.");
                return [];
            }
        }
    }
}
