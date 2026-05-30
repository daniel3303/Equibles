using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

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
public class NportFilingProcessor : IFilingProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NportFilingProcessor> _logger;
    private readonly ErrorReporter _errorReporter;

    // NPORT dates are ISO yyyy-MM-dd.
    private static readonly string[] DateFormats = ["yyyy-MM-dd"];

    // Optional identifiers the filer leaves blank are reported as the literal "N/A"; treat as absent.
    private const string NotApplicable = "N/A";

    public NportFilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<NportFilingProcessor> logger,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public bool CanProcess(DocumentType documentType)
    {
        return documentType == DocumentType.NportP || documentType == DocumentType.NportPa;
    }

    public async Task<bool> Process(FilingData filing, CommonStock companyOutContext)
    {
        var companyId = companyOutContext.Id;
        var companyTicker = companyOutContext.Ticker;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var repository = scope.ServiceProvider.GetRequiredService<NportFilingRepository>();

        var existing = await repository.GetByAccessionNumber(filing.AccessionNumber).AnyAsync();
        if (existing)
            return false;

        var content = await secEdgarClient.GetDocumentContent(filing);
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning(
                "Empty content for {Ticker} NPORT-P - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return false;
        }

        var root = await TryParseSubmission(content, filing, companyTicker);
        if (root == null)
            return false;

        var entity = ParseFiling(root, companyId, filing);
        if (entity == null)
        {
            _logger.LogWarning(
                "NPORT-P XML missing genInfo for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return false;
        }

        repository.Add(entity);
        await repository.SaveChanges();

        _logger.LogInformation(
            "Imported NPORT-P for {Ticker} ({Series}, net assets {NetAssets}, {Holdings} holdings) from {AccessionNumber}",
            companyTicker,
            entity.SeriesName,
            entity.NetAssets,
            entity.Holdings.Count,
            filing.AccessionNumber
        );

        return true;
    }

    private async Task<XElement> TryParseSubmission(
        string content,
        FilingData filing,
        string companyTicker
    )
    {
        var sanitized = SanitizeXml(content);

        // NPORT-P has only ever been filed as XML, so a submission without the
        // <edgarSubmission> root is unexpected — skip quietly.
        if (!sanitized.Contains("<edgarSubmission", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Skipping non-XML NPORT-P filing for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return null;
        }

        try
        {
            return XDocument.Parse(sanitized).Root;
        }
        catch (System.Xml.XmlException ex)
        {
            _logger.LogWarning(
                ex,
                "Malformed NPORT-P XML for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            await _errorReporter.Report(
                ErrorSource.DocumentScraper,
                "Nport.ParseXml",
                ex.Message,
                ex.StackTrace,
                $"ticker: {companyTicker}, accession: {filing.AccessionNumber}"
            );
            return null;
        }
    }

    private static NportFiling ParseFiling(XElement root, Guid companyId, FilingData filing)
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
            RegistrantName = Clean(Val(genInfo, "regName")),
            SeriesName = Clean(Val(genInfo, "seriesName")),
            SeriesId = Clean(Val(genInfo, "seriesId")),
            SeriesLei = Clean(Val(genInfo, "seriesLei")),
            ReportPeriodDate = ParseDate(Val(genInfo, "repPdDate")) ?? filing.ReportDate,
            ReportPeriodEnd = ParseDate(Val(genInfo, "repPdEnd")) ?? filing.ReportDate,
            TotalAssets = ParseDecimal(Val(fundInfo, "totAssets")),
            TotalLiabilities = ParseDecimal(Val(fundInfo, "totLiabs")),
            NetAssets = ParseDecimal(Val(fundInfo, "netAssets")),
            IsFinalFiling = ParseYesNo(Val(genInfo, "isFinalFiling")),
        };

        foreach (var investment in Els(El(fundInfo, "invstOrSecs"), "invstOrSec"))
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
            Name = name,
            Title = Clean(Val(element, "title")),
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

        return filing.Form?.Contains("/A", StringComparison.OrdinalIgnoreCase) == true;
    }

    // ── XML helpers (navigate by local name; NPORT declares the nport namespace) ──────

    private static XElement El(XElement parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Els(XElement parent, string name) =>
        parent?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    private static string Val(XElement parent, string name)
    {
        var value = El(parent, name)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

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

    private const string XmlEnvelopeStart = "<XML>";
    private const string XmlEnvelopeEnd = "</XML>";

    internal static string SanitizeXml(string xml)
    {
        // SEC filings wrap the actual XML inside an SGML envelope (one .txt holds the whole
        // submission); pull out the <XML>...</XML> body before parsing.
        var xmlStart = xml.IndexOf(XmlEnvelopeStart, StringComparison.OrdinalIgnoreCase);
        var xmlEnd = xml.IndexOf(XmlEnvelopeEnd, StringComparison.OrdinalIgnoreCase);
        if (xmlStart >= 0 && xmlEnd > xmlStart)
        {
            xml = xml[(xmlStart + XmlEnvelopeStart.Length)..xmlEnd].Trim();
        }

        // Escape stray ampersands that aren't already part of an entity.
        return Regex.Replace(xml, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)", "&amp;");
    }

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
