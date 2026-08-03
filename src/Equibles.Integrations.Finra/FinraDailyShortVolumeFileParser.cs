using System.Globalization;
using Equibles.Integrations.Finra.Models;

namespace Equibles.Integrations.Finra;

public static class FinraDailyShortVolumeFileParser
{
    private const string ExpectedHeader =
        "Date|Symbol|ShortVolume|ShortExemptVolume|TotalVolume|Market";

    public static async Task<List<ShortVolumeRecord>> Parse(
        Stream stream,
        DateOnly expectedDate,
        CancellationToken cancellationToken = default
    )
    {
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync(cancellationToken);
        if (!string.Equals(header, ExpectedHeader, StringComparison.Ordinal))
            throw new FormatException($"Unexpected FINRA daily short-volume header: '{header}'.");

        var records = new List<ShortVolumeRecord>();
        int? declaredCount = null;
        string line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.Contains('|'))
            {
                declaredCount = ParseTrailer(line);
                continue;
            }

            if (declaredCount != null)
                throw new FormatException(
                    "FINRA daily short-volume file contains data after its trailer."
                );

            records.Add(ParseRecord(line, expectedDate));
        }

        if (declaredCount == null)
            throw new FormatException(
                "FINRA daily short-volume file is missing its record-count trailer."
            );
        if (declaredCount.Value != records.Count)
        {
            throw new FormatException(
                $"FINRA daily short-volume trailer declares {declaredCount.Value} records but {records.Count} were parsed."
            );
        }

        return records;
    }

    private static ShortVolumeRecord ParseRecord(string line, DateOnly expectedDate)
    {
        var fields = line.Split('|');
        if (fields.Length != 6)
            throw new FormatException($"Unexpected FINRA daily short-volume row: '{line}'.");

        if (
            !DateOnly.TryParseExact(
                fields[0],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
            || date != expectedDate
        )
        {
            throw new FormatException(
                $"FINRA daily short-volume row date '{fields[0]}' does not match {expectedDate:yyyy-MM-dd}."
            );
        }

        if (string.IsNullOrWhiteSpace(fields[1]) || string.IsNullOrWhiteSpace(fields[5]))
            throw new FormatException(
                $"FINRA daily short-volume row has a blank symbol or market: '{line}'."
            );

        return new ShortVolumeRecord
        {
            TradeReportDate = expectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Symbol = NormalizeSymbol(fields[1]),
            ShortVolume = ParseQuantity(fields[2], "ShortVolume"),
            ShortExemptVolume = ParseQuantity(fields[3], "ShortExemptVolume"),
            TotalVolume = ParseQuantity(fields[4], "TotalVolume"),
            MarketCode = fields[5],
        };
    }

    // FINRA uses slash for class shares; CommonStock stores the equivalent exchange dash form.
    private static string NormalizeSymbol(string symbol) => symbol.Replace('/', '-');

    private static decimal ParseQuantity(string value, string fieldName)
    {
        if (
            !decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var quantity
            )
        )
        {
            throw new FormatException(
                $"FINRA daily short-volume {fieldName} value '{value}' is invalid."
            );
        }

        if (quantity < 0)
        {
            throw new FormatException(
                $"FINRA daily short-volume {fieldName} value '{value}' cannot be negative."
            );
        }

        if (decimal.Round(quantity, 6) != quantity)
        {
            throw new FormatException(
                $"FINRA daily short-volume {fieldName} value '{value}' has more than 6 decimal places."
            );
        }

        return quantity;
    }

    private static int ParseTrailer(string line)
    {
        if (!int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            throw new FormatException($"Invalid FINRA daily short-volume trailer: '{line}'.");
        return count;
    }
}
