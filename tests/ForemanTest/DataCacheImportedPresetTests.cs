using Foreman;
using Foreman.DataCaching;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace ForemanTest {
    //Covers the final-review C1 finding: DataCache.LoadAllData's icon-cache path was hardcoded to the
    //bundle Presets directory (Path.Combine(AppPaths.ExecutableDirectory, "Presets", ...)), so a preset
    //imported into the user's own Presets directory (PresetImporter's write target since Task 7) loaded
    //with zero icons - IconCache.LoadIconCache's missing-file branch just returns [] silently. Drives the
    //real import pipeline (StubFactorioHarness, same as PresetImporterTests) into a temp user directory,
    //then loads it back through DataCache.LoadAllData pointed at that same directory via the seam
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
                installPath, NewTempDir(), NewTempDir(), "Imported Icon Preset", userPresetsDir,
                NullProgress.Instance, AlwaysContinue, CancellationToken.None);
            Assert.AreEqual(PresetImportOutcome.Ok, importResult.Outcome, importResult.WarningMessage);

            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(
                new Preset("Imported Icon Preset", true, true), NullProgress.Instance,
                userPresetsDirectoryOverride: userPresetsDir);

            var store = TestDataCacheHelper.RequireStore(cache);
            Assert.IsGreaterThan(0, store.IconCache?.Count ?? 0, "expected the imported preset's own icon cache, not the bundle's empty one.");
        }
    }
}
