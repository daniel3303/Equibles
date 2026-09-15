using System.Globalization;
using System.Text.RegularExpressions;
using Equibles.Core.Identity;
using Equibles.Integrations.Xetra.Models;

namespace Equibles.Integrations.Xetra;

public static partial class XetraInstrumentListParser
{
    // The publisher's cache nodes write the same link root-relative or absolute on its own host.
    [GeneratedRegex(
        @"href=""(?:https://www\.cashmarket\.deutsche-boerse\.com)?(?<href>/resource/blob/\d+/[0-9a-f]+/data/t7-xetr-allTradableInstruments\.csv)""",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex DownloadLink();

    private static readonly string[] RequiredColumns =
    [
        "Product Status",
        "Instrument Status",
        "Instrument",
        "ISIN",
        "WKN",
        "Mnemonic",
        "MIC Code",
        "Instrument Type",
        "Currency",
        "Country Of Issue",
        "Primary Market MIC Code",
    ];

    // The blob hash rotates with every publication, so the address is read from the page each time.
    public static string ReadDownloadPath(string html)
    {
        var links = DownloadLink()
            .Matches(html ?? "")
            .Select(match => match.Groups["href"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (links.Count != 1)
            throw new InvalidDataException(
                "Xetra instrument page must link exactly one all-tradable-instruments file."
            );
        return links[0];
    }

    public static XetraInstrumentList Read(string csv)
    {
        var lines = (csv ?? "")
            .TrimStart('\uFEFF')
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();
        if (lines.Count < 4)
            throw new InvalidDataException("Xetra instrument file is too short to be complete.");
        var market = Split(lines[0]);
        var updated = Split(lines[1]);
        if (
            market.Count < 2
            || market[0] != "Market:"
            || market[1] is not { Length: 4 }
            || updated.Count < 2
            || updated[0] != "Date Last Update:"
            || !DateOnly.TryParseExact(
                updated[1],
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var lastUpdate
            )
        )
            throw new InvalidDataException("Xetra instrument file preamble has changed.");
        var header = Split(lines[2]);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < header.Count; index++)
            columns.TryAdd(header[index], index);
        var missing = RequiredColumns.Where(column => !columns.ContainsKey(column)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException(
                "Xetra instrument file lacks columns: " + string.Join(", ", missing)
            );
        var list = new XetraInstrumentList
        {
            MarketIdentifierCode = market[1],
            LastUpdate = lastUpdate,
            Csv = csv,
        };
        var identities = new HashSet<(string Isin, string Mic)>();
        foreach (var line in lines.Skip(3))
        {
            if (line.Length == 0)
                continue;
            var cells = Split(line);
            if (cells.Count != header.Count)
                throw new InvalidDataException("Xetra instrument row has an unexpected shape.");
            string Cell(string column) => cells[columns[column]].Trim();
            var row = new XetraInstrument
            {
                ProductStatus = Cell("Product Status"),
                InstrumentStatus = Cell("Instrument Status"),
                Name = Cell("Instrument"),
                Isin = Cell("ISIN"),
                Wkn = Cell("WKN"),
                Mnemonic = Cell("Mnemonic"),
                MarketIdentifierCode = Cell("MIC Code"),
                InstrumentType = Cell("Instrument Type"),
                Currency = Cell("Currency"),
                CountryOfIssue = Cell("Country Of Issue"),
                PrimaryMarketIdentifierCode = NullIfEmpty(Cell("Primary Market MIC Code")),
            };
            if (row.MarketIdentifierCode != list.MarketIdentifierCode)
                throw new InvalidDataException("Xetra instrument row names another market.");
            // A share pending deletion may already have lost its mnemonic; only tradable shares must state one.
            if (row.InstrumentType == "CS" && row.InstrumentStatus == "Active")
            {
                if (
                    !InternationalSecurityIdentifiers.IsValidIsin(row.Isin)
                    || string.IsNullOrWhiteSpace(row.Mnemonic)
                    || row.Mnemonic.Length > 32
                    || string.IsNullOrWhiteSpace(row.Name)
                    || row.Name.Length > 500
                    || row.PrimaryMarketIdentifierCode != null
                        && !IsMarketIdentifierCode(row.PrimaryMarketIdentifierCode)
                )
                    throw new InvalidDataException(
                        "Xetra share row lacks valid stated listing identity."
                    );
                if (!identities.Add((row.Isin, row.MarketIdentifierCode)))
                    throw new InvalidDataException(
                        "Xetra instrument file repeats a share identity."
                    );
            }
            list.Instruments.Add(row);
        }
        if (list.Instruments.Count == 0)
            throw new InvalidDataException("Xetra instrument file contains no rows.");
        return list;
    }

    private static string NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static bool IsMarketIdentifierCode(string value) =>
        value.Length == 4
        && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');

    // Semicolon-separated with optional double quotes; a quoted cell may contain the separator.
    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else if (character == '"')
                    quoted = false;
                else
                    current.Append(character);
            }
            else if (character == '"')
                quoted = true;
            else if (character == ';')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(character);
        }
        cells.Add(current.ToString());
        return cells;
    }
}
