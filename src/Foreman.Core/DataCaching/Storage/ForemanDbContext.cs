using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Data;
using System.IO;
using System.Linq;

namespace Foreman.DataCaching.Storage {
    public sealed class ForemanDbContext : DbContext {
        private readonly string _dbPath;

        public ForemanDbContext(string dbPath) {
            _dbPath = dbPath;
        }

        public DbSet<SettingsRow> Settings => Set<SettingsRow>();
        public DbSet<PresetDataRow> Presets => Set<PresetDataRow>();
        public DbSet<IconBlob> IconBlobs => Set<IconBlob>();
        public DbSet<PresetIconRow> PresetIcons => Set<PresetIconRow>();
        public DbSet<SchemaVersionRow> SchemaVersions => Set<SchemaVersionRow>();
        public DbSet<GraphRow> Graphs => Set<GraphRow>();
        public DbSet<GraphVersionRow> GraphVersions => Set<GraphVersionRow>();

        private static readonly object BootstrapLock = new();

        //EnsureCreated never alters an existing file's schema, so each bump needs its own step in MigrateToCurrent.
        private const int CurrentSchemaVersion = 6;

        public static ForemanDbContext Open(string dbPath) {
            string? directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }

            lock (BootstrapLock) {
                ForemanDbContext context = OpenOrQuarantine(dbPath);

                SchemaVersionRow? versionRow = context.SchemaVersions.SingleOrDefault();
                if (versionRow is null) {
                    context.SchemaVersions.Add(new SchemaVersionRow { Version = CurrentSchemaVersion });
                    context.SaveChanges();
                } else if (versionRow.Version < CurrentSchemaVersion) {
                    MigrateToCurrent(context, versionRow);
                }

                return context;
            }
        }

        private static void MigrateToCurrent(ForemanDbContext context, SchemaVersionRow versionRow) {
            using IDbContextTransaction transaction = context.Database.BeginTransaction();

            if (versionRow.Version == 1 && !ColumnExists(context, "Settings", "UiScale")) {
                context.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN UiScale REAL NOT NULL DEFAULT 1.0");
            }
            if (versionRow.Version < 3 && !ColumnExists(context, "Settings", "InspectorPinned")) {
                context.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN InspectorPinned INTEGER NOT NULL DEFAULT 0");
            }
            if (versionRow.Version < 4) {
                MigrateToV4(context);
            }
            if (versionRow.Version < 5) {
                MigrateToV5(context);
            }
            if (versionRow.Version < 6) {
                MigrateToV6(context);
            }

            context.SchemaVersions.Remove(versionRow);
            context.SchemaVersions.Add(new SchemaVersionRow { Version = CurrentSchemaVersion });
            context.SaveChanges();

            transaction.Commit();
        }

        private static readonly (string Column, string Definition)[] V4AddedColumns = [
            ("InspectorWidth", "REAL NOT NULL DEFAULT 300"),
            ("BrowserWidth", "REAL NOT NULL DEFAULT 760"),
            ("BrowserHeight", "REAL NOT NULL DEFAULT 520"),
            ("BrowserItemsGroup", "TEXT NOT NULL DEFAULT ''"),
            ("BrowserRecipesGroup", "TEXT NOT NULL DEFAULT ''"),
            ("SettingsEnabledObjectsWidth", "REAL NULL"),
            ("SettingsEnabledObjectsHeight", "REAL NULL"),
            ("SettingsAdvancedWidth", "REAL NULL"),
            ("SettingsAdvancedHeight", "REAL NULL"),
            ("DefaultLinkRouting", "INTEGER NOT NULL DEFAULT 0"),
        ];

        private static readonly string[] V4DroppedColumns = ["ShowRecipeToolTip", "RecipeNameOnlyFilter"];

        private static void MigrateToV4(ForemanDbContext context) {
            foreach ((string column, string definition) in V4AddedColumns) {
                if (!ColumnExists(context, "Settings", column))
                    Execute(context, "ALTER TABLE Settings ADD COLUMN " + column + " " + definition);
            }
            foreach (string column in V4DroppedColumns) {
                if (ColumnExists(context, "Settings", column))
                    Execute(context, "ALTER TABLE Settings DROP COLUMN " + column);
            }
        }

        private static readonly (string Column, string Definition)[] V5AddedColumns = [
            ("ShowToolbar", "INTEGER NOT NULL DEFAULT 1"),
            ("ShowWelcomeAtLaunch", "INTEGER NOT NULL DEFAULT 1"),
            ("OpenRecentClearedAt", "TEXT NULL"),
            ("GraphSummaryLeft", "REAL NULL"),
            ("GraphSummaryTop", "REAL NULL"),
            ("GraphSummaryWidth", "REAL NULL"),
            ("GraphSummaryHeight", "REAL NULL"),
            ("GraphSummarySegment", "INTEGER NOT NULL DEFAULT 0"),
        ];

        private static readonly (string Column, string Definition)[] V6AddedColumns = [
            ("ResearchLeft", "REAL NULL"),
            ("ResearchTop", "REAL NULL"),
            ("ResearchWidth", "REAL NULL"),
            ("ResearchHeight", "REAL NULL"),
            ("ResearchSegment", "INTEGER NOT NULL DEFAULT 0"),
        ];

        private static void MigrateToV6(ForemanDbContext context) {
            foreach ((string column, string definition) in V6AddedColumns) {
                if (!ColumnExists(context, "Settings", column))
                    Execute(context, "ALTER TABLE Settings ADD COLUMN " + column + " " + definition);
            }
        }

        private static void MigrateToV5(ForemanDbContext context) {
            Execute(context, """
                CREATE TABLE IF NOT EXISTS "Graphs" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Graphs" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "PresetName" TEXT NOT NULL,
                    "NodeCount" INTEGER NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "ModifiedAt" TEXT NOT NULL,
                    "LastOpenedAt" TEXT NOT NULL,
                    "SavedAt" TEXT NULL,
                    "Data" TEXT NOT NULL,
                    "Thumbnail" BLOB NULL,
                    "SourcePath" TEXT NULL
                )
                """);
            Execute(context, """
                CREATE TABLE IF NOT EXISTS "GraphVersions" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_GraphVersions" PRIMARY KEY,
                    "GraphId" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "Kind" INTEGER NOT NULL,
                    "NodeCount" INTEGER NOT NULL,
                    "Data" TEXT NOT NULL,
                    CONSTRAINT "FK_GraphVersions_Graphs_GraphId" FOREIGN KEY ("GraphId") REFERENCES "Graphs" ("Id") ON DELETE CASCADE
                )
                """);
            Execute(context, "CREATE INDEX IF NOT EXISTS \"IX_GraphVersions_GraphId\" ON \"GraphVersions\" (\"GraphId\")");
            foreach ((string column, string definition) in V5AddedColumns) {
                if (!ColumnExists(context, "Settings", column))
                    Execute(context, "ALTER TABLE Settings ADD COLUMN " + column + " " + definition);
            }
        }

        private static void Execute(ForemanDbContext context, string sql) => context.Database.ExecuteSqlRaw(sql);

        private static bool ColumnExists(ForemanDbContext context, string table, string column) {
            IDbConnection connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) {
                connection.Open();
            }

            using IDbCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table})";
            using IDataReader reader = command.ExecuteReader();
            int nameOrdinal = reader.GetOrdinal("name");
            while (reader.Read()) {
                if (string.Equals(reader.GetString(nameOrdinal), column, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        private const int SqliteErrorNotADatabase = 26;
        private const int SqliteErrorCorrupt = 11;

        private static ForemanDbContext OpenOrQuarantine(string dbPath) {
            ForemanDbContext context = new ForemanDbContext(dbPath);
            try {
                context.Database.EnsureCreated();
                return context;
            } catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteErrorNotADatabase or SqliteErrorCorrupt) {
                context.Dispose();
                //Microsoft.Data.Sqlite pools native connections by path; clear the pool before moving the file.
                SqliteConnection.ClearAllPools();
                ErrorLogging.LogException(ex, $"foreman.db at '{dbPath}' is unreadable; quarantining and recreating");
                QuarantineCorruptDb(dbPath);
                context = new ForemanDbContext(dbPath);
                context.Database.EnsureCreated();
                return context;
            }
        }

        private static void QuarantineCorruptDb(string dbPath) {
            MoveAside(dbPath, dbPath + ".corrupt");
            MoveAside(dbPath + "-wal", dbPath + "-wal.corrupt");
            MoveAside(dbPath + "-shm", dbPath + "-shm.corrupt");
        }

        private static void MoveAside(string sourcePath, string destinationPath) {
            if (!File.Exists(sourcePath))
                return;
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(sourcePath, destinationPath);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<SettingsRow>(entity => {
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).ValueGeneratedNever();
                entity.Property(row => row.LowPriorityPower).HasPrecision(18, 6);
                entity.Property(row => row.PullConsumerNodesPower).HasPrecision(18, 6);
            });

            modelBuilder.Entity<PresetDataRow>(entity => entity.HasKey(row => row.PresetName));

            modelBuilder.Entity<IconBlob>(entity => entity.HasKey(row => row.Sha256));

            modelBuilder.Entity<PresetIconRow>(entity => {
                entity.HasKey(row => new { row.PresetName, row.IconName });
                entity.HasOne<IconBlob>()
                    .WithMany()
                    .HasForeignKey(row => row.Sha256);
            });

            modelBuilder.Entity<SchemaVersionRow>(entity => entity.HasKey(row => row.Version));

            modelBuilder.Entity<GraphRow>(entity => {
                entity.HasKey(row => row.Id);
                entity.HasMany(row => row.Versions)
                    .WithOne()
                    .HasForeignKey(version => version.GraphId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GraphVersionRow>(entity => entity.HasKey(row => row.Id));
        }
    }
}
