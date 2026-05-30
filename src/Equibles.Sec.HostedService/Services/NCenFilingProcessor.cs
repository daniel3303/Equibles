using System.Globalization;
using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Helpers;
using Equibles.Sec.Repositories;
using static Equibles.Sec.HostedService.Helpers.EdgarXmlSubmissionParser;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Processes SEC Form N-CEN filings — the annual report filed by registered investment companies
/// (mutual funds, ETFs, closed-end funds) — into structured <see cref="NCenFiling"/> records plus
/// the fund's named service providers. N-CEN appears in the registrant's submissions feed, so each
/// report is attributed to the registrant's stock. Both the original report ("N-CEN") and its
/// amendments ("N-CEN/A") are processed. The XML root is <c>edgarSubmission</c> in the
/// <c>http://www.sec.gov/edgar/ncen</c> namespace, so elements are navigated by local name.
/// Booleans are reported as "Y"/"N".
/// </summary>
public class NCenFilingProcessor : IssuerFeedFilingProcessor<NCenFiling, NCenFilingRepository>
{
    // N-CEN dates are ISO yyyy-MM-dd.
    private static readonly string[] DateFormats = ["yyyy-MM-dd"];

    // Optional fields the filer leaves blank are reported as the literal "N/A"; treat as absent.
    private const string NotApplicable = "N/A";

    public NCenFilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<NCenFilingProcessor> logger,
        ErrorReporter errorReporter
    )
        : base(scopeFactory, logger, errorReporter) { }

    public override bool CanProcess(DocumentType documentType)
    {
        return documentType == DocumentType.NCen || documentType == DocumentType.NCenA;
    }

    protected override string FormLabel => "N-CEN";

    protected override string ParseContext => "NCen.ParseXml";

    protected override string RequiredSection => "registrantInfo";

    protected override IQueryable<NCenFiling> GetByAccessionNumber(
        NCenFilingRepository repository,
        string accessionNumber
    ) => repository.GetByAccessionNumber(accessionNumber);

    protected override void LogImported(NCenFiling entity, string ticker, string accessionNumber)
    {
        Logger.LogInformation(
            "Imported N-CEN for {Ticker} ({Registrant}, type {Type}, {Providers} service providers) from {AccessionNumber}",
            ticker,
            entity.RegistrantName,
            entity.InvestmentCompanyType,
            entity.ServiceProviders.Count,
            accessionNumber
        );
    }

    protected override NCenFiling ParseFiling(XElement root, Guid companyId, FilingData filing)
    {
        var headerData = El(root, "headerData");
        var filerInfo = El(headerData, "filerInfo");
        var formData = El(root, "formData");
        var registrant = El(formData, "registrantInfo");
        if (registrant == null)
            return null;

        var generalInfo = El(formData, "generalInfo");

        var entity = new NCenFiling
        {
            CommonStockId = companyId,
            AccessionNumber = filing.AccessionNumber,
            FilingDate = filing.FilingDate,
            IsAmendment = ParseIsAmendment(headerData, filing),
            RegistrantName = Clean(Val(registrant, "registrantFullName")),
            InvestmentCompanyType = Clean(Val(filerInfo, "investmentCompanyType")),
            InvestmentCompanyFileNumber = Clean(Val(registrant, "investmentCompFileNo")),
            RegistrantLei = Clean(Val(registrant, "registrantLei")),
            State = Clean(Val(registrant, "registrantstate")),
            Country = Clean(Val(registrant, "registrantcountry")),
            ReportEndingPeriod =
                ParseDate(Attr(generalInfo, "reportEndingPeriod")) ?? filing.ReportDate,
            IsReportPeriodLessThan12Months = ParseYesNo(Attr(generalInfo, "isReportPeriodLt12")),
            IsFirstFiling = ParseYesNo(Val(registrant, "isRegistrantFirstFiling")),
            IsLastFiling = ParseYesNo(Val(registrant, "isRegistrantLastFiling")),
            IsFamilyInvestmentCompany = ParseYesNo(Val(registrant, "isRegistrantFamilyInvComp")),
        };

        AddServiceProviders(entity, registrant, formData);

        return entity;
    }

    private static void AddServiceProviders(
        NCenFiling entity,
        XElement registrant,
        XElement formData
    )
    {
        var providers = new List<NCenServiceProvider>();

        // Registrant-level providers.
        AddProviders(
            providers,
            Els(El(registrant, "publicAccountants"), "publicAccountant"),
            NCenServiceProviderType.PublicAccountant,
            a => Val(a, "publicAccountantName"),
            a => Attr(El(a, "publicAccountantStateCountry"), "publicAccountantCountry"),
            _ => false
        );

        AddProviders(
            providers,
            Els(El(registrant, "principalUnderwriters"), "principalUnderwriter"),
            NCenServiceProviderType.PrincipalUnderwriter,
            u => Val(u, "principalUnderwriterName"),
            u => Val(u, "principalUnderWriterCountry"),
            u => ParseYesNo(Val(u, "isPrincipalUnderwriterAffiliatedWithRegistrant"))
        );

        // Per-series (management investment company) providers. A multi-series fund repeats the
        // same firms across series, so the list is de-duplicated by role + name below.
        var seriesInfo = El(formData, "managementInvestmentQuestionSeriesInfo");
        foreach (var question in Els(seriesInfo, "managementInvestmentQuestion"))
        {
            AddProviders(
                providers,
                Els(El(question, "investmentAdvisers"), "investmentAdviser"),
                NCenServiceProviderType.InvestmentAdviser,
                a => Val(a, "investmentAdviserName"),
                a => Val(a, "investmentAdviserCountry"),
                _ => false
            );

            AddProviders(
                providers,
                Els(El(question, "subAdvisers"), "subAdviser"),
                NCenServiceProviderType.SubAdviser,
                a => Val(a, "subAdviserName"),
                a => Val(a, "subAdviserCountry"),
                a => ParseYesNo(Val(a, "isSubAdviserAffiliated"))
            );

            AddProviders(
                providers,
                Els(El(question, "custodians"), "custodian"),
                NCenServiceProviderType.Custodian,
                c => Val(c, "custodianName"),
                c => Val(c, "custodianCountry"),
                c => ParseYesNo(Val(c, "isCustodianAffiliated"))
            );

            AddProviders(
                providers,
                Els(El(question, "transferAgents"), "transferAgent"),
                NCenServiceProviderType.TransferAgent,
                a => Val(a, "transferAgentName"),
                a => Attr(El(a, "transferAgentStateCountry"), "transferAgentCountry"),
                a => ParseYesNo(Val(a, "isTransferAgentAffiliated"))
            );

            AddProviders(
                providers,
                Els(El(question, "admins"), "admin"),
                NCenServiceProviderType.Administrator,
                a => Val(a, "adminName"),
                a => Attr(El(a, "adminStateCountry"), "adminCountry"),
                a => ParseYesNo(Val(a, "isAdminAffiliated"))
            );

            AddProviders(
                providers,
                Els(El(question, "pricingServices"), "pricingService"),
                NCenServiceProviderType.PricingService,
                p => Val(p, "pricingServiceName"),
                p => Val(p, "pricingServiceCountry"),
                p => ParseYesNo(Val(p, "isPricingServiceAffiliated"))
            );

            AddProviders(
                providers,
                Els(El(question, "shareholderServicingAgents"), "shareholderServicingAgent"),
                NCenServiceProviderType.ShareholderServicingAgent,
                s => Val(s, "shareholderServiceAgentName"),
                s =>
                    Attr(
                        El(s, "shareholderServiceAgentStateCountry"),
                        "shareholderServiceAgentCountry"
                    ),
                s => ParseYesNo(Val(s, "isShareholderServiceAgentAffiliated"))
            );
        }

        foreach (
            var provider in providers
                .Where(p => p != null)
                .GroupBy(p => (p.ProviderType, p.Name.ToUpperInvariant()))
                .Select(g => g.First())
        )
        {
            entity.ServiceProviders.Add(provider);
        }
    }

    // Iterate one provider collection, building a provider per element with the given
    // role + field accessors. MakeProvider may return null (blank / "N/A" placeholder);
    // those nulls are filtered out during the role+name de-duplication above.
    private static void AddProviders(
        List<NCenServiceProvider> providers,
        IEnumerable<XElement> items,
        NCenServiceProviderType type,
        Func<XElement, string> name,
        Func<XElement, string> country,
        Func<XElement, bool> affiliated
    )
    {
        foreach (var item in items)
            providers.Add(MakeProvider(type, name(item), country(item), affiliated(item)));
    }

    // Returns null when the firm has no usable name (blank or the literal "N/A"), so the caller
    // skips it — N-CEN pads unused provider slots with "N/A" placeholders.
    private static NCenServiceProvider MakeProvider(
        NCenServiceProviderType type,
        string name,
        string country,
        bool affiliated
    )
    {
        var cleanName = Clean(name);
        if (cleanName == null)
            return null;

        return new NCenServiceProvider
        {
            ProviderType = type,
            Name = cleanName,
            Country = Clean(country),
            IsAffiliated = affiliated,
        };
    }

    private static bool ParseIsAmendment(XElement headerData, FilingData filing)
    {
        var submissionType = Val(headerData, "submissionType");
        if (submissionType != null)
            return submissionType.Contains("/A", StringComparison.OrdinalIgnoreCase);

        return filing.Form?.Contains("/A", StringComparison.OrdinalIgnoreCase) == true;
    }

    // ── XML helpers (navigate by local name; N-CEN declares the ncen namespace) ──────

    private static string Attr(XElement element, string name)
    {
        var value = element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
        return string.IsNullOrEmpty(value) ? null : value.Trim();
    }

    // Normalize the "N/A" placeholder and blanks to null.
    private static string Clean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Equals(NotApplicable, StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    // Pinned by processor-scoped tests; the implementation lives in the shared parser.
    internal static string SanitizeXml(string xml) => EdgarXmlSubmissionParser.SanitizeXml(xml);

    internal static bool ParseYesNo(string value)
    {
        return value != null && value.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase);
    }

    internal static DateOnly? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateOnly.TryParseExact(
            value.Trim(),
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed
        )
            ? parsed
            : null;
    }
}
