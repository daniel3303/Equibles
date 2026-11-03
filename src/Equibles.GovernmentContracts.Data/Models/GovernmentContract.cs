using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.GovernmentContracts.Data.Models;

/// <summary>
/// A federal procurement contract award, from USAspending.gov for the United States or the
/// Government of Canada's proactive publication of contracts, resolved to a public company in
/// our <see cref="Issuer"/> universe. Awards that do not resolve to a listed company are not persisted.
/// </summary>
[Index(nameof(AwardUniqueKey), IsUnique = true)]
[Index(nameof(EquityIssuerId), nameof(ActionDate))]
[Index(nameof(ActionDate))]
[Index(nameof(CountryCode), nameof(ActionDate))]
public class GovernmentContract
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquityIssuerId { get; set; }

    /// <summary>
    /// The government that awarded the contract, as an ISO country code: US or CA. It also
    /// sets the currency of <see cref="Amount"/>, so readers sum and rank one country at a time.
    /// </summary>
    [Required]
    [MaxLength(2)]
    public string CountryCode { get; set; } = "US";
    public virtual EquityIssuer Issuer { get; set; }

    /// <summary>
    /// USAspending's globally-unique award identifier (the award-detail slug,
    /// <c>generated_internal_id</c>). Used as the idempotency key so re-scraping
    /// updates the existing row rather than inserting a duplicate.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string AwardUniqueKey { get; set; }

    /// <summary>Human-readable contract/PIID award number.</summary>
    [MaxLength(128)]
    public string AwardId { get; set; }

    [Required]
    [MaxLength(512)]
    public string RecipientName { get; set; }

    /// <summary>USAspending recipient hash (level-qualified), for cross-reference.</summary>
    [MaxLength(64)]
    public string RecipientId { get; set; }

    public GovernmentContractAwardType AwardType { get; set; }

    [MaxLength(256)]
    public string AwardingAgency { get; set; }

    /// <summary>
    /// Total award value in the awarding country's currency: US dollars (current obligated +
    /// potential) for US awards, Canadian dollars (the contract value including amendments) for CA.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Total outlays disbursed against the award to date, in US dollars.</summary>
    public decimal? TotalOutlays { get; set; }

    /// <summary>
    /// The award's action date — USAspending's base obligation date, i.e. the date the base
    /// award was signed/first obligated. Drives the incremental import cursor, so it must
    /// never hold a future date (the period-of-performance start, which can sit years in
    /// the future, once poisoned this column and froze ingestion).
    /// </summary>
    public DateOnly? ActionDate { get; set; }

    /// <summary>The period-of-performance current end date.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>USAspending's last-modified date for the award, for cross-reference.</summary>
    public DateOnly? LastModifiedDate { get; set; }

    [MaxLength(8)]
    public string NaicsCode { get; set; }

    [MaxLength(8)]
    public string PscCode { get; set; }

    // Award descriptions are free-form and occasionally long; left untyped (text)
    // so a long value never overflows and silently discards the save.
    public string Description { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
