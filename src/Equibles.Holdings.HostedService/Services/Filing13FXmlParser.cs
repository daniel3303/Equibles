using System.Globalization;
using System.Xml.Linq;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Parses the raw XML of a single 13F-HR filing. SEC ships several namespace
/// variants of these schemas over time, so every lookup is by element
/// <see cref="XName.LocalName"/> and ignores namespaces entirely — the only
/// approach that survives historical and current filings alike.
/// </summary>
[Service]
public class Filing13FXmlParser
{
    /// <summary>
    /// Parses the cover page (<c>primary_doc.xml</c>) into filing metadata.
    /// <paramref name="accessionNumber"/> and <paramref name="cik"/> come from
    /// the daily index and are used as fallbacks when the XML omits them.
    /// </summary>
    public Parsed13FFiling ParseCoverPage(
        string primaryDocXml,
        string accessionNumber,
        string cik,
        DateOnly filingDate
    )
    {
        var root = XDocument.Parse(primaryDocXml).Root;
        if (root == null)
            throw new FormatException("primary_doc.xml has no root element");

        // Scope every lookup to its structural parent. Several local names
        // (cik, form13FFileNumber, name) legitimately recur elsewhere in the
        // document — under headerData vs an otherManager block — so an
        // unscoped first-match would silently grab the wrong element.
        var headerData = Descendant(root, "headerData");
        var coverPage = Descendant(root, "coverPage");
        var filingManager = coverPage == null ? null : Descendant(coverPage, "filingManager");
        var address = filingManager == null ? null : Descendant(filingManager, "address");

        var xmlCik = headerData == null ? null : Value(Descendant(headerData, "cik"));

        var filing = new Parsed13FFiling
        {
            AccessionNumber = accessionNumber,
            Cik = string.IsNullOrEmpty(xmlCik) ? cik?.TrimStart('0') : xmlCik.TrimStart('0'),
            FilingDate = filingDate,
            PeriodOfReport = ParseSecDate(
                Value(coverPage == null ? null : Descendant(coverPage, "reportCalendarOrQuarter"))
            ),
            IsAmendment = IsAmendmentValue(
                Value(coverPage == null ? null : Descendant(coverPage, "isAmendment"))
            ),
            FilingManagerName = Value(filingManager == null ? null : Child(filingManager, "name")),
            City = Value(address == null ? null : Child(address, "city")),
            StateOrCountry = Value(address == null ? null : Child(address, "stateOrCountry")),
            Form13FFileNumber = Value(
                coverPage == null ? null : Descendant(coverPage, "form13FFileNumber")
            ),
            CrdNumber = Value(coverPage == null ? null : Descendant(coverPage, "crdNumber")),
        };

        var otherManagersScope = coverPage ?? root;
        foreach (var otherManager2 in Descendants(otherManagersScope, "otherManager2"))
        {
            var seqText = Value(Child(otherManager2, "sequenceNumber"));
            if (!int.TryParse(seqText, out var seq))
                continue;

            var inner = Child(otherManager2, "otherManager");
            var name = Value(inner == null ? null : Child(inner, "name"));
            if (!string.IsNullOrEmpty(name))
                filing.OtherManagers[seq] = name;
        }

        return filing;
    }

    /// <summary>
    /// Parses the information-table XML into holding rows. Tolerates a missing
    /// or empty table (an amendment that removes every position) by returning
    /// an empty list.
    /// </summary>
    public List<Parsed13FHolding> ParseInformationTable(string infoTableXml)
    {
        var holdings = new List<Parsed13FHolding>();
        if (string.IsNullOrWhiteSpace(infoTableXml))
            return holdings;

        var root = XDocument.Parse(infoTableXml).Root;
        if (root == null)
            return holdings;

        // infoTable rows are flat direct children of the table root. Use
        // direct-child traversal so a field is never read from a sibling row
        // (recursive Descendants would cross row boundaries if the schema ever
        // nested). Fall back to a recursive scan only if the root is wrapped.
        var rows = Children(root, "infoTable").ToList();
        if (rows.Count == 0)
            rows = Descendants(root, "infoTable").ToList();

        foreach (var info in rows)
        {
            var amount = Child(info, "shrsOrPrnAmt");
            var voting = Child(info, "votingAuthority");

            holdings.Add(
                new Parsed13FHolding
                {
                    Cusip = Value(Child(info, "cusip")),
                    TitleOfClass = Value(Child(info, "titleOfClass")),
                    ShareType = Value(amount == null ? null : Child(amount, "sshPrnamtType")),
                    Shares = ParseLong(Value(amount == null ? null : Child(amount, "sshPrnamt"))),
                    PutCall = Value(Child(info, "putCall")),
                    InvestmentDiscretion = Value(Child(info, "investmentDiscretion")),
                    VotingAuthSole = ParseLong(
                        Value(voting == null ? null : Child(voting, "Sole"))
                    ),
                    VotingAuthShared = ParseLong(
                        Value(voting == null ? null : Child(voting, "Shared"))
                    ),
                    VotingAuthNone = ParseLong(
                        Value(voting == null ? null : Child(voting, "None"))
                    ),
                    OtherManagerNumber = ParseFirstInt(Value(Child(info, "otherManager"))),
                }
            );
        }

        return holdings;
    }

    private static IEnumerable<XElement> Children(XElement parent, string localName) =>
        parent
            .Elements()
            .Where(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase)
            );

    private static XElement Descendant(XElement parent, string localName) =>
        parent
            .Descendants()
            .FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase)
            );

    private static IEnumerable<XElement> Descendants(XElement parent, string localName) =>
        parent.Descendants()
            .Where(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase)
            );

    private static XElement Child(XElement parent, string localName) =>
        parent
            .Elements()
            .FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase)
            );

    private static string Value(XElement element) => element?.Value.Trim();

    private static bool IsAmendmentValue(string raw) =>
        !string.IsNullOrEmpty(raw)
        && (
            raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("y", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw == "1"
        );

    /// <summary>
    /// Cover-page dates are <c>MM-DD-YYYY</c>; a few historical filings use
    /// ISO. Returns <see cref="DateOnly.MinValue"/> when unparseable so the
    /// downstream pipeline filters the filing out rather than crashing.
    /// </summary>
    private static DateOnly ParseSecDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DateOnly.MinValue;

        if (
            DateOnly.TryParseExact(
                raw.Trim(),
                "MM-dd-yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact
            )
        )
            return exact;

        return DateOnly.TryParse(
            raw.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var general
        )
            ? general
            : DateOnly.MinValue;
    }

    private static long ParseLong(string raw) =>
        long.TryParse(
            raw?.Replace(",", string.Empty),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : 0;

    private static int? ParseFirstInt(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var first = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return int.TryParse(first, out var value) ? value : null;
    }
}
