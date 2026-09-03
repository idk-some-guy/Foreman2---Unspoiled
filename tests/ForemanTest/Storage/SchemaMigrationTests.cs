using Foreman.DataCaching.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;

namespace ForemanTest.Storage {
    //Proves the v1->v2 additive migration (phase 9 Task 3): EF's EnsureCreated is a no-op against an
    //already-existing db file, so a column added to SettingsRow (UiScale) never reaches a db written by
    //Task B2-era code without a real ALTER TABLE step. LegacyV1Context below reproduces that exact
    //pre-Task-3 schema (every SettingsRow column except UiScale) via EF's own DDL generator, so the
    //fixture is faithful rather than hand-guessed SQL.
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
        public void Open_V1Db_AddsUiScaleColumn_PreservesData_BumpsVersionToTwo() {
            string dbPath = BuildV1Fixture();

            using ForemanDbContext context = ForemanDbContext.Open(dbPath);

            SettingsRow row = context.Settings.Single();
            Assert.AreEqual("pre-migration-preset", row.CurrentPresetName);
            Assert.AreEqual(99, row.IconsSize);
            Assert.AreEqual(1.0, row.UiScale);
            Assert.AreEqual(2, context.SchemaVersions.Single().Version);
        }

        [TestMethod]
        public void Open_V1Db_ReopenedTwice_IsIdempotent() {
            string dbPath = BuildV1Fixture();

            using (ForemanDbContext.Open(dbPath)) { }

            using ForemanDbContext reopened = ForemanDbContext.Open(dbPath);
            Assert.AreEqual(2, reopened.SchemaVersions.Single().Version);
            Assert.AreEqual("pre-migration-preset", reopened.Settings.Single().CurrentPresetName);
        }
    }
}
