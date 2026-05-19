using BreakersOfE.Data;
using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace BreakersOfE.Windows
{
    public partial class KeywordSearchWindow : Window
    {
        // ── State ─────────────────────────────────────────────────────────────
        private List<PoolCard> _pool = new();
        private List<CollectionDisplayRow> _collection = new();
        private readonly HashSet<string> _selectedKeywords = new(StringComparer.OrdinalIgnoreCase);
        private bool _suppressFilter = false;

        // ── Constructor ───────────────────────────────────────────────────────
        public KeywordSearchWindow()
        {
            InitializeComponent();
            LoadData();
            BuildKeywordTree();
            ApplyFilter();
        }

        // ── Data loading ──────────────────────────────────────────────────────
        private void LoadData()
        {
            using var db = new AppDbContext();
            _pool = db.PoolCards.AsNoTracking()
                .OrderBy(c => c.Name).ThenBy(c => c.SetCode)
                .ToList();

            using var cdb = new CollectionDbContext();
            var entries = cdb.CollectionEntries.AsNoTracking().ToList();
            if (entries.Count > 0)
            {
                var ids = entries.Select(e => e.PoolId).ToHashSet();
                var cards = _pool.Where(c => ids.Contains(c.PoolId))
                                 .ToDictionary(c => c.PoolId);
                _collection = entries
                    .Where(e => cards.ContainsKey(e.PoolId))
                    .Select(e =>
                    {
                        var pc = cards[e.PoolId];
                        return new CollectionDisplayRow
                        {
                            PoolId = pc.PoolId,
                            Name = pc.Name,
                            TypeLine = pc.TypeLine,
                            ManaCost = pc.ManaCost,
                            ManaValue = pc.ManaValue,
                            SetCode = pc.SetCode,
                            Rarity = pc.Rarity,
                            Power = pc.Power,
                            Toughness = pc.Toughness,
                            ColorIdentity = pc.ColorIdentity,
                            Colors = pc.Colors,
                            LegalitiesJson = pc.LegalitiesJson,
                            Keywords = pc.Keywords,
                            Quantity = e.Quantity,
                            FoilQuantity = e.FoilQuantity,
                            LocalImagePath = pc.LocalImagePath,
                            ImageNormalUrl = pc.ImageNormalUrl,
                            PriceUsd = pc.PriceUsd,
                            PriceUsdFoil = pc.PriceUsdFoil,
                        };
                    })
                    .OrderBy(r => r.Name)
                    .ToList();
            }
        }

        // ── Keyword tree builder ──────────────────────────────────────────────
        private void BuildKeywordTree()
        {
            // Gather all keywords actually present in pool
            var poolKeywords = MtgKeywordService.GetAllPoolKeywords(_pool)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            KeywordTree.Items.Clear();

            foreach (var (categoryName, keywords) in MtgKeywordService.ByCategory)
            {
                // Only show categories that have at least one keyword in the pool
                var visible = keywords
                    .Where(k => poolKeywords.Contains(k.Name) ||
                                MtgKeywordService.All.Any(m =>
                                    m.Name.Equals(k.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (visible.Count == 0) continue;

                var catItem = new TreeViewItem
                {
                    Header = $"▶ {categoryName}",
                    IsExpanded = false,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(2)
                };

                foreach (var kw in visible)
                {
                    bool inPool = poolKeywords.Contains(kw.Name);
                    var cb = new CheckBox
                    {
                        Content = kw.Name,
                        IsEnabled = inPool,
                        Opacity = inPool ? 1.0 : 0.4,
                        Margin = new Thickness(0, 1, 0, 1),
                        ToolTip = kw.Definition,
                        Tag = kw.Name
                    };
                    cb.Checked += Keyword_CheckChanged;
                    cb.Unchecked += Keyword_CheckChanged;

                    var kwItem = new TreeViewItem
                    {
                        Header = cb,
                        Padding = new Thickness(0)
                    };
                    catItem.Items.Add(kwItem);
                }

                KeywordTree.Items.Add(catItem);
            }
        }

        private void Keyword_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not string name) return;
            if (cb.IsChecked == true) _selectedKeywords.Add(name);
            else _selectedKeywords.Remove(name);
            ApplyFilter();
        }

        // ── Filter ────────────────────────────────────────────────────────────
        private void ApplyFilter()
        {
            if (_suppressFilter) return;

            // Guard against calls during InitializeComponent before controls exist
            if (RbCollection == null || RbAnd == null || ResultsGrid == null) return;

            bool useCollection = RbCollection.IsChecked == true;
            bool andLogic = RbAnd.IsChecked == true;

            // Selected color identity letters
            var selColors = new List<char>();
            if (BtnW.IsChecked == true) selColors.Add('W');
            if (BtnU.IsChecked == true) selColors.Add('U');
            if (BtnB.IsChecked == true) selColors.Add('B');
            if (BtnR.IsChecked == true) selColors.Add('R');
            if (BtnG.IsChecked == true) selColors.Add('G');
            if (BtnC.IsChecked == true) selColors.Add('C');

            bool atMost = RbColorAtMost.IsChecked == true;
            bool includes = RbColorIncludes.IsChecked == true;
            bool exact = RbColorExact.IsChecked == true;

            // Legality filters
            bool needStandard = ChkStandard.IsChecked == true;
            bool needPioneer = ChkPioneer.IsChecked == true;
            bool needModern = ChkModern.IsChecked == true;
            bool needLegacy = ChkLegacy.IsChecked == true;
            bool needVintage = ChkVintage.IsChecked == true;
            bool needCommander = ChkCommander.IsChecked == true;
            bool needPauper = ChkPauper.IsChecked == true;
            bool anyLegality = needStandard || needPioneer || needModern ||
                                 needLegacy || needVintage || needCommander || needPauper;

            // Keyword filter text (for filtering the tree display)
            string kwSearch = TxtKeywordSearch.Text.Trim();

            IEnumerable<PoolCard> filtered;

            if (useCollection)
            {
                // Filter collection then project back to PoolCard view
                // We match on the underlying pool card properties
                var collFiltered = _collection.AsEnumerable();
                filtered = FilterPoolCards(
                    _pool.Where(pc => _collection.Any(cr => cr.PoolId == pc.PoolId)),
                    andLogic, selColors, atMost, includes, exact,
                    anyLegality, needStandard, needPioneer, needModern,
                    needLegacy, needVintage, needCommander, needPauper);
            }
            else
            {
                filtered = FilterPoolCards(
                    _pool,
                    andLogic, selColors, atMost, includes, exact,
                    anyLegality, needStandard, needPioneer, needModern,
                    needLegacy, needVintage, needCommander, needPauper);
            }

            var results = filtered.ToList();
            for (int i = 0; i < results.Count; i++) results[i].RowIndex = i;

            ResultsGrid.ItemsSource = results;
            ResultCountText.Text = $"{results.Count:N0} card{(results.Count == 1 ? "" : "s")}";

            UpdateActionButtons();
        }

        private IEnumerable<PoolCard> FilterPoolCards(
            IEnumerable<PoolCard> source,
            bool andLogic,
            List<char> selColors,
            bool atMost, bool includes, bool exact,
            bool anyLegality,
            bool needStandard, bool needPioneer, bool needModern,
            bool needLegacy, bool needVintage, bool needCommander, bool needPauper)
        {
            return source.Where(card =>
            {
                // ── Keyword filter ───────────────────────────────────────────
                if (_selectedKeywords.Count > 0)
                {
                    var cardKws = MtgKeywordService.GetCardKeywords(
                        card.Keywords, card.OracleText)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    bool kwMatch = andLogic
                        ? _selectedKeywords.All(k => cardKws.Contains(k))
                        : _selectedKeywords.Any(k => cardKws.Contains(k));

                    if (!kwMatch) return false;
                }

                // ── Color identity filter ────────────────────────────────────
                if (selColors.Count > 0)
                {
                    var cardCI = card.ColorIdentity
                        .Where(ch => "WUBRG".Contains(char.ToUpper(ch)))
                        .Select(char.ToUpper)
                        .ToHashSet();

                    bool colorMatch;
                    if (atMost)
                        // Card's colors must ALL be within selected set
                        colorMatch = cardCI.All(c => selColors.Contains(c));
                    else if (includes)
                        // Card must contain ALL selected colors
                        colorMatch = selColors.All(c => cardCI.Contains(c));
                    else // exact
                        // Card's colors == selected colors exactly
                        colorMatch = cardCI.Count == selColors.Count &&
                                     selColors.All(c => cardCI.Contains(c));

                    // Handle colorless filter
                    if (selColors.Contains('C') && cardCI.Count == 0)
                        colorMatch = true;

                    if (!colorMatch) return false;
                }

                // ── Legality filter ──────────────────────────────────────────
                if (anyLegality)
                {
                    if (needStandard && !card.IsLegalStandard) return false;
                    if (needPioneer && !card.IsLegalPioneer) return false;
                    if (needModern && !card.IsLegalModern) return false;
                    if (needLegacy && !card.IsLegalLegacy) return false;
                    if (needVintage && !card.IsLegalVintage) return false;
                    if (needCommander &&
                        GetLegalityRaw(card, "commander") != "legal") return false;
                    if (needPauper &&
                        GetLegalityRaw(card, "pauper") != "legal") return false;
                }

                return true;
            });
        }

        private static string GetLegalityRaw(PoolCard card, string format)
        {
            if (string.IsNullOrWhiteSpace(card.LegalitiesJson)) return string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(card.LegalitiesJson);
                if (doc.RootElement.TryGetProperty(format, out var v))
                    return v.GetString()?.ToLower() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void Source_Changed(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void Logic_Changed(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void Color_Changed(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void Legality_Changed(object sender, RoutedEventArgs e)
            => ApplyFilter();

        private void TxtKeywordSearch_Changed(object sender, TextChangedEventArgs e)
        {
            // Filter the keyword tree to matching entries
            string q = TxtKeywordSearch.Text.Trim();
            foreach (TreeViewItem cat in KeywordTree.Items)
            {
                int visible = 0;
                foreach (TreeViewItem kwItem in cat.Items)
                {
                    if (kwItem.Header is CheckBox cb)
                    {
                        bool show = string.IsNullOrEmpty(q) ||
                                    cb.Tag?.ToString()?.Contains(q,
                                        StringComparison.OrdinalIgnoreCase) == true;
                        kwItem.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                        if (show) visible++;
                    }
                }
                cat.Visibility = visible > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (visible > 0 && !string.IsNullOrEmpty(q))
                    cat.IsExpanded = true;
            }
        }

        private void BtnClearKeywords_Click(object sender, RoutedEventArgs e)
        {
            _selectedKeywords.Clear();
            foreach (TreeViewItem cat in KeywordTree.Items)
                foreach (TreeViewItem kwItem in cat.Items)
                    if (kwItem.Header is CheckBox cb)
                        cb.IsChecked = false;
            ApplyFilter();
        }

        private void BtnResetAll_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilter = true;

            // Reset source
            RbPool.IsChecked = true;

            // Reset keywords
            _selectedKeywords.Clear();
            foreach (TreeViewItem cat in KeywordTree.Items)
            {
                cat.IsExpanded = false;
                cat.Visibility = Visibility.Visible;
                foreach (TreeViewItem kwItem in cat.Items)
                {
                    kwItem.Visibility = Visibility.Visible;
                    if (kwItem.Header is CheckBox cb) cb.IsChecked = false;
                }
            }
            TxtKeywordSearch.Text = string.Empty;

            // Reset logic
            RbAnd.IsChecked = true;

            // Reset colors
            BtnW.IsChecked = BtnU.IsChecked = BtnB.IsChecked =
            BtnR.IsChecked = BtnG.IsChecked = BtnC.IsChecked = false;
            RbColorAtMost.IsChecked = true;

            // Reset legality
            ChkStandard.IsChecked = ChkPioneer.IsChecked = ChkModern.IsChecked =
            ChkLegacy.IsChecked = ChkVintage.IsChecked = ChkCommander.IsChecked =
            ChkPauper.IsChecked = false;

            _suppressFilter = false;
            ApplyFilter();
        }

        private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateActionButtons();

        private void ResultsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PoolCard pc) return;
            var win = new CardImageWindow(null, pc.Name) { Owner = this };
            // Load image if available
            if (!string.IsNullOrEmpty(pc.LocalImagePath) &&
                System.IO.File.Exists(pc.LocalImagePath))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(pc.LocalImagePath, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                win = new CardImageWindow(bmp, pc.Name) { Owner = this };
            }
            win.ShowDialog();
        }

        private void UpdateActionButtons()
        {
            bool hasSelection = ResultsGrid.SelectedItem != null;
            bool useCollection = RbCollection.IsChecked == true;
            BtnAddToDeck.IsEnabled = hasSelection;
            BtnAddToCollection.IsEnabled = hasSelection && !useCollection;
            BtnAddToWantList.IsEnabled = hasSelection;
        }

        // ── Action buttons ────────────────────────────────────────────────────
        private void BtnAddToDeck_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PoolCard pc) return;
            // Signal parent window to add to active deck
            (Owner as MainWindow)?.AddPoolCardToActiveDeck(pc);
            MessageBox.Show($"'{pc.Name}' added to active deck.",
                "Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAddToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PoolCard pc) return;
            using var cdb = new CollectionDbContext();
            var existing = cdb.CollectionEntries
                .FirstOrDefault(c => c.PoolId == pc.PoolId);
            if (existing == null)
                cdb.CollectionEntries.Add(new CollectionEntry
                {
                    PoolId = pc.PoolId,
                    Quantity = 1,
                    Condition = "Near Mint",
                    Language = "English",
                    DateAdded = DateTime.Now,
                    DateModified = DateTime.Now
                });
            else
                existing.Quantity++;
            cdb.SaveChanges();
            MessageBox.Show($"'{pc.Name}' added to collection.",
                "Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAddToWantList_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not PoolCard pc) return;
            using var cdb = new CollectionDbContext();
            var existing = cdb.WantListEntries
                .FirstOrDefault(w => w.PoolId == pc.PoolId);
            if (existing == null)
                cdb.WantListEntries.Add(new WantListEntry
                {
                    PoolId = pc.PoolId,
                    Quantity = 1,
                    DateAdded = DateTime.Now
                });
            else
                existing.Quantity++;
            cdb.SaveChanges();
            MessageBox.Show($"'{pc.Name}' added to Want List.",
                "Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDictionary_Click(object sender, RoutedEventArgs e)
        {
            var win = new KeywordDictionaryWindow { Owner = this };
            win.ShowDialog();
        }
    }
}