using System.Text.Json;
using Equibles.Integrations.Euronext.Models;
using HtmlAgilityPack;

namespace Equibles.Integrations.Euronext;

public static class EuronextDirectoryParser
{
    private static readonly Uri Origin = new("https://live.euronext.com");
    private static readonly HashSet<string> LisbonMarkets = ["XLIS", "ENXL", "ALXL"];
    internal const string DataPoints =
        "name,isin,symbol,market,lastPrice,precentDayChange,lastTradeTime";

    public static Uri ReadLisbonGateway(string html)
    {
        var document = Html(html);
        var settings = document.DocumentNode.SelectNodes(
            "//script[@data-drupal-selector='drupal-settings-json']"
        );
        if (settings?.Count != 1)
            throw new InvalidDataException("Expected one Euronext directory settings document.");
        using var json = JsonDocument.Parse(settings[0].InnerHtml);
        var root = json.RootElement;
        if (
            !root.TryGetProperty("jsongateway", out var gateway)
            || gateway.ValueKind != JsonValueKind.String
        )
            throw new InvalidDataException("Euronext directory gateway is absent.");
        var uri = SameOrigin(gateway.GetString());
        var queryParts = uri.Query.TrimStart('?').Split('&');
        var marketArgument = queryParts.Length == 1 ? queryParts[0].Split('=', 2) : [];
        var markets =
            marketArgument.Length == 2 && marketArgument[0] == "mics"
                ? Uri.UnescapeDataString(marketArgument[1]).Split(',')
                : [];
        if (markets.Length != LisbonMarkets.Count || !LisbonMarkets.SetEquals(markets))
            throw new InvalidDataException(
                "Euronext directory must include all Lisbon markets without additional filters."
            );
        if (uri.AbsolutePath != "/en/product_directory/data/stocks-lisbon")
            throw new InvalidDataException(
                "Euronext directory gateway does not identify Lisbon equities."
            );
        if (
            !root.TryGetProperty("datapoints", out var points)
            || points.ValueKind != JsonValueKind.Array
            || string.Join(',', points.EnumerateArray().Select(point => point.GetString()))
                != DataPoints
        )
            throw new InvalidDataException("Euronext directory columns have changed.");
        return uri;
    }

    public static EuronextDirectoryPage ReadLisbonPage(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (
            !root.TryGetProperty("iTotalRecords", out var totalValue)
            || !totalValue.TryGetInt32(out var total)
            || !root.TryGetProperty("iTotalDisplayRecords", out var displayedValue)
            || !displayedValue.TryGetInt32(out var displayed)
            || total <= 0
            || total != displayed
            || total > 2000
        )
            throw new InvalidDataException(
                "Euronext directory must report a non-empty, unfiltered bounded total."
            );
        if (
            !root.TryGetProperty("aaData", out var rows)
            || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() == 0
            || rows.GetArrayLength() > total
        )
            throw new InvalidDataException(
                "Euronext directory rows are incomplete or inconsistent."
            );
        var page = new EuronextDirectoryPage { TotalRecords = total };
        var identities = new HashSet<(string Isin, string Mic)>();
        foreach (var row in rows.EnumerateArray())
        {
            if (
                row.ValueKind != JsonValueKind.Array
                || row.GetArrayLength() != 7
                || row.EnumerateArray().Any(cell => cell.ValueKind != JsonValueKind.String)
            )
                throw new InvalidDataException("Euronext directory row has an unexpected shape.");
            var nameCell = Html(row[0].GetString());
            var links = nameCell.DocumentNode.SelectNodes("//a[@href]");
            var name = links?.Count == 1 ? Text(links[0]) : null;
            var isin = PlainCell(row[1].GetString());
            var symbol = PlainCell(row[2].GetString());
            var mic = PlainCell(row[3].GetString());
            if (
                string.IsNullOrWhiteSpace(name)
                || name.Length > 500
                || !ValidIsin(isin)
                || string.IsNullOrWhiteSpace(symbol)
                || symbol.Length > 32
                || !LisbonMarkets.Contains(mic)
            )
                throw new InvalidDataException(
                    "Euronext directory row lacks valid stated listing identity."
                );
            var source = SameOrigin(links[0].GetAttributeValue("href", null));
            if (
                source.AbsolutePath != $"/en/product/equities/{isin}-{mic}"
                || source.Query.Length != 0
                || source.Fragment.Length != 0
            )
                throw new InvalidDataException(
                    "Euronext product link conflicts with its stated ISIN and market."
                );
            if (!identities.Add((isin, mic)))
                throw new InvalidDataException("Euronext directory repeats a listing identity.");
            var currencyNode = Html(row[4].GetString())
                .DocumentNode.SelectSingleNode(
                    "//*[contains(concat(' ', normalize-space(@class), ' '), ' pd_currency_es ')]"
                );
            var currency =
                currencyNode == null
                    ? null
                    : string.Concat(
                            currencyNode
                                .ChildNodes.Where(node => node.NodeType == HtmlNodeType.Text)
                                .Select(Text)
                        )
                        .Trim();
            if (currency is "" or "-")
                currency = null;
            page.Listings.Add(
                new EuronextEquityListing
                {
                    Name = name,
                    Isin = isin,
                    Symbol = symbol,
                    MarketIdentifierCode = mic,
                    ReportedCurrency = currency,
                    SourceUrl = source,
                }
            );
        }
        return page;
    }

    public static Uri ValidateProductUrl(EuronextEquityListing listing)
    {
        if (
            listing == null
            || !ValidIsin(listing.Isin)
            || !LisbonMarkets.Contains(listing.MarketIdentifierCode)
            || string.IsNullOrWhiteSpace(listing.Symbol)
            || listing.Symbol.Length > 32
        )
            throw new InvalidDataException("A stated Lisbon listing identity is required.");
        if (listing.SourceUrl is not { IsAbsoluteUri: true })
            throw new InvalidDataException("An absolute source product URL is required.");
        var source = SameOrigin(listing.SourceUrl.AbsoluteUri);
        if (
            source.AbsolutePath
                != $"/en/product/equities/{listing.Isin}-{listing.MarketIdentifierCode}"
            || source.Query.Length != 0
            || source.Fragment.Length != 0
        )
            throw new InvalidDataException(
                "Euronext product link conflicts with its stated listing."
            );
        return source;
    }

    public static EuronextInstrumentIdentity ReadInstrumentIdentity(
        EuronextEquityListing listing,
        string html
    )
    {
        var sourceUrl = ValidateProductUrl(listing);
        var document = Html(html);
        var settings = document.DocumentNode.SelectNodes(
            "//script[@data-drupal-selector='drupal-settings-json']"
        );
        if (settings?.Count != 1)
            throw new InvalidDataException("Expected one Euronext product settings document.");
        using var json = JsonDocument.Parse(settings[0].InnerHtml);
        // Other global settings may contain unrelated example instruments; only this node names the product.
        if (
            !json.RootElement.TryGetProperty("custom", out var custom)
            || custom.ValueKind != JsonValueKind.Object
            || !custom.TryGetProperty("instrument", out var instrument)
            || instrument.ValueKind != JsonValueKind.Object
        )
            throw new InvalidDataException("Euronext product identity is absent.");
        var isin = RequiredString(instrument, "isin", 12);
        var mic = RequiredString(instrument, "mic", 4);
        var symbol = RequiredString(instrument, "symbol", 32);
        var type = RequiredString(instrument, "type", 32);
        if (
            isin != listing.Isin
            || mic != listing.MarketIdentifierCode
            || symbol != listing.Symbol
            || type != "STOCK"
            || RequiredString(instrument, "product_data", 32) != $"{isin}-{mic}"
            || RequiredString(instrument, "url_type", 32) != "equities"
        )
            throw new InvalidDataException(
                "Euronext product identity conflicts with the directory record."
            );
        return new EuronextInstrumentIdentity
        {
            Isin = isin,
            MarketIdentifierCode = mic,
            Symbol = symbol,
            Name = RequiredString(instrument, "name", 500),
            IssuerCode = RequiredString(instrument, "issuer_code", 128),
            SourceInstrumentType = type,
            SourceUrl = sourceUrl,
            RawInstrumentJson = instrument.GetRawText(),
        };
    }

    private static string RequiredString(JsonElement node, string key, int maxLength)
    {
        if (
            !node.TryGetProperty(key, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString())
            || value.GetString().Length > maxLength
        )
            throw new InvalidDataException($"Euronext product identity is missing {key}.");
        return value.GetString();
    }

    private static Uri SameOrigin(string value)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(Origin, value, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != Origin.Host
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
        )
            throw new InvalidDataException(
                "Euronext source URL must remain on its official HTTPS origin."
            );
        return uri;
    }

    private static HtmlDocument Html(string value)
    {
        var document = new HtmlDocument();
        document.LoadHtml(value ?? "");
        return document;
    }

    private static string PlainCell(string value) => Text(Html(value).DocumentNode);

    private static string Text(HtmlNode node) => HtmlEntity.DeEntitize(node.InnerText).Trim();

    // ISO 6166 identifier syntax/check digit, not instrument classification.
    private static bool ValidIsin(string value)
    {
        if (
            value?.Length != 12
            || value[0] is < 'A' or > 'Z'
            || value[1] is < 'A' or > 'Z'
            || value[^1] is < '0' or > '9'
        )
            return false;
        var digits = new List<int>();
        foreach (var character in value)
        {
            if (character is >= '0' and <= '9')
                digits.Add(character - '0');
            else if (character is >= 'A' and <= 'Z')
            {
                var number = character - 'A' + 10;
                digits.Add(number / 10);
                digits.Add(number % 10);
            }
            else
                return false;
        }
        var sum = 0;
        for (var index = digits.Count - 1; index >= 0; index--)
        {
            var digit = digits[index] * ((digits.Count - 1 - index) % 2 == 0 ? 1 : 2);
            sum += digit / 10 + digit % 10;
        }
        return sum % 10 == 0;
    }
}
