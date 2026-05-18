using BreakersOfE.Models;
using BreakersOfE.Services;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace BreakersOfE.Windows
{
    public partial class ImportExportWindow : Window
    {
        private readonly Deck? _activeDeck;

        public ImportExportWindow(Deck? activeDeck = null)
        {
            InitializeComponent();
            _activeDeck = activeDeck;

            // Pre-fill export path with exports folder
            TxtExportPath.Text = AppFolderService.ExportsFolder;
        }

        // ── Tab change ────────────────────────────────────────────────────────
        private void ModeTab_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (BtnAction == null) return;
            bool isImport = ModeTab.SelectedIndex == 0;
            BtnAction.Content = isImport ? "⬇  Import" : "⬆  Export";
        }

        // ── Import target changed ─────────────────────────────────────────────
        private void ImportTarget_Changed(object sender, RoutedEventArgs e)
        {
            if (RbFmtMtgStudioDeck == null) return;
            bool isDeck = RbImportDeck.IsChecked == true;
            // Only MTG Studio .deck format for deck import
            RbFmtMtgStudioDeck.IsEnabled = isDeck;
            if (isDeck) RbFmtMtgStudioDeck.IsChecked = true;
            else if (RbFmtMtgStudioDeck.IsChecked == true)
                RbFmtAutoDetect.IsChecked = true;
        }

        // ── File browse — Import ──────────────────────────────────────────────
        private void BtnBrowseImport_Click(object sender, RoutedEventArgs e)
        {
            bool isDeck = RbImportDeck.IsChecked == true;
            var dlg = new OpenFileDialog
            {
                Title = "Select file to import",
                Filter = isDeck
                    ? "MTG Studio Deck (*.deck)|*.deck|All files (*.*)|*.*"
                    : "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                InitialDirectory = AppFolderService.ImportsFolder
            };
            if (dlg.ShowDialog() == true)
                TxtImportPath.Text = dlg.FileName;
        }

        // ── File browse — Export ──────────────────────────────────────────────
        private void BtnBrowseExport_Click(object sender, RoutedEventArgs e)
        {
            bool isDeck = RbExportDeck.IsChecked == true;
            bool isJson = RbExpNative.IsChecked == true;
            bool isMtgDeck = RbExpMtgStudioDeck.IsChecked == true;

            string filter = isDeck || isMtgDeck
                ? "MTG Studio Deck (*.deck)|*.deck|All files (*.*)|*.*"
                : isJson
                    ? "JSON files (*.json)|*.json|All files (*.*)|*.*"
                    : "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

            string defaultName = isDeck || isMtgDeck
                ? $"{_activeDeck?.Name ?? "deck"}.deck"
                : isJson
                    ? "collection.json"
                    : "collection.csv";

            var dlg = new SaveFileDialog
            {
                Title = "Save export file",
                Filter = filter,
                InitialDirectory = AppFolderService.ExportsFolder,
                FileName = defaultName
            };
            if (dlg.ShowDialog() == true)
                TxtExportPath.Text = dlg.FileName;
        }

        // ── Main action button ────────────────────────────────────────────────
        private void BtnAction_Click(object sender, RoutedEventArgs e)
        {
            bool isImport = ModeTab.SelectedIndex == 0;
            if (isImport) DoImport();
            else DoExport();
        }

        private void DoImport()
        {
            string path = TxtImportPath.Text.Trim();
            if (!File.Exists(path))
            {
                Log("❌ No file selected or file not found.");
                return;
            }

            // Detect or select format
            ImportExportFormat fmt;
            if (RbFmtAutoDetect.IsChecked == true)
                fmt = CollectionImportExportService.DetectFormat(path);
            else if (RbFmtMtgStudioCsv.IsChecked == true) fmt = ImportExportFormat.MtgStudioCsv;
            else if (RbFmtMtgStudioDeck.IsChecked == true) fmt = ImportExportFormat.MtgStudioDeck;
            else if (RbFmtMoxfield.IsChecked == true) fmt = ImportExportFormat.Moxfield;
            else if (RbFmtTcgPlayer.IsChecked == true) fmt = ImportExportFormat.TcgPlayer;
            else if (RbFmtDeckbox.IsChecked == true) fmt = ImportExportFormat.Deckbox;
            else if (RbFmtDragonShield.IsChecked == true) fmt = ImportExportFormat.DragonShield;
            else fmt = CollectionImportExportService.DetectFormat(path);

            Log($"📂 Importing: {Path.GetFileName(path)}");
            Log($"   Format detected: {fmt}");

            BtnAction.IsEnabled = false;
            try
            {
                if (RbImportDeck.IsChecked == true)
                {
                    var (result, deck) = CollectionImportExportService
                        .ImportDeck(path, fmt);
                    LogResult(result);
                    if (deck != null)
                    {
                        // Save the imported deck
                        string deckPath = Path.Combine(AppFolderService.DecksFolder,
                            deck.FileName);
                        DeckService.SaveAs(deck, deckPath);
                        Log($"✅ Deck saved as: {deck.FileName}");
                        Log("   Reload the app or open the deck from File → Open Deck.");
                    }
                }
                else
                {
                    bool merge = RbMerge.IsChecked == true;
                    var result = CollectionImportExportService
                        .ImportCollection(path, fmt, merge);
                    LogResult(result);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error: {ex.Message}");
            }
            finally
            {
                BtnAction.IsEnabled = true;
            }
        }

        private void DoExport()
        {
            string path = TxtExportPath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                Log("❌ No output file selected.");
                return;
            }

            // Determine format
            ImportExportFormat fmt;
            if (RbExpMtgStudio.IsChecked == true) fmt = ImportExportFormat.MtgStudioCsv;
            else if (RbExpMtgStudioDeck.IsChecked == true) fmt = ImportExportFormat.MtgStudioDeck;
            else if (RbExpMoxfield.IsChecked == true) fmt = ImportExportFormat.Moxfield;
            else if (RbExpTcgPlayer.IsChecked == true) fmt = ImportExportFormat.TcgPlayer;
            else if (RbExpDeckbox.IsChecked == true) fmt = ImportExportFormat.Deckbox;
            else if (RbExpDragonShield.IsChecked == true) fmt = ImportExportFormat.DragonShield;
            else fmt = ImportExportFormat.BreakersOfE;

            Log($"💾 Exporting to: {Path.GetFileName(path)}");
            Log($"   Format: {fmt}");

            BtnAction.IsEnabled = false;
            try
            {
                if (RbExportDeck.IsChecked == true)
                {
                    if (_activeDeck == null)
                    {
                        Log("❌ No active deck. Open a deck first.");
                        return;
                    }
                    CollectionImportExportService.ExportDeck(path, fmt, _activeDeck);
                    Log($"✅ Deck exported: {_activeDeck.Cards.Count} cards.");
                }
                else
                {
                    // Collection, Want List, or Trade Binder — all use collection export for now
                    // Future: separate export handlers for want list and trade binder
                    CollectionImportExportService.ExportCollection(path, fmt);
                    Log($"✅ Export complete.");
                }

                StatusText.Text = $"Exported to {Path.GetFileName(path)}";

                // Open the folder
                if (MessageBox.Show("Export complete. Open folder?",
                    "Done", MessageBoxButton.YesNo, MessageBoxImage.Information)
                    == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start("explorer.exe",
                        Path.GetDirectoryName(path)!);
            }
            catch (Exception ex)
            {
                Log($"❌ Error: {ex.Message}");
            }
            finally
            {
                BtnAction.IsEnabled = true;
            }
        }

        private void LogResult(Services.CollectionImportResult result)
        {
            if (result.CardsImported > 0)
                Log($"✅ Imported:   {result.CardsImported} card(s)");
            if (result.CardsUpdated > 0)
                Log($"🔄 Updated:    {result.CardsUpdated} existing entry(s)");
            if (result.CardsNotFound > 0)
                Log($"⚠️  Not found:  {result.CardsNotFound} card(s) (not in your pool)");
            if (result.CardsSkipped > 0)
                Log($"⏭  Skipped:    {result.CardsSkipped} card(s)");
            foreach (var w in result.Warnings)
                Log($"   ⚠ {w}");
            foreach (var err in result.Errors)
                Log($"   ❌ {err}");

            StatusText.Text = result.Success
                ? $"Done — {result.CardsImported + result.CardsUpdated} card(s) processed"
                : "Completed with errors";
        }

        private void Log(string message)
        {
            LogBox.Text += (LogBox.Text.Length > 0 ? "\n" : "") + message;
            LogBox.ScrollToEnd();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}