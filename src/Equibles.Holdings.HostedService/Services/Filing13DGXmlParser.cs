using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Parses the raw <c>primary_doc.xml</c> of a single Schedule 13D or 13G filing
/// (beneficial-ownership reports, machine-readable XML since 2024-12-18).
///
/// The two forms share a namespace family but differ in element names — 13D uses
/// <c>dateOfEvent</c>, <c>issuerCUSIP</c>, repeated <c>reportingPersonInfo</c>
/// blocks with flat <c>soleVotingPower</c>… children and <c>percentOfClass</c>;
/// 13G uses <c>eventDateRequiresFilingThisStatement</c>, <c>issuerCusip</c>,
/// repeated <c>coverPageHeaderReportingPersonDetails</c> blocks with the voting
/// powers nested under <c>reportingPersonBeneficiallyOwnedNumberOfShares</c> and
/// <c>classPercent</c>. Every lookup is by <see cref="XName.LocalName"/> (so
/// namespace versions don't matter) and accepts the candidate names from both
/// forms, which lets one parser handle 13D and 13G alike.
/// </summary>
[Service]
public class Filing13DGXmlParser
{
    /// <summary>
    /// Parses a 13D/13G <c>primary_doc.xml</c>. <paramref name="accessionNumber"/>,
    /// <paramref name="cik"/> and <paramref name="filingDate"/> come from the daily
    /// index; the CIK is a fallback when the XML header omits the filer CIK.
    /// </summary>
    public Parsed13DGFiling ParseFiling(
        string primaryDocXml,
        string accessionNumber,
        string cik,
        DateOnly filingDate
    )
    {
        var root = ParseDocument(primaryDocXml).Root;
        if (root == null)
            throw new FormatException("primary_doc.xml has no root element");

        var headerData = Descendant(root, "headerData");
        var submissionType = Value(Descendant(headerData, "submissionType"));
        var xmlFilerCik = Value(Descendant(headerData, "cik"));

        var coverPage = Descendant(root, "coverPageHeader");
        var issuerInfo = Descendant(coverPage, "issuerInfo");

        var filing = new Parsed13DGFiling
        {
            AccessionNumber = accessionNumber,
            FilingDate = filingDate,
            SubmissionType = submissionType,
            // A 13D/13G XML can only describe a 13D or 13G; default to 13D if the
            // header is somehow unmapped so the filing is never silently dropped.
            FilingType = submissionType.ToHoldingsFilingType() ?? FilingType.Schedule13D,
            IsAmendment = submissionType.IsAmendmentFormType(),
            FilerCik = string.IsNullOrEmpty(xmlFilerCik)
                ? cik?.TrimStart('0')
                : xmlFilerCik.TrimStart('0'),
            DateOfEvent = ParseSecDate(
                FirstValue(coverPage, "dateOfEvent", "eventDateRequiresFilingThisStatement")
            ),
            IssuerCik = FirstValue(issuerInfo, "issuerCIK", "issuerCik")?.TrimStart('0'),
            // Schema X0202 (~2026-03-16) wraps the CUSIP in a list:
            // <issuerCusips><issuerCusipNumber>…</issuerCusipNumber></issuerCusips>;
            // the descendant probe takes the first entry.
            IssuerCusip = FirstValue(issuerInfo, "issuerCUSIP", "issuerCusip", "issuerCusipNumber"),
            IssuerName = Value(Descendant(issuerInfo, "issuerName")),
            SecuritiesClassTitle = Value(Descendant(coverPage, "securitiesClassTitle")),
        };

        // 13D groups reporting persons under <reportingPersonInfo>; 13G under
        // <coverPageHeaderReportingPersonDetails>. Collect either.
        var personElements = Descendants(root, "reportingPersonInfo")
            .Concat(Descendants(root, "coverPageHeaderReportingPersonDetails"));

        foreach (var person in personElements)
            filing.ReportingPersons.Add(ParseReportingPerson(person));

        return filing;
    }

    // Matches an end tag whose closing '>' was dropped by the filer software — `</name`
    // followed (after optional whitespace) by the next tag's '<'. In well-formed XML a
    // literal `</` can never start unescaped text content, so the match is unambiguous.
    private static readonly Regex TruncatedEndTag = new(
        @"</([A-Za-z_][A-Za-z0-9._:-]*)(?=\s*<)",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Strict parse with one repair retry: some EDGAR-accepted filings carry end tags
    /// missing their closing '>' (e.g. <c>&lt;/fundsSource</c> at a line end). The repair
    /// runs only after a strict parse failed, so well-formed documents are never touched.
    /// </summary>
    private static XDocument ParseDocument(string primaryDocXml)
    {
        try
        {
            return XDocument.Parse(primaryDocXml);
        }
        catch (XmlException)
        {
            return XDocument.Parse(TruncatedEndTag.Replace(primaryDocXml, "</$1>"));
        }
    }

    private static Parsed13DGReportingPerson ParseReportingPerson(XElement person)
    {
        var cik = Value(Child(person, "reportingPersonCIK"));

        return new Parsed13DGReportingPerson
        {
            Cik = string.IsNullOrEmpty(cik) ? null : cik.TrimStart('0'),
            Name = Value(Descendant(person, "reportingPersonName")),
            // In 13D these powers are flat children; in 13G they nest under
            // <reportingPersonBeneficiallyOwnedNumberOfShares>. A descendant scan
            // scoped to this person's element finds both without cross-row bleed.
            SoleVotingPower = ParseShares(Value(Descendant(person, "soleVotingPower"))),
            SharedVotingPower = ParseShares(Value(Descendant(person, "sharedVotingPower"))),
            SoleDispositivePower = ParseShares(Value(Descendant(person, "soleDispositivePower"))),
            SharedDispositivePower = ParseShares(
                Value(Descendant(person, "sharedDispositivePower"))
            ),
            AggregateAmountOwned = ParseShares(
                FirstValue(
                    person,
                    "aggregateAmountOwned",
                    "reportingPersonBeneficiallyOwnedAggregateNumberOfShares"
                )
            ),
            PercentOfClass = ParsePercent(FirstValue(person, "percentOfClass", "classPercent")),
            TypeOfReportingPerson = Value(Descendant(person, "typeOfReportingPerson")),
            CitizenshipOrOrganization = Value(Descendant(person, "citizenshipOrOrganization")),
        };
    }

    private static IEnumerable<XElement> WithLocalName(
        IEnumerable<XElement> source,
        string localName
    ) =>
        source.Where(e =>
            string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase)
        );

    private static IEnumerable<XElement> Children(XElement parent, string localName) =>
        WithLocalName(parent.Elements(), localName);

    private static IEnumerable<XElement> Descendants(XElement parent, string localName) =>
        WithLocalName(parent.Descendants(), localName);

    private static XElement Descendant(XElement parent, string localName) =>
        parent == null ? null : Descendants(parent, localName).FirstOrDefault();

    private static XElement Child(XElement parent, string localName) =>
        parent == null ? null : Children(parent, localName).FirstOrDefault();

    private static string Value(XElement element) => element?.Value.Trim();

    /// <summary>First non-empty descendant value among the candidate local names.</summary>
    private static string FirstValue(XElement parent, params string[] localNames)
    {
        if (parent == null)
            return null;

        foreach (var name in localNames)
        {
            var value = Value(Descendant(parent, name));
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        return null;
    }

    /// <summary>
    /// Cover-page event dates are <c>MM/DD/YYYY</c>. Returns
    /// <see cref="DateOnly.MinValue"/> when unparseable so the import pipeline
    /// filters the filing out rather than crashing.
    /// </summary>
    private static DateOnly ParseSecDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DateOnly.MinValue;

        if (
            DateOnly.TryParseExact(
                raw.Trim(),
                ["MM/dd/yyyy", "MM-dd-yyyy"],
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

    /// <summary>
    /// Share/power amounts. 13G files them as decimals (<c>1624818.00</c>),
    /// 13D as plain integers; both may carry thousands separators. Parsed as a
    /// decimal and truncated to whole shares.
    /// </summary>
    private static long ParseShares(string raw) =>
        decimal.TryParse(
            raw?.Replace(",", string.Empty),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value
        )
        // A value that parses but exceeds Int64 (corrupt/typo'd field) must degrade
        // to 0 like every other field-parser here; the decimal->long cast is always
        // range-checked and would otherwise throw, crashing the whole filing parse.
        && value >= long.MinValue
        && value <= long.MaxValue
            ? (long)value
            : 0;

    // The storage cap of the holdings percent columns (numeric(7,4)). The form reports
    // 0-100, so a value past the cap is filer garbage (a fat-fingered "5,000" parses as
    // 5000) — it degrades to null like any unparseable field rather than aborting the
    // whole batch flush with a numeric-overflow error.
    private const decimal MaxStorablePercent = 999.9999m;

    private static decimal? ParsePercent(string raw) =>
        decimal.TryParse(
            raw?.Replace(",", string.Empty),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value
        )
        && Math.Abs(value) <= MaxStorablePercent
            ? value
            : null;
}
