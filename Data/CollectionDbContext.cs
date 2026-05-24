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
        public DbSet<TradeBinderEntry> TradeBinderEntries { get; set; }
        public DbSet<WantListEntry> WantListEntries { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={AppFolderService.CollectionDatabasePath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CollectionEntry>()
                .HasIndex(e => e.PoolId);
        }

        public void EnsureCreated()
        {
            // Only create if the file doesn't exist — never overwrite existing data
            if (!System.IO.File.Exists(AppFolderService.CollectionDatabasePath))
                Database.EnsureCreated();
        }

        public void MigrateSchema()
        {
            // Only create if the file doesn't exist — never overwrite existing data
            if (!System.IO.File.Exists(AppFolderService.CollectionDatabasePath))
                Database.EnsureCreated();

            // Create ConspiracyCollectionEntries table if it doesn't exist
            try
            {
#pragma warning disable EF1002
                Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ConspiracyCollectionEntries (
                        ConspiracyCollectionEntryId  INTEGER PRIMARY KEY AUTOINCREMENT,
                        ConspiracyId                 INTEGER NOT NULL DEFAULT 0,
                        Quantity                     INTEGER NOT NULL DEFAULT 0,
                        FoilQuantity                 INTEGER NOT NULL DEFAULT 0,
                        Condition                    TEXT NOT NULL DEFAULT 'Unknown',
                        Language                     TEXT NOT NULL DEFAULT 'English',
                        Notes                        TEXT NOT NULL DEFAULT '',
                        StorageLocation              TEXT NOT NULL DEFAULT '',
                        DateAdded                    TEXT NOT NULL DEFAULT '',
                        DateModified                 TEXT NOT NULL DEFAULT ''
                    )");
#pragma warning restore EF1002
            }
            catch { }

            // Create TradeBinderEntries table if it doesn't exist
            try
            {
#pragma warning disable EF1002
                Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS TradeBinderEntries (
                        TradeBinderEntryId  INTEGER PRIMARY KEY AUTOINCREMENT,
                        PoolId              INTEGER NOT NULL DEFAULT 0,
                        Quantity            INTEGER NOT NULL DEFAULT 1,
                        IsFoil              INTEGER NOT NULL DEFAULT 0,
                        Condition           TEXT NOT NULL DEFAULT 'Near Mint',
                        AskingPrice         REAL,
                        DateAdded           TEXT NOT NULL DEFAULT ''
                    )");
                Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS IX_TradeBinderEntries_PoolId ON TradeBinderEntries (PoolId)");
#pragma warning restore EF1002
            }
            catch { }

            // Create WantListEntries table if it doesn't exist
            try
            {
#pragma warning disable EF1002
                Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS WantListEntries (
                        WantListEntryId  INTEGER PRIMARY KEY AUTOINCREMENT,
                        PoolId           INTEGER NOT NULL DEFAULT 0,
                        Quantity         INTEGER NOT NULL DEFAULT 1,
                        IsFoil           INTEGER NOT NULL DEFAULT 0,
                        OfferPrice       REAL,
                        DateAdded        TEXT NOT NULL DEFAULT ''
                    )");
                Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS IX_WantListEntries_PoolId ON WantListEntries (PoolId)");
#pragma warning restore EF1002
            }
            catch { }

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
                ("TradeBinderEntries",         "Notes",        "TEXT NOT NULL DEFAULT ''"),
                ("WantListEntries",            "Notes",        "TEXT NOT NULL DEFAULT ''"),
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