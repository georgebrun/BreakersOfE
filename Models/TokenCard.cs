using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Windows.Media;

namespace BreakersOfE.Models
{
    public class TokenCard
    {
        [Key]
        public int TokenId { get; set; }

        public string ScryfallId { get; set; } = string.Empty;
        public string OracleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public string Colors { get; set; } = string.Empty;
        public string ColorIdentity { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ImageSmallUrl { get; set; } = string.Empty;
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string Layout { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ReleasedAt { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        [NotMapped]
        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) && !string.IsNullOrWhiteSpace(Toughness)
                ? $"{Power}/{Toughness}"
                : string.Empty;

        [NotMapped]
        public string RarityCode => Rarity switch
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
                string path = Path.Combine(folder, $"{SetCode.ToLower()}.png");
                return File.Exists(path) ? path : string.Empty;
            }
        }

        [NotMapped]
        public Brush RowForegroundBrush
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ColorIdentity)) return Brushes.Silver;
                if (ColorIdentity.Length > 1) return Brushes.Gold;
                return ColorIdentity switch
                {
                    "W" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D2B48C")),
                    "U" => Brushes.DeepSkyBlue,
                    "B" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0D0D0")),
                    "R" => Brushes.Red,
                    "G" => Brushes.LimeGreen,
                    _ => Brushes.Gold
                };
            }
        }

        [NotMapped]
        public Brush RowBackgroundBrush =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
    }
}