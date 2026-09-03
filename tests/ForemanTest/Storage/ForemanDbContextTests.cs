using Foreman.DataCaching.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;

namespace ForemanTest.Storage {
    //Exercises ForemanDbContext's lifecycle (phase 9 Task 1): temp-dir-only, never touches real
    //~/Library. No production consumer yet - this proves the EF plumbing on its own.
    [TestClass]
    public class ForemanDbContextTests {
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

        [TestMethod]
        public void Open_CreatesDbFile_AndSeedsSchemaVersionOnce() {
            string dbPath = StoragePaths.DbPath(tempRoot);

            using (ForemanDbContext context = ForemanDbContext.Open(dbPath)) {
                Assert.IsTrue(File.Exists(dbPath));
                Assert.AreEqual(1, context.SchemaVersions.Single().Version);
            }

            using (ForemanDbContext reopened = ForemanDbContext.Open(dbPath)) {
                Assert.AreEqual(1, reopened.SchemaVersions.Count());
                Assert.AreEqual(1, reopened.SchemaVersions.Single().Version);
            }
        }

        [TestMethod]
        public void SettingsRow_RoundTrips_AcrossFreshContext() {
            string dbPath = StoragePaths.DbPath(tempRoot);

            using (ForemanDbContext context = ForemanDbContext.Open(dbPath)) {
                context.Settings.Add(new SettingsRow {
                    Id = 1,
                    CurrentPresetName = "vanilla-2.0",
                    IconsSize = 99,
                });
                context.SaveChanges();
            }

            using (ForemanDbContext reopened = new ForemanDbContext(dbPath)) {
                SettingsRow row = reopened.Settings.Single();
                Assert.AreEqual("vanilla-2.0", row.CurrentPresetName);
                Assert.AreEqual(99, row.IconsSize);
            }
        }
    }
}
