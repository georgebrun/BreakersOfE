using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BreakersOfE
{
    public partial class MainWindow : Window
    {
        // ── Current state ────────────────────────────────────────────────────
        private string _currentMode = "Pool";
        private string _searchText = string.Empty;

        // ── Local asset folders ──────────────────────────────────────────────
        private string CardImagesFolder =>
            EnsureFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CardImages"));

        private string SetSymbolsFolder =>
            EnsureFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SetSymbols"));

        private string ManaSymbolsFolder =>
            EnsureFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManaSymbols"));

        // ────────────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
            EnsureDatabase();
            ViewModeComboBox.SelectedIndex = 0;   // default to Pool
        }

        // ── Database init ────────────────────────────────────────────────────
        private void EnsureDatabase()
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();
        }

        // ── Folder helper ────────────────────────────────────────────────────
        private static string EnsureFolder(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        // ══════════════════════════════════════════════════════════════════════
        // MODE SWITCHER
        // ══════════════════════════════════════════════════════════════════════
        private void ViewModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _currentMode = tag;
                _searchText = string.Empty;
                if (SearchBox != null) SearchBox.Text = string.Empty;
                LoadCurrentMode();
            }
        }

        private void LoadCurrentMode()
        {
            switch (_currentMode)
            {
                case "Pool": LoadPool(); break;
                case "Collection": LoadCollection(); break;
                case "TradeBinder": LoadTradeBinder(); break;
                case "Tokens": LoadTokens(); break;
                case "MyTokens": LoadMyTokens(); break;
                case "Planar": LoadPlanar(); break;
                case "MyPlanar": LoadMyPlanar(); break;
                case "Schemes": LoadSchemes(); break;
                case "MySchemes": LoadMySchemes(); break;
                case "Vanguard": LoadVanguard(); break;
                case "Conspiracy": LoadConspiracy(); break;
                case "ArtSeries": LoadArtSeries(); break;
                case "MyArtSeries": LoadMyArtSeries(); break;
                case "Dashboard": LoadDashboard(); break;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // LOAD METHODS
        // ══════════════════════════════════════════════════════════════════════

        private void LoadPool()
        {
            using var db = new AppDbContext();
            var query = db.PoolCards.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string s = _searchText.ToLower();
                query = query.Where(c =>
                    c.Name.ToLower().Contains(s) ||
                    c.SetCode.ToLower().Contains(s) ||
                    c.SetName.ToLower().Contains(s) ||
                    c.TypeLine.ToLower().Contains(s));
            }

            var cards = query
                .OrderBy(c => c.Name)
                .ThenBy(c => c.SetCode)
                .ToList();

            MainDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Pool");
            SetStatus($"Pool loaded — {cards.Count:N0} cards.");
            ClearDetailPanel();
        }

        private void LoadCollection()
        {
            using var db = new AppDbContext();

            var items = db.CollectionEntries
                .AsNoTracking()
                .Join(db.PoolCards.AsNoTracking(),
                    ce => ce.PoolId,
                    pc => pc.PoolId,
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
                        Notes = ce.Notes
                    })
                .OrderBy(x => x.Name)
                .ToList();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string s = _searchText.ToLower();
                items = items.Where(x =>
                    x.Name.ToLower().Contains(s) ||
                    x.SetCode.ToLower().Contains(s) ||
                    x.TypeLine.ToLower().Contains(s)).ToList();
            }

            MainDataGrid.ItemsSource = items;
            UpdateRowCount(items.Count, "Collection");
            SetStatus($"Collection loaded — {items.Count:N0} entries.");
            ClearDetailPanel();
        }

        private void LoadTradeBinder()
        {
            using var db = new AppDbContext();
            var items = db.TradeBinderEntries.AsNoTracking().ToList();
            MainDataGrid.ItemsSource = items;
            UpdateRowCount(items.Count, "Trade Binder");
            SetStatus($"Trade Binder loaded — {items.Count:N0} entries.");
            ClearDetailPanel();
        }

        private void LoadTokens()
        {
            using var db = new AppDbContext();
            var cards = db.TokenCards.AsNoTracking()
                .OrderBy(c => c.Name).ThenBy(c => c.SetCode).ToList();
            MainDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Token Database");
            SetStatus($"Tokens loaded — {cards.Count:N0} cards.");
            ClearDetailPanel();
        }

        private void LoadMyTokens()
        {
            using var db = new AppDbContext();
            var items = db.TokenCollectionEntries.AsNoTracking().ToList();
            MainDataGrid.ItemsSource = items;
            UpdateRowCount(items.Count, "My Tokens");
            SetStatus($"My Tokens loaded — {items.Count:N0} entries.");
            ClearDetailPanel();
        }

        private void LoadPlanar()
        {
            using var db = new AppDbContext();
            var cards = db.PlanarCards.AsNoTracking()
                .OrderBy(c => c.Name).ToList();
            MainDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Planar Database");
            SetStatus($"Planar cards loaded — {cards.Count:N0} cards.");
            ClearDetailPanel();
        }

        private void LoadMyPlanar()
        {
            using var db = new AppDbContext();
            var items = db.PlanarCollectionEntries.AsNoTracking().ToList();
            MainDataGrid.ItemsSource = items;
            UpdateRowCount(items.Count, "My Planar Cards");
            SetStatus($"My Planar Cards loaded — {items.Count:N0} entries.");
            ClearDetailPanel();
        }

        private void LoadSchemes()
        {
            using var db = new AppDbContext();
            var cards = db.SchemeCards.AsNoTracking()
                .OrderBy(c => c.Name).ToList();
            MainDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Scheme Database");
            SetStatus($"Schemes loaded — {cards.Count:N0} cards.");
            ClearDetailPanel();
        }

        private void LoadMySchemes()
        {
            using var db = new AppDbContext();
            var items = db.SchemeCollectionEntries.AsNoTracking().ToList();
            MainDataGrid.ItemsSource = items;
            UpdateRowCount(items.Count, "My Schemes");
            SetStatus($"My Schemes loaded — {items.Count:N0} entries.");
            ClearDetailPanel();
        }

        private void LoadVanguard()
        {
            using var db = new AppDbContext();
            var cards = db.VanguardCards.AsNoTracking()
                .OrderBy(c => c.Name).ToList();
            MainDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Vanguard");
            SetStatus($"Vanguard loaded — {cards.Count:N0} cards.");
            ClearDetailPanel();
        }

        private void LoadConspiracy()
        {
            using var db = new AppDbContext();
            var cards = db.ConspiracyCards.AsNoTracking()
                .OrderBy(c => c.Name).ToList();
            MainDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Conspiracy");
            SetStatus($"Conspiracy loaded — {cards.Count:N0} cards.");
            ClearDetailPanel();
        }

        private void LoadArtSeries()
        {
            using var db = new AppDbContext();
            var cards = db.ArtSeriesCards.AsNoTracking()
                .OrderBy(c => c.Name).ThenBy(c => c.SetCode).ToList();
            MainDataGrid.ItemsSource = cards;
            UpdateRowCount(cards.Count, "Art Series");
            SetStatus($"Art Series loaded — {cards.Count:N0} cards.");
            ClearDetailPanel();
        }

        private void LoadMyArtSeries()
        {
            using var db = new AppDbContext();
            var items = db.ArtSeriesCollectionEntries.AsNoTracking().ToList();
            MainDataGrid.ItemsSource = items;
            UpdateRowCount(items.Count, "My Art Series");
            SetStatus($"My Art Series loaded — {items.Count:N0} entries.");
            ClearDetailPanel();
        }

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

            MainDataGrid.ItemsSource = new List<DashboardStats> { stats };
            UpdateRowCount(0, "Dashboard");
            SetStatus("Dashboard loaded.");
            ClearDetailPanel();
        }

        // ══════════════════════════════════════════════════════════════════════
        // SEARCH
        // ══════════════════════════════════════════════════════════════════════
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text.Trim();
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchText)
                ? Visibility.Visible : Visibility.Collapsed;
            LoadCurrentMode();
        }

        // ══════════════════════════════════════════════════════════════════════
        // SELECTION — show card details on left panel
        // ══════════════════════════════════════════════════════════════════════
        private async void MainDataGrid_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            var item = MainDataGrid.SelectedItem;

            if (item is PoolCard pc)
            {
                ShowPoolCardDetail(pc);
                await LoadCardImageAsync(pc.LocalImagePath, pc.ImageNormalUrl);
            }
            else if (item is TokenCard tc)
            {
                ShowTokenCardDetail(tc);
                await LoadCardImageAsync(tc.LocalImagePath, tc.ImageNormalUrl);
            }
            else if (item is PlanarCard pl)
            {
                ShowPlanarCardDetail(pl);
                await LoadCardImageAsync(pl.LocalImagePath, pl.ImageNormalUrl);
            }
            else if (item is SchemeCard sc)
            {
                ShowSchemeCardDetail(sc);
                await LoadCardImageAsync(sc.LocalImagePath, sc.ImageNormalUrl);
            }
            else if (item is VanguardCard vc)
            {
                ShowVanguardCardDetail(vc);
                await LoadCardImageAsync(vc.LocalImagePath, vc.ImageNormalUrl);
            }
            else if (item is ConspiracyCard cc)
            {
                ShowConspiracyCardDetail(cc);
                await LoadCardImageAsync(cc.LocalImagePath, cc.ImageNormalUrl);
            }
            else if (item is ArtSeriesCard ac)
            {
                ShowArtSeriesCardDetail(ac);
                await LoadCardImageAsync(ac.LocalImagePath, ac.ImageNormalUrl);
            }
            else if (item is CollectionDisplayRow cr)
            {
                ShowCollectionRowDetail(cr);
                await LoadCardImageAsync(cr.LocalImagePath, cr.ImageNormalUrl);
            }
            else
            {
                ClearDetailPanel();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // DETAIL PANEL HELPERS
        // ══════════════════════════════════════════════════════════════════════
        private void ShowPoolCardDetail(PoolCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapitalizeFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = c.PowerToughness;
            DetailPTLabel.Text = "POWER / TOUGHNESS";
            DetailFoilNonFoil.Text = BuildFoilString(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = FormatPrices(c.PricesJson);
            RenderManaCost(c.ManaCost);
            LoadSetSymbol(c.SetCode);
        }

        private void ShowTokenCardDetail(TokenCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapitalizeFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = c.PowerToughness;
            DetailPTLabel.Text = "POWER / TOUGHNESS";
            DetailFoilNonFoil.Text = BuildFoilString(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowPlanarCardDetail(PlanarCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapitalizeFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = BuildFoilString(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowSchemeCardDetail(SchemeCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapitalizeFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = BuildFoilString(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowVanguardCardDetail(VanguardCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapitalizeFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = $"Hand: {c.HandModifier}  Life: {c.LifeModifier}";
            DetailPTLabel.Text = "HAND / LIFE MODIFIER";
            DetailFoilNonFoil.Text = BuildFoilString(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowConspiracyCardDetail(ConspiracyCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapitalizeFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = BuildFoilString(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowArtSeriesCardDetail(ArtSeriesCard c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapitalizeFirst(c.Rarity);
            DetailOracle.Text = string.Empty;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = string.Empty;
            DetailPTLabel.Text = string.Empty;
            DetailFoilNonFoil.Text = BuildFoilString(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            DetailManaCostPanel.Children.Clear();
            LoadSetSymbol(c.SetCode);
        }

        private void ShowCollectionRowDetail(CollectionDisplayRow c)
        {
            DetailName.Text = c.Name;
            DetailType.Text = c.TypeLine;
            DetailSet.Text = $"{c.SetName} ({c.SetCode})";
            DetailCollectorNumber.Text = c.CollectorNumber;
            DetailRarity.Text = CapitalizeFirst(c.Rarity);
            DetailOracle.Text = c.OracleText;
            DetailFlavor.Text = c.FlavorText;
            DetailArtist.Text = c.Artist;
            DetailPT.Text = !string.IsNullOrWhiteSpace(c.Power)
                ? $"{c.Power}/{c.Toughness}" : string.Empty;
            DetailPTLabel.Text = "POWER / TOUGHNESS";
            DetailFoilNonFoil.Text = BuildFoilString(c.IsFoil, c.IsNonFoil);
            DetailPrices.Text = string.Empty;
            RenderManaCost(c.ManaCost);
            LoadSetSymbol(c.SetCode);
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
            SetPlaceholderImage();
        }

        // ══════════════════════════════════════════════════════════════════════
        // MANA COST RENDERING
        // ══════════════════════════════════════════════════════════════════════
        private void RenderManaCost(string manaCost)
        {
            DetailManaCostPanel.Children.Clear();

            if (string.IsNullOrWhiteSpace(manaCost)) return;

            // Parse tokens like {W}, {2}, {U/R}, {X} etc.
            var tokens = ParseManaSymbols(manaCost);

            foreach (var token in tokens)
            {
                string symbolFile = Path.Combine(ManaSymbolsFolder,
                    $"{SanitizeSymbolKey(token)}.png");

                if (File.Exists(symbolFile))
                {
                    var img = new Image
                    {
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(1, 0, 1, 0),
                        Source = LoadBitmap(symbolFile),
                        ToolTip = token
                    };
                    DetailManaCostPanel.Children.Add(img);
                }
                else
                {
                    // Text fallback
                    var tb = new System.Windows.Controls.TextBlock
                    {
                        Text = token,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 11,
                        Margin = new Thickness(1, 0, 1, 0)
                    };
                    DetailManaCostPanel.Children.Add(tb);
                }
            }
        }

        private static List<string> ParseManaSymbols(string manaCost)
        {
            var results = new List<string>();
            int i = 0;
            while (i < manaCost.Length)
            {
                if (manaCost[i] == '{')
                {
                    int end = manaCost.IndexOf('}', i);
                    if (end > i)
                    {
                        results.Add(manaCost.Substring(i, end - i + 1));
                        i = end + 1;
                        continue;
                    }
                }
                i++;
            }
            return results;
        }

        private static string SanitizeSymbolKey(string symbol)
        {
            // {W} → W,  {U/R} → U-R,  {2/W} → 2-W
            return symbol
                .Replace("{", "")
                .Replace("}", "")
                .Replace("/", "-");
        }

        // ══════════════════════════════════════════════════════════════════════
        // SET SYMBOL
        // ══════════════════════════════════════════════════════════════════════
        private void LoadSetSymbol(string setCode)
        {
            if (string.IsNullOrWhiteSpace(setCode))
            {
                DetailSetSymbol.Source = null;
                return;
            }

            string path = Path.Combine(SetSymbolsFolder,
                $"{setCode.ToLower()}.png");

            DetailSetSymbol.Source = File.Exists(path)
                ? LoadBitmap(path)
                : null;
        }

        // ══════════════════════════════════════════════════════════════════════
        // CARD IMAGE
        // ══════════════════════════════════════════════════════════════════════
        private async Task LoadCardImageAsync(string localPath, string remoteUrl)
        {
            // 1. Local cached file
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                SetCardImage(localPath);
                return;
            }

            // 2. Remote URL (load directly — no download, no disk write)
            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                SetCardImageFromUrl(remoteUrl);
                return;
            }

            // 3. Placeholder
            SetPlaceholderImage();
            await Task.CompletedTask;
        }

        private void SetCardImage(string filePath)
        {
            try
            {
                CardImage.Source = LoadBitmap(filePath);
            }
            catch
            {
                SetPlaceholderImage();
            }
        }

        private void SetCardImageFromUrl(string url)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.None;
                bitmap.EndInit();
                CardImage.Source = bitmap;
            }
            catch
            {
                SetPlaceholderImage();
            }
        }

        private void SetPlaceholderImage()
        {
            try
            {
                CardImage.Source = new BitmapImage(new Uri(
                    "pack://application:,,,/BreakersOfE;component/Resources/Images/image_unavailable.png",
                    UriKind.Absolute));
            }
            catch
            {
                CardImage.Source = null;
            }
        }

        private static BitmapImage LoadBitmap(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        // ══════════════════════════════════════════════════════════════════════
        // STATUS / ROW COUNT HELPERS
        // ══════════════════════════════════════════════════════════════════════
        private void SetStatus(string message) =>
            StatusText.Text = message;

        private void UpdateRowCount(int count, string mode) =>
            RowCountText.Text = $"{mode}  |  Rows: {count:N0}";

        // ══════════════════════════════════════════════════════════════════════
        // STRING HELPERS
        // ══════════════════════════════════════════════════════════════════════
        private static string CapitalizeFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

        private static string BuildFoilString(bool foil, bool nonFoil)
        {
            if (foil && nonFoil) return "Foil · Non-Foil";
            if (foil) return "Foil only";
            if (nonFoil) return "Non-Foil only";
            return string.Empty;
        }

        private static string FormatPrices(string pricesJson)
        {
            if (string.IsNullOrWhiteSpace(pricesJson)) return string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(pricesJson);
                var sb = new System.Text.StringBuilder();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Null)
                        sb.AppendLine($"{prop.Name}: ${prop.Value.GetString()}");
                }
                return sb.ToString().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // MENU HANDLERS
        // ══════════════════════════════════════════════════════════════════════

        private void MenuExit_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        // View menu — sync dropdown
        private void MenuViewPool_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("Pool");
        private void MenuViewCollection_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("Collection");
        private void MenuViewTradeBinder_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("TradeBinder");
        private void MenuViewTokens_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("Tokens");
        private void MenuViewMyTokens_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("MyTokens");
        private void MenuViewPlanar_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("Planar");
        private void MenuViewMyPlanar_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("MyPlanar");
        private void MenuViewSchemes_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("Schemes");
        private void MenuViewMySchemes_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("MySchemes");
        private void MenuViewVanguard_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("Vanguard");
        private void MenuViewConspiracy_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("Conspiracy");
        private void MenuViewArtSeries_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("ArtSeries");
        private void MenuViewMyArtSeries_Click(object sender, RoutedEventArgs e) =>
            SetModeCombo("MyArtSeries");
        private void MenuViewDashboard_Click(object sender, RoutedEventArgs e) =>
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

        private void MenuUpdateDatabase_Click(object sender, RoutedEventArgs e)
        {
            var win = new BreakersOfE.Windows.UpdateDatabaseWindow { Owner = this };
            if (win.ShowDialog() == true)
            {
                LoadCurrentMode();
                SetStatus("Database updated successfully!");
            }
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            // TODO — Settings window
            MessageBox.Show("Settings coming soon.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Breakers of E\nVersion 0.1\n\nA Magic: The Gathering collection manager.",
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HELPER CLASSES (same file for now)
    // ══════════════════════════════════════════════════════════════════════════

    public class CollectionDisplayRow
    {
        public int CollectionEntryId { get; set; }
        public int PoolId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string SetName { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public string ColorIdentity { get; set; } = string.Empty;
        public string TypeLine { get; set; } = string.Empty;
        public string ManaCost { get; set; } = string.Empty;
        public double ManaValue { get; set; }
        public string Power { get; set; } = string.Empty;
        public string Toughness { get; set; } = string.Empty;
        public string OracleText { get; set; } = string.Empty;
        public string FlavorText { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public bool IsFoil { get; set; }
        public bool IsNonFoil { get; set; }
        public string ImageNormalUrl { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int FoilQuantity { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;

        public string PowerToughness =>
            !string.IsNullOrWhiteSpace(Power) ? $"{Power}/{Toughness}" : string.Empty;

        public string RarityCode => Rarity switch
        {
            "common" => "C",
            "uncommon" => "U",
            "rare" => "R",
            "mythic" => "M",
            _ => "?"
        };
    }

    public class DashboardStats
    {
        public int PoolCount { get; set; }
        public int TokenCount { get; set; }
        public int PlanarCount { get; set; }
        public int SchemeCount { get; set; }
        public int VanguardCount { get; set; }
        public int ConspiracyCount { get; set; }
        public int ArtSeriesCount { get; set; }
        public int CollectionCount { get; set; }
        public int TradeBinderCount { get; set; }
        public int DeckCount { get; set; }
    }
}