using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class NewGameDialog : Window
    {
        // ================================================================
        // RESULT PROPERTIES
        // ================================================================
        public string Player1Name { get; private set; } = "Player 1";
        public string Player2Name { get; private set; } = "Player 2";
        public Deck? Player1Deck { get; private set; }
        public Deck? Player2Deck { get; private set; }
        public int StartingLife { get; private set; } = 20;
        public bool RestoreSave { get; private set; } = false;

        private readonly List<Deck> _openDecks;
        private readonly bool _hasSavedGame;

        // ================================================================
        // CONSTRUCTOR
        // ================================================================
        public NewGameDialog(List<Deck> openDecks, bool hasSavedGame = false)
        {
            InitializeComponent();
            _openDecks = openDecks;
            _hasSavedGame = hasSavedGame;

            PopulateDeckCombos();
            RestorePanel.Visibility = hasSavedGame
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================================================================
        // DECK COMBOS
        // ================================================================
        private void PopulateDeckCombos()
        {
            // Start with blank entry for solo/goldfish
            var items = new List<DeckComboItem>
            {
                new DeckComboItem { Label = "— None (Goldfish) —", Deck = null }
            };

            // Add all currently open decks
            foreach (var deck in _openDecks)
                items.Add(new DeckComboItem
                {
                    Label = $"{deck.Name} ({deck.Cards.Count} cards)",
                    Deck = deck
                });

            // Also allow browsing for a deck file
            items.Add(new DeckComboItem
            {
                Label = "📁 Browse for deck file...",
                Deck = null,
                IsBrowse = true
            });

            Player1DeckCombo.ItemsSource = items;
            Player2DeckCombo.ItemsSource = items;
            Player1DeckCombo.DisplayMemberPath = "Label";
            Player2DeckCombo.DisplayMemberPath = "Label";

            // Pre-select first open deck for player 1 if available
            Player1DeckCombo.SelectedIndex = _openDecks.Count > 0 ? 1 : 0;
            Player2DeckCombo.SelectedIndex = _openDecks.Count > 1 ? 2 : 0;

            UpdateLifeDefault();
        }

        private void DeckCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo) return;

            var item = combo.SelectedItem as DeckComboItem;
            if (item?.IsBrowse == true)
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Select Deck File",
                    Filter = "Deck Files (*.deck)|*.deck|All Files (*.*)|*.*"
                };
                if (dlg.ShowDialog() == true)
                {
                    var deck = DeckService.Load(dlg.FileName);
                    if (deck != null)
                    {
                        // Insert loaded deck into combo
                        var items = (combo.ItemsSource as List<DeckComboItem>)!;
                        var newItem = new DeckComboItem
                        {
                            Label = $"{deck.Name} ({deck.Cards.Count} cards)",
                            Deck = deck
                        };
                        items.Insert(items.Count - 1, newItem);
                        combo.Items.Refresh();
                        combo.SelectedItem = newItem;
                    }
                    else
                        combo.SelectedIndex = 0;
                }
                else
                    combo.SelectedIndex = 0;
            }

            UpdateLifeDefault();
        }

        private void UpdateLifeDefault()
        {
            var d1 = (Player1DeckCombo.SelectedItem as DeckComboItem)?.Deck;
            var d2 = (Player2DeckCombo.SelectedItem as DeckComboItem)?.Deck;

            bool isCommander = d1?.DeckType == DeckType.Commander
                            || d2?.DeckType == DeckType.Commander;

            if (isCommander)
            {
                StartingLifeBox.Text = "40";
                DeckTypeLabel.Text = "(Commander detected — 40 life)";
                DeckTypeLabel.Foreground = System.Windows.Media.Brushes.DarkGreen;
            }
            else
            {
                StartingLifeBox.Text = "20";
                DeckTypeLabel.Text = "(Standard — 20 life)";
                DeckTypeLabel.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        // ================================================================
        // RESTORE / DISCARD SAVE
        // ================================================================
        private void BtnRestoreGame_Click(object sender, RoutedEventArgs e)
        {
            RestoreSave = true;
            DialogResult = true;
        }

        private void BtnDiscardSave_Click(object sender, RoutedEventArgs e)
        {
            RestorePanel.Visibility = Visibility.Collapsed;
        }

        // ================================================================
        // START / CANCEL
        // ================================================================
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            Player1Name = string.IsNullOrWhiteSpace(Player1NameBox.Text)
                         ? "Player 1" : Player1NameBox.Text.Trim();
            Player2Name = string.IsNullOrWhiteSpace(Player2NameBox.Text)
                         ? "Player 2" : Player2NameBox.Text.Trim();
            Player1Deck = (Player1DeckCombo.SelectedItem as DeckComboItem)?.Deck;
            Player2Deck = (Player2DeckCombo.SelectedItem as DeckComboItem)?.Deck;
            StartingLife = int.TryParse(StartingLifeBox.Text, out int life)
                         ? life : 20;

            if (Player1Deck == null && Player2Deck == null)
            {
                MessageBox.Show(
                    "Please select at least one deck to start a game.",
                    "No Deck Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }

    public class DeckComboItem
    {
        public string Label { get; set; } = string.Empty;
        public Deck? Deck { get; set; }
        public bool IsBrowse { get; set; } = false;
    }
}