using Foreman.DataCaching.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace ForemanTest.Storage {
    [TestClass]
    public class GraphLibraryMigrationTests {
        private string tempRoot = "";

        [TestInitialize]
        public void CreateTempRoot() {
            tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
        }

        [TestCleanup]
        public void DeleteTempRoot() {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        private static readonly string[] V5Drops = [
            "ALTER TABLE Settings DROP COLUMN ShowToolbar",
            "ALTER TABLE Settings DROP COLUMN ShowWelcomeAtLaunch",
            "ALTER TABLE Settings DROP COLUMN OpenRecentClearedAt",
            "ALTER TABLE Settings DROP COLUMN GraphSummaryLeft",
            "ALTER TABLE Settings DROP COLUMN GraphSummaryTop",
            "ALTER TABLE Settings DROP COLUMN GraphSummaryWidth",
            "ALTER TABLE Settings DROP COLUMN GraphSummaryHeight",
            "ALTER TABLE Settings DROP COLUMN GraphSummarySegment",
        ];

        private string BuildV4Fixture() {
            string dbPath = StoragePaths.DbPath(tempRoot);
            using (ForemanDbContext context = ForemanDbContext.Open(dbPath)) {
                context.Settings.Add(new SettingsRow { Id = 1, CurrentPresetName = "v4-preset", InspectorWidth = 280, UiScale = 1.25 });
                context.SaveChanges();
                context.Database.ExecuteSqlRaw("DROP TABLE GraphVersions");
                context.Database.ExecuteSqlRaw("DROP TABLE Graphs");
                foreach (string statement in V5Drops)
                    context.Database.ExecuteSqlRaw(statement);
                context.Database.ExecuteSqlRaw("UPDATE SchemaVersions SET Version = 4");
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            return dbPath;
        }

        private static List<string> Names(ForemanDbContext context, string sql) {
            IDbConnection connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            using IDataReader reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
                names.Add(reader.GetString(0));
            return names;
        }

        [TestMethod]
        public void V4Fixture_OpensAsV5_WithBothTables_SettingsValuesUnchanged() {
            string dbPath = BuildV4Fixture();

            using ForemanDbContext context = ForemanDbContext.Open(dbPath);

            Assert.AreEqual(5, context.SchemaVersions.Single().Version);
            List<string> tables = Names(context, "SELECT name FROM sqlite_master WHERE type = 'table'");
            CollectionAssert.Contains(tables, "Graphs");
            CollectionAssert.Contains(tables, "GraphVersions");
            SettingsRow row = context.Settings.Single();
            Assert.AreEqual("v4-preset", row.CurrentPresetName);
            Assert.AreEqual(280, row.InspectorWidth);
            Assert.AreEqual(1.25, row.UiScale);
            Assert.IsTrue(row.ShowToolbar);
            Assert.IsTrue(row.ShowWelcomeAtLaunch);
            Assert.IsNull(row.OpenRecentClearedAt);
            Assert.IsNull(row.GraphSummaryWidth);
            Assert.AreEqual(0, row.GraphSummarySegment);
            Assert.AreEqual(0, context.Graphs.Count());
        }

        [TestMethod]
        public void V4Fixture_HandWrittenTables_RoundTripEveryFieldThroughTheStore() {
            string dbPath = BuildV4Fixture();
            var store = new GraphLibraryStore(dbPath);
            DateTime created = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            DateTime saved = created.AddMinutes(5);
            byte[] thumbnail = [1, 2, 3, 4];

            GraphRow row = store.Create("Plant", "preset", "data", 3, "/tmp/plant.fjson", created);
            store.WriteAutosave(row.Id, "data 2", 4, thumbnail, created.AddMinutes(2));
            GraphVersionRow version = store.AddVersion(row.Id, GraphVersionKind.Saved, saved);

            GraphRow read = store.Get(row.Id)!;
            Assert.AreEqual("Plant", read.Name);
            Assert.AreEqual("preset", read.PresetName);
            Assert.AreEqual("data 2", read.Data);
            Assert.AreEqual(4, read.NodeCount);
            Assert.AreEqual("/tmp/plant.fjson", read.SourcePath);
            Assert.AreEqual(created, read.CreatedAt);
            Assert.AreEqual(created.AddMinutes(2), read.ModifiedAt);
            Assert.AreEqual(created, read.LastOpenedAt);
            Assert.AreEqual(saved, read.SavedAt);
            CollectionAssert.AreEqual(thumbnail, read.Thumbnail);
            GraphVersionRow readVersion = store.Versions(row.Id).Single();
            Assert.AreEqual(version.Id, readVersion.Id);
            Assert.AreEqual(row.Id, readVersion.GraphId);
            Assert.AreEqual(saved, readVersion.CreatedAt);
            Assert.AreEqual(GraphVersionKind.Saved, readVersion.Kind);
            Assert.AreEqual(4, readVersion.NodeCount);
            Assert.AreEqual("data 2", readVersion.Data);

            store.Delete(row.Id);

            Assert.IsNull(store.Get(row.Id));
            Assert.AreEqual(0, store.Versions(row.Id).Count);
        }

        [TestMethod]
        public void V4Fixture_MigrationIsIdempotentAfterACrashedBump() {
            string dbPath = BuildV4Fixture();
            using (ForemanDbContext first = ForemanDbContext.Open(dbPath))
                first.Database.ExecuteSqlRaw("UPDATE SchemaVersions SET Version = 4");

            using ForemanDbContext context = ForemanDbContext.Open(dbPath);

            Assert.AreEqual(5, context.SchemaVersions.Single().Version);
            CollectionAssert.AreEquivalent(
                typeof(SettingsRow).GetProperties().Select(p => p.Name).ToArray(),
                Names(context, "SELECT name FROM pragma_table_info('Settings')"));
        }

        [TestMethod]
        public void V5File_RunsNoMigration() {
            string dbPath = StoragePaths.DbPath(tempRoot);
            using (ForemanDbContext created = ForemanDbContext.Open(dbPath)) {
                created.Settings.Add(new SettingsRow { Id = 1, CurrentPresetName = "v5-preset", InspectorWidth = 333 });
                created.SaveChanges();
                created.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ShowRecipeToolTip INTEGER NOT NULL DEFAULT 1");
            }

            using ForemanDbContext reopened = ForemanDbContext.Open(dbPath);

            Assert.AreEqual(5, reopened.SchemaVersions.Single().Version);
            Assert.AreEqual("v5-preset", reopened.Settings.Single().CurrentPresetName);
            Assert.AreEqual(333, reopened.Settings.Single().InspectorWidth);
            CollectionAssert.Contains(
                Names(reopened, "SELECT name FROM pragma_table_info('Settings')"), "ShowRecipeToolTip");
        }
    }
}
