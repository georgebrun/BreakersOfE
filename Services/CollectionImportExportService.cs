using BreakersOfE.Data;
using BreakersOfE.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace BreakersOfE.Services
{
    // ── Format identifier ─────────────────────────────────────────────────────
    public enum ImportExportFormat
    {
        MtgStudioCsv,
        MtgStudioDeck,
        Moxfield,
        TcgPlayer,
        Deckbox,
        DragonShield,
        BreakersOfE   // native JSON round-trip
    }

    // ── Result of an import operation ─────────────────────────────────────────
    public class CollectionImportResult
    {
        public int CardsImported { get; set; }
        public int CardsSkipped { get; set; }
        public int CardsNotFound { get; set; }
        public int CardsUpdated { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public bool Success => Errors.Count == 0;
    }

    // ── A single parsed row from any import file ──────────────────────────────
    public class ImportRow
    {
        public string ScryfallId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SetCode { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public bool IsFoil { get; set; }
        public string Condition { get; set; } = "Near Mint";
        public string Language { get; set; } = "English";
        public string Notes { get; set; } = string.Empty;
        public string StorageLocation { get; set; } = string.Empty;
        public decimal? BuyAt { get; set; }
        public decimal? SellAt { get; set; }
        public string Group { get; set; } = string.Empty;
        public DateTime DateAdded { get; set; } = DateTime.Now;
        // For deck import
        public bool IsSideboard { get; set; }
        public bool IsCommander { get; set; }
    }

    public static class CollectionImportExportService
    {
        // ════════════════════════════════════════════════════════════════════
        // FORMAT DETECTION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Auto-detect format from file extension and header row.</summary>
        public static ImportExportFormat DetectFormat(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();

            if (ext == ".deck") return ImportExportFormat.MtgStudioDeck;
            if (ext == ".json") return ImportExportFormat.BreakersOfE;

            if (ext == ".csv" || ext == ".txt")
            {
                string header = ReadFirstLine(filePath).ToLower();

                // MTG Studio CSV: has "scryfallid" and "cardid"
                if (header.Contains("scryfallid") && header.Contains("cardid"))
                    return ImportExportFormat.MtgStudioCsv;

                // Moxfield: "count,tradelist count,name,edition,condition,language,foil,tags,collector number"
                if (header.Contains("tradelist count"))
                    return ImportExportFormat.Moxfield;

                // TCGPlayer: "quantity,product name,set name,number,rarity,condition,add to quantity"
                if (header.Contains("product name") || header.Contains("tcg"))
                    return ImportExportFormat.TcgPlayer;

                // Deckbox: "count,tradelist count,name,edition,card number,condition,language,foil"
                if (header.Contains("tradelist") && header.Contains("card number"))
                    return ImportExportFormat.Deckbox;

                // Dragon Shield: "quantity,card name,set name,card number,finish,condition"
                if (header.Contains("finish") && header.Contains("card name"))
                    return ImportExportFormat.DragonShield;

                // Fallback — try Moxfield
                return ImportExportFormat.Moxfield;
            }

            return ImportExportFormat.BreakersOfE;
        }

        private static string ReadFirstLine(string path)
        {
            try
            {
                using var sr = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
                return sr.ReadLine() ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        // ════════════════════════════════════════════════════════════════════
        // IMPORT
        // ════════════════════════════════════════════════════════════════════

        public static CollectionImportResult ImportCollection(string filePath,
            ImportExportFormat format, bool mergeWithExisting = true)
        {
            var rows = format switch
            {
                ImportExportFormat.MtgStudioCsv => ParseMtgStudioCsv(filePath),
                ImportExportFormat.Moxfield => ParseMoxfieldCsv(filePath),
                ImportExportFormat.TcgPlayer => ParseTcgPlayerCsv(filePath),
                ImportExportFormat.Deckbox => ParseDeckboxCsv(filePath),
                ImportExportFormat.DragonShield => ParseDragonShieldCsv(filePath),
                _ => ParseMtgStudioCsv(filePath)
            };

            return ApplyCollectionRows(rows, mergeWithExisting);
        }

        public static (CollectionImportResult result, Deck? deck) ImportDeck(
            string filePath, ImportExportFormat format)
        {
            if (format == ImportExportFormat.MtgStudioDeck)
                return ImportMtgStudioDeck(filePath);

            // CSV formats — parse as card list and build a deck
            List<ImportRow> rows = format switch
            {
                ImportExportFormat.MtgStudioCsv => ParseMtgStudioCsv(filePath),
                ImportExportFormat.Moxfield => ParseMoxfieldCsv(filePath),
                ImportExportFormat.TcgPlayer => ParseTcgPlayerCsv(filePath),
                ImportExportFormat.Deckbox => ParseDeckboxCsv(filePath),
                ImportExportFormat.DragonShield => ParseDragonShieldCsv(filePath),
                _ => new List<ImportRow>()
            };

            if (rows.Count == 0)
            {
                var err = new CollectionImportResult();
                err.Errors.Add($"Deck import not supported for format {format}");
                return (err, null);
            }

            return BuildDeckFromRows(rows, Path.GetFileNameWithoutExtension(filePath));
        }

        private static (CollectionImportResult result, Deck? deck) BuildDeckFromRows(
            List<ImportRow> rows, string deckName)
        {
            var result = new CollectionImportResult();
            var deck = DeckService.CreateNew(deckName, DeckType.Standard);

            using var pdb = new AppDbContext();
            var cardLookup = pdb.PoolCards.AsNoTracking()
                .Select(c => new {
                    c.PoolId,
                    c.Name,
                    c.SetCode,
                    c.CollectorNumber,
                    c.TypeLine,
                    c.ManaCost,
                    c.ManaValue,
                    c.ColorIdentity,
                    c.Power,
                    c.Toughness,
                    c.LocalImagePath,
                    c.ImageNormalUrl
                })
                .ToList();

            foreach (var row in rows)
            {
                string matchName = row.Name.Contains(" // ")
                    ? row.Name.Split(new[] { " // " }, StringSplitOptions.None)[0].Trim()
                    : row.Name;

                var match = cardLookup.FirstOrDefault(c =>
                    c.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase) &&
                    c.SetCode.Equals(row.SetCode, StringComparison.OrdinalIgnoreCase))
                    ?? cardLookup.FirstOrDefault(c =>
                        c.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase))
                    ?? cardLookup.FirstOrDefault(c =>
                        (c.Name.Contains(" // ")
                            ? c.Name.Split(new[] { " // " }, StringSplitOptions.None)[0].Trim()
                            : c.Name)
                        .Equals(matchName, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    result.CardsNotFound++;
                    result.Warnings.Add($"Not in pool: {row.Name} [{row.SetCode}]");
                    continue;
                }

                deck.Cards.Add(new DeckCard
                {
                    Name = match.Name,
                    SetCode = match.SetCode,
                    CollectorNumber = match.CollectorNumber,
                    TypeLine = match.TypeLine,
                    ManaCost = match.ManaCost,
                    ManaValue = match.ManaValue,
                    ColorIdentity = match.ColorIdentity,
                    Power = match.Power,
                    Toughness = match.Toughness,
                    LocalImagePath = match.LocalImagePath,
                    ImageNormalUrl = match.ImageNormalUrl,
                    Quantity = row.IsFoil ? 0 : row.Quantity,
                    FoilQuantity = row.IsFoil ? row.Quantity : 0,
                    Category = DeckCardCategory.Mainboard
                });
                result.CardsImported += row.Quantity;
            }

            result.Warnings.Add(
                "CSV deck import does not include commander or sideboard information. " +
                "Please set commanders manually in the deck editor.");

            return (result, deck);
        }

        // ── MTG Studio CSV parser ─────────────────────────────────────────────
        private static List<ImportRow> ParseMtgStudioCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;

            var cols = ParseCsvHeader(header);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                if (fields.Length == 0) continue;

                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Quantity"), out int q) ? q : 1;
                bool isFoil = Get("Foil").Equals("true", StringComparison.OrdinalIgnoreCase);

                // MTG Studio uses "UN" for Unknown condition
                string cond = MapMtgStudioCondition(Get("Condition"));

                // Parse collector number — strip rarity suffix (e.g. "001/281 C" → "001")
                string cn = Get("CollectorNo");
                if (cn.Contains('/')) cn = cn.Split('/')[0].Trim();
                if (cn.Contains(' ')) cn = cn.Split(' ')[0].Trim();

                decimal? buyAt = decimal.TryParse(Get("BuyAt"), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out decimal b) ? b : null;
                decimal? sellAt = decimal.TryParse(Get("SellAt"), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out decimal s) ? s : null;

                DateTime added = DateTime.TryParse(Get("Added"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime dt) ? dt : DateTime.Now;

                rows.Add(new ImportRow
                {
                    ScryfallId = Get("ScryfallId"),
                    Name = Get("Name"),
                    SetCode = Get("SetAbbreviation"),
                    CollectorNumber = cn,
                    Quantity = qty,
                    IsFoil = isFoil,
                    Condition = cond,
                    Notes = Get("Notes"),
                    StorageLocation = Get("Storage"),
                    BuyAt = buyAt,
                    SellAt = sellAt,
                    Group = Get("Group"),
                    DateAdded = added
                });
            }
            return rows;
        }

        private static string MapMtgStudioCondition(string code) => code.ToUpper() switch
        {
            "NM" or "M" => "Near Mint",
            "LP" or "EX" => "Lightly Played",
            "MP" or "GD" => "Moderately Played",
            "HP" or "PL" => "Heavily Played",
            "D" or "PO" => "Damaged",
            _ => "Unknown"
        };

        // ── Moxfield CSV parser ───────────────────────────────────────────────
        // Header: Count,Tradelist Count,Name,Edition,Condition,Language,Foil,Tags,Collector Number,Alter,Proxy,Purchase Price
        private static List<ImportRow> ParseMoxfieldCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);

            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Count"), out int q) ? Math.Max(q, 1) : 1;
                bool foil = Get("Foil").Equals("foil", StringComparison.OrdinalIgnoreCase);

                rows.Add(new ImportRow
                {
                    Name = Get("Name"),
                    SetCode = Get("Edition"),
                    CollectorNumber = Get("Collector Number"),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(Get("Condition")),
                    Language = NormalizeLanguage(Get("Language")),
                    Notes = Get("Tags"),
                    BuyAt = decimal.TryParse(Get("Purchase Price"),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out decimal pp)
                        ? pp : null
                });
            }
            return rows;
        }

        private static string MapMoxfieldCondition(string c) => c.ToUpper() switch
        {
            "M" or "MINT" => "Near Mint",
            "NM" or "NEAR MINT" => "Near Mint",
            "LP" or "LIGHTLY PLAYED" => "Lightly Played",
            "MP" or "MODERATELY PLAYED" => "Moderately Played",
            "HP" or "HEAVILY PLAYED" => "Heavily Played",
            "D" or "DAMAGED" => "Damaged",
            _ => "Unknown"
        };

        // ── TCGPlayer CSV parser ──────────────────────────────────────────────
        // Header: Quantity,Product Name,Set Name,Number,Rarity,Condition,Add to Quantity
        private static List<ImportRow> ParseTcgPlayerCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);

            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Quantity"), out int q) ? Math.Max(q, 1) : 1;
                string condFull = Get("Condition"); // e.g. "Near Mint Foil"
                bool foil = condFull.Contains("Foil", StringComparison.OrdinalIgnoreCase);
                string cond = condFull
                    .Replace("Foil", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

                rows.Add(new ImportRow
                {
                    Name = Get("Product Name"),
                    SetCode = string.Empty, // TCGPlayer uses set name not code
                    CollectorNumber = Get("Number"),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(cond),
                    Language = NormalizeLanguage(Get("Language"))
                });
            }
            return rows;
        }

        // ── Deckbox CSV parser ────────────────────────────────────────────────
        // Header: Count,Tradelist Count,Name,Edition,Card Number,Condition,Language,Foil,...
        private static List<ImportRow> ParseDeckboxCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);

            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Count"), out int q) ? Math.Max(q, 1) : 1;
                bool foil = !string.IsNullOrEmpty(Get("Foil"));

                rows.Add(new ImportRow
                {
                    Name = Get("Name"),
                    SetCode = Get("Edition"),
                    CollectorNumber = Get("Card Number"),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(Get("Condition")),
                    Language = NormalizeLanguage(Get("Language"))
                });
            }
            return rows;
        }

        // ── Dragon Shield CSV parser ──────────────────────────────────────────
        // Header: Quantity,Tradelist Count,Card Name,Set Name,Card Number,Finish,Condition,Date Added,Language
        private static List<ImportRow> ParseDragonShieldCsv(string filePath)
        {
            var rows = new List<ImportRow>();
            using var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
            string? header = sr.ReadLine();
            if (header == null) return rows;
            var cols = ParseCsvHeader(header);

            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = ParseCsvLine(line);
                string Get(string col) => cols.TryGetValue(col, out int i) && i < fields.Length
                    ? fields[i].Trim() : string.Empty;

                int qty = int.TryParse(Get("Quantity"), out int q) ? Math.Max(q, 1) : 1;
                string finish = Get("Finish").ToLower();
                bool foil = finish.Contains("foil") || finish.Contains("etched");

                DateTime added = DateTime.TryParse(Get("Date Added"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime dt) ? dt : DateTime.Now;

                rows.Add(new ImportRow
                {
                    Name = Get("Card Name"),
                    SetCode = string.Empty,
                    CollectorNumber = Get("Card Number"),
                    Quantity = qty,
                    IsFoil = foil,
                    Condition = MapMoxfieldCondition(Get("Condition")),
                    Language = NormalizeLanguage(Get("Language")),
                    DateAdded = added
                });
            }
            return rows;
        }

        // ── MTG Studio Deck XML import ────────────────────────────────────────
        private static (CollectionImportResult result, Deck? deck) ImportMtgStudioDeck(string filePath)
        {
            var result = new CollectionImportResult();
            try
            {
                var xml = XDocument.Load(filePath);
                var deckInfo = xml.Descendants("deckinfo").FirstOrDefault();
                var title = deckInfo?.Element("title")?.Value ?? Path.GetFileNameWithoutExtension(filePath);
                var creator = deckInfo?.Element("creator")?.Value ?? string.Empty;

                var deck = DeckService.CreateNew(title, deckType: DeckType.Standard);
                deck.Description = creator;

                using var pdb = new AppDbContext();
                // Build name→card lookup for fast matching
                var cardLookup = pdb.PoolCards.AsNoTracking()
                    .Select(c => new {
                        c.PoolId,
                        c.Name,
                        c.SetCode,
                        ScryfallId = "",
                        c.LocalImagePath,
                        c.ImageNormalUrl,
                        c.TypeLine,
                        c.ManaCost,
                        c.ManaValue,
                        c.ColorIdentity,
                        c.Power,
                        c.Toughness,
                        c.CollectorNumber
                    })
                    .ToList();

                foreach (var cardEl in xml.Descendants("card"))
                {
                    int deckQty = int.TryParse(cardEl.Attribute("deck")?.Value, out int dq) ? dq : 1;
                    int sbQty = int.TryParse(cardEl.Attribute("sb")?.Value, out int sq) ? sq : 0;
                    string edition = cardEl.Attribute("edition")?.Value ?? string.Empty;
                    string rawName = cardEl.Value.Trim();

                    // Strip MTG Studio suffixes: "[Reprint]", "(1)", etc.
                    string name = System.Text.RegularExpressions.Regex
                        .Replace(rawName, @"\s*\[.*?\]\s*$", "").Trim();
                    name = System.Text.RegularExpressions.Regex
                        .Replace(name, @"\s*\(\d+\)\s*$", "").Trim();

                    // Handle split/DFC cards — use first half for matching
                    // "Struggle/Survive" → "Struggle"
                    // "Defacing Duskmage // Vandal's Edit" → "Defacing Duskmage"
                    string matchName = name.Contains(" // ")
                        ? name.Split(new[] { " // " }, StringSplitOptions.None)[0].Trim()
                        : name.Contains('/')
                            ? name.Split('/')[0].Trim()
                            : name;

                    // Find pool card — try full name first, then front face
                    var match = cardLookup.FirstOrDefault(c =>
                        c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                        c.SetCode.Equals(edition, StringComparison.OrdinalIgnoreCase))
                        ?? cardLookup.FirstOrDefault(c =>
                            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        // Try front face match for DFCs stored with " // " in pool
                        ?? cardLookup.FirstOrDefault(c =>
                            (c.Name.Contains(" // ")
                                ? c.Name.Split(new[] { " // " }, StringSplitOptions.None)[0].Trim()
                                : c.Name)
                            .Equals(matchName, StringComparison.OrdinalIgnoreCase) &&
                            c.SetCode.Equals(edition, StringComparison.OrdinalIgnoreCase))
                        ?? cardLookup.FirstOrDefault(c =>
                            (c.Name.Contains(" // ")
                                ? c.Name.Split(new[] { " // " }, StringSplitOptions.None)[0].Trim()
                                : c.Name)
                            .Equals(matchName, StringComparison.OrdinalIgnoreCase));

                    if (match == null)
                    {
                        result.CardsNotFound++;
                        // Debug: show what we tried to match
                        result.Warnings.Add($"Not found: '{name}' [{edition}] (matchName='{matchName}')");
                        continue;
                    }

                    if (deckQty > 0)
                    {
                        deck.Cards.Add(new DeckCard
                        {
                            Name = match.Name,
                            SetCode = match.SetCode,
                            CollectorNumber = match.CollectorNumber,
                            TypeLine = match.TypeLine,
                            ManaCost = match.ManaCost,
                            ManaValue = match.ManaValue,
                            ColorIdentity = match.ColorIdentity,
                            Power = match.Power,
                            Toughness = match.Toughness,
                            LocalImagePath = match.LocalImagePath,
                            ImageNormalUrl = match.ImageNormalUrl,
                            Quantity = deckQty,
                            Category = DeckCardCategory.Mainboard
                        });
                        result.CardsImported += deckQty;
                    }

                    if (sbQty > 0)
                    {
                        deck.Cards.Add(new DeckCard
                        {
                            Name = match.Name,
                            SetCode = match.SetCode,
                            CollectorNumber = match.CollectorNumber,
                            TypeLine = match.TypeLine,
                            ManaCost = match.ManaCost,
                            ManaValue = match.ManaValue,
                            ColorIdentity = match.ColorIdentity,
                            Power = match.Power,
                            Toughness = match.Toughness,
                            LocalImagePath = match.LocalImagePath,
                            ImageNormalUrl = match.ImageNormalUrl,
                            Quantity = sbQty,
                            Category = DeckCardCategory.Sideboard
                        });
                        result.CardsImported += sbQty;
                    }
                }

                // MTG Studio .deck XML does not store foil or commander data
                result.Warnings.Add(
                    "MTG Studio .deck format does not include foil or commander " +
                    "information. Please mark foil cards and commanders manually " +
                    "in the deck editor, or use Breakers of E (.json) for full fidelity.");

                return (result, deck);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to parse deck file: {ex.Message}");
                return (result, null);
            }
        }

        // ── Apply parsed rows to the collection DB ────────────────────────────
        private static CollectionImportResult ApplyCollectionRows(List<ImportRow> rows,
            bool mergeWithExisting)
        {
            var result = new CollectionImportResult();
            if (rows.Count == 0)
            {
                result.Errors.Add("No rows found in import file.");
                return result;
            }

            using var pdb = new AppDbContext();
            using var cdb = new CollectionDbContext();

            // Build lookup dictionaries for fast matching
            var byScryfallId = pdb.PoolCards.AsNoTracking()
                .Where(c => "" != "")
                .ToDictionary(c => "", c => c,
                    StringComparer.OrdinalIgnoreCase);

            var byNameSet = pdb.PoolCards.AsNoTracking()
                .ToList()
                .GroupBy(c => $"{c.Name}|{c.SetCode}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            var existingEntries = cdb.CollectionEntries
                .ToList()
                .GroupBy(e => e.ScryfallId)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var row in rows)
            {
                // Match pool card
                PoolCard? pc = null;

                // 1. ScryfallId (most reliable)
                if (!string.IsNullOrEmpty(row.ScryfallId) &&
                    byScryfallId.TryGetValue(row.ScryfallId, out var byId))
                    pc = byId;

                // 2. Name + SetCode
                if (pc == null && !string.IsNullOrEmpty(row.Name))
                {
                    string key = $"{row.Name}|{row.SetCode}";
                    if (byNameSet.TryGetValue(key, out var byNS)) pc = byNS;
                }

                // 3. Name only
                if (pc == null && !string.IsNullOrEmpty(row.Name))
                {
                    string nameKey = $"{row.Name}|";
                    pc = byNameSet.Values.FirstOrDefault(c =>
                        c.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
                }

                // 4. DFC fallback — strip " // Back Face" and try front face only
                if (pc == null && row.Name.Contains(" // "))
                {
                    string frontFace = row.Name.Split(new[] { " // " },
                        StringSplitOptions.None)[0].Trim();
                    string dfcKey = $"{frontFace}|{row.SetCode}";
                    if (!byNameSet.TryGetValue(dfcKey, out pc))
                        pc = byNameSet.Values.FirstOrDefault(c =>
                            c.Name.Equals(frontFace,
                                StringComparison.OrdinalIgnoreCase));
                }

                if (pc == null)
                {
                    result.CardsNotFound++;
                    result.Warnings.Add($"Not in pool: {row.Name} [{row.SetCode}]");
                    continue;
                }

                // Find or create collection entry
                if (existingEntries.TryGetValue(pc.ScryfallId, out var existing))
                {
                    if (mergeWithExisting)
                    {
                        // Merge: take the higher of existing vs imported quantity
                        // This makes re-importing the same file safe (idempotent)
                        if (row.IsFoil)
                            existing.FoilQuantity = Math.Max(existing.FoilQuantity, row.Quantity);
                        else
                            existing.Quantity = Math.Max(existing.Quantity, row.Quantity);
                        existing.DateModified = DateTime.Now;
                        result.CardsUpdated++;
                    }
                    else
                    {
                        // Replace: overwrite quantities exactly
                        if (row.IsFoil) existing.FoilQuantity = row.Quantity;
                        else existing.Quantity = row.Quantity;
                        existing.DateModified = DateTime.Now;
                        result.CardsUpdated++;
                    }
                    // Update pricing if provided
                    if (row.BuyAt.HasValue) existing.BuyAt = row.BuyAt;
                    if (row.SellAt.HasValue) existing.SellAt = row.SellAt;
                    if (!string.IsNullOrEmpty(row.Notes))
                        existing.Notes = row.Notes;
                    if (!string.IsNullOrEmpty(row.StorageLocation))
                        existing.StorageLocation = row.StorageLocation;
                }
                else
                {
                    var entry = new CollectionEntry
                    {
                        PoolId = pc.PoolId,
                        ScryfallId = pc.ScryfallId,
                        OracleId = pc.OracleId,
                        Name = pc.Name,
                        ManaCost = pc.ManaCost,
                        ManaValue = pc.ManaValue,
                        TypeLine = pc.TypeLine,
                        OracleText = pc.OracleText,
                        FlavorText = pc.FlavorText,
                        Power = pc.Power,
                        Toughness = pc.Toughness,
                        LoyaltyOrDefense = pc.LoyaltyOrDefense,
                        Colors = pc.Colors,
                        ColorIdentity = pc.ColorIdentity,
                        SetCode = pc.SetCode,
                        SetName = pc.SetName,
                        SetType = pc.SetType,
                        CollectorNumber = pc.CollectorNumber,
                        Rarity = pc.Rarity,
                        Artist = pc.Artist,
                        ImageSmallUrl = pc.ImageSmallUrl,
                        ImageNormalUrl = pc.ImageNormalUrl,
                        ImageBackUrl = pc.ImageBackUrl,
                        LocalImagePath = pc.LocalImagePath,
                        LocalImageBackPath = pc.LocalImageBackPath,
                        Layout = pc.Layout,
                        IsFoilAvailable = pc.IsFoil,
                        IsNonFoilAvailable = pc.IsNonFoil,
                        IsToken = pc.IsToken,
                        IsMeld = pc.IsMeld,
                        ReleasedAt = pc.ReleasedAt,
                        LegalitiesJson = pc.LegalitiesJson,
                        IsFavorite = pc.IsFavorite,
                        Keywords = pc.Keywords,
                        PriceUsd = pc.PriceUsd,
                        PriceUsdFoil = pc.PriceUsdFoil,
                        PriceUsdEtched = pc.PriceUsdEtched,
                        PriceEur = pc.PriceEur,
                        PriceEurFoil = pc.PriceEurFoil,
                        PriceTix = pc.PriceTix,
                        PricesJson = pc.PricesJson,
                        Quantity = row.IsFoil ? 0 : row.Quantity,
                        FoilQuantity = row.IsFoil ? row.Quantity : 0,
                        Condition = row.Condition,
                        Language = row.Language,
                        Notes = row.Notes,
                        StorageLocation = row.StorageLocation,
                        BuyAt = row.BuyAt,
                        SellAt = row.SellAt,
                        CardGroup = row.Group,
                        DateAdded = row.DateAdded,
                        DateModified = DateTime.Now
                    };
                    cdb.CollectionEntries.Add(entry);
                    existingEntries[pc.ScryfallId] = entry;
                    result.CardsImported++;
                }
            }

            try
            {
                cdb.SaveChanges();
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Database error: {ex.Message}");
            }

            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        // EXPORT
        // ════════════════════════════════════════════════════════════════════

        public static void ExportCollection(string filePath,
            ImportExportFormat format)
        {
            using var cdb = new CollectionDbContext();

            var entries = cdb.CollectionEntries.AsNoTracking().ToList();
            // Build a PoolId->entry dict for export methods that expect Dictionary<int, PoolCard>
            // We create lightweight PoolCard wrappers from the self-contained entries
            var cards = entries.ToDictionary(e => e.CollectionEntryId, e => new PoolCard
            {
                PoolId = e.PoolId,
                ScryfallId = e.ScryfallId,
                Name = e.Name,
                SetCode = e.SetCode,
                SetName = e.SetName,
                CollectorNumber = e.CollectorNumber,
                TypeLine = e.TypeLine,
                OracleText = e.OracleText,
                ManaCost = e.ManaCost,
                ManaValue = e.ManaValue,
                ColorIdentity = e.ColorIdentity,
                Colors = e.Colors,
                Rarity = e.Rarity,
                Artist = e.Artist,
                Power = e.Power,
                Toughness = e.Toughness,
                ImageNormalUrl = e.ImageNormalUrl,
                LocalImagePath = e.LocalImagePath,
                PriceUsd = e.PriceUsd,
                PriceUsdFoil = e.PriceUsdFoil,
                LegalitiesJson = e.LegalitiesJson,
            });
            // Remap entries to use CollectionEntryId as key for the export dict
            var remapped = entries.Select(e => { var clone = e; return clone; }).ToList();
            foreach (var e in remapped) e.PoolId = e.CollectionEntryId;

            switch (format)
            {
                case ImportExportFormat.MtgStudioCsv:
                    ExportMtgStudioCsv(filePath, entries, cards); break;
                case ImportExportFormat.Moxfield:
                    ExportMoxfieldCsv(filePath, entries, cards); break;
                case ImportExportFormat.TcgPlayer:
                    ExportTcgPlayerCsv(filePath, entries, cards); break;
                case ImportExportFormat.Deckbox:
                    ExportDeckboxCsv(filePath, entries, cards); break;
                case ImportExportFormat.DragonShield:
                    ExportDragonShieldCsv(filePath, entries, cards); break;
                case ImportExportFormat.BreakersOfE:
                    ExportNativeJson(filePath, entries, cards); break;
            }
        }

        public static void ExportWantList(string filePath,
            ImportExportFormat format)
        {
            using var cdb = new CollectionDbContext();

            var entries = cdb.WantListEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return;

            // Convert to CollectionEntry-like for reuse of export methods
            // Card data is self-contained in the WantListEntry
            var collEntries = entries
                .Select(e => new CollectionEntry
                {
                    PoolId = e.WantListEntryId, // use as dict key
                    ScryfallId = e.ScryfallId,
                    Quantity = e.IsFoil ? 0 : e.Quantity,
                    FoilQuantity = e.IsFoil ? e.Quantity : 0,
                    Condition = "Near Mint",
                    BuyAt = e.OfferPrice,
                    DateAdded = e.DateAdded
                }).ToList();
            var cards = entries.ToDictionary(e => e.WantListEntryId, e => new PoolCard
            {
                ScryfallId = e.ScryfallId,
                Name = e.Name,
                SetCode = e.SetCode,
                SetName = e.SetName,
                CollectorNumber = e.CollectorNumber,
                PriceUsd = e.PriceUsd,
                PriceUsdFoil = e.PriceUsdFoil,
            });

            switch (format)
            {
                case ImportExportFormat.MtgStudioCsv:
                    ExportMtgStudioCsv(filePath, collEntries, cards); break;
                case ImportExportFormat.Moxfield:
                    ExportMoxfieldCsv(filePath, collEntries, cards); break;
                case ImportExportFormat.TcgPlayer:
                    ExportTcgPlayerCsv(filePath, collEntries, cards); break;
                case ImportExportFormat.Deckbox:
                    ExportDeckboxCsv(filePath, collEntries, cards); break;
                case ImportExportFormat.DragonShield:
                    ExportDragonShieldCsv(filePath, collEntries, cards); break;
                default:
                    ExportMtgStudioCsv(filePath, collEntries, cards); break;
            }
        }

        public static void ExportTradeBinder(string filePath,
            ImportExportFormat format)
        {
            using var cdb = new CollectionDbContext();

            var entries = cdb.TradeBinderEntries.AsNoTracking().ToList();
            if (entries.Count == 0) return;

            var collEntries = entries
                .Select(e => new CollectionEntry
                {
                    PoolId = e.TradeBinderEntryId, // use as dict key
                    ScryfallId = e.ScryfallId,
                    Quantity = e.IsFoil ? 0 : e.Quantity,
                    FoilQuantity = e.IsFoil ? e.Quantity : 0,
                    Condition = e.Condition,
                    SellAt = e.AskingPrice,
                    DateAdded = e.DateAdded
                }).ToList();
            var cards = entries.ToDictionary(e => e.TradeBinderEntryId, e => new PoolCard
            {
                ScryfallId = e.ScryfallId,
                Name = e.Name,
                SetCode = e.SetCode,
                SetName = e.SetName,
                CollectorNumber = e.CollectorNumber,
                PriceUsd = e.PriceUsd,
                PriceUsdFoil = e.PriceUsdFoil,
            });

            switch (format)
            {
                case ImportExportFormat.MtgStudioCsv:
                    ExportMtgStudioCsv(filePath, collEntries, cards); break;
                case ImportExportFormat.Moxfield:
                    ExportMoxfieldCsv(filePath, collEntries, cards); break;
                case ImportExportFormat.TcgPlayer:
                    ExportTcgPlayerCsv(filePath, collEntries, cards); break;
                case ImportExportFormat.Deckbox:
                    ExportDeckboxCsv(filePath, collEntries, cards); break;
                case ImportExportFormat.DragonShield:
                    ExportDragonShieldCsv(filePath, collEntries, cards); break;
                default:
                    ExportMtgStudioCsv(filePath, collEntries, cards); break;
            }
        }

        public static void ExportDeck(string filePath,
            ImportExportFormat format, Deck deck)
        {
            switch (format)
            {
                case ImportExportFormat.MtgStudioDeck:
                    ExportMtgStudioDeck(filePath, deck);
                    break;
                case ImportExportFormat.MtgStudioCsv:
                    ExportDeckAsMtgStudioCsv(filePath, deck);
                    break;
                case ImportExportFormat.Moxfield:
                    ExportDeckAsMoxfieldCsv(filePath, deck);
                    break;
                case ImportExportFormat.TcgPlayer:
                    ExportDeckAsTcgPlayerCsv(filePath, deck);
                    break;
                case ImportExportFormat.Deckbox:
                    ExportDeckAsDeckboxCsv(filePath, deck);
                    break;
                case ImportExportFormat.DragonShield:
                    ExportDeckAsDragonShieldCsv(filePath, deck);
                    break;
                case ImportExportFormat.BreakersOfE:
                    ExportDeckAsJson(filePath, deck);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Deck export not supported for {format}");
            }
        }

        private static void ExportDeckAsMtgStudioCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("CardId,ScryfallId,TcgPlayerId,MtgOnline3Id,Name,SetAbbreviation," +
                "SetName,CollectorNo,CollectorNoSortable,Quantity,Foil,Condition," +
                "Notes,Storage,Used,Target,Needed,Excess,Group,PrintType," +
                "BuyAt,SellAt,Desired,Buy,Sell,Added");
            int id = 1;
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                string cond = MapToMtgStudioCondition("Near Mint");
                if (c.Quantity > 0)
                    sw.WriteLine(CsvRow(id++, "", "", "", c.Name,
                        c.SetCode, c.SetName, c.CollectorNumber, c.CollectorNumber,
                        c.Quantity, "False", cond, "", "", 0, 0, 0, 0, "", "Paper",
                        0, 0, "Unassigned", "", "", DateTime.Now.ToString("O")));
                if (c.FoilQuantity > 0)
                    sw.WriteLine(CsvRow(id++, "", "", "", c.Name,
                        c.SetCode, c.SetName, c.CollectorNumber, c.CollectorNumber,
                        c.FoilQuantity, "True", cond, "", "", 0, 0, 0, 0, "", "Paper",
                        0, 0, "Unassigned", "", "", DateTime.Now.ToString("O")));
            }
        }

        private static void ExportDeckAsMoxfieldCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Count,Tradelist Count,Name,Edition,Condition,Language,Foil,Tags,Collector Number,Alter,Proxy,Purchase Price");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                if (c.Quantity > 0)
                    sw.WriteLine(CsvRow(c.Quantity, 0, c.Name, c.SetCode,
                        "Near Mint", "English", "", "", c.CollectorNumber, "False", "False", ""));
                if (c.FoilQuantity > 0)
                    sw.WriteLine(CsvRow(c.FoilQuantity, 0, c.Name, c.SetCode,
                        "Near Mint", "English", "foil", "", c.CollectorNumber, "False", "False", ""));
            }
        }

        private static void ExportDeckAsTcgPlayerCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Quantity,Name,Set,Card Number,Condition,Printing");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                if (c.Quantity > 0)
                    sw.WriteLine(CsvRow(c.Quantity, c.Name, c.SetName,
                        c.CollectorNumber, "Near Mint", "Normal"));
                if (c.FoilQuantity > 0)
                    sw.WriteLine(CsvRow(c.FoilQuantity, c.Name, c.SetName,
                        c.CollectorNumber, "Near Mint", "Foil"));
            }
        }

        private static void ExportDeckAsDeckboxCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Count,Tradelist Count,Name,Edition,Card Number,Condition,Language,Foil,Signed,Artist Proof,Altered Art,Misprint,Promo,Textless,My Price");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                if (c.Quantity > 0)
                    sw.WriteLine(CsvRow(c.Quantity, 0, c.Name, c.SetName,
                        c.CollectorNumber, "Near Mint", "English", "", "", "", "", "", "", "", ""));
                if (c.FoilQuantity > 0)
                    sw.WriteLine(CsvRow(c.FoilQuantity, 0, c.Name, c.SetName,
                        c.CollectorNumber, "Near Mint", "English", "foil", "", "", "", "", "", "", ""));
            }
        }

        private static void ExportDeckAsDragonShieldCsv(string filePath, Deck deck)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Folder Name,Quantity,Trade Quantity,Card Name,Set Code,Set Name,Collector Number,Printing,Condition,Language");
            foreach (var c in deck.Cards.OrderBy(c => c.Name))
            {
                if (c.Quantity > 0)
                    sw.WriteLine(CsvRow("Deck", c.Quantity, 0, c.Name,
                        c.SetCode, c.SetName, c.CollectorNumber, "Normal", "NM", "English"));
                if (c.FoilQuantity > 0)
                    sw.WriteLine(CsvRow("Deck", c.FoilQuantity, 0, c.Name,
                        c.SetCode, c.SetName, c.CollectorNumber, "Foil", "NM", "English"));
            }
        }

        private static void ExportDeckAsJson(string filePath, Deck deck)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(deck,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        // ── MTG Studio CSV export ─────────────────────────────────────────────
        private static void ExportMtgStudioCsv(string filePath,
            List<CollectionEntry> entries,
            Dictionary<int, PoolCard> cards)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("CardId,ScryfallId,TcgPlayerId,MtgOnline3Id,Name,SetAbbreviation," +
                "SetName,CollectorNo,CollectorNoSortable,Quantity,Foil,Condition," +
                "Notes,Storage,Used,Target,Needed,Excess,Group,PrintType," +
                "BuyAt,SellAt,Desired,Buy,Sell,Added");

            int id = 1;
            foreach (var e in entries.OrderBy(e =>
                cards.TryGetValue(e.PoolId, out var c) ? c.Name : ""))
            {
                if (!cards.TryGetValue(e.PoolId, out var pc)) continue;

                string cond = MapToMtgStudioCondition(e.Condition);

                // Write non-foil row
                if (e.Quantity > 0)
                    sw.WriteLine(CsvRow(id++, pc.ScryfallId, "", "", pc.Name,
                        pc.SetCode, pc.SetName, pc.CollectorNumber,
                        pc.CollectorNumber, e.Quantity, "False", cond,
                        e.Notes, e.StorageLocation, e.UsedCount, e.Target,
                        e.Needed, e.Excess, e.CardGroup, "Paper",
                        e.BuyAt ?? 0, e.SellAt ?? 0, "Unassigned", "", "",
                        e.DateAdded.ToString("O")));

                // Write foil row
                if (e.FoilQuantity > 0)
                    sw.WriteLine(CsvRow(id++, pc.ScryfallId, "", "", pc.Name,
                        pc.SetCode, pc.SetName, pc.CollectorNumber,
                        pc.CollectorNumber, e.FoilQuantity, "True", cond,
                        e.Notes, e.StorageLocation, 0, e.Target,
                        e.Needed, e.Excess, e.CardGroup, "Paper",
                        e.BuyAt ?? 0, e.SellAt ?? 0, "Unassigned", "", "",
                        e.DateAdded.ToString("O")));
            }
        }

        private static string MapToMtgStudioCondition(string c) => c switch
        {
            "Near Mint" => "NM",
            "Lightly Played" => "LP",
            "Moderately Played" => "MP",
            "Heavily Played" => "HP",
            "Damaged" => "D",
            _ => "UN"
        };

        // ── Moxfield CSV export ───────────────────────────────────────────────
        private static void ExportMoxfieldCsv(string filePath,
            List<CollectionEntry> entries, Dictionary<int, PoolCard> cards)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Count,Tradelist Count,Name,Edition,Condition,Language,Foil,Tags,Collector Number,Alter,Proxy,Purchase Price");

            foreach (var e in entries.OrderBy(e =>
                cards.TryGetValue(e.PoolId, out var c) ? c.Name : ""))
            {
                if (!cards.TryGetValue(e.PoolId, out var pc)) continue;
                string cond = MapToMoxfieldCondition(e.Condition);

                if (e.Quantity > 0)
                    sw.WriteLine($"{e.Quantity},0,{Q(pc.Name)},{pc.SetCode},{cond},{e.Language},," +
                        $"{Q(e.Notes)},{pc.CollectorNumber},,," +
                        $"{e.BuyAt?.ToString("F2", CultureInfo.InvariantCulture) ?? ""}");

                if (e.FoilQuantity > 0)
                    sw.WriteLine($"{e.FoilQuantity},0,{Q(pc.Name)},{pc.SetCode},{cond},{e.Language},foil," +
                        $"{Q(e.Notes)},{pc.CollectorNumber},,,");
            }
        }

        private static string MapToMoxfieldCondition(string c) => c switch
        {
            "Near Mint" => "Near Mint",
            "Lightly Played" => "Lightly Played",
            "Moderately Played" => "Moderately Played",
            "Heavily Played" => "Heavily Played",
            "Damaged" => "Damaged",
            "NM" => "Near Mint",
            "LP" => "Lightly Played",
            "MP" => "Moderately Played",
            "HP" => "Heavily Played",
            "D" => "Damaged",
            _ => "Near Mint"
        };

        // ── TCGPlayer CSV export ──────────────────────────────────────────────
        private static void ExportTcgPlayerCsv(string filePath,
            List<CollectionEntry> entries, Dictionary<int, PoolCard> cards)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Quantity,Product Name,Set Name,Number,Rarity,Condition,Add to Quantity");

            foreach (var e in entries.OrderBy(e =>
                cards.TryGetValue(e.PoolId, out var c) ? c.Name : ""))
            {
                if (!cards.TryGetValue(e.PoolId, out var pc)) continue;
                string cond = MapToMoxfieldCondition(e.Condition);

                if (e.Quantity > 0)
                    sw.WriteLine($"{e.Quantity},{Q(pc.Name)},{Q(pc.SetName)}," +
                        $"{pc.CollectorNumber},{CapFirst(pc.Rarity)},{cond},0");

                if (e.FoilQuantity > 0)
                    sw.WriteLine($"{e.FoilQuantity},{Q(pc.Name)},{Q(pc.SetName)}," +
                        $"{pc.CollectorNumber},{CapFirst(pc.Rarity)},{cond} Foil,0");
            }
        }

        // ── Deckbox CSV export ────────────────────────────────────────────────
        private static void ExportDeckboxCsv(string filePath,
            List<CollectionEntry> entries, Dictionary<int, PoolCard> cards)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Count,Tradelist Count,Name,Edition,Card Number,Condition,Language,Foil,Signed,Artist Proof,Altered Art,Misprint,Promo,Textless,My Price");

            foreach (var e in entries.OrderBy(e =>
                cards.TryGetValue(e.PoolId, out var c) ? c.Name : ""))
            {
                if (!cards.TryGetValue(e.PoolId, out var pc)) continue;
                string cond = MapToMoxfieldCondition(e.Condition);

                if (e.Quantity > 0)
                    sw.WriteLine($"{e.Quantity},0,{Q(pc.Name)},{Q(pc.SetName)}," +
                        $"{pc.CollectorNumber},{cond},{e.Language},,,,,,,,");

                if (e.FoilQuantity > 0)
                    sw.WriteLine($"{e.FoilQuantity},0,{Q(pc.Name)},{Q(pc.SetName)}," +
                        $"{pc.CollectorNumber},{cond},{e.Language},foil,,,,,,,");
            }
        }

        // ── Dragon Shield CSV export ──────────────────────────────────────────
        private static void ExportDragonShieldCsv(string filePath,
            List<CollectionEntry> entries, Dictionary<int, PoolCard> cards)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Quantity,Tradelist Count,Card Name,Set Name,Card Number,Finish,Condition,Date Added,Language");

            foreach (var e in entries.OrderBy(e =>
                cards.TryGetValue(e.PoolId, out var c) ? c.Name : ""))
            {
                if (!cards.TryGetValue(e.PoolId, out var pc)) continue;
                string cond = MapToMoxfieldCondition(e.Condition);
                string added = e.DateAdded.ToString("yyyy-MM-dd");

                if (e.Quantity > 0)
                    sw.WriteLine($"{e.Quantity},0,{Q(pc.Name)},{Q(pc.SetName)}," +
                        $"{pc.CollectorNumber},Normal,{cond},{added},{e.Language}");

                if (e.FoilQuantity > 0)
                    sw.WriteLine($"{e.FoilQuantity},0,{Q(pc.Name)},{Q(pc.SetName)}," +
                        $"{pc.CollectorNumber},Foil,{cond},{added},{e.Language}");
            }
        }

        // ── Breakers of E native JSON export ──────────────────────────────────
        private static void ExportNativeJson(string filePath,
            List<CollectionEntry> entries, Dictionary<int, PoolCard> cards)
        {
            var export = new
            {
                ExportedBy = "Breakers of E",
                ExportedAt = DateTime.Now,
                Cards = entries.Select(e =>
                {
                    cards.TryGetValue(e.PoolId, out var pc);
                    return new
                    {
                        ScryfallId = pc?.ScryfallId ?? "",
                        Name = pc?.Name ?? "",
                        SetCode = pc?.SetCode ?? "",
                        CollectorNumber = pc?.CollectorNumber ?? "",
                        Quantity = e.Quantity,
                        FoilQuantity = e.FoilQuantity,
                        Condition = e.Condition,
                        Language = e.Language,
                        BuyAt = e.BuyAt,
                        SellAt = e.SellAt,
                        Notes = e.Notes,
                        StorageLocation = e.StorageLocation,
                        DateAdded = e.DateAdded
                    };
                }).ToList()
            };
            File.WriteAllText(filePath,
                JsonSerializer.Serialize(export, new JsonSerializerOptions
                { WriteIndented = true }));
        }

        // ── MTG Studio deck XML export ────────────────────────────────────────
        private static void ExportMtgStudioDeck(string filePath, Deck deck)
        {
            var cardElements = new List<XElement>();

            foreach (var c in deck.Cards.Where(c => c.Category != DeckCardCategory.Sideboard))
            {
                // Non-foil copies
                for (int i = 0; i < c.Quantity; i++)
                    cardElements.Add(new XElement("card",
                        new XAttribute("deck", "1"),
                        new XAttribute("sb", "0"),
                        new XAttribute("edition", c.SetCode),
                        c.Name));
                // Foil copies — MTG Studio XML has no foil attribute
                // so foil cards are written as regular entries
                for (int i = 0; i < c.FoilQuantity; i++)
                    cardElements.Add(new XElement("card",
                        new XAttribute("deck", "1"),
                        new XAttribute("sb", "0"),
                        new XAttribute("edition", c.SetCode),
                        c.Name));
            }

            foreach (var c in deck.Cards.Where(c => c.Category == DeckCardCategory.Sideboard))
            {
                for (int i = 0; i < c.Quantity; i++)
                    cardElements.Add(new XElement("card",
                        new XAttribute("deck", "0"),
                        new XAttribute("sb", "1"),
                        new XAttribute("edition", c.SetCode),
                        c.Name));
                for (int i = 0; i < c.FoilQuantity; i++)
                    cardElements.Add(new XElement("card",
                        new XAttribute("deck", "0"),
                        new XAttribute("sb", "1"),
                        new XAttribute("edition", c.SetCode),
                        c.Name));
            }

            var xml = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement("mtgstudiodeck",
                    new XAttribute("version", "1.0"),
                    new XElement("deck",
                        new XElement("deckinfo",
                            new XElement("title", deck.Name),
                            new XElement("archetype", "Unspecified"),
                            new XElement("creator", ""),
                            new XElement("created", DateTime.Now.ToString("yyyy-MM-dd")),
                            new XElement("modified", DateTime.Now.ToString("yyyy-MM-dd")),
                            new XElement("version", "1.0"),
                            new XElement("description"),
                            new XElement("email")),
                        new XElement("cards", cardElements.ToArray()))));

            xml.Save(filePath);
        }

        // ════════════════════════════════════════════════════════════════════
        // CSV HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static Dictionary<string, int> ParseCsvHeader(string header)
        {
            var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var fields = ParseCsvLine(header);
            for (int i = 0; i < fields.Length; i++)
            {
                var fld = fields[i].Trim();
                if (fld.Length > 0 && fld[0] == (char)0xFEFF) fld = fld.Substring(1);
                cols.TryAdd(fld, i);
            }
            return cols;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        // CSV-safe quoted field
        private static string Q(string s) =>
            s.Contains(',') || s.Contains('"') || s.Contains('\n')
                ? $"\"{s.Replace("\"", "\"\"")}\""
                : s;

        // Build a full CSV row from args
        private static string CsvRow(params object?[] fields) =>
            string.Join(",", fields.Select(f =>
            {
                string s = f?.ToString() ?? string.Empty;
                return Q(s);
            }));

        private static string CapFirst(string s) =>
            string.IsNullOrEmpty(s) ? s :
            char.ToUpper(s[0]) + s[1..].ToLower();

        private static string NormalizeLanguage(string lang) => lang switch
        {
            var l when l.Equals("en", StringComparison.OrdinalIgnoreCase) => "English",
            var l when l.Equals("ja", StringComparison.OrdinalIgnoreCase) => "Japanese",
            var l when l.Equals("de", StringComparison.OrdinalIgnoreCase) => "German",
            var l when l.Equals("fr", StringComparison.OrdinalIgnoreCase) => "French",
            var l when l.Equals("it", StringComparison.OrdinalIgnoreCase) => "Italian",
            var l when l.Equals("es", StringComparison.OrdinalIgnoreCase) => "Spanish",
            var l when l.Equals("pt", StringComparison.OrdinalIgnoreCase) => "Portuguese",
            var l when l.Equals("ru", StringComparison.OrdinalIgnoreCase) => "Russian",
            var l when l.Equals("ko", StringComparison.OrdinalIgnoreCase) => "Korean",
            var l when l.Equals("zh-hans", StringComparison.OrdinalIgnoreCase) => "Chinese Simplified",
            var l when l.Equals("zh-hant", StringComparison.OrdinalIgnoreCase) => "Chinese Traditional",
            "" => "English",
            _ => lang
        };
    }
}