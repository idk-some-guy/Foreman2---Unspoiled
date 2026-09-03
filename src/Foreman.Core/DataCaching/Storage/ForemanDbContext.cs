using Microsoft.EntityFrameworkCore;
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

        //Opens (creating if needed) the db at dbPath and seeds SchemaVersion=1 exactly once;
        //idempotent across repeated calls on the same path.
        public static ForemanDbContext Open(string dbPath) {
            string? directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }

            ForemanDbContext context = new ForemanDbContext(dbPath);
            context.Database.EnsureCreated();

            if (!context.SchemaVersions.Any()) {
                context.SchemaVersions.Add(new SchemaVersionRow { Version = 1 });
                context.SaveChanges();
            }

            return context;
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
