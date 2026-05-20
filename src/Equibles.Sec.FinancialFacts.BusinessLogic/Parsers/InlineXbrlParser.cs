using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Equibles.Core.AutoWiring;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

/// <summary>
/// Extracts financial facts from an <strong>inline XBRL (iXBRL)</strong>
/// document — the primary <c>.htm</c> filings post the 2019–2021 phase-in
/// embed in the SGML envelope. Walks <c>ix:nonFraction</c> elements, resolves
/// each one's <c>contextRef</c> to its <c>xbrli:context</c> (period +
/// <c>xbrldi:explicitMember</c> dimensions) and <c>unitRef</c> to its
/// <c>xbrli:unit</c>, then decodes the human-readable value text (thousands
/// separators, parenthesised negatives, dashes-as-zero) and applies the
/// iXBRL <c>scale</c> / <c>sign</c> attributes.
///
/// <para>
/// <strong>Not wired into the worker pipeline yet.</strong> The contexts /
/// units / fact elements live inside <c>ix:header</c>, which
/// <see cref="Equibles.Sec.BusinessLogic.Normalizers.XbrlStripStep"/> deletes
/// before normalisation reaches downstream consumers. The wiring requires
/// either (a) running this parser on the raw envelope <em>before</em>
/// stripping, or (b) persisting the raw envelope for later extraction —
/// see GH-1118. Until that lands, this parser ships as library-only
/// infrastructure with unit-test coverage.
/// </para>
///
/// <para>
/// Scope: <c>ix:nonFraction</c> (numeric) only. Narrative
/// <c>ix:nonNumeric</c>, <c>continuation</c> elements, fragmented values,
/// and <c>typedMember</c> dimensions are out of scope for this first
/// iteration; <c>decimals="INF"</c> resolves to <see cref="int.MaxValue"/>.
/// </para>
/// </summary>
[Service]
public class InlineXbrlParser
{
    private const string NonFractionLocalName = "ix:nonfraction";
    private const string ContextLocalName = "xbrli:context";
    private const string UnitLocalName = "xbrli:unit";
    private const string PeriodLocalName = "xbrli:period";
    private const string InstantLocalName = "xbrli:instant";
    private const string StartDateLocalName = "xbrli:startdate";
    private const string EndDateLocalName = "xbrli:enddate";
    private const string MeasureLocalName = "xbrli:measure";
    private const string DivideLocalName = "xbrli:divide";
    private const string NumeratorLocalName = "xbrli:unitnumerator";
    private const string DenominatorLocalName = "xbrli:unitdenominator";
    private const string ExplicitMemberLocalName = "xbrldi:explicitmember";

    private readonly HtmlParser _parser = new(
        new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
    );

    public List<ParsedXbrlFact> Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var document = _parser.ParseDocument(html);
        if (document?.DocumentElement == null)
            return [];

        var contexts = BuildContextMap(document);
        var units = BuildUnitMap(document);

        var facts = new List<ParsedXbrlFact>();
        foreach (var element in FindByLocalName(document, NonFractionLocalName))
        {
            if (TryParseFact(element, contexts, units, out var fact))
                facts.Add(fact);
        }
        return facts;
    }

    private static IEnumerable<IElement> FindByLocalName(IParentNode root, string localName) =>
        root.QuerySelectorAll("*")
            .Where(e => string.Equals(e.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<
        string,
        (bool IsInstant, DateOnly Start, DateOnly End, List<ParsedXbrlDimension> Dimensions)
    > BuildContextMap(IDocument document)
    {
        var contexts = new Dictionary<
            string,
            (bool, DateOnly, DateOnly, List<ParsedXbrlDimension>)
        >(StringComparer.Ordinal);

        foreach (var contextElement in FindByLocalName(document, ContextLocalName))
        {
            var id = contextElement.GetAttribute("id");
            if (string.IsNullOrEmpty(id))
                continue;

            var period = FindFirstChildByLocalName(contextElement, PeriodLocalName);
            if (period == null)
                continue;

            if (!TryParsePeriod(period, out var isInstant, out var start, out var end))
                continue;

            var dimensions = ExtractDimensions(contextElement);
            contexts[id] = (isInstant, start, end, dimensions);
        }

        return contexts;
    }

    private static IElement FindFirstChildByLocalName(IElement parent, string localName) =>
        parent.Children.FirstOrDefault(c =>
            string.Equals(c.LocalName, localName, StringComparison.OrdinalIgnoreCase)
        );

    private static bool TryParsePeriod(
        IElement period,
        out bool isInstant,
        out DateOnly start,
        out DateOnly end
    )
    {
        var instant = FindFirstChildByLocalName(period, InstantLocalName);
        if (instant != null && DateOnly.TryParse(instant.TextContent.Trim(), out var instantDate))
        {
            isInstant = true;
            start = instantDate;
            end = instantDate;
            return true;
        }

        var startElement = FindFirstChildByLocalName(period, StartDateLocalName);
        var endElement = FindFirstChildByLocalName(period, EndDateLocalName);
        if (
            startElement != null
            && endElement != null
            && DateOnly.TryParse(startElement.TextContent.Trim(), out var startDate)
            && DateOnly.TryParse(endElement.TextContent.Trim(), out var endDate)
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

    private static List<ParsedXbrlDimension> ExtractDimensions(IElement contextElement)
    {
        var dimensions = new List<ParsedXbrlDimension>();
        foreach (var member in FindByLocalName(contextElement, ExplicitMemberLocalName))
        {
            var axis = member.GetAttribute("dimension");
            var memberValue = member.TextContent?.Trim();
            if (string.IsNullOrEmpty(axis) || string.IsNullOrEmpty(memberValue))
                continue;
            dimensions.Add(new ParsedXbrlDimension { Axis = axis, Member = memberValue });
        }
        return dimensions;
    }

    private static Dictionary<string, string> BuildUnitMap(IDocument document)
    {
        var units = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var unitElement in FindByLocalName(document, UnitLocalName))
        {
            var id = unitElement.GetAttribute("id");
            if (string.IsNullOrEmpty(id))
                continue;
            var resolved = ResolveUnit(unitElement);
            if (resolved == null)
                continue;
            units[id] = resolved;
        }
        return units;
    }

    private static string ResolveUnit(IElement unitElement)
    {
        var divide = FindFirstChildByLocalName(unitElement, DivideLocalName);
        if (divide != null)
        {
            var numerator = FindFirstChildByLocalName(divide, NumeratorLocalName);
            var denominator = FindFirstChildByLocalName(divide, DenominatorLocalName);
            var numeratorMeasure =
                numerator == null
                    ? null
                    : FindFirstChildByLocalName(numerator, MeasureLocalName)?.TextContent;
            var denominatorMeasure =
                denominator == null
                    ? null
                    : FindFirstChildByLocalName(denominator, MeasureLocalName)?.TextContent;
            if (string.IsNullOrEmpty(numeratorMeasure) || string.IsNullOrEmpty(denominatorMeasure))
                return null;
            return $"{StripPrefix(numeratorMeasure.Trim())}/{StripPrefix(denominatorMeasure.Trim())}";
        }

        var measure = FindFirstChildByLocalName(unitElement, MeasureLocalName);
        var measureValue = measure?.TextContent?.Trim();
        return string.IsNullOrEmpty(measureValue) ? null : StripPrefix(measureValue);
    }

    private static string StripPrefix(string qname)
    {
        var colonIdx = qname.IndexOf(':');
        return colonIdx >= 0 ? qname.Substring(colonIdx + 1) : qname;
    }

    private static bool TryParseFact(
        IElement element,
        Dictionary<
            string,
            (bool IsInstant, DateOnly Start, DateOnly End, List<ParsedXbrlDimension> Dimensions)
        > contexts,
        Dictionary<string, string> units,
        out ParsedXbrlFact fact
    )
    {
        fact = null;

        var contextRef = element.GetAttribute("contextRef") ?? element.GetAttribute("contextref");
        var unitRef = element.GetAttribute("unitRef") ?? element.GetAttribute("unitref");
        var name = element.GetAttribute("name");
        if (
            string.IsNullOrEmpty(contextRef)
            || string.IsNullOrEmpty(unitRef)
            || string.IsNullOrEmpty(name)
        )
            return false;

        // xsi:nil="true" appears as a flat attribute in HTML-parsed iXBRL —
        // AngleSharp doesn't preserve the xsi: prefix on attributes the way
        // it does on elements, so check both shapes defensively.
        var nilAttribute = element.GetAttribute("xsi:nil") ?? element.GetAttribute("nil");
        if (string.Equals(nilAttribute, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        var colonIdx = name.IndexOf(':');
        if (colonIdx <= 0 || colonIdx >= name.Length - 1)
            return false;
        var taxonomy = name.Substring(0, colonIdx);
        var tag = name.Substring(colonIdx + 1);

        if (!contexts.TryGetValue(contextRef, out var context))
            return false;
        if (!units.TryGetValue(unitRef, out var unit))
            return false;

        if (!TryDecodeValue(element, out var value))
            return false;

        fact = new ParsedXbrlFact
        {
            Taxonomy = taxonomy,
            Tag = tag,
            Unit = unit,
            Value = value,
            IsInstant = context.IsInstant,
            PeriodStart = context.Start,
            PeriodEnd = context.End,
            Dimensions = context.Dimensions,
            Decimals = ParseDecimals(element.GetAttribute("decimals")),
        };
        return true;
    }

    private static bool TryDecodeValue(IElement element, out decimal value)
    {
        value = 0m;

        var raw = element.TextContent?.Trim();
        if (string.IsNullOrEmpty(raw))
            return false;

        var format = element.GetAttribute("format") ?? string.Empty;

        // Format hints that the value is a hard-coded zero (numdash → "—",
        // fixed-zero → literal 0). The Inline XBRL Transformations spec
        // emits these for placeholders; consumers expect 0, not a parse
        // failure on the dash glyph.
        if (
            format.EndsWith("numdash", StringComparison.OrdinalIgnoreCase)
            || format.EndsWith("fixed-zero", StringComparison.OrdinalIgnoreCase)
            || format.EndsWith("fixedzero", StringComparison.OrdinalIgnoreCase)
        )
        {
            return TryApplyScaleAndSign(0m, element, out value);
        }

        var parenthesised = raw.StartsWith('(') && raw.EndsWith(')');
        if (parenthesised)
            raw = raw.Substring(1, raw.Length - 2).Trim();

        var commaDecimal = format.EndsWith("numcommadecimal", StringComparison.OrdinalIgnoreCase);

        var cleaned = NormaliseDigits(raw, commaDecimal);
        if (string.IsNullOrEmpty(cleaned))
            return false;

        if (
            !decimal.TryParse(
                cleaned,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
            return false;

        if (parenthesised)
            parsed = -parsed;

        return TryApplyScaleAndSign(parsed, element, out value);
    }

    private static string NormaliseDigits(string raw, bool commaDecimal)
    {
        // Strip currency / spacing glyphs filers routinely use ($, NBSP,
        // narrow NBSP, thin spaces). Then collapse thousands separators
        // and align the decimal separator to '.' so InvariantCulture
        // parsing succeeds regardless of the document's locale.
        var buffer = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            switch (ch)
            {
                case ' ':
                case ' ':
                case ' ':
                case ' ':
                case '$':
                case '€':
                case '£':
                case '¥':
                    continue;
            }
            buffer.Append(ch);
        }
        var stripped = buffer.ToString();

        if (commaDecimal)
        {
            stripped = stripped.Replace(".", string.Empty).Replace(',', '.');
        }
        else
        {
            stripped = stripped.Replace(",", string.Empty);
        }

        return stripped;
    }

    private static bool TryApplyScaleAndSign(decimal parsed, IElement element, out decimal value)
    {
        value = 0m;
        var scaleAttribute = element.GetAttribute("scale");
        if (!string.IsNullOrEmpty(scaleAttribute) && int.TryParse(scaleAttribute, out var scale))
        {
            // Positive scale shifts the value up; negative shifts down.
            // We use Pow over a literal multiplier so non-trivial scales
            // (e.g. 6 for "in millions") survive without precision loss.
            // A scale large enough to drive Math.Pow(10, scale) — or the
            // subsequent multiply — past decimal.MaxValue (~7.92e28) is
            // malformed input; drop the fact instead of aborting the parse.
            try
            {
                parsed *= (decimal)Math.Pow(10, scale);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        var signAttribute = element.GetAttribute("sign");
        if (string.Equals(signAttribute, "-", StringComparison.Ordinal))
        {
            parsed = -parsed;
        }

        value = parsed;
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
