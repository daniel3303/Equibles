using System.Globalization;
using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Extensions;
using Equibles.Sec.HostedService.Helpers;
using Equibles.Sec.Repositories;
using static Equibles.Sec.HostedService.Helpers.EdgarXmlSubmissionParser;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Processes SEC Form NPORT-P filings — a registered investment company's monthly portfolio report
/// for one of its series — into a structured <see cref="NportFiling"/> record plus the series'
/// schedule of portfolio investments (<see cref="NportHolding"/>). NPORT-P appears in the
/// registrant's submissions feed, so each report is attributed to the registrant's stock. Both the
/// original report ("NPORT-P") and its amendments ("NPORT-P/A") are processed. The XML root is
/// <c>edgarSubmission</c> in the <c>http://www.sec.gov/edgar/nport</c> namespace, so elements are
/// navigated by local name. Booleans are reported as "Y"/"N"; amounts are decimal strings.
/// </summary>
public class NportFilingProcessor : IssuerFeedFilingProcessor<NportFiling, NportFilingRepository>
{
    // NPORT dates are ISO yyyy-MM-dd.
    private static readonly string[] DateFormats = ["yyyy-MM-dd"];

    public NportFilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<NportFilingProcessor> logger,
        ErrorReporter errorReporter
    )
        : base(scopeFactory, logger, errorReporter) { }

    public override bool CanProcess(DocumentType documentType)
    {
        return documentType == DocumentType.NportP || documentType == DocumentType.NportPa;
    }

    protected override string FormLabel => "NPORT-P";

    protected override string ParseContext => "Nport.ParseXml";

    protected override string RequiredSection => "genInfo";

    protected override IQueryable<NportFiling> GetByAccessionNumber(
        NportFilingRepository repository,
        string accessionNumber
    ) => repository.GetByAccessionNumber(accessionNumber);

    protected override void LogImported(NportFiling entity, string ticker, string accessionNumber)
    {
        Logger.LogInformation(
            "Imported NPORT-P for {Ticker} ({Series}, net assets {NetAssets}, {Holdings} holdings) from {AccessionNumber}",
            ticker,
            entity.SeriesName,
            entity.NetAssets,
            entity.Holdings.Count,
            accessionNumber
        );
    }

    protected override NportFiling ParseFiling(XElement root, Guid companyId, FilingData filing) =>
        ParseEntity(root, companyId, filing);

    /// <summary>
    /// Parses an NPORT-P submission root into a <see cref="NportFiling"/> with its schedule of
    /// holdings, stamped at <see cref="NportFiling.CurrentParserVersion"/>. Shared by the ingest
    /// pipeline (<see cref="ParseFiling"/>) and the version-driven reprocess pass, so both derive
    /// holdings identically. Returns null when the submission lacks the required genInfo section.
    /// </summary>
    internal static NportFiling ParseEntity(XElement root, Guid companyId, FilingData filing)
    {
        var headerData = El(root, "headerData");
        var formData = El(root, "formData");
        var genInfo = El(formData, "genInfo");
        if (genInfo == null)
            return null;

        var fundInfo = El(formData, "fundInfo");

        var entity = new NportFiling
        {
            CommonStockId = companyId,
            AccessionNumber = filing.AccessionNumber,
            FilingDate = filing.FilingDate,
            IsAmendment = ParseIsAmendment(headerData, filing),
            RegistrantName = Truncate(Clean(Val(genInfo, "regName")), 512),
            SeriesName = Truncate(Clean(Val(genInfo, "seriesName")), 512),
            SeriesId = Clean(Val(genInfo, "seriesId")),
            SeriesLei = Clean(Val(genInfo, "seriesLei")),
            ReportPeriodDate = ParseDate(Val(genInfo, "repPdDate")) ?? filing.ReportDate,
            ReportPeriodEnd = ParseDate(Val(genInfo, "repPdEnd")) ?? filing.ReportDate,
            TotalAssets = ParseDecimal(Val(fundInfo, "totAssets")),
            TotalLiabilities = ParseDecimal(Val(fundInfo, "totLiabs")),
            NetAssets = ParseDecimal(Val(fundInfo, "netAssets")),
            IsFinalFiling = ParseYesNo(Val(genInfo, "isFinalFiling")),
            ParserVersion = NportFiling.CurrentParserVersion,
        };

        // invstOrSecs is a child of formData, a sibling of fundInfo — not nested inside fundInfo.
        foreach (var investment in Els(El(formData, "invstOrSecs"), "invstOrSec"))
        {
            var holding = ParseHolding(investment);
            if (holding != null)
                entity.Holdings.Add(holding);
        }

        return entity;
    }

    // Returns null when the line has neither a name nor a value — NPORT pads some sections with
    // empty placeholder entries.
    private static NportHolding ParseHolding(XElement element)
    {
        var name = Clean(Val(element, "name"));
        var valueUsd = ParseDecimal(Val(element, "valUSD"));
        if (name == null && valueUsd == 0)
            return null;

        var identifiers = El(element, "identifiers");

        return new NportHolding
        {
            Name = Truncate(name, 512),
            Title = Truncate(Clean(Val(element, "title")), 512),
            Cusip = Clean(Val(element, "cusip")),
            Isin = Clean(Attr(El(identifiers, "isin"), "value")),
            Lei = Clean(Val(element, "lei")),
            Balance = ParseDecimal(Val(element, "balance")),
            Units = Clean(Val(element, "units")),
            Currency = Clean(Val(element, "curCd")),
            ValueUsd = valueUsd,
            PercentValue = ParseDecimal(Val(element, "pctVal")),
            PayoffProfile = Clean(Val(element, "payoffProfile")),
            AssetCategory = Clean(Val(element, "assetCat")),
            IssuerCategory = ParseIssuerCategory(element),
            InvestmentCountry = Clean(Val(element, "invCountry")),
        };
    }

    // NPORT reports the issuer category either as an <issuerCat> element or, for conditional
    // categories (e.g. swaps), as the issuerCat attribute on <issuerConditional>.
    private static string ParseIssuerCategory(XElement element)
    {
        var direct = Clean(Val(element, "issuerCat"));
        if (direct != null)
            return direct;

        return Clean(Attr(El(element, "issuerConditional"), "issuerCat"));
    }

    private static bool ParseIsAmendment(XElement headerData, FilingData filing)
    {
        var submissionType = Val(headerData, "submissionType");
        if (submissionType != null)
            return submissionType.Contains("/A", StringComparison.OrdinalIgnoreCase);

        return filing.IsAmendmentForm();
    }

    // Pinned by processor-scoped tests; the implementation lives in the shared parser.
    internal static string SanitizeXml(string xml) => EdgarXmlSubmissionParser.SanitizeXml(xml);

    internal static bool ParseYesNo(string value)
    {
        return value != null && value.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase);
    }

    internal static DateOnly? ParseDate(string value) =>
        EdgarXmlSubmissionParser.ParseDate(value, DateFormats);

    internal static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        return decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result
        )
            ? result
            : 0;
    }
}
