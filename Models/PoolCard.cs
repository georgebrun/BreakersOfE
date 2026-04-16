using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
        public string PricesJson { get; set; } = string.Empty;
        public string LegalitiesJson { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }
        public string Keywords { get; set; } = string.Empty;

        // ── Computed display helpers ────────────────────────────────────────

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
        public Brush RarityBrush => Rarity switch
        {
            "mythic" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6600")),
            "rare" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A58E4A")),
            "uncommon" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0"))
        };

        [NotMapped]
        public Brush RowForegroundBrush
        {
            get
            {
                bool isLight = IsFoil;

                if (!string.IsNullOrWhiteSpace(TypeLine) && TypeLine.Contains("Land"))
                    return isLight
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7A4A1F"))
                        : Brushes.Tan;

                if ((!string.IsNullOrWhiteSpace(TypeLine) && TypeLine.Contains("Artifact")) ||
                    string.IsNullOrWhiteSpace(ColorIdentity))
                    return isLight ? Brushes.DimGray : Brushes.Silver;

                // Multi-color
                if (ColorIdentity.Length > 1)
                    return isLight
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#996515"))
                        : Brushes.Gold;

                return ColorIdentity switch
                {
                    "W" => isLight
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B5E00"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D2B48C")),
                    "U" => isLight ? Brushes.DarkBlue : Brushes.DeepSkyBlue,
                    "B" => isLight ? Brushes.Black
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0D0D0")),
                    "R" => isLight ? Brushes.DarkRed : Brushes.Red,
                    "G" => isLight
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#006400"))
                        : Brushes.LimeGreen,
                    _ => isLight
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#996515"))
                        : Brushes.Gold
                };
            }
        }

        [NotMapped]
        public Brush RowBackgroundBrush
        {
            get
            {
                if (IsFoil)
                {
                    var gradient = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0.5, 0),
                        EndPoint = new System.Windows.Point(0.5, 1)
                    };
                    gradient.GradientStops.Add(new GradientStop(
                        (Color)ColorConverter.ConvertFromString("#B5B5B5"), 0.0));
                    gradient.GradientStops.Add(new GradientStop(
                        (Color)ColorConverter.ConvertFromString("#F2F2F2"), 0.5));
                    gradient.GradientStops.Add(new GradientStop(
                        (Color)ColorConverter.ConvertFromString("#B5B5B5"), 1.0));
                    return gradient;
                }

                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E1E"));
            }
        }

        [NotMapped]
        public string FavoriteGlyph => IsFavorite ? "★" : string.Empty;

        [NotMapped]
        public string SetSymbolPath
        {
            get
            {
                string folder = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "SetSymbols");
                string path = System.IO.Path.Combine(folder,
                    $"{SetCode.ToLower()}.png");
                return System.IO.File.Exists(path) ? path : string.Empty;
            }
        }
    }
}