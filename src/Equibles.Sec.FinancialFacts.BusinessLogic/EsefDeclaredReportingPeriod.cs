using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

internal static class EsefDeclaredReportingPeriod
{
    private const string Instance = "http://www.xbrl.org/2003/instance";
    private const string DanishGeneral = "http://xbrl.dcca.dk/gsd";
    private const string StartField = "ReportingPeriodStartDate";
    private const string EndField = "ReportingPeriodEndDate";

    // Report-level dates can use a qualified cover context. They constrain annual admission;
    // they never establish consolidated financial evidence or replace the numeric proof.
    internal static bool AllowsAnnual(string html, string issuerLei, DateOnly periodEnd)
    {
        var contexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new List<(string Field, string Context, string Value)>();
        try
        {
            using var input = new StringReader(html.TrimStart('\uFEFF'));
            using var reader = XmlReader.Create(
                input,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null,
                    MaxCharactersInDocument = 300 * 1024 * 1024,
                }
            );
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.NamespaceURI == Instance && reader.LocalName == "context")
                {
                    ReadContext(reader, contexts);
                    continue;
                }
                if (
                    reader.LocalName != "nonNumeric"
                    || reader.NamespaceURI
                        is not (
                            "http://www.xbrl.org/2013/inlineXBRL"
                            or "http://www.xbrl.org/2008/inlineXBRL"
                        )
                )
                    continue;
                var name = reader.GetAttribute("name")?.Split(':');
                if (
                    name?.Length != 2
                    || reader.LookupNamespace(name[0]) != DanishGeneral
                    || name[1] is not (StartField or EndField)
                )
                    continue;
                var context = reader.GetAttribute("contextRef");
                var unsupported =
                    reader.GetAttribute("continuedAt") != null
                    || reader.GetAttribute("format") != null
                    || reader.GetAttribute("nil", "http://www.w3.org/2001/XMLSchema-instance") is "true" or "1";
                using var subtree = reader.ReadSubtree();
                var element = XElement.Load(subtree);
                declarations.Add(
                    (name[1], context, unsupported || element.HasElements ? null : element.Value)
                );
            }
        }
        catch (XmlException)
        {
            return false;
        }

        var dates = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (
                declaration.Context == null
                || !contexts.TryGetValue(declaration.Context, out var owner)
                || owner == null
            )
                return false;
            if (!string.Equals(owner, issuerLei, StringComparison.OrdinalIgnoreCase))
                continue;
            if (
                !DateOnly.TryParseExact(
                    declaration.Value?.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
                || (dates.TryGetValue(declaration.Field, out var previous) && previous != date)
            )
                return false;
            dates[declaration.Field] = date;
        }
        if (dates.Count == 0)
            return true;
        return dates.TryGetValue(StartField, out var start)
            && dates.TryGetValue(EndField, out var end)
            && end == periodEnd
            && end.DayNumber - start.DayNumber is >= 350 and <= 380;
    }

    private static void ReadContext(XmlReader reader, Dictionary<string, string> contexts)
    {
        using var subtree = reader.ReadSubtree();
        var context = XElement.Load(subtree);
        var id = (string)context.Attribute("id");
        if (string.IsNullOrEmpty(id))
            return;
        var identifiers = context
            .Elements(XName.Get("entity", Instance))
            .SelectMany(entity => entity.Elements(XName.Get("identifier", Instance)))
            .ToList();
        var owner =
            identifiers.Count == 1
            && (string)identifiers[0].Attribute("scheme") == "http://standards.iso.org/iso/17442"
                ? identifiers[0].Value.Trim()
                : null;
        contexts[id] = contexts.ContainsKey(id) || string.IsNullOrWhiteSpace(owner) ? null : owner;
    }
}
