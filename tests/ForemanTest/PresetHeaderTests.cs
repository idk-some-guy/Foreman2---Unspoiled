using Foreman;
using Foreman.DataCaching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class PresetHeaderTests {
        private static readonly string[] BundledNames = [
            "Factorio 2.0 Vanilla", "Factorio 2.0 Space Age", "Factorio 2.1 Vanilla", "Factorio 2.1 Space Age"];

        private readonly System.Collections.Generic.List<string> written = [];

        [TestCleanup]
        public void RemoveWrittenPresets() {
            foreach (string path in written)
                File.Delete(path);
        }

        private Preset WriteUserPreset(string text) {
            string name = "Header " + Path.GetRandomFileName();
            string path = PresetProcessor.GetUserPresetPath(name, ".pjson");
            written.Add(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return new Preset(name, false, false);
        }

        [TestMethod]
        public void ReadPresetHeader_TwoOnePreset_Reads2_1_24AndCurrent() {
            PresetHeader? header = PresetProcessor.ReadPresetHeader(new Preset("Factorio 2.1 Vanilla", false, false));

            Assert.AreEqual(new PresetHeader(new FactorioGameVersion(2, 1, 24), false), header);
        }

        [TestMethod]
        public void ReadPresetHeader_PresetWithoutMarker_FallsBackToBaseVersion() {
            PresetHeader? header = PresetProcessor.ReadPresetHeader(new Preset("Factorio 2.0 Vanilla", false, false));

            Assert.AreEqual(new PresetHeader(new FactorioGameVersion(2, 0, 76), false), header);
        }

        [TestMethod]
        public void ReadPresetHeader_MarkerPresent_WinsOverBaseMod() {
            Preset preset = WriteUserPreset("{\"foreman_export_version\":1,\"mods\":[{\"name\":\"base\",\"version\":\"2.0.76\"}],\"game_version\":\"2.1.24\"}");

            Assert.AreEqual(new FactorioGameVersion(2, 1, 24), PresetProcessor.ReadPresetHeader(preset)?.GameVersion);
        }

        [TestMethod]
        public void ReadPresetHeader_NoExportVersion_IsOutdated() {
            Preset preset = WriteUserPreset("{\"mods\":[{\"name\":\"base\",\"version\":\"2.0.28\"}],\"technologies\":[{\"name\":\"x\",\"nested\":[1,2,{\"a\":null}]}]}");

            Assert.AreEqual(new PresetHeader(new FactorioGameVersion(2, 0, 28), true), PresetProcessor.ReadPresetHeader(preset));
        }

        [TestMethod]
        public void ReadPresetHeader_ExportVersionWrittenAsDecimal_IsCurrent() {
            Preset preset = WriteUserPreset("{\"foreman_export_version\":1.0,\"mods\":[]}");

            Assert.AreEqual(false, PresetProcessor.ReadPresetHeader(preset)?.IsOutdated);
        }

        [TestMethod]
        public void ReadPresetHeader_NoMarkerNoBase_HasNoVersion() {
            Preset preset = WriteUserPreset("{\"foreman_export_version\":1,\"mods\":[{\"name\":\"core\",\"version\":\"1.0\"}]}");

            Assert.AreEqual(new PresetHeader(null, false), PresetProcessor.ReadPresetHeader(preset));
        }

        [TestMethod]
        public void ReadPresetHeader_MissingFile_ReturnsNull() {
            Assert.IsNull(PresetProcessor.ReadPresetHeader(new Preset("No such preset " + Path.GetRandomFileName(), false, false)));
        }

        [TestMethod]
        public void ReadPresetHeader_DamagedFile_ReturnsNull() {
            Preset preset = WriteUserPreset("{\"foreman_export_version\":1,\"mods\":[{\"name\":");

            Assert.IsNull(PresetProcessor.ReadPresetHeader(preset));
        }

        [TestMethod]
        public async Task ReadPresetHeader_MatchesDataCacheGameVersion() {
            foreach (string name in BundledNames) {
                var cache = new DataCache(false);
                await cache.LoadAllData(new Preset(name, false, false), new Progress<System.Collections.Generic.KeyValuePair<int, string>>(), loadIcons: false);

                PresetHeader? header = PresetProcessor.ReadPresetHeader(new Preset(name, false, false));

                Assert.AreEqual(cache.GameVersion, header?.GameVersion, name);
                Assert.AreEqual(PresetExportFormat.IsOutdated(cache), header?.IsOutdated, name);
            }
        }
    }
}
