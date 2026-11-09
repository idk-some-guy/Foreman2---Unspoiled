using Foreman.DataCaching.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace ForemanTest.Storage {
    [TestClass]
    public class SchemaMigrationTests {
        private string tempRoot = "";

        [TestInitialize]
        public void CreateTempRoot() {
            tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempRoot);
        }

        [TestCleanup]
        public void DeleteTempRoot() {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }

        private sealed class SettingsRowV1 {
            public int Id { get; set; }
            public string CurrentPresetName { get; set; } = "";
            public int DefaultModuleOption { get; set; }
            public int MinorGridlines { get; set; }
            public int MajorGridlines { get; set; }
            public bool AltGridlines { get; set; }
            public bool ShowHidden { get; set; }
            public bool IgnoreAssemblerStatus { get; set; }
            public bool DynamicLineWidth { get; set; }
            public bool RecipeNameOnlyFilter { get; set; }
            public int LevelOfDetail { get; set; }
            public int DefaultAssemblerOption { get; set; }
            public int DefaultRateUnit { get; set; }
            public string LastSaveFileLocation { get; set; } = "";
            public bool ShowRecipeToolTip { get; set; }
            public bool ShowUnavailable { get; set; }
            public int NodeCountForSimpleView { get; set; }
            public bool UseRecipeBWfilters { get; set; }
            public bool ShowWarningArrows { get; set; }
            public bool ShowErrorArrows { get; set; }
            public bool AbbreviateSciPacks { get; set; }
            public bool RoundAssemblerCount { get; set; }
            public bool EnableExtraProductivityForNonMiners { get; set; }
            public bool ShowDisconnectedArrows { get; set; }
            public int DefaultNodeDirection { get; set; }
            public bool FlagOUSuppliedNodes { get; set; }
            public bool ShowOUSuppliedArrows { get; set; }
            public bool IconsOnlyView { get; set; }
            public int IconsSize { get; set; }
            public bool SimplePassthroughNodes { get; set; }
            public bool ArrowsOnLinks { get; set; }
            public bool SmartNodeDirection { get; set; }
            public int FlagDarkMode { get; set; }
            public bool UpgradeRequired { get; set; }
            public string AnnotTextFontFamily { get; set; } = "";
            public string AnnotTextFontSize { get; set; } = "";
            public int AnnotTextFontStyle { get; set; }
            public int AnnotTextColorARGB { get; set; }
            public int AnnotTextBackColorARGB { get; set; }
            public int AnnotTextAlign { get; set; }
            public int AnnotShapeType { get; set; }
            public int AnnotShapeFillColorARGB { get; set; }
            public int AnnotShapeBorderColorARGB { get; set; }
            public int AnnotShapeBorderWidth { get; set; }
            public int QualitySteps { get; set; }
            public decimal LowPriorityPower { get; set; }
            public bool PullConsumerNodes { get; set; }
            public decimal PullConsumerNodesPower { get; set; }
        }

        private sealed class LegacyV1Context(string dbPath) : DbContext {
            public DbSet<SettingsRowV1> Settings => Set<SettingsRowV1>();
            public DbSet<SchemaVersionRow> SchemaVersions => Set<SchemaVersionRow>();

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
                optionsBuilder.UseSqlite($"Data Source={dbPath}");

            protected override void OnModelCreating(ModelBuilder modelBuilder) {
                modelBuilder.Entity<SettingsRowV1>(entity => {
                    entity.ToTable("Settings");
                    entity.HasKey(row => row.Id);
                    entity.Property(row => row.Id).ValueGeneratedNever();
                    entity.Property(row => row.LowPriorityPower).HasPrecision(18, 6);
                    entity.Property(row => row.PullConsumerNodesPower).HasPrecision(18, 6);
                });
                modelBuilder.Entity<SchemaVersionRow>(entity => entity.HasKey(row => row.Version));
            }
        }

        private string BuildV1Fixture() {
            string dbPath = StoragePaths.DbPath(tempRoot);
            using var legacy = new LegacyV1Context(dbPath);
            legacy.Database.EnsureCreated();
            legacy.Settings.Add(new SettingsRowV1 { Id = 1, CurrentPresetName = "pre-migration-preset", IconsSize = 99 });
            legacy.SchemaVersions.Add(new SchemaVersionRow { Version = 1 });
            legacy.SaveChanges();
            return dbPath;
        }

        [TestMethod]
        public void Open_V1Db_AddsUiScaleAndInspectorPinnedColumns_PreservesData_BumpsVersionToCurrent() {
            string dbPath = BuildV1Fixture();

            using ForemanDbContext context = ForemanDbContext.Open(dbPath);

            SettingsRow row = context.Settings.Single();
            Assert.AreEqual("pre-migration-preset", row.CurrentPresetName);
            Assert.AreEqual(99, row.IconsSize);
            Assert.AreEqual(1.0, row.UiScale);
            Assert.IsFalse(row.InspectorPinned);
            Assert.AreEqual(5, context.SchemaVersions.Single().Version);
        }

        [TestMethod]
        public void Open_V1Db_ReopenedTwice_IsIdempotent() {
            string dbPath = BuildV1Fixture();

            using (ForemanDbContext.Open(dbPath)) { }

            using ForemanDbContext reopened = ForemanDbContext.Open(dbPath);
            Assert.AreEqual(5, reopened.SchemaVersions.Single().Version);
            Assert.AreEqual("pre-migration-preset", reopened.Settings.Single().CurrentPresetName);
        }

        private string BuildCrashedMigrationFixture() {
            string dbPath = BuildV1Fixture();
            using (var legacy = new LegacyV1Context(dbPath)) {
                legacy.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN UiScale REAL NOT NULL DEFAULT 1.0");
            }
            return dbPath;
        }

        [TestMethod]
        public void Open_HalfMigratedV1Db_HealsWithoutThrowing_PreservesData() {
            string dbPath = BuildCrashedMigrationFixture();

            using ForemanDbContext context = ForemanDbContext.Open(dbPath);

            SettingsRow row = context.Settings.Single();
            Assert.AreEqual("pre-migration-preset", row.CurrentPresetName);
            Assert.AreEqual(99, row.IconsSize);
            Assert.AreEqual(1.0, row.UiScale);
            Assert.AreEqual(5, context.SchemaVersions.Single().Version);
        }

        private sealed class SettingsRowV2 {
            public int Id { get; set; }
            public string CurrentPresetName { get; set; } = "";
            public int DefaultModuleOption { get; set; }
            public int MinorGridlines { get; set; }
            public int MajorGridlines { get; set; }
            public bool AltGridlines { get; set; }
            public bool ShowHidden { get; set; }
            public bool IgnoreAssemblerStatus { get; set; }
            public bool DynamicLineWidth { get; set; }
            public bool RecipeNameOnlyFilter { get; set; }
            public int LevelOfDetail { get; set; }
            public int DefaultAssemblerOption { get; set; }
            public int DefaultRateUnit { get; set; }
            public string LastSaveFileLocation { get; set; } = "";
            public bool ShowRecipeToolTip { get; set; }
            public bool ShowUnavailable { get; set; }
            public int NodeCountForSimpleView { get; set; }
            public bool UseRecipeBWfilters { get; set; }
            public bool ShowWarningArrows { get; set; }
            public bool ShowErrorArrows { get; set; }
            public bool AbbreviateSciPacks { get; set; }
            public bool RoundAssemblerCount { get; set; }
            public bool EnableExtraProductivityForNonMiners { get; set; }
            public bool ShowDisconnectedArrows { get; set; }
            public int DefaultNodeDirection { get; set; }
            public bool FlagOUSuppliedNodes { get; set; }
            public bool ShowOUSuppliedArrows { get; set; }
            public bool IconsOnlyView { get; set; }
            public int IconsSize { get; set; }
            public bool SimplePassthroughNodes { get; set; }
            public bool ArrowsOnLinks { get; set; }
            public bool SmartNodeDirection { get; set; }
            public int FlagDarkMode { get; set; }
            public bool UpgradeRequired { get; set; }
            public string AnnotTextFontFamily { get; set; } = "";
            public string AnnotTextFontSize { get; set; } = "";
            public int AnnotTextFontStyle { get; set; }
            public int AnnotTextColorARGB { get; set; }
            public int AnnotTextBackColorARGB { get; set; }
            public int AnnotTextAlign { get; set; }
            public int AnnotShapeType { get; set; }
            public int AnnotShapeFillColorARGB { get; set; }
            public int AnnotShapeBorderColorARGB { get; set; }
            public int AnnotShapeBorderWidth { get; set; }
            public int QualitySteps { get; set; }
            public decimal LowPriorityPower { get; set; }
            public bool PullConsumerNodes { get; set; }
            public decimal PullConsumerNodesPower { get; set; }
            public double UiScale { get; set; }
        }

        private sealed class LegacyV2Context(string dbPath) : DbContext {
            public DbSet<SettingsRowV2> Settings => Set<SettingsRowV2>();
            public DbSet<SchemaVersionRow> SchemaVersions => Set<SchemaVersionRow>();

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
                optionsBuilder.UseSqlite($"Data Source={dbPath}");

            protected override void OnModelCreating(ModelBuilder modelBuilder) {
                modelBuilder.Entity<SettingsRowV2>(entity => {
                    entity.ToTable("Settings");
                    entity.HasKey(row => row.Id);
                    entity.Property(row => row.Id).ValueGeneratedNever();
                    entity.Property(row => row.LowPriorityPower).HasPrecision(18, 6);
                    entity.Property(row => row.PullConsumerNodesPower).HasPrecision(18, 6);
                });
                modelBuilder.Entity<SchemaVersionRow>(entity => entity.HasKey(row => row.Version));
            }
        }

        private string BuildV2Fixture() {
            string dbPath = StoragePaths.DbPath(tempRoot);
            using var legacy = new LegacyV2Context(dbPath);
            legacy.Database.EnsureCreated();
            legacy.Settings.Add(new SettingsRowV2 { Id = 1, CurrentPresetName = "pre-migration-preset", IconsSize = 99, UiScale = 1.25 });
            legacy.SchemaVersions.Add(new SchemaVersionRow { Version = 2 });
            legacy.SaveChanges();
            return dbPath;
        }

        [TestMethod]
        public void Open_V2Db_AddsInspectorPinnedColumn_PreservesData_BumpsVersionToCurrent() {
            string dbPath = BuildV2Fixture();

            using ForemanDbContext context = ForemanDbContext.Open(dbPath);

            SettingsRow row = context.Settings.Single();
            Assert.AreEqual("pre-migration-preset", row.CurrentPresetName);
            Assert.AreEqual(1.25, row.UiScale);
            Assert.IsFalse(row.InspectorPinned);
            Assert.AreEqual(5, context.SchemaVersions.Single().Version);
        }

        [TestMethod]
        public void Open_V2Db_ReopenedTwice_IsIdempotent() {
            string dbPath = BuildV2Fixture();

            using (ForemanDbContext.Open(dbPath)) { }

            using ForemanDbContext reopened = ForemanDbContext.Open(dbPath);
            Assert.AreEqual(5, reopened.SchemaVersions.Single().Version);
            Assert.AreEqual("pre-migration-preset", reopened.Settings.Single().CurrentPresetName);
        }

        private string BuildCrashedV2MigrationFixture() {
            string dbPath = BuildV2Fixture();
            using (var legacy = new LegacyV2Context(dbPath)) {
                legacy.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN InspectorPinned INTEGER NOT NULL DEFAULT 0");
            }
            return dbPath;
        }

        [TestMethod]
        public void Open_HalfMigratedV2Db_HealsWithoutThrowing_PreservesData() {
            string dbPath = BuildCrashedV2MigrationFixture();

            using ForemanDbContext context = ForemanDbContext.Open(dbPath);

            SettingsRow row = context.Settings.Single();
            Assert.AreEqual("pre-migration-preset", row.CurrentPresetName);
            Assert.AreEqual(1.25, row.UiScale);
            Assert.AreEqual(5, context.SchemaVersions.Single().Version);
        }

        private string BuildV3Fixture() {
            string dbPath = BuildV2Fixture();
            using var legacy = new LegacyV2Context(dbPath);
            legacy.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN InspectorPinned INTEGER NOT NULL DEFAULT 0");
            legacy.Database.ExecuteSqlRaw("UPDATE Settings SET InspectorPinned = 1, ShowRecipeToolTip = 1, RecipeNameOnlyFilter = 1");
            legacy.Database.ExecuteSqlRaw("UPDATE SchemaVersions SET Version = 3");
            return dbPath;
        }

        private static string[] SettingsColumns(ForemanDbContext context) {
            IDbConnection connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(Settings)";
            using IDataReader reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
                names.Add(reader.GetString(reader.GetOrdinal("name")));
            return [.. names];
        }

        [TestMethod]
        public void V3Database_OpensMigratesToCurrentOnce_AndRoundTrips() {
            string dbPath = BuildV3Fixture();

            using (ForemanDbContext context = ForemanDbContext.Open(dbPath)) {
                Assert.AreEqual(5, context.SchemaVersions.Single().Version);
                string[] columns = SettingsColumns(context);
                CollectionAssert.DoesNotContain(columns, "ShowRecipeToolTip");
                CollectionAssert.DoesNotContain(columns, "RecipeNameOnlyFilter");
                CollectionAssert.AreEquivalent(
                    typeof(SettingsRow).GetProperties().Select(p => p.Name).ToArray(), columns);

                SettingsRow row = context.Settings.Single();
                Assert.AreEqual("pre-migration-preset", row.CurrentPresetName);
                Assert.AreEqual(99, row.IconsSize);
                Assert.AreEqual(1.25, row.UiScale);
                Assert.IsTrue(row.InspectorPinned);
                Assert.AreEqual(300, row.InspectorWidth);
                Assert.AreEqual(760, row.BrowserWidth);
                Assert.AreEqual(520, row.BrowserHeight);
                Assert.AreEqual("", row.BrowserItemsGroup);
                Assert.AreEqual("", row.BrowserRecipesGroup);
                Assert.IsNull(row.SettingsEnabledObjectsWidth);
                Assert.IsNull(row.SettingsEnabledObjectsHeight);
                Assert.IsNull(row.SettingsAdvancedWidth);
                Assert.IsNull(row.SettingsAdvancedHeight);
                Assert.AreEqual(0, row.DefaultLinkRouting);

                row.InspectorWidth = 333;
                row.BrowserItemsGroup = "logistics";
                row.SettingsAdvancedWidth = 700;
                row.SettingsAdvancedHeight = 640;
                row.DefaultLinkRouting = 1;
                context.SaveChanges();
            }

            using (ForemanDbContext reopened = ForemanDbContext.Open(dbPath)) {
                Assert.AreEqual(5, reopened.SchemaVersions.Single().Version);
                SettingsRow row = reopened.Settings.Single();
                Assert.AreEqual("pre-migration-preset", row.CurrentPresetName);
                Assert.IsTrue(row.InspectorPinned);
                Assert.AreEqual(333, row.InspectorWidth);
                Assert.AreEqual("logistics", row.BrowserItemsGroup);
                Assert.AreEqual(700, row.SettingsAdvancedWidth);
                Assert.AreEqual(640, row.SettingsAdvancedHeight);
                Assert.IsNull(row.SettingsEnabledObjectsWidth);
                Assert.AreEqual(1, row.DefaultLinkRouting);
            }
        }
    }
}
