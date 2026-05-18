using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace BreakersOfE.Windows
{
    // ── Help topic data model ─────────────────────────────────────────────────
    public class HelpTopic
    {
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public List<HelpSection> Sections { get; init; } = new();
        public List<HelpTopic> Children { get; init; } = new();
    }

    public class HelpSection
    {
        public string? Heading { get; init; }
        public List<HelpBlock> Blocks { get; init; } = new();
    }

    public record HelpBlock(HelpBlockType Type, string Text,
        string? Extra = null);

    public enum HelpBlockType
    {
        Paragraph, BulletItem, KeyValue, Tip, Warning, Shortcut, Code
    }

    public partial class HelpWindow : Window
    {
        private List<HelpTopic> _allTopics = new();
        private HelpTopic? _selected = null;

        public HelpWindow(string? startTopic = null)
        {
            InitializeComponent();
            _allTopics = BuildTopics();
            PopulateTree(_allTopics, null);

            // Select first topic or requested one
            SelectTopicByTitle(startTopic ?? "Getting Started");
        }

        // ════════════════════════════════════════════════════════════════════
        // TOPIC TREE
        // ════════════════════════════════════════════════════════════════════

        private void PopulateTree(List<HelpTopic> topics,
            TreeViewItem? parent)
        {
            foreach (var topic in topics)
            {
                var item = new TreeViewItem
                {
                    Header = topic.Title,
                    Tag = topic,
                    Padding = new Thickness(2)
                };
                item.Selected += TopicItem_Selected;

                if (topic.Children.Count > 0)
                    PopulateTree(topic.Children, item);

                if (parent == null) TopicTree.Items.Add(item);
                else parent.Items.Add(item);
            }
        }

        private void TopicItem_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.Tag is HelpTopic topic)
            {
                _selected = topic;
                RenderTopic(topic);
                e.Handled = true;
            }
        }

        private void TopicTree_SelectionChanged(object sender,
            RoutedPropertyChangedEventArgs<object> e)
        { }

        private void TxtSearch_Changed(object sender, TextChangedEventArgs e)
        {
            string q = TxtSearch.Text.Trim().ToLower();
            FilterTree(TopicTree.Items, q);
        }

        private bool FilterTree(ItemCollection items, string q)
        {
            bool anyVisible = false;
            foreach (TreeViewItem item in items)
            {
                bool titleMatch = string.IsNullOrEmpty(q) ||
                    item.Header.ToString()!.ToLower().Contains(q);
                bool childMatch = item.Items.Count > 0 &&
                    FilterTree(item.Items, q);
                bool visible = titleMatch || childMatch;
                item.Visibility = visible
                    ? Visibility.Visible : Visibility.Collapsed;
                if (visible && !string.IsNullOrEmpty(q))
                    item.IsExpanded = childMatch;
                anyVisible |= visible;
            }
            return anyVisible;
        }

        private void SelectTopicByTitle(string title)
        {
            SelectInItems(TopicTree.Items, title);
        }

        private bool SelectInItems(ItemCollection items, string title)
        {
            foreach (TreeViewItem item in items)
            {
                if (item.Header.ToString() == title)
                {
                    item.IsSelected = true;
                    item.BringIntoView();
                    return true;
                }
                if (SelectInItems(item.Items, title)) return true;
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        // CONTENT RENDERING
        // ════════════════════════════════════════════════════════════════════

        private void RenderTopic(HelpTopic topic)
        {
            TopicTitle.Text = topic.Title;
            TopicSubtitle.Text = topic.Subtitle;
            ContentPanel.Children.Clear();

            foreach (var section in topic.Sections)
            {
                if (!string.IsNullOrEmpty(section.Heading))
                    ContentPanel.Children.Add(MakeHeading(section.Heading));

                foreach (var block in section.Blocks)
                    ContentPanel.Children.Add(MakeBlock(block));
            }
        }

        private static UIElement MakeHeading(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.HighlightBrush,
                Margin = new Thickness(0, 14, 0, 6)
            };
        }

        private static UIElement MakeBlock(HelpBlock block)
        {
            switch (block.Type)
            {
                case HelpBlockType.Paragraph:
                    return new TextBlock
                    {
                        Text = block.Text,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 8),
                        LineHeight = 20
                    };

                case HelpBlockType.BulletItem:
                    var bullet = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(8, 2, 0, 2)
                    };
                    bullet.Children.Add(new TextBlock
                    {
                        Text = "•",
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 8, 0),
                        Foreground = SystemColors.HighlightBrush
                    });
                    bullet.Children.Add(new TextBlock
                    {
                        Text = block.Text,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 580
                    });
                    return bullet;

                case HelpBlockType.KeyValue:
                    var kv = new Grid { Margin = new Thickness(8, 2, 0, 2) };
                    kv.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(160) });
                    kv.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(1, GridUnitType.Star) });
                    var key = new TextBlock
                    {
                        Text = block.Extra ?? "",
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold
                    };
                    var val = new TextBlock
                    {
                        Text = block.Text,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    };
                    Grid.SetColumn(key, 0);
                    Grid.SetColumn(val, 1);
                    kv.Children.Add(key);
                    kv.Children.Add(val);
                    return kv;

                case HelpBlockType.Shortcut:
                    var sc = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(8, 3, 0, 3)
                    };
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromRgb(0x33, 0x33, 0x33)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    badge.Child = new TextBlock
                    {
                        Text = block.Extra ?? "",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11,
                        Foreground = Brushes.White
                    };
                    sc.Children.Add(badge);
                    sc.Children.Add(new TextBlock
                    {
                        Text = block.Text,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    return sc;

                case HelpBlockType.Tip:
                    return new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromArgb(30, 0, 120, 80)),
                        BorderBrush = new SolidColorBrush(
                            Color.FromRgb(0, 120, 80)),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 4, 0, 8),
                        Child = new TextBlock
                        {
                            Text = "💡  " + block.Text,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap
                        }
                    };

                case HelpBlockType.Warning:
                    return new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromArgb(30, 200, 80, 0)),
                        BorderBrush = new SolidColorBrush(
                            Color.FromRgb(200, 80, 0)),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 4, 0, 8),
                        Child = new TextBlock
                        {
                            Text = "⚠  " + block.Text,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap
                        }
                    };

                case HelpBlockType.Code:
                    return new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromRgb(0xF0, 0xF0, 0xF0)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 2, 0, 8),
                        Child = new TextBlock
                        {
                            Text = block.Text,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11
                        }
                    };

                default:
                    return new TextBlock { Text = block.Text };
            }
        }

        // Helpers
        static HelpBlock P(string t) => new(HelpBlockType.Paragraph, t);
        static HelpBlock B(string t) => new(HelpBlockType.BulletItem, t);
        static HelpBlock KV(string k, string v) => new(HelpBlockType.KeyValue, v, k);
        static HelpBlock Tip(string t) => new(HelpBlockType.Tip, t);
        static HelpBlock Warn(string t) => new(HelpBlockType.Warning, t);
        static HelpBlock SK(string key, string desc)
            => new(HelpBlockType.Shortcut, desc, key);

        static HelpSection S(string? heading, params HelpBlock[] blocks)
            => new() { Heading = heading, Blocks = blocks.ToList() };

        // ════════════════════════════════════════════════════════════════════
        // HELP CONTENT
        // ════════════════════════════════════════════════════════════════════

        private static List<HelpTopic> BuildTopics() => new()
        {
            // ── GETTING STARTED ───────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Getting Started",
                Subtitle = "First steps with Breakers of E",
                Sections = new()
                {
                    S(null,
                        P("Welcome to Breakers of E — a Magic: The Gathering collection manager, deck builder, and tabletop simulator."),
                        P("On first launch your card pool will be empty. Follow the steps below to get up and running.")),
                    S("Step 1 — Download the Card Database",
                        P("Go to Tools → Update Card Database. Click Download to fetch all cards from Scryfall (~80,000 cards). This only needs to be done once and takes a few minutes depending on your connection."),
                        Tip("After the download completes, run Tools → Download Missing Card Images to download card artwork. You can also download images for just the cards in your collection.")),
                    S("Step 2 — Add Cards to Your Collection",
                        P("Use the View Mode dropdown to switch to Pool → Collection. Select a card in the top table and double-click it (or use the + button) to add it to your collection. Hold Shift to add a foil copy."),
                        P("Alternatively, import an existing collection from MTG Studio, Moxfield, TCGPlayer, Deckbox, or Dragon Shield via File → Import Collection / Deck.")),
                    S("Step 3 — Build a Deck",
                        P("Switch to Pool → Deck or Collection → Deck mode. Create a new deck with File → New Deck (or Ctrl+N). Select cards in the top table and double-click to add them to your deck."),
                        P("The deck tabs at the bottom show all open decks. Click any tab to switch between them.")),
                    S("Step 4 — Play a Game",
                        P("Open the Tabletop Simulator from File → Tabletop Simulator. Load your deck in the New Game dialog and start playing. The simulator supports Commander, Standard, and other formats."))
                }
            },

            // ── MAIN WINDOW ───────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Main Window",
                Subtitle = "Understanding the main interface",
                Sections = new()
                {
                    S(null,
                        P("The main window is divided into four areas: the menu bar at the top, the toolbar below it, the main content area, and the deck tabs at the bottom.")),
                    S("View Modes",
                        P("The View Mode dropdown controls what appears in the two card tables. The top table is always the source; the bottom table is the destination."),
                        KV("Pool → Collection",   "Browse all cards and manage your collection"),
                        KV("Pool → Deck",          "Build a deck from the full card pool"),
                        KV("Collection → Deck",    "Build a deck using only cards you own"),
                        KV("Deck → Collection",    "Move cards from a deck into your collection"),
                        KV("Pool → Want List",     "Mark cards from the pool as wanted"),
                        KV("Collection → Trade Binder", "Mark owned cards as available to trade"),
                        KV("Pool → Tokens",        "Browse and collect token cards"),
                        KV("Pool → Planechase",    "Browse Plane cards"),
                        KV("Pool → Archenemy",     "Browse Scheme cards"),
                        KV("Pool → Vanguard",      "Browse Vanguard cards"),
                        KV("Pool → Conspiracy",    "Browse Conspiracy cards"),
                        KV("Pool → Art Series",    "Browse Art Series cards")),
                    S("Left Panel — Card Details",
                        P("Selecting any card in either table shows its details in the left panel: name, type, mana cost, oracle text, flavor text, P/T, legality, set symbol, prices, and card image."),
                        P("For double-faced cards (DFCs), a 🔄 Show Back Face button appears below the card image. Click it to see the card's back face.")),
                    S("Deck Tabs",
                        P("Open decks appear as tabs at the bottom of the window. Click a tab to make that deck active. The active deck is the target when adding cards. Right-click a tab for deck options."))
                }
            },

            // ── TOOLBAR ───────────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Toolbar Reference",
                Subtitle = "Every toolbar button explained",
                Sections = new()
                {
                    S("Group 1 — Deck File",
                        KV("New Deck",         "Create a new empty deck"),
                        KV("Open Deck",        "Open an existing deck file"),
                        KV("Save Deck",        "Save the active deck"),
                        KV("Save All Decks",   "Save all open decks"),
                        KV("Close Deck",       "Close the active deck"),
                        KV("Close All Decks",  "Close all open decks"),
                        KV("Deck Properties",  "Edit deck name, type, description, author"),
                        KV("Deck Legality",    "Check which formats the deck is legal in"),
                        KV("Deck Statistics",  "Open the full deck analysis window (11 tabs)")),
                    S("Group 2 — Deck Cards",
                        KV("Add 1 to Deck",       "Add one copy of the selected card to the active deck"),
                        KV("Add 4 to Deck",        "Add four copies (Standard playset)"),
                        KV("Add 1 Foil to Deck",   "Add one foil copy"),
                        KV("Remove from Deck",     "Remove one copy from the deck"),
                        KV("+ / − Qty",            "Increase or decrease quantity in the deck")),
                    S("Group 3 — Collection",
                        KV("Add to Collection",      "Add selected card to your collection"),
                        KV("Add Foil to Collection", "Add a foil copy to your collection"),
                        KV("Import Deck to Collection", "Add all cards in the active deck to your collection"),
                        KV("Remove from Collection", "Remove one row from your collection")),
                    S("Group 4 — Filter & Search",
                        KV("Filter",              "Open the quick filter panel"),
                        KV("Advanced Filter",     "Open the expression-builder filter window"),
                        KV("Search",              "Search for a card by name (incremental)"),
                        KV("Find Next / Prev",    "Jump to the next/previous search match"),
                        KV("Clear Search",        "Clear the current search"),
                        KV("Toggle Legality",     "Show/hide legality columns in the table"),
                        KV("Column Chooser",      "Choose which columns are visible"),
                        KV("Keyword Search",      "Open the Keyword Search window")),
                    S("Group 5 — View",
                        KV("Update Database", "Download the latest card data from Scryfall"),
                        KV("Toggle Theme",    "Switch between light and dark mode"))
                }
            },

            // ── KEYBOARD SHORTCUTS ────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Keyboard Shortcuts",
                Subtitle = "Work faster with the keyboard",
                Sections = new()
                {
                    S("Main Window",
                        SK("Enter",          "Add selected top-table card to deck or collection"),
                        SK("Shift + Enter",  "Add as foil"),
                        SK("Delete",         "Remove selected bottom-table card from deck or collection"),
                        SK("Ctrl + N",       "New Deck"),
                        SK("Ctrl + O",       "Open Deck"),
                        SK("Ctrl + S",       "Save active deck"),
                        SK("Ctrl + F",       "Focus the search box"),
                        SK("F3",             "Find next search match"),
                        SK("Shift + F3",     "Find previous search match"),
                        SK("Escape",         "Clear search"),
                        SK("F1",             "Open this Help window")),
                    S("Filter Window",
                        SK("Enter",    "Apply filter and close"),
                        SK("Escape",   "Cancel filter")),
                    S("Tabletop Simulator",
                        SK("← / →",         "Flip to previous/next page in the binder"),
                        SK("Page Up/Down",   "Navigate zone browser pages"),
                        SK("Home",           "Go to first page"),
                        SK("Escape",         "Close any open card viewer"))
                }
            },

            // ── COLLECTION ────────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Collection",
                Subtitle = "Managing your card collection",
                Children = new()
                {
                    new HelpTopic
                    {
                        Title    = "Adding Cards",
                        Subtitle = "How to add cards to your collection",
                        Sections = new()
                        {
                            S(null,
                                P("Switch to Pool → Collection view mode. The top table shows all cards in the pool; the bottom table shows your collection."),
                                P("To add a card: select it in the top table and either double-click it, press Enter, or click the + Add to Collection button in the toolbar."),
                                P("To add a foil copy: hold Shift while double-clicking, or click the ✨ Add Foil button.")),
                            S("Column-by-Column Entry",
                                P("After adding a card you can edit its details directly in the bottom table: Quantity, Foil Qty, Condition, Language, Storage Location, Buy At price, Sell At price, Notes, and Group.")),
                            S("Import from Other Apps",
                                P("If you're coming from another collection manager, use File → Import Collection / Deck. Supported platforms:"),
                                B("MTG Studio — imports your full collection including prices and storage info"),
                                B("Moxfield — imports quantities, conditions, foil status"),
                                B("TCGPlayer — imports quantities and conditions"),
                                B("Deckbox — imports quantities, foil, language"),
                                B("Dragon Shield — imports quantities, finish (foil/normal), conditions"),
                                Tip("MTG Studio imports use ScryfallId for exact card matching — the most reliable import method."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Conditions & Language",
                        Subtitle = "Tracking card condition and language",
                        Sections = new()
                        {
                            S(null,
                                P("Each collection entry tracks Condition and Language separately from quantity. These are set per-row in the collection table."),
                                KV("Near Mint (NM)",   "Card is in perfect or near-perfect condition"),
                                KV("Lightly Played (LP)", "Minor wear, fully playable"),
                                KV("Moderately Played (MP)", "Noticeable wear but still tournament-legal"),
                                KV("Heavily Played (HP)", "Significant wear"),
                                KV("Damaged (D)",      "Severely worn, marked, or bent"),
                                KV("Unknown",          "Condition not assessed")),
                            S("Effect on Value",
                                P("Condition affects the computed market value. Near Mint cards are valued at 100% of market price; Damaged cards are valued at roughly 25%."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Pricing",
                        Subtitle = "Working with card prices",
                        Sections = new()
                        {
                            S(null,
                                P("Prices come from Scryfall and are stored when you run Update Card Database or Update Prices Only."),
                                KV("Market Value",  "Scryfall USD price for non-foil"),
                                KV("Foil Price",    "Scryfall USD foil price"),
                                KV("Buy At",        "Your personal buy price (what you'd pay)"),
                                KV("Sell At",       "Your personal sell price (what you'd charge)"),
                                KV("Price Low",     "Lowest market price across printings")),
                            S("Updating Prices",
                                P("Go to Tools → Update Prices Only to refresh prices without re-downloading all card data. This is faster and can be done frequently."),
                                Tip("Price updates only update the market prices — your personal Buy At and Sell At values are never overwritten."))
                        }
                    }
                }
            },

            // ── DECK BUILDER ─────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Deck Builder",
                Subtitle = "Creating and managing decks",
                Children = new()
                {
                    new HelpTopic
                    {
                        Title    = "Creating a Deck",
                        Subtitle = "Start a new deck from scratch",
                        Sections = new()
                        {
                            S(null,
                                P("Go to File → New Deck (or Ctrl+N). In the New Deck dialog, enter a name, choose a deck type (Standard or Commander), and set the author."),
                                P("Switch to Pool → Deck or Collection → Deck view mode. The top table is your card pool; the bottom is your deck."),
                                P("Double-click any card in the top table to add it. The deck tab at the bottom updates in real time.")),
                            S("Deck Types",
                                KV("Standard",   "60-card minimum, up to 4 copies of any card"),
                                KV("Commander",  "100-card singleton with a commander. Starting life is 40.")),
                            S("Adding Cards",
                                B("Double-click a pool card to add 1 copy"),
                                B("Hold Shift while double-clicking to add a foil copy"),
                                B("Click Add 4 to Deck to add a full playset (Standard)"),
                                B("Press Enter with a card selected to add it"),
                                Tip("In Collection → Deck mode, only cards you own are available. If you have foil copies but no regular, the app automatically marks the card as foil."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Deck Statistics",
                        Subtitle = "Analyzing your deck",
                        Sections = new()
                        {
                            S(null,
                                P("Click the Deck Statistics button (chart icon) or go to the Deck Statistics window. This gives you a full analysis of your deck across 11 tabs:"),
                                KV("Overview",      "Total cards, lands, creatures, spells, avg CMC, color breakdown"),
                                KV("Mana Curve",    "Bar chart of cards by converted mana cost"),
                                KV("Color Pie",     "Visual breakdown of color distribution"),
                                KV("Card Types",    "Creatures, instants, sorceries, enchantments, etc."),
                                KV("Lands",         "Land count, basic vs non-basic, mana production"),
                                KV("Creatures",     "P/T distribution, keyword breakdown"),
                                KV("Rarity",        "Common/Uncommon/Rare/Mythic breakdown"),
                                KV("Value",         "Estimated collection value of the deck"),
                                KV("Legality",      "Format legality check for all banned/restricted lists"),
                                KV("Suggestions",   "Mana curve suggestions and deck improvement tips"),
                                KV("Card Usage",    "Which collection cards are used across all decks"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Importing Decks",
                        Subtitle = "Import decks from other apps",
                        Sections = new()
                        {
                            S(null,
                                P("Go to File → Import Collection / Deck. Switch to the Import tab and select 'Deck' as the import target."),
                                P("Currently supported deck import format:"),
                                KV("MTG Studio (.deck)", "Full XML deck format including sideboard"),
                                P("Cards are matched against your pool by set code and name. Cards not found in your pool are logged as warnings."),
                                Tip("After importing a deck, reload the app or use File → Open Deck to open the saved deck file."))
                        }
                    }
                }
            },

            // ── FILTERING ─────────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Filtering Cards",
                Subtitle = "Finding the cards you need",
                Children = new()
                {
                    new HelpTopic
                    {
                        Title    = "Quick Search",
                        Subtitle = "Fast name-based search",
                        Sections = new()
                        {
                            S(null,
                                P("The search box in the toolbar (or Ctrl+F) filters the current table by card name as you type. It's incremental — results update with every keystroke."),
                                SK("F3",         "Jump to next match"),
                                SK("Shift + F3", "Jump to previous match"),
                                SK("Escape",     "Clear the search and return to full list")),
                            S(null,
                                Tip("The search also matches set codes. Type 'MH3' to see all Modern Horizons 3 cards."))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Advanced Filter",
                        Subtitle = "The expression-builder filter",
                        Sections = new()
                        {
                            S(null,
                                P("Click the Advanced Filter button or press the filter funnel icon to open the full filter window. This has three tabs:"),
                                KV("Blocks/Formats/Editions", "Filter by set or block. Check any editions to restrict cards to those sets."),
                                KV("Cards",    "Build complex filter conditions using the expression builder. Chain conditions with AND/OR logic."),
                                KV("Options",  "Search options (case sensitivity, partial match), color identity filter, price filter.")),
                            S("Expression Builder",
                                P("Click '+ press the button to add a new condition' to add a filter row. Each row has:"),
                                B("Field — what to filter on (Name, Type, CMC, Color, Rarity, etc.)"),
                                B("Operator — how to compare (Contains, Equals, Greater Than, Is any of, etc.)"),
                                B("Value — what to compare against"),
                                P("Multiple conditions are joined with AND by default. Click AND to toggle to OR."),
                                Tip("To find all Flying blue creatures, add: Type Contains 'Creature', then Color Contains 'U', then Oracle Text Contains 'flying'.")),
                            S("Color Identity Filter (Options Tab)",
                                P("The Options tab has a quick color identity filter with W/U/B/R/G/C toggle buttons and three modes:"),
                                KV("At Most (Commander)", "Show cards whose color identity fits within the selected colors — ideal for Commander deck building"),
                                KV("Includes",            "Show cards that contain all selected colors"),
                                KV("Exactly",             "Show only cards that are exactly the selected colors"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Keyword Search",
                        Subtitle = "Search by card abilities",
                        Sections = new()
                        {
                            S(null,
                                P("Open Keyword Search from the toolbar (🔍) or File → Keyword Search. This lets you find cards by their rules text keywords."),
                                P("Keywords are grouped by category in the left panel: Evasion, Combat, Protection, Triggered, Activated, Static, Spell, Cost/Payment, Replacement, Format-Specific, and Other."),
                                Tip("Only keywords present in your card pool appear as active checkboxes. Keywords not in your pool are shown grayed out.")),
                            S("Combining Filters",
                                B("Check multiple keywords and use AND to find cards with all of them"),
                                B("Switch to OR to find cards with any of the selected keywords"),
                                B("Add color identity and legality filters to narrow results further")),
                            S("Action Buttons",
                                KV("Add to Deck",        "Add the selected card to your active deck"),
                                KV("Add to Collection",  "Add the selected card to your collection"),
                                KV("Add to Want List",   "Add the selected card to your want list"),
                                KV("📖 Keyword Dictionary", "Open the full keyword glossary"))
                        }
                    }
                }
            },

            // ── TABLETOP SIMULATOR ────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Tabletop Simulator",
                Subtitle = "Playing Magic: The Gathering",
                Children = new()
                {
                    new HelpTopic
                    {
                        Title    = "Starting a Game",
                        Subtitle = "Setting up a new game",
                        Sections = new()
                        {
                            S(null,
                                P("Open the Tabletop Simulator from File → Tabletop Simulator (or 🂠 Tabletop). The New Game Setup dialog appears."),
                                P("In the dialog, select decks for both players (or leave the opponent's deck empty for goldfish testing). Starting life is auto-detected based on deck type — 40 for Commander, 20 for Standard."),
                                P("After setup, the game begins with an automatic Mulligan phase. The London Mulligan rule is used — commanders get a free first mulligan.")),
                            S("Board Layout",
                                KV("Top half",    "Opponent's zones: library, hand, battlefield, exile, graveyard"),
                                KV("Center rail", "Phase indicator, Stack toggle, modern mechanics, Pass Turn"),
                                KV("Bottom half",  "Your zones: library, hand, battlefield (lands + non-lands), exile, graveyard"),
                                KV("Left panel",   "Life totals, poison counters, commander damage, energy, mana pool"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Game Actions",
                        Subtitle = "What you can do during a game",
                        Sections = new()
                        {
                            S("Cards in Hand",
                                B("Right-click a hand card → Play to Battlefield, Play as Land, Play Face-Down, Reveal to Opponent, Discard, Return to Library Top"),
                                B("Double-click a hand card to view it enlarged")),
                            S("Battlefield Cards",
                                B("Click a card to tap/untap it"),
                                B("Right-click → Move to → send to any zone (hand, graveyard, exile, library top/bottom)"),
                                B("Right-click → Add Counter → +1/+1, -1/-1, Loyalty, Charge, or custom"),
                                B("Right-click → Add Temp Effect → 'Flying until end of turn', custom P/T boost"),
                                B("Right-click → Transform (for DFC cards)"),
                                B("Right-click → View Card for full card image"),
                                B("Drag cards to reposition them on the battlefield")),
                            S("Drawing Cards",
                                B("Click Draw to draw one card"),
                                B("Right-click the Draw button to draw 2, 3, 4, 5, 7, or a custom number")),
                            S("Life & Counters",
                                B("Click + / − next to life or poison totals to adjust by 1"),
                                B("Double-click the life total to type an exact value"),
                                B("Commander damage is tracked in both directions")),
                            S("Special Mechanics",
                                B("Monarch — click the 👑 indicator to claim or give the Monarch"),
                                B("Initiative — click ⚡ to take or give the Initiative"),
                                B("Energy — click ⚡ to add/remove energy counters for either player"),
                                B("The Ring — click 💍 to advance The Ring tempts you (levels 1-4)"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Library Actions",
                        Subtitle = "Searching and manipulating your library",
                        Sections = new()
                        {
                            S(null,
                                P("Right-click your library stack to access:"),
                                KV("Search Library",   "Search by card name and send to hand, battlefield, or top/bottom of library"),
                                KV("Surveil N",        "Look at the top N cards and send each to graveyard or keep on top"),
                                KV("Look at Top N",    "Look at the top N cards without moving them"),
                                KV("Reveal Top Card",  "Flip the top card face-up for all to see"),
                                KV("Shuffle",          "Shuffle your library (cryptographic shuffle)"),
                                KV("Draw X",           "Draw multiple cards at once"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Tabletop Settings",
                        Subtitle = "Customizing the tabletop experience",
                        Sections = new()
                        {
                            S(null,
                                P("Click the ⚙ Settings button in the tabletop toolbar to open Tabletop Settings."),
                                KV("Default Starting Life",  "Override the auto-detected life total (20/30/40/custom)"),
                                KV("Auto-draw on Pass Turn", "Toggle automatic draw when passing turn"),
                                KV("Mana Pool Auto-Empties", "Show ∅ indicator when leaving a main phase"),
                                KV("Show Game Over Prompt",  "Toggle the game over popup on win conditions"),
                                KV("Table Color",            "Choose from 6 felt colors for the table background"),
                                KV("Blur Opponent Hand",     "Obscure cards in the opponent's hand area"),
                                KV("Card Sleeve Image",      "Upload a custom card sleeve image"),
                                KV("Your Playmat",           "Upload a custom playmat image for your side"),
                                KV("Opponent Playmat",       "Upload a custom playmat image for the opponent"))
                        }
                    },
                    new HelpTopic
                    {
                        Title    = "Save & Restore",
                        Subtitle = "Saving game state",
                        Sections = new()
                        {
                            S(null,
                                P("The tabletop automatically saves game state when you close the window. To restore a game, simply open the Tabletop Simulator — if a saved game exists, a Restore prompt appears."),
                                P("Game state includes: life totals, poison, commander damage, all zones (library order, hand, graveyard, exile), the full battlefield with card positions, tap states, counters, transform states, and all temp effects."),
                                P("Modern mechanics are also saved: Monarch holder, Initiative holder, energy counts, and The Ring level and bearer."),
                                Warn("Game state is tied to the decks used to start the game. If you delete the deck files, card matching may fail on restore."))
                        }
                    }
                }
            },

            // ── TRADE BINDER ─────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Trade Binder",
                Subtitle = "Managing your trade list",
                Sections = new()
                {
                    S(null,
                        P("The Trade Binder helps you track which cards you're willing to trade away (Have List) and which cards you're looking to acquire (Want List)."),
                        P("Open the visual binder from File → Trade Binder. It shows your cards in a 9-pocket page layout — like a real physical binder.")),
                    S("Have List (Trade Binder)",
                        P("Switch to Collection → Trade Binder view mode. Select a card from your collection in the top table and add it to the binder with the + button. These are cards you own and want to trade away."),
                        P("Each entry tracks: Quantity, Foil/Non-Foil, Condition, and Asking Price (defaults to market value).")),
                    S("Want List",
                        P("Switch to Pool → Want List view mode. Select any card from the pool and add it to your want list. These are cards you're looking to acquire."),
                        P("Each entry tracks: Quantity, Foil/Non-Foil, and Offer Price.")),
                    S("Visual Binder",
                        P("The binder window shows cards in a 3×3 pocket grid per page. Use the Have/Want tab at the top to switch between lists. Navigate pages with the ◀ ▶ arrows or keyboard arrow keys."),
                        B("Double-click a card to see it enlarged"),
                        B("Right-click a card to remove it from the binder"),
                        Tip("Cards are sorted alphabetically within the binder."))
                }
            },

            // ── IMPORT / EXPORT ───────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Import & Export",
                Subtitle = "Moving data in and out of Breakers of E",
                Sections = new()
                {
                    S(null,
                        P("Open the Import/Export window from File → Import Collection / Deck or File → Export Collection / Deck.")),
                    S("Importing",
                        P("Select what to import (Collection, Deck, or Want List), choose the format, then browse for your file. The format is usually auto-detected from the file header."),
                        KV("MTG Studio CSV",    "Best import — uses ScryfallId for exact card matching. Preserves prices, storage, notes, foil status."),
                        KV("MTG Studio .deck",  "Imports a deck with sideboard. Cards matched by set code + name."),
                        KV("Moxfield CSV",      "Imports quantities, conditions, foil status, purchase price"),
                        KV("TCGPlayer CSV",     "Imports quantities and conditions (foil extracted from condition string)"),
                        KV("Deckbox CSV",       "Imports quantities, foil, language"),
                        KV("Dragon Shield CSV", "Imports quantities, finish (Foil/Normal/Etched), conditions"),
                        P("Conflict handling: Merge (add quantities to existing entries) or Replace (overwrite quantities).")),
                    S("Exporting",
                        P("Select what to export and the target format. The export file is saved to your Exports folder (My Documents\\Breakers of E\\Exports) by default."),
                        KV("MTG Studio CSV",    "Full column set including ScryfallId — best for backup and re-import"),
                        KV("MTG Studio .deck",  "Deck export in their XML format, importable directly into MTG Studio"),
                        KV("Moxfield CSV",      "Correct format for Moxfield import"),
                        KV("TCGPlayer CSV",     "Format for TCGPlayer buylist or collection sync"),
                        KV("Deckbox CSV",       "Deckbox collection format"),
                        KV("Dragon Shield CSV", "Dragon Shield app format"),
                        KV("Breakers of E JSON", "Native round-trip format — use for full backups")),
                    S(null,
                        Tip("For a full backup, export in Breakers of E JSON format. This preserves every field including your personal Buy At/Sell At prices and notes."))
                }
            },

            // ── KEYWORD DICTIONARY ────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Keyword Dictionary",
                Subtitle = "MTG keyword rules reference",
                Sections = new()
                {
                    S(null,
                        P("Open the Keyword Dictionary from Help → Keyword Dictionary or from within the Keyword Search window (📖 button)."),
                        P("The dictionary contains ~200 MTG keyword abilities organized by category with their full rules definitions. Categories include:"),
                        KV("Evasion",         "Flying, Menace, Shadow, Fear, Skulk, Intimidate, Landwalk, Horsemanship"),
                        KV("Combat",          "First Strike, Double Strike, Trample, Vigilance, Haste, Deathtouch, Lifelink, Reach, Infect, Wither, Battle Cry, Exalted, and more"),
                        KV("Protection",      "Hexproof, Shroud, Indestructible, Ward, Protection from, Phasing"),
                        KV("Triggered",       "Cascade, Landfall, Prowess, Magecraft, Morbid, Revolt, Enrage, Constellation, Mentor, and more"),
                        KV("Activated",       "Equip, Cycling, Morph, Unearth, Crew, Level Up, Monstrosity, and more"),
                        KV("Static",          "Flash, Defender, Convoke, Delve, Storm, Bestow, Overload, Cipher, and more"),
                        KV("Spell",           "Flashback, Jump-start, Retrace, Rebound, Buyback, Kicker, Fuse, and more"),
                        KV("Cost/Payment",    "Casualty, Evoke, Madness, Escape, Foretell, Blitz, Dash, Mutate, Cleave, and more"),
                        KV("Replacement",     "Undying, Persist, Regenerate, Dredge, Embalm, Eternalize, Scavenge"),
                        KV("Format-Specific", "Partner, Eminence, Monarch, The Initiative, Myriad, Background"),
                        KV("Other",           "Transform, Meld, Saga, Class, Decayed, Toxic, The Ring, Saddle, Gift, Offspring")),
                    S(null,
                        P("Use the search box to find keywords by name or definition text. Click any row to see the full definition below the table."))
                }
            },

            // ── PREFERENCES ───────────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Preferences",
                Subtitle = "Configuring Breakers of E",
                Sections = new()
                {
                    S(null,
                        P("Open Preferences from File → Preferences. All settings are saved immediately when you click Save.")),
                    S("Identity",
                        KV("Your Name",  "Used as the author name in new decks")),
                    S("Startup",
                        KV("Default View",    "Which view mode opens when the app starts (e.g. Pool → Collection)"),
                        KV("Theme",           "Light or Dark mode")),
                    S("Cards",
                        KV("Default Condition", "The condition assigned when adding cards to your collection"),
                        KV("Default Language",  "The language assigned when adding cards")),
                    S("Pricing",
                        KV("Currency Display", "Show prices in USD, EUR, or TIX")),
                    S("Tabletop",
                        KV("Default Starting Life", "Override the auto-detected starting life total for new games")),
                    S("File Locations",
                        KV("Card Images Folder", "Where card images are stored (default: My Documents\\Breakers of E\\Images)"),
                        KV("Decks Folder",        "Where deck files are stored (default: My Documents\\Breakers of E\\Decks)"),
                        KV("Open App Data Folder", "Opens the root Breakers of E data folder in Explorer"))
                }
            },

            // ── TROUBLESHOOTING ───────────────────────────────────────────
            new HelpTopic
            {
                Title    = "Troubleshooting",
                Subtitle = "Common issues and solutions",
                Sections = new()
                {
                    S("Card Pool is Empty",
                        P("If you see 'Your card pool is empty', you haven't downloaded the card database yet."),
                        B("Go to Tools → Update Card Database"),
                        B("Click Download and wait for it to complete (~80,000 cards)"),
                        B("This only needs to be done once")),
                    S("Card Images Not Showing",
                        P("Card images are downloaded separately from card data."),
                        B("Go to Tools → Download Missing Card Images"),
                        B("Images are cached locally in My Documents\\Breakers of E\\Images"),
                        B("Only images for cards in your collection are downloaded by default"),
                        Tip("To download all card images at once, use Tools → Download All Card Images (this is a large download).")),
                    S("Import Not Finding Cards",
                        P("If import reports many 'not found' cards:"),
                        B("Make sure you've run Update Card Database first"),
                        B("MTG Studio CSV imports use ScryfallId — this is the most reliable format"),
                        B("Other formats match by Name + Set Code. Set codes must match Scryfall's codes exactly"),
                        B("Promo and special cards may have different set codes in different apps")),
                    S("Deck Won't Save",
                        P("If a deck can't be saved, check:"),
                        B("The deck has been given a name in Deck Properties"),
                        B("The Decks folder is accessible (check Preferences → Decks Folder)"),
                        B("You have write permission to My Documents")),
                    S("App Data Location",
                        P("All Breakers of E data is stored in:"),
                        new HelpBlock(HelpBlockType.Code, "My Documents\\Breakers of E\\"),
                        P("Sub-folders:"),
                        KV("Images\\",   "Card images cached from Scryfall"),
                        KV("Decks\\",    "Your saved deck files (.deck)"),
                        KV("Exports\\",  "Files exported from the app"),
                        KV("Imports\\",  "Import staging area"),
                        KV("Filters\\",  "Saved filter presets"),
                        KV("Tabletop\\", "Sleeve and playmat images"),
                        P("The two databases are:"),
                        KV("breakersofe.db",     "Card pool (all ~80,000 cards from Scryfall)"),
                        KV("Collection\\collection.db", "Your collection, decks used count, trade binder, want list"))
                }
            }
        };
    }
}