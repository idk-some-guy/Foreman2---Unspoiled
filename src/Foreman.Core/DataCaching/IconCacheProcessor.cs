using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.DataCaching {

    public record struct IconInfo(string IconPath, int IconSize) {
        public double IconScale { get; set; } = 1;
        public Point IconOffset { get; set; } = new Point(0, 0);
        public Color IconTint { get; set; } = IconCacheProcessor.NoTint;

        public void SetIconTint(double a, double r, double g, double b) {
            a = (a <= 1 ? a * 255 : a);
            r = (r <= 1 ? r * 255 : r);
            g = (g <= 1 ? g * 255 : g);
            b = (b <= 1 ? b * 255 : b);
            IconTint = Color.FromArgb((int)a, (int)r, (int)g, (int)b);
        }
    }

    public class IconCacheProcessor : IDisposable {
        internal static readonly Color NoTint = Color.White;
        private static readonly SKSamplingOptions HighQualitySampling = new(SKCubicResampler.Mitchell);

        public int TotalPathCount { get; private set; }
        public int FailedPathCount { get; private set; }

        private readonly Dictionary<string, IconColorPair> myIconCache;

        private readonly Dictionary<string, string> folderLinks;
        private Dictionary<string, ZipArchiveEntry>? archiveFileLinks;
        private readonly List<ZipArchive> openedArchives = [];
        private readonly Dictionary<string, SKBitmap?> bitmapCache = []; //just so we dont have to load the same file multiple times

        public IconCacheProcessor() {
            TotalPathCount = 0;
            FailedPathCount = 0;

            myIconCache = [];

            folderLinks = [];
            archiveFileLinks = [];
        }

        public bool PrepareModPaths(Dictionary<string, string> modSet, string modsPath, string dataPath, CancellationToken token) {
            ObjectDisposedException.ThrowIf(disposedValue, this);
            folderLinks.Clear();
            archiveFileLinks?.Clear();
            bitmapCache.Clear();

            //factorio checks for foldeer <name>_<version>, then folder <name> then zip <name>_<version>
            //if zip, then the actual files can either be in the root of zip, or in <name> foler, or in <name>_<version> folder
            //NOTE: versions are of type v1.v2.v3 where each number can have any amount of leading zeros
            foreach (KeyValuePair<string, string> mod in modSet) {
                if (token.IsCancellationRequested)
                    return false;

                string versionMatch = string.Join(".", mod.Value.Split('.').Select(s => "0*" + int.Parse(s, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));

                string[] folders = Directory.GetDirectories(modsPath);
                string[] files = Directory.GetFiles(modsPath);

                var foundFolder = folders.FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f).ToLowerInvariant(), string.Format(CultureInfo.InvariantCulture, "{0}_{1}", mod.Key, versionMatch)));
                foundFolder ??= folders.FirstOrDefault(f => string.Equals(Path.GetFileName(f), mod.Key, StringComparison.OrdinalIgnoreCase));

                if (foundFolder != null)
                    folderLinks.Add("__" + mod.Key.ToLowerInvariant() + "__", foundFolder);
                else {
                    var foundFile = files.FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f).ToLowerInvariant(), string.Format(CultureInfo.InvariantCulture, "{0}_{1}.zip", mod.Key, versionMatch)));
                    if (foundFile == null) {
                        if (!string.Equals(mod.Key, "core", StringComparison.OrdinalIgnoreCase) && !string.Equals(mod.Key, "base", StringComparison.OrdinalIgnoreCase) && !string.Equals(mod.Key, "elevated-rails", StringComparison.OrdinalIgnoreCase) && !string.Equals(mod.Key, "quality", StringComparison.OrdinalIgnoreCase) && !string.Equals(mod.Key, "space-age", StringComparison.OrdinalIgnoreCase))
                            return false;
                        continue;
                    }

                    //for zip files, since we have to iterate through them for each file we might as well make a full link of every possible filepath to given entry
                    ZipArchive zip = ZipFile.Open(foundFile, ZipArchiveMode.Read);
                    openedArchives.Add(zip);
                    foreach (ZipArchiveEntry zentity in zip.Entries) {
                        if (string.IsNullOrEmpty(zentity.Name))
                            continue; //folder

                        var brokenPath = new LinkedList<string>();
                        string filePath = zentity.FullName;
                        while (!string.IsNullOrEmpty(filePath) && Path.GetFileName(filePath) is string fileName && Path.GetDirectoryName(filePath) is string dirName) {
                            brokenPath.AddFirst(fileName);
                            filePath = dirName;
                        }
                        brokenPath.First?.Value = "__" + mod.Key.ToLowerInvariant() + "__";
                        archiveFileLinks?.Add(Path.Combine([.. brokenPath]).ToLowerInvariant(), zentity);
                    }
                }
            }
            folderLinks.Add("__core__", Path.Combine(dataPath, "core"));
            folderLinks.Add("__base__", Path.Combine(dataPath, "base"));
            folderLinks.Add("__elevated-rails__", Path.Combine(dataPath, "elevated-rails"));
            folderLinks.Add("__quality__", Path.Combine(dataPath, "quality"));
            folderLinks.Add("__space-age__", Path.Combine(dataPath, "space-age"));

            return true;
        }

        public async Task<bool> CreateIconCache(JsonObject iconJObject, string cachePath, IProgress<KeyValuePair<int, string>> progress, int startingPercent, int endingPercent, CancellationToken token) {
            ObjectDisposedException.ThrowIf(disposedValue, this);
            TotalPathCount = 0;
            FailedPathCount = 0;

            myIconCache.Clear();
            bitmapCache.Clear();

            int totalCount =
                PresetJson.CountArray(iconJObject, "technologies") +
                PresetJson.CountArray(iconJObject, "recipes") +
                PresetJson.CountArray(iconJObject, "items") +
                PresetJson.CountArray(iconJObject, "fluids") +
                PresetJson.CountArray(iconJObject, "entities") +
                PresetJson.CountArray(iconJObject, "groups") +
                PresetJson.CountArray(iconJObject, "qualities");

            progress.Report(new(startingPercent, "Creating icons."));
            int counter = 0;
            foreach (JsonNode iconJToken in PresetJson.EnumerateArray(iconJObject, "technologies")) {
                if (token.IsCancellationRequested)
                    return false;
                progress.Report(new(startingPercent + (endingPercent - startingPercent) * counter++ / totalCount, ""));
                ProcessIcon(iconJToken, 256);
            }
            foreach (JsonNode iconJToken in PresetJson.EnumerateArray(iconJObject, "recipes")) {
                if (token.IsCancellationRequested)
                    return false;
                progress.Report(new(startingPercent + (endingPercent - startingPercent) * counter++ / totalCount, ""));
                ProcessIcon(iconJToken, 32);
            }
            foreach (JsonNode iconJToken in PresetJson.EnumerateArray(iconJObject, "items")) {
                if (token.IsCancellationRequested)
                    return false;
                progress.Report(new(startingPercent + (endingPercent - startingPercent) * counter++ / totalCount, ""));
                ProcessIcon(iconJToken, 32);
            }
            foreach (JsonNode iconJToken in PresetJson.EnumerateArray(iconJObject, "fluids")) {
                if (token.IsCancellationRequested)
                    return false;
                progress.Report(new(startingPercent + (endingPercent - startingPercent) * counter++ / totalCount, ""));
                ProcessIcon(iconJToken, 32);
            }
            foreach (JsonNode iconJToken in PresetJson.EnumerateArray(iconJObject, "entities")) {
                if (token.IsCancellationRequested)
                    return false;
                progress.Report(new(startingPercent + (endingPercent - startingPercent) * counter++ / totalCount, ""));
                ProcessIcon(iconJToken, 64);
            }
            foreach (JsonNode iconJToken in PresetJson.EnumerateArray(iconJObject, "groups")) {
                if (token.IsCancellationRequested)
                    return false;
                progress.Report(new(startingPercent + (endingPercent - startingPercent) * counter++ / totalCount, ""));
                ProcessIcon(iconJToken, 64);
            }
            foreach (JsonNode iconJToken in PresetJson.EnumerateArray(iconJObject, "qualities")) {
                if (token.IsCancellationRequested)
                    return false;
                progress.Report(new(startingPercent + (endingPercent - startingPercent) * counter++ / totalCount, ""));
                ProcessIcon(iconJToken, 32);
            }

            await IconCache.SaveIconCacheAsync(cachePath, myIconCache, token).ConfigureAwait(false);

            return (FailedPathCount == 0);
        }

        private void ProcessIcon(JsonNode objJToken, int defaultIconSize) {
            if (PresetJson.GetNode(objJToken, "icon_data") is not JsonNode iconDataJToken)
                return;

            string? iconName = PresetJson.GetString(objJToken, "icon_name");
            if (iconName is null || myIconCache.ContainsKey(iconName))
                return;

            string? mainIconPath = PresetJson.GetString(iconDataJToken, "icon");
            int baseIconSize = PresetJson.GetInt32(iconDataJToken, "icon_size") ?? 32;

            IconInfo iicon = mainIconPath is not null
                ? new IconInfo(mainIconPath, baseIconSize)
                : new IconInfo("", baseIconSize);
            if (mainIconPath is not null)
                iicon.IconScale = defaultIconSize / iicon.IconSize;

            var layerIcons = new List<IconInfo>();
            foreach (JsonNode iconJToken in PresetJson.EnumerateArray(iconDataJToken, "icons")) {
                if (PresetJson.GetString(iconJToken, "icon") is not string icon ||
                    PresetJson.GetNode(iconJToken, "shift") is not JsonArray shift ||
                    shift.Count < 2 ||
                    PresetJson.GetNode(iconJToken, "tint") is not JsonArray tint ||
                    tint.Count < 4)
                    continue;

                var layerIcon = new IconInfo(icon, PresetJson.GetInt32(iconJToken, "icon_size") ?? baseIconSize);
                layerIcon.IconScale = PresetJson.GetDouble(iconJToken, "scale") ?? defaultIconSize / layerIcon.IconSize;
                layerIcon.IconOffset = new Point(PresetJson.GetInt32Value(shift[0]) ?? default, PresetJson.GetInt32Value(shift[1]) ?? default);
                layerIcon.SetIconTint(
                    PresetJson.GetDoubleValue(tint[3]) ?? default,
                    PresetJson.GetDoubleValue(tint[0]) ?? default,
                    PresetJson.GetDoubleValue(tint[1]) ?? default,
                    PresetJson.GetDoubleValue(tint[2]) ?? default);
                layerIcons.Add(layerIcon);
            }

            if (mainIconPath is null && layerIcons.Count == 0)
                return;

            myIconCache.Add(iconName, GetIconAndColor(iicon, layerIcons, defaultIconSize));
        }


        public IconColorPair GetIconAndColor(IconInfo iinfo, List<IconInfo> iinfos, int defaultCanvasSize) {
            iinfos ??= [];
            double IconCanvasScale = defaultCanvasSize == 32 ? 2 : 1; //just some upscailing for icons (item icons are set at 32x32, but they look better at 64x64)
            int IconCanvasSize = (int)(defaultCanvasSize * IconCanvasScale);

            if (iinfos.Count == 0) //if there are no icons, use the single icon
                iinfos.Add(iinfo);

            //quick check to ensure it isnt a null icon
            bool empty = true;
            foreach (IconInfo ii in iinfos) {
                if (!string.IsNullOrEmpty(ii.IconPath))
                    empty = false;
            }
            if (empty)
                return new IconColorPair(null, Color.Black);

            //prepare the canvas - we will add each successive icon/layer on top of it. Kept premultiplied throughout (Skia only renders onto premultiplied/opaque targets, unlike GDI+).
            SKBitmap? result = new(IconCanvasSize, IconCanvasSize, SKColorType.Bgra8888, SKAlphaType.Premul);
            int cBPP = result.BytesPerPixel;
            int stride = result.RowBytes;
            int bCount = stride * result.Height;
            byte[] canvasPixels = new byte[bCount];
            Marshal.Copy(result.GetPixels(), canvasPixels, 0, canvasPixels.Length);
            int heightInPixels = result.Height;
            int widthInBytes = result.Width * cBPP;

            foreach (IconInfo ii in iinfos) {
                //load the image and prep it for processing
                int iconSize = ii.IconSize > 0 ? ii.IconSize : iinfo.IconSize;
                int iconDrawSize = (int)(iconSize * (ii.IconScale > 0 ? ii.IconScale : (double)defaultCanvasSize / iconSize));
                iconDrawSize = (int)(iconDrawSize * IconCanvasScale);

                using SKBitmap? iconImage = LoadImageFromMod(ii.IconPath, iconDrawSize);
                if (iconImage is null)
                    continue;
                //draw the icon onto a layer (that we will apply tint to and blend with canvas)
                using var layerSlice = new SKBitmap(result.Width, result.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var layerCanvas = new SKCanvas(layerSlice))
                    layerCanvas.DrawBitmap(iconImage, (IconCanvasSize / 2) - (iconDrawSize / 2) + ii.IconOffset.X, (IconCanvasSize / 2) - (iconDrawSize / 2) + ii.IconOffset.Y);

                //grab the layer data
                byte[] layerPixels = new byte[bCount];
                Marshal.Copy(layerSlice.GetPixels(), layerPixels, 0, layerPixels.Length);

                //blend -> for each value in 0->1 (so when multiplying, you have to divide by 255 if in 0->255)
                //newCanvas(A/R/G/B) = Layer(A/R/G/B) * tint(A/R/G/B)   +   oldCanvas(A/R/G/B) * (1 - tint(A) * Layer(A))
                //https://www.factorio.com/blog/post/fff-172
                for (int y = 0; y < heightInPixels; y++) {
                    int currentLine = y * stride;
                    for (int x = 0; x < widthInBytes; x += cBPP) {
                        int canvasMulti = 255 - (ii.IconTint.A * layerPixels[currentLine + x + 3] / 255);
                        canvasPixels[currentLine + x + 0] = (byte)Math.Min(255,
                            (layerPixels[currentLine + x + 0] * ii.IconTint.B / 255) +
                            (canvasPixels[currentLine + x + 0] * canvasMulti / 255));
                        canvasPixels[currentLine + x + 1] = (byte)Math.Min(255,
                            (layerPixels[currentLine + x + 1] * ii.IconTint.G / 255) +
                            (canvasPixels[currentLine + x + 1] * canvasMulti / 255));
                        canvasPixels[currentLine + x + 2] = (byte)Math.Min(255,
                            (layerPixels[currentLine + x + 2] * ii.IconTint.R / 255) +
                            (canvasPixels[currentLine + x + 2] * canvasMulti / 255));
                        canvasPixels[currentLine + x + 3] = (byte)Math.Min(255,
                            (layerPixels[currentLine + x + 3] * ii.IconTint.A / 255) +
                            (canvasPixels[currentLine + x + 3] * canvasMulti / 255));

                    }
                }
            }

            //we are done adding all the layers, so copy the canvas data back
            Marshal.Copy(canvasPixels, 0, result.GetPixels(), canvasPixels.Length);
            result.NotifyPixelsChanged();

            try {
                //calculate the average color (yes, it comes out a bit different due to inclusion of transparency)
                Color averageColor = GetAverageColor(result);
                if (averageColor.GetBrightness() > 0.9) {
                    using SKBitmap flatIcon = result;
                    result = AddBorder(flatIcon); //if the image is too bright, add a border to it. Honestly, this is never done anymore - it was useful before layer blending was fixed and some icons came out... white.
                }
                if (averageColor.GetBrightness() > 0.7)
                    averageColor = Color.FromArgb(255, (int)(averageColor.R * 0.7), (int)(averageColor.G * 0.7), (int)(averageColor.B * 0.7));

                var ret = new IconColorPair(result, averageColor);
                result = null;
                return ret;
            } finally {
                result?.Dispose();
            }
        }

        private SKBitmap? LoadImageFromMod(string fileName, int resultSize = 32) //NOTE: must make sure we use pre-multiplied alpha
        {
            ObjectDisposedException.ThrowIf(disposedValue, this);
            if (string.IsNullOrEmpty(fileName))
                return null;
            string separator = Path.DirectorySeparatorChar.ToString();
            fileName = fileName.ToLowerInvariant().Replace("/", separator, StringComparison.Ordinal);
            while (fileName.Contains(separator + separator, StringComparison.Ordinal)) //found this error in krastorio - apparently factorio ignores multiple slashes in file name
                fileName = fileName.Replace(separator + separator, separator, StringComparison.Ordinal);

            //if the image isnt currently in the cache, process it and add it to cache
            if (!bitmapCache.ContainsKey(fileName)) {
                TotalPathCount++;
                string origin = fileName[..(fileName.IndexOf("__", 2, StringComparison.Ordinal) + 2)];
                string file = fileName[(fileName.IndexOf("__", 2, StringComparison.Ordinal) + 3)..];

                if (folderLinks.TryGetValue(origin, out string? folderPath)) {

                    file = Path.Combine(folderPath, file);
                    try {
                        bitmapCache.Add(fileName, SKBitmap.Decode(file) ?? throw new InvalidDataException($"Could not decode image at '{file}'."));
                    } catch (Exception ex) {
                        bitmapCache.Add(fileName, null);
                        FailedPathCount++;
                        ErrorLogging.LogException(ex, "IconCacheProcessor: failed to load icon from file " + fileName);
                    }

                } else if (archiveFileLinks?.TryGetValue(fileName, out var entry) is true) {
                    try {
                        using Stream entryStream = entry.Open();
                        bitmapCache.Add(fileName, SKBitmap.Decode(entryStream) ?? throw new InvalidDataException($"Could not decode archive icon '{fileName}'."));
                    } catch (Exception ex) {
                        bitmapCache.Add(fileName, null);
                        FailedPathCount++;
                        ErrorLogging.LogException(ex, "IconCacheProcessor: failed to load icon from archive " + fileName);
                    }

                } else {
                    FailedPathCount++;
                    bitmapCache.Add(fileName, null);
                    ErrorLogging.LogLine("IconCacheProcessor: given fileName not found in mod folders: " + fileName);
                }
            }

            if (bitmapCache[fileName] is not SKBitmap image)
                return null;

            //draw it to correct size.
            var bmp = new SKBitmap(resultSize, resultSize, SKColorType.Bgra8888, SKAlphaType.Premul);
            try {
                using (var canvas = new SKCanvas(bmp))
                using (var sourceImage = SKImage.FromBitmap(image))
                    canvas.DrawImage(sourceImage, new SKRect(0, 0, resultSize * image.Width / image.Height, resultSize), HighQualitySampling);
                var ret = bmp;
                bmp = null;
                return ret;
            } finally {
                bmp?.Dispose();
            }
        }

        private static Color GetAverageColor(SKBitmap? icon) {
            if (icon == null)
                return Color.Black;

            int bytesPerPixel = icon.BytesPerPixel;
            int stride = icon.RowBytes;
            int byteCount = stride * icon.Height;
            byte[] iconPixels = new byte[byteCount];
            Marshal.Copy(icon.GetPixels(), iconPixels, 0, iconPixels.Length);
            int heightInPixels = icon.Height;
            int widthInBytes = icon.Width * bytesPerPixel;

            int[] totalPixel = [0, 0, 0, 0];
            int totalCounter = 1; //just to avoid div by 0 in case of completely empty bitmap
            for (int y = 0; y < heightInPixels; y++) {
                int currentLine = y * stride;
                for (int x = 0; x < widthInBytes; x += bytesPerPixel) {
                    int alpha = iconPixels[currentLine + x + 3];
                    if (alpha > 10) //ignore transparent pixels
                    {
                        //icon is premultiplied - undo the premultiplication so the average reflects true color, not alpha-darkened color
                        totalPixel[3] += iconPixels[currentLine + x] * 255 / alpha;     //B
                        totalPixel[2] += iconPixels[currentLine + x + 1] * 255 / alpha; //G
                        totalPixel[1] += iconPixels[currentLine + x + 2] * 255 / alpha; //R
                        totalCounter++;
                    }
                }
            }
            for (int i = 1; i < 4; i++) {
                totalPixel[i] /= totalCounter;
                totalPixel[i] = Math.Min(totalPixel[i], 255);
            }

            return Color.FromArgb(255, totalPixel[1], totalPixel[2], totalPixel[3]);
        }

        private const int iconBorder = 1; //border is drawn on a new layer as
        private const byte borderAlpha = 64;
        private static readonly byte borderChannel = (byte)(64 * borderAlpha / 255); //premultiplied equivalent of the gray(64,64,64) @ alpha 64 GDI+ wrote directly
        private static SKBitmap AddBorder(SKBitmap icon) {
            var canvas = new SKBitmap(icon.Width, icon.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            int bytesPerPixel = icon.BytesPerPixel;
            int stride = icon.RowBytes;
            int byteCount = stride * icon.Height; //same for both
            byte[] iconPixels = new byte[byteCount];
            byte[] canvasPixels = new byte[byteCount];

            Marshal.Copy(icon.GetPixels(), iconPixels, 0, iconPixels.Length);
            int heightInPixels = icon.Height;
            int widthInBytes = icon.Width * bytesPerPixel;

            for (int y = iconBorder; y < heightInPixels - iconBorder; y++) {
                int currentLine = y * stride;
                for (int x = iconBorder * bytesPerPixel; x < widthInBytes - iconBorder * bytesPerPixel; x += bytesPerPixel) {
                    if (iconPixels[currentLine + x + 3] > 11) //check if A >= 10
                    {
                        for (int iy = -iconBorder; iy <= iconBorder; iy++) {
                            for (int ix = -iconBorder * bytesPerPixel; ix <= iconBorder * bytesPerPixel; ix += bytesPerPixel) {
                                int currentCanvasIndex = currentLine + iy * stride + x + ix;
                                canvasPixels[currentCanvasIndex] = borderChannel;
                                canvasPixels[currentCanvasIndex + 1] = borderChannel;
                                canvasPixels[currentCanvasIndex + 2] = borderChannel;
                                canvasPixels[currentCanvasIndex + 3] = borderAlpha;
                            }
                        }
                    }
                }
            }
            Marshal.Copy(canvasPixels, 0, canvas.GetPixels(), canvasPixels.Length);
            canvas.NotifyPixelsChanged();

            //draw the processed icon (singluar) onto the main canvas
            using (var skCanvas = new SKCanvas(canvas))
            using (var sourceImage = SKImage.FromBitmap(icon))
                skCanvas.DrawImage(sourceImage, 0, 0);

            return canvas;
        }

        private bool disposedValue;
        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    archiveFileLinks?.Clear();
                    archiveFileLinks = null;

                    foreach (SKBitmap? bitmap in bitmapCache.Values)
                        bitmap?.Dispose();
                    bitmapCache.Clear();

                    foreach (ZipArchive zip in openedArchives)
                        zip.Dispose();
                    openedArchives.Clear();
                }
                disposedValue = true;
            }
        }
        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private static readonly Dictionary<KeyValuePair<SKBitmap, SKBitmap>, SKBitmap> combinedBitmapDictionary = [];
        private const double qualitySizeMultiplier = 0.5;
        public static SKBitmap? CombinedQualityIcon(SKBitmap? baseIcon, SKBitmap? qualityIcon) {
            if (baseIcon is null || qualityIcon is null)
                return null;

            if (combinedBitmapDictionary.TryGetValue(new KeyValuePair<SKBitmap, SKBitmap>(baseIcon, qualityIcon), out SKBitmap? combinedBitmap))
                return combinedBitmap;

            //combine the two bitmaps. Canvas is always premultiplied, regardless of the source icons' alpha type - Skia only renders onto premultiplied/opaque targets.
            var canvas = new SKBitmap(baseIcon.Width, baseIcon.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var skCanvas = new SKCanvas(canvas))
            using (var baseImage = SKImage.FromBitmap(baseIcon))
            using (var qualityImage = SKImage.FromBitmap(qualityIcon)) {
                skCanvas.DrawImage(baseImage, new SKRect(0, 0, baseIcon.Width, baseIcon.Height));
                int qx = (int)(baseIcon.Width * (1 - qualitySizeMultiplier));
                int qy = (int)(baseIcon.Height * (1 - qualitySizeMultiplier));
                int qw = (int)(baseIcon.Width * qualitySizeMultiplier);
                int qh = (int)(baseIcon.Height * qualitySizeMultiplier);
                skCanvas.DrawImage(qualityImage, new SKRect(qx, qy, qx + qw, qy + qh), HighQualitySampling);
            }
            combinedBitmapDictionary.Add(new KeyValuePair<SKBitmap, SKBitmap>(baseIcon, qualityIcon), canvas);
            return canvas;
        }
    }
}
