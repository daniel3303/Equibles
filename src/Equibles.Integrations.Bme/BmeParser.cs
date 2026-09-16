using System.Text.Json;
using Equibles.Core.Identity;
using Equibles.Integrations.Bme.Models;

namespace Equibles.Integrations.Bme;

public static class BmeParser
{
    public const string ContinuousMarket = "SIBE";

    public static BmeListedCompanyList ReadListedCompanies(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        // An unpaged request answers the whole market; a reply that admits more is not a directory.
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("totalResults", out var total)
            || total.ValueKind != JsonValueKind.Number
            || !root.TryGetProperty("hasMoreResults", out var more)
            || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
        )
            throw new InvalidDataException("BME listed-companies reply has changed shape.");
        if (more.GetBoolean() || total.GetInt32() != data.GetArrayLength())
            throw new InvalidDataException("BME listed-companies reply is not the complete list.");
        var list = new BmeListedCompanyList { TotalResults = total.GetInt32(), Json = json };
        var isins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in data.EnumerateArray())
        {
            var company = new BmeListedCompany
            {
                Name = Text(row, "name"),
                CompanyKey = Text(row, "companyKey"),
                Isin = Text(row, "isin"),
                ShareName = Text(row, "shareName"),
                Sector = Text(row, "sector"),
                Subsector = Text(row, "subsector"),
                TradingSystem = Text(row, "tradingSystem"),
            };
            if (
                !InternationalSecurityIdentifiers.IsValidIsin(company.Isin)
                || string.IsNullOrWhiteSpace(company.Name)
                || company.Name.Length > 500
                || string.IsNullOrWhiteSpace(company.CompanyKey)
                || company.TradingSystem != ContinuousMarket
            )
                throw new InvalidDataException(
                    "BME listed-companies row lacks valid stated listing identity."
                );
            if (!isins.Add(company.Isin))
                throw new InvalidDataException(
                    "BME listed-companies reply repeats a share identity."
                );
            list.Companies.Add(company);
        }
        if (list.Companies.Count == 0)
            throw new InvalidDataException("BME listed-companies reply contains no rows.");
        return list;
    }

    public static BmeShareDetails ReadShareDetails(string json)
    {
        using var document = Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("BME share-details reply has changed shape.");
        var details = new BmeShareDetails
        {
            Name = Text(root, "name"),
            ShortName = Text(root, "shortName"),
            Isin = Text(root, "isin"),
            Ticker = Text(root, "ticker"),
            IssuerCode = Text(root, "issuerCode"),
            Market = Text(root, "market"),
            TradingSystem = Text(root, "tradingSystem"),
            Currency = Text(root, "currency"),
            Active = Text(root, "active"),
            Json = json,
        };
        if (
            !InternationalSecurityIdentifiers.IsValidIsin(details.Isin)
            || string.IsNullOrWhiteSpace(details.Name)
            || details.Name.Length > 500
            || string.IsNullOrWhiteSpace(details.Ticker)
            || details.Ticker.Length > 32
            || string.IsNullOrWhiteSpace(details.IssuerCode)
            || string.IsNullOrWhiteSpace(details.TradingSystem)
            || !IsCurrencyCode(details.Currency)
        )
            throw new InvalidDataException(
                "BME share-details reply lacks valid stated listing identity."
            );
        if (
            root.TryGetProperty("otherSharesFromIssuer", out var others)
            && others.ValueKind == JsonValueKind.Array
        )
            foreach (var other in others.EnumerateArray())
            {
                var share = new BmeIssuerShare
                {
                    Isin = Text(other, "isin"),
                    Name = Text(other, "name"),
                    LongName = Text(other, "longName"),
                    Active = Text(other, "active"),
                    ExclusionDate = Text(other, "exclusionDate"),
                };
                if (!InternationalSecurityIdentifiers.IsValidIsin(share.Isin))
                    throw new InvalidDataException(
                        "BME share-details reply names an issuer line without a valid ISIN."
                    );
                details.OtherSharesFromIssuer.Add(share);
            }
        return details;
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json ?? "");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("BME reply is not JSON.", exception);
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool IsCurrencyCode(string value) =>
        value is { Length: 3 } && value.All(character => character is >= 'A' and <= 'Z');
}
