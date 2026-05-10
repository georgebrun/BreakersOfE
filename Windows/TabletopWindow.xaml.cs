using BreakersOfE.Models;
using BreakersOfE.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreakersOfE.Windows
{
    public partial class TabletopWindow : Window
    {
        // ================================================================
        // STATE
        // ================================================================
        private int _yourLife = 40;
        private int _oppLife = 40;
        private int _yourPoison = 0;
        private int _oppPoison = 0;
        private int _yourCmdDmg = 0;
        private int _oppCmdDmg = 0;
        private int _turnCounter = 1;
        private int _currentPhase = 0;
        private bool _stackVisible = false;
        private bool _oppHandHidden = true;
        private bool _tableRotated = false;

        // Game state
        private List<DeckCard> _yourLibrary = new();
        private List<DeckCard> _oppLibrary = new();
        private List<DeckCard> _yourHand = new();
        private List<DeckCard> _oppHand = new();
        private List<DeckCard> _yourGrave = new();
        private List<DeckCard> _oppGrave = new();
        private List<DeckCard> _yourExile = new();
        private List<DeckCard> _oppExile = new();
        private List<DeckCard> _yourBattlefield = new();
        private List<DeckCard> _oppBattlefield = new();
        private DeckCard? _yourCommander = null;
        private DeckCard? _oppCommander = null;
        private int _yourCmdTax = 0;
        private int _oppCmdTax = 0;
        private string _yourPlayerName = "Player 1";
        private string _oppPlayerName = "Player 2";

        private const string SavedGameKey = "Tabletop_SavedGame";
        private static readonly Random _rng = new();

        private static readonly string[] PhaseNames =
            { "Untap", "Upkeep", "Draw", "Main1", "Combat", "Main2", "End" };

        private readonly Deck? _yourDeck;
        private readonly Deck? _oppDeck;
        private readonly List<Deck> _allOpenDecks;

        // ================================================================
        // CONSTRUCTOR
        // ================================================================
        public TabletopWindow(List<Deck>? openDecks = null)
        {
            InitializeComponent();
            _allOpenDecks = openDecks ?? new List<Deck>();
            _yourDeck = _allOpenDecks.FirstOrDefault();
            _oppDeck = _allOpenDecks.Count > 1 ? _allOpenDecks[1] : null;

            Loaded += TabletopWindow_Loaded;
        }

        private void TabletopWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPlaymats();
            ShowNewGameDialog();
        }

        // ================================================================
        // NEW GAME DIALOG
        // ================================================================
        private void ShowNewGameDialog()
        {
            bool hasSave = !string.IsNullOrEmpty(GetSetting(SavedGameKey));

            var dlg = new NewGameDialog(_allOpenDecks, hasSave) { Owner = this };
            if (dlg.ShowDialog() != true)
            {
                Close();
                return;
            }

            if (dlg.RestoreSave)
            {
                RestoreGameState();
                return;
            }

            _yourPlayerName = dlg.Player1Name;
            _oppPlayerName = dlg.Player2Name;
            _yourLife = dlg.StartingLife;
            _oppLife = dlg.StartingLife;
            _yourPoison = _oppPoison = 0;
            _yourCmdDmg = _oppCmdDmg = 0;
            _turnCounter = 1;
            _currentPhase = 0;

            YourNameLabel.Text = _yourPlayerName;
            OppNameLabel.Text = _oppPlayerName;

            SetupPlayerLibrary(dlg.Player1Deck, isYour: true);
            SetupPlayerLibrary(dlg.Player2Deck, isYour: false);

            UpdateLifeDisplays();
            UpdatePhaseDisplay();
            UpdateZoneCounts();
            RenderLibrary(isYour: true);
            RenderLibrary(isYour: false);
            RenderHand(isYour: true);
            RenderHand(isYour: false);
            RenderCommander(isYour: true);
            RenderCommander(isYour: false);
            UpdateHandCounts();

            SaveSetting(SavedGameKey, string.Empty);

            // Download missing card images in background
            _ = CacheCardImagesAsync(dlg.Player1Deck);
            _ = CacheCardImagesAsync(dlg.Player2Deck);
        }

        // ================================================================
        // IMAGE CACHING — download missing card images for tabletop use
        // ================================================================
        private static string GetImageCacheFolder() =>
            Services.AppFolderService.CardImagesFolder;

        private static string CardBackPath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "Images", "MTG_Back.png");

        private static string ImageUnavailablePath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Resources", "Images", "image_unavailable.png");

        private async System.Threading.Tasks.Task CacheCardImagesAsync(Deck? deck)
        {
            if (deck == null) return;
            var folder = GetImageCacheFolder();
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);

            foreach (var card in deck.Cards)
            {
                // Skip if already cached
                if (!string.IsNullOrEmpty(card.LocalImagePath)
                    && System.IO.File.Exists(card.LocalImagePath))
                    continue;

                // Build a safe filename
                string safeName = string.Concat(
                    card.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
                string path = System.IO.Path.Combine(folder, $"{safeName}.jpg");

                if (!System.IO.File.Exists(path))
                {
                    if (string.IsNullOrEmpty(card.ImageNormalUrl)) continue;
                    try
                    {
                        var bytes = await http.GetByteArrayAsync(card.ImageNormalUrl);
                        await System.IO.File.WriteAllBytesAsync(path, bytes);
                    }
                    catch { continue; }
                }

                card.LocalImagePath = path;

                // Re-render hand/commander on UI thread if this card is there
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_yourHand.Any(c => c.Name == card.Name)
                        || _oppHand.Any(c => c.Name == card.Name))
                    {
                        RenderHand(isYour: true);
                        RenderHand(isYour: false);
                    }
                    if (_yourCommander?.Name == card.Name)
                        RenderCommander(isYour: true);
                    if (_oppCommander?.Name == card.Name)
                        RenderCommander(isYour: false);
                });
            }
        }

        // ================================================================
        // LIBRARY SETUP
        // ================================================================
        private void SetupPlayerLibrary(Deck? deck, bool isYour)
        {
            var library = isYour ? _yourLibrary : _oppLibrary;
            var hand = isYour ? _yourHand : _oppHand;
            library.Clear();
            hand.Clear();

            if (deck == null) return;

            // Separate commander
            DeckCard? commander = null;
            if (deck.DeckType == DeckType.Commander)
            {
                commander = deck.CommanderCards.FirstOrDefault();
                if (isYour)
                {
                    _yourCommander = commander;
                    _yourCmdTax = 0;
                    YourCmdTaxText.Text = "Tax: 0";
                }
                else
                {
                    _oppCommander = commander;
                    _oppCmdTax = 0;
                    OppCmdTaxText.Text = "Tax: 0";
                }
            }

            // Build library — expand quantities, exclude commander
            foreach (var card in deck.MainboardCards)
            {
                if (commander != null &&
                    card.Name == commander.Name) continue;
                for (int i = 0; i < card.TotalQuantity; i++)
                    library.Add(card);
            }

            // Shuffle
            Shuffle(library);

            // Deal opening hand (7 cards)
            int handSize = Math.Min(7, library.Count);
            for (int i = 0; i < handSize; i++)
            {
                hand.Add(library[0]);
                library.RemoveAt(0);
            }
        }

        private static void Shuffle(List<DeckCard> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ================================================================
        // CARD DIMENSIONS (75% of standard 63x88mm at 96dpi)
        // ================================================================
        private const double CardW = 90;  // ~135 * 0.67
        private const double CardH = 126; // ~189 * 0.67

        // ================================================================
        // RENDER LIBRARY — show card back stack with count
        // ================================================================
        private void RenderLibrary(bool isYour)
        {
            // Library uses the count label in XAML — just update count
            // The card back image is shown in the Border placeholder
            // We draw a card back visual in the library border
            UpdateZoneCounts();
        }

        // ================================================================
        // RENDER HAND — draw cards in hand canvas
        // ================================================================
        private void RenderHand(bool isYour)
        {
            // Opponent hand is never rendered - only count shown in info bar
            if (!isYour)
            {
                UpdateHandCounts();
                return;
            }

            var canvas = YourHandCanvas;
            canvas.Children.Clear();
            if (_yourHand.Count == 0) return;

            double canvasW = canvas.ActualWidth;
            double canvasH = canvas.ActualHeight;
            if (canvasW < 10) canvasW = 700;
            if (canvasH < 10) canvasH = 100;

            double spacing = Math.Min(CardW + 4,
                (canvasW - CardW) / Math.Max(1, _yourHand.Count - 1));
            double totalW = CardW + spacing * (_yourHand.Count - 1);
            double startX = (canvasW - totalW) / 2;
            double y = (canvasH - CardH) / 2;

            for (int i = 0; i < _yourHand.Count; i++)
            {
                var card = _yourHand[i];
                double x = startX + i * spacing;
                var visual = MakeCardVisual(card, isYour: true, isHand: true);
                Canvas.SetLeft(visual, x);
                Canvas.SetTop(visual, y);
                canvas.Children.Add(visual);
            }
            UpdateHandCounts();
        }

        // ================================================================
        // RENDER COMMANDER — show commander in command zone
        // ================================================================
        private void RenderCommander(bool isYour)
        {
            var canvas = isYour ? YourCommandCanvas : OppCommandCanvas;
            var commander = isYour ? _yourCommander : _oppCommander;
            canvas.Children.Clear();
            if (commander == null) return;

            var visual = MakeCardVisual(commander, isYour, isHand: false);
            Canvas.SetLeft(visual, 2);
            Canvas.SetTop(visual, 2);
            canvas.Children.Add(visual);
        }

        // ================================================================
        // MAKE CARD VISUAL — image if available, card back otherwise
        // ================================================================
        private Border MakeCardVisual(DeckCard card, bool isYour,
            bool isHand, bool faceDown = false)
        {
            var border = new Border
            {
                Width = CardW,
                Height = CardH,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = card.Name
            };

            if (faceDown || string.IsNullOrEmpty(card.LocalImagePath)
                || !System.IO.File.Exists(card.LocalImagePath))
            {
                // Try image_unavailable.png as fallback
                string fallback = ImageUnavailablePath;
                if (System.IO.File.Exists(fallback))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(fallback, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = (int)CardW;
                        bmp.EndInit();
                        border.Child = new System.Windows.Controls.Image
                        {
                            Source = bmp,
                            Stretch = Stretch.Fill
                        };
                    }
                    catch { border.Child = MakeCardBack(); }
                }
                else
                    border.Child = MakeCardBack();
            }
            else
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)CardW;
                    bmp.EndInit();
                    border.Child = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Fill
                    };
                }
                catch
                {
                    border.Child = MakeCardBack();
                }
            }

            // Cards always display upright regardless of which side
            return border;
        }

        // ================================================================
        // CARD BACK — drawn with WPF shapes, no external image needed
        // ================================================================
        private static Grid MakeCardBack()
        {
            var grid = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x5E))
            };

            // Outer decorative border
            grid.Children.Add(new Border
            {
                Margin = new Thickness(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xA0, 0x30)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3)
            });

            // Inner border
            grid.Children.Add(new Border
            {
                Margin = new Thickness(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x60, 0x18)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(
                    Color.FromRgb(0x12, 0x1E, 0x4A))
            });

            // MTG logo text
            grid.Children.Add(new TextBlock
            {
                Text = "M",
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0xA0, 0x30)),
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7
            });

            return grid;
        }

        private bool _handExpanded = false;
        private const double HandCollapsedH = 32;
        private const double HandExpandedH = 160;
        private System.Windows.Threading.DispatcherTimer? _handAnimTimer;
        private double _handAnimTarget;
        private double _handAnimCurrent;

        private void YourHand_Click(object sender, MouseButtonEventArgs e)
        {
            _handExpanded = !_handExpanded;
            _handAnimTarget = _handExpanded ? HandExpandedH : HandCollapsedH;
            _handAnimCurrent = YourHandRow.Height.Value;

            HandPeekLabel.Visibility = _handExpanded
                ? Visibility.Collapsed : Visibility.Visible;

            _handAnimTimer?.Stop();
            _handAnimTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60fps
            };
            _handAnimTimer.Tick += (s, _) =>
            {
                double diff = _handAnimTarget - _handAnimCurrent;
                if (Math.Abs(diff) < 0.5)
                {
                    YourHandRow.Height = new GridLength(_handAnimTarget);
                    _handAnimTimer.Stop();
                    if (_handExpanded) RenderHand(isYour: true);
                    return;
                }
                _handAnimCurrent += diff * 0.25; // ease out
                YourHandRow.Height = new GridLength(_handAnimCurrent);
            };
            _handAnimTimer.Start();
        }

        private void UpdateHandCounts()
        {
            if (HandCountLabel != null)
                HandCountLabel.Text = $"({_yourHand.Count})";
            if (OppHandCountLabel != null)
                OppHandCountLabel.Text = _oppHand.Count.ToString();
        }

        private void YourHandCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 10) RenderHand(isYour: true);
        }

        private void OppHandCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Opponent hand is never rendered — count only shown in info bar
        }

        // ================================================================
        // ZONE COUNT UPDATES
        // ================================================================
        private void UpdateZoneCounts()
        {
            YourLibraryCount.Text = _yourLibrary.Count.ToString();
            OppLibraryCount.Text = _oppLibrary.Count.ToString();
            YourGraveyardCount.Text = _yourGrave.Count.ToString();
            OppGraveyardCount.Text = _oppGrave.Count.ToString();
            YourExileCount.Text = _yourExile.Count.ToString();
            OppExileCount.Text = _oppExile.Count.ToString();
        }

        // ================================================================
        // SAVE / RESTORE GAME STATE
        // ================================================================
        private void SaveGameState()
        {
            var state = new GameState
            {
                YourPlayerName = _yourPlayerName,
                OppPlayerName = _oppPlayerName,
                YourLife = _yourLife,
                OppLife = _oppLife,
                YourPoison = _yourPoison,
                OppPoison = _oppPoison,
                YourCmdDmg = _yourCmdDmg,
                OppCmdDmg = _oppCmdDmg,
                YourCmdTax = _yourCmdTax,
                OppCmdTax = _oppCmdTax,
                TurnCounter = _turnCounter,
                CurrentPhase = _currentPhase,
                YourLibrary = _yourLibrary.Select(c => c.Name).ToList(),
                OppLibrary = _oppLibrary.Select(c => c.Name).ToList(),
                YourHand = _yourHand.Select(c => c.Name).ToList(),
                OppHand = _oppHand.Select(c => c.Name).ToList(),
                YourGrave = _yourGrave.Select(c => c.Name).ToList(),
                OppGrave = _oppGrave.Select(c => c.Name).ToList(),
                YourExile = _yourExile.Select(c => c.Name).ToList(),
                OppExile = _oppExile.Select(c => c.Name).ToList(),
            };
            var json = JsonSerializer.Serialize(state);
            SaveSetting(SavedGameKey, json);
        }

        private void RestoreGameState()
        {
            var json = GetSetting(SavedGameKey);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var state = JsonSerializer.Deserialize<GameState>(json);
                if (state == null) return;

                _yourPlayerName = state.YourPlayerName;
                _oppPlayerName = state.OppPlayerName;
                _yourLife = state.YourLife;
                _oppLife = state.OppLife;
                _yourPoison = state.YourPoison;
                _oppPoison = state.OppPoison;
                _yourCmdDmg = state.YourCmdDmg;
                _oppCmdDmg = state.OppCmdDmg;
                _yourCmdTax = state.YourCmdTax;
                _oppCmdTax = state.OppCmdTax;
                _turnCounter = state.TurnCounter;
                _currentPhase = state.CurrentPhase;

                YourNameLabel.Text = _yourPlayerName;
                OppNameLabel.Text = _oppPlayerName;
                YourCmdTaxText.Text = $"Tax: {_yourCmdTax}";
                OppCmdTaxText.Text = $"Tax: {_oppCmdTax}";

                UpdateLifeDisplays();
                UpdatePhaseDisplay();
                UpdateZoneCounts();

                MessageBox.Show("Game restored successfully.",
                    "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show("Could not restore saved game.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ================================================================
        // PLAYMAT — PERSISTENCE
        // ================================================================
        private const string YourPlaymatKey = "Tabletop_YourPlaymat";
        private const string OppPlaymatKey = "Tabletop_OppPlaymat";

        private static string? GetSetting(string key)
        {
            try
            {
                using var db = new Data.AppDbContext();
                return db.AppSettings.FirstOrDefault(s => s.Key == key)?.Value;
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
                    db.AppSettings.Add(
                        new Models.AppSetting { Key = key, Value = value });
                else
                    s.Value = value;
                db.SaveChanges();
            }
            catch { }
        }

        private void LoadPlaymats()
        {
            var yourPath = GetSetting(YourPlaymatKey);
            var oppPath = GetSetting(OppPlaymatKey);
            if (!string.IsNullOrEmpty(yourPath) && System.IO.File.Exists(yourPath))
                SetPlaymat(YourPlaymatImage, yourPath);
            if (!string.IsNullOrEmpty(oppPath) && System.IO.File.Exists(oppPath))
                SetPlaymat(OppPlaymatImage, oppPath);
        }

        private static void SetPlaymat(System.Windows.Controls.Image img, string path)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                img.Source = bmp;
            }
            catch { img.Source = null; }
        }

        private void YourPlaymat_RightClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
            => ShowPlaymatMenu(YourPlaymatImage, YourPlaymatKey);

        private void OppPlaymat_RightClick(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
            => ShowPlaymatMenu(OppPlaymatImage, OppPlaymatKey);

        private void ShowPlaymatMenu(
            System.Windows.Controls.Image img, string settingKey)
        {
            var menu = new ContextMenu();

            var upload = new MenuItem { Header = "📁 Upload Playmat Image..." };
            upload.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Playmat Image",
                    Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*"
                };
                if (dlg.ShowDialog() == true)
                {
                    SetPlaymat(img, dlg.FileName);
                    SaveSetting(settingKey, dlg.FileName);
                }
            };

            var clear = new MenuItem { Header = "✕ Clear Playmat" };
            clear.Click += (s, e) =>
            {
                img.Source = null;
                SaveSetting(settingKey, string.Empty);
            };

            menu.Items.Add(upload);
            menu.Items.Add(clear);
            menu.IsOpen = true;
        }

        // ================================================================
        // LIFE TOTALS
        // ================================================================
        private void YourLifeUp_Click(object sender, RoutedEventArgs e)
        {
            _yourLife++;
            UpdateLifeDisplays();
        }

        private void YourLifeDown_Click(object sender, RoutedEventArgs e)
        {
            _yourLife--;
            UpdateLifeDisplays();
        }

        private void OppLifeUp_Click(object sender, RoutedEventArgs e)
        {
            _oppLife++;
            UpdateLifeDisplays();
        }

        private void OppLifeDown_Click(object sender, RoutedEventArgs e)
        {
            _oppLife--;
            UpdateLifeDisplays();
        }

        private void YourPoisonUp_Click(object sender, RoutedEventArgs e)
        {
            _yourPoison++;
            UpdateLifeDisplays();
            if (_yourPoison >= 10)
                MessageBox.Show("Player has 10 or more poison counters — they lose!",
                    "Poison", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void YourPoisonDown_Click(object sender, RoutedEventArgs e)
        {
            if (_yourPoison > 0) _yourPoison--;
            UpdateLifeDisplays();
        }

        private void OppPoisonUp_Click(object sender, RoutedEventArgs e)
        {
            _oppPoison++;
            UpdateLifeDisplays();
            if (_oppPoison >= 10)
                MessageBox.Show("Opponent has 10 or more poison counters — they lose!",
                    "Poison", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OppPoisonDown_Click(object sender, RoutedEventArgs e)
        {
            if (_oppPoison > 0) _oppPoison--;
            UpdateLifeDisplays();
        }

        private void OppCmdDmgUp_Click(object sender, RoutedEventArgs e)
        {
            _oppCmdDmg++;
            UpdateLifeDisplays();
            if (_oppCmdDmg >= 21)
                MessageBox.Show("Opponent has taken 21+ commander damage — they lose!",
                    "Commander Damage", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OppCmdDmgDown_Click(object sender, RoutedEventArgs e)
        {
            if (_oppCmdDmg > 0) _oppCmdDmg--;
            UpdateLifeDisplays();
        }

        private void UpdateLifeDisplays()
        {
            YourLifeText.Text = _yourLife.ToString();
            OppLifeText.Text = _oppLife.ToString();
            YourPoisonText.Text = _yourPoison.ToString();
            OppPoisonText.Text = _oppPoison.ToString();
            OppCmdDmgText.Text = _oppCmdDmg.ToString();
        }

        // ================================================================
        // ================================================================
        // ================================================================
        // SWITCH SEATS
        // ================================================================
        // ================================================================
        // SWITCH SEATS -- swap all data, re-render from data, view stays fixed
        // ================================================================
        private void BtnRotateTable_Click(object sender, RoutedEventArgs e)
        {
            _tableRotated = !_tableRotated;

            // Swap all data lists
            (_yourLibrary, _oppLibrary) = (_oppLibrary, _yourLibrary);
            (_yourHand, _oppHand) = (_oppHand, _yourHand);
            (_yourGrave, _oppGrave) = (_oppGrave, _yourGrave);
            (_yourExile, _oppExile) = (_oppExile, _yourExile);
            (_yourBattlefield, _oppBattlefield) = (_oppBattlefield, _yourBattlefield);
            (_yourCommander, _oppCommander) = (_oppCommander, _yourCommander);

            // Swap all counters
            (_yourLife, _oppLife) = (_oppLife, _yourLife);
            (_yourPoison, _oppPoison) = (_oppPoison, _yourPoison);
            (_yourCmdDmg, _oppCmdDmg) = (_oppCmdDmg, _yourCmdDmg);
            (_yourCmdTax, _oppCmdTax) = (_oppCmdTax, _yourCmdTax);

            // Swap player names
            (_yourPlayerName, _oppPlayerName) = (_oppPlayerName, _yourPlayerName);

            // Swap playmat paths and reload both images from new paths
            var yourPath = GetSetting(YourPlaymatKey) ?? string.Empty;
            var oppPath = GetSetting(OppPlaymatKey) ?? string.Empty;
            SaveSetting(YourPlaymatKey, oppPath);
            SaveSetting(OppPlaymatKey, yourPath);
            LoadPlaymats();

            // Update all labels from swapped data
            YourNameLabel.Text = _yourPlayerName;
            OppNameLabel.Text = _oppPlayerName;
            YourCmdTaxText.Text = $"Tax: {_yourCmdTax}";
            OppCmdTaxText.Text = $"Tax: {_oppCmdTax}";

            // Re-render everything from data -- no canvas swapping
            UpdateLifeDisplays();
            UpdateZoneCounts();
            UpdateHandCounts();

            // Clear all canvases and re-render from swapped data
            YourHandCanvas.Children.Clear();
            OppHandCanvas.Children.Clear();
            YourCommandCanvas.Children.Clear();
            OppCommandCanvas.Children.Clear();
            YourBattlefieldCanvas.Children.Clear();
            OppBattlefieldCanvas.Children.Clear();
            YourLandCanvas.Children.Clear();
            OppLandCanvas.Children.Clear();

            RenderHand(isYour: true);
            RenderCommander(isYour: true);
            RenderCommander(isYour: false);
        }

        // ================================================================
        // PHASE TRACKER
        // ================================================================
        private void Phase_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string tag = btn.Tag?.ToString() ?? string.Empty;
            _currentPhase = Array.IndexOf(PhaseNames, tag);
            UpdatePhaseDisplay();
        }

        private void UpdatePhaseDisplay()
        {
            Button[] phaseButtons =
            {
                PhaseUntap, PhaseUpkeep, PhaseDraw,
                PhaseMain1, PhaseCombat, PhaseMain2, PhaseEnd
            };
            for (int i = 0; i < phaseButtons.Length; i++)
                phaseButtons[i].Style = i == _currentPhase
                    ? (Style)FindResource("ActivePhaseButtonStyle")
                    : (Style)FindResource("PhaseButtonStyle");
            TurnCounterText.Text = _turnCounter.ToString();
        }

        private void BtnPassTurn_Click(object sender, RoutedEventArgs e)
        {
            _currentPhase = 0;
            _turnCounter++;
            UpdatePhaseDisplay();
            MessageBox.Show($"Turn {_turnCounter} — Untap your permanents.",
                "Pass Turn", MessageBoxButton.OK, MessageBoxImage.None);
        }

        // ================================================================
        // STACK
        // ================================================================
        private void BtnToggleStack_Click(object sender, RoutedEventArgs e)
        {
            _stackVisible = !_stackVisible;
            StackPanel.Visibility = _stackVisible
                ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleStack.Content = _stackVisible ? "STACK ▲" : "STACK ▼";
        }

        // ================================================================
        // ZONE CLICKS — stubs
        // ================================================================
        private void YourLibrary_Click(object sender, MouseButtonEventArgs e)
            => MessageBox.Show("Library — Phase 3", "Coming Soon");
        private void YourGraveyard_Click(object sender, MouseButtonEventArgs e)
            => MessageBox.Show("Graveyard — Phase 5", "Coming Soon");
        private void YourExile_Click(object sender, MouseButtonEventArgs e)
            => MessageBox.Show("Exile — Phase 5", "Coming Soon");
        private void OppLibrary_Click(object sender, MouseButtonEventArgs e)
            => MessageBox.Show("Opponent Library — Phase 3", "Coming Soon");
        private void OppGraveyard_Click(object sender, MouseButtonEventArgs e)
            => MessageBox.Show("Opponent Graveyard — Phase 5", "Coming Soon");
        private void OppExile_Click(object sender, MouseButtonEventArgs e)
            => MessageBox.Show("Opponent Exile — Phase 5", "Coming Soon");

        // ================================================================
        // MANA POOL
        // ================================================================
        private void ManaPool_Click(object sender, MouseButtonEventArgs e)
            => MessageBox.Show("Mana Pool — Phase 6", "Coming Soon");

        // ================================================================
        // DICE
        // ================================================================
        private void BtnDice_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            void Add(string header, Func<string> roll)
            {
                var item = new MenuItem { Header = header };
                item.Click += (s, _) =>
                    MessageBox.Show(roll(), "Dice Roll",
                        MessageBoxButton.OK, MessageBoxImage.None);
                menu.Items.Add(item);
            }
            Add("Flip Coin", () => _rng.Next(2) == 0 ? "Heads!" : "Tails!");
            Add("Roll d6", () => $"You rolled: {_rng.Next(1, 7)}");
            Add("Roll d20", () => $"You rolled: {_rng.Next(1, 21)}");
            Add("Roll d100", () => $"You rolled: {_rng.Next(1, 101)}");
            menu.IsOpen = true;
        }

        // ================================================================
        // STUBS
        // ================================================================
        private void BtnToken_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Token picker — Phase 8", "Coming Soon");
        private void BtnUntapAll_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Untap All — Phase 7", "Coming Soon");
        private void BtnTabletopSettings_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Settings — Phase 10", "Coming Soon");
        private void BtnTabletopClose_Click(object sender, RoutedEventArgs e)
        {
            // Auto-save on close
            SaveGameState();
            Close();
        }
    }

    // ================================================================
    // GAME STATE — serializable snapshot
    // ================================================================
    public class GameState
    {
        public string YourPlayerName { get; set; } = "Player 1";
        public string OppPlayerName { get; set; } = "Player 2";
        public int YourLife { get; set; } = 20;
        public int OppLife { get; set; } = 20;
        public int YourPoison { get; set; }
        public int OppPoison { get; set; }
        public int YourCmdDmg { get; set; }
        public int OppCmdDmg { get; set; }
        public int YourCmdTax { get; set; }
        public int OppCmdTax { get; set; }
        public int TurnCounter { get; set; } = 1;
        public int CurrentPhase { get; set; }
        public List<string> YourLibrary { get; set; } = new();
        public List<string> OppLibrary { get; set; } = new();
        public List<string> YourHand { get; set; } = new();
        public List<string> OppHand { get; set; } = new();
        public List<string> YourGrave { get; set; } = new();
        public List<string> OppGrave { get; set; } = new();
        public List<string> YourExile { get; set; } = new();
        public List<string> OppExile { get; set; } = new();
    }
}