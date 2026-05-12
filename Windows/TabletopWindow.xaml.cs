using BreakersOfE.Models;
using BreakersOfE.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
        private List<BattlefieldCard> _yourField = new();
        private List<BattlefieldCard> _oppField = new();
        private Window? _enlargedCardWindow = null;
        private DeckCard? _oppCommander = null;
        private int _yourCmdTax = 0;
        private int _oppCmdTax = 0;
        private CommanderLocation _yourCmdLocation = CommanderLocation.CommandZone;
        private CommanderLocation _oppCmdLocation = CommanderLocation.CommandZone;
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

            // Close enlarged card view whenever main window is clicked
            MouseDown += (s, e) =>
            {
                if (_enlargedCardWindow != null && e.Source != _enlargedCardWindow)
                {
                    _enlargedCardWindow.Close();
                    _enlargedCardWindow = null;
                }
            };
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
            _mulliganCount = 0;
            _yourCmdLocation = CommanderLocation.CommandZone;
            _oppCmdLocation = CommanderLocation.CommandZone;

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
        private const double CardW = 90;   // hand/commander width
        private const double CardH = 126;  // hand/commander height
        private const double FieldW = 90;   // battlefield untapped = same as hand
        private const double FieldH = 126;  // battlefield untapped height
        // Tapped = landscape: FieldH wide x FieldW tall (126 x 90)

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
                int idx = i;
                double x = startX + i * spacing;
                var visual = MakeCardVisual(card, isYour: true, isHand: true);
                WireHandCardPlay(visual, idx, isYour: true);
                // Single click = enlarge card view
                var capturedCard = card;
                visual.MouseLeftButtonDown += (s, e) =>
                {
                    ShowCardEnlarged(capturedCard);
                    e.Handled = true;
                };
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
            visual.Width = CardW;
            visual.Height = CardH;

            // Left click = view enlarged
            var cap = commander;
            visual.MouseLeftButtonDown += (s, e) =>
            {
                ShowCardEnlarged(cap);
                e.Handled = true;
            };

            // Right click = commander context menu
            visual.MouseRightButtonDown += (s, e) =>
            {
                ShowCommanderContextMenu(cap, isYour, visual);
                e.Handled = true;
            };

            Canvas.SetLeft(visual, 2);
            Canvas.SetTop(visual, 2);
            canvas.Children.Add(visual);
        }

        // ================================================================
        // COMMANDER LOCATION TRACKING
        // ================================================================
        private void SetCommanderLocation(bool isYour, CommanderLocation loc)
        {
            if (isYour) _yourCmdLocation = loc;
            else _oppCmdLocation = loc;
            UpdateCmdZoneUI(isYour);
        }

        private void UpdateCmdZoneUI(bool isYour)
        {
            var loc = isYour ? _yourCmdLocation : _oppCmdLocation;
            int castCount = isYour ? _yourCmdTax : _oppCmdTax;
            int taxCost = castCount * 2;

            // Tax always visible — shows cast count and additional mana
            string taxStr = $"Cast: {castCount}x  (+{taxCost})";
            if (isYour) YourCmdTaxText.Text = taxStr;
            else OppCmdTaxText.Text = taxStr;

            // Return button — visible only when commander is away from CMD/Battlefield
            bool showReturn = loc != CommanderLocation.CommandZone
                           && loc != CommanderLocation.Battlefield;
            string locLabel = loc switch
            {
                CommanderLocation.Graveyard => "↩ Return (Grave)",
                CommanderLocation.Exile => "↩ Return (Exile)",
                CommanderLocation.Hand => "↩ Return (Hand)",
                CommanderLocation.Library => "↩ Return (Library)",
                _ => "↩ Return"
            };

            if (isYour)
            {
                BtnYourCmdReturn.Visibility = showReturn
                    ? Visibility.Visible : Visibility.Collapsed;
                BtnYourCmdReturn.Content = locLabel;
            }
            else
            {
                BtnOppCmdReturn.Visibility = showReturn
                    ? Visibility.Visible : Visibility.Collapsed;
                BtnOppCmdReturn.Content = locLabel;
            }
        }

        private void BtnYourCmdReturn_Click(object sender, RoutedEventArgs e)
            => ReturnCommanderToCommandZone(isYour: true);

        private void BtnOppCmdReturn_Click(object sender, RoutedEventArgs e)
            => ReturnCommanderToCommandZone(isYour: false);

        private void ReturnCommanderToCommandZone(bool isYour)
        {
            var loc = isYour ? _yourCmdLocation : _oppCmdLocation;
            var commander = isYour ? _yourCommander : _oppCommander;
            if (commander == null) return;

            // Remove from current zone
            switch (loc)
            {
                case CommanderLocation.Graveyard:
                    if (isYour) _yourGrave.Remove(commander);
                    else _oppGrave.Remove(commander);
                    break;
                case CommanderLocation.Exile:
                    if (isYour) _yourExile.Remove(commander);
                    else _oppExile.Remove(commander);
                    break;
                case CommanderLocation.Hand:
                    if (isYour) _yourHand.Remove(commander);
                    else _oppHand.Remove(commander);
                    RenderHand(isYour: true);
                    UpdateHandCounts();
                    break;
                case CommanderLocation.Library:
                    if (isYour) _yourLibrary.Remove(commander);
                    else _oppLibrary.Remove(commander);
                    break;
            }

            // Put back in command zone
            SetCommanderLocation(isYour, CommanderLocation.CommandZone);
            RenderCommander(isYour);
            UpdateZoneCounts();
        }

        // ================================================================
        // COMMANDER CONTEXT MENU
        // ================================================================
        private void ShowCommanderContextMenu(DeckCard commander,
            bool isYour, Border visual)
        {
            int tax = isYour ? _yourCmdTax : _oppCmdTax;
            int castCost = tax * 2;

            var menu = new ContextMenu();

            var castItem = new MenuItem
            {
                Header = $"⚔ Cast Commander (Tax: +{castCost} generic)"
            };
            castItem.Click += (s, e) => CastCommander(isYour);
            menu.Items.Add(castItem);
            menu.Items.Add(new Separator());

            var viewItem = new MenuItem { Header = "🔍 View Card" };
            var cmdCap = commander;
            viewItem.Click += (s, e) => ShowCardEnlarged(cmdCap);
            menu.Items.Add(viewItem);

            visual.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void CastCommander(bool isYour)
        {
            var commander = isYour ? _yourCommander : _oppCommander;
            if (commander == null) return;

            // Increment tax
            if (isYour) { _yourCmdTax++; YourCmdTaxText.Text = $"Tax: +{_yourCmdTax * 2}"; }
            else { _oppCmdTax++; OppCmdTaxText.Text = $"Tax: +{_oppCmdTax * 2}"; }

            // Place on battlefield
            var field = isYour ? _yourField : _oppField;
            var canvas = isYour ? YourBattlefieldCanvas : OppBattlefieldCanvas;
            var existing = field.Where(c => !c.IsLandZone).ToList();
            double x = 20 + (existing.Count % 8) * (FieldW + 6);
            double y = 10 + (existing.Count / 8) * (FieldH + 6);

            var bc = new BattlefieldCard
            {
                Card = commander,
                X = x,
                Y = y,
                IsYour = isYour,
                IsLandZone = false
            };
            field.Add(bc);
            RenderBattlefieldCard(bc, canvas);

            // Remove from command zone
            if (isYour) _yourCommander = null;
            else _oppCommander = null;
            (isYour ? YourCommandCanvas : OppCommandCanvas).Children.Clear();
            SetCommanderLocation(isYour, CommanderLocation.Battlefield);
        }

        // ================================================================
        // COMMANDER ZONE CHOICE PROMPT
        // ================================================================
        private void PromptCommanderZone(DeckCard card, bool isYour,
            string defaultDestination)
        {
            string destLabel = defaultDestination switch
            {
                "graveyard" => "Graveyard",
                "exile" => "Exile",
                "hand" => "Hand",
                "libtop" => "Library (Top)",
                "libbot" => "Library (Bottom)",
                _ => defaultDestination
            };

            string chosen = "command"; // default to command zone

            var win = new Window
            {
                Title = "Commander — Zone Choice",
                Width = 380,
                Height = 190,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
            };

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock
            {
                Text = $"{card.Name} would go to {destLabel}.",
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "As the commander's owner, where would you like to send it?",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var btnCmd = new Button
            {
                Content = "⚔ Command Zone",
                Width = 145,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x44, 0x88)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xCC))
            };
            btnCmd.Click += (s, e) => { chosen = "command"; win.Close(); };

            var btnDest = new Button
            {
                Content = $"→ {destLabel}",
                Width = 130,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
            };
            btnDest.Click += (s, e) => { chosen = defaultDestination; win.Close(); };

            btns.Children.Add(btnCmd);
            btns.Children.Add(btnDest);
            stack.Children.Add(btns);
            win.Content = stack;
            win.ShowDialog();

            ApplyCommanderZoneMove(card, isYour, chosen);
        }

        private void ApplyCommanderZoneMove(DeckCard card, bool isYour,
            string destination)
        {
            switch (destination)
            {
                case "command":
                    if (isYour) _yourCommander = card;
                    else _oppCommander = card;
                    SetCommanderLocation(isYour, CommanderLocation.CommandZone);
                    RenderCommander(isYour);
                    break;
                case "graveyard":
                    if (isYour) _yourGrave.Add(card); else _oppGrave.Add(card);
                    SetCommanderLocation(isYour, CommanderLocation.Graveyard);
                    break;
                case "exile":
                    if (isYour) _yourExile.Add(card); else _oppExile.Add(card);
                    SetCommanderLocation(isYour, CommanderLocation.Exile);
                    break;
                case "hand":
                    if (isYour) _yourHand.Add(card); else _oppHand.Add(card);
                    SetCommanderLocation(isYour, CommanderLocation.Hand);
                    RenderHand(isYour: true); UpdateHandCounts();
                    break;
                case "libtop":
                    if (isYour) _yourLibrary.Insert(0, card);
                    else _oppLibrary.Insert(0, card);
                    SetCommanderLocation(isYour, CommanderLocation.Library);
                    break;
                case "libbot":
                    if (isYour) _yourLibrary.Add(card);
                    else _oppLibrary.Add(card);
                    SetCommanderLocation(isYour, CommanderLocation.Library);
                    break;
            }
            UpdateZoneCounts();
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
            (_yourField, _oppField) = (_oppField, _yourField);
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
            // Reset all visual references before redraw
            foreach (var bc in _yourField.Concat(_oppField)) bc.Visual = null;

            RenderHand(isYour: true);
            RenderCommander(isYour: true);
            RenderCommander(isYour: false);
            RedrawBattlefield(isYour: true);
            RedrawBattlefield(isYour: false);
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
            // Untap all your permanents
            UntapAllYour();

            // Advance to next turn
            _currentPhase = 0;
            _turnCounter++;
            UpdatePhaseDisplay();

            // Auto-draw one card (Draw phase)
            DrawCard();
        }

        // ================================================================
        // PHASE 4 — DRAW & MULLIGAN
        // ================================================================
        private void BtnDrawCard_Click(object sender, RoutedEventArgs e)
            => DrawCard();

        private void DrawCard()
        {
            if (_yourLibrary.Count == 0)
            {
                MessageBox.Show("Your library is empty!",
                    "Draw", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _yourHand.Add(_yourLibrary[0]);
            _yourLibrary.RemoveAt(0);
            UpdateZoneCounts();
            UpdateHandCounts();
            RenderHand(isYour: true);
        }

        private int _mulliganCount = 0; // tracks how many times mulliganed this game

        private void BtnMulligan_Click(object sender, RoutedEventArgs e)
            => DoMulligan();

        private void DoMulligan()
        {
            // Return current hand to library and shuffle
            foreach (var card in _yourHand)
                _yourLibrary.Add(card);
            _yourHand.Clear();
            Shuffle(_yourLibrary);

            // Draw 7
            for (int i = 0; i < 7 && _yourLibrary.Count > 0; i++)
            {
                _yourHand.Add(_yourLibrary[0]);
                _yourLibrary.RemoveAt(0);
            }

            _mulliganCount++;

            bool isCommander = _yourCommander != null;

            // Open mulligan window
            var dlg = new MulliganWindow(
                new List<DeckCard>(_yourHand),
                _yourLibrary,
                _mulliganCount,
                isCommander)
            { Owner = this };

            if (dlg.ShowDialog() != true) return;

            if (dlg.MulliganedAgain)
            {
                // Recurse — mulligan again
                DoMulligan();
                return;
            }

            // Apply result — set hand to kept cards
            _yourHand.Clear();
            foreach (var card in dlg.FinalHand)
                _yourHand.Add(card);

            // Put selected cards on bottom of library
            foreach (var card in dlg.BottomCards)
                _yourLibrary.Add(card);

            UpdateZoneCounts();
            UpdateHandCounts();
            RenderHand(isYour: true);
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
        // PHASE 5 — BATTLEFIELD
        // ================================================================

        // -- Play card from hand to battlefield --
        // Get the correct canvas for any battlefield card
        private Canvas GetCardCanvas(BattlefieldCard bc) => bc.IsLandZone
            ? (bc.IsYour ? YourLandCanvas : OppLandCanvas)
            : (bc.IsYour ? YourBattlefieldCanvas : OppBattlefieldCanvas);

        private void PlayCardFromHand(int handIndex, bool isYour, bool toLand = false)
        {
            var hand = isYour ? _yourHand : _oppHand;
            var field = isYour ? _yourField : _oppField;
            var canvas = toLand
                ? (isYour ? YourLandCanvas : OppLandCanvas)
                : (isYour ? YourBattlefieldCanvas : OppBattlefieldCanvas);

            if (handIndex < 0 || handIndex >= hand.Count) return;
            var card = hand[handIndex];
            hand.RemoveAt(handIndex);

            // Count cards already in this specific zone for positioning
            var zoneCards = field.Where(c => c.IsLandZone == toLand).ToList();
            double x = 20 + (zoneCards.Count % 8) * (FieldW + 6);
            double y = 10 + (zoneCards.Count / 8) * (FieldH + 6);

            var bc = new BattlefieldCard
            {
                Card = card,
                X = x,
                Y = y,
                IsYour = isYour,
                IsLandZone = toLand   // track which zone this card lives in
            };
            field.Add(bc);
            RenderBattlefieldCard(bc, canvas);
            UpdateHandCounts();
            RenderHand(isYour: true);
        }

        // -- Safely remove old visual --
        private static void RemoveCardVisual(BattlefieldCard bc, Canvas canvas)
        {
            if (bc.Visual == null) return;
            // Detach child from logical tree BEFORE removing from canvas
            try { bc.Visual.Child = null; } catch { }
            try
            {
                if (canvas.Children.Contains(bc.Visual))
                    canvas.Children.Remove(bc.Visual);
            }
            catch { }
            bc.Visual = null;
        }

        // -- Render a single battlefield card --
        private void RenderBattlefieldCard(BattlefieldCard bc, Canvas canvas)
        {
            RemoveCardVisual(bc, canvas); // always clean up old visual
            var border = MakeCardVisual(bc.Card, bc.IsYour, isHand: false);
            // Always portrait dimensions — LayoutTransform rotates for tapped
            border.Width = FieldW;
            border.Height = FieldH;
            border.Tag = bc;

            // Tapped = border rotated 90° via LayoutTransform (affects layout size)
            // Untapped = portrait (90w x 126h), no transform
            if (bc.IsTapped)
            {
                border.LayoutTransform = new RotateTransform(90);
                border.BorderBrush = new SolidColorBrush(
                    Color.FromRgb(0xFF, 0xCC, 0x44));
            }
            else
            {
                border.LayoutTransform = new RotateTransform(0);
                border.BorderBrush = new SolidColorBrush(
                    Color.FromRgb(0x22, 0x22, 0x22));
            }

            // Counter badge — overlay inside the border, not wrapping it
            if (bc.Counters.Count > 0)
            {
                var badgeText = BuildCounterText(bc);

                var grid = new Grid();
                var existingChild = border.Child;
                border.Child = null;
                grid.Children.Add(existingChild ?? new UIElement());
                var badge = new TextBlock
                {
                    Text = badgeText,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
                    Padding = new Thickness(4, 2, 4, 2),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Panel.SetZIndex(badge, 10);
                grid.Children.Add(badge);
                border.Child = grid;
            }

            // Click = tap/untap
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    ShowCardEnlarged(bc.Card, bc);
                    e.Handled = true;
                    return;
                }
                TapUntap(bc, canvas);
                e.Handled = true;
            };

            // Right-click = context menu
            border.MouseRightButtonDown += (s, e) =>
            {
                ShowBattlefieldContextMenu(bc, canvas, border);
                e.Handled = true;
            };

            // Drag to reposition
            bool dragging = false;
            Point dragStart = default;
            border.MouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || !dragging) return;
                var pos = e.GetPosition(canvas);
                bc.X = pos.X - dragStart.X;
                bc.Y = pos.Y - dragStart.Y;
                Canvas.SetLeft(border, bc.X);
                Canvas.SetTop(border, bc.Y);
            };
            border.MouseDown += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                dragging = true;
                dragStart = e.GetPosition(border);
                border.CaptureMouse();
            };
            border.MouseUp += (s, e) =>
            {
                dragging = false;
                border.ReleaseMouseCapture();
            };

            bc.Visual = border;
            Canvas.SetLeft(border, bc.X);
            Canvas.SetTop(border, bc.Y);
            Panel.SetZIndex(border, 5);
            canvas.Children.Add(border);
        }

        // -- Tap / Untap --
        private void TapUntap(BattlefieldCard bc, Canvas canvas)
        {
            bc.IsTapped = !bc.IsTapped;
            RenderBattlefieldCard(bc, GetCardCanvas(bc));
        }

        // -- Untap All your permanents --
        private void UntapAllYour()
        {
            foreach (var bc in _yourField)
                bc.IsTapped = false;
            RedrawBattlefield(isYour: true);
        }

        // -- Redraw full battlefield --
        private void RedrawBattlefield(bool isYour)
        {
            // Clear both canvases for this player
            if (isYour) { YourBattlefieldCanvas.Children.Clear(); YourLandCanvas.Children.Clear(); }
            else { OppBattlefieldCanvas.Children.Clear(); OppLandCanvas.Children.Clear(); }

            var field = isYour ? _yourField : _oppField;
            foreach (var bc in field)
            {
                bc.Visual = null; // reset — canvas already cleared
                RenderBattlefieldCard(bc, GetCardCanvas(bc));
            }
        }

        // -- Context menu --
        private void ShowBattlefieldContextMenu(BattlefieldCard bc,
            Canvas canvas, Border border)
        {
            bool isYour = bc.IsYour;
            var menu = new ContextMenu();

            void Add(string header, Action action)
            {
                var item = new MenuItem { Header = header };
                item.Click += (s, e) => action();
                menu.Items.Add(item);
            }
            void Sep() => menu.Items.Add(new Separator());

            Add(bc.IsTapped ? "⟳ Untap" : "↷ Tap", () => TapUntap(bc, canvas));
            Sep();

            // Move to zone
            var moveMenu = new MenuItem { Header = "Move to →" };
            void AddMove(string label, Action act)
            {
                var mi = new MenuItem { Header = label };
                mi.Click += (s, e) => act();
                moveMenu.Items.Add(mi);
            }

            AddMove("Graveyard", () => MoveFromBattlefield(bc, "graveyard", isYour));
            AddMove("Exile", () => MoveFromBattlefield(bc, "exile", isYour));
            AddMove("Hand", () => MoveFromBattlefield(bc, "hand", isYour));
            AddMove("Library — Top", () => MoveFromBattlefield(bc, "libtop", isYour));
            AddMove("Library — Bottom", () => MoveFromBattlefield(bc, "libbot", isYour));
            if (bc.Card.IsCommander)
                AddMove("Command Zone", () => MoveFromBattlefield(bc, "command", isYour));
            menu.Items.Add(moveMenu);

            Sep();

            // Add counter
            var counterMenu = new MenuItem { Header = "Add Counter →" };
            foreach (var ctype in new[] { "+1/+1", "-1/-1", "+1/0", "0/+1",
                "Loyalty", "Charge", "Time", "Poison", "Custom..." })
            {
                var ct = ctype;
                var mi = new MenuItem { Header = ct };
                mi.Click += (s, e) => AddCounter(bc, ct, canvas);
                counterMenu.Items.Add(mi);
            }
            menu.Items.Add(counterMenu);

            // Remove counter
            if (bc.Counters.Count > 0)
            {
                var removeMenu = new MenuItem { Header = "Remove Counter →" };
                foreach (var kv in bc.Counters.ToList())
                {
                    var key = kv.Key;
                    var mi = new MenuItem { Header = $"{key} ({kv.Value})" };
                    mi.Click += (s, e) => RemoveCounter(bc, key, canvas);
                    removeMenu.Items.Add(mi);
                }
                menu.Items.Add(removeMenu);
            }

            Sep();
            Add("🔍 View Card", () => ShowCardEnlarged(bc.Card, bc));
            Add("📋 Clone", () => CloneCard(bc, canvas, isYour));

            border.ContextMenu = menu;
            menu.IsOpen = true;
        }

        // -- Move from battlefield to another zone --
        private void MoveFromBattlefield(BattlefieldCard bc,
            string destination, bool isYour)
        {
            var field = isYour ? _yourField : _oppField;

            RemoveCardVisual(bc, GetCardCanvas(bc));
            field.Remove(bc);

            bool isCommander = (isYour && _yourCommander?.Name == bc.Card.Name)
                             || (!isYour && _oppCommander?.Name == bc.Card.Name)
                             || bc.Card.IsCommander;

            if (isCommander && destination != "command")
            {
                // Commander leaving battlefield — offer zone choice
                PromptCommanderZone(bc.Card, isYour, destination);
                return;
            }

            // Non-commander or going directly to command zone
            switch (destination)
            {
                case "graveyard":
                    if (isYour) _yourGrave.Add(bc.Card); else _oppGrave.Add(bc.Card);
                    break;
                case "exile":
                    if (isYour) _yourExile.Add(bc.Card); else _oppExile.Add(bc.Card);
                    break;
                case "hand":
                    if (isYour) _yourHand.Add(bc.Card); else _oppHand.Add(bc.Card);
                    RenderHand(isYour: true); UpdateHandCounts();
                    break;
                case "libtop":
                    if (isYour) _yourLibrary.Insert(0, bc.Card);
                    else _oppLibrary.Insert(0, bc.Card);
                    break;
                case "libbot":
                    if (isYour) _yourLibrary.Add(bc.Card);
                    else _oppLibrary.Add(bc.Card);
                    break;
                case "command":
                    if (isYour) _yourCommander = bc.Card;
                    else _oppCommander = bc.Card;
                    SetCommanderLocation(isYour, CommanderLocation.CommandZone);
                    RenderCommander(isYour);
                    break;
            }
            UpdateZoneCounts();
        }

        // -- Add counter --
        private void AddCounter(BattlefieldCard bc, string type, Canvas canvas)
        {
            string key = type;
            if (type == "Custom...")
            {
                var dlg = new InputDialog("Custom Counter",
                    "Enter counter type (e.g. Oil, Quest):")
                { Owner = this };
                if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value))
                    return;
                key = dlg.Value.Trim();
            }
            // For P/T counters combine into running totals
            if (key == "+1/+1") { AddPTCounter(bc, 1, 1); }
            else if (key == "-1/-1") { AddPTCounter(bc, -1, -1); }
            else if (key == "+1/0") { AddPTCounter(bc, 1, 0); }
            else if (key == "0/+1") { AddPTCounter(bc, 0, 1); }
            else { bc.Counters.TryGetValue(key, out int cur); bc.Counters[key] = cur + 1; }
            RenderBattlefieldCard(bc, GetCardCanvas(bc));
        }

        // -- Remove counter --
        private void RemoveCounter(BattlefieldCard bc, string key, Canvas canvas)
        {
            if (!bc.Counters.ContainsKey(key)) return;
            bc.Counters[key]--;
            if (bc.Counters[key] <= 0) bc.Counters.Remove(key);
            RenderBattlefieldCard(bc, GetCardCanvas(bc));
        }

        // -- Clone card --
        private void CloneCard(BattlefieldCard bc, Canvas canvas, bool isYour)
        {
            var clone = new BattlefieldCard
            {
                Card = bc.Card,
                X = bc.X + 20,
                Y = bc.Y + 20,
                IsYour = isYour
            };
            var field = isYour ? _yourField : _oppField;
            field.Add(clone);
            RenderBattlefieldCard(clone, canvas);
        }

        // -- P/T counter accumulation --
        private static void AddPTCounter(BattlefieldCard bc, int power, int tough)
        {
            bc.Counters.TryGetValue("_P", out int p);
            bc.Counters.TryGetValue("_T", out int t);
            bc.Counters["_P"] = p + power;
            bc.Counters["_T"] = t + tough;
            // Remove if both zero
            if (bc.Counters["_P"] == 0 && bc.Counters["_T"] == 0)
            {
                bc.Counters.Remove("_P");
                bc.Counters.Remove("_T");
            }
        }

        // -- Build display text for counters --
        private static string BuildCounterText(BattlefieldCard bc)
        {
            var parts = new List<string>();
            int p = 0, t = 0;
            bc.Counters.TryGetValue("_P", out p);
            bc.Counters.TryGetValue("_T", out t);
            if (p != 0 || t != 0)
                parts.Add($"{(p >= 0 ? "+" : "")}{p}/{(t >= 0 ? "+" : "")}{t}");
            foreach (var kv in bc.Counters.Where(k => k.Key != "_P" && k.Key != "_T"))
                parts.Add($"{kv.Key}×{kv.Value}");
            return string.Join("  ", parts);
        }

        // -- View card enlarged --
        private void ShowCardEnlarged(DeckCard card, BattlefieldCard? bc = null)
        {
            _enlargedCardWindow?.Close();
            _enlargedCardWindow = null;

            bool hasCounters = bc != null && bc.Counters.Count > 0;
            var win = new Window
            {
                Title = card.Name,
                Width = 340,
                Height = hasCounters ? 540 : 480,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = Brushes.Black
            };

            var stack = new StackPanel { Background = Brushes.Black };

            // Card image
            if (!string.IsNullOrEmpty(card.LocalImagePath)
                && System.IO.File.Exists(card.LocalImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(card.LocalImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    stack.Children.Add(new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Uniform,
                        Height = 460
                    });
                }
                catch
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = card.Name,
                        Foreground = Brushes.White,
                        FontSize = 18,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 0)
                    });
                }
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = card.Name,
                    Foreground = Brushes.White,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
            }

            // Counter info
            if (hasCounters)
            {
                var counterText = BuildCounterText(bc!);
                if (!string.IsNullOrEmpty(counterText))
                    stack.Children.Add(new TextBlock
                    {
                        Text = counterText,
                        Foreground = Brushes.Yellow,
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Background = new SolidColorBrush(
                            Color.FromArgb(200, 0, 0, 0)),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 4, 0, 0)
                    });
            }

            win.Content = stack;
            win.MouseLeftButtonDown += (s, e) => win.Close();
            win.Closed += (s, e) =>
            {
                if (_enlargedCardWindow == win) _enlargedCardWindow = null;
            };
            _enlargedCardWindow = win;
            win.Show();
        }

        // -- Wire hand cards to play on right-click --
        private void WireHandCardPlay(Border border, int index, bool isYour)
        {
            border.MouseRightButtonDown += (s, e) =>
            {
                var menu = new ContextMenu();

                var playBf = new MenuItem { Header = "▶ Play to Battlefield" };
                playBf.Click += (s2, e2) => PlayCardFromHand(index, isYour, toLand: false);

                var playMana = new MenuItem { Header = "◈ Play to Mana Zone" };
                playMana.Click += (s2, e2) => PlayCardFromHand(index, isYour, toLand: true);

                menu.Items.Add(playBf);
                menu.Items.Add(playMana);
                border.ContextMenu = menu;
                menu.IsOpen = true;
                e.Handled = true;
            };
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
            => UntapAllYour();
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
    // SIMPLE INPUT DIALOG — for custom counter type
    // ================================================================
    public class InputDialog : Window
    {
        private readonly TextBox _box;
        public string Value => _box.Text;

        public InputDialog(string title, string prompt)
        {
            Title = title;
            Width = 320; Height = 130;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;

            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock
            { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });
            _box = new TextBox { Height = 26, Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(_box);
            var btn = new Button
            { Content = "OK", Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
            btn.Click += (s, e) => { DialogResult = true; };
            stack.Children.Add(btn);
            Content = stack;
            _box.Focus();
        }
    }

    // ================================================================
    // COMMANDER ZONE TRACKER
    // ================================================================
    public enum CommanderLocation
    {
        CommandZone, Battlefield, Graveyard, Exile, Hand, Library
    }

    // ================================================================
    // BATTLEFIELD CARD — tracks position, tap state, counters
    // ================================================================
    public class BattlefieldCard
    {
        public DeckCard Card { get; set; } = null!;
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsTapped { get; set; }
        public bool IsYour { get; set; }
        public bool IsLandZone { get; set; } // true = mana zone, false = battlefield
        public Dictionary<string, int> Counters { get; set; } = new();
        public Border? Visual { get; set; }
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