using System.Globalization;
using System.Text;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Retained JSON supplies tagged financial-note excerpts, not the complete visual report.
/// Context dates select disclosures but never become invented wording in their retrieval text.
/// </summary>
public static class EsefJsonReportContent
{
    public static byte[] Build(
        string json,
        string issuerLei,
        DateOnly periodEnd,
        ISecDocumentHtmlNormalizer normalizer,
        ISecDocumentHtmlToMarkdownConverter converter
    )
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(converter);
        if (json == null || json.Length > 50 * 1024 * 1024)
            throw new InvalidDataException("The JSON report exceeds the retrieval source limit.");

        // Reuse the financial parser's envelope, duplicate-property, unit and owner checks.
        var facts = new JsonXbrlParser().Parse(json, issuerLei, periodEnd);
        if (!facts.Any(fact => fact.ConsolidatedLei == issuerLei))
            throw new InvalidDataException("The JSON report does not prove the required issuer.");

        using var input = new StringReader(json.TrimStart('\uFEFF'));
        using var reader = new JsonTextReader(input)
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 64,
        };
        var root = JObject.Load(reader);
        var namespaces = (JObject)root["documentInfo"]["namespaces"];
        var fragments = new List<string>();
        long totalCharacters = 0;
        foreach (var property in ((JObject)root["facts"]).Properties())
        {
            var fact = (JObject)property.Value;
            if (
                fact["dimensions"] is not JObject dimensions
                || fact["value"]?.Type != JTokenType.String
                || fact["decimals"] != null
                || dimensions.Properties().Any(property =>
                    property.Name is not ("concept" or "entity" or "period" or "language")
                )
                || !MatchesIssuer(dimensions, namespaces, issuerLei)
                || !IsDisclosure(dimensions, namespaces)
                || !MatchesPeriod(dimensions, periodEnd)
            )
                continue;

            var markup = EsefReportContent.PrepareRetrievalMarkup((string)fact["value"]);
            if (string.IsNullOrWhiteSpace(markup))
                continue;
            totalCharacters += markup.Length;
            if (totalCharacters > EsefReportContent.MaxRetrievalHtmlChars)
                return [];
            fragments.Add(markup);
        }

        var output = new StringBuilder();
        foreach (var fragment in fragments)
        {
            var text = EsefReportContent.Build(fragment, normalizer, converter);
            if (text.Length == 0)
                continue;
            if (output.Length > 0)
                output.Append("\n\n---\n\n");
            output.Append(Encoding.UTF8.GetString(text));
        }
        if (fragments.Count > 0 && string.IsNullOrWhiteSpace(output.ToString()))
            throw new InvalidDataException("The tagged report disclosures produced no readable text.");
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    private static bool MatchesIssuer(JObject dimensions, JObject namespaces, string issuerLei)
    {
        var name = QualifiedName(dimensions["entity"]);
        return name != null
            && (string)namespaces[name[0]] == "http://standards.iso.org/iso/17442"
            && name[1] == issuerLei;
    }

    private static bool IsDisclosure(JObject dimensions, JObject namespaces)
    {
        var name = QualifiedName(dimensions["concept"]);
        return name != null
            && name[1].EndsWith("Explanatory", StringComparison.Ordinal)
            && Uri.TryCreate((string)namespaces[name[0]], UriKind.Absolute, out var address)
            && address.Scheme is "http" or "https"
            && address.Host == "xbrl.ifrs.org"
            && address.AbsolutePath.EndsWith("/ifrs-full", StringComparison.Ordinal);
    }

    private static string[] QualifiedName(JToken value)
    {
        if (value?.Type != JTokenType.String)
            return null;
        var parts = ((string)value).Split(':');
        return parts.Length == 2 && parts.All(part => part.Length > 0) ? parts : null;
    }

    private static bool MatchesPeriod(JObject dimensions, DateOnly periodEnd)
    {
        if (dimensions["period"]?.Type != JTokenType.String || periodEnd == DateOnly.MaxValue)
            return false;
        var parts = ((string)dimensions["period"]).Split('/');
        var exclusiveEnd = periodEnd.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (parts.Length is < 1 or > 2 || parts[^1] != exclusiveEnd + "T00:00:00")
            return false;
        return parts.Length == 1
            || (
                parts[0].Length == 19
                && parts[0].EndsWith("T00:00:00", StringComparison.Ordinal)
                && DateOnly.TryParseExact(
                    parts[0][..10],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var start
                ) && start <= periodEnd
            );
    }
}
