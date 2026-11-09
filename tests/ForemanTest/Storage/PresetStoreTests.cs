using Foreman.DataCaching.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;

namespace ForemanTest.Storage {
    [TestClass]
    public class PresetStoreTests {
        private string tempRoot = "";

        private const string SamplePJson = """
        {
          "mods": [
            { "name": "base", "version": "2.0.28" },
            { "name": "quality", "version": "1.0.0" }
          ],
          "difficulty": { "0": 0, "1": 1 },
          "items": [
            { "name": "iron-plate", "type": "item" },
            { "name": "copper-plate", "type": "item" }
          ],
          "recipes": [
            { "name": "iron-gear-wheel", "ingredients": [ { "name": "iron-plate", "amount": 2 } ] }
          ]
        }
        """;

        private const string SampleOverlayJson = """{ "hiddenItems": [ "copper-plate" ] }""";

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

        [TestMethod]
        public void Import_ThenRead_RoundTripsBothPartsTextIdentical_AcrossFreshContext() {
            string dbPath = StoragePaths.DbPath(tempRoot);

            using (ForemanDbContext db = ForemanDbContext.Open(dbPath))
                PresetStore.Import(db, "vanilla-2.0", SamplePJson, SampleOverlayJson);

            using ForemanDbContext reopened = new ForemanDbContext(dbPath);
            (string pjsonText, string? customOverlayJson) = PresetStore.Read(reopened, "vanilla-2.0");

            Assert.AreEqual(SamplePJson, pjsonText);
            Assert.AreEqual(SampleOverlayJson, customOverlayJson);
        }

        [TestMethod]
        public void Import_WithNullOverlay_RoundTripsOverlayAsNull() {
            string dbPath = StoragePaths.DbPath(tempRoot);

            using (ForemanDbContext db = ForemanDbContext.Open(dbPath))
                PresetStore.Import(db, "vanilla-2.0", SamplePJson, null);

            using ForemanDbContext reopened = new ForemanDbContext(dbPath);
            (string pjsonText, string? customOverlayJson) = PresetStore.Read(reopened, "vanilla-2.0");

            Assert.AreEqual(SamplePJson, pjsonText);
            Assert.IsNull(customOverlayJson);
        }

        [TestMethod]
        public void Import_OverwritingExistingPreset_ReplacesRowContentAndKeepsRowCountAtOne() {
            string dbPath = StoragePaths.DbPath(tempRoot);

            using ForemanDbContext db = ForemanDbContext.Open(dbPath);
            PresetStore.Import(db, "vanilla-2.0", SamplePJson, SampleOverlayJson);
            System.DateTime firstImportedUtc = db.Presets.Single().ImportedUtc;

            const string updatedPJson = """{ "mods": [], "difficulty": { "0": 1, "1": 0 } }""";
            PresetStore.Import(db, "vanilla-2.0", updatedPJson, null);

            Assert.AreEqual(1, db.Presets.Count());
            (string pjsonText, string? customOverlayJson) = PresetStore.Read(db, "vanilla-2.0");
            Assert.AreEqual(updatedPJson, pjsonText);
            Assert.IsNull(customOverlayJson);
            Assert.IsTrue(db.Presets.Single().ImportedUtc >= firstImportedUtc);
        }
    }
}
