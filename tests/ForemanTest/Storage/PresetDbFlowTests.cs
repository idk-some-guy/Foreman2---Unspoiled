using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ForemanTest.Storage {
    [TestClass]
    public class PresetDbFlowTests {
        private const string PresetName = "Db Flow Miniature";
        private const string IconName = "icon.i.iron-plate";

        private string tempDir = "";

        [TestInitialize]
        public void CreateTempDir() {
            tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
        }

        [TestCleanup]
        public void DeleteTempDir() {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        private static SKBitmap SolidBitmap(byte r, byte g, byte b) {
            var bitmap = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
            using SKCanvas canvas = new(bitmap);
            canvas.Clear(new SKColor(r, g, b, 255));
            return bitmap;
        }

        [TestMethod]
        public async Task LoadAllData_FirstLoadImportsIntoDb_SecondLoadWithLegacyFilesDeletedMatchesFirst() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            string pjsonPath = Path.Combine(tempDir, PresetName + ".pjson");
            string datPath = Path.Combine(tempDir, PresetName + ".dat");

            string pjsonText = File.ReadAllText(Path.Combine(VanillaDataCacheFixture.PresetsDirectory, VanillaDataCacheFixture.PresetName + ".pjson"));
            File.WriteAllText(pjsonPath, pjsonText);

            using SKBitmap ironBitmap = SolidBitmap(200, 40, 40);
            var icons = new Dictionary<string, IconColorPair> {
                [IconName] = new IconColorPair(ironBitmap, Color.FromArgb(255, 10, 20, 30)),
            };
            await ForemanIconCacheFile.WriteAsync(datPath, icons);

            JsonObject controlJson = PresetJson.ParseObject(pjsonText);
            int controlItemCount = PresetJson.CountArray(controlJson, "items");
            int controlRecipeCount = PresetJson.CountArray(controlJson, "recipes");
            var controlIcons = await ForemanIconCacheFile.ReadAsync(datPath);
            byte[] controlIronBytes = controlIcons[IconName].Icon!.Bytes;

            string dbPath = StoragePaths.DbPath(tempDir);

            var firstCache = new DataCache(filterRecipes: true);
            await firstCache.LoadAllData(new Preset(PresetName, true, true), NullProgress.Instance, userPresetsDirectoryOverride: tempDir);

            using (var db = new ForemanDbContext(dbPath)) {
                Assert.IsTrue(db.Presets.Any(row => row.PresetName == PresetName), "expected a PresetDataRow after the first load.");
                Assert.IsTrue(db.PresetIcons.Any(row => row.PresetName == PresetName), "expected PresetIconRows after the first load.");
            }

            Assert.AreEqual(controlItemCount, PresetJson.CountArray(PresetProcessor.PrepPreset(new Preset(PresetName, true, true), tempDir), "items"));
            Assert.AreEqual(controlRecipeCount, PresetJson.CountArray(PresetProcessor.PrepPreset(new Preset(PresetName, true, true), tempDir), "recipes"));

            var firstStore = TestDataCacheHelper.RequireStore(firstCache);
            CollectionAssert.AreEqual(controlIronBytes, firstStore.IconCache![IconName].Icon!.Bytes);

            File.Delete(pjsonPath);
            File.Delete(datPath);

            var secondCache = new DataCache(filterRecipes: true);
            await secondCache.LoadAllData(new Preset(PresetName, true, true), NullProgress.Instance, userPresetsDirectoryOverride: tempDir);

            Assert.AreEqual(firstCache.Items.Count, secondCache.Items.Count);
            Assert.AreEqual(firstCache.Recipes.Count, secondCache.Recipes.Count);

            var secondStore = TestDataCacheHelper.RequireStore(secondCache);
            CollectionAssert.AreEqual(controlIronBytes, secondStore.IconCache![IconName].Icon!.Bytes);
        }

        [TestMethod]
        public async Task PrepPreset_PresetRowImportedWithoutIconRows_RecoversIconsOnLoad() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            string pjsonPath = Path.Combine(tempDir, PresetName + ".pjson");
            string datPath = Path.Combine(tempDir, PresetName + ".dat");

            string pjsonText = File.ReadAllText(Path.Combine(VanillaDataCacheFixture.PresetsDirectory, VanillaDataCacheFixture.PresetName + ".pjson"));
            File.WriteAllText(pjsonPath, pjsonText);

            using SKBitmap ironBitmap = SolidBitmap(200, 40, 40);
            var icons = new Dictionary<string, IconColorPair> {
                [IconName] = new IconColorPair(ironBitmap, Color.FromArgb(255, 10, 20, 30)),
            };
            await ForemanIconCacheFile.WriteAsync(datPath, icons);
            var controlEntries = await ForemanIconCacheFile.ReadEntriesAsync(datPath);
            byte[] controlIronPngBytes = controlEntries.Single(entry => entry.Name == IconName).PngBytes;

            string dbPath = StoragePaths.DbPath(tempDir);

            using (var db = ForemanDbContext.Open(dbPath))
                PresetStore.Import(db, PresetName, pjsonText, null);

            using (var db = ForemanDbContext.Open(dbPath))
                Assert.IsFalse(db.PresetIcons.Any(row => row.PresetName == PresetName), "test setup should not have pre-populated icon rows.");

            var preset = new Preset(PresetName, true, true);
            PresetProcessor.PrepPreset(preset, tempDir);
            var iconEntries = PresetProcessor.GetPresetIconEntries(preset, tempDir);

            Assert.AreEqual(1, iconEntries.Count);
            CollectionAssert.AreEqual(controlIronPngBytes, iconEntries.Single(entry => entry.Name == IconName).PngBytes);
        }

        [TestMethod]
        public void PrepPreset_OverlayFileDeletedAfterImport_KeepsStoredOverlayApplied() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            string pjsonPath = Path.Combine(tempDir, PresetName + ".pjson");
            string overlayPath = Path.Combine(tempDir, PresetName + ".json");
            string datPath = Path.Combine(tempDir, PresetName + ".dat");
            string pjsonText = File.ReadAllText(Path.Combine(VanillaDataCacheFixture.PresetsDirectory, VanillaDataCacheFixture.PresetName + ".pjson"));
            File.WriteAllText(pjsonPath, pjsonText);
            File.WriteAllText(overlayPath, "{\"items\":[{\"name\":\"overlay-test-item\"}]}");

            using SKBitmap ironBitmap = SolidBitmap(200, 40, 40);
            var icons = new Dictionary<string, IconColorPair> {
                [IconName] = new IconColorPair(ironBitmap, Color.FromArgb(255, 10, 20, 30)),
            };
            ForemanIconCacheFile.WriteAsync(datPath, icons).GetAwaiter().GetResult();

            var preset = new Preset(PresetName, true, true);
            JsonObject firstLoad = PresetProcessor.PrepPreset(preset, tempDir);
            Assert.IsTrue(PresetJson.EnumerateArray(firstLoad, "items").Any(node => PresetJson.GetString(node, "name") == "overlay-test-item"));

            File.Delete(overlayPath);

            JsonObject secondLoad = PresetProcessor.PrepPreset(preset, tempDir);
            Assert.IsTrue(PresetJson.EnumerateArray(secondLoad, "items").Any(node => PresetJson.GetString(node, "name") == "overlay-test-item"),
                "overlay should still apply from the db after presetCustom.json is deleted");
        }

        [TestMethod]
        public void PrepPreset_NoDatFile_CachesPjsonOnFirstLoad_SecondLoadSurvivesPjsonDeletion() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            string pjsonPath = Path.Combine(tempDir, PresetName + ".pjson");
            string pjsonText = File.ReadAllText(Path.Combine(VanillaDataCacheFixture.PresetsDirectory, VanillaDataCacheFixture.PresetName + ".pjson"));
            File.WriteAllText(pjsonPath, pjsonText);

            JsonObject controlJson = PresetJson.ParseObject(pjsonText);
            int controlItemCount = PresetJson.CountArray(controlJson, "items");

            var preset = new Preset(PresetName, true, true);
            PresetProcessor.PrepPreset(preset, tempDir);

            File.Delete(pjsonPath);

            JsonObject secondLoad = PresetProcessor.PrepPreset(preset, tempDir);
            Assert.AreEqual(controlItemCount, PresetJson.CountArray(secondLoad, "items"));
        }

        [TestMethod]
        public void PrepPreset_DatFileAppearsAfterFirstLoad_ImportsIconsOnNextLoad() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            string pjsonPath = Path.Combine(tempDir, PresetName + ".pjson");
            string datPath = Path.Combine(tempDir, PresetName + ".dat");
            string pjsonText = File.ReadAllText(Path.Combine(VanillaDataCacheFixture.PresetsDirectory, VanillaDataCacheFixture.PresetName + ".pjson"));
            File.WriteAllText(pjsonPath, pjsonText);

            var preset = new Preset(PresetName, true, true);
            PresetProcessor.PrepPreset(preset, tempDir);
            Assert.AreEqual(0, PresetProcessor.GetPresetIconEntries(preset, tempDir).Count);

            using SKBitmap ironBitmap = SolidBitmap(200, 40, 40);
            var icons = new Dictionary<string, IconColorPair> {
                [IconName] = new IconColorPair(ironBitmap, Color.FromArgb(255, 10, 20, 30)),
            };
            ForemanIconCacheFile.WriteAsync(datPath, icons).GetAwaiter().GetResult();

            PresetProcessor.PrepPreset(preset, tempDir);
            var iconEntries = PresetProcessor.GetPresetIconEntries(preset, tempDir);
            Assert.AreEqual(1, iconEntries.Count);
        }

        [TestMethod]
        public void PrepPreset_CorruptDbFile_QuarantinesAndReimportsFromDisk() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            string pjsonPath = Path.Combine(tempDir, PresetName + ".pjson");
            string pjsonText = File.ReadAllText(Path.Combine(VanillaDataCacheFixture.PresetsDirectory, VanillaDataCacheFixture.PresetName + ".pjson"));
            File.WriteAllText(pjsonPath, pjsonText);

            string dbPath = StoragePaths.DbPath(tempDir);
            File.WriteAllBytes(dbPath, [1, 2, 3, 4, 5]);

            JsonObject controlJson = PresetJson.ParseObject(pjsonText);
            int controlItemCount = PresetJson.CountArray(controlJson, "items");

            JsonObject result = PresetProcessor.PrepPreset(new Preset(PresetName, true, true), tempDir);

            Assert.AreEqual(controlItemCount, PresetJson.CountArray(result, "items"));
            Assert.IsTrue(File.Exists(dbPath + ".corrupt"));
        }
    }
}
