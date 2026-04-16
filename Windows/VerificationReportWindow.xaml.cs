using BreakersOfE.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BreakersOfE.Windows
{
    public partial class VerificationReportWindow : Window
    {
        public VerificationReportWindow(ImportResult result)
        {
            InitializeComponent();
            BuildReport(result);
        }

        private void BuildReport(ImportResult result)
        {
            // ── Card tables ──────────────────────────────────────────────────
            AddSectionHeader("CARD TABLES");
            AddRow("Pool Cards", result.PoolCardsImported);
            AddRow("Token Cards", result.TokenCardsImported);
            AddRow("Planar Cards", result.PlanarCardsImported);
            AddRow("Scheme Cards", result.SchemeCardsImported);
            AddRow("Vanguard Cards", result.VanguardCardsImported);
            AddRow("Conspiracy Cards", result.ConspiracyCardsImported);
            AddRow("Art Series Cards", result.ArtSeriesCardsImported);
            AddRow("Skipped", result.SkippedCount);
            AddDivider();
            AddRow("Total Imported", result.TotalImported, bold: true);

            // ── Color breakdown ──────────────────────────────────────────────
            AddSectionHeader("BY COLOR  (Pool Cards)");
            AddRow("⬜  White", result.WhiteCount);
            AddRow("🟦  Blue", result.BlueCount);
            AddRow("⬛  Black", result.BlackCount);
            AddRow("🟥  Red", result.RedCount);
            AddRow("🟩  Green", result.GreenCount);
            AddRow("🌈  Multicolor", result.MulticolorCount);
            AddRow("◻   Colorless", result.ColorlessCount);
            AddRow("🟫  Land", result.LandCount);
            AddCheckRow("Color totals match", result.ColorCountMatchesTotal);

            // ── Rarity breakdown ─────────────────────────────────────────────
            AddSectionHeader("BY RARITY  (Pool Cards)");
            AddRow("C   Common", result.CommonCount);
            AddRow("U   Uncommon", result.UncommonCount);
            AddRow("R   Rare", result.RareCount);
            AddRow("M   Mythic", result.MythicCount);
            AddRow("?   Other", result.OtherRarityCount);
            AddCheckRow("Rarity totals match", result.RarityCountMatchesTotal);

            // ── Symbols ──────────────────────────────────────────────────────
            AddSectionHeader("SYMBOLS");
            AddRow("Mana Symbols", result.ManaSymbolsDownloaded);
            AddRow("Set Symbols", result.SetSymbolsDownloaded);

            // ── Basic verification ───────────────────────────────────────────
            AddSectionHeader("BASIC VERIFICATION");
            AddRow("Scryfall Total", result.ScryfallReportedTotal);
            AddRow("Database Total", result.DatabaseTotal);
            AddDivider();
            AddMatchRow(result.ScryfallReportedTotal == result.DatabaseTotal);

            // ── Deep verification ────────────────────────────────────────────
            AddSectionHeader("DEEP VERIFICATION");
            AddRow("Duplicate ScryfallIds", result.DuplicateScryfallIds);
            AddRow("Cards with no image URL", result.CardsWithNoImageUrl,
                note: "(will show placeholder image)");
            AddRow("Cards with empty name", result.CardsWithEmptyName);
            AddRow("Cards with empty set code", result.CardsWithEmptySetCode);
            AddCheckRow("Color count sanity", result.ColorCountMatchesTotal);
            AddCheckRow("Rarity count sanity", result.RarityCountMatchesTotal);
        }

        // ── Section header ────────────────────────────────────────────────────
        private void AddSectionHeader(string text)
        {
            ReportPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 14, 0, 4)
            });
        }

        // ── Data row ─────────────────────────────────────────────────────────
        private void AddRow(string label, int value,
            bool bold = false, string note = "")
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(90)
            });

            // Label (with optional note)
            var labelPanel = new StackPanel { Orientation = Orientation.Horizontal };
            labelPanel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 12
            });

            if (!string.IsNullOrEmpty(note))
            {
                labelPanel.Children.Add(new TextBlock
                {
                    Text = $"  {note}",
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(150, 150, 150)),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var valueBlock = new TextBlock
            {
                Text = $"{value:N0}",
                Foreground = Brushes.White,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 12,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(labelPanel, 0);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(labelPanel);
            grid.Children.Add(valueBlock);
            ReportPanel.Children.Add(grid);
        }

        // ── Check row (pass/fail) ─────────────────────────────────────────────
        private void AddCheckRow(string label, bool passed)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(90)
            });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 12
            };

            var valueBlock = new TextBlock
            {
                Text = passed ? "✅" : "⚠️",
                FontSize = 12,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
            ReportPanel.Children.Add(grid);
        }

        // ── Match row ─────────────────────────────────────────────────────────
        private void AddMatchRow(bool match)
        {
            ReportPanel.Children.Add(new TextBlock
            {
                Text = match
                    ? "✅  Counts match perfectly"
                    : "⚠️  Count mismatch — some cards may have been skipped",
                Foreground = match
                    ? new SolidColorBrush(Color.FromRgb(78, 201, 78))
                    : new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        // ── Divider ───────────────────────────────────────────────────────────
        private void AddDivider()
        {
            ReportPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                Margin = new Thickness(0, 6, 0, 6)
            });
        }

        private void AddSpacer()
        {
            ReportPanel.Children.Add(new Border
            {
                Height = 6
            });
        }

        // ── Close ─────────────────────────────────────────────────────────────
        private void CloseButton_Click(object sender, RoutedEventArgs e) =>
            Close();
    }
}
