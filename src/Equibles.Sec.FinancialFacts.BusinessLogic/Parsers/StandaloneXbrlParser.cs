using System.Globalization;
using System.Xml;
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
/// Fed by the dimensional-fact extraction sweep
/// (<c>XbrlFactsExtractionWorker</c>) from the raw standalone-XBRL artifacts
/// captured on each document at ingest/backfill time (GH-1118) — no filing is
/// re-downloaded to parse it.
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

        try
        {
            if (!TryReadContextsAndUnits(xml, out var contexts, out var units))
                return [];
            return ReadFacts(xml, contexts, units);
        }
        catch (XmlException)
        {
            return [];
        }
    }

    public List<ParsedFiscalYearEnd> ParseFiscalYearEnds(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];
        // Stored SEC TEXT bodies retain this SGML wrapper around the XML instance.
        xml = xml.Trim();
        if (
            xml.StartsWith("<XBRL>", StringComparison.Ordinal)
            && xml.EndsWith("</XBRL>", StringComparison.Ordinal)
        )
            xml = xml[6..^7].Trim();

        var observations = new List<ParsedFiscalYearEnd>();
        try
        {
            if (!TryReadContextsAndUnits(xml, out var contexts, out _))
                return observations;

            using var reader = CreateReader(xml);
            if (!TryMoveToXbrlRoot(reader))
                return observations;

            // Explicit advance for the same reason as ReadFacts: the content read
            // leaves the reader past the element it consumed.
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1)
                {
                    reader.Read();
                    continue;
                }

                if (!TryMatchFiscalYearEnd(reader, contexts, out var context))
                {
                    reader.Skip();
                    continue;
                }

                var rawValue = reader.ReadElementContentAsString();
                if (!FiscalYearEndValueParser.TryParse(rawValue, out var date))
                    continue;
                observations.Add(
                    new ParsedFiscalYearEnd(
                        context.ConsolidatedCik,
                        context.Start,
                        context.End,
                        date.Month,
                        date.Day
                    )
                );
            }
        }
        catch (XmlException)
        {
            return observations;
        }
        return observations;
    }

    // ---- streaming plumbing -------------------------------------------------
    // This parser used to materialise the whole instance as an XDocument. A
    // standalone instance is the largest XML artifact in the pipeline, and a DOM
    // expands multi-MB XML by roughly an order of magnitude, so peak memory
    // scaled with filing size: one oversized instance is enough to OOM the
    // worker (see the XbrlFactsExtraction.Extract row in Errors).
    //
    // Every pass below walks the document with XmlReader instead. Contexts and
    // units are few and small, so each one is still lifted into a throwaway
    // XElement - that keeps the existing LINQ-to-XML logic byte-identical
    // instead of re-deriving it, and the element is released on the next
    // iteration. Facts are the many, repeated part and are never materialised.

    private static XmlReader CreateReader(string xml) =>
        XmlReader.Create(
            new StringReader(xml),
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            }
        );

    /// <summary>
    /// Positions <paramref name="reader"/> on the document element and reports
    /// whether it is <c>xbrli:xbrl</c> — the previous <c>document.Root.Name !=
    /// XbrlRoot</c> guard.
    /// </summary>
    private static bool TryMoveToXbrlRoot(XmlReader reader)
    {
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            return reader.LocalName == XbrlRoot.LocalName
                && reader.NamespaceURI == XbrlRoot.NamespaceName;
        }
        return false;
    }

    /// <summary>
    /// First pass: the document's contexts and units. Returns false when the
    /// document is not an <c>xbrli:xbrl</c> instance, matching the old
    /// root-name guard.
    /// </summary>
    private static bool TryReadContextsAndUnits(
        string xml,
        out Dictionary<string, ParsedContext> contexts,
        out Dictionary<string, string> units
    )
    {
        contexts = new Dictionary<string, ParsedContext>(StringComparer.Ordinal);
        units = new Dictionary<string, string>(StringComparer.Ordinal);

        using var reader = CreateReader(xml);
        if (!TryMoveToXbrlRoot(reader))
            return false;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1)
                continue;
            if (reader.NamespaceURI != XbrliNamespace)
                continue;

            if (reader.LocalName == ContextElement.LocalName)
            {
                using var subtree = reader.ReadSubtree();
                AddContext(contexts, XElement.Load(subtree));
            }
            else if (reader.LocalName == UnitElement.LocalName)
            {
                using var subtree = reader.ReadSubtree();
                AddUnit(units, XElement.Load(subtree));
            }
        }

        return true;
    }

    private static void AddContext(
        Dictionary<string, ParsedContext> contexts,
        XElement contextElement
    )
    {
        var id = (string)contextElement.Attribute("id");
        if (string.IsNullOrEmpty(id))
            return;

        var period = contextElement.Element(PeriodElement);
        if (period == null)
            return;

        if (!TryParsePeriod(period, out var isInstant, out var start, out var end))
            return;

        var dimensions = ExtractDimensions(contextElement);
        var identifier = contextElement
            .Element(XName.Get("entity", XbrliNamespace))
            ?.Element(XName.Get("identifier", XbrliNamespace));
        var unqualified =
            !contextElement.Descendants(XName.Get("segment", XbrliNamespace)).Any()
            && !contextElement.Descendants(XName.Get("scenario", XbrliNamespace)).Any();
        var scheme = (string)identifier?.Attribute("scheme");
        var cik =
            unqualified && scheme == "http://www.sec.gov/CIK" ? identifier?.Value.Trim() : null;
        // A European report states the same unqualified identity under the ISO 17442 scheme.
        var lei =
            unqualified && scheme == "http://standards.iso.org/iso/17442"
                ? identifier?.Value.Trim()
                : null;
        if (contexts.ContainsKey(id))
        {
            cik = null;
            lei = null;
        }
        contexts[id] = new ParsedContext(isInstant, start, end, dimensions, cik, lei);
    }

    private static bool TryParsePeriod(
        XElement period,
        out bool isInstant,
        out DateOnly start,
        out DateOnly end
    )
    {
        var instant = period.Element(InstantElement);
        if (instant != null && TryParseInvariantDate(instant.Value, out var instantDate))
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
            && TryParseInvariantDate(startElement.Value, out var startDate)
            && TryParseInvariantDate(endElement.Value, out var endDate)
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

    private static bool TryParseInvariantDate(string value, out DateOnly date) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

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

    private static void AddUnit(Dictionary<string, string> units, XElement unitElement)
    {
        var id = (string)unitElement.Attribute("id");
        if (string.IsNullOrEmpty(id))
            return;

        var resolved = ResolveUnit(unitElement);
        if (resolved == null)
            return;

        units[id] = resolved;
    }

    private static string ResolveUnit(XElement unitElement)
    {
        var divide = unitElement.Element(DivideElement);
        if (divide != null)
        {
            var numerator = divide.Element(NumeratorElement);
            var denominator = divide.Element(DenominatorElement);
            var numeratorMeasure = numerator?.Element(MeasureElement)?.Value?.Trim();
            var denominatorMeasure = denominator?.Element(MeasureElement)?.Value?.Trim();
            if (string.IsNullOrEmpty(numeratorMeasure) || string.IsNullOrEmpty(denominatorMeasure))
                return null;
            var numeratorLocal = XbrlValueParser.StripPrefix(numeratorMeasure);
            var denominatorLocal = XbrlValueParser.StripPrefix(denominatorMeasure);
            if (numeratorLocal == null || denominatorLocal == null)
                return null;
            return $"{numeratorLocal}/{denominatorLocal}";
        }

        var measure = unitElement.Element(MeasureElement);
        var measureValue = measure?.Value?.Trim();
        if (string.IsNullOrEmpty(measureValue))
            return null;
        return XbrlValueParser.StripPrefix(measureValue);
    }

    /// <summary>
    /// Second pass: one <see cref="ParsedXbrlFact"/> per numeric fact element
    /// under the root. Same accept/reject rules as the old XElement walk — the
    /// only mechanical difference is that the element's value is read from the
    /// reader instead of from a materialised tree, which advances the reader
    /// past the element, so every attribute-derived decision is made first.
    /// </summary>
    /// <summary>
    /// True when the depth-1 element at the reader is the SEC dei
    /// <c>CurrentFiscalYearEndDate</c> and its context carries a consolidated CIK.
    /// Returns false without advancing the reader.
    /// </summary>
    private static bool TryMatchFiscalYearEnd(
        XmlReader reader,
        Dictionary<string, ParsedContext> contexts,
        out ParsedContext context
    )
    {
        context = default;
        if (reader.LocalName != "CurrentFiscalYearEndDate")
            return false;
        if (!Uri.TryCreate(reader.NamespaceURI, UriKind.Absolute, out var ns))
            return false;
        if (
            ns.Host != "xbrl.sec.gov"
            || !ns.AbsolutePath.StartsWith("/dei/", StringComparison.Ordinal)
        )
            return false;
        var nil = reader.GetAttribute(XsiNil.LocalName, XsiNil.NamespaceName);
        if (nil is "true" or "1")
            return false;
        var contextRef = reader.GetAttribute("contextRef") ?? "";
        if (!contexts.TryGetValue(contextRef, out context))
            return false;
        if (string.IsNullOrWhiteSpace(context.ConsolidatedCik))
            return false;
        return true;
    }

    private static List<ParsedXbrlFact> ReadFacts(
        string xml,
        Dictionary<string, ParsedContext> contexts,
        Dictionary<string, string> units
    )
    {
        var facts = new List<ParsedXbrlFact>();

        using var reader = CreateReader(xml);
        if (!TryMoveToXbrlRoot(reader))
            return facts;

        // NOT `while (reader.Read())`. TryReadFact consumes a fact element with
        // ReadElementContentAsString, which leaves the reader on the node AFTER
        // that element - so a Read() at the top of every iteration skips whatever
        // follows, which on a fact-dense document is the next fact. Measured on a
        // 2000-fact fixture: 1000 facts came back, every other one dropped.
        // Advance explicitly instead: fact elements advance themselves, and
        // anything rejected is stepped over with Skip().
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1)
            {
                reader.Read();
                continue;
            }

            if (!TryReadFact(reader, contexts, units, out var fact))
            {
                reader.Skip();
                continue;
            }
            facts.Add(fact);
        }

        return facts;
    }

    /// <summary>
    /// Consumes one depth-1 element if it is a numeric fact. Returns false WITHOUT
    /// advancing the reader (the caller decides how to step over it), and true
    /// having read the element's content — which leaves the reader past it.
    /// </summary>
    private static bool TryReadFact(
        XmlReader reader,
        Dictionary<string, ParsedContext> contexts,
        Dictionary<string, string> units,
        out ParsedXbrlFact fact
    )
    {
        fact = null;

        // xbrli: namespace elements (context, unit, …) are XBRL machinery, not facts.
        if (reader.NamespaceURI == XbrliNamespace)
            return false;

        var contextRef = reader.GetAttribute("contextRef");
        var unitRef = reader.GetAttribute("unitRef");
        if (string.IsNullOrEmpty(contextRef) || string.IsNullOrEmpty(unitRef))
            return false;

        var nilAttribute = reader.GetAttribute(XsiNil.LocalName, XsiNil.NamespaceName);
        if (string.Equals(nilAttribute, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!contexts.TryGetValue(contextRef, out var context))
            return false;
        if (!units.TryGetValue(unitRef, out var unit))
            return false;

        // The element's own prefix is the taxonomy alias. XDocument's
        // GetPrefixOfNamespace walked up to the declaration; XmlReader's Prefix is
        // the prefix actually used on this element, the same alias for any document
        // that declares it in scope. An unprefixed (default-namespace) element is
        // rejected in both.
        var taxonomy = reader.Prefix;
        if (string.IsNullOrEmpty(taxonomy))
            return false;

        // Everything derivable from attributes must be captured before the content
        // read, which advances the reader past the element.
        var decimals = XbrlValueParser.ParseDecimals(reader.GetAttribute("decimals"));
        var tag = reader.LocalName;
        var elementNamespace = reader.NamespaceURI;
        var rawValue = reader.ReadElementContentAsString();
        if (
            !decimal.TryParse(
                rawValue,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value
            )
        )
            return false;

        fact = new ParsedXbrlFact
        {
            Taxonomy = taxonomy,
            Tag = tag,
            Namespace = elementNamespace,
            Unit = unit,
            Value = value,
            IsInstant = context.IsInstant,
            PeriodStart = context.Start,
            PeriodEnd = context.End,
            Dimensions = context.Dimensions,
            ConsolidatedCik = context.ConsolidatedCik,
            ConsolidatedLei = context.ConsolidatedLei,
            Decimals = decimals,
        };
        return true;
    }
}
