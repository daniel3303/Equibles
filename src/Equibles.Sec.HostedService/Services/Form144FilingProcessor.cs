using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Processes SEC Form 144 filings — an affiliate's notice of intent to sell restricted or
/// control securities — into structured <see cref="Form144Filing"/> records (plus the prior
/// three months of sales). Form 144 appears in the issuer's submissions feed, so each notice
/// is attributed to the issuer's stock. The XML uses the same <c>edgar/ownership</c> namespace
/// as Forms 3/4/5 but a different root (<c>edgarSubmission</c>) and flat (unwrapped) values.
/// </summary>
public class Form144FilingProcessor : IFilingProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Form144FilingProcessor> _logger;
    private readonly ErrorReporter _errorReporter;

    private static readonly string[] DateFormats = ["MM/dd/yyyy", "M/d/yyyy"];

    public Form144FilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<Form144FilingProcessor> logger,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public bool CanProcess(DocumentType documentType)
    {
        return documentType == DocumentType.Form144;
    }

    public async Task<bool> Process(FilingData filing, CommonStock companyOutContext)
    {
        // Capture IDs from the outer-scope entity to avoid leaking untracked entities into inner scope.
        var companyId = companyOutContext.Id;
        var companyTicker = companyOutContext.Ticker;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var repository = scope.ServiceProvider.GetRequiredService<Form144FilingRepository>();

        var existing = await repository.GetByAccessionNumber(filing.AccessionNumber).AnyAsync();
        if (existing)
            return false;

        var content = await secEdgarClient.GetDocumentContent(filing);
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning(
                "Empty content for {Ticker} Form 144 - {AccessionNumber}",
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
                "Form 144 XML missing formData for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return false;
        }

        repository.Add(entity);
        await repository.SaveChanges();

        _logger.LogInformation(
            "Imported Form 144 for {Ticker} ({Shares} shares, {Prior} prior sales) from {AccessionNumber}",
            companyTicker,
            entity.SharesToBeSold,
            entity.PriorSales.Count,
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

        // Form 144 has only been filed as XML since the SEC's April 2022 e-filing mandate,
        // so a submission without the <edgarSubmission> root is unexpected — skip quietly.
        if (!sanitized.Contains("<edgarSubmission", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Skipping non-XML Form 144 filing for {Ticker} - {AccessionNumber}",
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
                "Malformed Form 144 XML for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            await _errorReporter.Report(
                ErrorSource.DocumentScraper,
                "Form144.ParseXml",
                ex.Message,
                ex.StackTrace,
                $"ticker: {companyTicker}, accession: {filing.AccessionNumber}"
            );
            return null;
        }
    }

    private static Form144Filing ParseFiling(XElement root, Guid companyId, FilingData filing)
    {
        var formData = El(root, "formData");
        if (formData == null)
            return null;

        var issuerInfo = El(formData, "issuerInfo");
        var securities = El(formData, "securitiesInformation");

        var relationships = El(issuerInfo, "relationshipsToIssuer");
        var relationship = string.Join(
            ", ",
            Els(relationships, "relationshipToIssuer")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
        );

        var entity = new Form144Filing
        {
            CommonStockId = companyId,
            AccessionNumber = filing.AccessionNumber,
            FilingDate = filing.FilingDate,
            SellerName = Val(issuerInfo, "nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold"),
            RelationshipToIssuer = string.IsNullOrEmpty(relationship) ? null : relationship,
            SecurityClassTitle = Val(securities, "securitiesClassTitle"),
            BrokerName = Val(El(securities, "brokerOrMarketmakerDetails"), "name"),
            SharesToBeSold = ParseLong(Val(securities, "noOfUnitsSold")),
            AggregateMarketValue = ParseDecimal(Val(securities, "aggregateMarketValue")),
            SharesOutstanding = ParseLong(Val(securities, "noOfUnitsOutstanding")),
            ApproxSaleDate = ParseDate(Val(securities, "approxSaleDate")),
            SecuritiesExchangeName = Val(securities, "securitiesExchangeName"),
            Remarks = Val(formData, "remarks"),
        };

        foreach (var saleElement in Els(formData, "securitiesSoldInPast3Months"))
        {
            var priorSale = ParsePriorSale(saleElement);
            if (priorSale != null)
                entity.PriorSales.Add(priorSale);
        }

        return entity;
    }

    private static Form144PriorSale ParsePriorSale(XElement element)
    {
        var saleDate = ParseDate(Val(element, "saleDate"));
        var amount = ParseLong(Val(element, "amountOfSecuritiesSold"));
        var grossProceeds = ParseDecimal(Val(element, "grossProceeds"));

        // When the filer reports nothing to disclose, the element can still be emitted as an
        // empty shell — skip rows that carry no sale data so they don't pollute the table.
        if (saleDate == null && amount == 0 && grossProceeds == 0)
            return null;

        return new Form144PriorSale
        {
            SellerName = Val(El(element, "sellerDetails"), "name"),
            SecurityClassTitle = Val(element, "securitiesClassTitle"),
            SaleDate = saleDate,
            AmountSold = amount,
            GrossProceeds = grossProceeds,
        };
    }

    // ── XML helpers (namespace-agnostic: Form 144 declares a default xmlns) ──────────

    private static XElement El(XElement parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Els(XElement parent, string name) =>
        parent?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    private static string Val(XElement parent, string name)
    {
        var value = El(parent, name)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
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

    // Form 144 dates are US-format MM/dd/yyyy. Parse culture-independently so a non-Gregorian
    // host culture doesn't silently drop every date.
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

    internal static long ParseLong(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;
        var d = ParseDecimal(value);
        return d > long.MaxValue || d < long.MinValue ? 0 : (long)d;
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
