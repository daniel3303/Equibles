using System.Globalization;
using System.Xml.Linq;
using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

/// <summary>
/// Extracts financial facts from a <strong>standalone</strong> XBRL instance
/// document — the dedicated <c>.xml</c> artifact older filings ship alongside
/// the human-readable HTML (e.g. <c>aapl-20180929.xml</c>). Resolves
/// <c>contextRef</c> to its <c>xbrli:context</c> (period + any
/// <c>xbrldi:explicitMember</c> dimensions) and <c>unitRef</c> to its
/// <c>xbrli:unit</c>; emits one <see cref="ParsedXbrlFact"/> per numeric fact
/// element under the root <c>xbrli:xbrl</c>.
///
/// <para>
/// <strong>Not wired into the worker pipeline yet.</strong> Persisting raw
/// standalone-XBRL artifacts at ingest time is tracked in GH-1118; until that
/// lands, running this parser at scale would require re-downloading every
/// historical filing on each cycle. The parser ships now as reusable
/// infrastructure with full unit-test coverage; the hosted-service wiring
/// follows GH-1118.
/// </para>
///
/// <para>
/// Scope: numeric facts only (elements with both <c>contextRef</c> and
/// <c>unitRef</c>). Narrative <c>nonNumeric</c> facts, the <c>scale</c>
/// attribute, and <c>typedMember</c> dimensions are out of scope for the
/// first iteration; <c>decimals="INF"</c> resolves to
/// <see cref="int.MaxValue"/>.
/// </para>
/// </summary>
[Service]
public class StandaloneXbrlParser
{
    private const string XbrliNamespace = "http://www.xbrl.org/2003/instance";
    private const string XbrldiNamespace = "http://xbrl.org/2006/xbrldi";
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly XName XbrlRoot = XName.Get("xbrl", XbrliNamespace);
    private static readonly XName ContextElement = XName.Get("context", XbrliNamespace);
    private static readonly XName UnitElement = XName.Get("unit", XbrliNamespace);
    private static readonly XName PeriodElement = XName.Get("period", XbrliNamespace);
    private static readonly XName InstantElement = XName.Get("instant", XbrliNamespace);
    private static readonly XName StartDateElement = XName.Get("startDate", XbrliNamespace);
    private static readonly XName EndDateElement = XName.Get("endDate", XbrliNamespace);
    private static readonly XName MeasureElement = XName.Get("measure", XbrliNamespace);
    private static readonly XName DivideElement = XName.Get("divide", XbrliNamespace);
    private static readonly XName NumeratorElement = XName.Get("unitNumerator", XbrliNamespace);
    private static readonly XName DenominatorElement = XName.Get("unitDenominator", XbrliNamespace);
    private static readonly XName ExplicitMemberElement = XName.Get(
        "explicitMember",
        XbrldiNamespace
    );
    private static readonly XName XsiNil = XName.Get("nil", XsiNamespace);

    public List<ParsedXbrlFact> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var root = document.Root;
        if (root == null || root.Name != XbrlRoot)
            return [];

        var contexts = BuildContextMap(root);
        var units = BuildUnitMap(root);

        var facts = new List<ParsedXbrlFact>();
        foreach (var element in root.Elements())
        {
            if (TryParseFact(element, contexts, units, out var fact))
                facts.Add(fact);
        }

        return facts;
    }

    private static Dictionary<
        string,
        (bool IsInstant, DateOnly Start, DateOnly End, List<ParsedXbrlDimension> Dimensions)
    > BuildContextMap(XElement root)
    {
        var contexts = new Dictionary<
            string,
            (bool, DateOnly, DateOnly, List<ParsedXbrlDimension>)
        >(StringComparer.Ordinal);

        foreach (var contextElement in root.Elements(ContextElement))
        {
            var id = (string)contextElement.Attribute("id");
            if (string.IsNullOrEmpty(id))
                continue;

            var period = contextElement.Element(PeriodElement);
            if (period == null)
                continue;

            if (!TryParsePeriod(period, out var isInstant, out var start, out var end))
                continue;

            var dimensions = ExtractDimensions(contextElement);
            contexts[id] = (isInstant, start, end, dimensions);
        }

        return contexts;
    }

    private static bool TryParsePeriod(
        XElement period,
        out bool isInstant,
        out DateOnly start,
        out DateOnly end
    )
    {
        var instant = period.Element(InstantElement);
        if (instant != null && DateOnly.TryParse(instant.Value, out var instantDate))
        {
            isInstant = true;
            start = instantDate;
            end = instantDate;
            return true;
        }

        var startElement = period.Element(StartDateElement);
        var endElement = period.Element(EndDateElement);
        if (
            startElement != null
            && endElement != null
            && DateOnly.TryParse(startElement.Value, out var startDate)
            && DateOnly.TryParse(endElement.Value, out var endDate)
        )
        {
            isInstant = false;
            start = startDate;
            end = endDate;
            return true;
        }

        isInstant = false;
        start = default;
        end = default;
        return false;
    }

    private static List<ParsedXbrlDimension> ExtractDimensions(XElement contextElement)
    {
        // Per the XBRL spec, explicitMember can appear under entity/segment
        // and/or under scenario — both are valid and both contribute dimensions
        // to the same fact. A descendant scan picks up either placement
        // without depending on the filer's structural choice.
        var dimensions = new List<ParsedXbrlDimension>();
        foreach (var member in contextElement.Descendants(ExplicitMemberElement))
        {
            var axis = (string)member.Attribute("dimension");
            var memberValue = member.Value?.Trim();
            if (string.IsNullOrEmpty(axis) || string.IsNullOrEmpty(memberValue))
                continue;

            dimensions.Add(new ParsedXbrlDimension { Axis = axis, Member = memberValue });
        }

        return dimensions;
    }

    private static Dictionary<string, string> BuildUnitMap(XElement root)
    {
        var units = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var unitElement in root.Elements(UnitElement))
        {
            var id = (string)unitElement.Attribute("id");
            if (string.IsNullOrEmpty(id))
                continue;

            var resolved = ResolveUnit(unitElement);
            if (resolved == null)
                continue;

            units[id] = resolved;
        }

        return units;
    }

    private static string ResolveUnit(XElement unitElement)
    {
        var divide = unitElement.Element(DivideElement);
        if (divide != null)
        {
            var numerator = divide.Element(NumeratorElement);
            var denominator = divide.Element(DenominatorElement);
            var numeratorMeasure = numerator?.Element(MeasureElement)?.Value;
            var denominatorMeasure = denominator?.Element(MeasureElement)?.Value;
            if (string.IsNullOrEmpty(numeratorMeasure) || string.IsNullOrEmpty(denominatorMeasure))
                return null;
            return $"{StripPrefix(numeratorMeasure)}/{StripPrefix(denominatorMeasure)}";
        }

        var measure = unitElement.Element(MeasureElement);
        var measureValue = measure?.Value;
        return string.IsNullOrEmpty(measureValue) ? null : StripPrefix(measureValue);
    }

    private static string StripPrefix(string qname)
    {
        var colonIdx = qname.IndexOf(':');
        return colonIdx >= 0 ? qname.Substring(colonIdx + 1) : qname;
    }

    private static bool TryParseFact(
        XElement element,
        Dictionary<
            string,
            (bool IsInstant, DateOnly Start, DateOnly End, List<ParsedXbrlDimension> Dimensions)
        > contexts,
        Dictionary<string, string> units,
        out ParsedXbrlFact fact
    )
    {
        fact = null;

        // xbrli: namespace elements (context, unit, …) are XBRL machinery, not facts.
        if (element.Name.NamespaceName == XbrliNamespace)
            return false;

        var contextRef = (string)element.Attribute("contextRef");
        var unitRef = (string)element.Attribute("unitRef");
        if (string.IsNullOrEmpty(contextRef) || string.IsNullOrEmpty(unitRef))
            return false;

        var nilAttribute = (string)element.Attribute(XsiNil);
        if (string.Equals(nilAttribute, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!contexts.TryGetValue(contextRef, out var context))
            return false;
        if (!units.TryGetValue(unitRef, out var unit))
            return false;

        if (
            !decimal.TryParse(
                element.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
            return false;

        var taxonomy = element.GetPrefixOfNamespace(element.Name.Namespace);
        if (string.IsNullOrEmpty(taxonomy))
            return false;

        fact = new ParsedXbrlFact
        {
            Taxonomy = taxonomy,
            Tag = element.Name.LocalName,
            Unit = unit,
            Value = value,
            IsInstant = context.IsInstant,
            PeriodStart = context.Start,
            PeriodEnd = context.End,
            Dimensions = context.Dimensions,
            Decimals = ParseDecimals((string)element.Attribute("decimals")),
        };
        return true;
    }

    private static int? ParseDecimals(string decimalsAttribute)
    {
        if (string.IsNullOrEmpty(decimalsAttribute))
            return null;
        if (string.Equals(decimalsAttribute, "INF", StringComparison.OrdinalIgnoreCase))
            return int.MaxValue;
        return int.TryParse(decimalsAttribute, out var value) ? value : null;
    }
}
