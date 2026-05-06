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

        // ── Asset folders ────────────────────────────────────────────────────
        private string SetSymbolsFolder =>
            Services.AppFolderService.SetSymbolsFolder;

        private string ManaSymbolsFolder =>
            Services.AppFolderService.ManaSymbolsFolder;

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
            => MessageBox.Show("Deck Legality window coming soon!",
                "Coming Soon", MessageBoxButton.OK, MessageBoxImage.Information);

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
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card && !card.IsFooter)
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

        private void BtnColumnChooser_Click(object sender, RoutedEventArgs e)
        {
            var grid = _bottomTableHasFocus
                ? (DataGrid)BottomDataGrid
                : TopDataGrid;
            var popup = new BreakersOfE.Windows.ColumnChooserPopup(grid)
            { Owner = this };
            var btn = sender as Button;
            var screenPos = btn!.PointToScreen(new Point(0, btn.ActualHeight));
            popup.Left = screenPos.X;
            popup.Top = screenPos.Y;
            popup.Show();
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
                    if (!foil && cr.Quantity == 0 && cr.FoilQuantity > 0) foil = true;

                    // Block if no copies available
                    if (_currentMode == "CollectionToDeck" && cr.AvailableCount <= 0)
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
                    TopSummaryGrid, TopDataGrid, nf, f, u, a, v);
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

            _openDecks.Add(deck);
            AddDeckTab(deck);
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
                // No file yet — prompt once to establish path, then save
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Deck Before Continuing",
                    Filter = "Deck Files (*.deck)|*.deck",
                    InitialDirectory = Services.AppFolderService.DecksFolder,
                    FileName = Services.AppFolderService.SafeFileName(deck.Name)
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
            MessageBox.Show("Deck Statistics window coming in Round 5!",
                "Coming Soon", MessageBoxButton.OK,
                MessageBoxImage.Information);
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
                UpdateBottomTableLabel();
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

            // Columns
            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "ES",
                Width = new DataGridLength(32),
                CanUserResize = false,
                CellTemplate = CreateSetSymbolTemplate()
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Binding = new System.Windows.Data.Binding("Name"),
                Width = new DataGridLength(200)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Edition",
                Binding = new System.Windows.Data.Binding("SetCode"),
                Width = new DataGridLength(60)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Qty",
                Binding = new System.Windows.Data.Binding("TotalQuantity"),
                Width = new DataGridLength(45),
                IsReadOnly = true
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Non-Foil",
                Binding = new System.Windows.Data.Binding("Quantity")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger =
                        System.Windows.Data.UpdateSourceTrigger.LostFocus
                },
                Width = new DataGridLength(58),
                IsReadOnly = false
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Foil",
                Binding = new System.Windows.Data.Binding("FoilQuantity")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger =
                        System.Windows.Data.UpdateSourceTrigger.LostFocus
                },
                Width = new DataGridLength(45),
                IsReadOnly = false
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Category",
                Binding = new System.Windows.Data.Binding("CategoryDisplay"),
                Width = new DataGridLength(85)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Color",
                Binding = new System.Windows.Data.Binding("ColorDisplay"),
                Width = new DataGridLength(55)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Type",
                Binding = new System.Windows.Data.Binding("TypeLine"),
                Width = new DataGridLength(160)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Rarity",
                Binding = new System.Windows.Data.Binding("RarityCode"),
                Width = new DataGridLength(50)
            });

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Cost",
                Width = new DataGridLength(110),
                CellTemplate = CreateManaCostTemplate()
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "P/T",
                Binding = new System.Windows.Data.Binding("PowerToughness"),
                Width = new DataGridLength(55)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "USD",
                Binding = new System.Windows.Data.Binding("PriceUsdDisplay"),
                Width = new DataGridLength(70)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Value",
                Binding = new System.Windows.Data.Binding("ValueDisplay"),
                Width = new DataGridLength(70)
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

        // ── Refresh deck grid ─────────────────────────────────────────────────────
        private static void RefreshDeckGrid(DataGrid grid, Deck deck)
        {
            // Commanders first (blue highlight), then rest sorted by category/name
            var cards = deck.Cards
                .Where(c => !c.IsFooter)
                .OrderBy(c => c.IsCommander ? 0 : 1)
                .ThenBy(c => c.Category)
                .ThenBy(c => c.Name)
                .ToList();

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

            // Switch to collection tab
            DeckTabControl.SelectedIndex = 0;
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

            var deckCard = DeckService.FromPoolCard(card);
            deckCard.IsFoil = foil && card.IsFoil;

            // Check if card already in deck — offer to increment qty
            var existing = _activeDeck.Cards.FirstOrDefault(c =>
                c.PoolId == card.PoolId && c.IsFoil == deckCard.IsFoil &&
                c.Category == category);
            if (existing != null)
            {
                var result = MessageBox.Show(
                    $"'{card.Name}' is already in the deck ({existing.Quantity} copies).\n\n" +
                    $"Press 'Yes' to add another copy\n" +
                    $"Press 'No' to skip",
                    "Card Already in Deck",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result == MessageBoxResult.No) return;
            }

            bool added = DeckService.AddCard(
                _activeDeck, deckCard, category, out string error);

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

            // Auto-foil if only foil available
            bool foil = row.Quantity == 0 && row.FoilQuantity > 0;

            // Block if no copies available
            if (_currentMode == "CollectionToDeck" && row.AvailableCount <= 0)
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

        private void RefreshActiveDeckGrid()
        {
            if (_activeDeck == null) return;

            if (DeckTabControl.SelectedItem is TabItem tab &&
                tab.Content is Grid container)
            {
                var mainGrid = container.Children.OfType<DataGrid>()
                    .FirstOrDefault(g => g.Name == "MainDeckGrid");

                if (mainGrid != null)
                {
                    RefreshDeckGrid(mainGrid, _activeDeck);

                    var deck = _activeDeck;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _wiredSummaries.Remove((mainGrid, BottomSummaryGrid));
                        SyncAndPopulateDeckSummary(
                            BottomSummaryGrid, mainGrid, deck);
                        WireSummaryColumnSync(mainGrid, BottomSummaryGrid);
                    }),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
                }

                UpdateBottomTableLabel();
            }
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
                { "USD",      "PriceUsdDisplay" }
            };

            foreach (var col in srcGrid.Columns)
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
                }
            };
        }


        // ── Update used count ─────────────────────────────────────────────────────
        private void UpdateUsedCount(int poolId, int delta)
        {
            if (poolId <= 0) return;
            using var db = new Data.AppDbContext();
            var entry = db.CollectionEntries
                .FirstOrDefault(c => c.PoolId == poolId);
            if (entry == null) return;

            entry.UsedCount = Math.Max(0, entry.UsedCount + delta);
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
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card && !card.IsFooter)
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
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card && !card.IsFooter)
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

        private void DeckCtxSetCommander_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck?.DeckType != DeckType.Commander) return;
            if (!(GetActiveDeckGrid()?.SelectedItem is DeckCard card) ||
                card.IsFooter) return;

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
        }

        private void DeckCtxRemoveCommander_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck?.DeckType != DeckType.Commander) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card &&
                card.IsCommander)
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
            if (DeckTabControl.SelectedItem is not TabItem tab) return null;

            // New structure: tab.Content is a Grid container
            if (tab.Content is Grid container)
            {
                var grids = container.Children.OfType<DataGrid>().ToList();
                // Return whichever grid has a selected item, preferring main grid
                var cmdGrid = grids.FirstOrDefault(g => g.Name == "CommanderGrid");
                var mainGrid = grids.FirstOrDefault(g => g.Name == "MainDeckGrid");
                if (cmdGrid?.SelectedItem != null) return cmdGrid;
                return mainGrid;
            }

            // Legacy: tab.Content is directly a DataGrid
            if (tab.Content is DataGrid grid) return grid;
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
            InitializeComponent();
            EnsureDatabase();
            LoadCaches();
            BtnTheme.Content = ThemeService.ThemeToggleIcon;
            BtnTheme.ToolTip = ThemeService.ThemeToggleTooltip;
            ViewModeComboBox.SelectedIndex = 0;
        }

        private void EnsureDatabase()
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();
            db.MigrateSchema();
        }

        private void LoadCaches()
        {
            using var db = new AppDbContext();

            _poolCache = db.PoolCards.AsNoTracking()
                                .OrderBy(c => c.Name)
                                .ThenBy(c => c.SetCode)
                                .ToList();
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

            // Clear mana symbol panels — only deck modes populate them
            if (!isDeckTabMode)
            {
                BottomTableManaSymbols.Children.Clear();
                TopTableManaSymbols.Children.Clear();
            }
            BottomDataGrid.Visibility = isDeckTabMode
                ? Visibility.Collapsed : Visibility.Visible;
            DeckTabControl.Visibility = isDeckTabMode
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

                case "PoolToDeck":
                    LoadTopTable_Pool();
                    TopSearchLabel.Text = "Pool  (read only)";
                    BottomTableLabel.Text = "Deck";
                    UpdateDeckSummary(_activeDeck);
                    break;

                case "CollectionToDeck":
                    LoadTopTable_CollectionForDeck();
                    TopSearchLabel.Text = "Collection";
                    BottomTableLabel.Text = "Deck";
                    UpdateDeckSummary(_activeDeck);
                    TopDataGrid.IsReadOnly = false;
                    break;

                case "DeckToCollection":
                    LoadTopTable_DeckForCollection();
                    LoadBottomTable_Collection();
                    UpdateTopTableLabel();
                    BottomTableLabel.Text = "Collection";
                    break;
            }
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
        private void LoadTopTable_Pool()
        {
            SetTopExpandColumnVisibility(Visibility.Collapsed);
            RemoveCollectionColumns(TopDataGrid);
            var all = _poolCache ?? new List<PoolCard>();

            var filtered = FilterService.Apply(all, _topFilter, _searchText);

            if (_topFilterNode != null &&
                FilterExpressionService.HasConditions(_topFilterNode))
                filtered = FilterExpressionService.Apply(
                    filtered, _topFilterNode, true);

            if (_topSelectedSetCodes.Count > 0)
                filtered = filtered
                    .Where(c => _topSelectedSetCodes.Contains(c.SetCode))
                    .ToList();

            if (_topColumnFilters.HasActiveFilters)
                filtered = _topColumnFilters.Apply(filtered);

            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;

            TopDataGrid.ItemsSource = filtered;
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
            var filtered = FilterService.Apply(all, _topFilter, _searchText);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
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
            UpdateTopSummary("Art Series", filtered.Count);
        }

        private void LoadTopTable_DeckForCollection()
        {
            RemoveCollectionColumns(TopDataGrid);
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
            SetTopExpandColumnVisibility(Visibility.Visible);
            EnsureCollectionColumns(TopDataGrid);
            using var db = new Data.AppDbContext();
            var rows = BuildCollectionRows(db);
            if (!string.IsNullOrWhiteSpace(_lastSearchTerm))
                rows = rows.Where(c => c.Name.Contains(
                    _lastSearchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
            if (_topColumnFilters.HasActiveFilters)
                rows = _topColumnFilters.Apply(rows);
            for (int i = 0; i < rows.Count; i++)
                rows[i].RowIndex = i;
            TopDataGrid.ItemsSource = rows;
            UpdateTopSummary("Collection",
                nonFoil: rows.Sum(r => r.Quantity),
                foil: rows.Sum(r => r.FoilQuantity),
                total: rows.Sum(r => r.Quantity + r.FoilQuantity),
                value: rows.Sum(r => r.TotalValue));
        }

        private void SetTopExpandColumnVisibility(Visibility vis)
        {
            // First column in TopDataGrid is the expand button column
            if (TopDataGrid.Columns.Count > 0)
                TopDataGrid.Columns[0].Visibility = vis;
        }

        private static readonly string CollectionColumnMarker = "CollectionCol_";

        private void EnsureCollectionColumns(DataGrid grid)
        {
            var toRemove = grid.Columns
                .Where(c => c.SortMemberPath?.StartsWith(CollectionColumnMarker) == true)
                .ToList();
            foreach (var col in toRemove)
                grid.Columns.Remove(col);

            int insertAt = Math.Min(3, grid.Columns.Count);

            var textCols = new[]
            {
                (Display: "Qty",           Binding: "Quantity",        Width: 50),
                (Display: "Foil Qty",      Binding: "FoilQuantity",    Width: 60),
                (Display: "Used",          Binding: "UsedCount",       Width: 50),
                (Display: "Available",     Binding: "AvailableCount",  Width: 70),
                (Display: "Buy At",        Binding: "BuyAt",           Width: 70),
                (Display: "Sell At",       Binding: "SellAt",          Width: 70),
                (Display: "Sell At Value", Binding: "SellAtValue",     Width: 90),
                (Display: "Price High",    Binding: "PriceHigh",       Width: 80),
                (Display: "Market Value",  Binding: "MarketValue",     Width: 90),
                (Display: "Needed",        Binding: "Needed",          Width: 60),
                (Display: "Excess",        Binding: "Excess",          Width: 60),
                (Display: "Target",        Binding: "Target",          Width: 60),
                (Display: "Condition",     Binding: "Condition",       Width: 90),
                (Display: "Notes",         Binding: "Notes",           Width: 150),
                (Display: "Storage",       Binding: "StorageLocation", Width: 100),
                (Display: "Desired",       Binding: "Desired",         Width: 100),
                (Display: "Group",         Binding: "Group",           Width: 80),
                (Display: "Print Type",    Binding: "PrintType",       Width: 90),
                (Display: "Buy",           Binding: "BuyStatus",       Width: 90),
                (Display: "Sell",          Binding: "SellStatus",      Width: 90),
                (Display: "Added",         Binding: "DateAdded",       Width: 130),
            };

            for (int i = 0; i < textCols.Length; i++)
            {
                var def = textCols[i];
                var col = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = def.Display,
                    SortMemberPath = CollectionColumnMarker + def.Binding,
                    Binding = new System.Windows.Data.Binding(def.Binding),
                    Width = new System.Windows.Controls.DataGridLength(def.Width)
                };
                grid.Columns.Insert(insertAt + i, col);
            }
        }

        private void RemoveCollectionColumns(DataGrid grid)
        {
            var toRemove = grid.Columns
                .Where(c => c.SortMemberPath?.StartsWith(CollectionColumnMarker) == true)
                .ToList();
            foreach (var col in toRemove)
                grid.Columns.Remove(col);
        }

        // ════════════════════════════════════════════════════════════════════
        // BOTTOM TABLE LOADERS
        // ════════════════════════════════════════════════════════════════════
        private void LoadBottomTable_Collection()
        {
            using var db = new AppDbContext();
            var rows = BuildCollectionRows(db);

            // Apply text search
            if (!string.IsNullOrWhiteSpace(_bottomSearch))
                rows = rows.Where(c => c.Name.Contains(
                    _bottomSearch, StringComparison.OrdinalIgnoreCase)).ToList();

            // Apply set code filter from Tab 1
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

            if (_bottomColumnFilters.HasActiveFilters)
                rows = _bottomColumnFilters.Apply(rows);

            for (int i = 0; i < rows.Count; i++)
                rows[i].RowIndex = i;

            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_PlanechaseCollection()
        {
            using var db = new AppDbContext();
            var rows = db.PlanarCollectionEntries.AsNoTracking()
                .Join(db.PlanarCards.AsNoTracking(),
                    ce => ce.PlanarId, pc => pc.PlanarId,
                    (ce, pc) => new CollectionDisplayRow
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
                        Group = ce.Group,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        IsLegalStandard = pc.IsLegalStandard,
                        IsLegalModern = pc.IsLegalModern,
                        IsLegalPioneer = pc.IsLegalPioneer,
                        IsLegalLegacy = pc.IsLegalLegacy,
                        IsLegalVintage = pc.IsLegalVintage
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_ArchenemyCollection()
        {
            using var db = new AppDbContext();
            var rows = db.SchemeCollectionEntries.AsNoTracking()
                .Join(db.SchemeCards.AsNoTracking(),
                    ce => ce.SchemeId, sc => sc.SchemeId,
                    (ce, sc) => new CollectionDisplayRow
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
                        Group = ce.Group,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        IsLegalStandard = pc.IsLegalStandard,
                        IsLegalModern = pc.IsLegalModern,
                        IsLegalPioneer = pc.IsLegalPioneer,
                        IsLegalLegacy = pc.IsLegalLegacy,
                        IsLegalVintage = pc.IsLegalVintage
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_VanguardCollection()
        {
            using var db = new AppDbContext();
            var rows = db.VanguardCollectionEntries.AsNoTracking()
                .Join(db.VanguardCards.AsNoTracking(),
                    ce => ce.VanguardId, vc => vc.VanguardId,
                    (ce, vc) => new CollectionDisplayRow
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
                        Group = ce.Group,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        IsLegalStandard = pc.IsLegalStandard,
                        IsLegalModern = pc.IsLegalModern,
                        IsLegalPioneer = pc.IsLegalPioneer,
                        IsLegalLegacy = pc.IsLegalLegacy,
                        IsLegalVintage = pc.IsLegalVintage
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_TokenCollection()
        {
            using var db = new AppDbContext();
            var rows = db.TokenCollectionEntries.AsNoTracking()
                .Join(db.TokenCards.AsNoTracking(),
                    ce => ce.TokenId, tc => tc.TokenId,
                    (ce, tc) => new CollectionDisplayRow
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
                        Group = ce.Group,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        IsLegalStandard = pc.IsLegalStandard,
                        IsLegalModern = pc.IsLegalModern,
                        IsLegalPioneer = pc.IsLegalPioneer,
                        IsLegalLegacy = pc.IsLegalLegacy,
                        IsLegalVintage = pc.IsLegalVintage
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        private void LoadBottomTable_ArtSeriesCollection()
        {
            using var db = new AppDbContext();
            var rows = db.ArtSeriesCollectionEntries.AsNoTracking()
                .Join(db.ArtSeriesCards.AsNoTracking(),
                    ce => ce.ArtSeriesId, ac => ac.ArtSeriesId,
                    (ce, ac) => new CollectionDisplayRow
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
                        Group = ce.Group,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        IsLegalStandard = pc.IsLegalStandard,
                        IsLegalModern = pc.IsLegalModern,
                        IsLegalPioneer = pc.IsLegalPioneer,
                        IsLegalLegacy = pc.IsLegalLegacy,
                        IsLegalVintage = pc.IsLegalVintage
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
            UpdateSummaryRow(rows);
        }

        // ── Shared collection row builder ────────────────────────────────────
        private static List<CollectionDisplayRow> BuildCollectionRows(
    AppDbContext db)
        {
            return db.CollectionEntries.AsNoTracking()
                .Join(db.PoolCards.AsNoTracking(),
                    ce => ce.PoolId, pc => pc.PoolId,
                    (ce, pc) => new CollectionDisplayRow
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
                        Group = ce.Group,
                        PrintType = ce.PrintType,
                        BuyStatus = ce.BuyStatus,
                        SellStatus = ce.SellStatus,
                        IsLegalStandard = pc.IsLegalStandard,
                        IsLegalModern = pc.IsLegalModern,
                        IsLegalPioneer = pc.IsLegalPioneer,
                        IsLegalLegacy = pc.IsLegalLegacy,
                        IsLegalVintage = pc.IsLegalVintage,
                        Notes = ce.Notes,
                        DateAdded = ce.DateAdded,
                        DateModified = ce.DateModified,
                        // ── Pricing ──────────────────────────────────────────────
                        PriceUsd = pc.PriceUsd,
                        PriceUsdFoil = pc.PriceUsdFoil
                    })
                .OrderBy(x => x.Name)
                .ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // REFRESH BOTTOM
        // ════════════════════════════════════════════════════════════════════
        private void RefreshBottom()
        {
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
                case "PoolToPlanechase": LoadBottomTable_PlanechaseCollection(); break;
                case "PoolToArchenemy": LoadBottomTable_ArchenemyCollection(); break;
                case "PoolToVanguard": LoadBottomTable_VanguardCollection(); break;
                case "PoolToTokens": LoadBottomTable_TokenCollection(); break;
                case "PoolToArtSeries": LoadBottomTable_ArtSeriesCollection(); break;
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
                    {
                        BottomDataGrid.SelectedItem = match;
                        BottomDataGrid.ScrollIntoView(match);
                    }
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

            RestoreFocus();
        }

        // ════════════════════════════════════════════════════════════════════
        // TOOLBAR STATE
        // ════════════════════════════════════════════════════════════════════
        private void UpdateToolbarState()
        {
            bool isDeckMode = _currentMode == "PoolToDeck" ||
                              _currentMode == "CollectionToDeck" ||
                              _currentMode == "DeckToCollection";
            bool hasDeck = _activeDeck != null;
            bool anyDecks = _openDecks.Any();
            bool hasTopSel = TopDataGrid?.SelectedItem != null;
            bool hasBottomSel = BottomDataGrid?.SelectedItem != null;
            bool hasDeckSel = hasDeck && GetActiveDeckGrid()?.SelectedItem != null;

            bool canFoil = false;
            bool canNonFoil = false;

            if (hasTopSel)
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
            BtnAddToCollection.IsEnabled = hasTopSel && canNonFoil &&
                (!isDeckMode || isDeckToCollection);
            BtnAddFoilToCollection.IsEnabled = hasTopSel && canFoil &&
                (!isDeckMode || isDeckToCollection);
            BtnRemoveFromCollection.IsEnabled = hasBottomSel && !isDeckMode;
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
                    using var db = new Data.AppDbContext();
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
                    entry.Group = row.Group;
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
            => _bottomTableHasFocus = false;

        private void BottomDataGrid_CellEditEnding(object sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not CollectionDisplayRow row || row.IsFooter) return;

            // Save after the binding commits (slight delay)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    using var db = new Data.AppDbContext();
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
                    entry.Group = row.Group;
                    entry.PrintType = row.PrintType;
                    entry.BuyStatus = row.BuyStatus;
                    entry.SellStatus = row.SellStatus;
                    entry.DateModified = DateTime.Now;

                    db.SaveChanges();
                    UpdateSummaryRow(
                        BottomDataGrid.ItemsSource as List<CollectionDisplayRow>
                        ?? new List<CollectionDisplayRow>());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Save failed: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }),
            System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BottomDataGrid_GotFocus(object sender, RoutedEventArgs e)
            => _bottomTableHasFocus = true;

        // Returns keyboard focus to whichever grid the user was last in
        private void RestoreFocus()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
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
                    await LoadCardImageAsync(pc.LocalImagePath, pc.ImageNormalUrl);
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
                case CollectionDisplayRow cr:
                    ShowCollectionRowDetail(cr);
                    await LoadCardImageAsync(cr.LocalImagePath, cr.ImageNormalUrl);
                    break;
                case DeckCard dc:
                    ShowDeckCardDetail(dc);
                    await LoadCardImageAsync(dc.LocalImagePath, dc.ImageNormalUrl);
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
                if (TopDataGrid.SelectedItem is PoolCard pc)
                    AddCardToActiveDeck(pc, foil: false);
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
                    _bottomSearch = SearchBox.Text.Trim();
                    RefreshBottom();

                    // Build match list from bottom table
                    _lastSearchTerm = _bottomSearch;
                    _searchMatches.Clear();
                    _searchMatchIndex = -1;
                    if (!string.IsNullOrEmpty(_lastSearchTerm) &&
                        BottomDataGrid.ItemsSource is
                            System.Collections.IEnumerable bottomItems)
                    {
                        foreach (var item in bottomItems)
                        {
                            string? name = item.GetType()
                                .GetProperty("Name")?.GetValue(item)?.ToString();
                            if (name != null && name.Contains(_lastSearchTerm,
                                    StringComparison.OrdinalIgnoreCase))
                                _searchMatches.Add(item);
                        }
                    }
                }
                else
                {
                    _lastSearchTerm = SearchBox.Text.Trim();
                    _searchMatches.Clear();
                    _searchMatchIndex = -1;

                    // Build match list from top table WITHOUT filtering
                    if (!string.IsNullOrEmpty(_lastSearchTerm) &&
                        TopDataGrid.ItemsSource is
                            System.Collections.IEnumerable topItems)
                    {
                        foreach (var item in topItems)
                        {
                            string? name = item.GetType()
                                .GetProperty("Name")?.GetValue(item)?.ToString();
                            if (name != null && name.Contains(_lastSearchTerm,
                                    StringComparison.OrdinalIgnoreCase))
                                _searchMatches.Add(item);
                        }
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
            if (ChkTopFilter.IsChecked == false)
            {
                _topFilter.Clear();
                _topFilterNode = null;
                _topSelectedSetCodes.Clear();
                _topColumnFilters.ClearAll();
                TopFilterSummary.Text = "No filter active";
                LoadCurrentMode();
            }
        }

        private void ChkBottomFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkBottomFilter.IsChecked == false)
            {
                _bottomFilter.Clear();
                _bottomFilterNode = null;
                _bottomSelectedSetCodes.Clear();
                _bottomColumnFilters.ClearAll();
                BottomFilterSummary.Text = "No filter active";
                RefreshBottom();
            }
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
                    TopFilterSummary.Text = FilterExpressionService
                        .Summarize(_topFilterNode);
                    if (string.IsNullOrEmpty(TopFilterSummary.Text) &&
                        _topSelectedSetCodes.Count == 0)
                        TopFilterSummary.Text = "No filter active";
                    else if (_topSelectedSetCodes.Count > 0)
                        TopFilterSummary.Text =
                            $"Editions: {_topSelectedSetCodes.Count} selected  " +
                            TopFilterSummary.Text;
                    LoadCurrentMode();
                }
                else
                {
                    _bottomFilterNode = win.ResultNode;
                    _bottomSelectedSetCodes = win.SelectedSetCodes;
                    BottomFilterSummary.Text = FilterExpressionService
                        .Summarize(_bottomFilterNode);
                    if (string.IsNullOrEmpty(BottomFilterSummary.Text) &&
                        _bottomSelectedSetCodes.Count == 0)
                        BottomFilterSummary.Text = "No filter active";
                    RefreshBottom();
                }
            }
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

            var result = MessageBox.Show(
                "Are you sure you wish to remove 1 card row from your collection?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                RemoveFromCollection(row, 0, true);
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

        private void CtxRemove1_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (BottomDataGrid.SelectedItem is not CollectionDisplayRow row) return;

            var result = MessageBox.Show(
                "Are you sure you wish to remove 1 card row from your collection?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                RemoveFromCollection(row, 1, false);
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

            // Add 1 copy at a time — same as pool→collection
            // AddToPoolCollection handles the duplicate prompt
            AddToPoolCollection(card.PoolId, card.Name, 1, foil);

            // Update UsedCount in collection — this card is used in the active deck
            UpdateUsedCount(card.PoolId, 1);

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

            using var db = new AppDbContext();

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
            using var db = new AppDbContext();
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
        }

        private void AddToSpecialCollection(string type, int cardId,
            string name, int qty, bool foil)
        {
            using var db = new AppDbContext();

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
            }

            db.SaveChanges();
            RefreshBottom();
        }

        private void RemoveFromCollection(
            CollectionDisplayRow row, int qty, bool all)
        {
            using var db = new AppDbContext();

            switch (_currentMode)
            {
                case "PoolToCollection":
                    {
                        var e = db.CollectionEntries.FirstOrDefault(
                            c => c.CollectionEntryId == row.CollectionEntryId);
                        if (e == null) return;
                        if (all) db.CollectionEntries.Remove(e);
                        else
                        {
                            e.Quantity = Math.Max(0, e.Quantity - qty);
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
                default: return;
            }

            db.SaveChanges();
            RefreshBottom();
            RestoreFocus();
        }

        private void AdjustCollectionQty(
            CollectionDisplayRow row, int delta, bool foil)
        {
            using var db = new AppDbContext();

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
                default: return;
            }

            db.SaveChanges();
            RefreshBottom();
        }

        private void SetCollectionQty(
            CollectionDisplayRow row, int qty, bool foil)
        {
            if (qty < 0) return;
            using var db = new AppDbContext();

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
            _legalityVisible = !_legalityVisible;
            var vis = _legalityVisible
                ? Visibility.Visible : Visibility.Collapsed;

            foreach (var col in TopDataGrid.Columns)
            {
                if (col.Header is string h &&
                    new[] { "Standard", "Pioneer", "Modern",
                            "Legacy", "Vintage", "Commander", "Pauper" }
                        .Contains(h))
                    col.Visibility = vis;
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
        private async Task LoadCardImageAsync(string localPath,
            string remoteUrl)
        {
            if (!string.IsNullOrWhiteSpace(localPath) &&
                File.Exists(localPath))
            { SetCardImage(localPath); return; }

            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                await SetCardImageFromUrlAsync(remoteUrl);
                return;
            }

            SetPlaceholderImage();
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
                CardImage.Source = new BitmapImage(new Uri(
                    "pack://application:,,,/BreakersOfE;component/" +
                    "Resources/Images/image_unavailable.png",
                    UriKind.Absolute));
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
            _cardImageWindow = new BreakersOfE.Windows.CardImageWindow(
                CardImage.Source, name)
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
        private void SetStatus(string msg) { } // status bar removed

        private void UpdateRowCount(int count, string label) { } // footer grid handles this

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
        private void MenuExit_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        private void MenuPreferences_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show("Preferences coming soon!", "Preferences",
                MessageBoxButton.OK, MessageBoxImage.Information);

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

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _wiredSummaries.Remove((BottomDataGrid, BottomSummaryGrid));
                SyncAndPopulateCollectionSummary(
                    BottomSummaryGrid, BottomDataGrid,
                    totalNonFoil, totalFoils,
                    totalUsed, totalAvail, totalVal);
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
            int nonFoil, int foil, int used, int avail, decimal value)
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
                { "Qty",         "Qty" },
                { "Foil",        "FoilQty" },
                { "Foil Qty",    "FoilQty" },
                { "Used",        "Used" },
                { "Avail",       "Available" },
                { "Available",   "Available" },
                { "Value",       "TotalValue" },
                { "Foil Value",  "FoilQty" },
                { "Total Value", "TotalValue" }
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
                    Qty        = nonFoil.ToString("N0"),
                    FoilQty    = foil.ToString("N0"),
                    Used       = used.ToString("N0"),
                    Available  = avail.ToString("N0"),
                    TotalValue = $"${value:F2}"
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

            var values = GetUniqueValues(grid, propName);
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
                    UpdateColumnFilterSummary(isTop: true);
                }
                else
                {
                    ApplyBottomColumnFilters();
                    UpdateColumnFilterSummary(isTop: false);
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
            var popup = new BreakersOfE.Windows.ColumnChooserPopup(TopDataGrid)
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
            var popup = new BreakersOfE.Windows.ColumnChooserPopup(BottomDataGrid)
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

        private void MenuSettings_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show("Settings coming soon.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);

        private void MenuAbout_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show(
                "Breakers of E\nVersion 0.3\n\n" +
                "A Magic: The Gathering collection manager.",
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}