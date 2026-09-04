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

        //Guards first-time schema creation/seeding against concurrent Open calls on the same process
        //(phase 9 Task 5: PresetProcessor/PresetImporter open the db far more often than the original
        //single settings-service caller did, so two callers racing a brand-new db file is now a real
        //scenario, not just a theoretical one) - EnsureCreated and the SchemaVersion seed insert aren't
        //safe to run from two threads at once against the same not-yet-existing file.
        private static readonly object BootstrapLock = new();

        //Bumped for the Settings.InspectorPinned column (phase 9 C2a Task 3). EnsureCreated only creates
        //a db from scratch - it never alters an existing file's schema - so a db already on disk needs
        //its own ALTER TABLE step to pick up a new column; see MigrateToCurrent below.
        private const int CurrentSchemaVersion = 3;

        //Opens (creating if needed) the db at dbPath and seeds SchemaVersion=1 (or migrates a v1 db
        //forward to CurrentSchemaVersion) exactly once; idempotent across repeated calls on the same path.
        public static ForemanDbContext Open(string dbPath) {
            string? directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }

            lock (BootstrapLock) {
                ForemanDbContext context = OpenOrQuarantine(dbPath);

                SchemaVersionRow? versionRow = context.SchemaVersions.SingleOrDefault();
                if (versionRow is null) {
                    //Brand new file - EnsureCreated just built the full current-model schema (UiScale
                    //and InspectorPinned columns included), so there's nothing to migrate.
                    context.SchemaVersions.Add(new SchemaVersionRow { Version = CurrentSchemaVersion });
                    context.SaveChanges();
                } else if (versionRow.Version < CurrentSchemaVersion) {
                    MigrateToCurrent(context, versionRow);
                }

                return context;
            }
        }

        //SchemaVersionRow keys itself by Version (no separate id column), so bumping the version is a
        //remove-then-add rather than an in-place field update.
        private static void MigrateToCurrent(ForemanDbContext context, SchemaVersionRow versionRow) {
            using IDbContextTransaction transaction = context.Database.BeginTransaction();

            //A prior Open may have crashed after an ALTER but before the version bump below; the column
            //would then already be there, and re-issuing the same ALTER would throw "duplicate column
            //name" - each step below is independently guarded so a v1 db can jump straight to current
            //in one Open call, picking up every column a v2 db would only need the second of.
            if (versionRow.Version == 1 && !ColumnExists(context, "Settings", "UiScale")) {
                context.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN UiScale REAL NOT NULL DEFAULT 1.0");
            }
            if (versionRow.Version < 3 && !ColumnExists(context, "Settings", "InspectorPinned")) {
                context.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN InspectorPinned INTEGER NOT NULL DEFAULT 0");
            }

            context.SchemaVersions.Remove(versionRow);
            context.SchemaVersions.Add(new SchemaVersionRow { Version = CurrentSchemaVersion });
            context.SaveChanges();

            transaction.Commit();
        }

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

        //SQLITE_NOTADB - the file isn't a SQLite database at all; SQLITE_CORRUPT - its own header/pages
        //are corrupt. Anything else a write can throw (locked, readonly, disk full, ...) is a real failure
        //the caller needs to see - quarantining a perfectly good db file over a transient write failure
        //would be its own data-loss bug.
        private const int SqliteErrorNotADatabase = 26;
        private const int SqliteErrorCorrupt = 11;

        //foreman.db is a rebuildable cache (settings fall back to json/defaults, presets reimport from
        //the legacy .pjson/.dat still on disk), so a file EnsureCreated can't open because it's actually
        //corrupt gets moved aside instead of taking the whole app down on every launch.
        private static ForemanDbContext OpenOrQuarantine(string dbPath) {
            ForemanDbContext context = new ForemanDbContext(dbPath);
            try {
                context.Database.EnsureCreated();
                return context;
            } catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteErrorNotADatabase or SqliteErrorCorrupt) {
                context.Dispose();
                //Microsoft.Data.Sqlite pools native connections by file path; without clearing the pool
                //here, a fresh connection after the file move below can still hand back a handle bound
                //to the old (now-renamed) inode instead of the new file.
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
        }
    }
}
