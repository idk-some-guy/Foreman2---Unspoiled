using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace ForemanTest {
    //Lazy per-icon decode. LoadIconCacheFromStore hands out undecoded PNG bytes
    //behind IconColorPair.Icon, which decodes (and caches) only on first access.
    [TestClass]
    public class IconLazyDecodeTests {
        private static byte[] EncodePng(byte r, byte g, byte b) {
            using var bitmap = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
                canvas.Clear(new SKColor(r, g, b, 255));
            using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100)!;
            return data.ToArray();
        }

        private static List<(string Name, int Tint, byte[] PngBytes)> MiniaturePresetIcons() => [
            ("icon-a", Color.FromArgb(255, 10, 20, 30).ToArgb(), EncodePng(200, 40, 40)),
            ("icon-b", Color.FromArgb(255, 40, 50, 60).ToArgb(), EncodePng(40, 200, 40)),
            ("icon-c", Color.FromArgb(255, 70, 80, 90).ToArgb(), EncodePng(40, 40, 200)),
        ];

        [TestMethod]
        public async Task LoadIconCacheFromStore_MiniaturePreset_DecodesOnlyOnFirstRequestPerIcon() {
            IconCache.ResetLazyDecodeCount();
            Dictionary<string, IconColorPair> loaded = await IconCache.LoadIconCacheFromStore(
                MiniaturePresetIcons(), NullProgress.Instance, 0, 100);

            Assert.AreEqual(0, IconCache.LazyDecodeCount, "loading a preset must not decode any icon eagerly.");

            SKBitmap? first = loaded["icon-a"].Icon;
            SKBitmap? second = loaded["icon-b"].Icon;
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreEqual(2, IconCache.LazyDecodeCount, "requesting two icons must decode exactly those two.");

            _ = loaded["icon-a"].Icon;
            Assert.AreEqual(2, IconCache.LazyDecodeCount, "re-requesting an already-decoded icon must not redecode.");
        }

        [TestMethod]
        public async Task LoadIconCacheFromStore_OnDemandDecode_MatchesEagerDecodedPixels() {
            byte[] pngBytes = EncodePng(123, 45, 67);
            var entries = new List<(string Name, int Tint, byte[] PngBytes)> { ("icon-solo", Color.Black.ToArgb(), pngBytes) };

            Dictionary<string, IconColorPair> loaded = await IconCache.LoadIconCacheFromStore(entries, NullProgress.Instance, 0, 100);
            using SKBitmap onDemand = loaded["icon-solo"].Icon!;
            using SKBitmap eager = SKBitmap.Decode(pngBytes)!;

            Assert.AreEqual(eager.Width, onDemand.Width);
            Assert.AreEqual(eager.Height, onDemand.Height);
            CollectionAssert.AreEqual(eager.Bytes, onDemand.Bytes);
        }

        [TestMethod]
        public async Task LoadIconCacheFromStore_ConcurrentRequestsForSameIcon_DecodeOnceAndShareTheBitmap() {
            IconCache.ResetLazyDecodeCount();
            var entries = new List<(string Name, int Tint, byte[] PngBytes)> { ("icon-shared", Color.Black.ToArgb(), EncodePng(9, 8, 7)) };
            Dictionary<string, IconColorPair> loaded = await IconCache.LoadIconCacheFromStore(entries, NullProgress.Instance, 0, 100);
            IconColorPair pair = loaded["icon-shared"];

            SKBitmap?[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => pair.Icon)));

            Assert.AreEqual(1, IconCache.LazyDecodeCount);
            Assert.IsTrue(results.All(r => ReferenceEquals(r, results[0])));
        }

        [TestMethod]
        public async Task IconColorPair_DisposeIfDecoded_OnUndecodedIcon_DoesNotTriggerDecode() {
            IconCache.ResetLazyDecodeCount();
            var entries = new List<(string Name, int Tint, byte[] PngBytes)> { ("icon-x", Color.Black.ToArgb(), EncodePng(5, 6, 7)) };
            Dictionary<string, IconColorPair> loaded = await IconCache.LoadIconCacheFromStore(entries, NullProgress.Instance, 0, 100);

            loaded["icon-x"].DisposeIfDecoded();

            Assert.AreEqual(0, IconCache.LazyDecodeCount, "disposing an icon that was never read must not decode it first.");
        }

        [TestMethod]
        public async Task SetIconAndColor_WithLazyPair_DoesNotDecodeUntilIconIsRead() {
            var cache = new DataCache(filterRecipes: true);
            var group = new GroupPrototype(cache, "g", "G", "a");
            var subgroup = new SubgroupPrototype(cache, "sg", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            ItemPrototype item = TestDataCacheHelper.GetOrCreateItem(cache, subgroup, "widget");

            var entries = new List<(string Name, int Tint, byte[] PngBytes)> { ("icon-widget", Color.Red.ToArgb(), EncodePng(1, 2, 3)) };
            Dictionary<string, IconColorPair> loaded = await IconCache.LoadIconCacheFromStore(entries, NullProgress.Instance, 0, 100);

            IconCache.ResetLazyDecodeCount();
            item.SetIconAndColor(loaded["icon-widget"]);
            Assert.AreEqual(0, IconCache.LazyDecodeCount, "assigning an icon to a data object must not decode it.");

            _ = item.Icon;
            Assert.AreEqual(1, IconCache.LazyDecodeCount, "reading .Icon must decode exactly once.");

            _ = item.Icon;
            Assert.AreEqual(1, IconCache.LazyDecodeCount, "re-reading .Icon must not redecode.");
        }

        //UnknownIcon/SpoilageIcon/PlantingIcon are no longer warmed eagerly at load, so
        //their first touch can now come from concurrent render threads racing an unsynchronized
        //`field ??= GetIcon(...)`. Mirrors the lazy-decode concurrency test above for the same guarantee:
        //exactly one load, every caller sharing the same bitmap.
        [TestMethod]
        public async Task UnknownIcon_ConcurrentFirstAccess_LoadsOnceAndSharesTheBitmap() {
            IconCache.ResetCachedIconsForTests();

            SKBitmap[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => IconCache.UnknownIcon)));

            Assert.AreEqual(1, IconCache.CachedIconLoadCount);
            Assert.IsTrue(results.All(r => ReferenceEquals(r, results[0])));
        }
    }
}
