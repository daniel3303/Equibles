using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A SEC Form D notice — an issuer's report of an exempt (Regulation D) securities offering,
/// i.e. a private placement. Form D appears in the issuer's EDGAR submissions feed, so each
/// notice is attributed to the issuer's <see cref="CommonStock"/>. Offering amounts can be
/// reported as the literal "Indefinite" rather than a number; those are stored as <c>null</c>
/// and flagged via <see cref="IsOfferingAmountIndefinite"/> / <see cref="IsRemainingIndefinite"/>.
/// </summary>
[Index(nameof(CommonStockId), nameof(FilingDate))]
[Index(nameof(AccessionNumber), IsUnique = true)]
[Index(nameof(FilingDate))]
public class FormDFiling : IStockFiling
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommonStockId { get; set; }
    public virtual CommonStock CommonStock { get; set; }

    [MaxLength(32)]
    public string AccessionNumber { get; set; }

    public DateOnly FilingDate { get; set; }

    /// <summary>
    /// True when the submission type is "D/A" (an amendment to a prior Form D) rather than "D".
    /// </summary>
    public bool IsAmendment { get; set; }

    /// <summary>The issuer's legal name exactly as reported on the filing.</summary>
    [MaxLength(512)]
    public string EntityName { get; set; }

    /// <summary>Issuer entity type, e.g. "Limited Liability Company", "Corporation".</summary>
    [MaxLength(128)]
    public string EntityType { get; set; }

    /// <summary>State or country of incorporation/organization, e.g. "DELAWARE".</summary>
    [MaxLength(128)]
    public string JurisdictionOfInc { get; set; }

    /// <summary>Year of incorporation when disclosed; null when the issuer declined or is yet to be formed.</summary>
    public int? YearOfIncorporation { get; set; }

    /// <summary>Reported industry group, e.g. "Pooled Investment Fund", "Technology".</summary>
    [MaxLength(128)]
    public string IndustryGroup { get; set; }

    /// <summary>
    /// Claimed Regulation D exemptions/exclusions, joined with ", " (e.g. "06b, 3C, 3C.7").
    /// </summary>
    [MaxLength(256)]
    public string FederalExemptions { get; set; }

    /// <summary>Date of first sale when reported; null when the issuer marked it as yet to occur.</summary>
    public DateOnly? DateOfFirstSale { get; set; }

    /// <summary>Total dollar amount of the offering; null when reported as "Indefinite".</summary>
    public long? TotalOfferingAmount { get; set; }

    /// <summary>True when the issuer reported the total offering amount as "Indefinite".</summary>
    public bool IsOfferingAmountIndefinite { get; set; }

    /// <summary>Dollar amount sold to date.</summary>
    public long TotalAmountSold { get; set; }

    /// <summary>Dollar amount still being offered; null when reported as "Indefinite".</summary>
    public long? TotalRemaining { get; set; }

    /// <summary>True when the issuer reported the remaining amount as "Indefinite".</summary>
    public bool IsRemainingIndefinite { get; set; }

    /// <summary>Minimum investment the issuer will accept, in dollars.</summary>
    public long MinimumInvestmentAccepted { get; set; }

    /// <summary>Whether any non-accredited investors have been sold securities in the offering.</summary>
    public bool HasNonAccreditedInvestors { get; set; }

    /// <summary>Total number of investors who have already invested in the offering.</summary>
    public int TotalNumberAlreadyInvested { get; set; }

    /// <summary>
    /// Executives, directors and promoters named on the filing — the people behind the offering.
    /// </summary>
    public virtual List<FormDRelatedPerson> RelatedPersons { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
