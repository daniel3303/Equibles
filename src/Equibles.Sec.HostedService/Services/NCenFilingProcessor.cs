using System.Globalization;
using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Helpers;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
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
public class NCenFilingProcessor : IFilingProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NCenFilingProcessor> _logger;
    private readonly ErrorReporter _errorReporter;

    // N-CEN dates are ISO yyyy-MM-dd.
    private static readonly string[] DateFormats = ["yyyy-MM-dd"];

    // Optional fields the filer leaves blank are reported as the literal "N/A"; treat as absent.
    private const string NotApplicable = "N/A";

    public NCenFilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<NCenFilingProcessor> logger,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public bool CanProcess(DocumentType documentType)
    {
        return documentType == DocumentType.NCen || documentType == DocumentType.NCenA;
    }

    public async Task<bool> Process(FilingData filing, CommonStock companyOutContext)
    {
        var companyId = companyOutContext.Id;
        var companyTicker = companyOutContext.Ticker;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var repository = scope.ServiceProvider.GetRequiredService<NCenFilingRepository>();

        var existing = await repository.GetByAccessionNumber(filing.AccessionNumber).AnyAsync();
        if (existing)
            return false;

        var content = await secEdgarClient.GetDocumentContent(filing);
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning(
                "Empty content for {Ticker} N-CEN - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return false;
        }

        var root = await EdgarXmlSubmissionParser.TryParseSubmission(
            content,
            filing,
            companyTicker,
            "N-CEN",
            "NCen.ParseXml",
            _logger,
            _errorReporter
        );
        if (root == null)
            return false;

        var entity = ParseFiling(root, companyId, filing);
        if (entity == null)
        {
            _logger.LogWarning(
                "N-CEN XML missing registrantInfo for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return false;
        }

        repository.Add(entity);
        await repository.SaveChanges();

        _logger.LogInformation(
            "Imported N-CEN for {Ticker} ({Registrant}, type {Type}, {Providers} service providers) from {AccessionNumber}",
            companyTicker,
            entity.RegistrantName,
            entity.InvestmentCompanyType,
            entity.ServiceProviders.Count,
            filing.AccessionNumber
        );

        return true;
    }

    private static NCenFiling ParseFiling(XElement root, Guid companyId, FilingData filing)
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
        foreach (var accountant in Els(El(registrant, "publicAccountants"), "publicAccountant"))
        {
            providers.Add(
                MakeProvider(
                    NCenServiceProviderType.PublicAccountant,
                    Val(accountant, "publicAccountantName"),
                    Attr(El(accountant, "publicAccountantStateCountry"), "publicAccountantCountry"),
                    affiliated: false
                )
            );
        }

        foreach (
            var underwriter in Els(El(registrant, "principalUnderwriters"), "principalUnderwriter")
        )
        {
            providers.Add(
                MakeProvider(
                    NCenServiceProviderType.PrincipalUnderwriter,
                    Val(underwriter, "principalUnderwriterName"),
                    Val(underwriter, "principalUnderWriterCountry"),
                    ParseYesNo(Val(underwriter, "isPrincipalUnderwriterAffiliatedWithRegistrant"))
                )
            );
        }

        // Per-series (management investment company) providers. A multi-series fund repeats the
        // same firms across series, so the list is de-duplicated by role + name below.
        var seriesInfo = El(formData, "managementInvestmentQuestionSeriesInfo");
        foreach (var question in Els(seriesInfo, "managementInvestmentQuestion"))
        {
            foreach (var adviser in Els(El(question, "investmentAdvisers"), "investmentAdviser"))
            {
                providers.Add(
                    MakeProvider(
                        NCenServiceProviderType.InvestmentAdviser,
                        Val(adviser, "investmentAdviserName"),
                        Val(adviser, "investmentAdviserCountry"),
                        affiliated: false
                    )
                );
            }

            foreach (var subAdviser in Els(El(question, "subAdvisers"), "subAdviser"))
            {
                providers.Add(
                    MakeProvider(
                        NCenServiceProviderType.SubAdviser,
                        Val(subAdviser, "subAdviserName"),
                        Val(subAdviser, "subAdviserCountry"),
                        ParseYesNo(Val(subAdviser, "isSubAdviserAffiliated"))
                    )
                );
            }

            foreach (var custodian in Els(El(question, "custodians"), "custodian"))
            {
                providers.Add(
                    MakeProvider(
                        NCenServiceProviderType.Custodian,
                        Val(custodian, "custodianName"),
                        Val(custodian, "custodianCountry"),
                        ParseYesNo(Val(custodian, "isCustodianAffiliated"))
                    )
                );
            }

            foreach (var agent in Els(El(question, "transferAgents"), "transferAgent"))
            {
                providers.Add(
                    MakeProvider(
                        NCenServiceProviderType.TransferAgent,
                        Val(agent, "transferAgentName"),
                        Attr(El(agent, "transferAgentStateCountry"), "transferAgentCountry"),
                        ParseYesNo(Val(agent, "isTransferAgentAffiliated"))
                    )
                );
            }

            foreach (var admin in Els(El(question, "admins"), "admin"))
            {
                providers.Add(
                    MakeProvider(
                        NCenServiceProviderType.Administrator,
                        Val(admin, "adminName"),
                        Attr(El(admin, "adminStateCountry"), "adminCountry"),
                        ParseYesNo(Val(admin, "isAdminAffiliated"))
                    )
                );
            }

            foreach (var pricing in Els(El(question, "pricingServices"), "pricingService"))
            {
                providers.Add(
                    MakeProvider(
                        NCenServiceProviderType.PricingService,
                        Val(pricing, "pricingServiceName"),
                        Val(pricing, "pricingServiceCountry"),
                        ParseYesNo(Val(pricing, "isPricingServiceAffiliated"))
                    )
                );
            }

            foreach (
                var shareholder in Els(
                    El(question, "shareholderServicingAgents"),
                    "shareholderServicingAgent"
                )
            )
            {
                providers.Add(
                    MakeProvider(
                        NCenServiceProviderType.ShareholderServicingAgent,
                        Val(shareholder, "shareholderServiceAgentName"),
                        Attr(
                            El(shareholder, "shareholderServiceAgentStateCountry"),
                            "shareholderServiceAgentCountry"
                        ),
                        ParseYesNo(Val(shareholder, "isShareholderServiceAgentAffiliated"))
                    )
                );
            }
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
