using System.Globalization;
using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Helpers;
using static Equibles.Sec.HostedService.Helpers.EdgarXmlSubmissionParser;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Processes SEC Form 144 filings — an affiliate's notice of intent to sell restricted or
/// control securities — into structured <see cref="Form144Filing"/> records (plus the prior
/// three months of sales). Form 144 appears in the issuer's submissions feed, so each notice
/// is attributed to the issuer's stock. The XML uses the same <c>edgar/ownership</c> namespace
/// as Forms 3/4/5 but a different root (<c>edgarSubmission</c>) and flat (unwrapped) values.
/// </summary>
public class Form144FilingProcessor
    : IssuerFeedFilingProcessor<Form144Filing, Form144FilingRepository>
{
    private static readonly string[] DateFormats = ["MM/dd/yyyy", "M/d/yyyy"];

    public Form144FilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<Form144FilingProcessor> logger,
        ErrorReporter errorReporter
    )
        : base(scopeFactory, logger, errorReporter) { }

    public override bool CanProcess(DocumentType documentType)
    {
        return documentType == DocumentType.Form144;
    }

    protected override string FormLabel => "Form 144";

    protected override string ParseContext => "Form144.ParseXml";

    protected override string RequiredSection => "formData";

    protected override IQueryable<Form144Filing> GetByAccessionNumber(
        Form144FilingRepository repository,
        string accessionNumber
    ) => repository.GetByAccessionNumber(accessionNumber);

    protected override void LogImported(Form144Filing entity, string ticker, string accessionNumber)
    {
        Logger.LogInformation(
            "Imported Form 144 for {Ticker} ({Shares} shares, {Prior} prior sales) from {AccessionNumber}",
            ticker,
            entity.SharesToBeSold,
            entity.PriorSales.Count,
            accessionNumber
        );
    }

    protected override Form144Filing ParseFiling(XElement root, Guid companyId, FilingData filing)
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
            FilerCik = ParseFilerCik(root),
            PlanAdoptionDate = ParsePlanAdoptionDate(formData),
            SellerName = Truncate(
                Val(issuerInfo, "nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold"),
                512
            ),
            RelationshipToIssuer = Truncate(
                string.IsNullOrEmpty(relationship) ? null : relationship,
                256
            ),
            SecurityClassTitle = Truncate(Val(securities, "securitiesClassTitle"), 512),
            BrokerName = Truncate(Val(El(securities, "brokerOrMarketmakerDetails"), "name"), 256),
            SharesToBeSold = ParseLong(Val(securities, "noOfUnitsSold")),
            AggregateMarketValue = ParseDecimal(Val(securities, "aggregateMarketValue")),
            SharesOutstanding = ParseLong(Val(securities, "noOfUnitsOutstanding")),
            ApproxSaleDate = ParseDate(Val(securities, "approxSaleDate")),
            SecuritiesExchangeName = Truncate(Val(securities, "securitiesExchangeName"), 64),
            Remarks = Truncate(Val(formData, "remarks"), 2048),
        };

        foreach (var saleElement in Els(formData, "securitiesSoldInPast3Months"))
        {
            var priorSale = ParsePriorSale(saleElement);
            if (priorSale != null)
                entity.PriorSales.Add(priorSale);
        }

        return entity;
    }

    /// <summary>
    /// CIK of the natural person the notice is filed for, from
    /// <c>headerData/filerInfo/filer/filerCredentials/cik</c>. EDGAR indexes the notice under
    /// this CIK as well as the issuer's, and it is the same CIK that person's Forms 3/4/5 use,
    /// which is what makes a notice joinable to its executions.
    ///
    /// Kept verbatim (EDGAR zero-pads to ten characters) so it matches
    /// <c>InsiderOwner.OwnerCik</c>, which stores the Form 4 value the same way.
    /// </summary>
    private string ParseFilerCik(XElement root)
    {
        var filerInfo = El(El(root, "headerData"), "filerInfo");

        // A joint filing carries several <filer> elements. The first is the person the notice
        // is filed for; log the rest rather than guessing between them.
        var filers = Els(filerInfo, "filer").ToList();
        if (filers.Count == 0)
            return null;

        if (filers.Count > 1)
        {
            Logger.LogWarning(
                "Form 144 carries {Count} filer elements; taking the first CIK",
                filers.Count
            );
        }

        return Truncate(Val(El(filers[0], "filerCredentials"), "cik"), 16);
    }

    /// <summary>
    /// Adoption date of the Rule 10b5-1 plan the sale is made under, when the notice declares
    /// one, from <c>noticeSignature/planAdoptionDates/planAdoptionDate</c>. A notice may list
    /// more than one adoption date; the earliest is taken as the plan's origin.
    /// </summary>
    private static DateOnly? ParsePlanAdoptionDate(XElement formData)
    {
        var dates = Els(
                El(El(formData, "noticeSignature"), "planAdoptionDates"),
                "planAdoptionDate"
            )
            .Select(e => ParseDate(e.Value))
            .Where(d => d != null)
            .ToList();

        return dates.Count == 0 ? null : dates.Min();
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
            SellerName = Truncate(Val(El(element, "sellerDetails"), "name"), 512),
            SecurityClassTitle = Truncate(Val(element, "securitiesClassTitle"), 512),
            SaleDate = saleDate,
            AmountSold = amount,
            GrossProceeds = grossProceeds,
        };
    }

    // Pinned by processor-scoped tests; the implementation lives in the shared parser.
    internal static string SanitizeXml(string xml) => EdgarXmlSubmissionParser.SanitizeXml(xml);

    // Form 144 dates are US-format MM/dd/yyyy. Parse culture-independently so a non-Gregorian
    // host culture doesn't silently drop every date.
    internal static DateOnly? ParseDate(string value) =>
        EdgarXmlSubmissionParser.ParseDate(value, DateFormats);

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
