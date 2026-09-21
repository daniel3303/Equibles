using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

[Service]
public class JsonXbrlParser
{
    private const string DocumentType = "https://xbrl.org/2021/xbrl-json";
    private const string LeiNamespace = "http://standards.iso.org/iso/17442";
    private const string CurrencyNamespace = "http://www.xbrl.org/2003/iso4217";
    private const string InstanceNamespace = "http://www.xbrl.org/2003/instance";
    private static readonly Regex DecimalValue = new(
        @"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    public List<ParsedXbrlFact> Parse(
        string json,
        string requiredIssuer = null,
        DateOnly? requiredPeriodEnd = null
    )
    {
        using var input = new StringReader(json.TrimStart('\uFEFF'));
        using var reader = new JsonTextReader(input)
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 64,
        };
        var root = JObject.Load(
            reader,
            new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            }
        );
        if (reader.Read())
            throw new JsonReaderException("Trailing content in xBRL-JSON report.");
        if (
            root["documentInfo"] is not JObject info
            || Text(info["documentType"]) != DocumentType
            || info["namespaces"] is not JObject namespaces
            || info["taxonomy"] is not JArray taxonomy
            || taxonomy.Count == 0
            || root["facts"] is not JObject facts
        )
            throw new JsonReaderException("Unsupported xBRL-JSON report structure.");

        var result = new List<ParsedXbrlFact>();
        foreach (var property in facts.Properties())
        {
            if (property.Value is not JObject fact)
                throw new JsonReaderException("Invalid xBRL-JSON fact structure.");
            var parsed = ParseFact(fact, namespaces);
            if (parsed != null)
                result.Add(parsed);
        }
        var indexedPeriodEnd = requiredPeriodEnd.GetValueOrDefault();
        if (
            requiredPeriodEnd.HasValue
            && result.Any(fact =>
                fact.ConsolidatedLei == requiredIssuer && fact.PeriodEnd > indexedPeriodEnd
            )
        )
            throw new InvalidDataException(
                "The structured report contains later issuer periods than the indexed report."
            );
        var resolved = result
            .GroupBy(fact =>
                (
                    fact.ConsolidatedLei,
                    fact.Taxonomy,
                    fact.Tag,
                    fact.Unit,
                    fact.PeriodStart,
                    fact.PeriodEnd
                )
            )
            .Where(group => group.Select(fact => fact.Value).Distinct().Take(2).Count() == 1)
            .Select(group => group.OrderByDescending(fact => fact.Decimals).First())
            .ToList();
        if (
            requiredPeriodEnd.HasValue
            && !resolved.Any(fact =>
                fact.ConsolidatedLei == requiredIssuer && fact.PeriodEnd == indexedPeriodEnd
            )
        )
            throw new InvalidDataException(
                "The structured report has no conflict-free issuer facts at the indexed period."
            );
        return resolved;
    }

    private static ParsedXbrlFact ParseFact(JObject fact, JObject namespaces)
    {
        if (
            fact["dimensions"] is not JObject dimensions
            || dimensions
                .Properties()
                .Any(property => property.Name is not ("concept" or "entity" or "period" or "unit"))
            || !Resolve(
                Text(dimensions["concept"]),
                namespaces,
                out var prefix,
                out var tag,
                out var conceptNamespace
            )
            || prefix != "ifrs-full"
            || !Uri.TryCreate(conceptNamespace, UriKind.Absolute, out var conceptUri)
            || conceptUri.Host != "xbrl.ifrs.org"
            || !Resolve(
                Text(dimensions["entity"]),
                namespaces,
                out _,
                out var lei,
                out var entityNamespace,
                false
            )
            || entityNamespace != LeiNamespace
            || lei.Length != 20
            || !lei.All(character =>
                char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character)
            )
            || !TryValue(Text(fact["value"]), out var value)
            || !TryPeriod(Text(dimensions["period"]), out var start, out var end, out var instant)
        )
            return null;

        var unit = Unit(Text(dimensions["unit"]), namespaces);
        if (unit == null)
            return null;
        var decimals = int.MaxValue;
        if (
            fact.TryGetValue("decimals", out var precision)
            && (
                precision.Type != JTokenType.Integer
                || !int.TryParse(
                    precision.ToString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out decimals
                )
            )
        )
            return null;
        return new ParsedXbrlFact
        {
            ConsolidatedLei = lei,
            Taxonomy = prefix,
            Tag = tag,
            Namespace = conceptNamespace,
            Unit = unit,
            Value = value,
            IsInstant = instant,
            PeriodStart = start,
            PeriodEnd = end,
            Decimals = decimals,
        };
    }

    private static bool TryValue(string text, out decimal value)
    {
        value = default;
        if (text == null || text.Length > 128 || !DecimalValue.IsMatch(text))
            return false;
        var unsigned = text.TrimStart('+', '-');
        var point = unsigned.IndexOf('.');
        var fraction = point < 0 ? "" : unsigned[(point + 1)..].TrimEnd('0');
        var significant = unsigned.Replace(".", "").TrimStart('0');
        if (point >= 0)
            significant = significant.TrimEnd('0');
        return fraction.Length <= 28
            && significant.Length <= 28
            && decimal.TryParse(
                text,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value
            );
    }

    private static bool TryPeriod(
        string text,
        out DateOnly start,
        out DateOnly end,
        out bool instant
    )
    {
        start = end = default;
        instant = false;
        var parts = text?.Split('/');
        if (
            parts == null
            || parts.Length is < 1 or > 2
            || !Midnight(parts[^1], out var exclusiveEnd)
            || exclusiveEnd == DateOnly.MinValue
        )
            return false;
        end = exclusiveEnd.AddDays(-1);
        instant = parts.Length == 1;
        if (instant)
            start = end;
        else if (!Midnight(parts[0], out start))
            return false;
        return start <= end;
    }

    private static bool Midnight(string text, out DateOnly date)
    {
        date = default;
        return text.Length == 19
            && text.EndsWith("T00:00:00", StringComparison.Ordinal)
            && DateOnly.TryParseExact(
                text[..10],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date
            );
    }

    private static string Unit(string text, JObject namespaces)
    {
        var parts = text?.Split('/');
        if (parts == null || parts.Length is < 1 or > 2)
            return null;
        var numerator = Measure(parts[0], namespaces);
        if (parts.Length == 1)
            return numerator;
        return
            numerator is not (null or "shares" or "pure")
            && Measure(parts[1], namespaces) == "shares"
            ? numerator + "/shares"
            : null;
    }

    private static string Measure(string text, JObject namespaces)
    {
        if (!Resolve(text, namespaces, out _, out var local, out var uri))
            return null;
        if (uri == CurrencyNamespace && local.Length == 3 && local.All(char.IsAsciiLetterUpper))
            return local;
        return uri == InstanceNamespace && local is "shares" or "pure" ? local : null;
    }

    private static bool Resolve(
        string text,
        JObject namespaces,
        out string prefix,
        out string local,
        out string uri,
        bool verifyLocal = true
    )
    {
        prefix = local = uri = null;
        var parts = text?.Split(':');
        if (parts == null || parts.Length != 2 || parts.Any(string.IsNullOrEmpty))
            return false;
        try
        {
            XmlConvert.VerifyNCName(parts[0]);
            if (verifyLocal)
                XmlConvert.VerifyNCName(parts[1]);
        }
        catch (XmlException)
        {
            return false;
        }
        prefix = parts[0];
        local = parts[1];
        uri = Text(namespaces[prefix]);
        return uri != null;
    }

    private static string Text(JToken token) =>
        token?.Type == JTokenType.String ? token.Value<string>() : null;
}
