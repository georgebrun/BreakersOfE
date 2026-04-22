using BreakersOfE.Services;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Windows.Media;

namespace BreakersOfE.Models
{
    public class PoolCard
    {
        [Key]
        public int PoolId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public string LoyaltyOrDefense { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string ColorIdentity { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string SetType { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public bool IsToken { get; set; }
        public bool IsMeld { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LegalitiesJson { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public string Keywords { get; set; } = string.Empty;

        // ── Pricing fields ───────────────────────────────────────────────────
        public decimal? PriceUsd { get; set; }
        public decimal? PriceUsdFoil { get; set; }
        public decimal? PriceUsdEtched { get; set; }
        public decimal? PriceEur { get; set; }
        public decimal? PriceEurFoil { get; set; }
        public decimal? PriceTix { get; set; }

        // Keep raw JSON as backup
        public string PricesJson { get; set; } = string.Empty;

        // ── Row index for alternating colors ─────────────────────────────────
        [NotMapped] public int RowIndex { get; set; }

        // ── Computed display ─────────────────────────────────────────────────
        [NotMapped]
        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) &&
            !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}" : string.Empty;

        [NotMapped]
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

        [NotMapped]
        public string FavoriteGlyph => IsFavorite ? "★" : string.Empty;

        [NotMapped]
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

        [System.Text.Json.Serialization.JsonIgnore]
        public string ColorDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Colors))
                    return "N";

                // Count distinct WUBRG colors
                var distinct = Colors
                    .Where(c => "WUBRG".Contains(c))
                    .Distinct()
                    .ToList();

                if (distinct.Count == 0) return "N";
                if (distinct.Count > 1) return "M";
                return distinct[0].ToString();
            }
        }

        // ── Price display ────────────────────────────────────────────────────
        [NotMapped]
        public string PriceUsdDisplay =>
            PriceUsd.HasValue ? $"${PriceUsd.Value:F2}" : "—";

        [NotMapped]
        public string PriceUsdFoilDisplay =>
            PriceUsdFoil.HasValue ? $"${PriceUsdFoil.Value:F2}" : "—";

        // ── Legality columns ─────────────────────────────────────────────────
        [NotMapped] public string LegalityStandard => GetLegality("standard");
        [NotMapped] public string LegalityPioneer => GetLegality("pioneer");
        [NotMapped] public string LegalityModern => GetLegality("modern");
        [NotMapped] public string LegalityLegacy => GetLegality("legacy");
        [NotMapped] public string LegalityVintage => GetLegality("vintage");
        [NotMapped] public string LegalityCommander => GetLegality("commander");
        [NotMapped] public string LegalityPauper => GetLegality("pauper");

        private string GetLegality(string format)
        {
            if (string.IsNullOrWhiteSpace(LegalitiesJson))
                return string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument
                    .Parse(LegalitiesJson);
                if (doc.RootElement.TryGetProperty(format, out var v))
                    return v.GetString()?.ToLower() switch
                    {
                        "legal" => "✅",
                        "restricted" => "🔵",
                        _ => "❌"
                    };
            }
            catch { }
            return string.Empty;
        }

        // ── Theme-aware colors ───────────────────────────────────────────────
        [NotMapped]
        public Brush RowForegroundBrush =>
            CardColorService.GetForeground(
                ColorIdentity, TypeLine, IsFoil);

        [NotMapped]
        public Brush RowBackgroundBrush =>
            CardColorService.GetBackground(IsFoil, RowIndex);

        [NotMapped]
        public Brush CellBorderBrush =>
            CardColorService.GetCellBorderBrush();
    }
}