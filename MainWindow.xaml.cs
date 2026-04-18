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
        private string _currentMode = "PoolToCollection";
        private bool _legalityVisible = false;
        private bool _bottomLocked = false;

        // ── Filter state ─────────────────────────────────────────────────────
        private FilterState _topFilter = new();
        private FilterState _bottomFilter = new();
        private string _searchText = string.Empty;

        // ── Lock key prefix for AppSettings ──────────────────────────────────
        private const string LockKeyPrefix = "Lock_";

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
            ViewModeComboBox.SelectedIndex = 0;
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
                _searchText = string.Empty;
                if (SearchBox != null) SearchBox.Text = string.Empty;

                LoadCurrentMode();
                LoadLockState();
                UpdateToolbarState();
            }
        }

        private void LoadCurrentMode()
        {
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
            }
        }

        // ── Lock state ───────────────────────────────────────────────────────
        private void LoadLockState()
        {
            string key = LockKeyPrefix + _currentMode;
            using var db = new AppDbContext();
            var setting = db.AppSettings
                .FirstOrDefault(s => s.Key == key);

            _bottomLocked = setting?.Value == "true";
            UpdateLockUI();
        }

        private void SaveLockState()
        {
            string key = LockKeyPrefix + _currentMode;
            using var db = new AppDbContext();
            var setting = db.AppSettings
                .FirstOrDefault(s => s.Key == key);

            if (setting == null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = _bottomLocked ? "true" : "false"
                });
            }
            else
            {
                setting.Value = _bottomLocked ? "true" : "false";
            }
            db.SaveChanges();
        }

        private void UpdateLockUI()
        {
            // Bottom lock indicator
            if (BottomLockIndicator != null)
                BottomLockIndicator.Visibility = _bottomLocked
                    ? Visibility.Visible : Visibility.Collapsed;

            // Lock button icon
            if (BtnLock != null)
                BtnLock.Content = _bottomLocked ? "\uE1F7" : "\uE1F6";

            UpdateToolbarState();
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

            var filtered = FilterService.Apply(all, _topFilter, _searchText);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;

            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Pool");
            SetStatus($"Pool — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_Planechase()
        {
            using var db = new AppDbContext();
            var cards = db.PlanarCards.AsNoTracking()
                             .OrderBy(c => c.Name).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Planechase");
            SetStatus($"Planechase — {cards.Count:N0} cards");
        }

        private void LoadTopTable_Archenemy()
        {
            using var db = new AppDbContext();
            var cards = db.SchemeCards.AsNoTracking()
                             .OrderBy(c => c.Name).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Archenemy");
            SetStatus($"Archenemy — {cards.Count:N0} cards");
        }

        private void LoadTopTable_Vanguard()
        {
            using var db = new AppDbContext();
            var cards = db.VanguardCards.AsNoTracking()
                             .OrderBy(c => c.Name).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Vanguard");
            SetStatus($"Vanguard — {cards.Count:N0} cards");
        }

        private void LoadTopTable_Tokens()
        {
            using var db = new AppDbContext();
            var all = db.TokenCards.AsNoTracking()
                             .OrderBy(c => c.Name)
                             .ThenBy(c => c.SetCode).ToList();
            var filtered = FilterService.Apply(all, _topFilter, _searchText);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;
            TopDataGrid.ItemsSource = filtered;
            UpdateRowCount(filtered.Count, "Tokens");
            SetStatus($"Tokens — {filtered.Count:N0} cards");
        }

        private void LoadTopTable_ArtSeries()
        {
            using var db = new AppDbContext();
            var cards = db.ArtSeriesCards.AsNoTracking()
                             .OrderBy(c => c.Name)
                             .ThenBy(c => c.SetCode).ToList();
            for (int i = 0; i < cards.Count; i++) cards[i].RowIndex = i;
            TopDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Art Series");
            SetStatus($"Art Series — {cards.Count:N0} cards");
        }

        // ════════════════════════════════════════════════════════════════════
        // BOTTOM TABLE LOADERS
        // ════════════════════════════════════════════════════════════════════
        private void LoadBottomTable_Collection()
        {
            using var db = new AppDbContext();
            var rows = BuildCollectionRows(db);
            var filtered = FilterService.Apply(rows, _bottomFilter,
                string.Empty);
            for (int i = 0; i < filtered.Count; i++)
                filtered[i].RowIndex = i;
            BottomDataGrid.ItemsSource = filtered;
            UpdateSummaryRow(filtered);
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
            bool hasTopSel = TopDataGrid?.SelectedItem != null;
            bool hasBottomSel = BottomDataGrid?.SelectedItem != null;
            bool locked = _bottomLocked;

            // Foil availability — strict check against IsFoil in database
            bool canFoil = false;
            bool canNonFoil = false;

            if (hasTopSel)
            {
                canFoil = canNonFoil = false;

                if (TopDataGrid.SelectedItem is PoolCard pc)
                {
                    canFoil = pc.IsFoil;
                    canNonFoil = pc.IsNonFoil;
                }
                else if (TopDataGrid.SelectedItem is PlanarCard pl)
                {
                    canFoil = pl.IsFoil;
                    canNonFoil = pl.IsNonFoil;
                }
                else if (TopDataGrid.SelectedItem is SchemeCard sc)
                {
                    canFoil = sc.IsFoil;
                    canNonFoil = sc.IsNonFoil;
                }
                else if (TopDataGrid.SelectedItem is VanguardCard vc)
                {
                    canFoil = vc.IsFoil;
                    canNonFoil = vc.IsNonFoil;
                }
                else if (TopDataGrid.SelectedItem is TokenCard tc)
                {
                    canFoil = tc.IsFoil;
                    canNonFoil = tc.IsNonFoil;
                }
                else if (TopDataGrid.SelectedItem is ArtSeriesCard ac)
                {
                    canFoil = ac.IsFoil;
                    canNonFoil = ac.IsNonFoil;
                }
            }

            BtnAdd1.IsEnabled = hasTopSel && canNonFoil && !locked;
            BtnAddFoil.IsEnabled = hasTopSel && canFoil && !locked;
            TxtQty.IsEnabled = hasTopSel && !locked;
            BtnAddQty.IsEnabled = hasTopSel && !locked;
            BtnRemove1.IsEnabled = hasBottomSel && !locked;
            BtnLock.IsEnabled = true;
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
        private void SearchBox_TextChanged(object sender,
            TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text.Trim();

            // Only reload top table — search filters source pool
            switch (_currentMode)
            {
                case "PoolToCollection":
                case "PoolToPlanechase":
                case "PoolToArchenemy":
                case "PoolToVanguard":
                case "PoolToTokens":
                case "PoolToArtSeries":
                    LoadCurrentMode();
                    break;
            }
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
                RefreshBottom();
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
            RefreshBottom();
        }

        private void BtnTopFilterCustomize_Click(object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show("Filter window coming soon!",
                "Filter", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnBottomFilterCustomize_Click(object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show("Filter window coming soon!",
                "Filter", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnFilter_Click(object sender, RoutedEventArgs e) =>
            BtnTopFilterCustomize_Click(sender, e);

        // ════════════════════════════════════════════════════════════════════
        // TOOLBAR BUTTONS
        // ════════════════════════════════════════════════════════════════════
        private void BtnAdd1_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            AddFromTopSelection(foil: false, qty: 1);
        }

        private void BtnAddFoil_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            AddFromTopSelection(foil: true, qty: 1);
        }

        private void BtnAddQty_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (!int.TryParse(TxtQty.Text, out int qty) || qty < 1)
            {
                MessageBox.Show("Please enter a valid quantity.",
                    "Invalid Quantity",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            AddFromTopSelection(foil: false, qty: qty);
        }

        private void BtnRemove1_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
                RemoveFromCollection(row, 1, false);
        }

        private void BtnLock_Click(object sender, RoutedEventArgs e)
        {
            _bottomLocked = !_bottomLocked;
            SaveLockState();
            UpdateLockUI();
            SetStatus(_bottomLocked
                ? "Collection locked — editing disabled."
                : "Collection unlocked — editing enabled.");
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

            if (existing == null)
            {
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
            }
            else
            {
                if (foil) existing.FoilQuantity += qty;
                else existing.Quantity += qty;
                existing.DateModified = DateTime.Now;
            }

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
        private void CtxRemove1_Click(object sender, RoutedEventArgs e)
        {
            if (_bottomLocked) return;
            if (BottomDataGrid.SelectedItem is CollectionDisplayRow row)
            {
                var r = MessageBox.Show(
                    $"Remove 1 copy of {row.Name}?",
                    "Confirm Remove",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (r == MessageBoxResult.Yes)
                    RemoveFromCollection(row, 1, false);
            }
        }

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
            string path = Path.Combine(SetSymbolsFolder,
                $"{setCode.ToLower()}.png");
            if (!File.Exists(path))
            { DetailSetSymbol.Source = null; return; }
            DetailSetSymbol.Source = LoadBitmap(path);
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

        private static BitmapImage? LoadBitmap(string path)
        {
            try
            {
                byte[] header = new byte[5];
                using (var fs = File.OpenRead(path))
                    fs.Read(header, 0, 5);
                string h = System.Text.Encoding.UTF8.GetString(header);
                if (h.TrimStart().StartsWith("<")) return null; // SVG

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

        private void MenuUpdatePrices_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Price update coming soon!", "Update Prices",
                MessageBoxButton.OK, MessageBoxImage.Information);
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