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
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
        public int UsedCount { get; set; } = 0;
    }

    // ── Token Collection ────────────────────────────────────────────────────
    public class TokenCollectionEntry
    {
        [Key]
        public int TokenCollectionEntryId { get; set; }
        public int TokenId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
    }

    // ── Planar Collection ───────────────────────────────────────────────────
    public class PlanarCollectionEntry
    {
        [Key]
        public int PlanarCollectionEntryId { get; set; }
        public int PlanarId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
    }

    // ── Scheme Collection ───────────────────────────────────────────────────
    public class SchemeCollectionEntry
    {
        [Key]
        public int SchemeCollectionEntryId { get; set; }
        public int SchemeId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
    }

    // ── Vanguard Collection ─────────────────────────────────────────────────
    public class VanguardCollectionEntry
    {
        [Key]
        public int VanguardCollectionEntryId { get; set; }
        public int VanguardId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
    }

    // ── Conspiracy Collection ───────────────────────────────────────────────
    public class ConspiracyCollectionEntry
    {
        [Key]
        public int ConspiracyCollectionEntryId { get; set; }
        public int ConspiracyId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = string.Empty;
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
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
    }

    // ── Trade Binder ────────────────────────────────────────────────────────
    public class TradeBinderEntry
    {
        [Key]
        public int TradeBinderEntryId { get; set; }
        public int PoolId { get; set; }
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.Now;
    }

    // ── App Settings ─────────────────────────────────────────────────────────
    public class AppSetting
    {
        [Key]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    
}