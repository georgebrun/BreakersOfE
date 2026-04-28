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

        private void BtnDeckLegality_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Deck Legality window coming soon!",
                "Coming Soon", MessageBoxButton.OK, MessageBoxImage.Information);

        private void BtnAddToDeck_Click(object sender, RoutedEventArgs e)
            => AddFromTopSelectionToDeck(foil: false);

        private void BtnAdd4ToDeck_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck?.DeckType != DeckType.Standard) return;
            for (int i = 0; i < 4; i++)
                AddFromTopSelectionToDeck(foil: false);
        }

        private void BtnAddFoilToDeck_Click(object sender, RoutedEventArgs e)
            => AddFromTopSelectionToDeck(foil: true);

        private void BtnRemoveFromDeck_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card)
            {
                int qty = card.Quantity;
                DeckService.RemoveCard(_activeDeck, card, true);
                RefreshActiveDeckGrid();
                UpdateDeckTabTitle(_activeDeck);
                UpdateDeckSummary(_activeDeck);
                UpdateUsedCount(card.PoolId, -qty);
                SetStatus($"Removed {card.Name} from deck.");
            }
        }



        private void BtnDeckQtyIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card)
            {
                card.Quantity++;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
                UpdateDeckSummary(_activeDeck);
                SetStatus($"{card.Name} qty increased to {card.Quantity}.");
            }
        }

        private void BtnDeckQtyDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card)
            {
                if (card.Quantity <= 1)
                    DeckService.RemoveCard(_activeDeck, card, true);
                else
                    card.Quantity--;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
                UpdateDeckSummary(_activeDeck);
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
        {
            if (_activeDeck == null)
            {
                MessageBox.Show(
                    "No deck is open. Create or open a deck first.",
                    "No Deck Open", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DeckCard? deckCard = null;

            switch (TopDataGrid.SelectedItem)
            {
                case PoolCard pc:
                    if (foil && !pc.IsFoil) return;
                    if (!foil && !pc.IsNonFoil) return;
                    deckCard = DeckService.FromPoolCard(pc);
                    break;
                case CollectionDisplayRow cr:
                    deckCard = DeckService.FromCollectionRow(cr);
                    if (_currentMode == "CollectionToDeck")
                        UpdateUsedCount(cr.PoolId, 1);
                    break;
                default:
                    return;
            }

            if (deckCard == null) return;
            deckCard.IsFoil = foil;

            bool added = DeckService.AddCard(
                _activeDeck, deckCard,
                DeckCardCategory.Mainboard,
                out string error);

            if (!added)
            {
                MessageBox.Show(error, "Cannot Add Card",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshActiveDeckGrid();
            UpdateDeckTabTitle(_activeDeck);
            UpdateDeckSummary(_activeDeck);
            SetStatus($"Added {deckCard.Name} to {_activeDeck.Name}");
        }

        private void UpdateDeckSummary(Deck deck)
        {
            if (deck == null) { SummaryText.Text = string.Empty; return; }

            string limit = deck.DeckType == DeckType.Commander ? "/100" : "+";

            SummaryText.Text =
                $"Cards: {deck.MainboardCount}{limit}   " +
                $"Lands: {deck.LandCount}   " +
                $"Creatures: {deck.CreatureCount}   " +
                $"Spells: {deck.SpellCount}   " +
                $"Sideboard: {deck.SideboardCount}   " +
                $"Value: {deck.TotalValueDisplay}";
        }

        // ════════════════════════════════════════════════════════════════════
        // DECK MANAGEMENT
        // ════════════════════════════════════════════════════════════════════
        private void BtnNewDeck_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new BreakersOfE.Windows.NewDeckDialog
            { Owner = this };

            if (dialog.ShowDialog() != true) return;

            var deck = DeckService.CreateNew(
                dialog.DeckName, dialog.DeckType);

            _openDecks.Add(deck);
            AddDeckTab(deck);
            SetStatus($"New {deck.DeckType} deck created: {deck.Name}");
        }

        private void BtnOpenDeck_Click(object sender, RoutedEventArgs e)
        {
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
                SetStatus($"Opened deck: {deck.Name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open deck:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            SetStatus("All decks saved.");
        }

        private void BtnCloseDeck_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            CloseDeck(_activeDeck);
        }

        private void BtnDeckProperties_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            var win = new BreakersOfE.Windows.DeckPropertiesWindow(_activeDeck)
            { Owner = this };
            if (win.ShowDialog() == true)
            {
                UpdateDeckTabTitle(_activeDeck);
                SetStatus($"Deck properties updated: {_activeDeck.Name}");
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
                SetStatus($"Saved: {deck.Name}");
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

            _activeDeck = null;
            UpdateDeckToolbarState();
            SetStatus($"Closed deck: {deck.Name}");
        }

        // ── Add deck tab ──────────────────────────────────────────────────────────
        private void AddDeckTab(Deck deck)
        {
            var grid = BuildDeckDataGrid(deck);

            var tab = new TabItem
            {
                Header = deck.TabTitle,
                Tag = deck,
                Content = grid,
                Style = (Style)FindResource("DeckTabStyle")
            };

            DeckTabControl.Items.Add(tab);
            DeckTabControl.SelectedItem = tab;
            _activeDeck = deck;
            UpdateDeckToolbarState();
        }

        // ── Build deck DataGrid ───────────────────────────────────────────────────
        private DataGrid BuildDeckDataGrid(Deck deck)
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                Background = (System.Windows.Media.Brush)
                    FindResource("GridRowBrush"),
                Foreground = (System.Windows.Media.Brush)
                    FindResource("PrimaryTextBrush"),
                BorderThickness = new Thickness(0),
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                RowHeaderWidth = 0,
                RowHeight = 28,
                CanUserResizeRows = true,
                CanUserResizeColumns = true,
                CanUserReorderColumns = true,
                EnableRowVirtualization = true,
                ColumnHeaderStyle = (Style)FindResource("DataGridColumnHeaderStyle"),
                RowStyle = (Style)FindResource("DataGridRowStyle"),
                CellStyle = (Style)FindResource("DataGridCellStyle"),
                Tag = deck
            };

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
            var setSideboard = new MenuItem { Header = "Move to Sideboard" };
            setSideboard.Click += DeckCtxMoveSideboard_Click;
            var setMainboard = new MenuItem { Header = "Move to Mainboard" };
            setMainboard.Click += DeckCtxMoveMainboard_Click;
            ctx.Items.Add(removeOne);
            ctx.Items.Add(removeAll);
            ctx.Items.Add(new Separator());
            ctx.Items.Add(setCommander);
            ctx.Items.Add(setSideboard);
            ctx.Items.Add(setMainboard);
            grid.ContextMenu = ctx;

            grid.SelectionChanged    += DeckGrid_SelectionChanged;
            grid.CellEditEnding      += DeckGrid_CellEditEnding;
            grid.PreviewKeyDown      += DeckGrid_PreviewKeyDown;

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
                Header     = "Qty",
                Binding    = new System.Windows.Data.Binding("Quantity")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger =
                        System.Windows.Data.UpdateSourceTrigger.LostFocus
                },
                Width      = new DataGridLength(45),
                IsReadOnly = false
            });

            // Foil checkbox column
            var foilFactory = new FrameworkElementFactory(typeof(CheckBox));
            foilFactory.SetValue(CheckBox.HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            foilFactory.SetValue(CheckBox.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            var foilBinding = new System.Windows.Data.Binding("IsFoil")
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger =
                    System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            };
            foilFactory.SetBinding(CheckBox.IsCheckedProperty, foilBinding);
            foilFactory.AddHandler(CheckBox.CheckedEvent,
                new RoutedEventHandler(DeckFoilCheckbox_Changed));
            foilFactory.AddHandler(CheckBox.UncheckedEvent,
                new RoutedEventHandler(DeckFoilCheckbox_Changed));

            grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Foil",
                Width = new DataGridLength(45),
                CellTemplate = new DataTemplate { VisualTree = foilFactory }
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
                Binding = new System.Windows.Data.Binding("ColorIdentity"),
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
            grid.ItemsSource = deck.Cards
                .OrderBy(c => c.Category)
                .ThenBy(c => c.Name)
                .ToList();
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

                if (_activeDeck != null)
                    BottomTableLabel.Text = _activeDeck.DeckType ==
                        DeckType.Commander
                        ? "Commander Deck"
                        : "Standard Deck";
                else
                    BottomTableLabel.Text = "Deck";
            }
            else
            {
                _activeDeck = null;
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
            UpdateDeckTabTitle(_activeDeck);
            UpdateDeckSummary(_activeDeck);
            SetStatus($"Added {card.Name} to {_activeDeck.Name}");
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

            var deckCard = DeckService.FromCollectionRow(row);

            bool added = DeckService.AddCard(
                _activeDeck, deckCard, category, out string error);

            if (!added)
            {
                MessageBox.Show(error, "Cannot Add Card",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update used count in collection
            UpdateUsedCount(row.PoolId, 1);

            RefreshActiveDeckGrid();
            UpdateDeckTabTitle(_activeDeck);
            UpdateDeckSummary(_activeDeck);
            // Refresh top table to update Used/Available columns
            if (_currentMode == "CollectionToDeck")
                LoadTopTable_CollectionForDeck();
            SetStatus($"Added {row.Name} to {_activeDeck.Name}");
        }

        private void RefreshActiveDeckGrid()
        {
            if (_activeDeck == null) return;

            if (DeckTabControl.SelectedItem is TabItem tab &&
                tab.Content is DataGrid grid)
            {
                RefreshDeckGrid(grid, _activeDeck);
            }
        }

        // ── Update used count ─────────────────────────────────────────────────────
        private void UpdateUsedCount(int poolId, int delta)
        {
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
                grid.SelectedItem is DeckCard card)
                _ = HandleSelectionAsync(card);
        }

        private void DeckCtxRemove1_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card)
            {
                DeckService.RemoveCard(_activeDeck, card, false);
                RefreshActiveDeckGrid();
                UpdateDeckTabTitle(_activeDeck);
                UpdateDeckSummary(_activeDeck);
                UpdateUsedCount(card.PoolId, -1);
                SetStatus($"Removed 1x {card.Name} from deck.");
            }
        }

        private void DeckCtxRemoveAll_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card)
            {
                int qty = card.Quantity;
                DeckService.RemoveCard(_activeDeck, card, true);
                RefreshActiveDeckGrid();
                UpdateDeckTabTitle(_activeDeck);
                UpdateDeckSummary(_activeDeck);
                UpdateUsedCount(card.PoolId, -qty);
                SetStatus($"Removed all {card.Name} from deck.");
            }
        }

        private void DeckCtxSetCommander_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck?.DeckType != DeckType.Commander) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card)
            {
                card.Category = DeckCardCategory.Commander;
                card.IsCommander = true;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
                SetStatus($"{card.Name} set as Commander.");
            }
        }

        private void DeckCtxMoveSideboard_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card)
            {
                card.Category = DeckCardCategory.Sideboard;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
                SetStatus($"{card.Name} moved to Sideboard.");
            }
        }

        private void DeckCtxMoveMainboard_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            if (GetActiveDeckGrid()?.SelectedItem is DeckCard card)
            {
                card.Category = DeckCardCategory.Mainboard;
                _activeDeck.IsModified = true;
                RefreshActiveDeckGrid();
                SetStatus($"{card.Name} moved to Mainboard.");
            }
        }

        private void DeckFoilCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (_activeDeck == null) return;
            _activeDeck.IsModified = true;
            UpdateDeckTabTitle(_activeDeck);
        }

        private void DeckGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_activeDeck == null) return;
            if (e.Key != Key.Delete) return;
            if (sender is not DataGrid grid) return;
            if (grid.SelectedItem is not DeckCard card) return;

            int qty = card.Quantity;
            DeckService.RemoveCard(_activeDeck, card, true);
            RefreshActiveDeckGrid();
            UpdateDeckTabTitle(_activeDeck);
            UpdateDeckSummary(_activeDeck);
            UpdateUsedCount(card.PoolId, -qty);
            SetStatus($"Removed {card.Name} from deck.");
            e.Handled = true;
        }

        private void DeckGrid_CellEditEnding(object sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (_activeDeck == null) return;
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not DeckCard card) return;
            if (e.Column.Header?.ToString() != "Qty") return;

            if (e.EditingElement is TextBox tb &&
                int.TryParse(tb.Text, out int qty))
            {
                if (qty <= 0)
                {
                    DeckService.RemoveCard(_activeDeck, card, true);
                    Dispatcher.BeginInvoke(new Action(() =>
                        RefreshActiveDeckGrid()),
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
                else
                {
                    // Rule check
                    string? ruleError = null;
                    if (!card.IsBasicLand)
                    {
                        if (_activeDeck.DeckType == DeckType.Commander && qty > 1)
                            ruleError = "Commander decks allow only 1 copy of each non-basic card.";
                        else if (_activeDeck.DeckType == DeckType.Standard && qty > 4)
                            ruleError = "Standard decks allow max 4 copies of each non-basic card.";
                    }

                    if (ruleError != null)
                    {
                        MessageBox.Show(ruleError, "Deck Rule Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        tb.Text = card.Quantity.ToString();
                        e.Cancel = true;
                        return;
                    }

                    card.Quantity = qty;
                }

                _activeDeck.IsModified = true;
                UpdateDeckTabTitle(_activeDeck);
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
                // Check format rules
                string error = string.Empty;
                if (_activeDeck.DeckType == DeckType.Commander && qty > 1
                    && !card.IsBasicLand)
                    error = "Commander decks may only have 1 copy of each non-basic card.";
                else if (_activeDeck.DeckType == DeckType.Standard && qty > 4
                    && !card.IsBasicLand)
                    error = "Standard decks may only have 4 copies of each non-basic card.";

                if (!string.IsNullOrEmpty(error))
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
            UpdateDeckTabTitle(_activeDeck);
            UpdateDeckSummary(_activeDeck);
        }

        private DataGrid? GetActiveDeckGrid()
        {
            if (DeckTabControl.SelectedItem is TabItem tab &&
                tab.Content is DataGrid grid)
                return grid;
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

            string folder = Services.AppFolderService.DecksFolder;
            if (!Directory.Exists(folder)) return;

            var files = Directory.GetFiles(folder, "*.deck",
                SearchOption.TopDirectoryOnly);

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var deck = System.Text.Json.JsonSerializer
                        .Deserialize<Models.Deck>(json, options);
                    if (deck == null) continue;

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
                            Quantity = card.Quantity,
                            Category = card.CategoryDisplay,
                            IsFoil = card.IsFoil ? "Yes" : "No"
                        });
                    }
                }
                catch { }
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
            SetStatus("Loading card data into memory...");
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

            SetStatus("Ready.");
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
                _topColumnFilters.ClearAll();
                _bottomColumnFilters.ClearAll();

                LoadCurrentMode();
                UpdateToolbarState();
                UpdateToolbarState();
            }
        }

        private void LoadCurrentMode()
        {
            // Show collection or deck area based on mode
            bool isDeckMode = _currentMode == "PoolToDeck" ||
                              _currentMode == "CollectionToDeck";

            TopDataGrid.Visibility    = Visibility.Visible;
            BottomDataGrid.Visibility = isDeckMode
                ? Visibility.Collapsed : Visibility.Visible;
            DeckTabControl.Visibility = isDeckMode
                ? Visibility.Visible : Visibility.Collapsed;

            switch (_currentMode)
            {
                case "PoolToCollection":
                    LoadTopTable_Pool();
                    LoadBottomTable_Collection();
                    TopSearchLabel.Text = "Pool  (read only)";
                    BottomTableLabel.Text = "Collection";
                    ActionBarLabel.Text =
                        "Select a card → Enter to add  |  Shift+Enter to add foil";
                    break;

                case "PoolToPlanechase":
                    LoadTopTable_Planechase();
                    LoadBottomTable_PlanechaseCollection();
                    TopSearchLabel.Text = "Planechase  (read only)";
                    BottomTableLabel.Text = "My Planechase";
                    ActionBarLabel.Text = "Select a card → Enter to add";
                    break;

                case "PoolToArchenemy":
                    LoadTopTable_Archenemy();
                    LoadBottomTable_ArchenemyCollection();
                    TopSearchLabel.Text = "Archenemy  (read only)";
                    BottomTableLabel.Text = "My Archenemy";
                    ActionBarLabel.Text = "Select a card → Enter to add";
                    break;

                case "PoolToVanguard":
                    LoadTopTable_Vanguard();
                    LoadBottomTable_VanguardCollection();
                    TopSearchLabel.Text = "Vanguard  (read only)";
                    BottomTableLabel.Text = "My Vanguard";
                    ActionBarLabel.Text = "Select a card → Enter to add";
                    break;

                case "PoolToTokens":
                    LoadTopTable_Tokens();
                    LoadBottomTable_TokenCollection();
                    TopSearchLabel.Text = "Token Database  (read only)";
                    BottomTableLabel.Text = "My Tokens";
                    ActionBarLabel.Text = "Select a card → Enter to add";
                    break;

                case "PoolToArtSeries":
                    LoadTopTable_ArtSeries();
                    LoadBottomTable_ArtSeriesCollection();
                    TopSearchLabel.Text = "Art Series  (read only)";
                    BottomTableLabel.Text = "My Art Series";
                    ActionBarLabel.Text = "Select a card → Enter to add";
                    break;

                case "PoolToDeck":
                    LoadTopTable_Pool();
                    TopSearchLabel.Text = "Pool  (read only)";
                    BottomTableLabel.Text = "Deck";
                    ActionBarLabel.Text = "Select a card → Enter to add to deck";
                    break;

                case "CollectionToDeck":
                    LoadTopTable_CollectionForDeck();
                    TopSearchLabel.Text   = "Collection";
                    BottomTableLabel.Text = "Deck";
                    ActionBarLabel.Text   = "Select a card → Enter to add to deck";
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
            UpdateRowCount(filtered.Count, "Pool");
            SetStatus($"Pool — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_Tokens()
        {
            var all = _tokenCache ?? new List<TokenCard>();
            var filtered = FilterService.Apply(all, _topFilter, _searchText);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Tokens");
            SetStatus($"Tokens — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_Planechase()
        {
            var all = _planarCache ?? new List<PlanarCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Planechase");
            SetStatus($"Planechase — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_Archenemy()
        {
            var all = _schemeCache ?? new List<SchemeCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Archenemy");
            SetStatus($"Archenemy — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_Vanguard()
        {
            var all = _vanguardCache ?? new List<VanguardCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Vanguard");
            SetStatus($"Vanguard — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_ArtSeries()
        {
            var all = _artSeriesCache ?? new List<ArtSeriesCard>();
            var filtered = string.IsNullOrWhiteSpace(_searchText)
                ? all
                : all.Where(c => c.Name.Contains(
                    _searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            for (int i = 0; i < filtered.Count; i++) filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Art Series");
            SetStatus($"Art Series — {filtered.Count:N0} cards");
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
            UpdateRowCount(rows.Count, "Collection");
            SetStatus($"Collection — {rows.Count:N0} cards");
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

            var colDefs = new[]
            {
                (Display: "Qty",   Binding: "Quantity",       Width: 50),
                (Display: "Foil",  Binding: "FoilQuantity",   Width: 50),
                (Display: "Used",  Binding: "UsedCount",       Width: 50),
                (Display: "Avail", Binding: "AvailableCount",  Width: 55),
            };

            for (int i = 0; i < colDefs.Length; i++)
            {
                var def = colDefs[i];
                var col = new System.Windows.Controls.DataGridTextColumn
                {
                    Header         = def.Display,
                    SortMemberPath = CollectionColumnMarker + def.Binding,
                    Binding        = new System.Windows.Data.Binding(def.Binding),
                    Width          = new System.Windows.Controls.DataGridLength(def.Width)
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
                        StorageLocation = ce.StorageLocation
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
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
                        StorageLocation = ce.StorageLocation
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
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
                        StorageLocation = ce.StorageLocation
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
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
                        StorageLocation = ce.StorageLocation
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
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
                        StorageLocation = ce.StorageLocation
                    })
                .OrderBy(x => x.Name).ToList();
            for (int i = 0; i < rows.Count; i++) rows[i].RowIndex = i;
            BottomDataGrid.ItemsSource = rows;
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
            // Remember selected entry ID before refresh
            int? selectedId = null;
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow sel)
                selectedId = sel.CollectionEntryId;

            switch (_currentMode)
            {
                case "PoolToCollection": LoadBottomTable_Collection(); break;
                case "PoolToPlanechase": LoadBottomTable_PlanechaseCollection(); break;
                case "PoolToArchenemy": LoadBottomTable_ArchenemyCollection(); break;
                case "PoolToVanguard": LoadBottomTable_VanguardCollection(); break;
                case "PoolToTokens": LoadBottomTable_TokenCollection(); break;
                case "PoolToArtSeries": LoadBottomTable_ArtSeriesCollection(); break;
            }

            // Restore selection
            if (selectedId.HasValue &&
                BottomDataGrid.ItemsSource is List<CollectionDisplayRow> rows)
            {
                var match = rows.FirstOrDefault(
                    r => r.CollectionEntryId == selectedId.Value);
                if (match != null)
                {
                    BottomDataGrid.SelectedItem = match;
                    BottomDataGrid.ScrollIntoView(match);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // TOOLBAR STATE
        // ════════════════════════════════════════════════════════════════════
        private void UpdateToolbarState()
        {
            bool isDeckMode = _currentMode == "PoolToDeck" ||
                              _currentMode == "CollectionToDeck";
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
            BtnSaveDeck.IsEnabled = hasDeck;
            BtnSaveAllDecks.IsEnabled = anyDecks;
            BtnCloseAllDecks.IsEnabled = anyDecks;
            BtnDeckProperties.IsEnabled = hasDeck;
            BtnDeckLegality.IsEnabled = hasDeck;
            BtnDeckStats.IsEnabled = hasDeck;

            // ── Group 2: Deck card buttons ────────────────────────────────────────
            BtnAddToDeck.IsEnabled = hasTopSel && hasDeck && canNonFoil;
            BtnAdd4ToDeck.IsEnabled = hasTopSel && hasDeck && canNonFoil &&
                                           _activeDeck?.DeckType == DeckType.Standard;
            BtnAddFoilToDeck.IsEnabled = hasTopSel && hasDeck && canFoil;
            BtnRemoveFromDeck.IsEnabled = hasDeckSel;
            BtnDeckQtyIncrease.IsEnabled = hasDeckSel;
            BtnDeckQtyDecrease.IsEnabled = hasDeckSel;

            // ── Group 3: Collection buttons ───────────────────────────────────────
            BtnAddToCollection.IsEnabled = hasTopSel && canNonFoil && !isDeckMode;
            BtnAddFoilToCollection.IsEnabled = hasTopSel && canFoil && !isDeckMode;
            BtnRemoveFromCollection.IsEnabled = hasBottomSel && !isDeckMode;

            // ── Menu items ────────────────────────────────────────────────────────
            if (MenuSaveDeck != null) MenuSaveDeck.IsEnabled = hasDeck;
            if (MenuSaveAllDecks != null) MenuSaveAllDecks.IsEnabled = anyDecks;
            if (MenuCloseAllDecks != null) MenuCloseAllDecks.IsEnabled = anyDecks;
        }

        // ════════════════════════════════════════════════════════════════════
        // SELECTION HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void TopDataGrid_GotFocus(object sender, RoutedEventArgs e)
            => _bottomTableHasFocus = false;

        private void BottomDataGrid_GotFocus(object sender, RoutedEventArgs e)
            => _bottomTableHasFocus = true;

        private void BottomDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_currentMode != "CollectionToDeck") return;
            if (e.Key != Key.Enter) return;
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
                AddCardToActiveDeck(row);
            e.Handled = true;
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
                        vc.Name, 1, false); // vanguard has no foil
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

            if (handled) e.Handled = true;
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

                    // Scroll to first match — go to top first then jump to match
                    if (_searchMatches.Count > 0)
                    {
                        _searchMatchIndex = 0;
                        var first = _searchMatches[0];
                        TopDataGrid.SelectedItem = first;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // Scroll to very top first
                            if (TopDataGrid.Items.Count > 0)
                            {
                                TopDataGrid.UpdateLayout();
                                TopDataGrid.ScrollIntoView(TopDataGrid.Items[0]);
                                TopDataGrid.UpdateLayout();
                            }
                            // Then scroll to the match
                            TopDataGrid.ScrollIntoView(first);
                        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                    }
                }

                // Show match count AFTER loaders finish via background priority
                System.Windows.Threading.Dispatcher.CurrentDispatcher
                    .BeginInvoke(new Action(() =>
                    {
                        if (!string.IsNullOrEmpty(_lastSearchTerm))
                            SetStatus(_searchMatches.Count > 0
                                ? $"{_searchMatches.Count} matches for '{_lastSearchTerm}'"
                                : $"No matches for '{_lastSearchTerm}'");
                    }),
                    System.Windows.Threading.DispatcherPriority.Background);
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
                SetStatus($"Not found: {name}");
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

            SetStatus($"Match 1 of {_searchMatches.Count}: '{name}'");
        }

        private void NavigateMatch(bool forward)
        {
            if (_searchMatches.Count == 0)
            {
                SetStatus($"No matches for '{_lastSearchTerm}'");
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

            SetStatus($"Match {_searchMatchIndex + 1} of " +
                      $"{_searchMatches.Count}: '{_lastSearchTerm}'");
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
            SetStatus("Search cleared.");
        }

        // ════════════════════════════════════════════════════════════════════
        // TOOLBAR BUTTONS
        // ════════════════════════════════════════════════════════════════════
        private void BtnAddToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            AddFromTopSelection(foil: false, qty: 1);
        }

        private void BtnAddFoilToCollection_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            AddFromTopSelection(foil: true, qty: 1);
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
                    SetStatus($"Added {qty}x {(foil ? "[Foil] " : "")}{name} to existing row.");
                    RefreshBottom();
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
            SetStatus($"Added {qty}x {(foil ? "[Foil] " : "")}{name}.");
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
            SetStatus($"Added {qty}x {(foil ? "[Foil] " : "")}{name}.");
            RefreshBottom();
        }

        private void RemoveFromCollection(
            CollectionDisplayRow row, int qty, bool all)
        {
            using var db = new AppDbContext();

            // Only handles pool collection for now
            var entry = db.CollectionEntries
                .FirstOrDefault(c => c.CollectionEntryId ==
                                     row.CollectionEntryId);
            if (entry == null) return;

            if (all)
                db.CollectionEntries.Remove(entry);
            else
            {
                entry.Quantity = Math.Max(0, entry.Quantity - qty);
                entry.DateModified = DateTime.Now;
                if (entry.Quantity == 0 && entry.FoilQuantity == 0)
                    db.CollectionEntries.Remove(entry);
            }

            db.SaveChanges();
            SetStatus($"Removed from collection.");
            RefreshBottom();
        }

        private void AdjustCollectionQty(
            CollectionDisplayRow row, int delta, bool foil)
        {
            using var db = new AppDbContext();
            var entry = db.CollectionEntries
                .FirstOrDefault(c => c.CollectionEntryId ==
                                     row.CollectionEntryId);
            if (entry == null) return;

            if (foil)
                entry.FoilQuantity = Math.Max(0, entry.FoilQuantity + delta);
            else
                entry.Quantity = Math.Max(0, entry.Quantity + delta);

            entry.DateModified = DateTime.Now;

            if (entry.Quantity == 0 && entry.FoilQuantity == 0)
                db.CollectionEntries.Remove(entry);

            db.SaveChanges();
            RefreshBottom();
        }

        private void SetCollectionQty(
            CollectionDisplayRow row, int qty, bool foil)
        {
            if (qty < 0) return;
            using var db = new AppDbContext();
            var entry = db.CollectionEntries
                .FirstOrDefault(c => c.CollectionEntryId ==
                                     row.CollectionEntryId);
            if (entry == null) return;

            if (foil) entry.FoilQuantity = qty;
            else entry.Quantity = qty;

            entry.DateModified = DateTime.Now;

            if (entry.Quantity == 0 && entry.FoilQuantity == 0)
                db.CollectionEntries.Remove(entry);

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
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
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

            SetStatus(_legalityVisible
                ? "Legality columns shown."
                : "Legality columns hidden.");
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
            { SetCardImageFromUrl(remoteUrl); return; }

            SetPlaceholderImage();
            await Task.CompletedTask;
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

        private void SetCardImageFromUrl(string url)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.None;
                bmp.EndInit();
                CardImage.Source = bmp;
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
                CardImage.Source, name) { Owner = this };
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
        private void SetStatus(string msg) => StatusText.Text = msg;

        private void UpdateRowCount(int count, string label) =>
            RowCountText.Text = $"{label}: {count:N0} rows";

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
                SetStatus("Database updated successfully!");
            }
        }

        private void UpdateSummaryRow(List<CollectionDisplayRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                SummaryText.Text = "No cards in collection";
                return;
            }

            int totalRows = rows.Count;
            int totalCards = rows.Sum(r => r.Quantity);
            int totalFoils = rows.Sum(r => r.FoilQuantity);
            decimal totalValue = rows.Sum(r => r.TotalValue);

            SummaryText.Text =
                $"Rows: {totalRows:N0}   " +
                $"Cards: {totalCards:N0}   " +
                $"Foils: {totalFoils:N0}   " +
                $"Total Value: ${totalValue:F2}";
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
                if (isTop) ApplyTopColumnFilters();
                else ApplyBottomColumnFilters();
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
                btn.Visibility = isActive
                    ? Visibility.Visible : Visibility.Hidden;
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
                SetStatus("Prices updated successfully!");
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