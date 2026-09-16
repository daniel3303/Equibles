using System.Text.Json;
using Equibles.Core.Identity;
using Equibles.Integrations.NasdaqNordic.Models;

namespace Equibles.Integrations.NasdaqNordic;

public static class NasdaqNordicParser
{
    public static NasdaqNordicShareList ReadShareList(string json)
    {
        using var document = Parse(json);
        var data = Data(document);
        if (
            !data.TryGetProperty("instrumentListing", out var listing)
            || !listing.TryGetProperty("rows", out var rows)
            || rows.ValueKind != JsonValueKind.Array
        )
            throw new InvalidDataException("Nasdaq screener reply has changed shape.");
        // The venue pages long lists; a reply that admits more rows than it carries is not a directory.
        if (
            !data.TryGetProperty("pagination", out var pagination)
            || pagination.ValueKind != JsonValueKind.Object
            || !pagination.TryGetProperty("total", out var total)
            || total.ValueKind != JsonValueKind.Number
            || !total.TryGetInt32(out var totalRows)
            || !pagination.TryGetProperty("totalPages", out var pages)
            || pages.ValueKind != JsonValueKind.Number
            || !pages.TryGetInt32(out var totalPages)
        )
            throw new InvalidDataException("Nasdaq screener reply has changed shape.");
        if (totalRows != rows.GetArrayLength() || totalPages != 1)
            throw new InvalidDataException("Nasdaq screener reply is not the complete list.");
        var list = new NasdaqNordicShareList { Json = json };
        var isins = new HashSet<string>(StringComparer.Ordinal);
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            var share = new NasdaqNordicShare
            {
                FullName = Text(row, "fullName"),
                Symbol = Text(row, "symbol"),
                Isin = Text(row, "isin"),
                Currency = Text(row, "currency"),
                OrderbookId = Text(row, "orderbookId"),
                AssetClass = Text(row, "assetClass"),
                Sector = Text(row, "sector"),
            };
            if (
                share.AssetClass != "SHARES"
                || !InternationalSecurityIdentifiers.IsValidIsin(share.Isin)
                || string.IsNullOrWhiteSpace(share.Symbol)
                || share.Symbol.Length > 32
                || string.IsNullOrWhiteSpace(share.FullName)
                || share.FullName.Length > 500
                || !IsCurrencyCode(share.Currency)
                || !IsOrderbookId(share.OrderbookId)
            )
                throw new InvalidDataException(
                    "Nasdaq screener row lacks valid stated listing identity."
                );
            if (!isins.Add(share.Isin))
                throw new InvalidDataException("Nasdaq screener reply repeats a share identity.");
            if (!symbols.Add(share.Symbol))
                throw new InvalidDataException(
                    "Nasdaq screener reply gives one symbol multiple security identities."
                );
            list.Shares.Add(share);
        }
        if (list.Shares.Count == 0)
            throw new InvalidDataException("Nasdaq screener reply contains no rows.");
        return list;
    }

    public static NasdaqNordicInstrument ReadInstrument(string json)
    {
        using var document = Parse(json);
        var data = Data(document);
        if (
            !data.TryGetProperty("qdHeader", out var header)
            || header.ValueKind != JsonValueKind.Object
        )
            throw new InvalidDataException("Nasdaq instrument reply has changed shape.");
        var instrument = new NasdaqNordicInstrument
        {
            Symbol = Text(header, "symbol"),
            CompanyName = Text(header, "companyName"),
            Exchange = Text(header, "exchange"),
            Segment = Text(header, "segment"),
            MarketStatus = Text(header, "marketStatus"),
            Isin = Text(header, "isin"),
            Currency = Text(header, "currency"),
        };
        if (
            !InternationalSecurityIdentifiers.IsValidIsin(instrument.Isin)
            || string.IsNullOrWhiteSpace(instrument.Symbol)
            || string.IsNullOrWhiteSpace(instrument.Exchange)
            || !IsCurrencyCode(instrument.Currency)
        )
            throw new InvalidDataException(
                "Nasdaq instrument reply lacks valid stated listing identity."
            );
        return instrument;
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json ?? "");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Nasdaq reply is not JSON.", exception);
        }
    }

    // Every reply wraps its payload in data with a status block; anything but rCode 200 is a refusal.
    private static JsonElement Data(JsonDocument document)
    {
        var root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("status", out var status)
            || !status.TryGetProperty("rCode", out var code)
            || code.ValueKind != JsonValueKind.Number
            || code.GetInt32() != 200
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
        )
            throw new InvalidDataException("Nasdaq reply did not succeed.");
        return data;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool IsCurrencyCode(string value) =>
        value is { Length: 3 } && value.All(character => character is >= 'A' and <= 'Z');

    private static bool IsOrderbookId(string value) =>
        value is { Length: > 0 and <= 32 }
        && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');
}
