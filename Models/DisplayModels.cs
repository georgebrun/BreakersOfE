using BreakersOfE.Services;
using System;
using System.IO;
using System.Windows.Media;

namespace BreakersOfE.Models
{
    // ── Collection display row ────────────────────────────────────────────────
    public class CollectionDisplayRow
    {
        public int CollectionEntryId { get; set; }
        public int PoolId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string ColorIdentity { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public int UsedCount { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public DateTime DateAdded { get; set; }
        public DateTime DateModified { get; set; }

        // ── Row index for alternating colors ─────────────────────────────────
        public int RowIndex { get; set; }

        // ── Computed ─────────────────────────────────────────────────────────
        public int AvailableCount =>
            Math.Max(0, Quantity + FoilQuantity - UsedCount);

        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) &&
            !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}" : string.Empty;

        public string RarityCode => Rarity?.ToLower() switch
        {
            "common" => "C",
            "uncommon" => "U",
            "rare" => "R",
            "mythic" => "M",
            "special" => "S",
            "bonus" => "B",
            _ => "?"
        };

        public string FavoriteGlyph => IsFavorite ? "★" : string.Empty;

        public string SetSymbolPath
        {
            get
            {
                string folder = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "SetSymbols");
                string path = Path.Combine(
                    folder, $"{SetCode.ToLower()}.png");
                return File.Exists(path) ? path : string.Empty;
            }
        }

        // ── Theme-aware colors ────────────────────────────────────────────────
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(
                ColorIdentity, TypeLine, IsFoil);

        public Brush RowBackgroundBrush =>
            CardColorService.GetBackground(IsFoil, RowIndex);

        public Brush CellBorderBrush =>
            CardColorService.GetCellBorderBrush();
    }

    // ── Dashboard stats ───────────────────────────────────────────────────────
    public class DashboardStats
    {
        public int PoolCount { get; set; }
        public int TokenCount { get; set; }
        public int PlanarCount { get; set; }
        public int SchemeCount { get; set; }
        public int VanguardCount { get; set; }
        public int ConspiracyCount { get; set; }
        public int ArtSeriesCount { get; set; }
        public int CollectionCount { get; set; }
        public int TradeBinderCount { get; set; }
        public int DeckCount { get; set; }
    }
}