using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace BreakersOfE.Data
{
    public class AppDbContext : DbContext
    {
        // ── Card Pool Tables ────────────────────────────────────────────────
        public DbSet<PoolCard> PoolCards { get; set; }
        public DbSet<TokenCard> TokenCards { get; set; }
        public DbSet<PlanarCard> PlanarCards { get; set; }
        public DbSet<SchemeCard> SchemeCards { get; set; }
        public DbSet<VanguardCard> VanguardCards { get; set; }
        public DbSet<ArtSeriesCard> ArtSeriesCards { get; set; }

        // ── Collection Tables ───────────────────────────────────────────────
        public DbSet<CollectionEntry> CollectionEntries { get; set; }
        public DbSet<TokenCollectionEntry> TokenCollectionEntries { get; set; }
        public DbSet<PlanarCollectionEntry> PlanarCollectionEntries { get; set; }
        public DbSet<SchemeCollectionEntry> SchemeCollectionEntries { get; set; }
        public DbSet<VanguardCollectionEntry> VanguardCollectionEntries { get; set; }
        public DbSet<ConspiracyCollectionEntry> ConspiracyCollectionEntries { get; set; }
        public DbSet<ArtSeriesCollectionEntry> ArtSeriesCollectionEntries { get; set; }

        // ── Other Tables ────────────────────────────────────────────────────
        public DbSet<TradeBinderEntry> TradeBinderEntries { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Always store the database next to the executable
            string dbPath = Services.AppFolderService.DatabasePath;

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        // ── Schema migration for new columns ─────────────────────────────────────
        public void MigrateSchema()
        {
            var tables = new[]
            {
                ("TokenCollectionEntries",     "UsedCount"),
                ("PlanarCollectionEntries",    "UsedCount"),
                ("SchemeCollectionEntries",    "UsedCount"),
                ("VanguardCollectionEntries",  "UsedCount"),
                ("ArtSeriesCollectionEntries", "UsedCount"),
            };

            foreach (var (table, column) in tables)
            {
                try
                {
#pragma warning disable EF1002
                    Database.ExecuteSqlRaw(
                        $"ALTER TABLE {table} ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0");
#pragma warning restore EF1002
                }
                catch { /* Column already exists — ignore */ }
            }

            // New CollectionEntries trading/inventory columns
            var collectionCols = new (string col, string type, string def)[]
            {
                ("BuyAt",       "REAL",    ""),
                ("SellAt",      "REAL",    ""),
                ("SellAtValue", "REAL",    ""),
                ("PriceHigh",   "REAL",    ""),
                ("MarketValue", "REAL",    ""),
                ("PriceLow",    "REAL",    ""),
                ("Needed",      "INTEGER", "NOT NULL DEFAULT 0"),
                ("Excess",      "INTEGER", "NOT NULL DEFAULT 0"),
                ("Target",      "INTEGER", "NOT NULL DEFAULT 0"),
                ("Desired",     "TEXT",    "NOT NULL DEFAULT 'Unassigned'"),
                ("Group",       "TEXT",    "NOT NULL DEFAULT ''"),
                ("PrintType",   "TEXT",    "NOT NULL DEFAULT 'Unknown'"),
                ("BuyStatus",   "TEXT",    "NOT NULL DEFAULT 'Unassigned'"),
                ("SellStatus",  "TEXT",    "NOT NULL DEFAULT 'Unassigned'"),
            };

            foreach (var (col, type, def) in collectionCols)
            {
                try
                {
#pragma warning disable EF1002
                    Database.ExecuteSqlRaw(
                        $"ALTER TABLE CollectionEntries ADD COLUMN {col} {type} {(string.IsNullOrEmpty(def) ? "" : def)}");
#pragma warning restore EF1002
                }
                catch { /* Column already exists — ignore */ }
            }
        }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Indexes for fast searching ───────────────────────────────────

            // PoolCards
            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.Name);

            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.SetCode);

            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.ColorIdentity);

            modelBuilder.Entity<PoolCard>()
                .HasIndex(c => c.Rarity);

            // TokenCards
            modelBuilder.Entity<TokenCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            modelBuilder.Entity<TokenCard>()
                .HasIndex(c => c.Name);

            // PlanarCards
            modelBuilder.Entity<PlanarCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // SchemeCards
            modelBuilder.Entity<SchemeCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // VanguardCards
            modelBuilder.Entity<VanguardCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // ArtSeriesCards
            modelBuilder.Entity<ArtSeriesCard>()
                .HasIndex(c => c.ScryfallId)
                .IsUnique();

            // ── AppSettings key is already the primary key ───────────────────
            modelBuilder.Entity<AppSetting>()
                .HasKey(s => s.Key);
        }
    }
}