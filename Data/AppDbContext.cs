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
        public DbSet<ConspiracyCard> ConspiracyCards { get; set; }
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
        public DbSet<Deck> Decks { get; set; }
        public DbSet<DeckCard> DeckCards { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Always store the database next to the executable
            string dbPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "breakersofe.db");

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
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

            // ConspiracyCards
            modelBuilder.Entity<ConspiracyCard>()
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