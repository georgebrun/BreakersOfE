using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BreakersOfE.Models
{
    // ── Pool Collection ─────────────────────────────────────────────────────
    public class CollectionEntry
    {
        [Key]
        public int CollectionEntryId { get; set; }
        public int PoolId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";
    }

    // ── Token Collection ────────────────────────────────────────────────────
    public class TokenCollectionEntry
    {
        [Key]
        public int TokenCollectionEntryId { get; set; }
        public int TokenId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";
    }

    // ── Planar Collection ───────────────────────────────────────────────────
    public class PlanarCollectionEntry
    {
        [Key]
        public int PlanarCollectionEntryId { get; set; }
        public int PlanarId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";
    }

    // ── Scheme Collection ───────────────────────────────────────────────────
    public class SchemeCollectionEntry
    {
        [Key]
        public int SchemeCollectionEntryId { get; set; }
        public int SchemeId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";
    }

    // ── Vanguard Collection ─────────────────────────────────────────────────
    public class VanguardCollectionEntry
    {
        [Key]
        public int VanguardCollectionEntryId { get; set; }
        public int VanguardId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";
    }

    // ── Conspiracy Collection ───────────────────────────────────────────────
    public class ConspiracyCollectionEntry
    {
        [Key]
        public int ConspiracyCollectionEntryId { get; set; }
        public int ConspiracyId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
    }

    // ── Art Series Collection ───────────────────────────────────────────────
    public class ArtSeriesCollectionEntry
    {
        [Key]
        public int ArtSeriesCollectionEntryId { get; set; }
        public int ArtSeriesId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = "Unknown";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public decimal? SellAtValue { get; set; }
        public decimal? PriceHigh { get; set; }
        public decimal? MarketValue { get; set; }
        public decimal? PriceLow { get; set; }
        public int Needed { get; set; } = 0;
        public int Excess { get; set; } = 0;
        public int Target { get; set; } = 0;
        public string Desired { get; set; } = "Unassigned";
        public string CardGroup { get; set; } = string.Empty;
        public string PrintType { get; set; } = "Unknown";
        public string BuyStatus { get; set; } = "Unassigned";
        public string SellStatus { get; set; } = "Unassigned";
    }

    // ── Trade Binder ────────────────────────────────────────────────────────
    // ── Trade Binder — Have list (cards you own and want to trade away) ────────
    public class TradeBinderEntry
    {
        [Key]
        public int TradeBinderEntryId { get; set; }
        public int PoolId { get; set; }
        public int Quantity { get; set; } = 1;
        public bool IsFoil { get; set; } = false;
        public string Condition { get; set; } = "Near Mint";
        public decimal? AskingPrice { get; set; }  // your sell price
        public string Notes { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }

    // ── Want List — Want list (cards you are looking to acquire) ──────────────
    public class WantListEntry
    {
        [Key]
        public int WantListEntryId { get; set; }
        public int PoolId { get; set; }
        public int Quantity { get; set; } = 1;
        public bool IsFoil { get; set; } = false;
        public decimal? OfferPrice { get; set; }  // your buy price
        public string Notes { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
    }

    // ── App Settings ─────────────────────────────────────────────────────────
    public class AppSetting
    {
        [Key]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}