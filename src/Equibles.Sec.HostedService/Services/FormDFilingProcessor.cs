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
/// Processes SEC Form D filings — an issuer's notice of an exempt (Regulation D) securities
/// offering, i.e. a private placement — into structured <see cref="FormDFiling"/> records (plus
/// the executives, directors and promoters named on the filing). Form D appears in the issuer's
/// submissions feed, so each notice is attributed to the issuer's stock. Both the original
/// notice ("D") and its amendments ("D/A") are processed. The XML root is
/// <c>edgarSubmission</c> with no default namespace, so elements are navigated by local name.
/// </summary>
public class FormDFilingProcessor : IssuerFeedFilingProcessor<FormDFiling, FormDFilingRepository>
{
    // Form D dates are ISO yyyy-MM-dd; accept the US MM/dd/yyyy fallback defensively.
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy"];

    private const string IndefiniteValue = "Indefinite";

    public FormDFilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<FormDFilingProcessor> logger,
        ErrorReporter errorReporter
    )
        : base(scopeFactory, logger, errorReporter) { }

    public override bool CanProcess(DocumentType documentType)
    {
        return documentType == DocumentType.FormD || documentType == DocumentType.FormDa;
    }

    protected override string FormLabel => "Form D";

    protected override string ParseContext => "FormD.ParseXml";

    protected override string RequiredSection => "offeringData";

    protected override IQueryable<FormDFiling> GetByAccessionNumber(
        FormDFilingRepository repository,
        string accessionNumber
    ) => repository.GetByAccessionNumber(accessionNumber);

    protected override void LogImported(FormDFiling entity, string ticker, string accessionNumber)
    {
        Logger.LogInformation(
            "Imported Form D for {Ticker} ({EntityName}, sold {Sold}, {Persons} related persons) from {AccessionNumber}",
            ticker,
            entity.EntityName,
            entity.TotalAmountSold,
            entity.RelatedPersons.Count,
            accessionNumber
        );
    }

    protected override FormDFiling ParseFiling(XElement root, Guid companyId, FilingData filing)
    {
        var offeringData = El(root, "offeringData");
        if (offeringData == null)
            return null;

        var issuer = El(root, "primaryIssuer");
        var typeOfFiling = El(offeringData, "typeOfFiling");
        var salesAmounts = El(offeringData, "offeringSalesAmounts");
        var investors = El(offeringData, "investors");

        var (offeringAmount, offeringIndefinite) = ParseAmount(
            Val(salesAmounts, "totalOfferingAmount")
        );
        var (remaining, remainingIndefinite) = ParseAmount(Val(salesAmounts, "totalRemaining"));

        var exemptions = string.Join(
            ", ",
            Els(El(offeringData, "federalExemptionsExclusions"), "item")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
        );

        var entity = new FormDFiling
        {
            CommonStockId = companyId,
            AccessionNumber = filing.AccessionNumber,
            FilingDate = filing.FilingDate,
            IsAmendment = ParseIsAmendment(typeOfFiling, filing),
            EntityName = Truncate(Val(issuer, "entityName"), 512),
            EntityType = Truncate(Val(issuer, "entityType"), 128),
            JurisdictionOfInc = Truncate(Val(issuer, "jurisdictionOfInc"), 128),
            YearOfIncorporation = ParseNullableInt(Val(El(issuer, "yearOfInc"), "value")),
            IndustryGroup = Truncate(
                Val(El(offeringData, "industryGroup"), "industryGroupType"),
                128
            ),
            FederalExemptions = Truncate(string.IsNullOrEmpty(exemptions) ? null : exemptions, 256),
            DateOfFirstSale = ParseDate(Val(El(typeOfFiling, "dateOfFirstSale"), "value")),
            TotalOfferingAmount = offeringAmount,
            IsOfferingAmountIndefinite = offeringIndefinite,
            TotalAmountSold = ParseLong(Val(salesAmounts, "totalAmountSold")),
            TotalRemaining = remaining,
            IsRemainingIndefinite = remainingIndefinite,
            MinimumInvestmentAccepted = ParseLong(Val(offeringData, "minimumInvestmentAccepted")),
            HasNonAccreditedInvestors = ParseBool(Val(investors, "hasNonAccreditedInvestors")),
            // The XML is filer-entered, so an out-of-range count (a typo, or a figure pasted
            // into the wrong field) must not wrap negative when narrowed to the int column.
            // Clamp to a non-negative, representable value before casting.
            TotalNumberAlreadyInvested = (int)
                Math.Clamp(
                    ParseLong(Val(investors, "totalNumberAlreadyInvested")),
                    0,
                    int.MaxValue
                ),
        };

        foreach (var personElement in Els(El(root, "relatedPersonsList"), "relatedPersonInfo"))
        {
            var person = ParseRelatedPerson(personElement);
            if (person != null)
                entity.RelatedPersons.Add(person);
        }

        return entity;
    }

    private static FormDRelatedPerson ParseRelatedPerson(XElement element)
    {
        var name = El(element, "relatedPersonName");
        var fullName = string.Join(
            " ",
            new[] { Val(name, "firstName"), Val(name, "middleName"), Val(name, "lastName") }.Where(
                v => !string.IsNullOrEmpty(v)
            )
        );

        var relationships = string.Join(
            ", ",
            Els(El(element, "relatedPersonRelationshipList"), "relationship")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
        );

        if (string.IsNullOrEmpty(fullName) && string.IsNullOrEmpty(relationships))
            return null;

        return new FormDRelatedPerson
        {
            Name = Truncate(string.IsNullOrEmpty(fullName) ? null : fullName, 512),
            Relationships = Truncate(
                string.IsNullOrEmpty(relationships) ? null : relationships,
                256
            ),
        };
    }

    private static bool ParseIsAmendment(XElement typeOfFiling, FilingData filing)
    {
        var flag = Val(El(typeOfFiling, "newOrAmendment"), "isAmendment");
        if (flag != null)
            return ParseBool(flag);

        // Fall back to the submission form string ("D/A") when the flag is absent.
        return filing.IsAmendmentForm();
    }

    // Pinned by processor-scoped tests; the implementation lives in the shared parser.
    internal static string SanitizeXml(string xml) => EdgarXmlSubmissionParser.SanitizeXml(xml);

    // Returns (amount, isIndefinite). Form D reports offering amounts either as a dollar figure
    // or the literal "Indefinite"; the latter is stored as null and flagged.
    internal static (long?, bool) ParseAmount(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, false);
        if (value.Trim().Equals(IndefiniteValue, StringComparison.OrdinalIgnoreCase))
            return (null, true);
        return (ParseLong(value), false);
    }

    internal static DateOnly? ParseDate(string value) =>
        EdgarXmlSubmissionParser.ParseDate(value, DateFormats);

    internal static bool ParseBool(string value)
    {
        return bool.TryParse(value, out var result) && result;
    }

    internal static int? ParseNullableInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
            ? r
            : null;
    }

    internal static long ParseLong(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        if (long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;
        return 0;
    }
}
