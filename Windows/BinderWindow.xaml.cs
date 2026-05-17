using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BreakersOfE.Windows
{
    public partial class BinderWindow : Window
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const int CardsPerPage = 9;   // 3x3
        private const double PocketW = 100; // card pocket width
        private const double PocketH = 140; // card pocket height

        // ── State ─────────────────────────────────────────────────────────────
        private bool _showingHave = true;  // true = Have list, false = Want list
        private int _currentPage = 0;     // 0-based

        // Loaded card data
        private List<TradeBinderDisplayRow> _haveCards = new();
        private List<WantListDisplayRow> _wantCards = new();

        // ── Constructor ───────────────────────────────────────────────────────
        public BinderWindow()
        {
            InitializeComponent();
            LoadData();
            RenderPage();
        }

        // ── Data loading ──────────────────────────────────────────────────────
        private void LoadData()
        {
            _haveCards = LoadHaveCards();
            _wantCards = LoadWantCards();
        }

        private static List<TradeBinderDisplayRow> LoadHaveCards()
        {
            using var cdb = new CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.TradeBinderEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return new();
            var ids = entries.Select(e => e.PoolId).ToHashSet();
            var cards = pdb.PoolCards.AsNoTracking()
                .Where(c => ids.Contains(c.PoolId))
                .ToList().ToDictionary(c => c.PoolId);
            var rows = new List<TradeBinderDisplayRow>();
            foreach (var e in entries)
            {
                if (!cards.TryGetValue(e.PoolId, out var pc)) continue;
                for (int i = 0; i < e.Quantity; i++) // one pocket per copy
                    rows.Add(new TradeBinderDisplayRow
                    {
                        EntryId = e.TradeBinderEntryId,
                        PoolId = pc.PoolId,
                        Name = pc.Name,
                        SetCode = pc.SetCode,
                        Rarity = pc.Rarity,
                        Quantity = e.Quantity,
                        IsFoil = e.IsFoil,
                        Condition = e.Condition,
                        AskingPrice = e.AskingPrice,
                        MarketPrice = e.IsFoil ? pc.PriceUsdFoil : pc.PriceUsd,
                        LocalImagePath = pc.LocalImagePath,
                        ImageNormalUrl = pc.ImageNormalUrl,
                    });
            }
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return rows;
        }

        private static List<WantListDisplayRow> LoadWantCards()
        {
            using var cdb = new CollectionDbContext();
            using var pdb = new AppDbContext();
            var entries = cdb.WantListEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return new();
            var ids = entries.Select(e => e.PoolId).ToHashSet();
            var cards = pdb.PoolCards.AsNoTracking()
                .Where(c => ids.Contains(c.PoolId))
                .ToList().ToDictionary(c => c.PoolId);
            var rows = new List<WantListDisplayRow>();
            foreach (var e in entries)
            {
                if (!cards.TryGetValue(e.PoolId, out var pc)) continue;
                for (int i = 0; i < e.Quantity; i++)
                    rows.Add(new WantListDisplayRow
                    {
                        EntryId = e.WantListEntryId,
                        PoolId = pc.PoolId,
                        Name = pc.Name,
                        SetCode = pc.SetCode,
                        Rarity = pc.Rarity,
                        Quantity = e.Quantity,
                        IsFoil = e.IsFoil,
                        OfferPrice = e.OfferPrice,
                        MarketPrice = e.IsFoil ? pc.PriceUsdFoil : pc.PriceUsd,
                        LocalImagePath = pc.LocalImagePath,
                        ImageNormalUrl = pc.ImageNormalUrl,
                    });
            }
            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return rows;
        }

        // ── Rendering ─────────────────────────────────────────────────────────
        private void RenderPage()
        {
            var cards = _showingHave
                ? _haveCards.Cast<object>().ToList()
                : _wantCards.Cast<object>().ToList();

            int total = cards.Count;
            int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)CardsPerPage));
            _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);

            // Update header info
            PageIndicatorText.Text = $"Page {_currentPage + 1} of {totalPages}";
            CardCountText.Text = $"{total} card{(total == 1 ? "" : "s")}";

            // Nav button states
            BtnFirst.IsEnabled = _currentPage > 0;
            BtnPrev.IsEnabled = _currentPage > 0;
            BtnNext.IsEnabled = _currentPage < totalPages - 1;

            // Get the 9 cards for this page
            int start = _currentPage * CardsPerPage;
            var pageCards = cards.Skip(start).Take(CardsPerPage).ToList();

            // Clear and rebuild the 3x3 grid
            CardGrid.Children.Clear();

            for (int i = 0; i < CardsPerPage; i++)
            {
                if (i < pageCards.Count)
                    CardGrid.Children.Add(MakePocket(pageCards[i]));
                else
                    CardGrid.Children.Add(MakeEmptyPocket());
            }
        }

        private UIElement MakePocket(object card)
        {
            string name = card is TradeBinderDisplayRow tb ? tb.Name
                               : card is WantListDisplayRow wl ? wl.Name : "";
            string localPath = card is TradeBinderDisplayRow tb2 ? tb2.LocalImagePath
                               : card is WantListDisplayRow wl2 ? wl2.LocalImagePath : "";
            bool isFoil = card is TradeBinderDisplayRow tb3 && tb3.IsFoil
                               || card is WantListDisplayRow wl3 && wl3.IsFoil;
            string priceStr = card is TradeBinderDisplayRow tb4 ? tb4.PriceDisplay
                               : card is WantListDisplayRow wl4 ? wl4.PriceDisplay : "";
            string condition = card is TradeBinderDisplayRow tb5 ? tb5.Condition : "";

            var outer = new Border
            {
                Margin = new Thickness(6),
                Cursor = Cursors.Hand,
                ToolTip = $"{name}{(isFoil ? " (Foil)" : "")}\n{priceStr}"
            };

            var grid = new Grid();
            outer.Child = grid;

            // Pocket sleeve background
            var sleeve = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xCC, 0xC8, 0xBC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0xA0, 0x90)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(3)
            };
            grid.Children.Add(sleeve);

            // Card image inside sleeve
            var inner = new Grid();
            sleeve.Child = inner;

            // Image or fallback
            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(localPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 94;
                    bmp.EndInit();
                    bmp.Freeze();
                    inner.Children.Add(new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Stretch = Stretch.Fill,
                        Width = 94,
                        Height = 131
                    });
                }
                catch { inner.Children.Add(MakeFallbackCard(name)); }
            }
            else
            {
                inner.Children.Add(MakeFallbackCard(name));
            }

            // Foil shimmer overlay
            if (isFoil)
            {
                inner.Children.Add(new Border
                {
                    Width = 94,
                    Height = 131,
                    Background = new LinearGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0, 0xFF, 0xD7, 0x00), 0.0),
                            new GradientStop(Color.FromArgb(60, 0xFF, 0xD7, 0x00), 0.5),
                            new GradientStop(Color.FromArgb(0, 0xFF, 0xD7, 0x00), 1.0),
                        },
                        new Point(0, 0), new Point(1, 1)),
                    IsHitTestVisible = false
                });
            }

            // Card name label below image
            var nameLabel = new TextBlock
            {
                Text = name,
                FontSize = 9,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x18, 0x10)),
                Margin = new Thickness(0, 2, 0, 0),
                MaxWidth = 94
            };

            // Price label
            var priceLabel = new TextBlock
            {
                Text = priceStr,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0x44)),
                Margin = new Thickness(0, 1, 0, 0)
            };

            var pocketStack = new StackPanel();
            pocketStack.Children.Add(sleeve);
            pocketStack.Children.Add(nameLabel);
            pocketStack.Children.Add(priceLabel);
            outer.Child = pocketStack;

            // Click to view enlarged
            outer.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount >= 2) ShowEnlarged(card);
            };

            outer.MouseRightButtonDown += (s, e) =>
            {
                ShowPocketContextMenu(card, outer);
                e.Handled = true;
            };

            return outer;
        }

        private static Border MakeFallbackCard(string name)
        {
            return new Border
            {
                Width = 94,
                Height = 131,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x1A, 0x2A, 0x5E),
                    Color.FromRgb(0x0E, 0x18, 0x3A),
                    new Point(0, 0), new Point(1, 1)),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock
                {
                    Text = name,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(4)
                }
            };
        }

        private static UIElement MakeEmptyPocket()
        {
            return new Border
            {
                Margin = new Thickness(6),
                Width = 100,
                Height = 158,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0xAA, 0xA0, 0x90)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(40, 0xCC, 0xC8, 0xBC))
            };
        }

        // ── Context menu ──────────────────────────────────────────────────────
        private void ShowPocketContextMenu(object card, FrameworkElement anchor)
        {
            var menu = new ContextMenu();

            string name = card is TradeBinderDisplayRow tb ? tb.Name
                        : card is WantListDisplayRow wl ? wl.Name : "";

            var view = new MenuItem { Header = $"🔍 View Card — {name}" };
            view.Click += (s, e) => ShowEnlarged(card);
            menu.Items.Add(view);

            menu.Items.Add(new Separator());

            var remove = new MenuItem
            {
                Header = _showingHave ? "✕ Remove from Trade Binder"
                                          : "✕ Remove from Want List",
                Foreground = Brushes.IndianRed
            };
            remove.Click += (s, e) =>
            {
                if (MessageBox.Show($"Remove '{name}'?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes) return;

                RemoveEntry(card);
            };
            menu.Items.Add(remove);

            anchor.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void RemoveEntry(object card)
        {
            using var db = new CollectionDbContext();
            if (card is TradeBinderDisplayRow tb)
            {
                var e = db.TradeBinderEntries.FirstOrDefault(
                    x => x.TradeBinderEntryId == tb.EntryId);
                if (e != null)
                {
                    if (e.Quantity > 1) e.Quantity--;
                    else db.TradeBinderEntries.Remove(e);
                    db.SaveChanges();
                }
            }
            else if (card is WantListDisplayRow wl)
            {
                var e = db.WantListEntries.FirstOrDefault(
                    x => x.WantListEntryId == wl.EntryId);
                if (e != null)
                {
                    if (e.Quantity > 1) e.Quantity--;
                    else db.WantListEntries.Remove(e);
                    db.SaveChanges();
                }
            }
            LoadData();
            RenderPage();
        }

        // ── Enlarged card view ────────────────────────────────────────────────
        private void ShowEnlarged(object card)
        {
            string path = card is TradeBinderDisplayRow tb ? tb.LocalImagePath
                        : card is WantListDisplayRow wl ? wl.LocalImagePath : "";
            string name = card is TradeBinderDisplayRow tb2 ? tb2.Name
                        : card is WantListDisplayRow wl2 ? wl2.Name : "";

            ImageSource? src = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    src = bmp;
                }
                catch { }
            }

            var win = new CardImageWindow(src, name) { Owner = this };
            win.ShowDialog();
        }

        // ── Tab handlers ──────────────────────────────────────────────────────
        private void BtnTabHave_Click(object sender, RoutedEventArgs e)
        {
            _showingHave = true;
            _currentPage = 0;
            BtnTabHave.Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x4B));
            BtnTabHave.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x0E, 0x08));
            BtnTabWant.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x28, 0x10));
            BtnTabWant.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x4B));
            RenderPage();
        }

        private void BtnTabWant_Click(object sender, RoutedEventArgs e)
        {
            _showingHave = false;
            _currentPage = 0;
            BtnTabWant.Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x4B));
            BtnTabWant.Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x0E, 0x08));
            BtnTabHave.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x28, 0x10));
            BtnTabHave.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xA8, 0x4B));
            RenderPage();
        }

        // ── Navigation ────────────────────────────────────────────────────────
        private void BtnFirst_Click(object sender, RoutedEventArgs e)
        { _currentPage = 0; RenderPage(); }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        { if (_currentPage > 0) { _currentPage--; RenderPage(); } }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            var cards = _showingHave
                ? _haveCards.Cast<object>().ToList()
                : _wantCards.Cast<object>().ToList();
            int totalPages = Math.Max(1,
                (int)Math.Ceiling(cards.Count / (double)CardsPerPage));
            if (_currentPage < totalPages - 1) { _currentPage++; RenderPage(); }
        }

        // ── Keyboard navigation ───────────────────────────────────────────────
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Right || e.Key == Key.PageDown)
                BtnNext_Click(this, new RoutedEventArgs());
            else if (e.Key == Key.Left || e.Key == Key.PageUp)
                BtnPrev_Click(this, new RoutedEventArgs());
            else if (e.Key == Key.Home)
                BtnFirst_Click(this, new RoutedEventArgs());
        }
    }
}