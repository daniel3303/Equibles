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
/// Fed by the dimensional-fact extraction sweep
/// (<c>XbrlFactsExtractionWorker</c>): the contexts / units / fact elements
/// live inside <c>ix:header</c>, which
/// <see cref="Equibles.Sec.BusinessLogic.Normalizers.XbrlStripStep"/> deletes
/// during normalisation, so the sweep parses the raw envelope captured on the
/// document at ingest/backfill time (GH-1118) instead of the normalised HTML.
/// </para>
///
/// <para>
/// Scope: <c>ix:nonFraction</c> (numeric) facts, plus exactly one narrative
/// use of <c>ix:nonNumeric</c> — the three cover-page 12(b) registration tags
/// (<c>dei:Security12bTitle</c> / <c>dei:TradingSymbol</c> /
/// <c>dei:SecurityExchangeName</c>), whose short text values pair into
/// security listings by shared context. General narrative text,
/// <c>continuation</c> elements, fragmented values, and <c>typedMember</c>
/// dimensions remain out of scope; <c>decimals="INF"</c> resolves to
/// <see cref="int.MaxValue"/>.
/// </para>
/// </summary>
[Service]
public class InlineXbrlParser
{
    private const string NonFractionLocalName = "ix:nonfraction";
    private const string NonNumericLocalName = "ix:nonnumeric";

    // The cover-page 12(b) registration tags. The only ix:nonNumeric names this
    // parser reads — a whitelist, so narrative text blocks are never touched.
    private const string Security12bTitleName = "dei:Security12bTitle";
    private const string TradingSymbolName = "dei:TradingSymbol";
    private const string SecurityExchangeName = "dei:SecurityExchangeName";
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

    // Whitespace glyphs filers insert around figures; named because they are
    // visually indistinguishable from a regular space in source.
    private const char NoBreakSpace = '\u00A0';
    private const char NarrowNoBreakSpace = '\u202F';
    private const char ThinSpace = '\u2009';

    // Apostrophe thousands grouping (Swiss style, the TR4+ "-apos" transform
    // variants); the typographic right single quote is its common rendering.
    private const char Apostrophe = '\'';
    private const char RightSingleQuote = '\u2019';

    private readonly HtmlParser _parser = new(
        new HtmlParserOptions { IsAcceptingCustomElementsEverywhere = true }
    );

    public List<ParsedXbrlFact> Parse(string html) => ParseEnvelope(html).Facts;

    /// <summary>
    /// One pass over the envelope yielding both the numeric facts and the
    /// cover-page 12(b) listings — the document is DOM-parsed once for both.
    /// </summary>
    public InlineXbrlParseResult ParseEnvelope(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new InlineXbrlParseResult();

        var document = _parser.ParseDocument(html);
        if (document?.DocumentElement == null)
            return new InlineXbrlParseResult();

        var contexts = BuildContextMap(document);
        var units = BuildUnitMap(document);
        var namespaces = BuildNamespaceMap(document);

        var facts = new List<ParsedXbrlFact>();
        foreach (var element in FindByLocalName(document, NonFractionLocalName))
        {
            if (TryParseFact(element, contexts, units, namespaces, out var fact))
                facts.Add(fact);
        }
        return new InlineXbrlParseResult
        {
            Facts = facts,
            CoverListings = ExtractCoverListings(document),
        };
    }

    /// <summary>
    /// Pairs the cover page's 12(b) registration facts into listings. Each
    /// registered security's title / symbol / exchange share one XBRL context
    /// (the per-security member on a class-of-stock axis, or the dimensionless
    /// context when only one security is registered), so grouping the three
    /// whitelisted <c>ix:nonNumeric</c> tags by <c>contextRef</c> reassembles
    /// the table's rows without resolving contexts at all. Only groups that
    /// carry a title become listings; a repeated rendering of the same fact in
    /// a context keeps the first non-empty value.
    /// </summary>
    private static List<ParsedSecurityListing> ExtractCoverListings(IDocument document)
    {
        var byContext = new Dictionary<string, ParsedSecurityListing>(StringComparer.Ordinal);
        var contextOrder = new List<string>();

        foreach (var element in FindByLocalName(document, NonNumericLocalName))
        {
            var name = element.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                continue;

            var isTitle = string.Equals(
                name,
                Security12bTitleName,
                StringComparison.OrdinalIgnoreCase
            );
            var isSymbol = string.Equals(
                name,
                TradingSymbolName,
                StringComparison.OrdinalIgnoreCase
            );
            var isExchange = string.Equals(
                name,
                SecurityExchangeName,
                StringComparison.OrdinalIgnoreCase
            );
            if (!isTitle && !isSymbol && !isExchange)
                continue;

            var contextRef =
                element.GetAttribute("contextRef") ?? element.GetAttribute("contextref");
            if (string.IsNullOrEmpty(contextRef))
                continue;

            var text = CollapseWhitespace(element.TextContent);
            if (string.IsNullOrEmpty(text))
                continue;

            if (!byContext.TryGetValue(contextRef, out var listing))
            {
                listing = new ParsedSecurityListing();
                byContext[contextRef] = listing;
                contextOrder.Add(contextRef);
            }

            if (isTitle)
                listing.Title ??= text;
            else if (isSymbol)
                listing.TradingSymbol ??= text;
            else
                listing.ExchangeName ??= text;
        }

        return contextOrder
            .Select(id => byContext[id])
            .Where(listing => listing.Title != null)
            .ToList();
    }

    // Cover-page text values are short strings, but escaped titles can carry
    // nested markup whose TextContent folds in newlines and runs of spaces.
    private static string CollapseWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return string.Join(' ', value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Prefix → namespace URI map from every <c>xmlns:*</c> declaration in the
    /// document (filers declare them on <c>html</c>, but nothing forbids deeper
    /// placement). Case-insensitive because the HTML parser lowercases attribute
    /// names while <c>name</c> attribute values keep the author's prefix casing.
    /// Duplicate declarations keep the first URI seen — scoped redefinition of a
    /// taxonomy prefix does not occur in EDGAR filings.
    /// </summary>
    private static Dictionary<string, string> BuildNamespaceMap(IDocument document)
    {
        var namespaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.All)
        {
            foreach (var attribute in element.Attributes)
            {
                if (!attribute.Name.StartsWith("xmlns:", StringComparison.OrdinalIgnoreCase))
                    continue;
                var prefix = attribute.Name.Substring("xmlns:".Length);
                if (prefix.Length == 0 || string.IsNullOrEmpty(attribute.Value))
                    continue;
                namespaces.TryAdd(prefix, attribute.Value.Trim());
            }
        }
        return namespaces;
    }

    private static IEnumerable<IElement> FindByLocalName(IParentNode root, string localName) =>
        root.QuerySelectorAll("*")
            .Where(e => string.Equals(e.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, ParsedContext> BuildContextMap(IDocument document)
    {
        var contexts = new Dictionary<string, ParsedContext>(StringComparer.Ordinal);

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
            contexts[id] = new ParsedContext(isInstant, start, end, dimensions);
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
        if (TryParseChildDate(period, InstantLocalName, out var instantDate))
        {
            isInstant = true;
            start = instantDate;
            end = instantDate;
            return true;
        }

        if (
            TryParseChildDate(period, StartDateLocalName, out var startDate)
            && TryParseChildDate(period, EndDateLocalName, out var endDate)
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

    private static bool TryParseChildDate(IElement parent, string localName, out DateOnly date)
    {
        date = default;
        var child = FindFirstChildByLocalName(parent, localName);
        return child != null
            && DateOnly.TryParse(
                child.TextContent.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date
            );
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
            var numeratorLocal = XbrlValueParser.StripPrefix(numeratorMeasure.Trim());
            var denominatorLocal = XbrlValueParser.StripPrefix(denominatorMeasure.Trim());
            if (numeratorLocal == null || denominatorLocal == null)
                return null;
            return $"{numeratorLocal}/{denominatorLocal}";
        }

        var measure = FindFirstChildByLocalName(unitElement, MeasureLocalName);
        var measureValue = measure?.TextContent?.Trim();
        if (string.IsNullOrEmpty(measureValue))
            return null;
        return XbrlValueParser.StripPrefix(measureValue);
    }

    private static bool TryParseFact(
        IElement element,
        Dictionary<string, ParsedContext> contexts,
        Dictionary<string, string> units,
        Dictionary<string, string> namespaces,
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
            Namespace = namespaces.GetValueOrDefault(taxonomy),
            Unit = unit,
            Value = value,
            IsInstant = context.IsInstant,
            PeriodStart = context.Start,
            PeriodEnd = context.End,
            Dimensions = context.Dimensions,
            Decimals = XbrlValueParser.ParseDecimals(element.GetAttribute("decimals")),
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

        // Format hints that the value is a hard-coded zero (numdash /
        // zerodash → "—", fixed-zero → literal 0). Every transformation
        // registry spells it differently (TR1 numdash, TR2/TR3 zerodash,
        // TR4+ fixed-zero); consumers expect 0, not a parse failure on the
        // dash glyph.
        if (
            format.EndsWith("numdash", StringComparison.OrdinalIgnoreCase)
            || format.EndsWith("zerodash", StringComparison.OrdinalIgnoreCase)
            || format.EndsWith("fixed-zero", StringComparison.OrdinalIgnoreCase)
            || format.EndsWith("fixedzero", StringComparison.OrdinalIgnoreCase)
        )
        {
            return TryApplyScaleAndSign(0m, element, out value);
        }

        var parenthesised = raw.StartsWith('(') && raw.EndsWith(')');
        if (parenthesised)
            raw = raw.Substring(1, raw.Length - 2).Trim();

        // European decimal notation: TR2/TR3 call it "numcommadecimal", TR4+
        // renamed it "num-comma-decimal" (plus the "-apos" grouping variant).
        // Missing the hyphenated spellings routes "1.234,56" through the
        // comma-grouping path, silently misreading it as 1.23456.
        var commaDecimal =
            format.EndsWith("numcommadecimal", StringComparison.OrdinalIgnoreCase)
            || format.Contains("num-comma-decimal", StringComparison.OrdinalIgnoreCase);

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

        // Accounting parentheses and the sign="-" attribute are two encodings of
        // the same negativity; applying both would double-negate into a positive.
        // Negate for parentheses only when sign="-" is absent (TryApplyScaleAndSign
        // applies the sign negation).
        var signNegative = string.Equals(
            element.GetAttribute("sign"),
            "-",
            StringComparison.Ordinal
        );
        if (parenthesised && !signNegative)
            parsed = -parsed;

        return TryApplyScaleAndSign(parsed, element, out value);
    }

    private static string NormaliseDigits(string raw, bool commaDecimal)
    {
        // Strip currency / spacing / apostrophe-grouping glyphs filers
        // routinely use ($, NBSP, narrow NBSP, thin spaces, Swiss-style
        // apostrophes). Then collapse thousands separators and align the
        // decimal separator to '.' so InvariantCulture parsing succeeds
        // regardless of the document's locale.
        var buffer = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            switch (ch)
            {
                case ' ':
                case NoBreakSpace:
                case NarrowNoBreakSpace:
                case ThinSpace:
                case Apostrophe:
                case RightSingleQuote:
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
        if (
            !string.IsNullOrEmpty(scaleAttribute)
            && int.TryParse(
                scaleAttribute,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var scale
            )
        )
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
}
