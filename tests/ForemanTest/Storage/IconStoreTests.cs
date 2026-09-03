using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ForemanTest.Storage {
    //Exercises IconStore (phase 9 Task 3): content-hash dedup over a miniature FOIC file built with
    //the real writer. Never touches the bundled 25MB preset .dat - temp dir only.
    [TestClass]
    public class IconStoreTests {
        private string tempRoot = "";

        [TestInitialize]
        public void CreateTempRoot() {
            tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
        }

        [TestCleanup]
        public void DeleteTempRoot() {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, true);
            }
        }

        private static SKBitmap SolidBitmap(byte r, byte g, byte b) {
            var bitmap = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
            using SKCanvas canvas = new(bitmap);
            canvas.Clear(new SKColor(r, g, b, 255));
            return bitmap;
        }

        private static byte[] EncodePng(SKBitmap bitmap) {
            using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100)!;
            return data.ToArray();
        }

        private string WriteMiniatureFoic(string fileName, IReadOnlyDictionary<string, IconColorPair> icons) {
            string path = Path.Combine(tempRoot, fileName);
            ForemanIconCacheFile.WriteAsync(path, icons).GetAwaiter().GetResult();
            return path;
        }

        [TestMethod]
        public void ImportFoic_DedupsSharedPngBytes_AndRoundTripsThroughReadPresetIcons() {
            using SKBitmap sharedBitmap = SolidBitmap(200, 40, 40);
            using SKBitmap uniqueBitmap = SolidBitmap(40, 200, 40);
            byte[] sharedPng = EncodePng(sharedBitmap);
            byte[] uniquePng = EncodePng(uniqueBitmap);

            var icons = new Dictionary<string, IconColorPair> {
                ["entry-a"] = new IconColorPair(sharedBitmap, Color.FromArgb(255, 10, 20, 30)),
                ["entry-b"] = new IconColorPair(sharedBitmap, Color.FromArgb(255, 40, 50, 60)),
                ["entry-c"] = new IconColorPair(uniqueBitmap, Color.FromArgb(255, 70, 80, 90)),
            };
            string datPath = WriteMiniatureFoic("preset-1.foic", icons);

            using ForemanDbContext db = ForemanDbContext.Open(StoragePaths.DbPath(tempRoot));
            IconStore.ImportFoic(db, "preset-1", datPath);

            Assert.AreEqual(2, db.IconBlobs.Count());
            Assert.AreEqual(3, db.PresetIcons.Count(row => row.PresetName == "preset-1"));

            var read = IconStore.ReadPresetIcons(db, "preset-1").ToDictionary(e => e.Name);
            Assert.AreEqual(3, read.Count);
            CollectionAssert.AreEqual(sharedPng, read["entry-a"].PngBytes);
            CollectionAssert.AreEqual(sharedPng, read["entry-b"].PngBytes);
            CollectionAssert.AreEqual(uniquePng, read["entry-c"].PngBytes);
            Assert.AreEqual(Color.FromArgb(255, 10, 20, 30).ToArgb(), read["entry-a"].Tint);
            Assert.AreEqual(Color.FromArgb(255, 40, 50, 60).ToArgb(), read["entry-b"].Tint);
            Assert.AreEqual(Color.FromArgb(255, 70, 80, 90).ToArgb(), read["entry-c"].Tint);
        }

        [TestMethod]
        public void ImportFoic_ReimportingSamePreset_IsIdempotent() {
            using SKBitmap sharedBitmap = SolidBitmap(200, 40, 40);
            using SKBitmap uniqueBitmap = SolidBitmap(40, 200, 40);
            var icons = new Dictionary<string, IconColorPair> {
                ["entry-a"] = new IconColorPair(sharedBitmap, Color.FromArgb(255, 10, 20, 30)),
                ["entry-b"] = new IconColorPair(sharedBitmap, Color.FromArgb(255, 40, 50, 60)),
                ["entry-c"] = new IconColorPair(uniqueBitmap, Color.FromArgb(255, 70, 80, 90)),
            };
            string datPath = WriteMiniatureFoic("preset-1.foic", icons);

            using ForemanDbContext db = ForemanDbContext.Open(StoragePaths.DbPath(tempRoot));
            IconStore.ImportFoic(db, "preset-1", datPath);
            IconStore.ImportFoic(db, "preset-1", datPath);

            Assert.AreEqual(2, db.IconBlobs.Count());
            Assert.AreEqual(3, db.PresetIcons.Count(row => row.PresetName == "preset-1"));
        }

        [TestMethod]
        public void ImportFoic_SecondPresetSharingOneBlob_GrowsBlobCountByUniqueOnly() {
            using SKBitmap sharedBitmap = SolidBitmap(200, 40, 40);
            using SKBitmap uniqueBitmapOne = SolidBitmap(40, 200, 40);
            using SKBitmap uniqueBitmapTwo = SolidBitmap(40, 40, 200);

            var firstPresetIcons = new Dictionary<string, IconColorPair> {
                ["entry-a"] = new IconColorPair(sharedBitmap, Color.FromArgb(255, 10, 20, 30)),
                ["entry-b"] = new IconColorPair(sharedBitmap, Color.FromArgb(255, 40, 50, 60)),
                ["entry-c"] = new IconColorPair(uniqueBitmapOne, Color.FromArgb(255, 70, 80, 90)),
            };
            var secondPresetIcons = new Dictionary<string, IconColorPair> {
                ["entry-a"] = new IconColorPair(sharedBitmap, Color.FromArgb(255, 11, 21, 31)),
                ["entry-d"] = new IconColorPair(uniqueBitmapTwo, Color.FromArgb(255, 12, 22, 32)),
            };
            string firstDatPath = WriteMiniatureFoic("preset-1.foic", firstPresetIcons);
            string secondDatPath = WriteMiniatureFoic("preset-2.foic", secondPresetIcons);

            using ForemanDbContext db = ForemanDbContext.Open(StoragePaths.DbPath(tempRoot));
            IconStore.ImportFoic(db, "preset-1", firstDatPath);
            IconStore.ImportFoic(db, "preset-2", secondDatPath);

            Assert.AreEqual(3, db.IconBlobs.Count());
            Assert.AreEqual(2, db.PresetIcons.Count(row => row.PresetName == "preset-2"));
        }
    }
}
