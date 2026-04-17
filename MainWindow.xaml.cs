using BreakersOfE.Data;
using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BreakersOfE
{
    public partial class MainWindow : Window
    {
        // ── State ────────────────────────────────────────────────────────────
        private string _currentMode = "Pool";
        private bool _legalityVisible = false;
        private bool _isWorkMode = false;

        // ── Filter state ─────────────────────────────────────────────────────
        private FilterState _topFilter = new();
        private FilterState _bottomFilter = new();
        private string _topSearch = string.Empty;
        private string _bottomSearch = string.Empty;

        // ── Asset folders ────────────────────────────────────────────────────
        private string SetSymbolsFolder =>
            EnsureFolder(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "SetSymbols"));

        private string ManaSymbolsFolder =>
            EnsureFolder(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "ManaSymbols"));

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════
        public MainWindow()
        {
            InitializeComponent();
            EnsureDatabase();
            BtnTheme.Content = ThemeService.ThemeToggleIcon;
            BtnTheme.ToolTip = ThemeService.ThemeToggleTooltip;
            ViewModeComboBox.SelectedIndex = 1; // Pool
        }

        private void EnsureDatabase()
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();
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
                _topSearch = string.Empty;
                _bottomSearch = string.Empty;

                if (TopSearchBox != null) TopSearchBox.Text = string.Empty;
                if (BottomSearchBox != null) BottomSearchBox.Text = string.Empty;

                _isWorkMode = tag is "PoolToCollection" or
                                     "PoolToDeck" or
                                     "CollectionToDeck";

                UpdateToolbarState();
                LoadCurrentMode();
            }
        }

        private void LoadCurrentMode()
        {
            SetBottomTableVisibility(_isWorkMode);

            switch (_currentMode)
            {
                case "Pool":
                    LoadTopTable_Pool();
                    TopSearchLabel.Text = "Pool";
                    break;
                case "Collection":
                    LoadTopTable_Collection();
                    TopSearchLabel.Text = "Collection";
                    break;
                case "TradeBinder":
                    TopDataGrid.ItemsSource = GetTradeBinderRows();
                    TopSearchLabel.Text = "Trade Binder";
                    break;
                case "Tokens":
                    LoadTopTable_Tokens();
                    TopSearchLabel.Text = "Tokens";
                    break;
                case "MyTokens":
                    TopDataGrid.ItemsSource = GetMyTokenRows();
                    TopSearchLabel.Text = "My Tokens";
                    break;
                case "Planar":
                    LoadTopTable_Planar();
                    TopSearchLabel.Text = "Planar Cards";
                    break;
                case "MyPlanar":
                    TopDataGrid.ItemsSource = GetMyPlanarRows();
                    TopSearchLabel.Text = "My Planar";
                    break;
                case "Schemes":
                    LoadTopTable_Schemes();
                    TopSearchLabel.Text = "Schemes";
                    break;
                case "MySchemes":
                    TopDataGrid.ItemsSource = GetMySchemeRows();
                    TopSearchLabel.Text = "My Schemes";
                    break;
                case "Vanguard":
                    LoadTopTable_Vanguard();
                    TopSearchLabel.Text = "Vanguard";
                    break;
                case "Conspiracy":
                    LoadTopTable_Conspiracy();
                    TopSearchLabel.Text = "Conspiracy";
                    break;
                case "ArtSeries":
                    LoadTopTable_ArtSeries();
                    TopSearchLabel.Text = "Art Series";
                    break;
                case "MyArtSeries":
                    TopDataGrid.ItemsSource = GetMyArtSeriesRows();
                    TopSearchLabel.Text = "My Art Series";
                    break;
                case "Dashboard":
                    LoadDashboard();
                    break;
                case "PoolToCollection":
                    LoadTopTable_Pool();
                    LoadBottomTable_Collection();
                    TopSearchLabel.Text = "Pool";
                    BottomSearchLabel.Text = "Collection";
                    ActionBarLabel.Text =
                        "Select a card above → Enter to add  |  " +
                        "Shift+Enter to add foil";
                    break;
                case "PoolToDeck":
                    LoadTopTable_Pool();
                    LoadBottomTable_Deck();
                    TopSearchLabel.Text = "Pool";
                    BottomSearchLabel.Text = "Deck";
                    ActionBarLabel.Text =
                        "Select a card above to add to your deck below";
                    break;
                case "CollectionToDeck":
                    LoadTopTable_Collection();
                    LoadBottomTable_Deck();
                    TopSearchLabel.Text = "Collection";
                    BottomSearchLabel.Text = "Deck";
                    ActionBarLabel.Text =
                        "Select a card above to add to your deck below";
                    break;
            }
        }

        private void SetBottomTableVisibility(bool visible)
        {
            if (BottomDataGrid?.Parent is not Grid bottomGrid) return;
            if (bottomGrid.Parent is not Grid parentGrid) return;

            if (visible)
            {
                parentGrid.RowDefinitions[1].Height =
                    new GridLength(44);
                parentGrid.RowDefinitions[2].Height =
                    new GridLength(1, GridUnitType.Star);
            }
            else
            {
                parentGrid.RowDefinitions[1].Height =
                    new GridLength(0);
                parentGrid.RowDefinitions[2].Height =
                    new GridLength(0);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // TOP TABLE LOADERS
        // ════════════════════════════════════════════════════════════════════
        private void LoadTopTable_Pool()
        {
            using var db = new AppDbContext();
            var all = db.PoolCards.AsNoTracking()
                             .OrderBy(c => c.Name)
                             .ThenBy(c => c.SetCode)
                             .ToList();

            var filtered = FilterService.Apply(all, _topFilter, _topSearch);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;

            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Pool", true);
            SetStatus($"Pool — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_Collection()
        {
            using var db = new AppDbContext();
            var rows = BuildCollectionRows(db);
            var filtered = FilterService.Apply(rows, _topFilter, _topSearch);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;

            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Collection", true);
            SetStatus($"Collection — {filtered.Count:N0} entries");
        }

        private void LoadTopTable_Tokens()
        {
            using var db = new AppDbContext();
            var all = db.TokenCards.AsNoTracking()
                             .OrderBy(c => c.Name)
                             .ThenBy(c => c.SetCode)
                             .ToList();

            var filtered = FilterService.Apply(all, _topFilter, _topSearch);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;

            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Tokens", true);
            SetStatus($"Tokens — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_Planar()
        {
            using var db = new AppDbContext();
            var cards = db.PlanarCards.AsNoTracking()
                             .OrderBy(c => c.Name).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Planar", true);
            SetStatus($"Planar — {cards.Count:N0} cards");
        }

        private void LoadTopTable_Schemes()
        {
            using var db = new AppDbContext();
            var cards = db.SchemeCards.AsNoTracking()
                             .OrderBy(c => c.Name).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Schemes", true);
            SetStatus($"Schemes — {cards.Count:N0} cards");
        }

        private void LoadTopTable_Vanguard()
        {
            using var db = new AppDbContext();
            var cards = db.VanguardCards.AsNoTracking()
                             .OrderBy(c => c.Name).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Vanguard", true);
            SetStatus($"Vanguard — {cards.Count:N0} cards");
        }

        private void LoadTopTable_Conspiracy()
        {
            using var db = new AppDbContext();
            var cards = db.ConspiracyCards.AsNoTracking()
                             .OrderBy(c => c.Name).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Conspiracy", true);
            SetStatus($"Conspiracy — {cards.Count:N0} cards");
        }

        private void LoadTopTable_ArtSeries()
        {
            using var db = new AppDbContext();
            var cards = db.ArtSeriesCards.AsNoTracking()
                             .OrderBy(c => c.Name)
                             .ThenBy(c => c.SetCode).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Art Series", true);
            SetStatus($"Art Series — {cards.Count:N0} cards");
        }

        // ════════════════════════════════════════════════════════════════════
        // BOTTOM TABLE LOADERS
        // ════════════════════════════════════════════════════════════════════
        private void LoadBottomTable_Collection()
        {
            using var db = new AppDbContext();
            var rows = BuildCollectionRows(db);
            var filtered = FilterService.Apply(
                rows, _bottomFilter, _bottomSearch);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;

            BottomDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Collection", false);
        }

        private void LoadBottomTable_Deck()
        {
            BottomDataGrid.ItemsSource = null;
            BottomSearchLabel.Text = "Deck (no deck selected)";
            UpdateRowCount(0, "Deck", false);
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
                        Condition = ce.Condition,
                        Language = ce.Language,
                        StorageLocation = ce.StorageLocation,
                        Notes = ce.Notes,
                        DateAdded = ce.DateAdded,
                        DateModified = ce.DateModified
                    })
                .OrderBy(x => x.Name)
                .ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // COLLECTION DATA HELPERS
        // ════════════════════════════════════════════════════════════════════
        private List<object> GetTradeBinderRows()
        {
            using var db = new AppDbContext();
            return db.TradeBinderEntries.AsNoTracking()
                .ToList().Cast<object>().ToList();
        }

        private List<object> GetMyTokenRows()
        {
            using var db = new AppDbContext();
            return db.TokenCollectionEntries.AsNoTracking()
                .ToList().Cast<object>().ToList();
        }

        private List<object> GetMyPlanarRows()
        {
            using var db = new AppDbContext();
            return db.PlanarCollectionEntries.AsNoTracking()
                .ToList().Cast<object>().ToList();
        }

        private List<object> GetMySchemeRows()
        {
            using var db = new AppDbContext();
            return db.SchemeCollectionEntries.AsNoTracking()
                .ToList().Cast<object>().ToList();
        }

        private List<object> GetMyArtSeriesRows()
        {
            using var db = new AppDbContext();
            return db.ArtSeriesCollectionEntries.AsNoTracking()
                .ToList().Cast<object>().ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // DASHBOARD
        // ════════════════════════════════════════════════════════════════════
        private void LoadDashboard()
        {
            using var db = new AppDbContext();
            var stats = new DashboardStats
            {
                PoolCount = db.PoolCards.Count(),
                TokenCount = db.TokenCards.Count(),
                PlanarCount = db.PlanarCards.Count(),
                SchemeCount = db.SchemeCards.Count(),
                VanguardCount = db.VanguardCards.Count(),
                ConspiracyCount = db.ConspiracyCards.Count(),
                ArtSeriesCount = db.ArtSeriesCards.Count(),
                CollectionCount = db.CollectionEntries.Count(),
                TradeBinderCount = db.TradeBinderEntries.Count(),
                DeckCount = db.Decks.Count()
            };
            TopDataGrid.ItemsSource = new List<DashboardStats> { stats };
            TopSearchLabel.Text = "Dashboard";
            SetStatus("Dashboard loaded.");
        }

        // ════════════════════════════════════════════════════════════════════
        // TOOLBAR STATE
        // ════════════════════════════════════════════════════════════════════
        private void UpdateToolbarState()
        {
            bool isCollection = _currentMode is "Collection" or
                                "PoolToCollection" or "CollectionToDeck";
            bool hasTopSel = TopDataGrid?.SelectedItem != null;
            bool hasBottomSel = BottomDataGrid?.SelectedItem != null;

            bool canFoil = false;
            bool canNonFoil = false;

            if (hasTopSel && TopDataGrid.SelectedItem is PoolCard pc)
            {
                canFoil = pc.IsFoil;
                canNonFoil = pc.IsNonFoil;
            }
            else if (hasTopSel)
            {
                canFoil = canNonFoil = true;
            }

            BtnAdd1.IsEnabled = _isWorkMode && hasTopSel && canNonFoil;
            BtnAddFoil.IsEnabled = _isWorkMode && hasTopSel && canFoil;
            TxtQty.IsEnabled = _isWorkMode && hasTopSel;
            BtnAddQty.IsEnabled = _isWorkMode && hasTopSel;
            BtnRemove1.IsEnabled = _isWorkMode && hasBottomSel;

            BtnQtyPlus.Visibility = isCollection
                ? Visibility.Visible : Visibility.Collapsed;
            BtnQtyMinus.Visibility = isCollection
                ? Visibility.Visible : Visibility.Collapsed;
            SepQty.Visibility = isCollection
                ? Visibility.Visible : Visibility.Collapsed;

            BtnQtyPlus.IsEnabled = isCollection && hasBottomSel;
            BtnQtyMinus.IsEnabled = isCollection && hasBottomSel;
        }

        // ════════════════════════════════════════════════════════════════════
        // SELECTION HANDLERS
        // ════════════════════════════════════════════════════════════════════
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
                    await LoadCardImageAsync(
                        pc.LocalImagePath, pc.ImageNormalUrl);
                    break;
                case TokenCard tc:
                    ShowTokenCardDetail(tc);
                    await LoadCardImageAsync(
                        tc.LocalImagePath, tc.ImageNormalUrl);
                    break;
                case PlanarCard pl:
                    ShowPlanarCardDetail(pl);
                    await LoadCardImageAsync(
                        pl.LocalImagePath, pl.ImageNormalUrl);
                    break;
                case SchemeCard sc:
                    ShowSchemeCardDetail(sc);
                    await LoadCardImageAsync(
                        sc.LocalImagePath, sc.ImageNormalUrl);
                    break;
                case VanguardCard vc:
                    ShowVanguardCardDetail(vc);
                    await LoadCardImageAsync(
                        vc.LocalImagePath, vc.ImageNormalUrl);
                    break;
                case ConspiracyCard cc:
                    ShowConspiracyCardDetail(cc);
                    await LoadCardImageAsync(
                        cc.LocalImagePath, cc.ImageNormalUrl);
                    break;
                case ArtSeriesCard ac:
                    ShowArtSeriesCardDetail(ac);
                    await LoadCardImageAsync(
                        ac.LocalImagePath, ac.ImageNormalUrl);
                    break;
                case CollectionDisplayRow cr:
                    ShowCollectionRowDetail(cr);
                    await LoadCardImageAsync(
                        cr.LocalImagePath, cr.ImageNormalUrl);
                    break;
                default:
                    ClearDetailPanel();
                    break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // ENTER KEY — add card from top table
        // ════════════════════════════════════════════════════════════════════
        private void TopDataGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !_isWorkMode) return;

            if (TopDataGrid.SelectedItem is PoolCard card)
            {
                bool shift = Keyboard.IsKeyDown(Key.LeftShift) ||
                             Keyboard.IsKeyDown(Key.RightShift);

                if (shift && card.IsFoil)
                    AddToCollection(card, 1, foil: true);
                else if (!shift && card.IsNonFoil)
                    AddToCollection(card, 1, foil: false);
                else if (card.IsFoil)
                    AddToCollection(card, 1, foil: true);

                e.Handled = true;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // SEARCH HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void TopSearchBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            _topSearch = TopSearchBox.Text.Trim();
            LoadCurrentMode();
        }

        private void BottomSearchBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            _bottomSearch = BottomSearchBox.Text.Trim();
            if (_isWorkMode) LoadBottomTable_Collection();
        }

        // ════════════════════════════════════════════════════════════════════
        // FILTER HANDLERS
        // ════════════════════════════════════════════════════════════════════
        private void ChkTopFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkTopFilter.IsChecked == false)
            {
                _topFilter.Clear();
                TopFilterSummary.Text = "No filter active";
                LoadCurrentMode();
            }
        }

        private void ChkBottomFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkBottomFilter.IsChecked == false)
            {
                _bottomFilter.Clear();
                BottomFilterSummary.Text = "No filter active";
                if (_isWorkMode) LoadBottomTable_Collection();
            }
        }

        private void BtnTopFilterClear_Click(object sender, RoutedEventArgs e)
        {
            _topFilter.Clear();
            ChkTopFilter.IsChecked = false;
            TopFilterSummary.Text = "No filter active";
            LoadCurrentMode();
        }

        private void BtnBottomFilterClear_Click(object sender,
            RoutedEventArgs e)
        {
            _bottomFilter.Clear();
            ChkBottomFilter.IsChecked = false;
            BottomFilterSummary.Text = "No filter active";
            if (_isWorkMode) LoadBottomTable_Collection();
        }

        private void BtnTopFilterCustomize_Click(object sender,
            RoutedEventArgs e)
        {
            // TODO — FilterWindow (Round 4)
            MessageBox.Show("Filter window coming in the next step!",
                "Filter", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnBottomFilterCustomize_Click(object sender,
            RoutedEventArgs e)
        {
            // TODO — FilterWindow (Round 4)
            MessageBox.Show("Filter window coming in the next step!",
                "Filter", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnFilterTop_Click(object sender, RoutedEventArgs e) =>
            BtnTopFilterCustomize_Click(sender, e);

        // ════════════════════════════════════════════════════════════════════
        // TOOLBAR ACTION BUTTONS
        // ════════════════════════════════════════════════════════════════════
        private void BtnAdd1_Click(object sender, RoutedEventArgs e)
        {
            if (TopDataGrid.SelectedItem is PoolCard card)
                AddToCollection(card, 1, foil: false);
        }

        private void BtnAddFoil_Click(object sender, RoutedEventArgs e)
        {
            if (TopDataGrid.SelectedItem is PoolCard card)
                AddToCollection(card, 1, foil: true);
        }

        private void BtnAddQty_Click(object sender, RoutedEventArgs e)
        {
            if (TopDataGrid.SelectedItem is not PoolCard card) return;
            if (!int.TryParse(TxtQty.Text, out int qty) || qty < 1)
            {
                MessageBox.Show("Please enter a valid quantity.",
                    "Invalid Quantity",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            AddToCollection(card, qty, foil: false);
        }

        private void BtnRemove1_Click(object sender, RoutedEventArgs e)
        {
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
                RemoveFromCollection(row, qty: 1, all: false);
        }

        private void BtnQtyPlus_Click(object sender, RoutedEventArgs e)
        {
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
                AdjustCollectionQty(row, delta: 1, foil: false);
        }

        private void BtnQtyMinus_Click(object sender, RoutedEventArgs e)
        {
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
                AdjustCollectionQty(row, delta: -1, foil: false);
        }

        // ════════════════════════════════════════════════════════════════════
        // INLINE ROW +/- BUTTONS
        // ════════════════════════════════════════════════════════════════════
        private void BtnQtyPlus_Row_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is CollectionDisplayRow row)
                AdjustCollectionQty(row, 1, foil: false);
        }

        private void BtnQtyMinus_Row_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is CollectionDisplayRow row)
                AdjustCollectionQty(row, -1, foil: false);
        }

        private void BtnFoilQtyPlus_Row_Click(object sender,
            RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is CollectionDisplayRow row)
                AdjustCollectionQty(row, 1, foil: true);
        }

        private void BtnFoilQtyMinus_Row_Click(object sender,
            RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is CollectionDisplayRow row)
                AdjustCollectionQty(row, -1, foil: true);
        }

        // ════════════════════════════════════════════════════════════════════
        // INLINE ROW TEXT BOX
        // ════════════════════════════════════════════════════════════════════
        private void TxtQtyRow_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            if (sender is TextBox tb &&
                tb.Tag is CollectionDisplayRow row &&
                int.TryParse(tb.Text, out int qty))
                SetCollectionQty(row, qty, foil: false);
        }

        private void TxtFoilQtyRow_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            if (sender is TextBox tb &&
                tb.Tag is CollectionDisplayRow row &&
                int.TryParse(tb.Text, out int qty))
                SetCollectionQty(row, qty, foil: true);
        }

        private void TxtQtyRow_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox tb &&
                tb.Tag is CollectionDisplayRow row)
            {
                if (e.Key == Key.Up)
                { AdjustCollectionQty(row, 1, false); e.Handled = true; }
                else if (e.Key == Key.Down)
                { AdjustCollectionQty(row, -1, false); e.Handled = true; }
            }
        }

        private void TxtFoilQtyRow_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox tb &&
                tb.Tag is CollectionDisplayRow row)
            {
                if (e.Key == Key.Up)
                { AdjustCollectionQty(row, 1, true); e.Handled = true; }
                else if (e.Key == Key.Down)
                { AdjustCollectionQty(row, -1, true); e.Handled = true; }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CONTEXT MENU
        // ════════════════════════════════════════════════════════════════════
        private void CtxRemove1_Click(object sender, RoutedEventArgs e)
        {
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
            {
                var confirm = MessageBox.Show(
                    $"Remove 1 copy of {row.Name}?",
                    "Confirm Remove",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                    RemoveFromCollection(row, 1, false);
            }
        }

        private void CtxRemoveAll_Click(object sender, RoutedEventArgs e)
        {
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
            {
                var confirm = MessageBox.Show(
                    $"Remove ALL copies of {row.Name} from your collection?\n\nThis cannot be undone.",
                    "Confirm Remove All",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm == MessageBoxResult.Yes)
                    RemoveFromCollection(row, 0, true);
            }
        }

        private async void CtxViewDetails_Click(object sender,
            RoutedEventArgs e)
        {
            await HandleSelectionAsync(BottomDataGrid.SelectedItem);
        }

        // ════════════════════════════════════════════════════════════════════
        // COLLECTION OPERATIONS (AUTO-SAVE)
        // ════════════════════════════════════════════════════════════════════
        private void AddToCollection(PoolCard card, int qty, bool foil)
        {
            using var db = new AppDbContext();
            var existing = db.CollectionEntries
                .FirstOrDefault(c => c.PoolId == card.PoolId);

            if (existing == null)
            {
                db.CollectionEntries.Add(new CollectionEntry
                {
                    PoolId = card.PoolId,
                    Quantity = foil ? 0 : qty,
                    FoilQuantity = foil ? qty : 0,
                    Condition = "Near Mint",
                    Language = "English",
                    DateAdded = DateTime.Now,
                    DateModified = DateTime.Now
                });
            }
            else
            {
                if (foil) existing.FoilQuantity += qty;
                else existing.Quantity += qty;
                existing.DateModified = DateTime.Now;
            }

            db.SaveChanges();
            SetStatus($"Added {qty}x {(foil ? "[Foil] " : "")}" +
                      $"{card.Name} to collection.");
            RefreshBottomIfVisible();
        }

        private void RemoveFromCollection(
            CollectionDisplayRow row, int qty, bool all)
        {
            using var db = new AppDbContext();
            var entry = db.CollectionEntries
                .FirstOrDefault(c => c.CollectionEntryId ==
                                     row.CollectionEntryId);
            if (entry == null) return;

            if (all)
            {
                db.CollectionEntries.Remove(entry);
            }
            else
            {
                entry.Quantity = Math.Max(0, entry.Quantity - qty);
                entry.DateModified = DateTime.Now;
                if (entry.Quantity == 0 && entry.FoilQuantity == 0)
                    db.CollectionEntries.Remove(entry);
            }

            db.SaveChanges();
            SetStatus($"Removed {(all ? "all" : qty.ToString())}x " +
                      $"{row.Name} from collection.");
            RefreshBottomIfVisible();
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
            RefreshBottomIfVisible();
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
            // No refresh on keystroke — avoids disrupting typing
        }

        private void RefreshBottomIfVisible()
        {
            if (_isWorkMode)
                LoadBottomTable_Collection();
            else if (_currentMode == "Collection")
                LoadTopTable_Collection();
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
            DetailPT.Text = $"Hand: {c.HandModifier}  " +
                                         $"Life: {c.LifeModifier}";
            DetailPTLabel.Text = "HAND / LIFE MODIFIER";
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
            DetailPrices.Text = string.Empty;
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
                    }
                    else
                    {
                        DetailManaCostPanel.Children.Add(new TextBlock
                        {
                            Text = token,
                            FontSize = 11,
                            Margin = new Thickness(1, 0, 1, 0),
                            Foreground = System.Windows.Media.Brushes.Gray
                        });
                    }
                }
                else
                {
                    DetailManaCostPanel.Children.Add(new TextBlock
                    {
                        Text = token,
                        FontSize = 11,
                        Margin = new Thickness(1, 0, 1, 0),
                        Foreground = System.Windows.Media.Brushes.Gray
                    });
                }
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
                    { list.Add(cost.Substring(i, end - i + 1)); i = end + 1; continue; }
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
                        FontSize = 11,
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

            string path = Path.Combine(SetSymbolsFolder,
                $"{setCode.ToLower()}.png");

            if (!File.Exists(path))
            { DetailSetSymbol.Source = null; return; }

            DetailSetSymbol.Source = LoadBitmap(path);
        }

        // ════════════════════════════════════════════════════════════════════
        // CARD IMAGE
        // ════════════════════════════════════════════════════════════════════
        private async Task LoadCardImageAsync(
            string localPath, string remoteUrl)
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

        private static BitmapImage? LoadBitmap(string path)
        {
            try
            {
                byte[] header = new byte[5];
                using (var fs = File.OpenRead(path))
                    fs.Read(header, 0, 5);

                string h = System.Text.Encoding.UTF8.GetString(header);
                if (h.TrimStart().StartsWith("<"))
                    return null; // SVG file

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

        // ════════════════════════════════════════════════════════════════════
        // STATUS / ROW COUNT
        // ════════════════════════════════════════════════════════════════════
        private void SetStatus(string msg) => StatusText.Text = msg;

        private void UpdateRowCount(int count, string label, bool top)
        {
            if (top)
                RowCountText.Text = $"{label}: {count:N0} rows";
        }

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
                LoadCurrentMode();
                SetStatus("Database updated successfully!");
            }
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show("Settings coming soon.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);

        private void MenuAbout_Click(object sender, RoutedEventArgs e) =>
            MessageBox.Show(
                "Breakers of E\nVersion 0.2\n\n" +
                "A Magic: The Gathering collection manager.",
                "About", MessageBoxButton.OK, MessageBoxImage.Information);

        private void MenuViewPool_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("Pool");
        private void MenuViewCollection_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("Collection");
        private void MenuViewTradeBinder_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("TradeBinder");
        private void MenuViewTokens_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("Tokens");
        private void MenuViewMyTokens_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("MyTokens");
        private void MenuViewPlanar_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("Planar");
        private void MenuViewMyPlanar_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("MyPlanar");
        private void MenuViewSchemes_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("Schemes");
        private void MenuViewMySchemes_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("MySchemes");
        private void MenuViewVanguard_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("Vanguard");
        private void MenuViewConspiracy_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("Conspiracy");
        private void MenuViewArtSeries_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("ArtSeries");
        private void MenuViewMyArtSeries_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("MyArtSeries");
        private void MenuViewDashboard_Click(object s, RoutedEventArgs e) =>
            SetModeCombo("Dashboard");

        private void SetModeCombo(string tag)
        {
            foreach (ComboBoxItem item in ViewModeComboBox.Items)
            {
                if (item.Tag as string == tag)
                {
                    ViewModeComboBox.SelectedItem = item;
                    return;
                }
            }
        }
    }
}