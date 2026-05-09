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

        // ================================================================
        // CONSTRUCTOR
        // ================================================================
        public TabletopWindow(Deck? yourDeck = null, Deck? oppDeck = null)
        {
            InitializeComponent();
            _yourDeck = yourDeck;
            _oppDeck = oppDeck;

            OppHandBlur.Visibility = Visibility.Visible;
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

            // Gather open decks — start with injected decks
            var openDecks = new List<Deck>();
            if (_yourDeck != null) openDecks.Add(_yourDeck);
            if (_oppDeck != null && _oppDeck != _yourDeck)
                openDecks.Add(_oppDeck);

            var dlg = new NewGameDialog(openDecks, hasSave) { Owner = this };
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

            // Start fresh game with chosen settings
            _yourPlayerName = dlg.Player1Name;
            _oppPlayerName = dlg.Player2Name;
            _yourLife = dlg.StartingLife;
            _oppLife = dlg.StartingLife;
            _yourPoison = 0;
            _oppPoison = 0;
            _yourCmdDmg = 0;
            _oppCmdDmg = 0;
            _turnCounter = 1;
            _currentPhase = 0;

            YourNameLabel.Text = _yourPlayerName;
            OppNameLabel.Text = _oppPlayerName;

            SetupPlayerLibrary(dlg.Player1Deck, isYour: true);
            SetupPlayerLibrary(dlg.Player2Deck, isYour: false);

            UpdateLifeDisplays();
            UpdatePhaseDisplay();
            UpdateZoneCounts();

            // Clear saved game on new start
            SaveSetting(SavedGameKey, string.Empty);
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
        // OPPONENT HAND TOGGLE
        // ================================================================
        private void BtnToggleOppHand_Click(object sender, RoutedEventArgs e)
        {
            _oppHandHidden = !_oppHandHidden;
            OppHandBlur.Visibility = _oppHandHidden
                ? Visibility.Visible : Visibility.Collapsed;
            BtnToggleOppHand.Content = _oppHandHidden ? "👁 Hand" : "🚫 Hand";
        }

        // ================================================================
        // ================================================================
        // SWITCH SEATS
        // ================================================================
        private void BtnRotateTable_Click(object sender, RoutedEventArgs e)
        {
            _tableRotated = !_tableRotated;

            // Swap counters
            (_yourLife, _oppLife) = (_oppLife, _yourLife);
            (_yourPoison, _oppPoison) = (_oppPoison, _yourPoison);
            (_yourCmdDmg, _oppCmdDmg) = (_oppCmdDmg, _yourCmdDmg);

            // Swap names and zone counts using tuples
            (YourNameLabel.Text, OppNameLabel.Text) = (OppNameLabel.Text, YourNameLabel.Text);
            (YourLibraryCount.Text, OppLibraryCount.Text) = (OppLibraryCount.Text, YourLibraryCount.Text);
            (YourGraveyardCount.Text, OppGraveyardCount.Text) = (OppGraveyardCount.Text, YourGraveyardCount.Text);
            (YourExileCount.Text, OppExileCount.Text) = (OppExileCount.Text, YourExileCount.Text);
            (YourCmdTaxText.Text, OppCmdTaxText.Text) = (OppCmdTaxText.Text, YourCmdTaxText.Text);

            // Swap playmat image sources
            // YourPlaymatImage always shows at 0 deg (faces seated player)
            // OppPlaymatImage always shows at 180 deg (faces opp from their seat)
            // Swapping sources means each mat follows the player, not the position
            var yourSrc = YourPlaymatImage.Source;
            YourPlaymatImage.Source = OppPlaymatImage.Source;
            OppPlaymatImage.Source = yourSrc;

            // Persist swapped paths
            var yourPath = GetSetting(YourPlaymatKey) ?? string.Empty;
            var oppPath = GetSetting(OppPlaymatKey) ?? string.Empty;
            SaveSetting(YourPlaymatKey, oppPath);
            SaveSetting(OppPlaymatKey, yourPath);

            // Swap all card canvases
            SwapCanvasChildren(YourBattlefieldCanvas, OppBattlefieldCanvas);
            SwapCanvasChildren(YourHandCanvas, OppHandCanvas);
            SwapCanvasChildren(YourLandCanvas, OppLandCanvas);
            SwapCanvasChildren(YourCommandCanvas, OppCommandCanvas);

            UpdateLifeDisplays();
        }

        private static void SwapCanvasChildren(Canvas a, Canvas b)
        {
            var aChildren = a.Children.Cast<UIElement>().ToList();
            var bChildren = b.Children.Cast<UIElement>().ToList();
            a.Children.Clear();
            b.Children.Clear();
            foreach (var child in bChildren) a.Children.Add(child);
            foreach (var child in aChildren) b.Children.Add(child);
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