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

        var coverPage = Descendant(root, "coverPage");
        var filingManager = coverPage == null ? null : Descendant(coverPage, "filingManager");
        var address = filingManager == null ? null : Descendant(filingManager, "address");

        var xmlCik = Value(Descendant(root, "cik"));

        var filing = new Parsed13FFiling
        {
            AccessionNumber = accessionNumber,
            Cik = string.IsNullOrEmpty(xmlCik) ? cik?.TrimStart('0') : xmlCik.TrimStart('0'),
            FilingDate = filingDate,
            PeriodOfReport = ParseSecDate(Value(Descendant(root, "reportCalendarOrQuarter"))),
            IsAmendment = IsAmendmentValue(Value(Descendant(root, "isAmendment"))),
            FilingManagerName = Value(filingManager == null ? null : Child(filingManager, "name")),
            City = Value(address == null ? null : Descendant(address, "city")),
            StateOrCountry = Value(
                address == null ? null : Descendant(address, "stateOrCountry")
            ),
            Form13FFileNumber = Value(Descendant(root, "form13FFileNumber")),
            CrdNumber = Value(Descendant(root, "crdNumber")),
        };

        foreach (var otherManager2 in Descendants(root, "otherManager2"))
        {
            var seqText = Value(Descendant(otherManager2, "sequenceNumber"));
            if (!int.TryParse(seqText, out var seq))
                continue;

            var inner = Descendant(otherManager2, "otherManager");
            var name = Value(inner == null ? null : Descendant(inner, "name"));
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

        foreach (var info in Descendants(root, "infoTable"))
        {
            var amount = Descendant(info, "shrsOrPrnAmt");
            var voting = Descendant(info, "votingAuthority");

            holdings.Add(
                new Parsed13FHolding
                {
                    Cusip = Value(Descendant(info, "cusip")),
                    TitleOfClass = Value(Descendant(info, "titleOfClass")),
                    ShareType = Value(
                        amount == null ? null : Descendant(amount, "sshPrnamtType")
                    ),
                    Shares = ParseLong(
                        Value(amount == null ? null : Descendant(amount, "sshPrnamt"))
                    ),
                    PutCall = Value(Descendant(info, "putCall")),
                    InvestmentDiscretion = Value(Descendant(info, "investmentDiscretion")),
                    VotingAuthSole = ParseLong(
                        Value(voting == null ? null : Descendant(voting, "Sole"))
                    ),
                    VotingAuthShared = ParseLong(
                        Value(voting == null ? null : Descendant(voting, "Shared"))
                    ),
                    VotingAuthNone = ParseLong(
                        Value(voting == null ? null : Descendant(voting, "None"))
                    ),
                    OtherManagerNumber = ParseFirstInt(Value(Descendant(info, "otherManager"))),
                }
            );
        }

        return holdings;
    }

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
