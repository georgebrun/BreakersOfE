using BreakersOfE.Data;
using BreakersOfE.Models;
using BreakersOfE.Services;
using BreakersOfE.Windows;
using Microsoft.EntityFrameworkCore;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BreakersOfE
{
    public partial class MainWindow : Window
    {
        // ── State ────────────────────────────────────────────────────────────
        private string _currentMode = "PoolToCollection";
        private bool _legalityVisible = false;
        private bool _bottomLocked = false;
        private List<string> _topSelectedSetCodes = new();
        private List<string> _bottomSelectedSetCodes = new();
        private bool _bottomTableHasFocus = false;

        // ── In-memory card cache ──────────────────────────────────────────────────
        private List<PoolCard>? _poolCache = null;
        private List<TokenCard>? _tokenCache = null;
        private List<PlanarCard>? _planarCache = null;
        private List<SchemeCard>? _schemeCache = null;
        private List<VanguardCard>? _vanguardCache = null;
        private List<ArtSeriesCard>? _artSeriesCache = null;
        private List<ConspiracyCard>? _conspiracyCache = null;

        // ── Filter state ─────────────────────────────────────────────────────
        private FilterState _topFilter = new();
        private FilterState _bottomFilter = new();
        private string _searchText = string.Empty;
        private string _bottomSearch = string.Empty;
        private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
        private List<object> _searchMatches = new();
        private int _searchMatchIndex = -1;
        private string _lastSearchTerm = string.Empty;

        // ── Lock key prefix for AppSettings ──────────────────────────────────
        private const string LockKeyPrefix = "Lock_";
        private const string ColVisPrefix = "ColVis_";
        private const string ColOrderPrefix = "ColOrder_";
        private const string ColLayoutVersion = "ColLayoutVer";
        private const string CurrentColLayoutVersion = "2"; // bump to force resize
        private const string RecentDecksKey = "RecentDecks";
        private const int MaxRecentDecks = 8;

        // ── AppSettings helpers ───────────────────────────────────────────────
        private static string? GetSetting(string key)
        {
            try
            {
                using var db = new Data.AppDbContext();
                return db.AppSettings
                    .FirstOrDefault(s => s.Key == key)?.Value;
            }
            catch { return null; }
        }

        private static void SaveSetting(string key, string value)
        {
            try
            {
                using var db = new Data.AppDbContext();
                var s = db.AppSettings.FirstOrDefault(s => s.Key == key);
                if (s == null)
                    db.AppSettings.Add(new Models.AppSetting
                    { Key = key, Value = value });
                else
                    s.Value = value;
                db.SaveChanges();
            }
            catch { }
        }

        private static void DeleteSetting(string key)
        {
            try
            {
                using var db = new Data.AppDbContext();
                var s = db.AppSettings.FirstOrDefault(x => x.Key == key);
                if (s != null) { db.AppSettings.Remove(s); db.SaveChanges(); }
            }
            catch { }
        }

        private static List<string> GetAllSettingKeys()
        {
            try
            {
                using var db = new Data.AppDbContext();
                return db.AppSettings.Select(s => s.Key).ToList();
            }
            catch { return new List<string>(); }
        }

        // ── Asset folders ────────────────────────────────────────────────────
        private string SetSymbolsFolder =>
            Services.AppFolderService.SetSymbolsFolder;

        private string ManaSymbolsFolder =>
            Services.AppFolderService.ManaSymbolsFolder;

        private static string CardImagesFolder =>
            Services.AppFolderService.CardImagesFolder;

        private static string ImageUnavailablePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "Images", "image_unavailable.png");

        private static async Task DownloadAndCacheCardImageAsync(
            string cardName, string imageUrl, string? backImageUrl = null)
        {
            if (string.IsNullOrEmpty(imageUrl)) return;
            try
            {
                string safeName = string.Concat(
                    cardName.Split(Path.GetInvalidFileNameChars()));
                string path = Path.Combine(CardImagesFolder, $"{safeName}.jpg");
                string backPath = Path.Combine(CardImagesFolder, $"{safeName}_back.jpg");

                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);

                // Download front face
                if (!File.Exists(path))
                {
                    var bytes = await http.GetByteArrayAsync(imageUrl);
                    await File.WriteAllBytesAsync(path, bytes);
                }

                // Download back face if DFC
                if (!string.IsNullOrEmpty(backImageUrl) && !File.Exists(backPath))
                {
                    try
                    {
                        var backBytes = await http.GetByteArrayAsync(backImageUrl);
                        await File.WriteAllBytesAsync(backPath, backBytes);
                    }
                    catch { /* back face download failed — non-critical */ }
                }

                // Update LocalImagePath on all matching PoolCards in DB
                using var db = new Data.AppDbContext();
                var card = db.PoolCards.FirstOrDefault(p => p.Name == cardName);
                if (card != null)
                {
                    card.LocalImagePath = path;
                    if (!string.IsNullOrEmpty(backImageUrl) && File.Exists(backPath))
                        card.LocalImageBackPath = backPath;
                    db.SaveChanges();
                }
            }
            catch { /* Silent fail — image unavailable fallback used */ }
        }

        // ── Deck menu handlers ────────────────────────────────────────────────────
        private void MenuNewDeck_Click(object sender, RoutedEventArgs e)
            => BtnNewDeck_Click(sender, e);
        private void MenuOpenDeck_Click(object sender, RoutedEventArgs e)
            => BtnOpenDeck_Click(sender, e);
        private void MenuSaveDeck_Click(object sender, RoutedEventArgs e)
            => BtnSaveDeck_Click(sender, e);
        private void MenuSaveAllDecks_Click(object sender, RoutedEventArgs e)
            => BtnSaveAllDecks_Click(sender, e);
        private void MenuCloseDeck_Click(object sender, RoutedEventArgs e)
            => BtnCloseDeck_Click(sender, e);
        private void MenuCloseAllDecks_Click(object sender, RoutedEventArgs e)
        {
            var decksToClose = _openDecks.ToList();
            foreach (var deck in decksToClose)
                CloseDeck(deck);
        }

        private void BtnCloseAllDecks_Click(object sender, RoutedEventArgs e)
            => MenuCloseAllDecks_Click(sender, e);

        private void BtnCloseDeck_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck != null)
                CloseDeck(_activeDeck);
        }

        private void BtnDeckLegality_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            var win = new Windows.DeckStatisticsWindow(_activeDeck) { Owner = this };
            win.Show();
        }

        private void BtnAddToDeck_Click(object sender, RoutedEventArgs e)
            => AddFromTopSelectionToDeck(foil: false);

        private void BtnAdd4ToDeck_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck?.DeckType != DeckType.Standard) return;

            int added = 0;
            string lastError = string.Empty;
            for (int i = 0; i < 4; i++)
            {
                // Always suppress internal errors — we show one at the end
                bool success = AddFromTopSelectionToDeck(foil: false,
                    suppressError: true, out string error);
                if (success)
                    added++;
                else
                {
                    lastError = error;
                    break;
                }
            }

            if (added > 0)

                // Show one error if we stopped short
                if (!string.IsNullOrEmpty(lastError))
                    MessageBox.Show(lastError, "Cannot Add Card",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnAddFoilToDeck_Click(object sender, RoutedEventArgs e)
            => AddFromTopSelectionToDeck(foil: true);

        private void BtnRemoveFromDeck_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (BottomDataGrid.SelectedItem is DeckCard card && !card.IsFooter)
            {
                int qty = card.TotalQuantity;
                DeckService.RemoveCard(_activeDeck, card, true);
                RefreshActiveDeckGrid();
                UpdateDeckTabTitle(_activeDeck!);
                UpdateDeckSummary(_activeDeck);
                UpdateUsedCount(card.PoolId, -qty);
                AutoSaveDeck(_activeDeck!);
                if (_currentMode == "CollectionToDeck") LoadTopTable_CollectionForDeck();
            }
        }

        private void BtnDeckQtyIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card && !card.IsFooter)
            {
                card.Quantity++;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
                UpdateDeckSummary(_activeDeck);
                AutoSaveDeck(_activeDeck!);
                if (_currentMode == "CollectionToDeck") LoadTopTable_CollectionForDeck();
            }
        }

        private void BtnDeckQtyDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card && !card.IsFooter)
            {
                if (card.TotalQuantity <= 1)
                    DeckService.RemoveCard(_activeDeck, card, true);
                else if (card.Quantity > 0)
                    card.Quantity--;
                else if (card.FoilQuantity > 0)
                    card.FoilQuantity--;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
                UpdateDeckSummary(_activeDeck);
                AutoSaveDeck(_activeDeck!);
                if (_currentMode == "CollectionToDeck") LoadTopTable_CollectionForDeck();
            }
        }

        private void BtnAdvancedFilter_Click(object sender, RoutedEventArgs e)
            => OpenFilterWindow(top: !_bottomTableHasFocus);

        private void ChooserBtn_DeckGrid_PreviewMouseDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn) return;

            // For deck grids, walk up to find the DataGrid
            var header = FindParent<DataGridColumnHeader>(btn);
            var grid = header != null
                ? FindParent<DataGrid>(header)
                : FindParent<DataGrid>(btn);
            if (grid == null) return;

            try
            {
                var popup = new Windows.ColumnChooserPopup(grid, "Deck")
                { Owner = this };
                var screenPos = btn.PointToScreen(new Point(0, btn.ActualHeight));
                popup.Left = screenPos.X;
                popup.Top = screenPos.Y;
                popup.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Column chooser error:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChooserBtn_PreviewMouseDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn) return;

            bool isDeckTabMode = _currentMode == "PoolToDeck" ||
                                 _currentMode == "CollectionToDeck";

            DataGrid? grid;
            string tableKey;

            if (btn.Tag is string t && t == "Bottom")
            {
                // Bottom button — in deck tab modes show the active deck grid
                if (isDeckTabMode && _activeDeck != null)
                {
                    grid = GetActiveDeckGrid();
                    tableKey = "Deck";
                }
                else
                {
                    grid = BottomDataGrid;
                    tableKey = GetTableKey(BottomDataGrid);
                }
            }
            else
            {
                // Top button
                grid = TopDataGrid;
                tableKey = GetTableKey(TopDataGrid);
            }

            if (grid == null) return;

            try
            {
                var popup = new Windows.ColumnChooserPopup(grid, tableKey)
                { Owner = this };
                var screenPos = btn.PointToScreen(new Point(0, btn.ActualHeight));
                popup.Left = screenPos.X;
                popup.Top = screenPos.Y;
                popup.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Column chooser error:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnColumnChooser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            e.Handled = true;

            bool isDeckTabMode = _currentMode == "PoolToDeck" ||
                                 _currentMode == "CollectionToDeck";

            DataGrid? grid;
            string tableKey;

            if (isDeckTabMode && _activeDeck != null)
            {
                grid = GetActiveDeckGrid();
                tableKey = "Deck";
            }
            else if (_bottomTableHasFocus)
            {
                grid = BottomDataGrid;
                tableKey = GetTableKey(BottomDataGrid);
            }
            else
            {
                grid = TopDataGrid;
                tableKey = GetTableKey(TopDataGrid);
            }

            if (grid == null) return;

            try
            {
                var popup = new Windows.ColumnChooserPopup(grid, tableKey)
                { Owner = this };
                var screenPos = btn.PointToScreen(new Point(0, btn.ActualHeight));
                popup.Left = screenPos.X;
                popup.Top = screenPos.Y;
                popup.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Column chooser error:\n{ex.Message}",
                    "Debug", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddFromTopSelectionToDeck(bool foil)
            => AddFromTopSelectionToDeck(foil, suppressError: false, out _);

        private bool AddFromTopSelectionToDeck(bool foil,
            bool suppressError, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (_activeDeck == null)
            {
                if (!suppressError)
                    MessageBox.Show(
                        "No deck is open. Create or open a deck first.",
                        "No Deck Open", MessageBoxButton.OK,
                        MessageBoxImage.Information);
                return false;
            }

            DeckCard? deckCard = null;

            switch (TopDataGrid.SelectedItem)
            {
                case PoolCard pc:
                    // Auto-switch to foil if non-foil requested but unavailable
                    if (!foil && !pc.IsNonFoil && pc.IsFoil) foil = true;
                    if (foil && !pc.IsFoil) return false;
                    if (!foil && !pc.IsNonFoil) return false;
                    deckCard = DeckService.FromPoolCard(pc);
                    break;
                case CollectionDisplayRow cr:
                    // Auto-switch to foil if non-foil requested but unavailable
                    int nfAvail = Math.Max(0, cr.Quantity - cr.UsedCount);
                    int fAvail = cr.FoilQuantity;
                    if (!foil && nfAvail == 0 && fAvail > 0) foil = true;

                    // Block if no copies available (foil or non-foil)
                    if (_currentMode == "CollectionToDeck" && nfAvail <= 0 && fAvail <= 0)
                    {
                        errorMessage =
                            $"No available copies of '{cr.Name}' left in your collection.\n" +
                            $"All {cr.Quantity + cr.FoilQuantity} cop{(cr.Quantity + cr.FoilQuantity == 1 ? "y is" : "ies are")} already used in decks.";
                        if (!suppressError)
                            MessageBox.Show(errorMessage, "No Copies Available",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    deckCard = DeckService.FromCollectionRow(cr);

                    // Prompt if card already in deck
                    if (_currentMode == "CollectionToDeck" && _activeDeck != null)
                    {
                        var existing = _activeDeck.Cards.FirstOrDefault(c =>
                            c.PoolId == deckCard.PoolId &&
                            c.Category == DeckCardCategory.Mainboard);

                        if (existing != null)
                        {
                            var result = MessageBox.Show(
                                $"'{cr.Name}' is already in your deck " +
                                $"({existing.TotalQuantity} cop{(existing.TotalQuantity == 1 ? "y" : "ies")}).\n\n" +
                                $"Press 'Yes' to add another copy.\n" +
                                $"Press 'No' to cancel.",
                                "Card Already in Deck",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result != MessageBoxResult.Yes)
                            {
                                errorMessage = string.Empty;
                                RestoreFocus();
                                return false;
                            }
                        }
                    }

                    if (_currentMode == "CollectionToDeck")
                        UpdateUsedCount(cr.PoolId, 1);
                    break;
                default:
                    return false;
            }

            if (deckCard == null) return false;

            bool added = DeckService.AddCard(
                _activeDeck!, deckCard,
                DeckCardCategory.Mainboard,
                foil,
                out string error);

            if (!added)
            {
                errorMessage = error;
                if (!suppressError)
                    MessageBox.Show(error, "Cannot Add Card",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            RefreshActiveDeckGrid();
            UpdateDeckTabTitle(_activeDeck!);
            UpdateDeckSummary(_activeDeck);

            // In CollectionToDeck mode refresh top grid so Used/Available update immediately
            if (_currentMode == "CollectionToDeck")
            {
                AutoSaveDeck(_activeDeck!);
                LoadTopTable_CollectionForDeck();
                if (TopDataGrid.ItemsSource is List<CollectionDisplayRow> topRows &&
                    deckCard != null)
                {
                    var updated = topRows.FirstOrDefault(r => r.PoolId == deckCard.PoolId);
                    if (updated != null)
                    {
                        TopDataGrid.SelectedItem = updated;
                        TopDataGrid.ScrollIntoView(updated);
                        _ = HandleSelectionAsync(updated);
                    }
                }
                RestoreFocus();
            }

            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // FOOTER GRID UPDATERS
        // ════════════════════════════════════════════════════════════════════

        private void UpdateDeckSummary(Deck? deck)
        {
            if (deck != null) RefreshActiveDeckGrid();
        }

        private void UpdateBottomTableLabel()
        {
            // In DeckToCollection mode the bottom table is always Collection
            if (_currentMode == "DeckToCollection")
            {
                BottomTableLabel.Text = "Collection";
                BottomTableManaSymbols.Children.Clear();
                return;
            }

            BottomTableManaSymbols.Children.Clear();

            if (_activeDeck == null)
            {
                BottomTableLabel.Text = "Deck";
                return;
            }

            string type = _activeDeck.DeckType == DeckType.Commander
                ? "Commander Deck" : "Standard Deck";

            if (_activeDeck.DeckType == DeckType.Commander)
            {
                var commander = _activeDeck.Cards
                    .FirstOrDefault(c => c.IsCommander);

                if (commander != null)
                {
                    BottomTableLabel.Text =
                        $"{_activeDeck.Name}  │  {type}  │  {commander.Name}  ";

                    string identity = commander.ColorIdentity ?? string.Empty;
                    // Show C symbol for colorless if no WUBRG colors
                    var colorChars = identity
                        .Where(ch => "WUBRG".Contains(char.ToUpper(ch)))
                        .ToList();
                    var symbolsToShow = colorChars.Any()
                        ? colorChars.Select(c => c.ToString().ToUpper())
                        : new[] { "C" };

                    foreach (string sym in symbolsToShow)
                    {
                        string symPath = System.IO.Path.Combine(
                            ManaSymbolsFolder, $"{SanitizeSymbol(sym)}.png");
                        var bmp = System.IO.File.Exists(symPath)
                            ? LoadBitmap(symPath) : null;

                        if (bmp != null)
                        {
                            BottomTableManaSymbols.Children.Add(
                                new System.Windows.Controls.Image
                                {
                                    Source = bmp,
                                    Width = 16,
                                    Height = 16,
                                    Margin = new Thickness(1, 0, 1, 0),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    ToolTip = sym
                                });
                        }
                        else
                        {
                            BottomTableManaSymbols.Children.Add(new TextBlock
                            {
                                Text = $"[{sym}]",
                                Foreground = (System.Windows.Media.Brush)
                                    FindResource("PrimaryTextBrush"),
                                FontWeight = FontWeights.SemiBold,
                                Margin = new Thickness(1, 0, 1, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            });
                        }
                    }
                }
                else
                {
                    BottomTableLabel.Text = $"{_activeDeck.Name}  │  {type}";
                }
            }
            else
            {
                BottomTableLabel.Text = $"{_activeDeck.Name}  │  {type}";
            }
        }

        private void UpdateTopTableLabel()
        {
            TopTableManaSymbols.Children.Clear();

            if (_currentMode != "DeckToCollection")
                return;

            if (_activeDeck == null)
            {
                TopSearchLabel.Text = "Deck  (no deck open)";
                return;
            }

            string type = _activeDeck.DeckType == DeckType.Commander
                ? "Commander Deck" : "Standard Deck";

            var commander = _activeDeck.DeckType == DeckType.Commander
                ? _activeDeck.Cards.FirstOrDefault(c => c.IsCommander)
                : null;

            if (commander != null)
            {
                TopSearchLabel.Text =
                    $"{_activeDeck.Name}  │  {type}  │  {commander.Name}  ";

                string identity = commander.ColorIdentity ?? string.Empty;
                var colorChars = identity
                    .Where(ch => "WUBRG".Contains(char.ToUpper(ch)))
                    .ToList();
                var symbolsToShow = colorChars.Any()
                    ? colorChars.Select(c => c.ToString().ToUpper())
                    : new[] { "C" };

                foreach (string sym in symbolsToShow)
                {
                    string symPath = System.IO.Path.Combine(
                        ManaSymbolsFolder, $"{SanitizeSymbol(sym)}.png");
                    var bmp = System.IO.File.Exists(symPath)
                        ? LoadBitmap(symPath) : null;

                    if (bmp != null)
                        TopTableManaSymbols.Children.Add(
                            new System.Windows.Controls.Image
                            {
                                Source = bmp,
                                Width = 16,
                                Height = 16,
                                Margin = new Thickness(1, 0, 1, 0),
                                VerticalAlignment = VerticalAlignment.Center,
                                ToolTip = sym
                            });
                    else
                        TopTableManaSymbols.Children.Add(new TextBlock
                        {
                            Text = $"[{sym}]",
                            Foreground = (System.Windows.Media.Brush)
                                FindResource("PrimaryTextBrush"),
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(1, 0, 1, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                }
            }
            else
            {
                TopSearchLabel.Text = $"{_activeDeck.Name}  │  {type}";
            }
        }

        private void UpdateTopSummary(string label,
            int nonFoil = -1, int foil = -1,
            int total = -1, decimal value = -1)
        {
            if (_currentMode != "CollectionToDeck") return;
            if (nonFoil < 0) return;

            int usedTotal = 0;
            int availTotal = 0;
            if (TopDataGrid.ItemsSource is List<CollectionDisplayRow> crows)
            {
                usedTotal = crows.Sum(r => r.UsedCount);
                availTotal = crows.Sum(r => r.AvailableCount);
            }

            int nf = nonFoil, f = foil >= 0 ? foil : 0;
            int u = usedTotal, a = availTotal;
            decimal v = value >= 0 ? value : 0m;

            void populate()
            {
                TopSummaryGrid.Visibility = Visibility.Visible;
                SyncAndPopulateCollectionSummary(
                    TopSummaryGrid, TopDataGrid, nf, f, u, a, v,
                    0, 0, 0, 0, 0, 0, 0);
                WireSummaryColumnSync(TopDataGrid, TopSummaryGrid);
            }

            // First pass after layout
            Dispatcher.BeginInvoke(new Action(populate),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
            // Second pass to catch ActualWidth after render
            Dispatcher.BeginInvoke(new Action(populate),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // ════════════════════════════════════════════════════════════════════
        // DECK MANAGEMENT
        // ════════════════════════════════════════════════════════════════════
        private void BtnNewDeck_Click(object sender, RoutedEventArgs e)
        {
            // Pool→Deck allows only one deck at a time
            if (_currentMode == "PoolToDeck" && _openDecks.Any())
            {
                MessageBox.Show(
                    "Pool → Deck mode allows only one deck open at a time.\n" +
                    "Close the current deck before creating a new one.",
                    "One Deck at a Time",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new BreakersOfE.Windows.NewDeckDialog
            { Owner = this };

            if (dialog.ShowDialog() != true) return;

            var deck = DeckService.CreateNew(
                dialog.DeckName, dialog.DeckType);

            // Set file path immediately so auto-save never needs to prompt
            deck.FilePath = System.IO.Path.Combine(
                Services.AppFolderService.DecksFolder,
                Services.AppFolderService.SafeFileName(deck.Name) + ".deck");

            _openDecks.Add(deck);
            AddDeckTab(deck);
            AutoSaveDeck(deck); // Save immediately on creation
        }

        private void BtnOpenDeck_Click(object sender, RoutedEventArgs e)
        {
            // Pool→Deck allows only one deck at a time
            if (_currentMode == "PoolToDeck" && _openDecks.Any())
            {
                MessageBox.Show(
                    "Pool → Deck mode allows only one deck open at a time.\n" +
                    "Close the current deck before opening another.",
                    "One Deck at a Time",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open Deck",
                Filter = "Deck Files (*.deck)|*.deck|All Files (*.*)|*.*",
                InitialDirectory = Services.AppFolderService.DecksFolder
            };

            if (dlg.ShowDialog() != true) return;

            // Check if already open
            var existing = _openDecks.FirstOrDefault(
                d => d.FilePath == dlg.FileName);
            if (existing != null)
            {
                // Switch to that tab
                SelectDeckTab(existing);
                return;
            }

            try
            {
                var deck = DeckService.Load(dlg.FileName);
                if (deck == null) return;

                _openDecks.Add(deck);
                AddDeckTab(deck);
                AddToRecentDecks(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open deck:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Silent auto-save — only saves if file path already exists
        // Used after card add/remove so nested deck usage table stays current
        private void AutoSaveDeck(Deck deck)
        {
            if (deck == null) return;
            if (string.IsNullOrEmpty(deck.FilePath) ||
                !System.IO.File.Exists(deck.FilePath))
            {
                // No file yet — save automatically to the decks folder
                deck.FilePath = System.IO.Path.Combine(
                    Services.AppFolderService.DecksFolder,
                    Services.AppFolderService.SafeFileName(deck.Name) + ".deck");
            }

            try
            {
                DeckService.Save(deck);
                deck.IsModified = false;
                UpdateDeckTabTitle(deck);
            }
            catch { /* Silent — don't interrupt workflow */ }
        }

        private void BtnSaveDeck_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            SaveDeck(_activeDeck);
        }

        private void BtnSaveAllDecks_Click(object sender, RoutedEventArgs e)
        {
            foreach (var deck in _openDecks.Where(d => d.IsModified))
                SaveDeck(deck);
        }

        private void BtnDeckProperties_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            var win = new BreakersOfE.Windows.DeckPropertiesWindow(_activeDeck)
            { Owner = this };
            if (win.ShowDialog() == true)
            {
                UpdateDeckTabTitle(_activeDeck!);
            }
        }

        private void BtnDeckStats_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            var win = new Windows.DeckStatisticsWindow(_activeDeck) { Owner = this };
            win.Show();
        }

        // ── Save deck ─────────────────────────────────────────────────────────────
        private void SaveDeck(Deck deck)
        {
            if (string.IsNullOrEmpty(deck.FilePath) ||
                !System.IO.File.Exists(deck.FilePath))
            {
                // Save As
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Deck",
                    Filter = "Deck Files (*.deck)|*.deck",
                    InitialDirectory = Services.AppFolderService.DecksFolder,
                    FileName = Services.AppFolderService
                        .SafeFileName(deck.Name)
                };
                if (dlg.ShowDialog() != true) return;
                deck.FilePath = dlg.FileName;
            }

            try
            {
                DeckService.Save(deck);
                deck.IsModified = false;
                UpdateDeckTabTitle(deck);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save deck:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Close deck ────────────────────────────────────────────────────────────
        private void CloseDeck(Deck deck)
        {
            if (deck.IsModified)
            {
                var result = MessageBox.Show(
                    $"Save changes to '{deck.Name}' before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;
                if (result == MessageBoxResult.Yes)
                    SaveDeck(deck);
            }

            _openDecks.Remove(deck);
            RemoveDeckTab(deck);

            _activeDeck = _openDecks.FirstOrDefault();
            if (_activeDeck != null)
                SelectDeckTab(_activeDeck);

            if (_activeDeck == null)
            {
                BottomSummaryGrid.Columns.Clear();
                BottomSummaryGrid.ItemsSource = null;
                UnwireDeckEvents();
                RemoveDeckColumns(BottomDataGrid);
                LoadCurrentMode(); // Reload bottom table to show collection/pool
            }

            UpdateDeckSummary(_activeDeck!);
            UpdateDeckToolbarState();
        }

        // ── Add deck tab ──────────────────────────────────────────────────────────
        private void AddDeckTab(Deck deck)
        {
            var container = BuildDeckTabContent(deck);

            var tab = new TabItem
            {
                Header = deck.TabTitle,
                Tag = deck,
                Content = container,
                Style = (Style)FindResource("DeckTabStyle")
            };

            DeckTabControl.Items.Add(tab);
            DeckTabControl.SelectedItem = tab;
            // Only show tab strip in deck-building modes
            if (_currentMode == "PoolToDeck" || _currentMode == "CollectionToDeck")
                DeckTabControl.Visibility = Visibility.Visible;
            _activeDeck = deck;
            _wiredSummaries.Clear();

            // Show summary grid immediately — even for empty new decks
            BottomSummaryGrid.Visibility = Visibility.Visible;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshActiveDeckGrid();
                UpdateBottomTableLabel();
            }),
            System.Windows.Threading.DispatcherPriority.ContextIdle);

            UpdateDeckToolbarState();
        }

        // ── Build deck tab content ────────────────────────────────────────────────
        private Grid BuildDeckTabContent(Deck deck)
        {
            var mainGrid = BuildDeckDataGrid(deck, commander: false,
                showHeaders: true);
            mainGrid.Name = "MainDeckGrid";

            var container = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            container.RowDefinitions.Add(new RowDefinition
            { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(mainGrid, 0);
            container.Children.Add(mainGrid);
            return container;
        }

        // ── Build deck summary DataGrid (column-aligned totals) ───────────────────
        private DataGrid BuildDeckSummaryGrid(Deck deck)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.None,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                RowHeight = 26,
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = (System.Windows.Media.Brush)
                    FindResource("BorderBrush"),
                Background = (System.Windows.Media.Brush)
                    FindResource("SurfaceAltBrush"),
                Foreground = (System.Windows.Media.Brush)
                    FindResource("PrimaryTextBrush"),
                FontWeight = FontWeights.SemiBold,
                CanUserResizeColumns = false,
                CanUserReorderColumns = false,
                Tag = deck
            };
            ScrollViewer.SetVerticalScrollBarVisibility(
                grid, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(
                grid, ScrollBarVisibility.Hidden);
            return grid;
        }

        // ── Build deck DataGrid ───────────────────────────────────────────────────
        private DataGrid BuildDeckDataGrid(Deck deck,
            bool commander = false, bool showHeaders = true)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = showHeaders
                    ? DataGridHeadersVisibility.Column
                    : DataGridHeadersVisibility.None,
                Background = (System.Windows.Media.Brush)
                    FindResource("GridRowBrush"),
                Foreground = (System.Windows.Media.Brush)
                    FindResource("PrimaryTextBrush"),
                BorderThickness = commander
                    ? new Thickness(0, 0, 0, 1)
                    : new Thickness(0),
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                RowHeaderWidth = 0,
                RowHeight = 28,
                CanUserResizeRows = false,
                CanUserResizeColumns = true,
                CanUserReorderColumns = !commander,
                EnableRowVirtualization = !commander,
                ColumnHeaderStyle = (Style)FindResource("DataGridColumnHeaderStyle"),
                RowStyle = (Style)FindResource("DataGridRowStyle"),
                CellStyle = (Style)FindResource("DataGridCellStyle"),
                Tag = deck
            };

            // ScrollViewer attached properties must be set after construction
            if (commander)
            {
                ScrollViewer.SetVerticalScrollBarVisibility(
                    grid, ScrollBarVisibility.Disabled);
                ScrollViewer.SetHorizontalScrollBarVisibility(
                    grid, ScrollBarVisibility.Disabled);
            }

            // Suppress selection highlight
            grid.Resources.Add(
                SystemColors.HighlightBrushKey,
                System.Windows.Media.Brushes.Transparent);
            grid.Resources.Add(
                SystemColors.InactiveSelectionHighlightBrushKey,
                System.Windows.Media.Brushes.Transparent);

            // Context menu
            var ctx = new ContextMenu();
            var removeOne = new MenuItem { Header = "Remove 1 Copy" };
            removeOne.Click += DeckCtxRemove1_Click;
            var removeAll = new MenuItem { Header = "Remove All Copies" };
            removeAll.Click += DeckCtxRemoveAll_Click;
            var setCommander = new MenuItem { Header = "Set as Commander" };
            setCommander.Click += DeckCtxSetCommander_Click;
            setCommander.Visibility = deck.DeckType == DeckType.Commander
                ? Visibility.Visible : Visibility.Collapsed;
            var removeCommander = new MenuItem { Header = "Remove as Commander" };
            removeCommander.Click += DeckCtxRemoveCommander_Click;
            removeCommander.Visibility = deck.DeckType == DeckType.Commander
                ? Visibility.Visible : Visibility.Collapsed;
            var setSideboard = new MenuItem { Header = "Move to Sideboard" };
            setSideboard.Click += DeckCtxMoveSideboard_Click;
            var setMainboard = new MenuItem { Header = "Move to Mainboard" };
            setMainboard.Click += DeckCtxMoveMainboard_Click;
            ctx.Items.Add(removeOne);
            ctx.Items.Add(removeAll);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(setCommander);
            ctx.Items.Add(removeCommander);
            ctx.Items.Add(setSideboard);
            ctx.Items.Add(setMainboard);
            grid.ContextMenu = ctx;

            grid.SelectionChanged += DeckGrid_SelectionChanged;
            grid.PreviewKeyDown += DeckGrid_PreviewKeyDown;
            if (!commander)
                grid.CellEditEnding += DeckGrid_CellEditEnding;

            // ── Column chooser button (fixed, leftmost) ────────────────────
            var chooserCol = new DataGridTemplateColumn
            {
                Width = new DataGridLength(22),
                CanUserResize = false,
                CanUserReorder = false,
                CanUserSort = false
            };
            var chooserHeaderTemplate = new DataTemplate();
            var btnFactory = new FrameworkElementFactory(typeof(Button));
            btnFactory.SetValue(Button.ContentProperty, "\u2630");
            btnFactory.SetValue(Button.WidthProperty, 18.0);
            btnFactory.SetValue(Button.HeightProperty, 18.0);
            btnFactory.SetValue(Button.PaddingProperty, new Thickness(0));
            btnFactory.SetValue(Button.FontSizeProperty, 10.0);
            btnFactory.SetValue(Button.ToolTipProperty, "Show/Hide Columns");
            btnFactory.SetValue(Button.StyleProperty,
                (Style)FindResource("BaseButtonStyle"));
            btnFactory.AddHandler(
                Button.PreviewMouseLeftButtonDownEvent,
                new System.Windows.Input.MouseButtonEventHandler(
                    ChooserBtn_DeckGrid_PreviewMouseDown));
            chooserHeaderTemplate.VisualTree = btnFactory;
            chooserCol.HeaderTemplate = chooserHeaderTemplate;
            var emptyCell = new DataTemplate();
            emptyCell.VisualTree = new FrameworkElementFactory(typeof(TextBlock));
            chooserCol.CellTemplate = emptyCell;
            grid.Columns.Add(chooserCol);

            // ── Columns in requested order ─────────────────────────────────
            // ES
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "ES",
                Width = new DataGridLength(32),
                CanUserResize = false,
                CellTemplate = CreateSetSymbolTemplate()
            });

            // Name
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Binding = new System.Windows.Data.Binding("Name"),
                Width = new DataGridLength(200)
            });

            // Legality pills
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Legal",
                Width = new DataGridLength(100),
                CellTemplate = CreateLegalityPillTemplate()
            });

            // Qty (total, read-only)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Qty",
                Binding = new System.Windows.Data.Binding("TotalQuantity"),
                Width = new DataGridLength(45),
                IsReadOnly = true
            });

            // SB (sideboard indicator)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "SB",
                Binding = new System.Windows.Data.Binding("SideboardDisplay"),
                Width = new DataGridLength(35),
                IsReadOnly = true
            });

            // Edition (abbreviation)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Edition",
                Binding = new System.Windows.Data.Binding("SetCode"),
                Width = new DataGridLength(55)
            });

            // Color
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Color",
                Binding = new System.Windows.Data.Binding("ColorDisplay"),
                Width = new DataGridLength(50)
            });

            // Type
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Type",
                Binding = new System.Windows.Data.Binding("TypeLine"),
                Width = new DataGridLength(160)
            });

            // Rarity
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Rarity",
                Binding = new System.Windows.Data.Binding("RarityCode"),
                Width = new DataGridLength(50)
            });

            // Cost (mana symbols)
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Cost",
                Width = new DataGridLength(110),
                CellTemplate = CreateManaCostTemplate()
            });

            // Text
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Text",
                Binding = new System.Windows.Data.Binding("OracleText"),
                Width = new DataGridLength(200)
            });

            // Flavor
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Flavor",
                Binding = new System.Windows.Data.Binding("FlavorText"),
                Width = new DataGridLength(160)
            });

            // P/T
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "P/T",
                Binding = new System.Windows.Data.Binding("PowerToughness"),
                Width = new DataGridLength(55)
            });

            // Artist
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Artist",
                Binding = new System.Windows.Data.Binding("Artist"),
                Width = new DataGridLength(130)
            });

            // Edition Name
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Edition Name",
                Binding = new System.Windows.Data.Binding("SetName"),
                Width = new DataGridLength(160)
            });

            // Number (collector number)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Number",
                Binding = new System.Windows.Data.Binding("CollectorNumber"),
                Width = new DataGridLength(65)
            });

            // Power
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Power",
                Binding = new System.Windows.Data.Binding("Power"),
                Width = new DataGridLength(50)
            });

            // Toughness
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Toughness",
                Binding = new System.Windows.Data.Binding("Toughness"),
                Width = new DataGridLength(70)
            });

            // CMC
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "CMC",
                Binding = new System.Windows.Data.Binding("ManaValue"),
                Width = new DataGridLength(45)
            });

            // Row
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Row",
                Binding = new System.Windows.Data.Binding("RowIndex"),
                Width = new DataGridLength(45),
                IsReadOnly = true
            });

            // Load data
            RefreshDeckGrid(grid, deck);
            return grid;
        }

        private DataTemplate CreateSetSymbolTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(Image));
            var binding = new System.Windows.Data.Binding("SetSymbolPath")
            {
                Converter = (System.Windows.Data.IValueConverter)
                    Application.Current.Resources["ImageSourceConverter"]
            };
            factory.SetBinding(Image.SourceProperty, binding);
            factory.SetValue(Image.WidthProperty, 16.0);
            factory.SetValue(Image.HeightProperty, 16.0);
            factory.SetValue(Image.HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            factory.SetValue(Image.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            return new DataTemplate { VisualTree = factory };
        }

        private DataTemplate CreateManaCostTemplate()
        {
            var factory = new FrameworkElementFactory(
                typeof(ContentPresenter));
            var binding = new System.Windows.Data.Binding("ManaCost")
            {
                Converter = (System.Windows.Data.IValueConverter)
                    Application.Current.Resources["ManaCostConverter"]
            };
            factory.SetBinding(ContentPresenter.ContentProperty, binding);
            factory.SetValue(ContentPresenter.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            return new DataTemplate { VisualTree = factory };
        }

        private DataTemplate CreateLegalityPillTemplate()
        {
            var converter = (System.Windows.Data.IValueConverter)
                Application.Current.Resources["BoolToGreenRedConverter"];

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));

            foreach (var (letter, prop) in new[]
            {
                ("S", "IsLegalStandard"),
                ("M", "IsLegalModern"),
                ("P", "IsLegalPioneer"),
                ("L", "IsLegalLegacy"),
                ("V", "IsLegalVintage")
            })
            {
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                border.SetValue(Border.MarginProperty, new Thickness(1, 0, 1, 0));
                border.SetValue(Border.PaddingProperty, new Thickness(3, 1, 3, 1));
                border.SetBinding(Border.BackgroundProperty,
                    new System.Windows.Data.Binding(prop) { Converter = converter });

                var text = new FrameworkElementFactory(typeof(TextBlock));
                text.SetValue(TextBlock.TextProperty, letter);
                text.SetValue(TextBlock.FontSizeProperty, 10.0);
                text.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
                text.SetValue(TextBlock.ForegroundProperty,
                    System.Windows.Media.Brushes.White);

                border.AppendChild(text);
                panel.AppendChild(border);
            }

            return new DataTemplate { VisualTree = panel };
        }

        // ── Refresh deck grid ─────────────────────────────────────────────────────
        private static void RefreshDeckGrid(DataGrid grid, Deck deck)
        {
            var cards = deck.Cards
                .Where(c => !c.IsFooter)
                .OrderBy(c => c.IsCommander ? 0 : 1)
                .ThenBy(c => c.Category)
                .ThenBy(c => c.Name)
                .ToList();

            for (int i = 0; i < cards.Count; i++)
                cards[i].RowIndex = i + 1;

            grid.ItemsSource = cards;
        }

        private static void RefreshCommanderGrid(DataGrid grid, Deck deck)
        {
            var commanders = deck.Cards
                .Where(c => c.IsCommander)
                .OrderBy(c => c.Name)
                .ToList();
            grid.ItemsSource = commanders;
            grid.Visibility = commanders.Any()
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Update deck tab title ─────────────────────────────────────────────────
        private void UpdateDeckTabTitle(Deck deck)
        {
            foreach (TabItem tab in DeckTabControl.Items)
            {
                if (tab.Tag is Deck d && d == deck)
                {
                    tab.Header = deck.TabTitle;
                    break;
                }
            }
        }

        // ── Remove deck tab ───────────────────────────────────────────────────────
        private void RemoveDeckTab(Deck deck)
        {
            TabItem? toRemove = null;
            foreach (TabItem tab in DeckTabControl.Items)
                if (tab.Tag is Deck d && d == deck)
                { toRemove = tab; break; }

            if (toRemove != null)
                DeckTabControl.Items.Remove(toRemove);

            if (!_openDecks.Any())
            {
                DeckTabControl.Visibility = Visibility.Collapsed;
                _activeDeck = null;
            }
            else
            {
                DeckTabControl.SelectedIndex = 0;
            }
        }

        // ── Select deck tab ───────────────────────────────────────────────────────
        private void SelectDeckTab(Deck deck)
        {
            foreach (TabItem tab in DeckTabControl.Items)
            {
                if (tab.Tag is Deck d && d == deck)
                {
                    DeckTabControl.SelectedItem = tab;
                    _activeDeck = deck;
                    break;
                }
            }
        }

        // ── Tab selection changed ─────────────────────────────────────────────────
        private void DeckTabControl_SelectionChanged(object sender,
    SelectionChangedEventArgs e)
        {
            if (DeckTabControl.SelectedItem is TabItem tab)
            {
                _activeDeck = tab.Tag as Deck;
            }
            else
            {
                _activeDeck = null;
            }

            UpdateBottomTableLabel();
            UpdateDeckSummary(_activeDeck!);

            // In DeckToCollection mode refresh top grid when active deck changes
            if (_currentMode == "DeckToCollection")
            {
                LoadTopTable_DeckForCollection();
                TopSearchLabel.Text = _activeDeck != null
                    ? $"Deck: {_activeDeck.Name}"
                    : "Deck  (no deck open)";
            }

            UpdateToolbarState();
        }

        // ── Update deck toolbar state ─────────────────────────────────────────────
        private void UpdateDeckToolbarState()
        {
            UpdateToolbarState();
        }

        // ════════════════════════════════════════════════════════════════════
        // ADD CARDS TO DECK
        // ════════════════════════════════════════════════════════════════════
        private void AddCardToActiveDeck(PoolCard card,
            DeckCardCategory category = DeckCardCategory.Mainboard,
            bool foil = false)
        {
            if (_activeDeck == null)
            {
                MessageBox.Show("No deck is open. Create or open a deck first.",
                    "No Deck Open", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Determine actual foil status based on what's available
            bool addAsFoil;
            if (foil && card.IsFoil)
                addAsFoil = true;
            else if (!foil && card.IsNonFoil)
                addAsFoil = false;
            else if (card.IsFoil)
                addAsFoil = true;   // only foil available — use it
            else
                addAsFoil = false;  // only non-foil available — use it

            var deckCard = DeckService.FromPoolCard(card);
            deckCard.IsFoil = addAsFoil;

            // Check if card already in deck — one row per card regardless of foil
            var existing = _activeDeck.Cards.FirstOrDefault(c =>
                c.PoolId == card.PoolId &&
                c.Category == category);
            if (existing != null)
            {
                int currentFoil = existing.FoilQuantity;
                int currentNonFoil = existing.Quantity;
                string copies = addAsFoil
                    ? $"{currentFoil} foil"
                    : $"{currentNonFoil} non-foil";
                var result = MessageBox.Show(
                    $"'{card.Name}' is already in the deck ({copies} copies).\n\n" +
                    $"Press 'Yes' to add another copy\n" +
                    $"Press 'No' to skip",
                    "Card Already in Deck",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.No) return;
            }

            bool added = DeckService.AddCard(
                _activeDeck, deckCard, category, addAsFoil, out string error);

            if (!added)
            {
                MessageBox.Show(error, "Cannot Add Card",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Refresh the active deck grid
            RefreshActiveDeckGrid();
            UpdateDeckTabTitle(_activeDeck!);
            UpdateDeckSummary(_activeDeck);
            AutoSaveDeck(_activeDeck!);
        }

        private void AddCardToActiveDeck(CollectionDisplayRow row,
            DeckCardCategory category = DeckCardCategory.Mainboard)
        {
            if (_activeDeck == null)
            {
                MessageBox.Show("No deck is open. Create or open a deck first.",
                    "No Deck Open", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Auto-foil if only foil copies available (non-foil used up)
            int nonFoilAvail = Math.Max(0, row.Quantity - row.UsedCount);
            int foilAvail = row.FoilQuantity; // foil copies aren't tracked via UsedCount
            bool foil = nonFoilAvail == 0 && foilAvail > 0;

            // Block if no copies available (neither foil nor non-foil)
            if (_currentMode == "CollectionToDeck" && nonFoilAvail <= 0 && foilAvail <= 0)
            {
                MessageBox.Show(
                    $"You have no available copies of '{row.Name}' left in your collection.\n" +
                    $"All {row.Quantity + row.FoilQuantity} cop{(row.Quantity + row.FoilQuantity == 1 ? "y is" : "ies are")} already used in decks.",
                    "No Copies Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                RestoreFocus();
                return;
            }

            var deckCard = DeckService.FromCollectionRow(row);

            // In CollectionToDeck mode — prompt if card already in deck
            if (_currentMode == "CollectionToDeck" && _activeDeck != null)
            {
                var existing = _activeDeck.Cards.FirstOrDefault(c =>
                    c.PoolId == deckCard.PoolId &&
                    c.Category == category);

                if (existing != null)
                {
                    var result = MessageBox.Show(
                        $"'{row.Name}' is already in your deck " +
                        $"({existing.TotalQuantity} cop{(existing.TotalQuantity == 1 ? "y" : "ies")}).\n\n" +
                        $"Press 'Yes' to add another copy.\n" +
                        $"Press 'No' to cancel.",
                        "Card Already in Deck",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        RestoreFocus();
                        return;
                    }
                }
            }

            bool added = DeckService.AddCard(
                _activeDeck!, deckCard, category, foil, out string error);

            if (!added)
            {
                MessageBox.Show(error, "Cannot Add Card",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateUsedCount(row.PoolId, 1);
            RefreshActiveDeckGrid();
            UpdateDeckTabTitle(_activeDeck!);
            UpdateDeckSummary(_activeDeck);
            AutoSaveDeck(_activeDeck!);

            // Refresh both grids and restore bottom selection/image
            if (_currentMode == "CollectionToDeck")
            {
                LoadTopTable_CollectionForDeck();
                if (TopDataGrid.ItemsSource is List<CollectionDisplayRow> topRows)
                {
                    var updated = topRows.FirstOrDefault(r => r.PoolId == row.PoolId);
                    if (updated != null)
                    {
                        TopDataGrid.SelectedItem = updated;
                        TopDataGrid.ScrollIntoView(updated);
                        _ = HandleSelectionAsync(updated);
                    }
                }
                RestoreFocus();
            }
        }

        private bool _deckEventsWired = false;

        private void WireDeckEvents()
        {
            if (_deckEventsWired) return;
            BottomDataGrid.SelectionChanged += DeckGrid_SelectionChanged;
            BottomDataGrid.PreviewKeyDown += DeckGrid_PreviewKeyDown;
            BottomDataGrid.CellEditEnding += DeckGrid_CellEditEnding;

            // Swap context menu to deck version
            var ctx = new ContextMenu();
            var removeOne = new MenuItem { Header = "Remove 1 Copy" };
            removeOne.Click += DeckCtxRemove1_Click;
            var removeAll = new MenuItem { Header = "Remove All Copies" };
            removeAll.Click += DeckCtxRemoveAll_Click;
            var setSideboard = new MenuItem { Header = "Move to Sideboard" };
            setSideboard.Click += DeckCtxMoveSideboard_Click;
            var setMainboard = new MenuItem { Header = "Move to Mainboard" };
            setMainboard.Click += DeckCtxMoveMainboard_Click;
            var setCommander = new MenuItem { Header = "⭐ Set as Commander" };
            setCommander.Click += DeckCtxSetCommander_Click;
            var removeCommander = new MenuItem { Header = "Remove as Commander" };
            removeCommander.Click += DeckCtxRemoveCommander_Click;
            var toggleType = new MenuItem();
            toggleType.Click += DeckCtxToggleDeckType_Click;
            ctx.Items.Add(removeOne);
            ctx.Items.Add(removeAll);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(setSideboard);
            ctx.Items.Add(setMainboard);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(setCommander);
            ctx.Items.Add(removeCommander);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(toggleType);

            // Only show commander options for Commander decks
            ctx.Opened += (s, e) =>
            {
                bool isCommander = _activeDeck?.DeckType == DeckType.Commander;
                setCommander.Visibility = isCommander
                    ? Visibility.Visible : Visibility.Collapsed;
                removeCommander.Visibility = isCommander
                    ? Visibility.Visible : Visibility.Collapsed;
                if (isCommander)
                {
                    var sel = BottomDataGrid.SelectedItem as Models.DeckCard;
                    setCommander.IsEnabled = sel != null && !sel.IsCommander && !sel.IsFooter;
                    removeCommander.IsEnabled = sel != null && sel.IsCommander;
                }
                // Update toggle label based on current type
                toggleType.Header = isCommander
                    ? "🔄 Switch to Standard Deck"
                    : "🔄 Switch to Commander Deck";
            };
            BottomDataGrid.ContextMenu = ctx;

            _deckEventsWired = true;
        }

        private void RestoreCollectionContextMenu()
        {
            var ctx = new ContextMenu();

            if (_currentMode == "CollectionToTradeBinder")
            {
                var rem = new MenuItem { Header = "Remove from Trade Binder" };
                rem.Click += (s, e) => {
                    if (BottomDataGrid.SelectedItem is CollectionDisplayRow r)
                        RemoveFromTradeBinderRow(r);
                };
                ctx.Items.Add(rem);
            }
            else if (_currentMode == "PoolToWantList")
            {
                var rem = new MenuItem { Header = "Remove from Want List" };
                rem.Click += (s, e) => {
                    if (BottomDataGrid.SelectedItem is CollectionDisplayRow r)
                        RemoveFromWantListRow(r);
                };
                ctx.Items.Add(rem);
            }
            else
            {
                var r1nf = new MenuItem { Header = "Remove 1 Non-Foil" };
                r1nf.Click += CtxRemove1NonFoil_Click;
                var r1f = new MenuItem { Header = "Remove 1 Foil" };
                r1f.Click += CtxRemove1Foil_Click;
                var rAll = new MenuItem { Header = "Remove All Copies" };
                rAll.Click += CtxRemoveAll_Click;
                var usage = new MenuItem { Header = "Show Deck Usage" };
                usage.Click += CtxShowDeckUsage_Click;
                ctx.Items.Add(r1nf);
                ctx.Items.Add(r1f);
                ctx.Items.Add(new Separator());
                ctx.Items.Add(rAll);
                ctx.Items.Add(new Separator());
                ctx.Items.Add(usage);
            }

            BottomDataGrid.ContextMenu = ctx;
        }

        private void UnwireDeckEvents()
        {
            if (!_deckEventsWired) return;
            BottomDataGrid.SelectionChanged -= DeckGrid_SelectionChanged;
            BottomDataGrid.PreviewKeyDown -= DeckGrid_PreviewKeyDown;
            BottomDataGrid.CellEditEnding -= DeckGrid_CellEditEnding;
            RestoreCollectionContextMenu();
            _deckEventsWired = false;
        }

        private void LoadBottomTable_ActiveDeck()
        {
            WireDeckEvents();
            BottomDataGrid.IsReadOnly = false;
            if (_activeDeck == null)
            {
                BottomDataGrid.ItemsSource = null;
                BottomSummaryGrid.ItemsSource = null;
                return;
            }
            EnsureDeckColumns(BottomDataGrid);
            RefreshDeckGrid(BottomDataGrid, _activeDeck);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _wiredSummaries.Remove((BottomDataGrid, BottomSummaryGrid));
                SyncAndPopulateDeckSummary(BottomSummaryGrid, BottomDataGrid, _activeDeck);
                WireSummaryColumnSync(BottomDataGrid, BottomSummaryGrid);
                RestoreColumnLayout(BottomDataGrid, "Deck");
                AutoSizeColumnsToHeader(BottomDataGrid, "Deck");
                WireColumnLayoutSave(BottomDataGrid, "Deck");
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void RefreshActiveDeckGrid()
        {
            if (_activeDeck == null) return;

            // In DeckToCollection mode, deck goes in TOP table not bottom
            if (_currentMode == "DeckToCollection")
            {
                LoadTopTable_DeckForCollection();
                return;
            }

            // Deck data always lives in BottomDataGrid
            EnsureDeckColumns(BottomDataGrid);
            RefreshDeckGrid(BottomDataGrid, _activeDeck);

            var deck = _activeDeck;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _wiredSummaries.Remove((BottomDataGrid, BottomSummaryGrid));
                SyncAndPopulateDeckSummary(BottomSummaryGrid, BottomDataGrid, deck);
                WireSummaryColumnSync(BottomDataGrid, BottomSummaryGrid);
                RestoreColumnLayout(BottomDataGrid, "Deck");
                AutoSizeColumnsToHeader(BottomDataGrid, "Deck");
                WireColumnLayoutSave(BottomDataGrid, "Deck");
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);

            UpdateBottomTableLabel();
        }

        // ── Sync summary grid columns with main grid then populate ───────────────
        // ── Sync deck summary grid columns then populate ─────────────────────────
        private static void SyncAndPopulateDeckSummary(
            DataGrid sumGrid, DataGrid? srcGrid, Deck deck)
        {
            if (srcGrid == null) return;

            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty,
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xD6, 0xE8, 0xD6))));
            rowStyle.Setters.Add(new Setter(DataGridRow.ForegroundProperty,
                System.Windows.Media.Brushes.Black));
            rowStyle.Setters.Add(new Setter(DataGridRow.FontWeightProperty,
                FontWeights.SemiBold));
            sumGrid.RowStyle = rowStyle;


            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty,
                (System.Windows.Media.Brush)Application.Current.FindResource("BorderBrush")));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty,
                new Thickness(0, 0, 1, 1)));
            cellStyle.Setters.Add(new Setter(DataGridCell.PaddingProperty,
                new Thickness(4, 2, 4, 2)));
            sumGrid.CellStyle = cellStyle;

            sumGrid.Columns.Clear();

            var bindings = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                { "Non-Foil", "Quantity" },
                { "Foil",     "FoilQuantity" },
                { "Total",    "TotalQuantity" },
                { "SB",       "SideboardDisplay" },
                { "Value",    "PriceUsdDisplay" },
                { "USD",      "PriceUsdDisplay" }
            };

            // Sort by DisplayIndex so summary matches user-reordered column order
            foreach (var col in srcGrid.Columns.OrderBy(c => c.DisplayIndex))
            {
                string hdr = col.Header?.ToString() ?? string.Empty;
                bool isNumeric = bindings.ContainsKey(hdr);
                var sc = new DataGridTextColumn
                {
                    Width = col.Width,
                    IsReadOnly = true,
                    ElementStyle = new Style(typeof(TextBlock))
                    {
                        Setters = { new Setter(
                            TextBlock.TextAlignmentProperty,
                            isNumeric ? TextAlignment.Right : TextAlignment.Left) }
                    }
                };
                if (bindings.TryGetValue(hdr, out var prop))
                    sc.Binding = new System.Windows.Data.Binding(prop);
                sumGrid.Columns.Add(sc);
            }

            sumGrid.ItemsSource = new[]
            {
                new Models.DeckCard
                {
                    IsFooter     = true,
                    Quantity     = deck.NonFoilCount,
                    FoilQuantity = deck.FoilCount,
                    PriceUsd     = deck.TotalValue
                    // TotalQuantity computed as Quantity + FoilQuantity
                    // ValueDisplay uses PriceUsd as the total value display
                }
            };
        }


        // ── Update used count ─────────────────────────────────────────────────────
        private void UpdateUsedCount(int poolId, int delta)
        {
            if (poolId <= 0) return;
            using var db = new Data.CollectionDbContext();
            var entry = db.CollectionEntries
                .FirstOrDefault(c => c.PoolId == poolId);
            if (entry == null) return;

            entry.UsedCount = Math.Max(0, entry.UsedCount + delta);
            db.SaveChanges();
        }

        private void SetUsedCount(int poolId, int count)
        {
            if (poolId <= 0) return;
            using var db = new Data.CollectionDbContext();
            var entry = db.CollectionEntries
                .FirstOrDefault(c => c.PoolId == poolId);
            if (entry == null) return;

            entry.UsedCount = Math.Max(0, count);
            db.SaveChanges();
        }

        // ════════════════════════════════════════════════════════════════════
        // DECK CONTEXT MENU HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void DeckGrid_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid &&
                grid.SelectedItem is DeckCard card && !card.IsFooter)
                _ = HandleSelectionAsync(card);
        }

        private void DeckCtxRemove1_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (BottomDataGrid.SelectedItem is DeckCard card && !card.IsFooter)
            {
                DeckService.RemoveCard(_activeDeck, card, false);
                RefreshActiveDeckGrid();
                UpdateDeckTabTitle(_activeDeck!);
                UpdateDeckSummary(_activeDeck);
                UpdateUsedCount(card.PoolId, -1);
                AutoSaveDeck(_activeDeck!);
                if (_currentMode == "CollectionToDeck") LoadTopTable_CollectionForDeck();
            }
        }

        private void DeckCtxRemoveAll_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (BottomDataGrid.SelectedItem is DeckCard card && !card.IsFooter)
            {
                int qty = card.TotalQuantity;
                DeckService.RemoveCard(_activeDeck, card, true);
                RefreshActiveDeckGrid();
                UpdateDeckTabTitle(_activeDeck!);
                UpdateDeckSummary(_activeDeck);
                UpdateUsedCount(card.PoolId, -qty);
                AutoSaveDeck(_activeDeck!);
                if (_currentMode == "CollectionToDeck") LoadTopTable_CollectionForDeck();
            }
        }

        private void DeckCtxToggleDeckType_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;

            bool isCurrentlyCommander = _activeDeck.DeckType == DeckType.Commander;

            if (isCurrentlyCommander)
            {
                // Switch to Standard — warn if over 4 copies of any non-land card
                var violations = _activeDeck.Cards
                    .Where(c => !c.TypeLine.Contains("Land", StringComparison.OrdinalIgnoreCase)
                        && c.TotalQuantity > 4)
                    .Select(c => $"{c.Name} ({c.TotalQuantity} copies)")
                    .ToList();

                string msg = "Switch this deck to Standard format?";
                if (violations.Any())
                    msg += $"\n\n⚠ These cards exceed the 4-copy limit:\n  • " +
                           string.Join("\n  • ", violations);

                if (MessageBox.Show(msg, "Switch to Standard",
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes) return;

                _activeDeck.DeckType = DeckType.Standard;
                // Clear commander flags
                foreach (var c in _activeDeck.Cards)
                    if (c.IsCommander) { c.IsCommander = false; c.Category = DeckCardCategory.Mainboard; }
            }
            else
            {
                // Switch to Commander — validate 100 cards, 1 copy each
                int total = _activeDeck.Cards.Sum(c => c.TotalQuantity);
                var overOneCopy = _activeDeck.Cards
                    .Where(c => !c.TypeLine.Contains("Land", StringComparison.OrdinalIgnoreCase)
                        && c.TotalQuantity > 1)
                    .Select(c => $"{c.Name} ({c.TotalQuantity} copies)")
                    .ToList();

                string msg = $"Switch this deck to Commander format?\n\nCurrent card count: {total} (Commander requires 100)";
                if (overOneCopy.Any())
                    msg += $"\n\n⚠ These non-land cards have more than 1 copy:\n  • " +
                           string.Join("\n  • ", overOneCopy);

                if (MessageBox.Show(msg, "Switch to Commander",
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes) return;

                _activeDeck.DeckType = DeckType.Commander;
            }

            _activeDeck.IsModified = true;
            RefreshActiveDeckGrid();
            UpdateBottomTableLabel();
            AutoSaveDeck(_activeDeck);
        }

        private void DeckCtxSetCommander_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck?.DeckType != DeckType.Commander) return;
            if (BottomDataGrid.SelectedItem is not DeckCard card || card.IsFooter) return;

            // Check for existing commander
            var existing = _activeDeck.Cards
                .FirstOrDefault(c => c.IsCommander && c != card);

            if (existing != null)
            {
                var result = MessageBox.Show(
                    $"'{existing.Name}' is currently the commander.\n\n" +
                    $"Remove '{existing.Name}' as commander and set " +
                    $"'{card.Name}' instead?",
                    "Replace Commander",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                existing.IsCommander = false;
                existing.Category = DeckCardCategory.Mainboard;
            }

            card.IsCommander = true;
            card.Category = DeckCardCategory.Commander;
            _activeDeck.IsModified = true;
            RefreshActiveDeckGrid();
            UpdateBottomTableLabel();
            AutoSaveDeck(_activeDeck);
        }

        private void DeckCtxRemoveCommander_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck?.DeckType != DeckType.Commander) return;
            if (BottomDataGrid.SelectedItem is DeckCard card && card.IsCommander)
            {
                card.Category = DeckCardCategory.Mainboard;
                card.IsCommander = false;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
            }
        }

        private void DeckCtxMoveSideboard_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card && !card.IsFooter)
            {
                card.Category = DeckCardCategory.Sideboard;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
            }
        }

        private void DeckCtxMoveMainboard_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card && !card.IsFooter)
            {
                card.Category = DeckCardCategory.Mainboard;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
            }
        }

        private void DeckGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_activeDeck == null) return;
            if (e.Key != Key.Delete) return;
            if (sender is not DataGrid grid) return;
            if (grid.SelectedItem is not DeckCard card) return;

            var result = MessageBox.Show(
                $"Remove all copies of '{card.Name}' from the deck?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                int qty = card.TotalQuantity;
                DeckService.RemoveCard(_activeDeck, card, true);
                RefreshActiveDeckGrid();
                UpdateDeckTabTitle(_activeDeck!);
                UpdateDeckSummary(_activeDeck);
                UpdateUsedCount(card.PoolId, -qty);
                AutoSaveDeck(_activeDeck!);
                if (_currentMode == "CollectionToDeck")
                    LoadTopTable_CollectionForDeck();
            }

            e.Handled = true;
        }

        private void DeckGrid_CellEditEnding(object? sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (_activeDeck == null) return;
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not DeckCard card) return;

            string? colHeader = e.Column.Header?.ToString();
            if (colHeader != "Non-Foil" && colHeader != "Foil") return;

            if (e.EditingElement is TextBox tb &&
                int.TryParse(tb.Text, out int qty) && qty >= 0)
            {
                bool isFoilCol = colHeader == "Foil";

                // Calculate what total would be after this edit
                int newTotal = isFoilCol
                    ? card.Quantity + qty
                    : qty + card.FoilQuantity;

                if (newTotal <= 0)
                {
                    DeckService.RemoveCard(_activeDeck, card, true);
                    Dispatcher.BeginInvoke(new Action(() =>
                        RefreshActiveDeckGrid()),
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                else
                {
                    string? ruleError = ValidateDeckCardQty(
                        _activeDeck, card, newTotal);

                    if (ruleError != null)
                    {
                        MessageBox.Show(ruleError, "Deck Rule Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        tb.Text = (isFoilCol
                            ? card.FoilQuantity
                            : card.Quantity).ToString();
                        e.Cancel = true;
                        return;
                    }

                    if (isFoilCol) card.FoilQuantity = qty;
                    else card.Quantity = qty;
                }

                _activeDeck.IsModified = true;
                UpdateDeckTabTitle(_activeDeck!);
                UpdateDeckSummary(_activeDeck);
            }
        }

        private void DeckQtyTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                UpdateDeckCardQty(tb);
                e.Handled = true;
            }
        }

        private void DeckQtyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                UpdateDeckCardQty(tb);
        }

        private void UpdateDeckCardQty(TextBox tb)
        {
            if (_activeDeck == null) return;
            if (tb.DataContext is not DeckCard card) return;
            if (!int.TryParse(tb.Text, out int qty) || qty < 0) return;

            if (qty == 0)
            {
                DeckService.RemoveCard(_activeDeck, card, true);
            }
            else
            {
                string? error = ValidateDeckCardQty(_activeDeck, card, qty);
                if (error != null)
                {
                    MessageBox.Show(error, "Deck Rule Violation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    tb.Text = card.Quantity.ToString();
                    return;
                }

                card.Quantity = qty;
                _activeDeck.IsModified = true;
            }

            RefreshActiveDeckGrid();
            UpdateDeckTabTitle(_activeDeck!);
            UpdateDeckSummary(_activeDeck);
        }

        // ── Shared qty validation ─────────────────────────────────────────────
        private static string? ValidateDeckCardQty(Deck deck, DeckCard card, int qty)
        {
            // Basic lands and "any number" cards are always unrestricted
            if (card.IsBasicLand || card.IsAnyNumber) return null;

            if (deck.DeckType == DeckType.Commander && qty > 1)
                return "Commander decks allow only 1 copy of each non-basic card.";

            if (deck.DeckType == DeckType.Standard && qty > 4)
                return "Standard decks allow max 4 copies of each non-basic card.";

            return null;
        }

        private DataGrid? GetActiveDeckGrid()
        {
            // Deck data now always lives in BottomDataGrid
            bool isDeckMode = _currentMode == "PoolToDeck" ||
                              _currentMode == "CollectionToDeck";
            if (isDeckMode && _activeDeck != null)
                return BottomDataGrid;
            return null;
        }


        // ── Context menu deck usage ───────────────────────────────────────────────
        private void CtxShowDeckUsage_Click(object sender, RoutedEventArgs e)
        {
            var grid = _bottomTableHasFocus ? BottomDataGrid : TopDataGrid;
            if (grid.SelectedItem is not CollectionDisplayRow row) return;

            // Load if not already loaded
            if (row.DeckUsageRows.Count == 0)
                LoadDeckUsageForRow(row);

            // Expand the row
            row.IsExpanded = true;
            var dgRow = grid.ItemContainerGenerator
                .ContainerFromItem(row) as DataGridRow;
            if (dgRow != null)
                dgRow.DetailsVisibility = Visibility.Visible;
        }

        private string GetTableKey(DataGrid grid)
        {
            if (grid == TopDataGrid)
            {
                return _currentMode switch
                {
                    "CollectionToDeck" => "Collection",
                    "DeckToCollection" => "Deck",
                    _ => "Pool"
                };
            }
            if (grid == BottomDataGrid)
            {
                return _currentMode switch
                {
                    "PoolToCollection" => "Collection",
                    "PoolToPlanechase" => "Planechase",
                    "PoolToArchenemy" => "Archenemy",
                    "PoolToVanguard" => "Vanguard",
                    "PoolToTokens" => "Tokens",
                    "PoolToArtSeries" => "ArtSeries",
                    "PoolToConspiracy" => "Conspiracy",
                    "CollectionToTradeBinder" => "TradeBinder",
                    "PoolToWantList" => "WantList",
                    "DeckToCollection" => "Collection",
                    _ => "Collection"
                };
            }
            // Deck tab grids
            return "Deck";
        }

        private void BtnExpandDeckUsage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not CollectionDisplayRow row) return;

            // Toggle expanded state
            row.IsExpanded = !row.IsExpanded;

            // Load deck usage if expanding and not yet loaded
            if (row.IsExpanded && row.DeckUsageRows.Count == 0)
                LoadDeckUsageForRow(row);

            // Show/hide row details — check both top and bottom grids
            foreach (var grid in new[] { TopDataGrid, BottomDataGrid })
            {
                var dgRow = grid.ItemContainerGenerator
                    .ContainerFromItem(row) as DataGridRow;
                if (dgRow != null)
                    dgRow.DetailsVisibility = row.IsExpanded
                        ? Visibility.Visible : Visibility.Collapsed;
            }

            // Update button glyph
            btn.Content = row.ExpandGlyph;

            e.Handled = true;
        }

        private void LoadDeckUsageForRow(CollectionDisplayRow row)
        {
            row.DeckUsageRows.Clear();

            // Check all open (possibly unsaved) decks first
            var checkedNames = new HashSet<string>();

            foreach (var deck in _openDecks)
            {
                checkedNames.Add(deck.Name);

                var matches = deck.Cards.Where(c =>
                    c.Name.Equals(row.Name,
                        StringComparison.OrdinalIgnoreCase) ||
                    (row.PoolId > 0 && c.PoolId == row.PoolId))
                    .ToList();

                foreach (var card in matches)
                {
                    row.DeckUsageRows.Add(new Models.DeckUsageRow
                    {
                        DeckName = deck.Name,
                        DeckType = deck.DeckType.ToString(),
                        Quantity = card.TotalQuantity,
                        Category = card.CategoryDisplay,
                        IsFoil = card.FoilQuantity > 0
                            ? (card.Quantity > 0 ? "Mixed" : "Yes")
                            : "No"
                    });
                }
            }

            // Then check saved deck files that aren't already open
            string folder = Services.AppFolderService.DecksFolder;
            if (Directory.Exists(folder))
            {
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                foreach (var file in Directory.GetFiles(folder, "*.deck",
                    SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var deck = System.Text.Json.JsonSerializer
                            .Deserialize<Models.Deck>(json, options);
                        if (deck == null) continue;

                        // Skip if already covered by open deck
                        if (checkedNames.Contains(deck.Name)) continue;

                        var matches = deck.Cards.Where(c =>
                            c.Name.Equals(row.Name,
                                StringComparison.OrdinalIgnoreCase) ||
                            (row.PoolId > 0 && c.PoolId == row.PoolId))
                            .ToList();

                        foreach (var card in matches)
                        {
                            row.DeckUsageRows.Add(new Models.DeckUsageRow
                            {
                                DeckName = deck.Name,
                                DeckType = deck.DeckType.ToString(),
                                Quantity = card.TotalQuantity,
                                Category = card.CategoryDisplay,
                                IsFoil = card.FoilQuantity > 0
                                    ? (card.Quantity > 0 ? "Mixed" : "Yes")
                                    : "No"
                            });
                        }
                    }
                    catch { }
                }
            }

            // If nothing found, collapse the row and reset expand state
            if (row.DeckUsageRows.Count == 0)
            {
                row.IsExpanded = false;
                foreach (var grid in new[] { TopDataGrid, BottomDataGrid })
                {
                    if (grid.ItemContainerGenerator
                        .ContainerFromItem(row) is DataGridRow dgRow)
                        dgRow.DetailsVisibility = Visibility.Collapsed;
                }
            }
        }

        // ── Deck state ────────────────────────────────────────────────────────────
        private List<Deck> _openDecks = new();
        private Deck? _activeDeck = null;

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════
        public MainWindow()
        {
            App.Splash?.SetStatus("Initializing database...", 10);
            InitializeComponent();

            App.Splash?.SetStatus("Checking database schema...", 20);
            EnsureDatabase();

            App.Splash?.SetStatus("Loading card pool...", 40);
            LoadCaches();

            App.Splash?.SetStatus("Applying preferences...", 80);
            BtnTheme.Content = ThemeService.ThemeToggleIcon;
            BtnTheme.ToolTip = ThemeService.ThemeToggleTooltip;

            // Wire column order persistence for the two permanent grids
            WireColumnLayoutSave(TopDataGrid, "Pool");
            WireColumnLayoutSave(BottomDataGrid, "Collection");

            // Auto-size columns after first full render
            Loaded += MainWindow_Loaded;

            // Apply saved startup view from preferences
            string startupView = GetSetting(Windows.PreferencesWindow.KeyStartupView)
                                 ?? "PoolToCollection";
            bool viewSet = false;
            foreach (ComboBoxItem item in ViewModeComboBox.Items)
            {
                if (item.Tag?.ToString() == startupView)
                {
                    ViewModeComboBox.SelectedItem = item;
                    viewSet = true;
                    break;
                }
            }
            if (!viewSet) ViewModeComboBox.SelectedIndex = 0;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Clear saved column layouts if version changed (forces header-sized columns)
            if (GetSetting(ColLayoutVersion) != CurrentColLayoutVersion)
            {
                var keys = GetAllSettingKeys()
                    .Where(k => k.StartsWith(ColOrderPrefix))
                    .ToList();
                foreach (var k in keys)
                    DeleteSetting(k);
                SaveSetting(ColLayoutVersion, CurrentColLayoutVersion);
            }

            LoadRecentDecks();

            // Close splash screen
            if (App.Splash != null)
            {
                App.Splash.SetStatus("Ready!", 100);
                _ = App.Splash.CloseWhenReady();
                App.Splash = null;
            }

            // First-run database download requested by installer
            if (App.FirstRunDownloadRequested)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var result = MessageBox.Show(
                        "Welcome to Breakers of E!\n\n" +
                        "Would you like to download the card database now?\n\n" +
                        "This downloads ~98,000 cards from Scryfall and is required " +
                        "before you can use the app. It may take a few minutes.",
                        "Download Card Database",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                        MenuUpdateDatabase_Click(this, new RoutedEventArgs());
                }), System.Windows.Threading.DispatcherPriority.Background);
            }

            // Dispatcher at Render priority ensures the grid has laid out
            // and ActualWidth values are valid
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string topKey = _currentMode switch
                {
                    "CollectionToDeck" => "Collection",
                    "DeckToCollection" => "Deck",
                    _ => "Pool"
                };
                AutoSizeColumnsToHeader(TopDataGrid, topKey);

                if (BottomDataGrid.Visibility == Visibility.Visible)
                    AutoSizeColumnsToHeader(BottomDataGrid, GetTableKey(BottomDataGrid));
            }),
            System.Windows.Threading.DispatcherPriority.Render);
        }

        private void EnsureDatabase()
        {
            // Pool database — card data, prices, legalities
            using var db = new AppDbContext();
            db.Database.EnsureCreated();
            db.MigrateSchema();

            // Collection database — completely separate, never touched by pool updates
            using var cdb = new Data.CollectionDbContext();
            cdb.MigrateSchema();
        }

        private void LoadCaches()
        {
            using var db = new AppDbContext();

            _poolCache = db.PoolCards.AsNoTracking()
                                .OrderBy(c => c.Name)
                                .ThenBy(c => c.SetCode)
                                .ToList();

            // First-run guidance — pool is empty
            if (_poolCache.Count == 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show(
                        "Welcome to Breakers of E!\n\n" +
                        "Your card pool is currently empty.\n\n" +
                        "To get started:\n" +
                        "  1. Go to Tools → Update Card Database\n" +
                        "  2. Click Download to fetch all cards from Scryfall\n" +
                        "  3. This only needs to be done once (~80,000 cards)\n\n" +
                        "After downloading, the full card pool will be available\n" +
                        "in all views and you can start building your collection.",
                        "Welcome — First Time Setup",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }

            _tokenCache = db.TokenCards.AsNoTracking()
                                .OrderBy(c => c.Name).ToList();
            _planarCache = db.PlanarCards.AsNoTracking()
                                .OrderBy(c => c.Name).ToList();
            _schemeCache = db.SchemeCards.AsNoTracking()
                                .OrderBy(c => c.Name).ToList();
            _vanguardCache = db.VanguardCards.AsNoTracking()
                                .OrderBy(c => c.Name).ToList();
            _artSeriesCache = db.ArtSeriesCards.AsNoTracking()
                                .OrderBy(c => c.Name).ToList();
            _conspiracyCache = db.ConspiracyCards.AsNoTracking()
                                .OrderBy(c => c.Name).ToList();
        }

        private static string EnsureFolder(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        // ════════════════════════════════════════════════════════════════════
        // MODE SWITCHER
        // ════════════════════════════════════════════════════════════════════
        private void ViewModeComboBox_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (ViewModeComboBox?.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                _currentMode = tag;
                _searchText = string.Empty;
                _bottomSearch = string.Empty;
                if (SearchBox != null) SearchBox.Text = string.Empty;

                // Clear ALL filter state — expression, set codes, column filters
                _topFilter.Clear();
                _topFilterNode = null;
                _topSelectedSetCodes.Clear();
                _topColumnFilters.ClearAll();
                TopFilterSummary.Text = "No filter active";

                _bottomFilter.Clear();
                _bottomFilterNode = null;
                _bottomSelectedSetCodes.Clear();
                _bottomColumnFilters.ClearAll();
                BottomFilterSummary.Text = "No filter active";

                // Reset funnel icons on both grids
                ResetAllFunnelIcons(TopDataGrid);
                ResetAllFunnelIcons(BottomDataGrid);

                // Hide commander freeze row when leaving DeckToCollection
                // Restore default row style when leaving DeckToCollection
                TopDataGrid.RowStyle = (Style)FindResource("DataGridRowStyle");

                LoadCurrentMode();
                UpdateToolbarState();
            }
        }

        private void LoadCurrentMode()
        {
            // Show collection or deck area based on mode
            bool isDeckMode = _currentMode == "PoolToDeck" ||
                              _currentMode == "CollectionToDeck" ||
                              _currentMode == "DeckToCollection";

            bool isDeckTabMode = _currentMode == "PoolToDeck" ||
                                 _currentMode == "CollectionToDeck";

            // Reset top grid editability — only CollectionToDeck allows editing
            TopDataGrid.IsReadOnly = true;

            // Restore pool columns if leaving DeckToCollection
            RemoveDeckColumns(TopDataGrid);
            RemoveCollectionColumns(TopDataGrid);

            // Restore bottom collection columns if leaving deck mode
            if (!isDeckTabMode)
            {
                UnwireDeckEvents();
                BottomDataGrid.IsReadOnly = false;
                RemoveDeckColumns(BottomDataGrid);
            }

            // Clear mana symbol panels — only deck modes populate them
            if (!isDeckTabMode)
            {
                BottomTableManaSymbols.Children.Clear();
                TopTableManaSymbols.Children.Clear();
            }

            // BottomDataGrid always visible — deck data loads into it
            BottomDataGrid.Visibility = Visibility.Visible;

            // Deck tab strip only visible in deck-building modes, not DeckToCollection
            DeckTabControl.Visibility = (_openDecks.Any() && isDeckTabMode)
                ? Visibility.Visible : Visibility.Collapsed;

            // TopSummaryGrid shows for CollectionToDeck (collection totals) and DeckToCollection (deck totals)
            bool showTopSummary = _currentMode == "CollectionToDeck" ||
                                  _currentMode == "DeckToCollection";
            TopSummaryGrid.Visibility = showTopSummary
                ? Visibility.Visible : Visibility.Collapsed;
            if (!showTopSummary)
            {
                TopSummaryGrid.ItemsSource = null;
                TopTableManaSymbols.Children.Clear();
            }
            // BottomSummaryGrid always visible — cleared when no data
            BottomSummaryGrid.Visibility = Visibility.Visible;
            if (isDeckTabMode && _activeDeck == null)
                BottomSummaryGrid.ItemsSource = null;

            switch (_currentMode)
            {
                case "PoolToCollection":
                    LoadTopTable_Pool();
                    LoadBottomTable_Collection();
                    TopSearchLabel.Text = "Pool  (read only)";
                    BottomTableLabel.Text = "Collection";
                    break;

                case "PoolToPlanechase":
                    LoadTopTable_Planechase();
                    LoadBottomTable_PlanechaseCollection();
                    TopSearchLabel.Text = "Planechase  (read only)";
                    BottomTableLabel.Text = "My Planechase";
                    break;

                case "PoolToArchenemy":
                    LoadTopTable_Archenemy();
                    LoadBottomTable_ArchenemyCollection();
                    TopSearchLabel.Text = "Archenemy  (read only)";
                    BottomTableLabel.Text = "My Archenemy";
                    break;

                case "PoolToVanguard":
                    LoadTopTable_Vanguard();
                    LoadBottomTable_VanguardCollection();
                    TopSearchLabel.Text = "Vanguard  (read only)";
                    BottomTableLabel.Text = "My Vanguard";
                    break;

                case "PoolToTokens":
                    LoadTopTable_Tokens();
                    LoadBottomTable_TokenCollection();
                    TopSearchLabel.Text = "Token Database  (read only)";
                    BottomTableLabel.Text = "My Tokens";
                    break;

                case "PoolToArtSeries":
                    LoadTopTable_ArtSeries();
                    LoadBottomTable_ArtSeriesCollection();
                    TopSearchLabel.Text = "Art Series  (read only)";
                    BottomTableLabel.Text = "My Art Series";
                    break;

                case "PoolToConspiracy":
                    LoadTopTable_Conspiracy();
                    LoadBottomTable_ConspiracyCollection();
                    TopSearchLabel.Text = "Conspiracy  (read only)";
                    BottomTableLabel.Text = "My Conspiracy";
                    break;

                case "CollectionToTradeBinder":
                    LoadTopTable_CollectionForDeck(); // reuse — shows your collection
                    LoadBottomTable_TradeBinder();
                    RestoreCollectionContextMenu();
                    TopSearchLabel.Text = "Collection";
                    BottomTableLabel.Text = "Trade Binder — Have List";
                    break;

                case "PoolToWantList":
                    LoadTopTable_Pool();
                    LoadBottomTable_WantList();
                    RestoreCollectionContextMenu();
                    TopSearchLabel.Text = "Pool  (read only)";
                    BottomTableLabel.Text = "Want List";
                    break;

                case "PoolToDeck":
                    LoadTopTable_Pool();
                    TopSearchLabel.Text = "Pool  (read only)";
                    BottomTableLabel.Text = "Deck";
                    LoadBottomTable_ActiveDeck();
                    UpdateDeckSummary(_activeDeck);
                    break;

                case "CollectionToDeck":
                    LoadTopTable_CollectionForDeck();
                    TopSearchLabel.Text = "Collection";
                    BottomTableLabel.Text = "Deck";
                    LoadBottomTable_ActiveDeck();
                    UpdateDeckSummary(_activeDeck);
                    TopDataGrid.IsReadOnly = false;
                    break;

                case "DeckToCollection":
                    UnwireDeckEvents();
                    RemoveDeckColumns(BottomDataGrid);
                    RestoreCollectionContextMenu();
                    LoadTopTable_DeckForCollection();
                    LoadBottomTable_Collection();
                    UpdateTopTableLabel();
                    BottomTableLabel.Text = "Collection";
                    break;
            }

            // Restore column visibility & order after mode loads
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string topKey = _currentMode switch
                {
                    "CollectionToDeck" => "Collection",
                    "DeckToCollection" => "Deck",
                    _ => "Pool"
                };
                RestoreColumnLayout(TopDataGrid, topKey);
                if (BottomDataGrid.Visibility == Visibility.Visible)
                    RestoreColumnLayout(BottomDataGrid, "Collection");
            }),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        // ── Lock state ───────────────────────────────────────────────────────
        private void LoadLockState()
        {
            UpdateToolbarState();
        }

        private void UpdateLockUI()
        {
            UpdateToolbarState();
        }

        // ════════════════════════════════════════════════════════════════════
        // TOP TABLE LOADERS
        // ════════════════════════════════════════════════════════════════════
        /// <summary>Apply default ascending Name sort to a DataGrid.</summary>
        private static void ApplyDefaultSort(DataGrid grid,
            string columnPath = "Name")
        {
            var view = System.Windows.Data.CollectionViewSource
                .GetDefaultView(grid.ItemsSource);
            if (view == null) return;
            if (view.SortDescriptions.Count == 0 ||
                view.SortDescriptions[0].PropertyName != columnPath ||
                view.SortDescriptions[0].Direction !=
                    System.ComponentModel.ListSortDirection.Ascending)
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                    columnPath,
                    System.ComponentModel.ListSortDirection.Ascending));
            }
        }

        private bool TopFilterActive => ChkTopFilter?.IsChecked == true;
        private bool BottomFilterActive => ChkBottomFilter?.IsChecked == true;

        private void LoadTopTable_Pool()
        {
            SetTopExpandColumnVisibility(Visibility.Collapsed);
            RemoveCollectionColumns(TopDataGrid);
            var all = _poolCache ?? new List<PoolCard>();

            var filtered = TopFilterActive
                ? FilterService.Apply(all, _topFilter, _searchText)
                : FilterService.Apply(all, new FilterState(), _searchText);

            if (TopFilterActive)
            {
                if (_topFilterNode != null &&
                    FilterExpressionService.HasConditions(_topFilterNode))
                    filtered = FilterExpressionService.Apply(
                        filtered, _topFilterNode, true);

                if (_topSelectedSetCodes.Count > 0)
                    filtered = filtered
                        .Where(c => _topSelectedSetCodes.Contains(c.SetCode))
                        .ToList();
            }

            if (TopFilterActive && _topColumnFilters.HasActiveFilters)
                filtered = _topColumnFilters.Apply(filtered);

            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;

            TopDataGrid.ItemsSource = filtered;
            ApplyDefaultSort(TopDataGrid);
            SetStatus($"{filtered.Count:N0} cards in pool");
            UpdateTopSummary("Pool",
                nonFoil: filtered.Cast<PoolCard>().Count(c => c.IsNonFoil),
                foil: filtered.Cast<PoolCard>().Count(c => c.IsFoil),
                value: (decimal)filtered.Cast<PoolCard>()
                    .Where(c => c.PriceUsd.HasValue)
                    .Sum(c => (double)c.PriceUsd!.Value));
        }

        private void LoadTopTable_Tokens()
        {
            RemoveCollectionColumns(TopDataGrid);
            var all = _tokenCache ?? new List<TokenCard>();
            var filtered = TopFilterActive
                ? FilterService.Apply(all, _topFilter, _searchText)
                : FilterService.Apply(all, new FilterState(), _searchText);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            ApplyDefaultSort(TopDataGrid);
            UpdateTopSummary("Tokens", filtered.Count);
        }

        private void LoadTopTable_Planechase()
        {
            RemoveCollectionColumns(TopDataGrid);
            var all = _planarCache ?? new List<PlanarCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            ApplyDefaultSort(TopDataGrid);
            UpdateTopSummary("Planechase", filtered.Count);
        }

        private void LoadTopTable_Archenemy()
        {
            RemoveCollectionColumns(TopDataGrid);
            var all = _schemeCache ?? new List<SchemeCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            ApplyDefaultSort(TopDataGrid);
            UpdateTopSummary("Archenemy", filtered.Count);
        }

        private void LoadTopTable_Vanguard()
        {
            RemoveCollectionColumns(TopDataGrid);
            var all = _vanguardCache ?? new List<VanguardCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            ApplyDefaultSort(TopDataGrid);
            UpdateTopSummary("Vanguard", filtered.Count);
        }

        private void LoadTopTable_ArtSeries()
        {
            RemoveCollectionColumns(TopDataGrid);
            var all = _artSeriesCache ?? new List<ArtSeriesCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            ApplyDefaultSort(TopDataGrid);
            UpdateTopSummary("Art Series", filtered.Count);
        }

        private void LoadTopTable_Conspiracy()
        {
            RemoveCollectionColumns(TopDataGrid);
            var all = _conspiracyCache ?? new List<ConspiracyCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            ApplyDefaultSort(TopDataGrid);
            UpdateTopSummary("Conspiracy", filtered.Count);
        }

        private void LoadBottomTable_ConspiracyCollection()
        {
            using var cdb = new CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.ConspiracyCollectionEntries.AsNoTracking().ToList();
            if (entries.Count == 0) { BottomDataGrid.ItemsSource = null; return; }
            var ids = entries.Select(e => e.ConspiracyId).ToHashSet();
            var cards = pdb.ConspiracyCards.AsNoTracking()
                .Where(c => ids.Contains(c.ConspiracyId))
                .ToList().ToDictionary(c => c.ConspiracyId);
            var rows = new List<CollectionDisplayRow>(entries.Count);
            foreach (var ce in entries)
            {
                if (!cards.TryGetValue(ce.ConspiracyId, out var cc)) continue;
                rows.Add(new CollectionDisplayRow
                {
                    CollectionEntryId = ce.ConspiracyCollectionEntryId,
                    Name = cc.Name,
                    SetCode = cc.SetCode,
                    SetName = cc.SetName,
                    CollectorNumber = cc.CollectorNumber,
                    TypeLine = cc.TypeLine,
                    ManaCost = cc.ManaCost,
                    ManaValue = cc.ManaValue,
                    ColorIdentity = cc.ColorIdentity,
                    Colors = cc.Colors,
                    OracleText = cc.OracleText,
                    FlavorText = cc.FlavorText,
                    Artist = cc.Artist,
                    Rarity = cc.Rarity,
                    IsFoil = cc.IsFoil,
                    IsNonFoil = cc.IsNonFoil,
                    ImageNormalUrl = cc.ImageNormalUrl,
                    LocalImagePath = cc.LocalImagePath,
                    Quantity = ce.Quantity,
                    FoilQuantity = ce.FoilQuantity,
                    Condition = ce.Condition,
                    Language = ce.Language,
                    StorageLocation = ce.StorageLocation,
                    Notes = ce.Notes,
                    DateAdded = ce.DateAdded,
                    DateModified = ce.DateModified
                });
            }
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_TradeBinder()
        {
            EnsureCollectionColumns(BottomDataGrid);
            using var cdb = new Data.CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.TradeBinderEntries.AsNoTracking().ToList();
            if (entries.Count == 0) { BottomDataGrid.ItemsSource = null; return; }
            var ids = entries.Select(e => e.PoolId).ToHashSet();
            var cards = pdb.PoolCards.AsNoTracking()
                .Where(c => ids.Contains(c.PoolId))
                .ToList().ToDictionary(c => c.PoolId);

            // Group by PoolId — merge foil + non-foil into one display row
            var grouped = entries.GroupBy(e => e.PoolId);
            var rows = new List<CollectionDisplayRow>();
            foreach (var grp in grouped)
            {
                if (!cards.TryGetValue(grp.Key, out var pc)) continue;
                var nonFoil = grp.FirstOrDefault(e => !e.IsFoil);
                var foil = grp.FirstOrDefault(e => e.IsFoil);
                var primary = nonFoil ?? foil!;
                rows.Add(new CollectionDisplayRow
                {
                    CollectionEntryId = primary.TradeBinderEntryId,
                    PoolId = pc.PoolId,
                    Name = pc.Name,
                    SetCode = pc.SetCode,
                    SetName = pc.SetName,
                    CollectorNumber = pc.CollectorNumber,
                    TypeLine = pc.TypeLine,
                    OracleText = pc.OracleText,
                    FlavorText = pc.FlavorText,
                    ManaCost = pc.ManaCost,
                    ManaValue = pc.ManaValue,
                    ColorIdentity = pc.ColorIdentity,
                    Rarity = pc.Rarity,
                    Artist = pc.Artist,
                    Power = pc.Power,
                    Toughness = pc.Toughness,
                    IsFoil = pc.IsFoil,
                    IsNonFoil = pc.IsNonFoil,
                    PriceUsd = pc.PriceUsd,
                    PriceUsdFoil = pc.PriceUsdFoil,
                    ImageNormalUrl = pc.ImageNormalUrl,
                    LocalImagePath = pc.LocalImagePath,
                    IsLegalStandard = pc.IsLegalStandard,
                    IsLegalModern = pc.IsLegalModern,
                    IsLegalPioneer = pc.IsLegalPioneer,
                    IsLegalLegacy = pc.IsLegalLegacy,
                    IsLegalVintage = pc.IsLegalVintage,
                    Quantity = nonFoil?.Quantity ?? 0,
                    FoilQuantity = foil?.Quantity ?? 0,
                    Condition = primary.Condition,
                    SellAt = primary.AskingPrice,
                    Notes = primary.Notes,
                    MarketValue = pc.PriceUsd,
                    DateAdded = primary.DateAdded
                });
            }
            rows.Sort((a, b) => string.Compare(
                a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_WantList()
        {
            EnsureCollectionColumns(BottomDataGrid);
            using var cdb = new Data.CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.WantListEntries.AsNoTracking().ToList();
            if (entries.Count == 0) { BottomDataGrid.ItemsSource = null; return; }
            var ids = entries.Select(e => e.PoolId).ToHashSet();
            var cards = pdb.PoolCards.AsNoTracking()
                .Where(c => ids.Contains(c.PoolId))
                .ToList().ToDictionary(c => c.PoolId);

            // Group by PoolId — merge foil + non-foil into one display row
            var grouped = entries.GroupBy(e => e.PoolId);
            var rows = new List<CollectionDisplayRow>();
            foreach (var grp in grouped)
            {
                if (!cards.TryGetValue(grp.Key, out var pc)) continue;
                var nonFoil = grp.FirstOrDefault(e => !e.IsFoil);
                var foil = grp.FirstOrDefault(e => e.IsFoil);
                // Use the first entry's ID for editing
                var primary = nonFoil ?? foil!;
                rows.Add(new CollectionDisplayRow
                {
                    CollectionEntryId = primary.WantListEntryId,
                    PoolId = pc.PoolId,
                    Name = pc.Name,
                    SetCode = pc.SetCode,
                    SetName = pc.SetName,
                    CollectorNumber = pc.CollectorNumber,
                    TypeLine = pc.TypeLine,
                    OracleText = pc.OracleText,
                    FlavorText = pc.FlavorText,
                    ManaCost = pc.ManaCost,
                    ManaValue = pc.ManaValue,
                    ColorIdentity = pc.ColorIdentity,
                    Rarity = pc.Rarity,
                    Artist = pc.Artist,
                    Power = pc.Power,
                    Toughness = pc.Toughness,
                    IsFoil = pc.IsFoil,
                    IsNonFoil = pc.IsNonFoil,
                    PriceUsd = pc.PriceUsd,
                    PriceUsdFoil = pc.PriceUsdFoil,
                    ImageNormalUrl = pc.ImageNormalUrl,
                    LocalImagePath = pc.LocalImagePath,
                    IsLegalStandard = pc.IsLegalStandard,
                    IsLegalModern = pc.IsLegalModern,
                    IsLegalPioneer = pc.IsLegalPioneer,
                    IsLegalLegacy = pc.IsLegalLegacy,
                    IsLegalVintage = pc.IsLegalVintage,
                    Quantity = nonFoil?.Quantity ?? 0,
                    FoilQuantity = foil?.Quantity ?? 0,
                    BuyAt = primary.OfferPrice,
                    Notes = primary.Notes,
                    MarketValue = pc.PriceUsd,
                    DateAdded = primary.DateAdded
                });
            }
            rows.Sort((a, b) => string.Compare(
                a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadTopTable_DeckForCollection()
        {
            RemoveCollectionColumns(TopDataGrid);
            EnsureDeckColumns(TopDataGrid);
            SetTopExpandColumnVisibility(Visibility.Collapsed);

            // Apply commander highlight row style
            var commanderStyle = new Style(typeof(DataGridRow),
                (Style)FindResource("DataGridRowStyle"));
            var trigger = new DataTrigger
            {
                Binding = new System.Windows.Data.Binding("IsCommander"),
                Value = true
            };
            trigger.Setters.Add(new Setter(
                DataGridRow.BackgroundProperty,
                FindResource("AccentBrush")));
            trigger.Setters.Add(new Setter(
                DataGridRow.ForegroundProperty,
                System.Windows.Media.Brushes.White));
            commanderStyle.Triggers.Add(trigger);
            TopDataGrid.RowStyle = commanderStyle;

            if (_activeDeck == null)
            {
                TopDataGrid.ItemsSource = null;
                UpdateTopSummary("Deck", 0);
                return;
            }

            // Commanders first (at top of list), then rest
            var rows = _activeDeck.Cards
                .OrderBy(c => c.IsCommander ? 0 : 1)
                .ThenBy(c => c.Category)
                .ThenBy(c => c.Name)
                .ToList();

            TopDataGrid.ItemsSource = rows;
            ApplyDefaultSort(TopDataGrid);

            // In DeckToCollection mode populate TopSummaryGrid with deck totals
            if (_currentMode == "DeckToCollection" && _activeDeck != null)
            {
                var deck = _activeDeck;
                // Remove from wired set so we always re-attach listeners
                // after SyncAndPopulateDeckSummary rebuilds the columns
                _wiredSummaries.Remove((TopDataGrid, TopSummaryGrid));
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SyncAndPopulateDeckSummary(TopSummaryGrid, TopDataGrid, deck);
                    _wiredSummaries.Remove((TopDataGrid, TopSummaryGrid));
                    WireSummaryColumnSync(TopDataGrid, TopSummaryGrid);
                }),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }

        private void LoadTopTable_CollectionForDeck()
        {
            EnsureCollectionColumns(TopDataGrid);
            SetTopExpandColumnVisibility(Visibility.Visible);
            using var cdb = new Data.CollectionDbContext();
            using var pdb = new Data.AppDbContext();
            var rows = BuildCollectionRows(cdb, pdb);
            if (!string.IsNullOrWhiteSpace(_lastSearchTerm))
                rows = rows.Where(c => c.Name.Contains(
                    _lastSearchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
            if (TopFilterActive && _topColumnFilters.HasActiveFilters)
                rows = _topColumnFilters.Apply(rows);
            for (int i = 0; i < rows.Count; i++)
                rows[i].RowIndex = i;
            TopDataGrid.ItemsSource = rows;
            ApplyDefaultSort(TopDataGrid);
            UpdateTopSummary("Collection",
                nonFoil: rows.Sum(r => r.Quantity),
                foil: rows.Sum(r => r.FoilQuantity),
                total: rows.Sum(r => r.Quantity + r.FoilQuantity),
                value: rows.Sum(r => r.TotalValue));
        }

        private void SetTopExpandColumnVisibility(Visibility vis)
        {
            var expandCol = TopDataGrid.Columns.FirstOrDefault(c =>
                c.SortMemberPath == CollectionColumnMarker + "expand");
            if (expandCol != null)
                expandCol.Visibility = vis;
        }

        private static readonly string CollectionColumnMarker = "CollectionCol_";
        private readonly List<DataGridColumn> _savedPoolColumns = new();

        private void EnsureCollectionColumns(DataGrid grid)
        {
            // If already in collection mode, do nothing
            if (grid.Columns.Any(c =>
                c.SortMemberPath?.StartsWith(CollectionColumnMarker) == true))
                return;

            // Save existing pool columns to restore later
            _savedPoolColumns.Clear();
            _savedPoolColumns.AddRange(grid.Columns.ToList());
            grid.Columns.Clear();

            DataGridColumn MakeText(string header, string binding,
                double width, bool readOnly = false)
            {
                var col = new DataGridTextColumn
                {
                    Header = header,
                    SortMemberPath = CollectionColumnMarker + binding,
                    Binding = new System.Windows.Data.Binding(binding),
                    Width = new DataGridLength(width),
                    IsReadOnly = readOnly
                };
                return col;
            }


            // ── Expand button ─────────────────────────────────────────────
            var expandCol = new DataGridTemplateColumn
            {
                Header = "↕",
                SortMemberPath = CollectionColumnMarker + "expand",
                Width = new DataGridLength(28),
                CanUserResize = false
            };
            var expandTemplate = new DataTemplate();
            var btnFactory = new FrameworkElementFactory(typeof(Button));
            btnFactory.SetBinding(Button.ContentProperty,
                new System.Windows.Data.Binding("ExpandGlyph"));
            btnFactory.SetBinding(VisibilityProperty,
                new System.Windows.Data.Binding("ExpandButtonVisibility"));
            btnFactory.SetBinding(Button.TagProperty,
                new System.Windows.Data.Binding()); // binds to entire row item
            btnFactory.SetValue(Button.WidthProperty, 20.0);
            btnFactory.SetValue(Button.HeightProperty, 20.0);
            btnFactory.SetValue(Button.BackgroundProperty,
                System.Windows.Media.Brushes.Transparent);
            btnFactory.SetValue(Button.BorderThicknessProperty,
                new Thickness(0));
            btnFactory.AddHandler(Button.ClickEvent,
                new RoutedEventHandler(BtnExpandDeckUsage_Click));
            expandTemplate.VisualTree = btnFactory;
            expandCol.CellTemplate = expandTemplate;
            grid.Columns.Add(expandCol);

            // ── ES symbol ────────────────────────────────────────────────
            var esCol = new DataGridTemplateColumn
            {
                Header = "ES",
                SortMemberPath = CollectionColumnMarker + "SetSymbolPath",
                Width = new DataGridLength(32),
                CanUserResize = false
            };
            var esTemplate = new DataTemplate();
            var imgFactory = new FrameworkElementFactory(typeof(Image));
            imgFactory.SetBinding(Image.SourceProperty,
                new System.Windows.Data.Binding("SetSymbolPath")
                {
                    Converter = (System.Windows.Data.IValueConverter)
                        Application.Current.Resources["ImageSourceConverter"]
                });
            imgFactory.SetValue(Image.WidthProperty, 16.0);
            imgFactory.SetValue(Image.HeightProperty, 16.0);
            esTemplate.VisualTree = imgFactory;
            esCol.CellTemplate = esTemplate;
            grid.Columns.Add(esCol);

            // ── Text columns in exact order ───────────────────────────────
            grid.Columns.Add(MakeText("Name", "Name", 200, true));
            grid.Columns.Add(MakeText("Edition", "SetCode", 55, true));
            grid.Columns.Add(MakeText("Qty", "Quantity", 45));
            grid.Columns.Add(MakeText("Foil Qty", "FoilQuantity", 60));
            grid.Columns.Add(MakeText("Used", "UsedCount", 50, true));
            grid.Columns.Add(MakeText("Available", "AvailableCount", 70, true));
            grid.Columns.Add(MakeText("Buy At", "BuyAtDisplay", 70));
            grid.Columns.Add(MakeText("Sell At", "SellAtDisplay", 70));
            grid.Columns.Add(MakeText("Sell At Value", "SellAtValueDisplay", 90, true));
            grid.Columns.Add(MakeText("Price High", "PriceHighDisplay", 80, true));
            grid.Columns.Add(MakeText("Market Value", "MarketValueDisplay", 90, true));
            grid.Columns.Add(MakeText("Needed", "Needed", 60));
            grid.Columns.Add(MakeText("Excess", "Excess", 60));
            grid.Columns.Add(MakeText("Target", "Target", 60));
            grid.Columns.Add(MakeText("Condition", "Condition", 90));
            grid.Columns.Add(MakeText("Notes", "Notes", 150));
            grid.Columns.Add(MakeText("Storage", "StorageLocation", 100));
            grid.Columns.Add(MakeText("Desired", "Desired", 90));
            grid.Columns.Add(MakeText("Group", "CardGroup", 80));
            grid.Columns.Add(MakeText("Print Type", "PrintType", 90));
            grid.Columns.Add(MakeText("Buy", "BuyStatus", 90));
            grid.Columns.Add(MakeText("Sell", "SellStatus", 90));
            grid.Columns.Add(MakeText("Added", "DateAdded", 130, true));
            grid.Columns.Add(MakeText("Market Price", "PriceUsdDisplay", 90, true));
            grid.Columns.Add(MakeText("Price Low", "PriceLowDisplay", 80, true));
            grid.Columns.Add(MakeText("Color", "ColorDisplay", 50, true));
            grid.Columns.Add(MakeText("Type", "TypeLine", 160, true));
            grid.Columns.Add(MakeText("Rarity", "RarityCode", 50, true));
            grid.Columns.Add(MakeText("P/T", "PowerToughness", 55, true));
            grid.Columns.Add(MakeText("Text", "OracleText", 220, true));
            grid.Columns.Add(MakeText("Flavor", "FlavorText", 160, true));
            grid.Columns.Add(MakeText("Artist", "Artist", 130, true));
            grid.Columns.Add(MakeText("No", "CollectorNumber", 55, true));
            grid.Columns.Add(MakeText("Power", "Power", 50, true));
            grid.Columns.Add(MakeText("Toughness", "Toughness", 70, true));
            grid.Columns.Add(MakeText("CMC", "ManaValue", 45, true));
            grid.Columns.Add(MakeText("Row", "RowIndex", 45, true));
        }

        private void RemoveCollectionColumns(DataGrid grid)
        {
            bool hasCollectionCols = grid.Columns.Any(c =>
                c.SortMemberPath?.StartsWith(CollectionColumnMarker) == true);

            if (!hasCollectionCols) return;

            grid.Columns.Clear();
            foreach (var col in _savedPoolColumns)
                grid.Columns.Add(col);
            _savedPoolColumns.Clear();
        }

        private const string DeckColumnMarker = "DeckCol_";
        private readonly List<DataGridColumn> _savedPoolColumnsForDeck = new();

        private void EnsureDeckColumns(DataGrid grid)
        {
            // Already has deck columns
            if (grid.Columns.Any(c =>
                c.SortMemberPath?.StartsWith(DeckColumnMarker) == true))
                return;

            // Save existing pool columns
            _savedPoolColumnsForDeck.Clear();
            _savedPoolColumnsForDeck.AddRange(grid.Columns);
            grid.Columns.Clear();

            DataGridColumn MakeText(string header, string binding,
                double width, bool readOnly = true)
            {
                var col = new DataGridTextColumn
                {
                    Header = header,
                    SortMemberPath = DeckColumnMarker + binding,
                    Binding = new System.Windows.Data.Binding(binding),
                    Width = new DataGridLength(width),
                    IsReadOnly = readOnly
                };
                return col;
            }

            // ES
            var esCol = new DataGridTemplateColumn
            {
                Header = "ES",
                SortMemberPath = DeckColumnMarker + "SetSymbolPath",
                Width = new DataGridLength(32),
                CanUserResize = false
            };
            var esTemplate = new DataTemplate();
            var imgFactory = new FrameworkElementFactory(typeof(Image));
            imgFactory.SetBinding(Image.SourceProperty,
                new System.Windows.Data.Binding("SetSymbolPath")
                {
                    Converter = (System.Windows.Data.IValueConverter)
                        Application.Current.Resources["ImageSourceConverter"]
                });
            imgFactory.SetValue(Image.WidthProperty, 16.0);
            imgFactory.SetValue(Image.HeightProperty, 16.0);
            esTemplate.VisualTree = imgFactory;
            esCol.CellTemplate = esTemplate;
            grid.Columns.Add(esCol);

            grid.Columns.Add(MakeText("Name", "Name", 200));
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Legal",
                SortMemberPath = DeckColumnMarker + "Legal",
                Width = new DataGridLength(100),
                CellTemplate = CreateLegalityPillTemplate()
            });
            grid.Columns.Add(MakeText("SB", "SideboardDisplay", 35));
            grid.Columns.Add(MakeText("Edition", "SetCode", 55));
            grid.Columns.Add(MakeText("Non-Foil", "Quantity", 60, readOnly: false));
            grid.Columns.Add(MakeText("Foil", "FoilQuantity", 50, readOnly: false));
            grid.Columns.Add(MakeText("Total", "TotalQuantity", 50));
            grid.Columns.Add(MakeText("USD", "ValueDisplay", 70));
            grid.Columns.Add(MakeText("Color", "ColorDisplay", 50));
            grid.Columns.Add(MakeText("Type", "TypeLine", 160));
            grid.Columns.Add(MakeText("Rarity", "RarityCode", 50));
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Cost",
                SortMemberPath = DeckColumnMarker + "ManaCost",
                Width = new DataGridLength(110),
                CellTemplate = CreateManaCostTemplate()
            });
            grid.Columns.Add(MakeText("Text", "OracleText", 200));
            grid.Columns.Add(MakeText("Flavor", "FlavorText", 160));
            grid.Columns.Add(MakeText("P/T", "PowerToughness", 55));
            grid.Columns.Add(MakeText("Artist", "Artist", 130));
            grid.Columns.Add(MakeText("Edition Name", "SetName", 160));
            grid.Columns.Add(MakeText("Number", "CollectorNumber", 65));
            grid.Columns.Add(MakeText("Power", "Power", 50));
            grid.Columns.Add(MakeText("Toughness", "Toughness", 70));
            grid.Columns.Add(MakeText("CMC", "ManaValue", 45));
            grid.Columns.Add(MakeText("Row", "RowIndex", 45));
        }

        private void RemoveDeckColumns(DataGrid grid)
        {
            bool hasDeckCols = grid.Columns.Any(c =>
                c.SortMemberPath?.StartsWith(DeckColumnMarker) == true);

            if (!hasDeckCols) return;

            grid.Columns.Clear();
            foreach (var col in _savedPoolColumnsForDeck)
                grid.Columns.Add(col);
            _savedPoolColumnsForDeck.Clear();
        }

        // ════════════════════════════════════════════════════════════════════
        // BOTTOM TABLE LOADERS
        // ════════════════════════════════════════════════════════════════════
        private void LoadBottomTable_Collection()
        {
            using var cdb = new Data.CollectionDbContext();
            using var pdb = new AppDbContext();
            var rows = BuildCollectionRows(cdb, pdb);

            // Apply set code filter from Tab 1
            if (BottomFilterActive)
            {
                if (_bottomSelectedSetCodes.Count > 0)
                    rows = rows
                        .Where(c => _bottomSelectedSetCodes.Contains(c.SetCode))
                        .ToList();

                // Apply expression filter from Filter Window
                if (_bottomFilterNode != null &&
                    FilterExpressionService.HasConditions(_bottomFilterNode))
                {
                    rows = FilterExpressionService.Apply(
                        rows, _bottomFilterNode, true);
                }
            }

            if (BottomFilterActive && _bottomColumnFilters.HasActiveFilters)
                rows = _bottomColumnFilters.Apply(rows);

            for (int i = 0; i < rows.Count; i++)
                rows[i].RowIndex = i;

            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_PlanechaseCollection()
        {
            using var cdb = new CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.PlanarCollectionEntries.AsNoTracking().ToList();
            if (entries.Count == 0) { BottomDataGrid.ItemsSource = null; return; }
            var ids = entries.Select(e => e.PlanarId).ToHashSet();
            var cards = pdb.PlanarCards.AsNoTracking()
                .Where(c => ids.Contains(c.PlanarId))
                .ToList().ToDictionary(c => c.PlanarId);
            var rows = entries
                .Where(ce => cards.ContainsKey(ce.PlanarId))
                .Select(ce => {
                    var pc = cards[ce.PlanarId]; return new CollectionDisplayRow
                    {
                        CollectionEntryId = ce.PlanarCollectionEntryId,
                        Name = pc.Name,
                        SetCode = pc.SetCode,
                        SetName = pc.SetName,
                        CollectorNumber = pc.CollectorNumber,
                        TypeLine = pc.TypeLine,
                        OracleText = pc.OracleText,
                        FlavorText = pc.FlavorText,
                        Artist = pc.Artist,
                        Rarity = pc.Rarity,
                        IsFoil = pc.IsFoil,
                        IsNonFoil = pc.IsNonFoil,
                        ImageNormalUrl = pc.ImageNormalUrl,
                        LocalImagePath = pc.LocalImagePath,
                        Quantity = ce.Quantity,
                        FoilQuantity = ce.FoilQuantity,
                        UsedCount = ce.UsedCount,
                        Condition = ce.Condition,
                        Language = ce.Language,
                        StorageLocation = ce.StorageLocation,
                        Notes = ce.Notes,
                        BuyAt = ce.BuyAt,
                        SellAt = ce.SellAt,
                        SellAtValue = ce.SellAtValue,
                        PriceHigh = ce.PriceHigh,
                        MarketValue = ce.MarketValue,
                        PriceLow = ce.PriceLow,
                        Needed = ce.Needed,
                        Excess = ce.Excess,
                        Target = ce.Target,
                        Desired = ce.Desired,
                        CardGroup = ce.CardGroup,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        DateAdded = ce.DateAdded,
                        DateModified = ce.DateModified
                    };
                })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_ArchenemyCollection()
        {
            using var cdb = new CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.SchemeCollectionEntries.AsNoTracking().ToList();
            if (entries.Count == 0) { BottomDataGrid.ItemsSource = null; return; }
            var ids = entries.Select(e => e.SchemeId).ToHashSet();
            var cards = pdb.SchemeCards.AsNoTracking()
                .Where(c => ids.Contains(c.SchemeId))
                .ToList().ToDictionary(c => c.SchemeId);
            var rows = entries
                .Where(ce => cards.ContainsKey(ce.SchemeId))
                .Select(ce => {
                    var sc = cards[ce.SchemeId]; return new CollectionDisplayRow
                    {
                        CollectionEntryId = ce.SchemeCollectionEntryId,
                        Name = sc.Name,
                        SetCode = sc.SetCode,
                        SetName = sc.SetName,
                        CollectorNumber = sc.CollectorNumber,
                        TypeLine = sc.TypeLine,
                        OracleText = sc.OracleText,
                        FlavorText = sc.FlavorText,
                        Artist = sc.Artist,
                        Rarity = sc.Rarity,
                        IsFoil = sc.IsFoil,
                        IsNonFoil = sc.IsNonFoil,
                        ImageNormalUrl = sc.ImageNormalUrl,
                        LocalImagePath = sc.LocalImagePath,
                        Quantity = ce.Quantity,
                        FoilQuantity = ce.FoilQuantity,
                        UsedCount = ce.UsedCount,
                        Condition = ce.Condition,
                        Language = ce.Language,
                        StorageLocation = ce.StorageLocation,
                        BuyAt = ce.BuyAt,
                        SellAt = ce.SellAt,
                        SellAtValue = ce.SellAtValue,
                        PriceHigh = ce.PriceHigh,
                        MarketValue = ce.MarketValue,
                        PriceLow = ce.PriceLow,
                        Needed = ce.Needed,
                        Excess = ce.Excess,
                        Target = ce.Target,
                        Desired = ce.Desired,
                        CardGroup = ce.CardGroup,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        DateAdded = ce.DateAdded,
                        DateModified = ce.DateModified
                    };
                })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_VanguardCollection()
        {
            using var cdb = new CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.VanguardCollectionEntries.AsNoTracking().ToList();
            if (entries.Count == 0) { BottomDataGrid.ItemsSource = null; return; }
            var ids = entries.Select(e => e.VanguardId).ToHashSet();
            var cards = pdb.VanguardCards.AsNoTracking()
                .Where(c => ids.Contains(c.VanguardId))
                .ToList().ToDictionary(c => c.VanguardId);
            var rows = entries
                .Where(ce => cards.ContainsKey(ce.VanguardId))
                .Select(ce => {
                    var vc = cards[ce.VanguardId]; return new CollectionDisplayRow
                    {
                        CollectionEntryId = ce.VanguardCollectionEntryId,
                        Name = vc.Name,
                        SetCode = vc.SetCode,
                        SetName = vc.SetName,
                        CollectorNumber = vc.CollectorNumber,
                        TypeLine = vc.TypeLine,
                        OracleText = vc.OracleText,
                        FlavorText = vc.FlavorText,
                        Artist = vc.Artist,
                        Rarity = vc.Rarity,
                        IsFoil = vc.IsFoil,
                        IsNonFoil = vc.IsNonFoil,
                        ImageNormalUrl = vc.ImageNormalUrl,
                        LocalImagePath = vc.LocalImagePath,
                        Quantity = ce.Quantity,
                        FoilQuantity = ce.FoilQuantity,
                        UsedCount = ce.UsedCount,
                        Condition = ce.Condition,
                        Language = ce.Language,
                        StorageLocation = ce.StorageLocation,
                        Notes = ce.Notes,
                        BuyAt = ce.BuyAt,
                        SellAt = ce.SellAt,
                        SellAtValue = ce.SellAtValue,
                        PriceHigh = ce.PriceHigh,
                        MarketValue = ce.MarketValue,
                        PriceLow = ce.PriceLow,
                        Needed = ce.Needed,
                        Excess = ce.Excess,
                        Target = ce.Target,
                        Desired = ce.Desired,
                        CardGroup = ce.CardGroup,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        DateAdded = ce.DateAdded,
                        DateModified = ce.DateModified
                    };
                })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_TokenCollection()
        {
            using var cdb = new CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.TokenCollectionEntries.AsNoTracking().ToList();
            if (entries.Count == 0) { BottomDataGrid.ItemsSource = null; return; }
            var ids = entries.Select(e => e.TokenId).ToHashSet();
            var cards = pdb.TokenCards.AsNoTracking()
                .Where(c => ids.Contains(c.TokenId))
                .ToList().ToDictionary(c => c.TokenId);
            var rows = entries
                .Where(ce => cards.ContainsKey(ce.TokenId))
                .Select(ce => {
                    var tc = cards[ce.TokenId]; return new CollectionDisplayRow
                    {
                        CollectionEntryId = ce.TokenCollectionEntryId,
                        Name = tc.Name,
                        SetCode = tc.SetCode,
                        SetName = tc.SetName,
                        CollectorNumber = tc.CollectorNumber,
                        TypeLine = tc.TypeLine,
                        OracleText = tc.OracleText,
                        FlavorText = tc.FlavorText,
                        Artist = tc.Artist,
                        Rarity = tc.Rarity,
                        IsFoil = tc.IsFoil,
                        IsNonFoil = tc.IsNonFoil,
                        ImageNormalUrl = tc.ImageNormalUrl,
                        LocalImagePath = tc.LocalImagePath,
                        Quantity = ce.Quantity,
                        FoilQuantity = ce.FoilQuantity,
                        UsedCount = ce.UsedCount,
                        Condition = ce.Condition,
                        Language = ce.Language,
                        StorageLocation = ce.StorageLocation,
                        BuyAt = ce.BuyAt,
                        SellAt = ce.SellAt,
                        SellAtValue = ce.SellAtValue,
                        PriceHigh = ce.PriceHigh,
                        MarketValue = ce.MarketValue,
                        PriceLow = ce.PriceLow,
                        Needed = ce.Needed,
                        Excess = ce.Excess,
                        Target = ce.Target,
                        Desired = ce.Desired,
                        CardGroup = ce.CardGroup,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        DateAdded = ce.DateAdded,
                        DateModified = ce.DateModified
                    };
                })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_ArtSeriesCollection()
        {
            using var cdb = new CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.ArtSeriesCollectionEntries.AsNoTracking().ToList();
            if (entries.Count == 0) { BottomDataGrid.ItemsSource = null; return; }
            var ids = entries.Select(e => e.ArtSeriesId).ToHashSet();
            var cards = pdb.ArtSeriesCards.AsNoTracking()
                .Where(c => ids.Contains(c.ArtSeriesId))
                .ToList().ToDictionary(c => c.ArtSeriesId);
            var rows = entries
                .Where(ce => cards.ContainsKey(ce.ArtSeriesId))
                .Select(ce => {
                    var ac = cards[ce.ArtSeriesId]; return new CollectionDisplayRow
                    {
                        CollectionEntryId = ce.ArtSeriesCollectionEntryId,
                        Name = ac.Name,
                        SetCode = ac.SetCode,
                        SetName = ac.SetName,
                        CollectorNumber = ac.CollectorNumber,
                        TypeLine = ac.TypeLine,
                        FlavorText = ac.FlavorText,
                        Artist = ac.Artist,
                        Rarity = ac.Rarity,
                        IsFoil = ac.IsFoil,
                        IsNonFoil = ac.IsNonFoil,
                        ImageNormalUrl = ac.ImageNormalUrl,
                        LocalImagePath = ac.LocalImagePath,
                        Quantity = ce.Quantity,
                        FoilQuantity = ce.FoilQuantity,
                        UsedCount = ce.UsedCount,
                        Condition = ce.Condition,
                        Language = ce.Language,
                        StorageLocation = ce.StorageLocation,
                        Notes = ce.Notes,
                        BuyAt = ce.BuyAt,
                        SellAt = ce.SellAt,
                        SellAtValue = ce.SellAtValue,
                        PriceHigh = ce.PriceHigh,
                        MarketValue = ce.MarketValue,
                        PriceLow = ce.PriceLow,
                        Needed = ce.Needed,
                        Excess = ce.Excess,
                        Target = ce.Target,
                        Desired = ce.Desired,
                        CardGroup = ce.CardGroup,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        DateAdded = ce.DateAdded,
                        DateModified = ce.DateModified
                    };
                })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        // ── Shared collection row builder ────────────────────────────────────
        // EF Core cannot join across two database contexts in a single LINQ query,
        // so we load each side into memory first, then join in-process.
        private static List<CollectionDisplayRow> BuildCollectionRows(
    Data.CollectionDbContext cdb, AppDbContext pdb)
        {
            // Load collection entries into memory
            var entries = cdb.CollectionEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return new List<CollectionDisplayRow>();

            // Load only the pool cards we actually need
            var poolIds = entries.Select(e => e.PoolId).Distinct().ToHashSet();
            var poolCards = pdb.PoolCards.AsNoTracking()
                .Where(pc => poolIds.Contains(pc.PoolId))
                .ToList()
                .ToDictionary(pc => pc.PoolId);

            // Join in memory
            var rows = new List<CollectionDisplayRow>(entries.Count);
            foreach (var ce in entries)
            {
                if (!poolCards.TryGetValue(ce.PoolId, out var pc)) continue;
                rows.Add(new CollectionDisplayRow
                {
                    CollectionEntryId = ce.CollectionEntryId,
                    PoolId = pc.PoolId,
                    Name = pc.Name,
                    SetCode = pc.SetCode,
                    SetName = pc.SetName,
                    CollectorNumber = pc.CollectorNumber,
                    ColorIdentity = pc.ColorIdentity,
                    Colors = pc.Colors,
                    TypeLine = pc.TypeLine,
                    ManaCost = pc.ManaCost,
                    ManaValue = pc.ManaValue,
                    Power = pc.Power,
                    Toughness = pc.Toughness,
                    OracleText = pc.OracleText,
                    FlavorText = pc.FlavorText,
                    Artist = pc.Artist,
                    Rarity = pc.Rarity,
                    IsFoil = pc.IsFoil,
                    IsNonFoil = pc.IsNonFoil,
                    ImageNormalUrl = pc.ImageNormalUrl,
                    LocalImagePath = pc.LocalImagePath,
                    ImageBackUrl = pc.ImageBackUrl,
                    LocalImageBackPath = pc.LocalImageBackPath,
                    Quantity = ce.Quantity,
                    FoilQuantity = ce.FoilQuantity,
                    UsedCount = ce.UsedCount,
                    Condition = ce.Condition,
                    Language = ce.Language,
                    StorageLocation = ce.StorageLocation,
                    Notes = ce.Notes,
                    BuyAt = ce.BuyAt,
                    SellAt = ce.SellAt,
                    SellAtValue = ce.SellAtValue,
                    PriceHigh = ce.PriceHigh,
                    MarketValue = pc.PriceUsd ?? ce.MarketValue,
                    PriceLow = ce.PriceLow,
                    Needed = ce.Needed,
                    Excess = ce.Excess,
                    Target = ce.Target,
                    Desired = ce.Desired,
                    CardGroup = ce.CardGroup,
                    PrintType = ce.PrintType,
                    BuyStatus = ce.BuyStatus,
                    SellStatus = ce.SellStatus,
                    IsLegalStandard = pc.IsLegalStandard,
                    IsLegalModern = pc.IsLegalModern,
                    IsLegalPioneer = pc.IsLegalPioneer,
                    IsLegalLegacy = pc.IsLegalLegacy,
                    IsLegalVintage = pc.IsLegalVintage,
                    LegalitiesJson = pc.LegalitiesJson,
                    Keywords = pc.Keywords,
                    DateAdded = ce.DateAdded,
                    DateModified = ce.DateModified,
                    PriceUsd = pc.PriceUsd,
                    PriceUsdFoil = pc.PriceUsdFoil
                });
            }

            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return rows;
        }

        // ════════════════════════════════════════════════════════════════════
        // REFRESH BOTTOM
        // ════════════════════════════════════════════════════════════════════
        private bool _refreshingBottom = false;

        private void RefreshBottom() => RefreshBottom(null, null);

        private void RefreshBottom(double? forcedH, double? forcedV)
        {
            if (_refreshingBottom) return;
            _refreshingBottom = true;

            // Remember if SearchBox had focus so we can restore it
            bool searchHadFocus = SearchBox.IsKeyboardFocused || SearchBox.IsFocused;

            // Commit any in-progress cell edit before refreshing
            BottomDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            // Use pre-captured offsets if provided, otherwise read current position
            _bottomDataGridScroller ??= FindVisualChild<ScrollViewer>(BottomDataGrid);
            double hOffset = forcedH ?? _bottomDataGridScroller?.HorizontalOffset ?? 0;
            double vOffset = forcedV ?? _bottomDataGridScroller?.VerticalOffset ?? 0;
            // Remember selected entry ID and expanded pool IDs before refresh
            int? selectedId = null;
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow sel)
                selectedId = sel.CollectionEntryId;

            var expandedPoolIds = new HashSet<int>();
            if (BottomDataGrid.ItemsSource is List<CollectionDisplayRow> prev)
                foreach (var r in prev.Where(r => r.IsExpanded && !r.IsFooter))
                    expandedPoolIds.Add(r.PoolId);

            // Save sort state — reassigning ItemsSource clears WPF sort descriptions
            var sortDescriptions = BottomDataGrid.Items.SortDescriptions
                .Select(sd => new System.ComponentModel.SortDescription(
                    sd.PropertyName, sd.Direction))
                .ToList();
            var sortedColumns = BottomDataGrid.Columns
                .Where(c => c.SortDirection.HasValue)
                .Select(c => (c.Header?.ToString(), c.SortDirection))
                .ToList();

            // Remember top grid selection so we can restore image after refresh
            var topSelection = TopDataGrid?.SelectedItem;

            switch (_currentMode)
            {
                case "PoolToCollection": LoadBottomTable_Collection(); break;
                case "DeckToCollection": LoadBottomTable_Collection(); break;
                case "PoolToPlanechase": LoadBottomTable_PlanechaseCollection(); break;
                case "PoolToArchenemy": LoadBottomTable_ArchenemyCollection(); break;
                case "PoolToVanguard": LoadBottomTable_VanguardCollection(); break;
                case "PoolToTokens": LoadBottomTable_TokenCollection(); break;
                case "PoolToArtSeries": LoadBottomTable_ArtSeriesCollection(); break;
                case "PoolToConspiracy": LoadBottomTable_ConspiracyCollection(); break;
                case "CollectionToTradeBinder": LoadBottomTable_TradeBinder(); break;
                case "PoolToWantList": LoadBottomTable_WantList(); break;
            }

            // Restore bottom selection
            if (BottomDataGrid.ItemsSource is List<CollectionDisplayRow> rows)
            {
                // Restore sort descriptions
                if (sortDescriptions.Any())
                {
                    foreach (var sd in sortDescriptions)
                        BottomDataGrid.Items.SortDescriptions.Add(sd);
                    // Restore column sort arrow indicators
                    foreach (var col in BottomDataGrid.Columns)
                    {
                        var match = sortedColumns.FirstOrDefault(
                            sc => sc.Item1 == col.Header?.ToString());
                        if (match.Item1 != null)
                            col.SortDirection = match.Item2;
                    }
                    BottomDataGrid.Items.Refresh();
                }
                // Restore selection
                if (selectedId.HasValue)
                {
                    var match = rows.FirstOrDefault(
                        r => r.CollectionEntryId == selectedId.Value);
                    if (match != null)
                        BottomDataGrid.SelectedItem = match;
                    // Don't ScrollIntoView — let scroll restore handle position
                }

                // Restore expanded rows — reload deck usage from current state
                foreach (var r in rows.Where(r => expandedPoolIds.Contains(r.PoolId)))
                {
                    LoadDeckUsageForRow(r);

                    if (r.DeckUsageRows.Count > 0)
                        r.IsExpanded = true;
                }

                // Defer DetailsVisibility until after grid has rendered new rows
                if (expandedPoolIds.Count > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        foreach (var r in rows.Where(r => r.IsExpanded))
                        {
                            if (BottomDataGrid.ItemContainerGenerator
                                .ContainerFromItem(r) is DataGridRow dgRow)
                                dgRow.DetailsVisibility = Visibility.Visible;
                        }
                    }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            // Restore top grid card image/detail if top was in focus
            if (topSelection != null && !_bottomTableHasFocus)
                _ = HandleSelectionAsync(topSelection);

            // Restore scroll position after layout
            if (hOffset > 0 || vOffset > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _bottomDataGridScroller ??= FindVisualChild<ScrollViewer>(BottomDataGrid);
                    _bottomDataGridScroller?.ScrollToHorizontalOffset(hOffset);
                    _bottomDataGridScroller?.ScrollToVerticalOffset(vOffset);
                    _bottomSummaryScroller ??= FindVisualChild<ScrollViewer>(BottomSummaryGrid);
                    _bottomSummaryScroller?.ScrollToHorizontalOffset(hOffset);
                }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }

            if (searchHadFocus)
                Dispatcher.BeginInvoke(new Action(() => SearchBox.Focus()),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            else
                RestoreFocus();
            _refreshingBottom = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // TOOLBAR STATE
        // ════════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════════
        // COLUMN LAYOUT — VISIBILITY & ORDER
        // ════════════════════════════════════════════════════════════════════

        private bool _suppressLayoutSave = false;

        private void SaveColumnLayout(DataGrid grid, string tableKey)
        {
            if (_suppressLayoutSave) return;
            // Visibility: dict of header→bool
            var vis = new Dictionary<string, bool>();
            var order = new Dictionary<string, int>();
            foreach (var col in grid.Columns)
            {
                string hdr = col.Header?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(hdr)) continue;
                vis[hdr] = col.Visibility == Visibility.Visible;
                order[hdr] = col.DisplayIndex;
            }
            SaveSetting(ColVisPrefix + tableKey,
                System.Text.Json.JsonSerializer.Serialize(vis));
            SaveSetting(ColOrderPrefix + tableKey,
                System.Text.Json.JsonSerializer.Serialize(order));
        }

        private void RestoreColumnLayout(DataGrid grid, string tableKey)
        {
            _suppressLayoutSave = true;
            try
            {
                var visJson = GetSetting(ColVisPrefix + tableKey);
                var orderJson = GetSetting(ColOrderPrefix + tableKey);

                if (!string.IsNullOrEmpty(visJson))
                {
                    try
                    {
                        var vis = System.Text.Json.JsonSerializer
                            .Deserialize<Dictionary<string, bool>>(visJson);
                        if (vis != null)
                            foreach (var col in grid.Columns)
                            {
                                string hdr = col.Header?.ToString() ?? string.Empty;
                                if (vis.TryGetValue(hdr, out bool show))
                                    col.Visibility = show
                                        ? Visibility.Visible
                                        : Visibility.Collapsed;
                            }
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(orderJson))
                {
                    try
                    {
                        var order = System.Text.Json.JsonSerializer
                            .Deserialize<Dictionary<string, int>>(orderJson);
                        if (order != null)
                        {
                            // Apply in ascending display-index order to avoid conflicts
                            var sorted = order.OrderBy(kv => kv.Value).ToList();
                            foreach (var (hdr, idx) in sorted)
                            {
                                var col = grid.Columns.FirstOrDefault(
                                    c => c.Header?.ToString() == hdr);
                                if (col != null)
                                {
                                    int safeIdx = Math.Min(idx,
                                        grid.Columns.Count - 1);
                                    if (col.DisplayIndex != safeIdx)
                                        col.DisplayIndex = safeIdx;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                _suppressLayoutSave = false;
            }
        }

        private void AutoSizeColumnsToHeader(DataGrid grid, string tableKey)
        {
            // Skip if user already has a saved layout
            if (!string.IsNullOrEmpty(GetSetting(ColOrderPrefix + tableKey)))
                return;

            // Size each column to fit its header text
            foreach (var col in grid.Columns)
            {
                var hdr = col.Header?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(hdr) || hdr == "ES" || hdr == "↕")
                    continue;
                col.Width = new DataGridLength(1, DataGridLengthUnitType.SizeToHeader);
            }

            grid.UpdateLayout();

            // Freeze the measured widths so columns stay resizable
            foreach (var col in grid.Columns)
            {
                var hdr = col.Header?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(hdr) || hdr == "ES" || hdr == "↕")
                    continue;
                if (col.ActualWidth > 0)
                    col.Width = new DataGridLength(col.ActualWidth);
            }
        }

        private void WireColumnLayoutSave(DataGrid grid, string tableKey)
        {
            grid.ColumnDisplayIndexChanged += (s, e) =>
                SaveColumnLayout(grid, tableKey);
        }

        private void ShowColumnChooser(DataGrid grid, string tableKey,
            UIElement anchor)
        {
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Slide
            };

            var border = new Border
            {
                Background = (System.Windows.Media.Brush)
                    FindResource("SurfaceBrush"),
                BorderBrush = (System.Windows.Media.Brush)
                    FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                MinWidth = 320,
                MaxHeight = 500
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var outer = new StackPanel { Margin = new Thickness(2) };

            // (All) checkbox
            var allCb = new CheckBox
            {
                Content = "(All)",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2, 2, 2, 6),
                IsChecked = grid.Columns.All(c =>
                    c.Visibility == Visibility.Visible)
            };

            // Two-column WrapPanel for individual columns
            var wrap = new WrapPanel
            {
                Orientation = Orientation.Vertical,
                MaxHeight = 400
            };

            // Skip the chooser-button column itself (no header text)
            var colsToShow = grid.Columns
                .Where(c => !string.IsNullOrEmpty(c.Header?.ToString()))
                .ToList();

            var checkboxes = new List<CheckBox>();
            foreach (var col in colsToShow)
            {
                var cb = new CheckBox
                {
                    Content = col.Header?.ToString(),
                    IsChecked = col.Visibility == Visibility.Visible,
                    Margin = new Thickness(4, 2, 12, 2),
                    Width = 130,
                    Tag = col
                };
                cb.Checked += (s, e) => {
                    ((DataGridColumn)((CheckBox)s!).Tag).Visibility =
                        Visibility.Visible;
                    allCb.IsChecked = checkboxes.All(c => c.IsChecked == true);
                    SaveColumnLayout(grid, tableKey);
                };
                cb.Unchecked += (s, e) => {
                    ((DataGridColumn)((CheckBox)s!).Tag).Visibility =
                        Visibility.Collapsed;
                    allCb.IsChecked = false;
                    SaveColumnLayout(grid, tableKey);
                };
                checkboxes.Add(cb);
                wrap.Children.Add(cb);
            }

            allCb.Checked += (s, e) => {
                foreach (var cb in checkboxes) cb.IsChecked = true;
            };
            allCb.Unchecked += (s, e) => {
                foreach (var cb in checkboxes) cb.IsChecked = false;
            };

            outer.Children.Add(allCb);
            outer.Children.Add(wrap);
            scroll.Content = outer;
            border.Child = scroll;
            popup.Child = border;
            popup.IsOpen = true;
        }

        private void UpdateToolbarState()
        {
            bool isDeckMode = _currentMode == "PoolToDeck" ||
                              _currentMode == "CollectionToDeck" ||
                              _currentMode == "DeckToCollection";
            bool isBinderMode = _currentMode == "CollectionToTradeBinder" ||
                                _currentMode == "PoolToWantList";
            bool hasDeck = _activeDeck != null;
            bool anyDecks = _openDecks.Any();
            bool hasTopSel = TopDataGrid?.SelectedItem != null;
            bool hasBottomSel = BottomDataGrid?.SelectedItem != null;
            bool hasDeckSel = hasDeck && GetActiveDeckGrid()?.SelectedItem != null;

            bool canFoil = false;
            bool canNonFoil = false;

            if (hasTopSel && TopDataGrid?.SelectedItem != null)
            {
                if (TopDataGrid.SelectedItem is PoolCard pc)
                { canFoil = pc.IsFoil; canNonFoil = pc.IsNonFoil; }
                else if (TopDataGrid.SelectedItem is PlanarCard pl)
                { canFoil = pl.IsFoil; canNonFoil = pl.IsNonFoil; }
                else if (TopDataGrid.SelectedItem is SchemeCard sc)
                { canFoil = sc.IsFoil; canNonFoil = sc.IsNonFoil; }
                else if (TopDataGrid.SelectedItem is VanguardCard vc)
                { canFoil = vc.IsFoil; canNonFoil = vc.IsNonFoil; }
                else if (TopDataGrid.SelectedItem is TokenCard tc)
                { canFoil = tc.IsFoil; canNonFoil = tc.IsNonFoil; }
                else if (TopDataGrid.SelectedItem is ArtSeriesCard ac)
                { canFoil = ac.IsFoil; canNonFoil = ac.IsNonFoil; }
                else if (TopDataGrid.SelectedItem is CollectionDisplayRow cr)
                { canFoil = cr.IsFoil; canNonFoil = cr.IsNonFoil; }
            }

            // ── Group 1: Deck file buttons ────────────────────────────────────────
            BtnNewDeck.IsEnabled = isDeckMode;
            BtnOpenDeck.IsEnabled = isDeckMode;
            BtnSaveDeck.IsEnabled = isDeckMode && hasDeck;
            BtnSaveAllDecks.IsEnabled = isDeckMode && anyDecks;
            BtnCloseDeck.IsEnabled = isDeckMode && hasDeck;
            BtnCloseAllDecks.IsEnabled = isDeckMode && anyDecks;
            BtnDeckProperties.IsEnabled = isDeckMode && hasDeck;
            BtnDeckLegality.IsEnabled = hasDeck;
            BtnDeckStats.IsEnabled = isDeckMode && hasDeck;

            // ── Group 2: Deck card buttons ────────────────────────────────────────
            BtnAddToDeck.IsEnabled = isDeckMode && hasTopSel && hasDeck && canNonFoil;
            BtnAdd4ToDeck.IsEnabled = isDeckMode && hasTopSel && hasDeck && canNonFoil &&
                                           _activeDeck != null &&
                                           _activeDeck.DeckType == DeckType.Standard;
            BtnAddFoilToDeck.IsEnabled = isDeckMode && hasTopSel && hasDeck && canFoil;
            BtnRemoveFromDeck.IsEnabled = isDeckMode && hasDeckSel;
            BtnDeckQtyIncrease.IsEnabled = isDeckMode && hasDeckSel;
            BtnDeckQtyDecrease.IsEnabled = isDeckMode && hasDeckSel;

            // ── Group 3: Collection buttons ───────────────────────────────────────
            bool isDeckToCollection = _currentMode == "DeckToCollection";
            bool canAddToBottom = !isDeckMode || isDeckToCollection || isBinderMode;
            BtnAddToCollection.IsEnabled = hasTopSel && canNonFoil && canAddToBottom;
            BtnAddFoilToCollection.IsEnabled = hasTopSel && canFoil && canAddToBottom;
            BtnRemoveFromCollection.IsEnabled = hasBottomSel && (!isDeckMode || isBinderMode);
            BtnImportDeckToCollection.IsEnabled = isDeckToCollection && hasDeck;

            // ── Menu items ────────────────────────────────────────────────────────
            if (MenuNewDeck != null) MenuNewDeck.IsEnabled = isDeckMode;
            if (MenuOpenDeck != null) MenuOpenDeck.IsEnabled = isDeckMode;
            if (MenuSaveDeck != null) MenuSaveDeck.IsEnabled = isDeckMode && hasDeck;
            if (MenuSaveAllDecks != null) MenuSaveAllDecks.IsEnabled = isDeckMode && anyDecks;
            if (MenuCloseDeck != null) MenuCloseDeck.IsEnabled = isDeckMode && hasDeck;
            if (MenuCloseAllDecks != null) MenuCloseAllDecks.IsEnabled = isDeckMode && anyDecks;
        }

        // ════════════════════════════════════════════════════════════════════
        // SELECTION HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void TopDataGrid_CellEditEnding(object sender,
            DataGridCellEditEndingEventArgs e)
        {
            // Only save collection edits in CollectionToDeck mode
            if (_currentMode != "CollectionToDeck") return;
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not CollectionDisplayRow row || row.IsFooter) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    using var db = new Data.CollectionDbContext();
                    var entry = db.CollectionEntries
                        .FirstOrDefault(c => c.CollectionEntryId == row.CollectionEntryId);
                    if (entry == null) return;

                    entry.Quantity = row.Quantity;
                    entry.FoilQuantity = row.FoilQuantity;
                    entry.Condition = row.Condition;
                    entry.Notes = row.Notes;
                    entry.StorageLocation = row.StorageLocation;
                    entry.BuyAt = row.BuyAt;
                    entry.SellAt = row.SellAt;
                    entry.SellAtValue = row.SellAtValue;
                    entry.PriceHigh = row.PriceHigh;
                    entry.MarketValue = row.MarketValue;
                    entry.PriceLow = row.PriceLow;
                    entry.Needed = row.Needed;
                    entry.Excess = row.Excess;
                    entry.Target = row.Target;
                    entry.Desired = row.Desired;
                    entry.CardGroup = row.CardGroup;
                    entry.PrintType = row.PrintType;
                    entry.BuyStatus = row.BuyStatus;
                    entry.SellStatus = row.SellStatus;
                    entry.DateModified = DateTime.Now;

                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Save failed: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }),
            System.Windows.Threading.DispatcherPriority.Background);
        }

        private void TopDataGrid_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_bottomTableHasFocus)
            {
                _bottomTableHasFocus = false;
                SearchBox.Clear();
                _bottomSearch = string.Empty;
                _searchText = string.Empty;
            }
        }

        private void BottomDataGrid_CellEditEnding(object sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not CollectionDisplayRow row || row.IsFooter) return;

            // Route to appropriate save method based on mode
            if (_currentMode == "CollectionToTradeBinder")
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    SaveTradeBinderRow(row)),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            if (_currentMode == "PoolToWantList")
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    SaveWantListRow(row)),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            // Capture scroll position NOW before the deferred invoke
            _bottomDataGridScroller ??= FindVisualChild<ScrollViewer>(BottomDataGrid);
            double savedH = _bottomDataGridScroller?.HorizontalOffset ?? 0;
            double savedV = _bottomDataGridScroller?.VerticalOffset ?? 0;
            string editedCol = (e.Column as System.Windows.Controls.DataGridBoundColumn)
                ?.Binding is System.Windows.Data.Binding b ? b.Path?.Path ?? ""
                : e.Column?.Header?.ToString() ?? "";
            bool isQtyEdit = editedCol is "Quantity" or "FoilQuantity";

            // For currency display columns, parse the edited text back to decimal
            if (editedCol is "BuyAtDisplay" or "SellAtDisplay")
            {
                if (e.EditingElement is System.Windows.Controls.TextBox tb)
                {
                    string raw = tb.Text.Replace("$", "").Trim();
                    if (decimal.TryParse(raw, out decimal val))
                    {
                        if (editedCol == "BuyAtDisplay") row.BuyAt = val;
                        if (editedCol == "SellAtDisplay") row.SellAt = val;
                    }
                    else
                    {
                        if (editedCol == "BuyAtDisplay") row.BuyAt = null;
                        if (editedCol == "SellAtDisplay") row.SellAt = null;
                    }
                }
            }

            // Save after the binding commits (slight delay)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    using var db = new Data.CollectionDbContext();
                    var entry = db.CollectionEntries
                        .FirstOrDefault(c => c.CollectionEntryId == row.CollectionEntryId);
                    if (entry == null) return;

                    entry.Quantity = row.Quantity;
                    entry.FoilQuantity = row.FoilQuantity;
                    entry.Condition = row.Condition;
                    entry.Language = row.Language;
                    entry.Notes = row.Notes;
                    entry.StorageLocation = row.StorageLocation;
                    entry.BuyAt = row.BuyAt;
                    entry.SellAt = row.SellAt;
                    entry.SellAtValue = row.SellAtValue;
                    entry.PriceHigh = row.PriceHigh;
                    entry.MarketValue = row.MarketValue;
                    entry.PriceLow = row.PriceLow;
                    entry.Needed = row.Needed;
                    entry.Excess = row.Excess;
                    entry.Target = row.Target;
                    entry.Desired = row.Desired;
                    entry.CardGroup = row.CardGroup;
                    entry.PrintType = row.PrintType;
                    entry.BuyStatus = row.BuyStatus;
                    entry.SellStatus = row.SellStatus;
                    entry.DateModified = DateTime.Now;
                    db.SaveChanges();

                    // Only do full refresh if quantity changed (affects Available)
                    // For metadata edits just update the summary row — no scroll reset
                    if (isQtyEdit)
                        RefreshBottom(savedH, savedV);
                    else
                        UpdateSummaryRow(
                            BottomDataGrid.ItemsSource as
                            System.Collections.Generic.List<CollectionDisplayRow>
                            ?? new System.Collections.Generic.List<CollectionDisplayRow>());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Save failed: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }),
            System.Windows.Threading.DispatcherPriority.Background);
        }

        private ScrollViewer? _bottomDataGridScroller;
        private ScrollViewer? _bottomSummaryScroller;

        private void BottomSummaryGrid_Loaded(object sender, RoutedEventArgs e)
        {
            _bottomSummaryScroller = FindVisualChild<ScrollViewer>(BottomSummaryGrid);
            _bottomDataGridScroller ??= FindVisualChild<ScrollViewer>(BottomDataGrid);
        }

        private void BottomDataGrid_ScrollChanged(object sender,
            ScrollChangedEventArgs e)
        {
            if (e.HorizontalChange == 0) return;
            _bottomSummaryScroller ??= FindVisualChild<ScrollViewer>(BottomSummaryGrid);
            _bottomSummaryScroller?.ScrollToHorizontalOffset(e.HorizontalOffset);
        }

        private static T? FindVisualChild<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper
                .GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void BottomDataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            _bottomDataGridScroller ??= FindVisualChild<ScrollViewer>(BottomDataGrid);
        }

        private void BottomDataGrid_GotFocus(object sender, RoutedEventArgs e)
        {
            if (!_bottomTableHasFocus)
            {
                _bottomTableHasFocus = true;
                SearchBox.Clear();
                _bottomSearch = string.Empty;
                _searchText = string.Empty;
            }
        }

        // Returns keyboard focus to whichever grid the user was last in
        private void RestoreFocus()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // If SearchBox has focus, leave it there
                if (SearchBox.IsKeyboardFocused || SearchBox.IsFocused)
                    return;

                var grid = _bottomTableHasFocus ? BottomDataGrid : TopDataGrid;
                grid.Focus();

                // Focus the selected cell so arrow keys navigate rows
                if (grid.SelectedItem != null)
                {
                    grid.ScrollIntoView(grid.SelectedItem);
                    var row = grid.ItemContainerGenerator
                        .ContainerFromItem(grid.SelectedItem) as DataGridRow;
                    if (row != null)
                    {
                        var cell = GetFirstCell(row);
                        cell?.Focus();
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private static DataGridCell? GetFirstCell(DataGridRow row)
        {
            var presenter = GetVisualChild<System.Windows.Controls.Primitives
                .DataGridCellsPresenter>(row);
            if (presenter == null) return null;
            return presenter.ItemContainerGenerator
                .ContainerFromIndex(0) as DataGridCell;
        }

        private void BottomDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter in CollectionToDeck mode — add to deck
            if (e.Key == Key.Enter &&
                _currentMode == "CollectionToDeck" &&
                BottomDataGrid.SelectedItem is CollectionDisplayRow row && !row.IsFooter)
            {
                AddCardToActiveDeck(row);
                e.Handled = true;
                return;
            }

            // Delete key — confirm before removing from collection
            if (e.Key == Key.Delete &&
                BottomDataGrid.SelectedItem is CollectionDisplayRow delRow && !delRow.IsFooter)
            {
                var result = MessageBox.Show(
                    $"Remove all copies of '{delRow.Name}' from your collection?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                    RemoveFromCollection(delRow, delRow.Quantity + delRow.FoilQuantity, true);

                e.Handled = true;
            }
        }

        private async void TopDataGrid_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            UpdateToolbarState();
            await HandleSelectionAsync(TopDataGrid.SelectedItem);
        }

        private async void BottomDataGrid_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            UpdateToolbarState();
            await HandleSelectionAsync(BottomDataGrid.SelectedItem);
        }

        private async Task HandleSelectionAsync(object? item)
        {
            if (item == null) { ClearDetailPanel(); return; }

            switch (item)
            {
                case PoolCard pc:
                    ShowPoolCardDetail(pc);
                    await LoadCardImageAsync(pc.LocalImagePath, pc.ImageNormalUrl,
                        pc.LocalImageBackPath, pc.ImageBackUrl);
                    break;
                case TokenCard tc:
                    ShowTokenCardDetail(tc);
                    await LoadCardImageAsync(tc.LocalImagePath, tc.ImageNormalUrl);
                    break;
                case PlanarCard pl:
                    ShowPlanarCardDetail(pl);
                    await LoadCardImageAsync(pl.LocalImagePath, pl.ImageNormalUrl);
                    break;
                case SchemeCard sc:
                    ShowSchemeCardDetail(sc);
                    await LoadCardImageAsync(sc.LocalImagePath, sc.ImageNormalUrl);
                    break;
                case VanguardCard vc:
                    ShowVanguardCardDetail(vc);
                    await LoadCardImageAsync(vc.LocalImagePath, vc.ImageNormalUrl);
                    break;
                case ArtSeriesCard ac:
                    ShowArtSeriesCardDetail(ac);
                    await LoadCardImageAsync(ac.LocalImagePath, ac.ImageNormalUrl);
                    break;
                case ConspiracyCard cc:
                    ShowConspiracyCardDetail(cc);
                    await LoadCardImageAsync(cc.LocalImagePath, cc.ImageNormalUrl);
                    break;
                case CollectionDisplayRow cr:
                    ShowCollectionRowDetail(cr);
                    await LoadCardImageAsync(cr.LocalImagePath, cr.ImageNormalUrl,
                        cr.LocalImageBackPath, cr.ImageBackUrl);
                    break;
                case DeckCard dc:
                    ShowDeckCardDetail(dc);
                    await LoadCardImageAsync(dc.LocalImagePath, dc.ImageNormalUrl,
                        dc.LocalImageBackPath, dc.ImageBackUrl);
                    break;
                default:
                    ClearDetailPanel();
                    break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ENTER KEY — PreviewKeyDown fixes WPF DataGrid capture issue
        // ════════════════════════════════════════════════════════════════════
        private void TopDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle deck modes
            if (_currentMode == "PoolToDeck" || _currentMode == "CollectionToDeck")
            {
                if (e.Key != Key.Enter) return;
                bool deckShift = Keyboard.IsKeyDown(Key.LeftShift) ||
                                 Keyboard.IsKeyDown(Key.RightShift);
                if (TopDataGrid.SelectedItem is PoolCard pc)
                    AddCardToActiveDeck(pc, foil: deckShift);
                else if (TopDataGrid.SelectedItem is CollectionDisplayRow cr)
                    AddCardToActiveDeck(cr);
                e.Handled = true;
                RestoreFocus();
                return;
            }

            // Deck → Collection mode
            if (_currentMode == "DeckToCollection")
            {
                if (e.Key != Key.Enter) return;
                if (TopDataGrid.SelectedItem is DeckCard dc)
                    AddDeckCardToCollection(dc);
                e.Handled = true;
                RestoreFocus();
                return;
            }

            if (e.Key != Key.Enter || _bottomLocked) return;

            bool shift = Keyboard.IsKeyDown(Key.LeftShift) ||
                         Keyboard.IsKeyDown(Key.RightShift);

            bool handled = false;

            switch (TopDataGrid.SelectedItem)
            {
                case PoolCard pc:
                    if (shift && pc.IsFoil)
                    { AddToPoolCollection(pc.PoolId, pc.Name, 1, true); handled = true; }
                    else if (!shift && pc.IsNonFoil)
                    { AddToPoolCollection(pc.PoolId, pc.Name, 1, false); handled = true; }
                    else if (pc.IsFoil)
                    { AddToPoolCollection(pc.PoolId, pc.Name, 1, true); handled = true; }
                    break;

                case PlanarCard pl:
                    AddToSpecialCollection("Planechase", pl.PlanarId,
                        pl.Name, 1, shift && pl.IsFoil);
                    handled = true;
                    break;

                case SchemeCard sc:
                    AddToSpecialCollection("Archenemy", sc.SchemeId,
                        sc.Name, 1, shift && sc.IsFoil);
                    handled = true;
                    break;

                case VanguardCard vc:
                    AddToSpecialCollection("Vanguard", vc.VanguardId,
                        vc.Name, 1, false);
                    handled = true;
                    break;

                case TokenCard tc:
                    AddToSpecialCollection("Token", tc.TokenId,
                        tc.Name, 1, shift && tc.IsFoil);
                    handled = true;
                    break;

                case ArtSeriesCard ac:
                    AddToSpecialCollection("ArtSeries", ac.ArtSeriesId,
                        ac.Name, 1, shift && ac.IsFoil);
                    handled = true;
                    break;
                case ConspiracyCard cc:
                    AddToSpecialCollection("Conspiracy", cc.ConspiracyId,
                        cc.Name, 1, shift && cc.IsFoil);
                    handled = true;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
                RestoreFocus();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // SEARCH
        // ════════════════════════════════════════════════════════════════════
        // ── Search debounce timer ─────────────────────────────────────────────────
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchDebounceTimer.Tick += (s, args) =>
            {
                _searchDebounceTimer.Stop();

                if (_bottomTableHasFocus)
                {
                    _lastSearchTerm = SearchBox.Text.Trim();
                    _searchMatches.Clear();
                    _searchMatchIndex = -1;

                    // Build match list from bottom table WITHOUT filtering
                    // Sort: exact match first, then StartsWith, then Contains
                    if (!string.IsNullOrEmpty(_lastSearchTerm) &&
                        BottomDataGrid.ItemsSource is
                            System.Collections.IEnumerable bottomItems)
                    {
                        var exact = new List<object>();
                        var startsWith = new List<object>();
                        var contains = new List<object>();
                        foreach (var item in bottomItems)
                        {
                            string? name = item.GetType()
                                .GetProperty("Name")?.GetValue(item)?.ToString();
                            if (name == null) continue;
                            if (name.Equals(_lastSearchTerm,
                                    StringComparison.OrdinalIgnoreCase))
                                exact.Add(item);
                            else if (name.StartsWith(_lastSearchTerm,
                                    StringComparison.OrdinalIgnoreCase))
                                startsWith.Add(item);
                            else if (name.Contains(_lastSearchTerm,
                                    StringComparison.OrdinalIgnoreCase))
                                contains.Add(item);
                        }
                        _searchMatches.AddRange(exact);
                        _searchMatches.AddRange(startsWith);
                        _searchMatches.AddRange(contains);
                    }

                    // Scroll to first match
                    if (_searchMatches.Count > 0)
                    {
                        _searchMatchIndex = 0;
                        var first = _searchMatches[0];
                        BottomDataGrid.SelectedItem = first;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            BottomDataGrid.UpdateLayout();
                            BottomDataGrid.ScrollIntoView(first);
                        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                    }
                }
                else
                {
                    _lastSearchTerm = SearchBox.Text.Trim();
                    _searchMatches.Clear();
                    _searchMatchIndex = -1;

                    // Build match list from top table WITHOUT filtering
                    // Sort: exact match first, then StartsWith, then Contains
                    if (!string.IsNullOrEmpty(_lastSearchTerm) &&
                        TopDataGrid.ItemsSource is
                            System.Collections.IEnumerable topItems)
                    {
                        var exact = new List<object>();
                        var startsWith = new List<object>();
                        var contains = new List<object>();
                        foreach (var item in topItems)
                        {
                            string? name = item.GetType()
                                .GetProperty("Name")?.GetValue(item)?.ToString();
                            if (name == null) continue;
                            if (name.Equals(_lastSearchTerm,
                                    StringComparison.OrdinalIgnoreCase))
                                exact.Add(item);
                            else if (name.StartsWith(_lastSearchTerm,
                                    StringComparison.OrdinalIgnoreCase))
                                startsWith.Add(item);
                            else if (name.Contains(_lastSearchTerm,
                                    StringComparison.OrdinalIgnoreCase))
                                contains.Add(item);
                        }
                        _searchMatches.AddRange(exact);
                        _searchMatches.AddRange(startsWith);
                        _searchMatches.AddRange(contains);
                    }

                    // Scroll to first match — position at top of visible area
                    if (_searchMatches.Count > 0)
                    {
                        _searchMatchIndex = 0;
                        var first = _searchMatches[0];
                        TopDataGrid.SelectedItem = first;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            TopDataGrid.UpdateLayout();
                            // Scroll to bottom first, then to match — forces it to top
                            if (TopDataGrid.Items.Count > 0)
                                TopDataGrid.ScrollIntoView(
                                    TopDataGrid.Items[TopDataGrid.Items.Count - 1]);
                            TopDataGrid.UpdateLayout();
                            TopDataGrid.ScrollIntoView(first);
                            // Keep focus on the search box so Enter still works
                            SearchBox.Focus();
                        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                    }
                }

            };
            _searchDebounceTimer.Start();
        }

        // ════════════════════════════════════════════════════════════════════
        // FILTER HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void ChkTopFilter_Changed(object sender, RoutedEventArgs e)
        {
            LoadCurrentMode();
            string summary = BuildFilterSummary(isTop: true);
            TopFilterSummary.Text = (ChkTopFilter.IsChecked == false && summary != "No filter active")
                ? "(filter suspended — " + summary + ")"
                : summary;
        }

        private void ChkBottomFilter_Changed(object sender, RoutedEventArgs e)
        {
            RefreshBottom();
            string summary = BuildFilterSummary(isTop: false);
            BottomFilterSummary.Text = (ChkBottomFilter.IsChecked == false && summary != "No filter active")
                ? "(filter suspended — " + summary + ")"
                : summary;
        }

        /// <summary>Build a complete filter summary string from ALL active sources.</summary>
        private string BuildFilterSummary(bool isTop)
        {
            var parts = new List<string>();

            // Expression filter
            var node = isTop ? _topFilterNode : _bottomFilterNode;
            var exprSummary = FilterExpressionService.Summarize(node);
            if (!string.IsNullOrEmpty(exprSummary))
                parts.Add(exprSummary);

            // Edition/set filter
            var setCodes = isTop ? _topSelectedSetCodes : _bottomSelectedSetCodes;
            if (setCodes.Count > 0)
                parts.Add($"Editions: {setCodes.Count} selected");

            // Color quick-filter
            var fs = isTop ? _topFilter : _bottomFilter;
            if (fs.FilterWhite || fs.FilterBlue || fs.FilterBlack ||
                fs.FilterRed || fs.FilterGreen || fs.FilterColorless)
                parts.Add("🎨 Color filter active");

            // Column filters — just show which columns are active
            var colFilters = isTop ? _topColumnFilters : _bottomColumnFilters;
            foreach (var f in colFilters.GetActiveFilters())
            {
                if (f.UseTextFilter)
                    parts.Add($"{f.ColumnName}: \"{f.TextValue}\"");
                else if (!f.AllSelected)
                    parts.Add($"{f.ColumnName} filter active");
            }

            return parts.Count > 0
                ? string.Join("  •  ", parts)
                : "No filter active";
        }

        private void BtnTopFilterClear_Click(object sender, RoutedEventArgs e)
        {
            _topFilter.Clear();
            _topFilterNode = null;
            _topSelectedSetCodes.Clear();
            _topColumnFilters.ClearAll();
            ChkTopFilter.IsChecked = false;
            TopFilterSummary.Text = "No filter active";
            ResetAllFunnelIcons(TopDataGrid);
            LoadCurrentMode();
        }

        private void BtnBottomFilterClear_Click(object sender, RoutedEventArgs e)
        {
            _bottomFilter.Clear();
            _bottomFilterNode = null;
            _bottomSelectedSetCodes.Clear();
            _bottomColumnFilters.ClearAll();
            ChkBottomFilter.IsChecked = false;
            BottomFilterSummary.Text = "No filter active";
            ResetAllFunnelIcons(BottomDataGrid);
            RefreshBottom();
        }

        private void BtnTopFilterCustomize_Click(object sender, RoutedEventArgs e)
            => OpenFilterWindow(top: true);

        private void BtnBottomFilterCustomize_Click(object sender, RoutedEventArgs e)
            => OpenFilterWindow(top: false);

        private void BtnFilter_Click(object sender, RoutedEventArgs e)
            => OpenFilterWindow(top: !_bottomTableHasFocus);

        private FilterNode? _topFilterNode = null;
        private FilterNode? _bottomFilterNode = null;
        private GridColumnFilters _topColumnFilters = new();
        private GridColumnFilters _bottomColumnFilters = new();
        private ColumnFilterPopup? _activeFilterPopup = null;

        private void OpenFilterWindow(bool top)
        {
            var context = top
                ? ModeToFilterContext(_currentMode, top: true)
                : ModeToFilterContext(_currentMode, top: false);

            var win = new BreakersOfE.Windows.FilterWindow(
                context,
                top ? _topFilterNode : _bottomFilterNode)
            { Owner = this };

            win.ApplyRequested += (s, e) =>
            {
                if (top) { _topFilterNode = win.ResultNode; LoadCurrentMode(); }
                else { _bottomFilterNode = win.ResultNode; RefreshBottom(); }
            };

            if (win.ShowDialog() == true)
            {
                if (top)
                {
                    _topFilterNode = win.ResultNode;
                    _topSelectedSetCodes = win.SelectedSetCodes;
                    ApplyColorQuickFilter(_topFilter, win);

                    string summary = BuildFilterSummary(isTop: true);
                    TopFilterSummary.Text = summary;
                    ChkTopFilter.IsChecked = summary != "No filter active";
                    LoadCurrentMode();
                }
                else
                {
                    _bottomFilterNode = win.ResultNode;
                    _bottomSelectedSetCodes = win.SelectedSetCodes;
                    ApplyColorQuickFilter(_bottomFilter, win);

                    string summary = BuildFilterSummary(isTop: false);
                    BottomFilterSummary.Text = summary;
                    ChkBottomFilter.IsChecked = summary != "No filter active";
                    RefreshBottom();
                }
            }
        }

        private static void ApplyColorQuickFilter(
            FilterState state, Windows.FilterWindow win)
        {
            if (!win.QuickColorActive)
            {
                // Clear color fields only
                state.FilterWhite = state.FilterBlue = state.FilterBlack =
                state.FilterRed = state.FilterGreen = state.FilterColorless = false;
                state.ColorMatch = ColorMatchMode.AtMost;
                return;
            }
            state.FilterWhite = win.QuickFilterW;
            state.FilterBlue = win.QuickFilterU;
            state.FilterBlack = win.QuickFilterB;
            state.FilterRed = win.QuickFilterR;
            state.FilterGreen = win.QuickFilterG;
            state.FilterColorless = win.QuickFilterC;
            state.ColorMatch = win.QuickColorMode;
        }

        private static FilterContext ModeToFilterContext(
            string mode, bool top)
        {
            if (!top) return FilterContext.Collection;
            return mode switch
            {
                "PoolToCollection" => FilterContext.Pool,
                "PoolToPlanechase" => FilterContext.Planechase,
                "PoolToArchenemy" => FilterContext.Archenemy,
                "PoolToVanguard" => FilterContext.Vanguard,
                "PoolToTokens" => FilterContext.Tokens,
                "PoolToArtSeries" => FilterContext.ArtSeries,
                "PoolToConspiracy" => FilterContext.Conspiracy,
                _ => FilterContext.Pool
            };
        }
        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (TopDataGrid.SelectedItem == null) return;

            // Enter adds the selected card to collection or deck
            bool isDeckMode = _currentMode == "PoolToDeck" ||
                              _currentMode == "CollectionToDeck" ||
                              _currentMode == "DeckToCollection";

            if (isDeckMode)
                AddFromTopSelectionToDeck(foil: false);
            else
                AddFromTopSelection(foil: false, qty: 1);

            e.Handled = true;

            RestoreFocus();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            var win = new BreakersOfE.Windows.SearchForCardWindow
            { Owner = this };

            if (win.ShowDialog() != true) return;

            string name = win.CardName;
            bool pool = win.SearchInPool;
            var grid = pool ? TopDataGrid : BottomDataGrid;
            var items = grid.ItemsSource as System.Collections.IEnumerable;
            if (items == null) return;

            // Build match list
            _searchMatches.Clear();
            _lastSearchTerm = name;
            _searchMatchIndex = -1;

            foreach (var item in items)
            {
                string? itemName = item.GetType()
                    .GetProperty("Name")?.GetValue(item)?.ToString();
                if (itemName != null &&
                    itemName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    _searchMatches.Add(item);
            }

            if (_searchMatches.Count == 0)
            {
                MessageBox.Show($"Card '{name}' not found.",
                    "Search", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Jump to first match
            _searchMatchIndex = 0;
            var match = _searchMatches[0];
            grid.SelectedItem = match;
            grid.UpdateLayout();
            grid.ScrollIntoView(match);
            var row = grid.ItemContainerGenerator
                .ContainerFromItem(match) as DataGridRow;
            row?.BringIntoView();
        }

        private void NavigateMatch(bool forward)
        {
            if (_searchMatches.Count == 0)
            {
                return;
            }

            if (forward)
                _searchMatchIndex = (_searchMatchIndex + 1) %
                                    _searchMatches.Count;
            else
                _searchMatchIndex = (_searchMatchIndex - 1 +
                                    _searchMatches.Count) %
                                    _searchMatches.Count;

            var grid = _bottomTableHasFocus ? BottomDataGrid : TopDataGrid;
            var item = _searchMatches[_searchMatchIndex];

            grid.SelectedItem = item;
            grid.UpdateLayout();
            grid.ScrollIntoView(item);
            var row = grid.ItemContainerGenerator
                .ContainerFromItem(item) as DataGridRow;
            row?.BringIntoView();
        }

        private void BtnFindNext_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSearchTerm))
            {
                BtnSearch_Click(sender, e);
                return;
            }
            NavigateMatch(forward: true);
        }

        private void BtnFindPrev_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSearchTerm))
                return;
            NavigateMatch(forward: false);
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            _searchMatches.Clear();
            _searchMatchIndex = -1;
            _lastSearchTerm = string.Empty;
            _searchText = string.Empty;
            if (SearchBox != null) SearchBox.Text = string.Empty;
            LoadCurrentMode();
        }

        // ════════════════════════════════════════════════════════════════════
        // TOOLBAR BUTTONS
        // ════════════════════════════════════════════════════════════════════
        private void BtnAddToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (_currentMode == "DeckToCollection" &&
                TopDataGrid.SelectedItem is DeckCard dc)
                AddDeckCardToCollection(dc, foil: false);
            else
                AddFromTopSelection(foil: false, qty: 1);
        }

        private void BtnAddFoilToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (_currentMode == "DeckToCollection" &&
                TopDataGrid.SelectedItem is DeckCard dc)
                AddDeckCardToCollection(dc, foil: true);
            else
                AddFromTopSelection(foil: true, qty: 1);
        }

        private void BtnImportDeckToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (!_activeDeck.Cards.Any())
            {
                MessageBox.Show("The deck has no cards to import.",
                    "Empty Deck", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var preview = new Windows.DeckImportPreviewWindow(_activeDeck)
            { Owner = this };

            if (preview.ShowDialog() != true) return;

            // Run the import and show report
            var report = ExecuteDeckImport(preview.SelectedCards);

            var reportWin = new Windows.DeckImportReportWindow(report)
            { Owner = this };
            reportWin.ShowDialog();

            // Refresh collection, top deck grid, and summary
            LoadBottomTable_Collection();
            LoadTopTable_DeckForCollection();
            TopSearchLabel.Text = _activeDeck != null
                ? $"Deck: {_activeDeck.Name}"
                : "Deck  (no deck open)";
            RestoreFocus();
        }

        private void BtnRemoveFromCollection_Click(object sender, RoutedEventArgs e)
        {
            if (BottomDataGrid.SelectedItem is not CollectionDisplayRow row) return;

            if (_currentMode == "CollectionToTradeBinder")
            {
                if (MessageBox.Show($"Remove '{row.Name}' from your Trade Binder?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    == MessageBoxResult.Yes)
                    RemoveFromTradeBinderRow(row);
                return;
            }

            if (_currentMode == "PoolToWantList")
            {
                if (MessageBox.Show($"Remove '{row.Name}' from your Want List?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    == MessageBoxResult.Yes)
                    RemoveFromWantListRow(row);
                return;
            }

            bool hasBoth = row.Quantity > 0 && row.FoilQuantity > 0;
            bool foil = false;

            if (hasBoth)
            {
                var ask = MessageBox.Show(
                    $"'{row.Name}' has both non-foil ({row.Quantity}) and foil ({row.FoilQuantity}) copies.\n\n" +
                    "Yes = Remove 1 Non-Foil\nNo = Remove 1 Foil",
                    "Remove Non-Foil or Foil?",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (ask == MessageBoxResult.Cancel) return;
                foil = ask == MessageBoxResult.No;
            }
            else if (row.FoilQuantity > 0 && row.Quantity == 0)
                foil = true;

            RemoveFromCollection(row, 1, false, foil);
        }

        // ════════════════════════════════════════════════════════════════════
        // POOL CONTEXT MENU (RIGHT-CLICK)
        // ════════════════════════════════════════════════════════════════════
        private void TopCtx_Opened(object sender, RoutedEventArgs e)
        {
            bool hasCard = TopDataGrid.SelectedItem != null;
            bool isDeckMode = _currentMode == "PoolToDeck" ||
                              _currentMode == "CollectionToDeck" ||
                              _currentMode == "DeckToCollection";
            bool isCollMode = !isDeckMode;

            bool canFoil = false, canNonFoil = false;
            switch (TopDataGrid.SelectedItem)
            {
                case PoolCard pc:
                    canFoil = pc.IsFoil; canNonFoil = pc.IsNonFoil; break;
                case TokenCard tc:
                    canFoil = tc.IsFoil; canNonFoil = tc.IsNonFoil; break;
                case PlanarCard pl:
                    canFoil = pl.IsFoil; canNonFoil = pl.IsNonFoil; break;
                case SchemeCard sc:
                    canFoil = sc.IsFoil; canNonFoil = sc.IsNonFoil; break;
                case ArtSeriesCard ac:
                    canFoil = ac.IsFoil; canNonFoil = ac.IsNonFoil; break;
                case ConspiracyCard cc:
                    canFoil = cc.IsFoil; canNonFoil = cc.IsNonFoil; break;
            }

            TopCtxAddToCollNonFoil.IsEnabled = hasCard && isCollMode && canNonFoil;
            TopCtxAddToCollFoil.IsEnabled = hasCard && isCollMode && canFoil;
            TopCtxAddToDeckNonFoil.IsEnabled = hasCard && isDeckMode &&
                                               _activeDeck != null && canNonFoil;
            TopCtxAddToDeckFoil.IsEnabled = hasCard && isDeckMode &&
                                               _activeDeck != null && canFoil;
            TopCtxDeckSeparator.Visibility = isDeckMode
                ? Visibility.Visible : Visibility.Collapsed;
            TopCtxAddToDeckNonFoil.Visibility = isDeckMode
                ? Visibility.Visible : Visibility.Collapsed;
            TopCtxAddToDeckFoil.Visibility = isDeckMode
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TopCtxAddToCollNonFoil_Click(object sender, RoutedEventArgs e)
            => AddFromTopSelection(foil: false, qty: 1);

        private void TopCtxAddToCollFoil_Click(object sender, RoutedEventArgs e)
            => AddFromTopSelection(foil: true, qty: 1);

        private void TopCtxAddToDeckNonFoil_Click(object sender, RoutedEventArgs e)
            => AddFromTopSelectionToDeck(foil: false);

        private void TopCtxAddToDeckFoil_Click(object sender, RoutedEventArgs e)
            => AddFromTopSelectionToDeck(foil: true);

        // ── Top grid double-click ─────────────────────────────────────────────
        private void TopDataGrid_MouseDoubleClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
            if (TopDataGrid.SelectedItem == null) return;

            bool shift = Keyboard.IsKeyDown(Key.LeftShift) ||
                         Keyboard.IsKeyDown(Key.RightShift);

            // Deck modes
            if (_currentMode == "PoolToDeck" || _currentMode == "CollectionToDeck")
            {
                if (TopDataGrid.SelectedItem is PoolCard pc)
                    AddCardToActiveDeck(pc, foil: shift);
                else if (TopDataGrid.SelectedItem is CollectionDisplayRow cr)
                    AddCardToActiveDeck(cr);
                e.Handled = true;
                return;
            }
            if (_currentMode == "DeckToCollection")
            {
                if (TopDataGrid.SelectedItem is DeckCard dc)
                    AddDeckCardToCollection(dc);
                e.Handled = true;
                return;
            }

            // Collection modes
            AddFromTopSelection(foil: shift, qty: 1);
            e.Handled = true;
        }

        // ── Remove 1 Non-Foil / 1 Foil ───────────────────────────────────────
        private void CtxRemove1NonFoil_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (BottomDataGrid.SelectedItem is not CollectionDisplayRow row) return;
            if (row.Quantity <= 0)
            {
                MessageBox.Show("No non-foil copies to remove.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RemoveFromCollection(row, 1, false, foil: false);
        }

        private void CtxRemove1Foil_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (BottomDataGrid.SelectedItem is not CollectionDisplayRow row) return;
            if (row.FoilQuantity <= 0)
            {
                MessageBox.Show("No foil copies to remove.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RemoveFromCollection(row, 1, false, foil: true);
        }

        // ── Combine duplicate collection rows ─────────────────────────────────
        private void MenuCombineDuplicates_Click(object sender, RoutedEventArgs e)
        {
            using var db = new Data.CollectionDbContext();
            var entries = db.CollectionEntries.ToList();

            var groups = entries.GroupBy(e => e.PoolId)
                .Where(g => g.Count() > 1).ToList();

            if (groups.Count == 0)
            {
                MessageBox.Show("No duplicate entries found in your collection.",
                    "Combine Duplicates", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Found {groups.Count} card(s) with multiple rows.\n\n" +
                "Quantities will be added together into a single row.\n" +
                "The newest entry's condition, notes, and prices will be kept.\n\n" +
                "Continue?",
                "Combine Duplicate Rows",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            int combined = 0;
            foreach (var group in groups)
            {
                var ordered = group.OrderByDescending(e => e.DateModified).ToList();
                var keep = ordered.First();
                keep.Quantity = ordered.Sum(e => e.Quantity);
                keep.FoilQuantity = ordered.Sum(e => e.FoilQuantity);
                keep.UsedCount = ordered.Sum(e => e.UsedCount);

                foreach (var dup in ordered.Skip(1))
                    db.CollectionEntries.Remove(dup);

                combined++;
            }

            db.SaveChanges();
            RefreshBottom();

            MessageBox.Show($"Combined {combined} duplicate row(s) successfully.",
                "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Route add from top selection ─────────────────────────────────────
        private void AddFromTopSelection(bool foil, int qty)
        {
            switch (TopDataGrid.SelectedItem)
            {
                case PoolCard pc:
                    if (foil && !pc.IsFoil) return; // strict foil check
                    if (!foil && !pc.IsNonFoil) return;
                    AddToPoolCollection(pc.PoolId, pc.Name, qty, foil);
                    break;
                case PlanarCard pl:
                    if (foil && !pl.IsFoil) return;
                    AddToSpecialCollection("Planechase", pl.PlanarId,
                        pl.Name, qty, foil);
                    break;
                case SchemeCard sc:
                    if (foil && !sc.IsFoil) return;
                    AddToSpecialCollection("Archenemy", sc.SchemeId,
                        sc.Name, qty, foil);
                    break;
                case VanguardCard vc:
                    AddToSpecialCollection("Vanguard", vc.VanguardId,
                        vc.Name, qty, false);
                    break;
                case TokenCard tc:
                    if (foil && !tc.IsFoil) return;
                    AddToSpecialCollection("Token", tc.TokenId,
                        tc.Name, qty, foil);
                    break;
                case ArtSeriesCard ac:
                    if (foil && !ac.IsFoil) return;
                    AddToSpecialCollection("ArtSeries", ac.ArtSeriesId,
                        ac.Name, qty, foil);
                    break;
                case ConspiracyCard cc:
                    if (foil && !cc.IsFoil) return;
                    AddToSpecialCollection("Conspiracy", cc.ConspiracyId,
                        cc.Name, qty, foil);
                    break;
            }

            // Trade Binder — top table is Collection, bottom is binder
            if (_currentMode == "CollectionToTradeBinder")
            {
                int poolId = 0;
                string name = "";
                if (TopDataGrid.SelectedItem is PoolCard pp)
                { poolId = pp.PoolId; name = pp.Name; }
                else if (TopDataGrid.SelectedItem is CollectionDisplayRow cr2)
                { poolId = cr2.PoolId; name = cr2.Name; }
                if (poolId > 0)
                {
                    using var pdb2 = new AppDbContext();
                    var pc2 = pdb2.PoolCards.FirstOrDefault(c => c.PoolId == poolId);
                    if (pc2 != null) AddToTradeBinder(pc2, foil, qty);
                }
                return;
            }

            // Want List — pool card only
            if (_currentMode == "PoolToWantList" &&
                TopDataGrid.SelectedItem is PoolCard poolCard2)
            {
                AddToWantList(poolCard2, foil, qty);
                return;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // COLLECTION OPERATIONS (ALL AUTO-SAVE)
        // ════════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════════
        // DECK → COLLECTION
        // ════════════════════════════════════════════════════════════════════
        private void AddDeckCardToCollection(DeckCard card, bool foil = false)
        {
            if (card.PoolId <= 0)
            {
                MessageBox.Show(
                    $"'{card.Name}' cannot be added — no pool entry found.",
                    "Cannot Add", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Auto-switch to whichever has qty if requested type is empty
            if (!foil && card.Quantity <= 0 && card.FoilQuantity > 0)
                foil = true;
            else if (foil && card.FoilQuantity <= 0 && card.Quantity > 0)
                foil = false;

            // Add 1 copy to collection
            AddToPoolCollection(card.PoolId, card.Name, 1, foil);

            // Update UsedCount — card is in active deck so mark it used
            if (_activeDeck != null)
            {
                var deckCard = _activeDeck.Cards
                    .FirstOrDefault(c => c.PoolId == card.PoolId);
                if (deckCard != null)
                    SetUsedCount(card.PoolId, deckCard.TotalQuantity);
            }

            // Refresh bottom collection table immediately
            LoadBottomTable_Collection();

            // Refresh top grid label in case deck name needs updating
            TopSearchLabel.Text = _activeDeck != null
                ? $"Deck: {_activeDeck.Name}"
                : "Deck  (no deck open)";

            RestoreFocus();
        }

        // Returns import report rows after processing checked cards
        private List<DeckImportReportRow> ExecuteDeckImport(
            List<DeckCard> cards)
        {
            var report = new List<DeckImportReportRow>();

            using var db = new Data.CollectionDbContext();

            foreach (var card in cards)
            {
                if (card.PoolId <= 0) continue;

                var entry = db.CollectionEntries
                    .FirstOrDefault(c => c.PoolId == card.PoolId);

                int prevQty = entry?.Quantity ?? 0;
                int prevFoil = entry?.FoilQuantity ?? 0;
                bool isNew = entry == null;

                if (entry == null)
                {
                    entry = new Models.CollectionEntry
                    {
                        PoolId = card.PoolId,
                        Quantity = card.Quantity,
                        FoilQuantity = card.FoilQuantity,
                        DateAdded = DateTime.Now,
                        DateModified = DateTime.Now,
                        UsedCount = card.TotalQuantity
                    };
                    db.CollectionEntries.Add(entry);
                }
                else
                {
                    entry.Quantity += card.Quantity;
                    entry.FoilQuantity += card.FoilQuantity;
                    entry.UsedCount += card.TotalQuantity;
                    entry.DateModified = DateTime.Now;
                }

                report.Add(new DeckImportReportRow
                {
                    Name = card.Name,
                    SetCode = card.SetCode,
                    Category = card.CategoryDisplay,
                    NonFoilAdded = card.Quantity,
                    FoilAdded = card.FoilQuantity,
                    PrevNonFoil = prevQty,
                    PrevFoil = prevFoil,
                    NewNonFoil = (isNew ? 0 : prevQty) + card.Quantity,
                    NewFoil = (isNew ? 0 : prevFoil) + card.FoilQuantity,
                    Status = isNew ? "New Row" : "Merged"
                });
            }

            db.SaveChanges();
            return report;
        }

        private void AddToPoolCollection(int poolId, string name,
    int qty, bool foil)
        {
            using var db = new CollectionDbContext();
            var existing = db.CollectionEntries
                .FirstOrDefault(c => c.PoolId == poolId);

            if (existing != null)
            {
                // Card already exists — show confirmation popup
                var result = MessageBox.Show(
                    $"The collection already has copies of '{name}' present.\n\n" +
                    $"Press 'Yes' to add the selected quantity ({qty}) to the " +
                    $"already existing collection card\n" +
                    $"Press 'No' to add the card at a new row\n" +
                    $"Press 'Cancel' to skip addition of the card to the collection.",
                    "Confirmation",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;

                if (result == MessageBoxResult.Yes)
                {
                    // Add to existing row
                    if (foil) existing.FoilQuantity += qty;
                    else existing.Quantity += qty;
                    existing.DateModified = DateTime.Now;
                    db.SaveChanges();
                    RefreshBottom();
                    RestoreFocus();
                    return;
                }

                // No = fall through to add as new row
            }

            // Add as new row
            db.CollectionEntries.Add(new CollectionEntry
            {
                PoolId = poolId,
                Quantity = foil ? 0 : qty,
                FoilQuantity = foil ? qty : 0,
                Condition = "Near Mint",
                Language = "English",
                DateAdded = DateTime.Now,
                DateModified = DateTime.Now
            });

            db.SaveChanges();
            RefreshBottom();

            // Download card image in background if not already cached
            using var pdb = new Data.AppDbContext();
            var poolCard = pdb.PoolCards.FirstOrDefault(p => p.PoolId == poolId);
            if (poolCard != null && !string.IsNullOrEmpty(poolCard.ImageNormalUrl))
                _ = DownloadAndCacheCardImageAsync(poolCard.Name, poolCard.ImageNormalUrl, poolCard.ImageBackUrl);
        }

        private void AddToSpecialCollection(string type, int cardId,
            string name, int qty, bool foil)
        {
            using var db = new Data.CollectionDbContext();

            switch (type)
            {
                case "Planechase":
                    var pe = db.PlanarCollectionEntries
                        .FirstOrDefault(c => c.PlanarId == cardId);
                    if (pe == null)
                        db.PlanarCollectionEntries.Add(new PlanarCollectionEntry
                        {
                            PlanarId = cardId,
                            Quantity = foil ? 0 : qty,
                            FoilQuantity = foil ? qty : 0,
                            Condition = "Near Mint",
                            Language = "English",
                            DateAdded = DateTime.Now,
                            DateModified = DateTime.Now
                        });
                    else
                    {
                        if (foil) pe.FoilQuantity += qty;
                        else pe.Quantity += qty;
                        pe.DateModified = DateTime.Now;
                    }
                    break;

                case "Archenemy":
                    var se = db.SchemeCollectionEntries
                        .FirstOrDefault(c => c.SchemeId == cardId);
                    if (se == null)
                        db.SchemeCollectionEntries.Add(new SchemeCollectionEntry
                        {
                            SchemeId = cardId,
                            Quantity = foil ? 0 : qty,
                            FoilQuantity = foil ? qty : 0,
                            Condition = "Near Mint",
                            Language = "English",
                            DateAdded = DateTime.Now,
                            DateModified = DateTime.Now
                        });
                    else
                    {
                        if (foil) se.FoilQuantity += qty;
                        else se.Quantity += qty;
                        se.DateModified = DateTime.Now;
                    }
                    break;

                case "Vanguard":
                    var ve = db.VanguardCollectionEntries
                        .FirstOrDefault(c => c.VanguardId == cardId);
                    if (ve == null)
                        db.VanguardCollectionEntries.Add(
                            new VanguardCollectionEntry
                            {
                                VanguardId = cardId,
                                Quantity = qty,
                                FoilQuantity = 0,
                                Condition = "Near Mint",
                                Language = "English",
                                DateAdded = DateTime.Now,
                                DateModified = DateTime.Now
                            });
                    else
                    {
                        ve.Quantity += qty;
                        ve.DateModified = DateTime.Now;
                    }
                    break;

                case "Token":
                    var te = db.TokenCollectionEntries
                        .FirstOrDefault(c => c.TokenId == cardId);
                    if (te == null)
                        db.TokenCollectionEntries.Add(new TokenCollectionEntry
                        {
                            TokenId = cardId,
                            Quantity = foil ? 0 : qty,
                            FoilQuantity = foil ? qty : 0,
                            Condition = "Near Mint",
                            Language = "English",
                            DateAdded = DateTime.Now,
                            DateModified = DateTime.Now
                        });
                    else
                    {
                        if (foil) te.FoilQuantity += qty;
                        else te.Quantity += qty;
                        te.DateModified = DateTime.Now;
                    }
                    break;

                case "ArtSeries":
                    var ae = db.ArtSeriesCollectionEntries
                        .FirstOrDefault(c => c.ArtSeriesId == cardId);
                    if (ae == null)
                        db.ArtSeriesCollectionEntries.Add(
                            new ArtSeriesCollectionEntry
                            {
                                ArtSeriesId = cardId,
                                Quantity = foil ? 0 : qty,
                                FoilQuantity = foil ? qty : 0,
                                Condition = "Near Mint",
                                Language = "English",
                                DateAdded = DateTime.Now,
                                DateModified = DateTime.Now
                            });
                    else
                    {
                        if (foil) ae.FoilQuantity += qty;
                        else ae.Quantity += qty;
                        ae.DateModified = DateTime.Now;
                    }
                    break;

                case "Conspiracy":
                    var cone = db.ConspiracyCollectionEntries
                        .FirstOrDefault(c => c.ConspiracyId == cardId);
                    if (cone == null)
                        db.ConspiracyCollectionEntries.Add(
                            new ConspiracyCollectionEntry
                            {
                                ConspiracyId = cardId,
                                Quantity = foil ? 0 : qty,
                                FoilQuantity = foil ? qty : 0,
                                Condition = "Near Mint",
                                Language = "English",
                                DateAdded = DateTime.Now,
                                DateModified = DateTime.Now
                            });
                    else
                    {
                        if (foil) cone.FoilQuantity += qty;
                        else cone.Quantity += qty;
                        cone.DateModified = DateTime.Now;
                    }
                    break;
            }

            db.SaveChanges();
            RefreshBottom();
        }

        // ── Trade Binder — Have List operations ───────────────────────────────
        private void AddToTradeBinder(PoolCard pc, bool foil, int qty)
        {
            using var db = new Data.CollectionDbContext();

            // Check if card is in a deck (UsedCount > 0)
            var collEntry = db.CollectionEntries
                .FirstOrDefault(c => c.PoolId == pc.PoolId);
            if (collEntry != null && collEntry.UsedCount > 0)
            {
                var r = MessageBox.Show(
                    $"'{pc.Name}' has {collEntry.UsedCount} copy/copies currently " +
                    $"in a deck.\n\nAre you sure you want to list it in the Trade Binder?",
                    "Card Is In a Deck",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            // Auto-pull condition and market price from collection entry
            string condition = collEntry?.Condition ?? "Near Mint";
            decimal? askingPrice = foil ? pc.PriceUsdFoil : pc.PriceUsd;

            var existing = db.TradeBinderEntries
                .FirstOrDefault(e => e.PoolId == pc.PoolId && e.IsFoil == foil);
            if (existing == null)
                db.TradeBinderEntries.Add(new Models.TradeBinderEntry
                {
                    PoolId = pc.PoolId,
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = condition,
                    AskingPrice = askingPrice,
                    DateAdded = DateTime.Now
                });
            else
            {
                existing.Quantity += qty;
            }
            db.SaveChanges();
            RefreshBottom();
        }

        private void SaveTradeBinderRow(CollectionDisplayRow row)
        {
            using var db = new Data.CollectionDbContext();
            var e = db.TradeBinderEntries.FirstOrDefault(
                t => t.TradeBinderEntryId == row.CollectionEntryId);
            if (e == null) return;
            e.Quantity = row.Quantity;
            e.Condition = row.Condition;
            e.AskingPrice = row.SellAt;
            e.Notes = row.Notes;
            db.SaveChanges();
        }

        private void SaveWantListRow(CollectionDisplayRow row)
        {
            using var db = new Data.CollectionDbContext();
            var e = db.WantListEntries.FirstOrDefault(
                w => w.WantListEntryId == row.CollectionEntryId);
            if (e == null) return;
            e.Quantity = row.Quantity;
            e.OfferPrice = row.BuyAt;
            e.Notes = row.Notes;
            db.SaveChanges();
        }

        private void RemoveFromTradeBinderRow(CollectionDisplayRow row)
        {
            using var db = new Data.CollectionDbContext();
            var e = db.TradeBinderEntries.FirstOrDefault(
                t => t.TradeBinderEntryId == row.CollectionEntryId);
            if (e == null) return;
            db.TradeBinderEntries.Remove(e);
            db.SaveChanges();
            RefreshBottom();
        }

        private void RemoveFromWantListRow(CollectionDisplayRow row)
        {
            using var db = new Data.CollectionDbContext();
            var e = db.WantListEntries.FirstOrDefault(
                w => w.WantListEntryId == row.CollectionEntryId);
            if (e == null) return;
            db.WantListEntries.Remove(e);
            db.SaveChanges();
            RefreshBottom();
        }


        // ── Want List operations ──────────────────────────────────────────────
        private void AddToWantList(PoolCard pc, bool foil, int qty)
        {
            using var db = new Data.CollectionDbContext();
            var existing = db.WantListEntries
                .FirstOrDefault(e => e.PoolId == pc.PoolId && e.IsFoil == foil);
            if (existing == null)
                db.WantListEntries.Add(new Models.WantListEntry
                {
                    PoolId = pc.PoolId,
                    Quantity = qty,
                    IsFoil = foil,
                    DateAdded = DateTime.Now
                });
            else
                existing.Quantity += qty;
            db.SaveChanges();
            RefreshBottom();
        }


        private void RemoveFromCollection(
            CollectionDisplayRow row, int qty, bool all, bool foil = false)
        {
            using var db = new Data.CollectionDbContext();

            switch (_currentMode)
            {
                case "PoolToCollection":
                case "DeckToCollection":
                    {
                        var e = db.CollectionEntries.FirstOrDefault(
                            c => c.CollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (all) db.CollectionEntries.Remove(e);
                        else
                        {
                            if (foil) e.FoilQuantity = Math.Max(0, e.FoilQuantity - qty);
                            else e.Quantity = Math.Max(0, e.Quantity - qty);
                            e.DateModified = DateTime.Now;
                            if (e.Quantity == 0 && e.FoilQuantity == 0)
                                db.CollectionEntries.Remove(e);
                        }
                        break;
                    }
                case "PoolToPlanechase":
                    {
                        var e = db.PlanarCollectionEntries.FirstOrDefault(
                            c => c.PlanarCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (all) db.PlanarCollectionEntries.Remove(e);
                        else
                        {
                            e.Quantity = Math.Max(0, e.Quantity - qty);
                            e.DateModified = DateTime.Now;
                            if (e.Quantity == 0 && e.FoilQuantity == 0)
                                db.PlanarCollectionEntries.Remove(e);
                        }
                        break;
                    }
                case "PoolToArchenemy":
                    {
                        var e = db.SchemeCollectionEntries.FirstOrDefault(
                            c => c.SchemeCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (all) db.SchemeCollectionEntries.Remove(e);
                        else
                        {
                            e.Quantity = Math.Max(0, e.Quantity - qty);
                            e.DateModified = DateTime.Now;
                            if (e.Quantity == 0 && e.FoilQuantity == 0)
                                db.SchemeCollectionEntries.Remove(e);
                        }
                        break;
                    }
                case "PoolToVanguard":
                    {
                        var e = db.VanguardCollectionEntries.FirstOrDefault(
                            c => c.VanguardCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (all) db.VanguardCollectionEntries.Remove(e);
                        else
                        {
                            e.Quantity = Math.Max(0, e.Quantity - qty);
                            e.DateModified = DateTime.Now;
                            if (e.Quantity == 0 && e.FoilQuantity == 0)
                                db.VanguardCollectionEntries.Remove(e);
                        }
                        break;
                    }
                case "PoolToTokens":
                    {
                        var e = db.TokenCollectionEntries.FirstOrDefault(
                            c => c.TokenCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (all) db.TokenCollectionEntries.Remove(e);
                        else
                        {
                            e.Quantity = Math.Max(0, e.Quantity - qty);
                            e.DateModified = DateTime.Now;
                            if (e.Quantity == 0 && e.FoilQuantity == 0)
                                db.TokenCollectionEntries.Remove(e);
                        }
                        break;
                    }
                case "PoolToArtSeries":
                    {
                        var e = db.ArtSeriesCollectionEntries.FirstOrDefault(
                            c => c.ArtSeriesCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (all) db.ArtSeriesCollectionEntries.Remove(e);
                        else
                        {
                            e.Quantity = Math.Max(0, e.Quantity - qty);
                            e.DateModified = DateTime.Now;
                            if (e.Quantity == 0 && e.FoilQuantity == 0)
                                db.ArtSeriesCollectionEntries.Remove(e);
                        }
                        break;
                    }
                case "PoolToConspiracy":
                    {
                        var e = db.ConspiracyCollectionEntries.FirstOrDefault(
                            c => c.ConspiracyCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (all) db.ConspiracyCollectionEntries.Remove(e);
                        else
                        {
                            e.Quantity = Math.Max(0, e.Quantity - qty);
                            e.DateModified = DateTime.Now;
                            if (e.Quantity == 0 && e.FoilQuantity == 0)
                                db.ConspiracyCollectionEntries.Remove(e);
                        }
                        break;
                    }
                default: return;
            }

            db.SaveChanges();
            RefreshBottom();
            RestoreFocus();
        }

        private void AdjustCollectionQty(
            CollectionDisplayRow row, int delta, bool foil)
        {
            using var db = new Data.CollectionDbContext();

            switch (_currentMode)
            {
                case "PoolToCollection":
                    {
                        var e = db.CollectionEntries.FirstOrDefault(
                            c => c.CollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = Math.Max(0, e.FoilQuantity + delta);
                        else e.Quantity = Math.Max(0, e.Quantity + delta);
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.CollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToPlanechase":
                    {
                        var e = db.PlanarCollectionEntries.FirstOrDefault(
                            c => c.PlanarCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = Math.Max(0, e.FoilQuantity + delta);
                        else e.Quantity = Math.Max(0, e.Quantity + delta);
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.PlanarCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToArchenemy":
                    {
                        var e = db.SchemeCollectionEntries.FirstOrDefault(
                            c => c.SchemeCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = Math.Max(0, e.FoilQuantity + delta);
                        else e.Quantity = Math.Max(0, e.Quantity + delta);
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.SchemeCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToVanguard":
                    {
                        var e = db.VanguardCollectionEntries.FirstOrDefault(
                            c => c.VanguardCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = Math.Max(0, e.FoilQuantity + delta);
                        else e.Quantity = Math.Max(0, e.Quantity + delta);
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.VanguardCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToTokens":
                    {
                        var e = db.TokenCollectionEntries.FirstOrDefault(
                            c => c.TokenCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = Math.Max(0, e.FoilQuantity + delta);
                        else e.Quantity = Math.Max(0, e.Quantity + delta);
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.TokenCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToArtSeries":
                    {
                        var e = db.ArtSeriesCollectionEntries.FirstOrDefault(
                            c => c.ArtSeriesCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = Math.Max(0, e.FoilQuantity + delta);
                        else e.Quantity = Math.Max(0, e.Quantity + delta);
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.ArtSeriesCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToConspiracy":
                    {
                        var e = db.ConspiracyCollectionEntries.FirstOrDefault(
                            c => c.ConspiracyCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = Math.Max(0, e.FoilQuantity + delta);
                        else e.Quantity = Math.Max(0, e.Quantity + delta);
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.ConspiracyCollectionEntries.Remove(e);
                        break;
                    }
                default: return;
            }

            db.SaveChanges();
            RefreshBottom();
        }

        private void SetCollectionQty(
            CollectionDisplayRow row, int qty, bool foil)
        {
            if (qty < 0) return;
            using var db = new Data.CollectionDbContext();

            switch (_currentMode)
            {
                case "PoolToCollection":
                    {
                        var e = db.CollectionEntries.FirstOrDefault(
                            c => c.CollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = qty; else e.Quantity = qty;
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.CollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToPlanechase":
                    {
                        var e = db.PlanarCollectionEntries.FirstOrDefault(
                            c => c.PlanarCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = qty; else e.Quantity = qty;
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.PlanarCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToArchenemy":
                    {
                        var e = db.SchemeCollectionEntries.FirstOrDefault(
                            c => c.SchemeCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = qty; else e.Quantity = qty;
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.SchemeCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToVanguard":
                    {
                        var e = db.VanguardCollectionEntries.FirstOrDefault(
                            c => c.VanguardCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = qty; else e.Quantity = qty;
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.VanguardCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToTokens":
                    {
                        var e = db.TokenCollectionEntries.FirstOrDefault(
                            c => c.TokenCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = qty; else e.Quantity = qty;
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.TokenCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToArtSeries":
                    {
                        var e = db.ArtSeriesCollectionEntries.FirstOrDefault(
                            c => c.ArtSeriesCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = qty; else e.Quantity = qty;
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.ArtSeriesCollectionEntries.Remove(e);
                        break;
                    }
                case "PoolToConspiracy":
                    {
                        var e = db.ConspiracyCollectionEntries.FirstOrDefault(
                            c => c.ConspiracyCollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (foil) e.FoilQuantity = qty; else e.Quantity = qty;
                        e.DateModified = DateTime.Now;
                        if (e.Quantity == 0 && e.FoilQuantity == 0)
                            db.ConspiracyCollectionEntries.Remove(e);
                        break;
                    }
                default: return;
            }

            db.SaveChanges();
        }

        // ════════════════════════════════════════════════════════════════════
        // INLINE ROW SPINNERS
        // ════════════════════════════════════════════════════════════════════
        private void BtnQtyPlus_Row_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if ((sender as Button)?.Tag is CollectionDisplayRow row)
                AdjustCollectionQty(row, 1, false);
        }

        private void BtnQtyMinus_Row_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if ((sender as Button)?.Tag is CollectionDisplayRow row)
                AdjustCollectionQty(row, -1, false);
        }

        private void BtnFoilQtyPlus_Row_Click(object sender,
            RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if ((sender as Button)?.Tag is CollectionDisplayRow row)
                AdjustCollectionQty(row, 1, true);
        }

        private void BtnFoilQtyMinus_Row_Click(object sender,
            RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if ((sender as Button)?.Tag is CollectionDisplayRow row)
                AdjustCollectionQty(row, -1, true);
        }

        private void TxtQtyRow_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            if (_bottomLocked) return;
            if (sender is TextBox tb &&
                tb.Tag is CollectionDisplayRow row &&
                int.TryParse(tb.Text, out int qty))
                SetCollectionQty(row, qty, false);
        }

        private void TxtFoilQtyRow_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            if (_bottomLocked) return;
            if (sender is TextBox tb &&
                tb.Tag is CollectionDisplayRow row &&
                int.TryParse(tb.Text, out int qty))
                SetCollectionQty(row, qty, true);
        }

        private void TxtQtyRow_KeyDown(object sender, KeyEventArgs e)
        {
            if (_bottomLocked) return;
            if (sender is TextBox tb &&
                tb.Tag is CollectionDisplayRow row)
            {
                if (e.Key == Key.Up)
                { AdjustCollectionQty(row, 1, false); e.Handled = true; }
                else if (e.Key == Key.Down)
                { AdjustCollectionQty(row, -1, false); e.Handled = true; }
                else if (e.Key == Key.Enter)
                {
                    if (int.TryParse(tb.Text, out int qty))
                        SetCollectionQty(row, qty, false);
                    RefreshBottom();
                    RestoreFocus();
                    e.Handled = true;
                }
            }
        }

        private void TxtFoilQtyRow_KeyDown(object sender, KeyEventArgs e)
        {
            if (_bottomLocked) return;
            if (sender is TextBox tb &&
                tb.Tag is CollectionDisplayRow row)
            {
                if (e.Key == Key.Up)
                { AdjustCollectionQty(row, 1, true); e.Handled = true; }
                else if (e.Key == Key.Down)
                { AdjustCollectionQty(row, -1, true); e.Handled = true; }
                else if (e.Key == Key.Enter)
                {
                    if (int.TryParse(tb.Text, out int qty))
                        SetCollectionQty(row, qty, true);
                    RefreshBottom();
                    RestoreFocus();
                    e.Handled = true;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CONTEXT MENU
        // ════════════════════════════════════════════════════════════════════

        private void CtxRemoveAll_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row && !row.IsFooter)
            {
                var r = MessageBox.Show(
                    $"Remove ALL copies of {row.Name}?\n\nThis cannot be undone.",
                    "Confirm Remove All",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (r == MessageBoxResult.Yes)
                    RemoveFromCollection(row, 0, true);
            }
        }

        private async void CtxViewDetails_Click(object sender,
            RoutedEventArgs e)
        {
            await HandleSelectionAsync(BottomDataGrid.SelectedItem);
        }

        // ════════════════════════════════════════════════════════════════════
        // LEGALITY TOGGLE
        // ════════════════════════════════════════════════════════════════════
        private void BtnLegality_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the Legal pill column visibility in all active grids
            _legalityVisible = !_legalityVisible;
            var vis = _legalityVisible ? Visibility.Visible : Visibility.Collapsed;

            foreach (var grid in new[] { TopDataGrid, BottomDataGrid })
            {
                var legalCol = grid.Columns.FirstOrDefault(
                    c => c.Header?.ToString() == "Legal");
                if (legalCol != null)
                    legalCol.Visibility = vis;
            }

            // Also toggle in active deck grid
            var deckGrid = GetActiveDeckGrid();
            if (deckGrid != null)
            {
                var legalCol = deckGrid.Columns.FirstOrDefault(
                    c => c.Header?.ToString() == "Legal");
                if (legalCol != null)
                    legalCol.Visibility = vis;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DETAIL PANEL
        // ════════════════════════════════════════════════════════════════════
        private void ShowPoolCardDetail(PoolCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = c.PowerToughness;
            DetailPTLabel.Text = "POWER / TOUGHNESS";
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = FormatPrices(c.PricesJson);
            RenderManaCost(c.ManaCost);
            LoadSetSymbol(c.SetCode);
            RenderLegality(c.LegalitiesJson);
        }

        private void ShowTokenCardDetail(TokenCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = c.PowerToughness;
            DetailPTLabel.Text = "POWER / TOUGHNESS";
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            DetailLegalityPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowPlanarCardDetail(PlanarCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            DetailLegalityPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowSchemeCardDetail(SchemeCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            DetailLegalityPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowVanguardCardDetail(VanguardCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text =
                $"Hand: {c.HandModifier}  Life: {c.LifeModifier}";
            DetailPTLabel.Text = "HAND / LIFE MODIFIER";
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            DetailLegalityPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowArtSeriesCardDetail(ArtSeriesCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = string.Empty;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            DetailLegalityPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowConspiracyCardDetail(ConspiracyCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            RenderManaCost(c.ManaCost);
            DetailLegalityPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        // Shows pool card detail by looking it up from cache by PoolId
        internal void ShowPoolCardDetailById(int poolId)
        {
            var pc = _poolCache?.FirstOrDefault(c => c.PoolId == poolId);
            if (pc != null) ShowPoolCardDetail(pc);
        }

        private void MenuTradeBinder_Click(object sender, RoutedEventArgs e)
        {
            var win = new Windows.BinderWindow { Owner = this };
            win.Show();
        }

        private void MenuKeywordSearch_Click(object sender, RoutedEventArgs e)
            => OpenKeywordSearch();

        private void BtnKeywordSearch_Click(object sender, RoutedEventArgs e)
            => OpenKeywordSearch();

        private void OpenKeywordSearch()
        {
            var win = new Windows.KeywordSearchWindow { Owner = this };
            win.Show();
        }

        /// <summary>
        /// Called by KeywordSearchWindow to add a card directly to the active deck.
        /// </summary>
        public void AddPoolCardToActiveDeck(PoolCard pc)
        {
            if (_activeDeck == null)
            {
                MessageBox.Show("No deck is open. Create or open a deck first.",
                    "No Deck Open", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var deckCard = DeckService.FromPoolCard(pc);
            if (deckCard == null) return;
            _activeDeck.Cards.Add(deckCard);
            AutoSaveDeck(_activeDeck);
            UpdateDeckSummary(_activeDeck);
            RefreshBottom();
        }

        private void ShowCollectionRowDetail(CollectionDisplayRow c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = c.PowerToughness;
            DetailPTLabel.Text = "POWER / TOUGHNESS";
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = FormatCollectionPrices(c.PriceUsd, c.PriceUsdFoil);
            RenderManaCost(c.ManaCost);
            LoadSetSymbol(c.SetCode);
            DetailLegalityPanel.Children.Clear();
        }

        private void ClearDetailPanel()
        {
            DetailName.Text = string.Empty;
            DetailType.Text = string.Empty;
            DetailSet.Text = string.Empty;
            DetailCollectorNumber.Text = string.Empty;
            DetailRarity.Text = string.Empty;
            DetailOracle.Text = string.Empty;
            DetailFlavor.Text = string.Empty;
            DetailArtist.Text = string.Empty;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = string.Empty;
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            DetailSetSymbol.Source = null;
            DetailLegalityPanel.Children.Clear();
            SetPlaceholderImage();
        }

        // ════════════════════════════════════════════════════════════════════
        // MANA COST RENDERING
        // ════════════════════════════════════════════════════════════════════
        private void RenderManaCost(string manaCost)
        {
            DetailManaCostPanel.Children.Clear();
            if (string.IsNullOrWhiteSpace(manaCost)) return;

            foreach (var token in ParseManaSymbols(manaCost))
            {
                string file = Path.Combine(ManaSymbolsFolder,
                    $"{SanitizeSymbol(token)}.png");

                if (File.Exists(file))
                {
                    var bmpSource = LoadBitmap(file);
                    if (bmpSource != null)
                    {
                        DetailManaCostPanel.Children.Add(new Image
                        {
                            Width = 16,
                            Height = 16,
                            Margin = new Thickness(1, 0, 1, 0),
                            Source = bmpSource,
                            ToolTip = token
                        });
                        continue;
                    }
                }
                DetailManaCostPanel.Children.Add(new TextBlock
                {
                    Text = token,
                    FontSize = 12,
                    Margin = new Thickness(1, 0, 1, 0),
                    Foreground = System.Windows.Media.Brushes.Gray
                });
            }
        }

        private static List<string> ParseManaSymbols(string cost)
        {
            var list = new List<string>();
            int i = 0;
            while (i < cost.Length)
            {
                if (cost[i] == '{')
                {
                    int end = cost.IndexOf('}', i);
                    if (end > i)
                    {
                        list.Add(cost.Substring(i, end - i + 1));
                        i = end + 1;
                        continue;
                    }
                }
                i++;
            }
            return list;
        }

        private static string SanitizeSymbol(string sym) =>
            sym.Replace("{", "").Replace("}", "").Replace("/", "-");

        // ════════════════════════════════════════════════════════════════════
        // LEGALITY RENDERING
        // ════════════════════════════════════════════════════════════════════
        private void RenderLegality(string json)
        {
            DetailLegalityPanel.Children.Clear();
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var formats = new[]
                {
                    "standard", "pioneer", "modern",
                    "legacy", "vintage", "commander", "pauper"
                };
                foreach (var fmt in formats)
                {
                    if (!doc.RootElement.TryGetProperty(fmt, out var val))
                        continue;
                    string status = val.GetString() ?? "not_legal";
                    string icon = status switch
                    {
                        "legal" => "✅",
                        "restricted" => "🔵",
                        _ => "❌"
                    };
                    var row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 1, 0, 1)
                    };
                    row.Children.Add(new TextBlock
                    {
                        Text = icon,
                        FontSize = 10,
                        Width = 18,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = CapFirst(fmt),
                        FontSize = 12,
                        Foreground = (System.Windows.Media.Brush)
                            Application.Current.Resources["PrimaryTextBrush"],
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    DetailLegalityPanel.Children.Add(row);
                }
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════
        // SET SYMBOL
        // ════════════════════════════════════════════════════════════════════
        private void LoadSetSymbol(string setCode)
        {
            if (string.IsNullOrWhiteSpace(setCode))
            { DetailSetSymbol.Source = null; return; }

            // Try PNG first, then SVG
            string pngPath = Path.Combine(SetSymbolsFolder,
                $"{setCode.ToLower()}.png");
            string svgPath = Path.Combine(SetSymbolsFolder,
                $"{setCode.ToLower()}.svg");

            if (File.Exists(pngPath))
            {
                DetailSetSymbol.Source = LoadBitmap(pngPath);
                return;
            }

            if (File.Exists(svgPath))
            {
                DetailSetSymbol.Source = LoadSvg(svgPath);
                return;
            }

            DetailSetSymbol.Source = null;
        }

        // ════════════════════════════════════════════════════════════════════
        // CARD IMAGE
        // ════════════════════════════════════════════════════════════════════

        // Stored back face info for current detail card — for flip button
        private string _currentBackLocalPath = string.Empty;
        private string _currentBackUrl = string.Empty;
        private bool _showingBackFace = false;

        internal async Task LoadCardImageAsync(string localPath, string remoteUrl,
            string backLocalPath = "", string backUrl = "")
        {
            // Reset flip state
            _showingBackFace = false;
            _currentBackLocalPath = backLocalPath;
            _currentBackUrl = backUrl;

            bool hasDfc = !string.IsNullOrEmpty(backLocalPath)
                          || !string.IsNullOrEmpty(backUrl);

            BtnFlipCard.Visibility = hasDfc
                ? Visibility.Visible : Visibility.Collapsed;
            BtnFlipCard.Content = "🔄 Show Back Face";

            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            { SetCardImage(localPath); return; }

            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                await SetCardImageFromUrlAsync(remoteUrl);
                return;
            }

            SetPlaceholderImage();
        }

        private void BtnFlipCard_Click(object sender, RoutedEventArgs e)
        {
            _showingBackFace = !_showingBackFace;

            if (_showingBackFace)
            {
                BtnFlipCard.Content = "🔄 Show Front Face";
                if (!string.IsNullOrEmpty(_currentBackLocalPath)
                    && File.Exists(_currentBackLocalPath))
                    SetCardImage(_currentBackLocalPath);
                else if (!string.IsNullOrEmpty(_currentBackUrl))
                    _ = SetCardImageFromUrlAsync(_currentBackUrl);
                else
                {
                    // No back face image yet — flip back
                    _showingBackFace = false;
                    BtnFlipCard.Content = "🔄 Show Back Face";
                    MessageBox.Show(
                        "Back face image not yet downloaded.\n" +
                        "Add this card to your collection or pool to cache it.",
                        "Back Face Unavailable",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                BtnFlipCard.Content = "🔄 Show Back Face";
                // Reload the front face — find it from DetailName
                // Just re-select the current row to reload
                var selected = TopDataGrid.SelectedItem ?? BottomDataGrid.SelectedItem;
                if (selected != null)
                    _ = HandleSelectionAsync(selected);
            }
        }

        private void SetCardImage(string path)
        {
            try
            {
                var bmp = LoadBitmap(path);
                if (bmp != null) CardImage.Source = bmp;
                else SetPlaceholderImage();
            }
            catch { SetPlaceholderImage(); }
        }

        private async Task SetCardImageFromUrlAsync(string url)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(
                    TimeSpan.FromSeconds(3));
                using var http = new System.Net.Http.HttpClient();
                var bytes = await http.GetByteArrayAsync(url, cts.Token);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new System.IO.MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                Dispatcher.Invoke(() => CardImage.Source = bmp);
            }
            catch { SetPlaceholderImage(); }
        }

        private void SetPlaceholderImage()
        {
            try
            {
                string fallback = ImageUnavailablePath;
                if (File.Exists(fallback))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(fallback, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    CardImage.Source = bmp;
                }
                else
                    CardImage.Source = null;
            }
            catch { CardImage.Source = null; }
        }

        private BreakersOfE.Windows.CardImageWindow? _cardImageWindow = null;

        private void CardImage_MouseDoubleClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            if (CardImage.Source == null) return;

            // Close existing window if open
            if (_cardImageWindow != null && _cardImageWindow.IsVisible)
            {
                _cardImageWindow.Close();
                _cardImageWindow = null;
                return;
            }

            string name = DetailName.Text;

            // Load back face image if available
            System.Windows.Media.ImageSource? backSource = null;
            if (!string.IsNullOrEmpty(_currentBackLocalPath)
                && File.Exists(_currentBackLocalPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(_currentBackLocalPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    backSource = bmp;
                }
                catch { }
            }

            _cardImageWindow = new BreakersOfE.Windows.CardImageWindow(
                CardImage.Source, name, backSource)
            { Owner = this };
            _cardImageWindow.Closed += (s, ev) => _cardImageWindow = null;
            _cardImageWindow.Show();
        }

        private static System.Windows.Media.ImageSource? LoadBitmap(string path)
        {
            try
            {
                // Check if file is SVG by extension first
                if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    return LoadSvg(path);

                // Check if file saved as .png is actually SVG content
                byte[] header = new byte[5];
                using (var fs = File.OpenRead(path))
                    fs.Read(header, 0, 5);

                string h = System.Text.Encoding.UTF8.GetString(header);
                if (h.TrimStart().StartsWith("<"))
                    return LoadSvg(path); // SVG disguised as PNG

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private static System.Windows.Media.ImageSource? LoadSvg(string path)
        {
            try
            {
                var settings = new WpfDrawingSettings
                {
                    IncludeRuntime = true,
                    TextAsGeometry = false
                };

                var converter = new FileSvgConverter(settings);
                converter.Convert(path);

                if (converter.Drawing != null)
                    return new System.Windows.Media.DrawingImage(
                        converter.Drawing);
            }
            catch { }
            return null;
        }

        // ════════════════════════════════════════════════════════════════════
        // STATUS / ROW COUNT
        // ════════════════════════════════════════════════════════════════════
        private void SetStatus(string msg)
        {
            if (StatusText != null) StatusText.Text = msg;
        }

        private void UpdateRowCount(int count, string label) { }

        // ════════════════════════════════════════════════════════════════════
        // STRING HELPERS
        // ════════════════════════════════════════════════════════════════════
        private static string CapFirst(string s) =>
            string.IsNullOrEmpty(s)
                ? s : char.ToUpper(s[0]) + s[1..];

        private static string BuildFoilStr(bool foil, bool nonFoil)
        {
            if (foil && nonFoil) return "Foil · Non-Foil";
            if (foil) return "Foil only";
            if (nonFoil) return "Non-Foil only";
            return string.Empty;
        }

        private static string FormatPrices(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var sb = new System.Text.StringBuilder();
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind !=
                        System.Text.Json.JsonValueKind.Null)
                        sb.AppendLine($"{p.Name}: ${p.Value.GetString()}");
                return sb.ToString().Trim();
            }
            catch { return string.Empty; }
        }

        private static string FormatCollectionPrices(
            decimal? usd, decimal? usdFoil)
        {
            var sb = new System.Text.StringBuilder();
            if (usd.HasValue)
                sb.AppendLine($"USD:       ${usd.Value:F2}");
            if (usdFoil.HasValue)
                sb.AppendLine($"USD Foil:  ${usdFoil.Value:F2}");
            return sb.ToString().Trim();
        }

        // ════════════════════════════════════════════════════════════════════
        // MENU HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void MenuTabletop_Click(object sender, RoutedEventArgs e)
        {
            // Put active deck first so it pre-populates as Player 1
            var decks = new List<Deck>();
            if (_activeDeck != null) decks.Add(_activeDeck);
            foreach (var d in _openDecks)
                if (d != _activeDeck) decks.Add(d);

            var win = new Windows.TabletopWindow(decks) { Owner = this };
            win.Show();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        private void MenuPreferences_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Windows.PreferencesWindow { Owner = this };
            dlg.ShowDialog();
        }

        private void MenuThemeLight_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Apply(AppTheme.Light);
            BtnTheme.Content = ThemeService.ThemeToggleIcon;
            BtnTheme.ToolTip = ThemeService.ThemeToggleTooltip;
        }

        private void MenuThemeDark_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Apply(AppTheme.Dark);
            BtnTheme.Content = ThemeService.ThemeToggleIcon;
            BtnTheme.ToolTip = ThemeService.ThemeToggleTooltip;
        }

        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Toggle();
            BtnTheme.Content = ThemeService.ThemeToggleIcon;
            BtnTheme.ToolTip = ThemeService.ThemeToggleTooltip;
        }

        private void BtnUpdateDb_Click(object sender, RoutedEventArgs e) =>
            MenuUpdateDatabase_Click(sender, e);

        private void MenuUpdateDatabase_Click(object sender, RoutedEventArgs e)
        {
            var win = new BreakersOfE.Windows.UpdateDatabaseWindow
            { Owner = this };
            if (win.ShowDialog() == true)
            {
                LoadCaches();      // ← refresh cache after update
                LoadCurrentMode();
            }
        }

        private void UpdateSummaryRow(List<CollectionDisplayRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                BottomSummaryGrid.ItemsSource = null;
                return;
            }

            rows.RemoveAll(r => r.IsFooter);

            int totalNonFoil = rows.Sum(r => r.Quantity);
            int totalFoils = rows.Sum(r => r.FoilQuantity);
            int totalUsed = rows.Sum(r => r.UsedCount);
            int totalAvail = rows.Sum(r => r.AvailableCount);
            decimal totalVal = rows.Sum(r => r.TotalValue);
            decimal totalMkt = rows.Where(r => r.MarketValue.HasValue)
                                    .Sum(r => r.MarketValue!.Value * (r.Quantity + r.FoilQuantity));
            decimal totalBuyAt = rows.Where(r => r.BuyAt.HasValue)
                                       .Sum(r => r.BuyAt!.Value);
            decimal totalSellAt = rows.Where(r => r.SellAt.HasValue)
                                       .Sum(r => r.SellAt!.Value);
            decimal totalSellVal = rows.Where(r => r.SellAtValue.HasValue)
                                       .Sum(r => r.SellAtValue!.Value);
            int totalNeeded = rows.Sum(r => r.Needed);
            int totalExcess = rows.Sum(r => r.Excess);
            int totalTarget = rows.Sum(r => r.Target);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _wiredSummaries.Remove((BottomDataGrid, BottomSummaryGrid));
                SyncAndPopulateCollectionSummary(
                    BottomSummaryGrid, BottomDataGrid,
                    totalNonFoil, totalFoils, totalUsed, totalAvail,
                    totalVal, totalMkt, totalBuyAt, totalSellAt,
                    totalSellVal, totalNeeded, totalExcess, totalTarget);
                WireSummaryColumnSync(BottomDataGrid, BottomSummaryGrid);
            }),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private readonly HashSet<(DataGrid, DataGrid)> _wiredSummaries = new();

        private void WireSummaryColumnSync(DataGrid src, DataGrid summary)
        {
            var pair = (src, summary);
            if (_wiredSummaries.Contains(pair)) return;
            _wiredSummaries.Add(pair);

            var descriptor = System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.ActualWidthProperty,
                    typeof(DataGridColumn));

            for (int i = 0; i < src.Columns.Count; i++)
            {
                int idx = i; // capture for closure
                var srcCol = src.Columns[i];
                descriptor.AddValueChanged(srcCol, (s, e) =>
                {
                    if (idx < summary.Columns.Count)
                        summary.Columns[idx].Width =
                            new DataGridLength(srcCol.ActualWidth);
                });
            }
        }

        private static void SyncAndPopulateCollectionSummary(
            DataGrid sumGrid, DataGrid srcGrid,
            int nonFoil, int foil, int used, int avail, decimal value,
            decimal mktValue, decimal buyAt, decimal sellAt,
            decimal sellAtValue, int needed, int excess, int target)
        {
            // Force green background on the summary row — override the DataGridRowStyle
            // which would otherwise try to bind RowBackgroundBrush (not on FooterRow)
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty,
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xD6, 0xE8, 0xD6))));
            rowStyle.Setters.Add(new Setter(DataGridRow.ForegroundProperty,
                System.Windows.Media.Brushes.Black));
            rowStyle.Setters.Add(new Setter(DataGridRow.FontWeightProperty,
                FontWeights.SemiBold));
            sumGrid.RowStyle = rowStyle;


            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty,
                (System.Windows.Media.Brush)Application.Current.FindResource("BorderBrush")));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty,
                new Thickness(0, 0, 1, 1)));
            cellStyle.Setters.Add(new Setter(DataGridCell.PaddingProperty,
                new Thickness(4, 2, 4, 2)));
            sumGrid.CellStyle = cellStyle;

            sumGrid.Columns.Clear();

            // Map header names to FooterRow property names — handles both grids
            var bindings = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                { "Qty",          "Qty" },
                { "Foil",         "FoilQty" },
                { "Foil Qty",     "FoilQty" },
                { "Used",         "Used" },
                { "Avail",        "Available" },
                { "Available",    "Available" },
                { "Value",        "TotalValue" },
                { "Total Value",  "TotalValue" },
                { "Market Value", "MarketValue" },
                { "Market...",    "MarketValue" },
                { "Buy At",       "BuyAt" },
                { "Sell At",      "SellAt" },
                { "Sell At Value","SellAtValue" },
                { "Sell...",      "SellAtValue" },
                { "Price High",   "PriceHigh" },
                { "Needed",       "Needed" },
                { "Excess",       "Excess" },
                { "Target",       "Target" },
            };

            foreach (var col in srcGrid.Columns)
            {
                string hdr = col.Header?.ToString() ?? string.Empty;
                var sc = new DataGridTextColumn
                {
                    Width = col.Width,
                    IsReadOnly = true,
                    ElementStyle = new Style(typeof(TextBlock))
                    {
                        Setters = { new Setter(
                            TextBlock.TextAlignmentProperty,
                            bindings.ContainsKey(hdr)
                                ? TextAlignment.Right
                                : TextAlignment.Left) }
                    }
                };
                if (bindings.TryGetValue(hdr, out var prop))
                    sc.Binding = new System.Windows.Data.Binding(prop);
                sumGrid.Columns.Add(sc);
            }

            sumGrid.ItemsSource = new[]
            {
                new Models.FooterRow
                {
                    Qty         = nonFoil.ToString("N0"),
                    FoilQty     = foil.ToString("N0"),
                    Used        = used.ToString("N0"),
                    Available   = avail.ToString("N0"),
                    TotalValue  = $"${value:F2}",
                    MarketValue = mktValue  > 0 ? $"${mktValue:F2}"  : "",
                    BuyAt       = buyAt     > 0 ? $"${buyAt:F2}"     : "",
                    SellAt      = sellAt    > 0 ? $"${sellAt:F2}"    : "",
                    SellAtValue = sellAtValue > 0 ? $"${sellAtValue:F2}" : "",
                    Needed      = needed > 0 ? needed.ToString("N0") : "",
                    Excess      = excess > 0 ? excess.ToString("N0") : "",
                    Target      = target > 0 ? target.ToString("N0") : "",
                }
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // COLUMN HEADER FUNNEL CLICK
        // ════════════════════════════════════════════════════════════════════
        private void ColumnFunnel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            var header = FindParent<DataGridColumnHeader>(btn);
            if (header == null) return;

            bool isTop = IsParentGrid(header, TopDataGrid);
            var grid = isTop ? TopDataGrid : BottomDataGrid;
            var filters = isTop ? _topColumnFilters : _bottomColumnFilters;

            string columnName = btn.Tag?.ToString() ?? string.Empty;
            string propName = GetPropertyNameForColumn(columnName, isTop);
            if (string.IsNullOrEmpty(propName)) return;

            // Always get values from the UNFILTERED source so all options
            // are visible even when a filter is already active
            var values = GetUniqueValuesFromCache(propName, isTop);
            var state = filters.GetOrCreate(columnName, propName);

            _activeFilterPopup?.Close();

            var popup = new BreakersOfE.Windows.ColumnFilterPopup(
                columnName, propName, values, state)
            { Owner = this };

            var screenPos = btn.PointToScreen(new Point(0, btn.ActualHeight));
            popup.Left = screenPos.X;
            popup.Top = screenPos.Y;

            popup.FilterChanged += (s, ev) =>
            {
                UpdateFunnelIcon(header, state.IsActive);
                if (isTop)
                {
                    ApplyTopColumnFilters();
                    TopFilterSummary.Text = BuildFilterSummary(isTop: true);
                    if (ChkTopFilter != null) ChkTopFilter.IsChecked = TopFilterSummary.Text != "No filter active";
                }
                else
                {
                    ApplyBottomColumnFilters();
                    BottomFilterSummary.Text = BuildFilterSummary(isTop: false);
                    if (ChkBottomFilter != null) ChkBottomFilter.IsChecked = BottomFilterSummary.Text != "No filter active";
                }
            };

            _activeFilterPopup = popup;
            popup.Show();
            e.Handled = true;
        }

        private void UpdateFunnelIcon(
            DataGridColumnHeader header, bool isActive)
        {
            if (header.Template.FindName(
                    "FunnelIcon", header) is TextBlock icon)
                icon.Foreground = isActive
                    ? new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4))
                    : System.Windows.Media.Brushes.Gray;

            if (header.Template.FindName(
                    "FunnelButton", header) is Button btn)
            {
                if (isActive)
                {
                    // Filter active — pin the button visible permanently
                    btn.Visibility = Visibility.Visible;
                }
                else
                {
                    // Filter cleared — remove local value so the
                    // IsMouseOver trigger can take control again
                    btn.ClearValue(Button.VisibilityProperty);
                }
            }
        }

        private void ResetAllFunnelIcons(DataGrid grid)
        {
            // Walk the visual tree to find all DataGridColumnHeaders
            // and reset their funnel icon state
            foreach (var col in grid.Columns)
            {
                var header = GetColumnHeader(grid, col);
                if (header != null)
                    UpdateFunnelIcon(header, false);
            }
        }

        private static DataGridColumnHeader? GetColumnHeader(
            DataGrid grid, DataGridColumn column)
        {
            if (grid.ItemContainerGenerator.Status !=
                System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                return null;

            return GetVisualChild<DataGridColumnHeader>(grid, h =>
                h.Column == column);
        }

        private static T? GetVisualChild<T>(
            DependencyObject parent, Func<T, bool>? predicate = null)
            where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper
                .GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper
                    .GetChild(parent, i);
                if (child is T typed &&
                    (predicate == null || predicate(typed)))
                    return typed;

                var result = GetVisualChild(child, predicate);
                if (result != null) return result;
            }
            return null;
        }

        private List<string> GetUniqueValuesFromCache(
            string propName, bool isTop)
        {
            System.Collections.IEnumerable? source = null;

            if (isTop)
            {
                source = _currentMode switch
                {
                    "PoolToCollection" or "PoolToDeck" or
                    "PoolToWantList" or "PoolToPlanechase" or
                    "PoolToArchenemy" or "PoolToVanguard" or
                    "PoolToTokens" or "PoolToArtSeries" or
                    "PoolToConspiracy" => _poolCache,
                    _ => null
                };
            }
            else
            {
                // Bottom table — always read full collection from DB
                using var cdb = new Data.CollectionDbContext();
                using var pdb = new AppDbContext();
                source = BuildCollectionRows(cdb, pdb);
            }

            // Fall back to grid ItemsSource if no source found
            if (source == null)
            {
                var grid = isTop ? TopDataGrid : BottomDataGrid;
                return GetUniqueValues(grid, propName);
            }

            var values = new HashSet<string>();
            foreach (var item in source)
            {
                var prop = item.GetType().GetProperty(propName,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase);
                string? val = prop?.GetValue(item)?.ToString();
                values.Add(val ?? string.Empty);
            }
            return values.OrderBy(v => v).ToList();
        }

        private static List<string> GetUniqueValues(
            DataGrid grid, string propName)
        {
            var values = new System.Collections.Generic.HashSet<string>();
            if (grid.ItemsSource == null) return new List<string>();

            foreach (var item in
                (System.Collections.IEnumerable)grid.ItemsSource)
            {
                var prop = item.GetType().GetProperty(propName,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase);

                string? val = prop?.GetValue(item)?.ToString();
                values.Add(val ?? string.Empty);
            }

            return values.OrderBy(v => v).ToList();
        }

        private static string GetPropertyNameForColumn(
            string columnName, bool isTop)
        {
            return columnName switch
            {
                "Name" => "Name",
                "Edition" => "SetCode",
                "Edition Name" => "SetName",
                "Color" => "ColorDisplay",
                "Type" => "TypeLine",
                "Rarity" => "RarityCode",
                "Cost" => "ManaCost",
                "P/T" => "PowerToughness",
                "Text" => "OracleText",
                "Flavor" => "FlavorText",
                "Artist" => "Artist",
                "No" => "CollectorNumber",
                "Power" => "Power",
                "Toughness" => "Toughness",
                "CMC" => "ManaValue",
                "USD" => "PriceUsdDisplay",
                "USD Foil" => "PriceUsdFoilDisplay",
                "Qty" => "Quantity",
                "Foil Qty" => "FoilQuantity",
                "Used" => "UsedCount",
                "Available" => "AvailableCount",
                "Value" => "ValueDisplay",
                "Foil Value" => "FoilValueDisplay",
                "Total Value" => "TotalValueDisplay",
                "Condition" => "Condition",
                "Language" => "Language",
                "Storage" => "StorageLocation",
                _ => string.Empty
            };
        }

        private void ApplyTopColumnFilters()
        {
            LoadCurrentMode();
        }

        private void ApplyBottomColumnFilters()
        {
            RefreshBottom();
        }

        private void UpdateColumnFilterSummary(bool isTop)
        {
            var filters = isTop ? _topColumnFilters : _bottomColumnFilters;
            var summaryLabel = isTop ? TopFilterSummary : BottomFilterSummary;
            var exprNode = isTop ? _topFilterNode : _bottomFilterNode;

            var activeFilters = filters.GetActiveFilters();
            var exprSummary = FilterExpressionService.Summarize(exprNode);

            if (activeFilters.Count == 0 && string.IsNullOrEmpty(exprSummary))
            {
                summaryLabel.Text = "No filter active";
                return;
            }

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(exprSummary))
                parts.Add(exprSummary);

            foreach (var f in activeFilters)
            {
                if (f.UseTextFilter)
                    parts.Add($"{f.ColumnName}: " +
                              $"{ColumnFilterState.GetOperatorLabel(f.TextOperator)}" +
                              $" \"{f.TextValue}\"");
                else if (!f.AllSelected)
                {
                    string vals = string.Join(", ", f.SelectedValues.Take(3));
                    if (f.SelectedValues.Count > 3)
                        vals += $" (+{f.SelectedValues.Count - 3})";
                    parts.Add($"{f.ColumnName}: {vals}");
                }
            }

            summaryLabel.Text = parts.Count > 0
                ? string.Join("  •  ", parts)
                : "No filter active";
        }

        private static T? FindParent<T>(DependencyObject child)
            where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper
                .GetParent(child);
            while (parent != null)
            {
                if (parent is T typed) return typed;
                parent = System.Windows.Media.VisualTreeHelper
                    .GetParent(parent);
            }
            return null;
        }

        private static bool IsParentGrid(
            DependencyObject element, DataGrid grid)
        {
            var parent = System.Windows.Media.VisualTreeHelper
                .GetParent(element);
            while (parent != null)
            {
                if (parent == grid) return true;
                parent = System.Windows.Media.VisualTreeHelper
                    .GetParent(parent);
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // COLUMN CHOOSER
        // ════════════════════════════════════════════════════════════════════
        private void BtnColumnChooserTop_Click(object sender, RoutedEventArgs e)
        {
            var popup = new BreakersOfE.Windows.ColumnChooserPopup(
                TopDataGrid, GetTableKey(TopDataGrid))
            { Owner = this };
            var btn = sender as Button;
            var screenPos = btn!.PointToScreen(
                new Point(0, btn.ActualHeight));
            popup.Left = screenPos.X;
            popup.Top = screenPos.Y;
            popup.Show();
        }

        private void BtnColumnChooserBottom_Click(
            object sender, RoutedEventArgs e)
        {
            var popup = new BreakersOfE.Windows.ColumnChooserPopup(
                BottomDataGrid, GetTableKey(BottomDataGrid))
            { Owner = this };
            var btn = sender as Button;
            var screenPos = btn!.PointToScreen(
                new Point(0, btn.ActualHeight));
            popup.Left = screenPos.X;
            popup.Top = screenPos.Y;
            popup.Show();
        }

        private void MenuUpdatePrices_Click(object sender, RoutedEventArgs e)
        {
            var win = new BreakersOfE.Windows.UpdateDatabaseWindow(priceOnly: true)
            { Owner = this };

            if (win.ShowDialog() == true)
            {
                LoadCaches();
                RefreshBottom();
            }
        }

        private void ShowDeckCardDetail(DeckCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = string.Empty;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = c.PowerToughness;
            DetailPTLabel.Text = "POWER / TOUGHNESS";
            DetailFoilNonFoil.Text = BuildFoilStr(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = FormatCollectionPrices(
                c.PriceUsd, c.PriceUsdFoil);
            RenderManaCost(c.ManaCost);
            LoadSetSymbol(c.SetCode);
            DetailLegalityPanel.Children.Clear();
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
            => MenuPreferences_Click(sender, e);

        // ================================================================
        // DOWNLOAD MISSING CARD IMAGES
        // ================================================================
        private bool _downloadInProgress = false;

        private async void MenuDownloadImages_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadInProgress)
            {
                MessageBox.Show("A download is already in progress.",
                    "Download Images", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                "This will download card images for all cards in your collections.\n\n" +
                "The download runs in the background — you can keep using the app.\n\n" +
                "Continue?",
                "Download Missing Card Images",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _downloadInProgress = true;
            MenuDownloadImages.IsEnabled = false;
            SetStatus("Scanning collections for missing images...", 0);
            StatusProgressBar.Visibility = System.Windows.Visibility.Visible;

            try
            {
                await DownloadAllCollectionImagesAsync();
                SetStatus("Image download complete.", 100);
            }
            catch (Exception ex)
            {
                SetStatus($"Download error: {ex.Message}", 0);
            }
            finally
            {
                _downloadInProgress = false;
                MenuDownloadImages.IsEnabled = true;
                await Task.Delay(3000);
                StatusProgressBar.Visibility = System.Windows.Visibility.Collapsed;
                SetStatus("Ready", 0);
            }
        }

        private async Task DownloadAllCollectionImagesAsync()
        {
            var folder = Services.AppFolderService.CardImagesFolder;
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            // Gather all cards from all 6 collection types
            var cards = new List<(string Name, string ImageUrl, Action<string> SetPath)>();

            using var cdb = new Data.CollectionDbContext();
            using var db = new Data.AppDbContext();

            // Standard collection — join to PoolCards
            var poolIds = cdb.CollectionEntries.Select(c => c.PoolId).Distinct().ToList();
            foreach (var id in poolIds)
            {
                var card = db.PoolCards.FirstOrDefault(p => p.PoolId == id);
                if (card != null && !string.IsNullOrEmpty(card.ImageNormalUrl)
                    && (string.IsNullOrEmpty(card.LocalImagePath)
                        || !File.Exists(card.LocalImagePath)))
                {
                    var capturedCard = card;
                    cards.Add((card.Name, card.ImageNormalUrl,
                        path => { capturedCard.LocalImagePath = path; }
                    ));
                }
            }

            // Token collection
            var tokenIds = cdb.TokenCollectionEntries.Select(c => c.TokenId).Distinct().ToList();
            foreach (var id in tokenIds)
            {
                var card = db.TokenCards.FirstOrDefault(p => p.TokenId == id);
                if (card != null && !string.IsNullOrEmpty(card.ImageNormalUrl)
                    && (string.IsNullOrEmpty(card.LocalImagePath)
                        || !File.Exists(card.LocalImagePath)))
                {
                    var capturedCard = card;
                    cards.Add((card.Name, card.ImageNormalUrl,
                        path => { capturedCard.LocalImagePath = path; }
                    ));
                }
            }

            // Planar collection
            var planarIds = cdb.PlanarCollectionEntries.Select(c => c.PlanarId).Distinct().ToList();
            foreach (var id in planarIds)
            {
                var card = db.PlanarCards.FirstOrDefault(p => p.PlanarId == id);
                if (card != null && !string.IsNullOrEmpty(card.ImageNormalUrl)
                    && (string.IsNullOrEmpty(card.LocalImagePath)
                        || !File.Exists(card.LocalImagePath)))
                {
                    var capturedCard = card;
                    cards.Add((card.Name, card.ImageNormalUrl,
                        path => { capturedCard.LocalImagePath = path; }
                    ));
                }
            }

            // Scheme collection
            var schemeIds = cdb.SchemeCollectionEntries.Select(c => c.SchemeId).Distinct().ToList();
            foreach (var id in schemeIds)
            {
                var card = db.SchemeCards.FirstOrDefault(p => p.SchemeId == id);
                if (card != null && !string.IsNullOrEmpty(card.ImageNormalUrl)
                    && (string.IsNullOrEmpty(card.LocalImagePath)
                        || !File.Exists(card.LocalImagePath)))
                {
                    var capturedCard = card;
                    cards.Add((card.Name, card.ImageNormalUrl,
                        path => { capturedCard.LocalImagePath = path; }
                    ));
                }
            }

            // Vanguard collection
            var vanguardIds = cdb.VanguardCollectionEntries.Select(c => c.VanguardId).Distinct().ToList();
            foreach (var id in vanguardIds)
            {
                var card = db.VanguardCards.FirstOrDefault(p => p.VanguardId == id);
                if (card != null && !string.IsNullOrEmpty(card.ImageNormalUrl)
                    && (string.IsNullOrEmpty(card.LocalImagePath)
                        || !File.Exists(card.LocalImagePath)))
                {
                    var capturedCard = card;
                    cards.Add((card.Name, card.ImageNormalUrl,
                        path => { capturedCard.LocalImagePath = path; }
                    ));
                }
            }

            // Art Series collection
            var artIds = cdb.ArtSeriesCollectionEntries.Select(c => c.ArtSeriesId).Distinct().ToList();
            foreach (var id in artIds)
            {
                var card = db.ArtSeriesCards.FirstOrDefault(p => p.ArtSeriesId == id);
                if (card != null && !string.IsNullOrEmpty(card.ImageNormalUrl)
                    && (string.IsNullOrEmpty(card.LocalImagePath)
                        || !File.Exists(card.LocalImagePath)))
                {
                    var capturedCard = card;
                    cards.Add((card.Name, card.ImageNormalUrl,
                        path => { capturedCard.LocalImagePath = path; }
                    ));
                }
            }

            if (cards.Count == 0)
            {
                SetStatus("All collection images already downloaded.", 100);
                return;
            }

            SetStatus($"Downloading {cards.Count} card images...", 0);
            int done = 0, failed = 0;

            foreach (var (name, url, setPath) in cards)
            {
                string safeName = string.Concat(
                    name.Split(Path.GetInvalidFileNameChars()));
                string path = Path.Combine(folder, $"{safeName}.jpg");

                try
                {
                    if (!File.Exists(path))
                    {
                        var bytes = await http.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(path, bytes);
                    }
                    setPath(path);
                    done++;
                }
                catch
                {
                    failed++;
                }

                int pct = (int)((double)(done + failed) / cards.Count * 100);
                SetStatus(
                    $"Downloading images: {done + failed} of {cards.Count}" +
                    (failed > 0 ? $" ({failed} failed)" : ""),
                    pct);
            }

            // Save all LocalImagePath updates to DB
            db.SaveChanges();

            SetStatus(
                $"Done — {done} images downloaded" +
                (failed > 0 ? $", {failed} failed" : "") + ".",
                100);
        }

        private void SetStatus(string text, int progress)
        {
            StatusText.Text = text;
            StatusProgressBar.Value = progress;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F1)
            {
                OpenHelp();
                e.Handled = true;
            }
            else if (e.Key == Key.S &&
                     Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_activeDeck != null)
                    SaveDeck(_activeDeck);
                e.Handled = true;
            }
            else if (e.Key == Key.F3)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    NavigateMatch(forward: false);
                else
                    NavigateMatch(forward: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape &&
                     !string.IsNullOrEmpty(SearchBox.Text))
            {
                BtnClearSearch_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void MainWindow_Closing(object sender,
            System.ComponentModel.CancelEventArgs e)
        {
            var unsaved = _openDecks.Where(d => d.IsModified).ToList();
            if (unsaved.Count == 0) return;

            string names = string.Join("\n  • ", unsaved.Select(d => d.Name));
            var result = MessageBox.Show(
                $"The following deck{(unsaved.Count == 1 ? "" : "s")} have unsaved changes:\n\n" +
                $"  • {names}\n\n" +
                "Save all before closing?",
                "Unsaved Decks",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (result == MessageBoxResult.Yes)
            {
                foreach (var deck in unsaved)
                    AutoSaveDeck(deck);
            }
        }

        private void MenuHelp_Click(object sender, RoutedEventArgs e)
            => OpenHelp();

        private void OpenHelp(string? topic = null)
        {
            var win = new Windows.HelpWindow(topic) { Owner = this };
            win.Show();
        }

        private void MenuKeywordDictionary_Click(object sender, RoutedEventArgs e)
        {
            var win = new Windows.KeywordDictionaryWindow { Owner = this };
            win.Show();
        }

        private void MenuImport_Click(object sender, RoutedEventArgs e)
        {
            var win = new Windows.ImportExportWindow(
                Windows.ImportExportWindow.StartMode.ImportCollection)
            { Owner = this };
            win.ShowDialog();
            if (_currentMode.Contains("Collection") || _currentMode == "PoolToCollection")
                LoadCurrentMode();
        }

        private void MenuExport_Click(object sender, RoutedEventArgs e)
        {
            var win = new Windows.ImportExportWindow(
                Windows.ImportExportWindow.StartMode.ExportCollection, _activeDeck)
            { Owner = this };
            win.ShowDialog();
        }

        private void MenuShowSplash_Click(object sender, RoutedEventArgs e)
        {
            var splash = new Windows.SplashWindow();
            splash.MouseLeftButtonDown += (s, ev) => splash.Close();
            splash.KeyDown += (s, ev) => { if (ev.Key == Key.Escape) splash.Close(); };
            splash.SetStatus("Click anywhere or press Escape to close", 100);
            splash.Show();
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Windows.AboutWindow { Owner = this };
            dlg.ShowDialog();
        }

        private static void OpenFolder(string path) =>
            System.Diagnostics.Process.Start("explorer.exe", path);

        private void MenuOpenAppFolder_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.RootFolder);

        private void MenuOpenFolder_Root_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.RootFolder);
        private void MenuOpenFolder_Decks_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.DecksFolder);
        private void MenuOpenFolder_CardImages_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.CardImagesFolder);
        private void MenuOpenFolder_Exports_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.ExportsFolder);
        private void MenuOpenFolder_Imports_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.ImportsFolder);
        private void MenuOpenFolder_Filters_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.FiltersFolder);
        private void MenuOpenFolder_Collection_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.CollectionFolder);
        private void MenuOpenFolder_Playmats_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.PlaymatImagesFolder);
        private void MenuOpenFolder_Sleeves_Click(object sender, RoutedEventArgs e)
            => OpenFolder(Services.AppFolderService.SleeveImagesFolder);

        // ── Recent Decks ──────────────────────────────────────────────────────
        private void LoadRecentDecks()
        {
            string raw = GetSetting(RecentDecksKey) ?? string.Empty;
            if (string.IsNullOrEmpty(raw)) return;

            var paths = raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
                           .Where(System.IO.File.Exists)
                           .ToList();

            MenuNoRecentDecks.Visibility = paths.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;

            // Remove old dynamic items (keep the placeholder)
            var toRemove = MenuRecentDecks.Items
                .OfType<MenuItem>()
                .Where(m => m.Name != "MenuNoRecentDecks")
                .ToList();
            foreach (var m in toRemove)
                MenuRecentDecks.Items.Remove(m);

            foreach (var path in paths)
            {
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                var item = new MenuItem { Header = label, ToolTip = path };
                item.Click += (s, e) => OpenDeckFromPath(path);
                MenuRecentDecks.Items.Add(item);
            }
        }

        private void AddToRecentDecks(string filePath)
        {
            string raw = GetSetting(RecentDecksKey) ?? string.Empty;
            var paths = raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
                           .Where(p => !p.Equals(filePath,
                               StringComparison.OrdinalIgnoreCase))
                           .Prepend(filePath)
                           .Take(MaxRecentDecks)
                           .ToList();
            SaveSetting(RecentDecksKey, string.Join("|", paths));
            LoadRecentDecks();
        }

        private void OpenDeckFromPath(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                MessageBox.Show($"Deck file not found:\n{path}",
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadRecentDecks(); // refresh list to remove missing file
                return;
            }
            var existing = _openDecks.FirstOrDefault(d => d.FilePath == path);
            if (existing != null) { SelectDeckTab(existing); return; }
            try
            {
                var deck = DeckService.Load(path);
                if (deck == null) return;
                _openDecks.Add(deck);
                AddDeckTab(deck);
                AddToRecentDecks(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open deck:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}