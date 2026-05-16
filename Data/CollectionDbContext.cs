using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.EntityFrameworkCore;

namespace BreakersOfE.Data
{
    /// <summary>
    /// Separate database for the user's collection.
    /// Lives in My Documents\Breakers of E\Collection\collection.db
    /// Completely independent of the card pool database — pool updates
    /// never touch this database.
    /// </summary>
    public class CollectionDbContext : DbContext
    {
        public DbSet<CollectionEntry> CollectionEntries { get; set; }
        public DbSet<TokenCollectionEntry> TokenCollectionEntries { get; set; }
        public DbSet<PlanarCollectionEntry> PlanarCollectionEntries { get; set; }
        public DbSet<SchemeCollectionEntry> SchemeCollectionEntries { get; set; }
        public DbSet<VanguardCollectionEntry> VanguardCollectionEntries { get; set; }
        public DbSet<ConspiracyCollectionEntry> ConspiracyCollectionEntries { get; set; }
        public DbSet<ArtSeriesCollectionEntry> ArtSeriesCollectionEntries { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={AppFolderService.CollectionDatabasePath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CollectionEntry>()
                .HasIndex(e => e.PoolId);
        }

        public void EnsureCreated() => Database.EnsureCreated();

        public void MigrateSchema()
        {
            Database.EnsureCreated();

            // Add any new columns safely
            var columns = new[]
            {
                ("CollectionEntries",          "FoilQuantity", "INTEGER NOT NULL DEFAULT 0"),
                ("CollectionEntries",          "UsedCount",    "INTEGER NOT NULL DEFAULT 0"),
                ("CollectionEntries",          "Condition",    "TEXT NOT NULL DEFAULT 'NM'"),
                ("TokenCollectionEntries",     "UsedCount",    "INTEGER NOT NULL DEFAULT 0"),
                ("PlanarCollectionEntries",    "UsedCount",    "INTEGER NOT NULL DEFAULT 0"),
                ("SchemeCollectionEntries",    "UsedCount",    "INTEGER NOT NULL DEFAULT 0"),
                ("VanguardCollectionEntries",  "UsedCount",    "INTEGER NOT NULL DEFAULT 0"),
                ("ArtSeriesCollectionEntries", "UsedCount",    "INTEGER NOT NULL DEFAULT 0"),
            };

            foreach (var (table, col, def) in columns)
            {
                try
                {
#pragma warning disable EF1002
                    Database.ExecuteSqlRaw(
                        $"ALTER TABLE {table} ADD COLUMN `{col}` {def}");
#pragma warning restore EF1002
                }
                catch { /* Column already exists — ignore */ }
            }
        }
    }
}