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
    //Covers phase 9 Task 5 (db-first preset read wiring): DataCache.LoadAllData routes through
    //PresetProcessor's db-backed read path now, importing a preset into PresetStore/IconStore the
    //first time it's loaded and reading straight from the db afterward. The pjson here is a byte-for-byte
    //copy of the real bundled Vanilla preset (same reasoning as DataCacheImportedPresetTests: a
    //hand-rolled minimal preset risks tripping DataCachePostLoadProcessor's consistency checks), paired
    //with a genuinely miniature writer-built FOIC .dat (one icon) so the icon round trip stays cheap
    //to assert byte-for-byte.
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

            //control: the untouched legacy readers, on the original files, before any db involvement.
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

            //Stand-in for the state a swallowed cross-preset IconBlobs.Sha256 race can leave behind:
            //the preset row committed, but no PresetIcons rows for it yet.
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

        //Final-review I2: presetCustom.json going missing between runs must not read back as "the
        //overlay was cleared" - a deleted file is no news, not an edit, and the stored overlay must
        //keep applying.
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

            //Needs a .dat too - otherwise PresetIcons never gets a row for this preset name, the preset
            //never counts as cached, and every load falls through ReadOrImportPreset's own reimport
            //branch regardless of this test's overlay-diff fix (that behavior is Task 5's own gap, not
            //this one's).
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

        //Final-review I5: a preset row alone should cache the pjson, independent of whether any icon
        //rows exist yet - a preset with no .dat file must still stop re-reading .pjson off disk after
        //its first load, the same one-time-import Task 5 already gives every other preset.
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

        //Companion to the above: a .dat file that shows up after the preset's first (icon-less) load
        //must still get imported on the next load - the pjson cache gate must not swallow the icon
        //import too.
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

        //Final-review C1: PrepPreset must recover from a corrupt foreman.db the same way SettingsService
        //does - quarantine the bad file and reimport straight from the legacy .pjson/.dat still on disk.
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
