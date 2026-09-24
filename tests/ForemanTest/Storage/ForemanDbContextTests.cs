using Foreman.DataCaching.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

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

        //A brand new db's EnsureCreated builds the current-model schema, so it seeds the current version.
        [TestMethod]
        public void Open_CreatesDbFile_AndSeedsSchemaVersionOnce() {
            string dbPath = StoragePaths.DbPath(tempRoot);

            using (ForemanDbContext context = ForemanDbContext.Open(dbPath)) {
                Assert.IsTrue(File.Exists(dbPath));
                Assert.AreEqual(4, context.SchemaVersions.Single().Version);
            }

            using (ForemanDbContext reopened = ForemanDbContext.Open(dbPath)) {
                Assert.AreEqual(1, reopened.SchemaVersions.Count());
                Assert.AreEqual(4, reopened.SchemaVersions.Single().Version);
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

        //A locked-down directory is a different failure from corruption (SQLITE_READONLY vs
        //SQLITE_NOTADB/SQLITE_CORRUPT): quarantining a perfectly good db file over a transient write
        //failure would itself be a data-loss bug, so Open must let this one propagate untouched.
        [TestMethod]
        [UnsupportedOSPlatform("windows")]
        public void Open_ReadOnlyDirectory_PropagatesSqliteException_DoesNotQuarantine() {
            string dbPath = StoragePaths.DbPath(tempRoot);
            using (ForemanDbContext.Open(dbPath)) { }

            //Microsoft.Data.Sqlite pools connections by path; without clearing the pool, the reopen
            //below would reuse a handle opened before the chmod and never hit the permission wall.
            SqliteConnection.ClearAllPools();
            File.SetUnixFileMode(tempRoot, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            try {
                Assert.ThrowsExactly<SqliteException>(() => {
                    using ForemanDbContext context = ForemanDbContext.Open(dbPath);
                });

                Assert.IsFalse(File.Exists(dbPath + ".corrupt"));
            } finally {
                File.SetUnixFileMode(tempRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }
}
