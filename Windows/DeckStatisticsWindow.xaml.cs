using BreakersOfE.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BreakersOfE.Windows
{
    // ── Helper row models ─────────────────────────────────────────────────────
    public class EditionRow
    {
        public string SetName { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public class LegalityRow
    {
        public string Format { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Issues { get; set; } = string.Empty;
        public bool IsLegal { get; set; }
    }

    public partial class DeckStatisticsWindow : Window
    {
        private readonly Deck _deck;

        // ── Color palettes ────────────────────────────────────────────────────
        private static readonly Dictionary<string, Color> MtgColors = new()
        {
            { "White",      Color.FromRgb(0xF5, 0xF0, 0xC0) },
            { "Blue",       Color.FromRgb(0x14, 0x6B, 0xD5) },
            { "Black",      Color.FromRgb(0x55, 0x55, 0x55) },
            { "Red",        Color.FromRgb(0xD3, 0x21, 0x2D) },
            { "Green",      Color.FromRgb(0x00, 0x73, 0x3E) },
            { "Multicolor", Color.FromRgb(0xD4, 0xAF, 0x37) },
            { "Colorless",  Color.FromRgb(0xC0, 0xBE, 0xB5) },
        };

        private static readonly Dictionary<string, Color> RarityColors = new()
        {
            { "Common",   Color.FromRgb(0x99, 0x99, 0x99) },
            { "Uncommon", Color.FromRgb(0x70, 0x90, 0xA0) },
            { "Rare",     Color.FromRgb(0xC0, 0x90, 0x30) },
            { "Mythic",   Color.FromRgb(0xE0, 0x6B, 0x1A) },
            { "Special",  Color.FromRgb(0xB8, 0x4A, 0xA4) },
            { "Other",    Color.FromRgb(0x88, 0x88, 0x88) },
        };

        private static readonly Color[] Palette =
        {
            Color.FromRgb(0xE0,0x50,0x3A), Color.FromRgb(0x3A,0x7E,0xD8),
            Color.FromRgb(0xE0,0x9A,0x2A), Color.FromRgb(0x3A,0xB8,0x6A),
            Color.FromRgb(0x8A,0x4A,0xC8), Color.FromRgb(0xD8,0x6A,0xB0),
            Color.FromRgb(0x4A,0xC8,0xD8), Color.FromRgb(0xB8,0xB8,0x30),
            Color.FromRgb(0x88,0x44,0x22), Color.FromRgb(0x22,0x88,0x88),
            Color.FromRgb(0xCC,0x44,0x88), Color.FromRgb(0x44,0xAA,0x44),
            Color.FromRgb(0x66,0x66,0xCC), Color.FromRgb(0xAA,0x66,0x00),
            Color.FromRgb(0x00,0xAA,0x66), Color.FromRgb(0xCC,0x66,0x66),
        };

        // ── Mana color defs ───────────────────────────────────────────────────
        private static readonly (string Name, char Symbol, Color Fill)[] ManaColorDefs =
        {
            ("White",     'W', Color.FromRgb(0xF5,0xF0,0xC0)),
            ("Blue",      'U', Color.FromRgb(0x14,0x6B,0xD5)),
            ("Black",     'B', Color.FromRgb(0x55,0x55,0x55)),
            ("Red",       'R', Color.FromRgb(0xD3,0x21,0x2D)),
            ("Green",     'G', Color.FromRgb(0x00,0x73,0x3E)),
            ("Colorless", 'C', Color.FromRgb(0xC0,0xBE,0xB5)),
        };

        // ════════════════════════════════════════════════════════════════════
        // CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════
        public DeckStatisticsWindow(Deck deck)
        {
            InitializeComponent();
            _deck = deck;
            Title = $"Statistics — {deck.Name}";

            Loaded += (s, e) => RefreshAllTabs();
            SizeChanged += (s, e) => RefreshCurrentTab();
        }

        private void RefreshAllTabs()
        {
            DrawColorsTab();
            DrawRarityTab();
            DrawCardTypeTab();
            PopulateEditionsTab();
            DrawManaCurveTab();
            DrawManaProductTab();
            DrawPowerAnalyserTab();
            PopulateLegalityTab();
        }

        private void MainTabControl_SelectionChanged(object sender,
            SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            RefreshCurrentTab();
        }

        private void RefreshCurrentTab()
        {
            switch (MainTabControl.SelectedIndex)
            {
                case 0: DrawColorsTab(); break;
                case 1: DrawRarityTab(); break;
                case 2: DrawCardTypeTab(); break;
                case 4: DrawManaCurveTab(); break;
                case 5: DrawManaProductTab(); break;
                case 6: DrawPowerAnalyserTab(); break;
            }
        }

        private void CardTypeDetailCombo_SelectionChanged(object sender,
            SelectionChangedEventArgs e) => DrawCardTypeTab();

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();

        // ════════════════════════════════════════════════════════════════════
        // SHARED — DRAW PIE CHART
        // ════════════════════════════════════════════════════════════════════
        private static void DrawPieChart(
            Canvas canvas,
            WrapPanel legend,
            Dictionary<string, int> data,
            List<Color> colors)
        {
            canvas.Children.Clear();
            legend.Children.Clear();

            if (data == null || data.Count == 0 || data.Values.Sum() == 0)
            {
                canvas.Children.Add(new TextBlock
                {
                    Text = "No data",
                    FontSize = 14,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                return;
            }

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double cx = w / 2;
            double cy = h / 2 - 10;
            double rx = Math.Min(cx - 40, cy - 30);
            double ry = rx * 0.55;   // flat pie
            double depth = rx * 0.18; // 3D depth

            int total = data.Values.Sum();
            double startAngle = -Math.PI / 2; // start at top

            var keys = data.Keys.ToList();
            var values = data.Values.ToList();

            // Draw bottom 3D edges first (back to front)
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                double sweep = 2 * Math.PI * values[i] / total;
                double mid = startAngle + sweep / 2;

                // Only draw edges for bottom half slices
                if (Math.Sin(mid) > -0.1)
                {
                    double x1 = cx + rx * Math.Cos(startAngle);
                    double y1 = cy + ry * Math.Sin(startAngle);
                    double x2 = cx + rx * Math.Cos(startAngle + sweep);
                    double y2 = cy + ry * Math.Sin(startAngle + sweep);

                    var darkColor = DarkenColor(colors[i], 0.5);
                    var darkBrush = new SolidColorBrush(darkColor);

                    // Left edge
                    if (Math.Sin(startAngle) > -0.1)
                    {
                        var leftEdge = new Polygon
                        {
                            Fill = darkBrush,
                            Points = new PointCollection
                            {
                                new Point(x1, y1),
                                new Point(x1, y1 + depth),
                                new Point(cx, cy + depth),
                                new Point(cx, cy)
                            }
                        };
                        canvas.Children.Add(leftEdge);
                    }

                    // Right edge
                    if (Math.Sin(startAngle + sweep) > -0.1)
                    {
                        var rightEdge = new Polygon
                        {
                            Fill = darkBrush,
                            Points = new PointCollection
                            {
                                new Point(x2, y2),
                                new Point(x2, y2 + depth),
                                new Point(cx, cy + depth),
                                new Point(cx, cy)
                            }
                        };
                        canvas.Children.Add(rightEdge);
                    }

                    // Outer arc edge
                    var arcPath = new System.Windows.Shapes.Path
                    {
                        Fill = darkBrush,
                        Data = BuildArcGeometry(cx, cy, rx, ry,
                                     startAngle, sweep, depth)
                    };
                    canvas.Children.Add(arcPath);
                }

                startAngle += sweep;
            }

            // Reset and draw top slices
            startAngle = -Math.PI / 2;
            for (int i = 0; i < keys.Count; i++)
            {
                double sweep = 2 * Math.PI * values[i] / total;

                var slice = new System.Windows.Shapes.Path
                {
                    Fill = new SolidColorBrush(colors[i]),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Data = BuildSliceGeometry(cx, cy, rx, ry,
                                          startAngle, sweep)
                };
                canvas.Children.Add(slice);

                // Label on slice
                double mid = startAngle + sweep / 2;
                double pct = Math.Round(100.0 * values[i] / total);
                if (pct >= 3)
                {
                    double lx = cx + (rx * 0.68) * Math.Cos(mid);
                    double ly = cy + (ry * 0.68) * Math.Sin(mid);
                    string label = $"{keys[i]} {pct} %";

                    var border = new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromArgb(200, 0xFF, 0xFF, 0xE0)),
                        BorderBrush = Brushes.DarkGray,
                        BorderThickness = new Thickness(0.5),
                        Padding = new Thickness(3, 1, 3, 1),
                        Child = new TextBlock
                        {
                            Text = label,
                            FontSize = 10,
                            Foreground = Brushes.Black
                        }
                    };

                    border.Measure(new Size(double.PositiveInfinity,
                        double.PositiveInfinity));
                    Canvas.SetLeft(border, lx - border.DesiredSize.Width / 2);
                    Canvas.SetTop(border, ly - border.DesiredSize.Height / 2);
                    canvas.Children.Add(border);
                }

                startAngle += sweep;
            }

            // Legend
            for (int i = 0; i < keys.Count; i++)
            {
                var item = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(6, 2, 6, 2)
                };
                item.Children.Add(new Rectangle
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(colors[i]),
                    Stroke = Brushes.DarkGray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                item.Children.Add(new TextBlock
                {
                    Text = $"{values[i]} {keys[i]}",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                legend.Children.Add(item);
            }
        }

        private static Geometry BuildSliceGeometry(
            double cx, double cy, double rx, double ry,
            double startAngle, double sweep)
        {
            var geo = new StreamGeometry();
            using var ctx = geo.Open();

            double endAngle = startAngle + sweep;
            bool largeArc = sweep > Math.PI;

            double x1 = cx + rx * Math.Cos(startAngle);
            double y1 = cy + ry * Math.Sin(startAngle);
            double x2 = cx + rx * Math.Cos(endAngle);
            double y2 = cy + ry * Math.Sin(endAngle);

            ctx.BeginFigure(new Point(cx, cy), true, true);
            ctx.LineTo(new Point(x1, y1), true, false);
            ctx.ArcTo(new Point(x2, y2),
                new Size(rx, ry), 0, largeArc,
                SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(cx, cy), true, false);

            return geo;
        }

        private static Geometry BuildArcGeometry(
            double cx, double cy, double rx, double ry,
            double startAngle, double sweep, double depth)
        {
            // Only draw the outer arc edge for slices in lower hemisphere
            var geo = new StreamGeometry();
            using var ctx = geo.Open();

            double endAngle = startAngle + sweep;
            bool largeArc = sweep > Math.PI;

            // Clamp to lower hemisphere
            double s = Math.Max(startAngle, 0);
            double e = Math.Min(endAngle, Math.PI);
            if (s >= e) return geo;

            double x1 = cx + rx * Math.Cos(s);
            double y1 = cy + ry * Math.Sin(s);
            double x2 = cx + rx * Math.Cos(e);
            double y2 = cy + ry * Math.Sin(e);

            ctx.BeginFigure(new Point(x1, y1), true, true);
            ctx.ArcTo(new Point(x2, y2),
                new Size(rx, ry), 0, (e - s) > Math.PI,
                SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(x2, y2 + depth), true, false);
            ctx.ArcTo(new Point(x1, y1 + depth),
                new Size(rx, ry), 0, (e - s) > Math.PI,
                SweepDirection.Counterclockwise, true, false);

            return geo;
        }

        private static Color DarkenColor(Color c, double factor) =>
            Color.FromRgb(
                (byte)(c.R * factor),
                (byte)(c.G * factor),
                (byte)(c.B * factor));

        // ════════════════════════════════════════════════════════════════════
        // TAB 1 — COLORS
        // ════════════════════════════════════════════════════════════════════
        private void DrawColorsTab()
        {
            var cards = GetMainCards();
            var data = new Dictionary<string, int>();
            foreach (var card in cards)
            {
                string key = GetColorCategory(card.ColorIdentity);
                data.TryGetValue(key, out int ex);
                data[key] = ex + card.TotalQuantity;
            }

            var order = new[] { "White", "Blue", "Black", "Red", "Green", "Multicolor", "Colorless" };
            var sorted = order.Where(k => data.ContainsKey(k))
                              .ToDictionary(k => k, k => data[k]);
            var colors = sorted.Keys
                .Select(k => MtgColors.TryGetValue(k, out var c) ? c
                    : Color.FromRgb(0x88, 0x88, 0x88))
                .ToList();

            DrawPieChart(ColorsPieCanvas, ColorsLegendPanel, sorted, colors);
        }

        private static string GetColorCategory(string ci)
        {
            var chars = ci.Where(c => "WUBRG".Contains(c)).Distinct().ToList();
            if (chars.Count == 0) return "Colorless";
            if (chars.Count > 1) return "Multicolor";
            return chars[0] switch
            {
                'W' => "White",
                'U' => "Blue",
                'B' => "Black",
                'R' => "Red",
                'G' => "Green",
                _ => "Colorless"
            };
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 2 — RARITY
        // ════════════════════════════════════════════════════════════════════
        private void DrawRarityTab()
        {
            var cards = GetMainCards();
            var data = new Dictionary<string, int>();
            foreach (var card in cards)
            {
                string key = card.Rarity?.ToLower() switch
                {
                    "common" => "Common",
                    "uncommon" => "Uncommon",
                    "rare" => "Rare",
                    "mythic" => "Mythic",
                    "special" => "Special",
                    _ => "Other"
                };
                data.TryGetValue(key, out int ex);
                data[key] = ex + card.TotalQuantity;
            }

            var order = new[] { "Common", "Uncommon", "Rare", "Mythic", "Special", "Other" };
            var sorted = order.Where(k => data.ContainsKey(k))
                              .ToDictionary(k => k, k => data[k]);
            var colors = sorted.Keys
                .Select(k => RarityColors.TryGetValue(k, out var c) ? c
                    : Color.FromRgb(0x88, 0x88, 0x88))
                .ToList();

            DrawPieChart(RarityPieCanvas, RarityLegendPanel, sorted, colors);
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 3 — CARD TYPE
        // ════════════════════════════════════════════════════════════════════
        private void DrawCardTypeTab()
        {
            if (CardTypeDetailCombo?.SelectedIndex == null) return;
            var cards = GetMainCards();
            Dictionary<string, int> data;

            switch (CardTypeDetailCombo.SelectedIndex)
            {
                case 0: // General
                    data = new Dictionary<string, int>
                    {
                        ["Land"] = cards.Where(c => c.IsLand).Sum(c => c.TotalQuantity),
                        ["Creature"] = cards.Where(c => c.IsCreature && !c.IsLand).Sum(c => c.TotalQuantity),
                        ["Spell"] = cards.Where(c => !c.IsCreature && !c.IsLand).Sum(c => c.TotalQuantity),
                    };
                    data = data.Where(kv => kv.Value > 0)
                               .ToDictionary(kv => kv.Key, kv => kv.Value);
                    DrawPieChart(CardTypePieCanvas, CardTypeLegendPanel, data,
                        new List<Color>
                        {
                            Color.FromRgb(0xE0,0x7A,0x2A),
                            Color.FromRgb(0xAA,0x22,0x22),
                            Color.FromRgb(0x22,0x55,0xCC)
                        }.Take(data.Count).ToList());
                    break;

                case 1: // Main
                    data = GetMainTypeBreakdown(cards);
                    DrawPieChart(CardTypePieCanvas, CardTypeLegendPanel, data,
                        data.Keys.Select((k, i) => Palette[i % Palette.Length]).ToList());
                    break;

                default: // Detailed
                    data = GetDetailedTypeBreakdown(cards);
                    DrawPieChart(CardTypePieCanvas, CardTypeLegendPanel, data,
                        data.Keys.Select((k, i) => Palette[i % Palette.Length]).ToList());
                    break;
            }
        }

        private static Dictionary<string, int> GetMainTypeBreakdown(List<DeckCard> cards)
        {
            var data = new Dictionary<string, int>();
            foreach (var card in cards)
            {
                string key = GetMainType(card.TypeLine);
                data.TryGetValue(key, out int ex);
                data[key] = ex + card.TotalQuantity;
            }
            return data.OrderByDescending(kv => kv.Value)
                       .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static string GetMainType(string tl)
        {
            if (string.IsNullOrWhiteSpace(tl)) return "Other";
            var t = tl.ToLower();
            if (t.Contains("basic land")) return "Basic Land";
            if (t.Contains("land")) return "Land";
            if (t.Contains("artifact creature")) return "Artifact Creature";
            if (t.Contains("enchantment creature")) return "Enchantment Creature";
            if (t.Contains("legendary creature")) return "Legendary Creature";
            if (t.Contains("creature")) return "Creature";
            if (t.Contains("planeswalker")) return "Planeswalker";
            if (t.Contains("instant")) return "Instant";
            if (t.Contains("sorcery")) return "Sorcery";
            if (t.Contains("artifact")) return "Artifact";
            if (t.Contains("enchantment")) return "Enchantment";
            if (t.Contains("battle")) return "Battle";
            return "Other";
        }

        private static Dictionary<string, int> GetDetailedTypeBreakdown(List<DeckCard> cards)
        {
            var data = new Dictionary<string, int>();
            foreach (var card in cards)
            {
                string key = string.IsNullOrWhiteSpace(card.TypeLine)
                    ? "Other" : card.TypeLine;
                if (key.Length > 38) key = key[..35] + "...";
                data.TryGetValue(key, out int ex);
                data[key] = ex + card.TotalQuantity;
            }
            return data.OrderByDescending(kv => kv.Value)
                       .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 4 — EDITIONS
        // ════════════════════════════════════════════════════════════════════
        private void PopulateEditionsTab()
        {
            var rows = GetMainCards()
                .GroupBy(c => new { c.SetCode, c.SetName })
                .Select(g => new EditionRow
                {
                    SetCode = g.Key.SetCode,
                    SetName = string.IsNullOrEmpty(g.Key.SetName)
                                   ? g.Key.SetCode : g.Key.SetName,
                    Quantity = g.Sum(c => c.TotalQuantity)
                })
                .OrderBy(r => r.Quantity)
                .ToList();

            EditionsGrid.ItemsSource = rows;
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 5 — MANA CURVE
        // ════════════════════════════════════════════════════════════════════
        private void DrawManaCurveTab()
        {
            var canvas = ManaCurveCanvas;
            canvas.Children.Clear();
            ManaStatsPanel.Children.Clear();

            var nonLands = GetMainCards().Where(c => !c.IsLand).ToList();
            var buckets = new int[11];
            foreach (var card in nonLands)
            {
                int cmc = Math.Min(10, (int)card.ManaValue);
                buckets[cmc] += card.TotalQuantity;
            }

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double padL = 36, padR = 16, padT = 28, padB = 36;
            double chartW = w - padL - padR;
            double chartH = h - padT - padB;

            int maxVal = Math.Max(1, buckets.Max());
            double barGroupW = chartW / 11;
            double barW = barGroupW * 0.62;
            double barOff = barGroupW * 0.19;

            // Title
            AddCanvasText(canvas, "Mana Curve", padL + chartW / 2,
                10, 12, FontWeights.SemiBold, HorizontalAlignment.Center);

            // Axes
            canvas.Children.Add(new Line
            {
                X1 = padL,
                Y1 = padT,
                X2 = padL,
                Y2 = padT + chartH,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            });
            canvas.Children.Add(new Line
            {
                X1 = padL,
                Y1 = padT + chartH,
                X2 = padL + chartW,
                Y2 = padT + chartH,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            });

            // Y grid lines
            for (int g = 1; g <= 4; g++)
            {
                double yg = padT + chartH - chartH * g / 4.0;
                canvas.Children.Add(new Line
                {
                    X1 = padL,
                    Y1 = yg,
                    X2 = padL + chartW,
                    Y2 = yg,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                    StrokeThickness = 0.5
                });
                int yVal = (int)Math.Round(maxVal * g / 4.0);
                AddCanvasText(canvas, yVal.ToString(),
                    padL - 4, yg, 9, FontWeights.Normal,
                    HorizontalAlignment.Right);
            }

            var linePoints = new PointCollection();

            for (int i = 0; i < 11; i++)
            {
                double x = padL + i * barGroupW + barOff;
                double barH = chartH * buckets[i] / maxVal;
                double y = padT + chartH - barH;

                // Bar body
                if (buckets[i] > 0)
                {
                    var rect = new Rectangle
                    {
                        Width = barW,
                        Height = Math.Max(1, barH),
                        Fill = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33)),
                        Stroke = new SolidColorBrush(Color.FromRgb(0x99, 0x11, 0x11)),
                        StrokeThickness = 0.5
                    };
                    Canvas.SetLeft(rect, x);
                    Canvas.SetTop(rect, y);
                    canvas.Children.Add(rect);

                    // 3D bottom edge
                    var edge = new Rectangle
                    {
                        Width = barW,
                        Height = 5,
                        Fill = new SolidColorBrush(Color.FromRgb(0x77, 0x00, 0x00))
                    };
                    Canvas.SetLeft(edge, x + 2);
                    Canvas.SetTop(edge, y + Math.Max(1, barH) - 1);
                    canvas.Children.Add(edge);

                    // Count label
                    var lbl = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 220)),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(0.5),
                        Padding = new Thickness(2, 0, 2, 0),
                        Child = new TextBlock
                        {
                            Text = buckets[i].ToString(),
                            FontSize = 10
                        }
                    };
                    lbl.Measure(new Size(200, 200));
                    Canvas.SetLeft(lbl, x + barW / 2 - lbl.DesiredSize.Width / 2);
                    Canvas.SetTop(lbl, y - lbl.DesiredSize.Height - 1);
                    canvas.Children.Add(lbl);
                }
                else
                {
                    // Zero label
                    AddCanvasText(canvas, "0",
                        x + barW / 2, padT + chartH - 14,
                        9, FontWeights.Normal, HorizontalAlignment.Center);
                }

                // X label
                AddCanvasText(canvas, i == 10 ? "10+" : i.ToString(),
                    x + barW / 2, padT + chartH + 4,
                    10, FontWeights.Normal, HorizontalAlignment.Center);

                linePoints.Add(new Point(x + barW / 2,
                    buckets[i] > 0 ? y : padT + chartH));
            }

            // Trend line
            canvas.Children.Add(new Polyline
            {
                Points = linePoints,
                Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x88, 0xCC)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            });

            // Stats
            int total = nonLands.Sum(c => c.TotalQuantity);
            double totalMv = nonLands.Sum(c => c.ManaValue * c.TotalQuantity);
            double avgCmc = total > 0 ? totalMv / total : 0;

            ManaStatsPanel.Children.Add(new TextBlock
            {
                Text = $"Total Cards: {total}    " +
                       $"Total Mana: {totalMv:F2}    " +
                       $"Average CMC: {avgCmc:F2}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 6 — MANA PRODUCT (stacked bar by CMC)
        // ════════════════════════════════════════════════════════════════════
        private void DrawManaProductTab()
        {
            var canvas = ManaProductCanvas;
            canvas.Children.Clear();
            ManaProductLegendPanel.Children.Clear();

            // Build: for each CMC bucket, count mana pips per color
            var cards = GetMainCards();
            // buckets[cmc][colorIndex] = pip count
            var buckets = new double[11, ManaColorDefs.Length];

            foreach (var card in cards)
            {
                int cmc = Math.Min(10, (int)card.ManaValue);
                // Count pips from ManaCost string e.g. {W}{W}{U}{2}
                if (!string.IsNullOrEmpty(card.ManaCost))
                {
                    foreach (var (_, sym, _) in ManaColorDefs)
                    {
                        int count = CountPips(card.ManaCost, sym);
                        int ci = Array.FindIndex(ManaColorDefs,
                            x => x.Symbol == sym);
                        if (ci >= 0)
                            buckets[cmc, ci] += count * card.TotalQuantity;
                    }
                }
            }

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double padL = 36, padR = 16, padT = 28, padB = 36;
            double chartW = w - padL - padR;
            double chartH = h - padT - padB;

            // Find max stack height
            double maxStack = 0;
            for (int i = 0; i < 11; i++)
            {
                double s = 0;
                for (int j = 0; j < ManaColorDefs.Length; j++) s += buckets[i, j];
                maxStack = Math.Max(maxStack, s);
            }
            if (maxStack < 1) maxStack = 1;

            AddCanvasText(canvas, "Mana Product", padL + chartW / 2,
                10, 12, FontWeights.SemiBold, HorizontalAlignment.Center);

            // Axes
            canvas.Children.Add(new Line
            {
                X1 = padL,
                Y1 = padT,
                X2 = padL,
                Y2 = padT + chartH,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            });
            canvas.Children.Add(new Line
            {
                X1 = padL,
                Y1 = padT + chartH,
                X2 = padL + chartW,
                Y2 = padT + chartH,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            });

            double barGroupW = chartW / 11;
            double barW = barGroupW * 0.55;
            double barOff = barGroupW * 0.225;

            for (int i = 0; i < 11; i++)
            {
                double x = padL + i * barGroupW + barOff;
                double stackBottom = padT + chartH;
                double totalH = 0;
                for (int j = 0; j < ManaColorDefs.Length; j++)
                    totalH += buckets[i, j];

                if (totalH < 0.01)
                {
                    AddCanvasText(canvas, "0",
                        x + barW / 2, padT + chartH - 14,
                        9, FontWeights.Normal, HorizontalAlignment.Center);
                }
                else
                {
                    for (int j = ManaColorDefs.Length - 1; j >= 0; j--)
                    {
                        if (buckets[i, j] < 0.01) continue;
                        double segH = chartH * buckets[i, j] / maxStack;
                        stackBottom -= segH;

                        var rect = new Rectangle
                        {
                            Width = barW,
                            Height = Math.Max(1, segH),
                            Fill = new SolidColorBrush(ManaColorDefs[j].Fill),
                            Stroke = Brushes.White,
                            StrokeThickness = 0.5
                        };
                        Canvas.SetLeft(rect, x);
                        Canvas.SetTop(rect, stackBottom);
                        canvas.Children.Add(rect);

                        // Segment label
                        if (segH > 14)
                        {
                            var lbl = new Border
                            {
                                Background = new SolidColorBrush(
                                    Color.FromArgb(180, 255, 255, 220)),
                                BorderBrush = Brushes.Gray,
                                BorderThickness = new Thickness(0.5),
                                Padding = new Thickness(1, 0, 1, 0),
                                Child = new TextBlock
                                {
                                    Text = ((int)buckets[i, j]).ToString(),
                                    FontSize = 9
                                }
                            };
                            lbl.Measure(new Size(200, 200));
                            Canvas.SetLeft(lbl, x + barW / 2 - lbl.DesiredSize.Width / 2);
                            Canvas.SetTop(lbl,
                                stackBottom + segH / 2 - lbl.DesiredSize.Height / 2);
                            canvas.Children.Add(lbl);
                        }
                    }

                    // Total on top
                    var totalLbl = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 220)),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(0.5),
                        Padding = new Thickness(2, 0, 2, 0),
                        Child = new TextBlock
                        {
                            Text = ((int)totalH).ToString(),
                            FontSize = 10
                        }
                    };
                    totalLbl.Measure(new Size(200, 200));
                    Canvas.SetLeft(totalLbl,
                        x + barW / 2 - totalLbl.DesiredSize.Width / 2);
                    Canvas.SetTop(totalLbl, stackBottom - totalLbl.DesiredSize.Height - 1);
                    canvas.Children.Add(totalLbl);
                }

                AddCanvasText(canvas, i == 10 ? "10+" : i.ToString(),
                    x + barW / 2, padT + chartH + 4,
                    10, FontWeights.Normal, HorizontalAlignment.Center);
            }

            // Legend
            ManaProductLegendPanel.Children.Add(new TextBlock
            {
                Text = "LEGEND",
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            });
            foreach (var (name, _, fill) in ManaColorDefs)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                row.Children.Add(new Rectangle
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(fill),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                ManaProductLegendPanel.Children.Add(row);
            }
        }

        private static int CountPips(string manaCost, char symbol)
        {
            int count = 0;
            bool inBrace = false;
            foreach (char c in manaCost)
            {
                if (c == '{') { inBrace = true; continue; }
                if (c == '}') { inBrace = false; continue; }
                if (inBrace && c == symbol) count++;
            }
            return count;
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 7 — POWER ANALYSER
        // ════════════════════════════════════════════════════════════════════
        private void DrawPowerAnalyserTab()
        {
            var canvas = PowerAnalyserCanvas;
            canvas.Children.Clear();
            PowerLegendPanel.Children.Clear();

            var creatures = GetMainCards()
                .Where(c => c.IsCreature && !c.IsLand)
                .ToList();

            // powerBuckets[cmc] = total power, toughnessBuckets[cmc] = total toughness
            var powerBuckets = new double[11];
            var toughnessBuckets = new double[11];

            foreach (var card in creatures)
            {
                int cmc = Math.Min(10, (int)card.ManaValue);
                if (double.TryParse(card.Power, out double p)) powerBuckets[cmc] += p * card.TotalQuantity;
                if (double.TryParse(card.Toughness, out double t)) toughnessBuckets[cmc] += t * card.TotalQuantity;
            }

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (w < 10 || h < 10) return;

            double padL = 36, padR = 16, padT = 28, padB = 36;
            double chartW = w - padL - padR;
            double chartH = h - padT - padB;

            double maxVal = Math.Max(1,
                Math.Max(powerBuckets.Max(), toughnessBuckets.Max()));

            AddCanvasText(canvas, "Power Analyser", padL + chartW / 2,
                10, 12, FontWeights.SemiBold, HorizontalAlignment.Center);

            // Axes
            canvas.Children.Add(new Line
            {
                X1 = padL,
                Y1 = padT,
                X2 = padL,
                Y2 = padT + chartH,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            });
            canvas.Children.Add(new Line
            {
                X1 = padL,
                Y1 = padT + chartH,
                X2 = padL + chartW,
                Y2 = padT + chartH,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            });

            // Grid lines
            for (int g = 1; g <= 4; g++)
            {
                double yg = padT + chartH - chartH * g / 4.0;
                canvas.Children.Add(new Line
                {
                    X1 = padL,
                    Y1 = yg,
                    X2 = padL + chartW,
                    Y2 = yg,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                    StrokeThickness = 0.5
                });
            }

            double barGroupW = chartW / 11;
            double halfW = barGroupW * 0.28;
            double gap = 2;

            var powerColor = Color.FromRgb(0xCC, 0x33, 0x33);
            var toughnessColor = Color.FromRgb(0x33, 0xAA, 0xCC);

            for (int i = 0; i < 11; i++)
            {
                double centerX = padL + i * barGroupW + barGroupW / 2;
                double pX = centerX - halfW - gap / 2;
                double tX = centerX + gap / 2;

                // Power bar
                double pH = chartH * powerBuckets[i] / maxVal;
                double pY = padT + chartH - pH;
                DrawBar(canvas, pX, pY, halfW, pH, powerColor, ((int)powerBuckets[i]).ToString());

                // Toughness bar
                double tH = chartH * toughnessBuckets[i] / maxVal;
                double tY = padT + chartH - tH;
                DrawBar(canvas, tX, tY, halfW, tH, toughnessColor, ((int)toughnessBuckets[i]).ToString());

                // X label
                AddCanvasText(canvas, i == 10 ? "10+" : i.ToString(),
                    centerX, padT + chartH + 4,
                    10, FontWeights.Normal, HorizontalAlignment.Center);
            }

            // Legend
            PowerLegendPanel.Children.Add(new TextBlock
            {
                Text = "LEGEND",
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            });
            foreach (var (name, color) in new[]
            {
                ("Power",     powerColor),
                ("Toughness", toughnessColor)
            })
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                row.Children.Add(new Rectangle
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(color),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                PowerLegendPanel.Children.Add(row);
            }
        }

        private static void DrawBar(Canvas canvas, double x, double y,
            double w, double h, Color color, string label)
        {
            if (h < 0.5)
            {
                // Zero label
                var zero = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 220)),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(2, 0, 2, 0),
                    Child = new TextBlock { Text = "0", FontSize = 9 }
                };
                zero.Measure(new Size(200, 200));
                Canvas.SetLeft(zero, x + w / 2 - zero.DesiredSize.Width / 2);
                Canvas.SetTop(zero, y - zero.DesiredSize.Height);
                canvas.Children.Add(zero);
                return;
            }

            var rect = new Rectangle
            {
                Width = w,
                Height = Math.Max(1, h),
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(DarkenColor(color, 0.7)),
                StrokeThickness = 0.5
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            canvas.Children.Add(rect);

            var lbl = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 220)),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(2, 0, 2, 0),
                Child = new TextBlock { Text = label, FontSize = 9 }
            };
            lbl.Measure(new Size(200, 200));
            Canvas.SetLeft(lbl, x + w / 2 - lbl.DesiredSize.Width / 2);
            Canvas.SetTop(lbl, y - lbl.DesiredSize.Height - 1);
            canvas.Children.Add(lbl);
        }

        // ════════════════════════════════════════════════════════════════════
        // TAB 8 — DECK LEGALITY
        // ════════════════════════════════════════════════════════════════════
        private void PopulateLegalityTab()
        {
            var formats = new[]
            {
                ("Standard",  "standard"),
                ("Pioneer",   "pioneer"),
                ("Modern",    "modern"),
                ("Legacy",    "legacy"),
                ("Vintage",   "vintage"),
                ("Commander", "commander"),
                ("Pauper",    "pauper"),
            };

            var rows = new List<LegalityRow>();
            var allCards = _deck.Cards
                .Where(c => c.Category != DeckCardCategory.Sideboard)
                .ToList();

            foreach (var (display, key) in formats)
            {
                var issues = new List<string>();

                // Check each card's legality
                foreach (var card in allCards)
                {
                    string status = GetCardLegality(card, key);
                    if (status == "not_legal")
                        issues.Add($"{card.Name} not legal");
                    else if (status == "banned")
                        issues.Add($"{card.Name} banned");
                    else if (status == "restricted" && card.TotalQuantity > 1)
                        issues.Add($"{card.Name} restricted (max 1)");
                }

                // Format-specific deck size checks
                if (key == "commander")
                {
                    int total = allCards.Sum(c => c.TotalQuantity) +
                                _deck.CommanderCards.Sum(c => c.TotalQuantity);
                    if (total != 100)
                        issues.Add($"Commander deck must be exactly 100 cards ({total} found)");
                }
                // No minimum card count enforced for non-Commander formats

                bool isLegal = issues.Count == 0;
                rows.Add(new LegalityRow
                {
                    Format = display,
                    Status = isLegal ? "✅ Legal" : "❌ Not Legal",
                    Issues = issues.Count == 0 ? "—"
                              : issues.Count == 1 ? issues[0]
                              : $"{issues[0]} (+{issues.Count - 1} more)",
                    IsLegal = isLegal
                });
            }

            LegalityGrid.ItemsSource = rows;
        }

        private static string GetCardLegality(DeckCard card, string format)
        {
            // DeckCard doesn't store LegalitiesJson — return unknown
            // In a future pass this can be joined from PoolCard
            return "unknown";
        }

        // ════════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════════
        private List<DeckCard> GetMainCards() =>
            _deck.Cards
                .Where(c => c.Category != DeckCardCategory.Sideboard)
                .ToList();

        private static void AddCanvasText(Canvas canvas, string text,
            double cx, double cy, double fontSize, FontWeight weight,
            HorizontalAlignment align)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = Brushes.Black
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double x = align == HorizontalAlignment.Center
                ? cx - tb.DesiredSize.Width / 2
                : align == HorizontalAlignment.Right
                    ? cx - tb.DesiredSize.Width
                    : cx;
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, cy);
            canvas.Children.Add(tb);
        }
    }
}