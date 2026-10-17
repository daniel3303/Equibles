using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// One reporting person of a Schedule 13D or 13G. Members of a group often report the same shares
/// at different levels of the ownership chain, so persons are never additive.
/// </summary>
[Index(nameof(BeneficialOwnershipFilingId), nameof(Ordinal), IsUnique = true)]
[Index(nameof(Cik))]
public class BeneficialOwnershipReportingPerson
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BeneficialOwnershipFilingId { get; set; }
    public virtual BeneficialOwnershipFiling Filing { get; set; }

    /// <summary>The person's position among the filing's cover pages, from 0.</summary>
    public int Ordinal { get; set; }

    /// <summary>The person's CIK without leading zeros; a 13G names persons without one.</summary>
    [MaxLength(16)]
    public string Cik { get; set; }

    [MaxLength(512)]
    public string Name { get; set; }

    public ReportingPersonTypes Types { get; set; }

    /// <summary>Type codes SEC's list does not define, as filed.</summary>
    [MaxLength(64)]
    public string UnrecognizedTypeCodes { get; set; }

    /// <summary>The state or country code of citizenship or organization, as filed.</summary>
    [MaxLength(64)]
    public string CitizenshipOrOrganization { get; set; }

    public long SoleVotingPower { get; set; }
    public long SharedVotingPower { get; set; }
    public long SoleDispositivePower { get; set; }
    public long SharedDispositivePower { get; set; }
    public long AggregateAmountOwned { get; set; }

    [Column(TypeName = "numeric(7,4)")]
    public decimal? PercentOfClass { get; set; }

    /// <summary>The person whose position the holdings rows carry for this filing.</summary>
    public bool IsLead { get; set; }
}
