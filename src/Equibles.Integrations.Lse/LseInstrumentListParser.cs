using System.Globalization;
using Equibles.Integrations.Lse.Models;
using Equibles.Integrations.Lse.Xlsx;

namespace Equibles.Integrations.Lse;

// Reads the shares sheet of the exchange's instrument list. The sheet states its own as-at date and how many
// instruments it holds, so a truncated or re-shaped workbook is refused instead of read as a shorter market.
public static class LseInstrumentListParser
{
    public const string SharesSheet = "1.1 Shares";
    private const string AsAtPrefix = "As at ";
    private const string CountPrefix = "Number of Instruments:";

    private static readonly string[] Columns =
    [
        "TIDM",
        "Issuer Name",
        "Instrument Name",
        "ISIN",
        "MiFIR Identifier Code",
        "Trading Currency",
        "LSE Market",
    ];

    private static readonly string[] AsAtFormats = ["d MMMM yyyy", "dd MMMM yyyy"];

    public static LseInstrumentList Read(byte[] workbook, long maxPartBytes)
    {
        using var document = XlsxWorkbook.Open(workbook, maxPartBytes);
        var rows = document.ReadSheet(SharesSheet);
        var header =
            rows.FirstOrDefault(row =>
                Columns.All(column =>
                    row.Cells.Any(cell =>
                        string.Equals(cell?.Trim(), column, StringComparison.Ordinal)
                    )
                )
            )
            ?? throw new InvalidDataException(
                "London instrument list has no shares header row; the workbook has changed shape."
            );
        var headings = header.Cells.Select(cell => cell?.Trim()).ToList();
        var columns = Columns.ToDictionary(
            column => column,
            column => headings.IndexOf(column),
            StringComparer.Ordinal
        );
        var preamble = rows.Where(row => row.Number < header.Number).ToList();
        var list = new LseInstrumentList
        {
            AsAt = ReadAsAt(preamble),
            StatedCount = ReadStatedCount(preamble),
        };
        list.Instruments = rows.Where(row => row.Number > header.Number)
            .Select(row => Instrument(row, columns))
            .Where(instrument => instrument.Isin.Length > 0)
            .ToList();
        if (list.Instruments.Count != list.StatedCount)
            throw new InvalidDataException(
                $"London instrument list states {list.StatedCount} shares and holds {list.Instruments.Count}."
            );
        return list;
    }

    private static LseInstrument Instrument(XlsxRow row, IReadOnlyDictionary<string, int> columns)
    {
        var lseMarket = Text(row, columns, "LSE Market");
        return new LseInstrument
        {
            Tidm = Text(row, columns, "TIDM"),
            IssuerName = Text(row, columns, "Issuer Name"),
            InstrumentName = Text(row, columns, "Instrument Name"),
            Isin = Text(row, columns, "ISIN"),
            MifirIdentifier = Text(row, columns, "MiFIR Identifier Code"),
            TradingCurrency = Text(row, columns, "Trading Currency"),
            LseMarket = lseMarket,
            MarketIdentifierCode = LseMarketSegments.TryResolve(lseMarket),
        };
    }

    private static string Text(
        XlsxRow row,
        IReadOnlyDictionary<string, int> columns,
        string column
    ) => row.Cell(columns[column]).Trim();

    private static DateOnly ReadAsAt(IEnumerable<XlsxRow> preamble)
    {
        var stated = Find(preamble, AsAtPrefix);
        if (
            stated != null
            && DateOnly.TryParseExact(
                stated[AsAtPrefix.Length..].Trim(),
                AsAtFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var asAt
            )
        )
            return asAt;
        throw new InvalidDataException(
            "London instrument list does not state a readable as-at date."
        );
    }

    private static int ReadStatedCount(IEnumerable<XlsxRow> preamble)
    {
        var stated = Find(preamble, CountPrefix);
        if (
            stated != null
            && int.TryParse(
                stated[CountPrefix.Length..].Trim(),
                NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var count
            )
            && count > 0
        )
            return count;
        throw new InvalidDataException(
            "London instrument list does not state how many shares it holds."
        );
    }

    private static string Find(IEnumerable<XlsxRow> rows, string prefix) =>
        rows.SelectMany(row => row.Cells)
            .Select(cell => cell?.Trim())
            .FirstOrDefault(cell =>
                cell != null && cell.StartsWith(prefix, StringComparison.Ordinal)
            );
}
