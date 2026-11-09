using Foreman;
using Foreman.DataCaching;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace ForemanTest {
    //PresetProcessor.PrepPreset/GetPresetPath already carry. The exported data payload is the real bundled
    //Vanilla preset's own .pjson content (its "mods" list only names core/base, both exempt from
    //IconCacheProcessor's mod-folder lookup) so DataCachePostLoadProcessor's full consistency checks
    //(rocket-silo lookups etc.) pass without a hand-rolled preset needing to satisfy them.
    [TestClass]
    [UnsupportedOSPlatform("windows")]
    public class DataCacheImportedPresetTests {
        private static readonly Func<int, int, Task<bool>> AlwaysContinue = (_, _) => Task.FromResult(true);

        private static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [TestMethod]
        public async Task LoadAllData_ImportedPresetViaUserDirectoryOverride_FindsItsOwnIconCache() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            string macOsDir = StubFactorioHarness.CreateExecutableDirectory();
            string installPath = Path.GetDirectoryName(macOsDir)!;
            string dataBaseDir = Path.Combine(installPath, "data", "base", "graphics");
            Directory.CreateDirectory(dataBaseDir);
            File.Copy(Path.Combine(AppPaths.ExecutableDirectory, "Graphics", "UnknownIcon.png"), Path.Combine(dataBaseDir, "icon.png"));

            string p2 = File.ReadAllText(Path.Combine(VanillaDataCacheFixture.PresetsDirectory, VanillaDataCacheFixture.PresetName + ".pjson"));
            const string p1 = "{\"items\":[{\"icon_name\":\"icon.i.iron-plate\",\"icon_data\":{\"icon\":\"__base__/graphics/icon.png\",\"icon_size\":32}}]}";
            StubFactorioHarness.WriteExportScript(macOsDir, "", p1, p2);

            string userPresetsDir = NewTempDir();
            PresetImporter.Result importResult = await PresetImporter.ProcessPreset(
                installPath, new FactorioGameVersion(2, 0, 76), NewTempDir(), NewTempDir(), "Imported Icon Preset", userPresetsDir,
                NullProgress.Instance, AlwaysContinue, CancellationToken.None, userDataDirectoryOverride: userPresetsDir);
            Assert.AreEqual(PresetImportOutcome.Ok, importResult.Outcome, importResult.WarningMessage);

            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(
                new Preset("Imported Icon Preset", true, true), NullProgress.Instance,
                userPresetsDirectoryOverride: userPresetsDir);

            var store = TestDataCacheHelper.RequireStore(cache);
            Assert.IsGreaterThan(0, store.IconCache?.Count ?? 0, "expected the imported preset's own icon cache, not the bundle's empty one.");
        }

        [TestMethod]
        public async Task GameVersion_PresetWithoutMarker_ReadsBaseModVersion() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            DataCache cache = await VanillaDataCacheFixture.GetLoadedAsync();

            Assert.AreEqual(new FactorioGameVersion(2, 0, 76), cache.GameVersion);
        }

        [TestMethod]
        public void GameVersion_MarkerPresent_WinsOverBaseMod() {
            JsonObject preset = PresetJson.ParseObject("{\"game_version\":\"2.1.24\",\"mods\":[{\"name\":\"base\",\"version\":\"2.0.76\"}]}");

            Assert.AreEqual(new FactorioGameVersion(2, 1, 24), DataCache.ReadGameVersion(preset));
        }

        [TestMethod]
        public void GameVersion_NoMarkerNoBase_IsNull() {
            JsonObject preset = PresetJson.ParseObject("{\"mods\":[{\"name\":\"core\",\"version\":\"1.0\"}]}");

            Assert.IsNull(DataCache.ReadGameVersion(preset));
        }
    }
}
