using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Equibles.Core.AutoWiring;
using Equibles.Core.Documents;
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
/// Scope: <c>ix:nonFraction</c> (numeric) facts, plus a whitelist of short
/// cover-page <c>ix:nonNumeric</c> values — the three 12(b) registration tags
/// (<c>dei:Security12bTitle</c> / <c>dei:TradingSymbol</c> /
/// <c>dei:SecurityExchangeName</c>), whose short text values pair into
/// security listings by shared context, the current fiscal year end, and the
/// registrant's file number and shell-company flag. General narrative text,
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

    // Cover-page facts about the registrant itself, matched by local name once the prefix is
    // proven to be the SEC dei namespace.
    private const string EntityFileNumberName = "EntityFileNumber";
    private const string EntityShellCompanyName = "EntityShellCompany";

    // The SEC transformation registry's ixt-sec:boolballotbox: an empty box is false, a checked or
    // crossed box is true.
    private const string BallotBox = "☐";
    private const string BallotBoxWithCheck = "☑";
    private const string BallotBoxWithX = "☒";

    // Hosts of the XBRL International (ixt) and SEC (ixt-sec) transformation registries.
    private static readonly HashSet<string> TransformationRegistryHosts = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "www.xbrl.org",
        "www.sec.gov",
    };

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

        var document = _parser.ParseDocument(XhtmlCompatibility.ExpandEmptyElements(html));
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
            FiscalYearEnds = ExtractFiscalYearEnds(document, contexts, namespaces),
            RegistrantCovers = ExtractRegistrantCovers(document, contexts, namespaces),
        };
    }

    /// <summary>
    /// The registrant's file number and shell-company flag, per unqualified CIK context. Only the
    /// SEC dei namespace counts, and a co-registrant's dimensioned context never answers for the
    /// filer. A fact stated twice with different values is left null rather than chosen by order.
    /// </summary>
    private static List<ParsedRegistrantCover> ExtractRegistrantCovers(
        IDocument document,
        Dictionary<string, ParsedContext> contexts,
        Dictionary<string, string> namespaces
    )
    {
        var fileNumbers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var shellFlags = new Dictionary<string, HashSet<bool?>>(StringComparer.Ordinal);
        foreach (var element in FindByLocalName(document, NonNumericLocalName))
        {
            var nil = element.GetAttribute("xsi:nil") ?? element.GetAttribute("nil");
            if (nil == "1" || string.Equals(nil, "true", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = (element.GetAttribute("name") ?? "").Split(':');
            if (
                parts.Length != 2
                || parts[1] is not (EntityFileNumberName or EntityShellCompanyName)
                || !IsSecDeiNamespace(namespaces.GetValueOrDefault(parts[0]))
            )
                continue;
            var contextRef =
                element.GetAttribute("contextRef") ?? element.GetAttribute("contextref");
            if (
                contextRef == null
                || !contexts.TryGetValue(contextRef, out var context)
                || string.IsNullOrWhiteSpace(context.ConsolidatedCik)
            )
                continue;
            var cik = context.ConsolidatedCik;

            if (parts[1] == EntityFileNumberName)
            {
                var fileNumber = CollapseWhitespace(element.TextContent);
                if (fileNumber == null)
                    continue;
                if (!fileNumbers.TryGetValue(cik, out var numbers))
                    fileNumbers[cik] = numbers = new HashSet<string>(StringComparer.Ordinal);
                numbers.Add(fileNumber);
            }
            else
            {
                if (!shellFlags.TryGetValue(cik, out var flags))
                    shellFlags[cik] = flags = [];
                // An undecodable rendering is recorded as unknown so it blocks, rather than
                // silently yields to, a decodable one elsewhere on the page.
                flags.Add(TryDecodeBoolean(element, namespaces, out var value) ? value : null);
            }
        }

        return fileNumbers
            .Keys.Union(shellFlags.Keys)
            .Select(cik => new ParsedRegistrantCover(
                cik,
                fileNumbers.TryGetValue(cik, out var numbers) && numbers.Count == 1
                    ? numbers.Single()
                    : null,
                shellFlags.TryGetValue(cik, out var flags) && flags.Count == 1
                    ? flags.Single()
                    : null
            ))
            .ToList();
    }

    private static bool IsSecDeiNamespace(string namespaceValue) =>
        Uri.TryCreate(namespaceValue, UriKind.Absolute, out var namespaceUri)
        && namespaceUri.Host == "xbrl.sec.gov"
        && namespaceUri.AbsolutePath.StartsWith("/dei/", StringComparison.Ordinal);

    /// <summary>
    /// Decodes an inline-XBRL boolean. A fixed-value transformation states its value in the format
    /// alone, so the rendered glyph is ignored (filers draw any box, letter or Wingdings character
    /// under it); the SEC ballot-box transformation reads the box; an untransformed fact must be
    /// the literal word. Any other format is not a boolean this parser can vouch for.
    /// </summary>
    private static bool TryDecodeBoolean(
        IElement element,
        Dictionary<string, string> namespaces,
        out bool value
    )
    {
        value = false;
        var text = CollapseWhitespace(element.TextContent);
        var format = element.GetAttribute("format");
        if (string.IsNullOrWhiteSpace(format))
            return bool.TryParse(text, out value);

        var formatParts = format.Split(':');
        if (formatParts.Length != 2)
            return false;
        var formatNamespace =
            ResolveNamespace(element, formatParts[0])
            ?? namespaces.GetValueOrDefault(formatParts[0]);
        if (
            !Uri.TryCreate(formatNamespace, UriKind.Absolute, out var formatUri)
            || !TransformationRegistryHosts.Contains(formatUri.Host)
        )
            return false;

        switch (formatParts[1].ToLowerInvariant())
        {
            case "fixed-true":
            case "booleantrue":
                value = true;
                return true;
            case "fixed-false":
            case "booleanfalse":
                value = false;
                return true;
            case "boolballotbox":
                if (text == BallotBox)
                    return true;
                if (text is BallotBoxWithCheck or BallotBoxWithX)
                {
                    value = true;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static List<ParsedFiscalYearEnd> ExtractFiscalYearEnds(
        IDocument document,
        Dictionary<string, ParsedContext> contexts,
        Dictionary<string, string> namespaces
    )
    {
        var observations = new List<ParsedFiscalYearEnd>();
        foreach (var element in FindByLocalName(document, NonNumericLocalName))
        {
            if (
                (element.GetAttribute("xsi:nil") ?? element.GetAttribute("nil")) == "1"
                || string.Equals(
                    element.GetAttribute("xsi:nil") ?? element.GetAttribute("nil"),
                    "true",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;
            var name = element.GetAttribute("name") ?? "";
            var parts = name.Split(':');
            if (
                parts.Length != 2
                || parts[1] != "CurrentFiscalYearEndDate"
                || !namespaces.TryGetValue(parts[0], out var namespaceValue)
                || !Uri.TryCreate(namespaceValue, UriKind.Absolute, out var namespaceUri)
                || namespaceUri.Host != "xbrl.sec.gov"
                || !namespaceUri.AbsolutePath.StartsWith("/dei/", StringComparison.Ordinal)
            )
                continue;
            var contextRef =
                element.GetAttribute("contextRef") ?? element.GetAttribute("contextref");
            if (
                contextRef == null
                || !contexts.TryGetValue(contextRef, out var context)
                || string.IsNullOrWhiteSpace(context.ConsolidatedCik)
            )
                continue;
            var format = element.GetAttribute("format");
            var formatParts = format?.Split(':');
            if (
                !string.IsNullOrWhiteSpace(format)
                && (formatParts?.Length != 2 || formatParts.Any(string.IsNullOrWhiteSpace))
            )
                continue;
            if (
                !FiscalYearEndValueParser.TryParse(
                    element.TextContent,
                    out var date,
                    formatParts?.Last(),
                    formatParts?.Length == 2 ? ResolveNamespace(element, formatParts[0]) : null
                )
            )
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
        return observations.Distinct().ToList();
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

    private static string ResolveNamespace(IElement element, string prefix)
    {
        for (var current = element; current != null; current = current.ParentElement)
        {
            var attribute = current.Attributes.FirstOrDefault(a =>
                a.Name.Equals("xmlns:" + prefix, StringComparison.OrdinalIgnoreCase)
            );
            if (attribute != null)
                return attribute.Value;
        }
        return null;
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
            var entity = FindFirstChildByLocalName(contextElement, "xbrli:entity");
            var identifier =
                entity == null ? null : FindFirstChildByLocalName(entity, "xbrli:identifier");
            var unqualified =
                !FindByLocalName(contextElement, "xbrli:segment").Any()
                && !FindByLocalName(contextElement, "xbrli:scenario").Any();
            var scheme = identifier?.GetAttribute("scheme");
            var cik =
                unqualified && scheme == "http://www.sec.gov/CIK"
                    ? identifier.TextContent?.Trim()
                    : null;
            // A European report states the same unqualified identity under the ISO 17442 scheme.
            var lei =
                unqualified && scheme == "http://standards.iso.org/iso/17442"
                    ? identifier.TextContent?.Trim()
                    : null;
            // Repeated IDs across concatenated exhibits cannot prove one source context.
            if (contexts.ContainsKey(id))
            {
                cik = null;
                lei = null;
            }
            contexts[id] = new ParsedContext(isInstant, start, end, dimensions, cik, lei);
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
            ConsolidatedCik = context.ConsolidatedCik,
            ConsolidatedLei = context.ConsolidatedLei,
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
